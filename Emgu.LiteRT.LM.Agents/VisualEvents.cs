// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Emgu.LiteRT.LM.Agents.Events
{
    /// <summary>Notification phase within a confirmed presence episode.</summary>
#if NET8_0_OR_GREATER
    [JsonConverter(typeof(JsonStringEnumConverter<VisualEventPhase>))]
#else
    [JsonConverter(typeof(JsonStringEnumConverter))]
#endif
    public enum VisualEventPhase
    {
        /// <summary>Presence was confirmed after the entry debounce.</summary>
        Started,
        /// <summary>A meaningful value changed during the same episode.</summary>
        Updated,
        /// <summary>Absence was confirmed after the exit grace period.</summary>
        Ended
    }
    /// <summary>Lifecycle state of a rule, independent of object presence.</summary>
#if NET8_0_OR_GREATER
    [JsonConverter(typeof(JsonStringEnumConverter<VisualMonitorState>))]
#else
    [JsonConverter(typeof(JsonStringEnumConverter))]
#endif
    public enum VisualMonitorState
    {
        /// <summary>Waiting for model preparation and the first successful analysis.</summary>
        Preparing,
        /// <summary>Analyzing a visible feed.</summary>
        Watching,
        /// <summary>Suspended by the user or because the feed is not visible.</summary>
        Paused,
        /// <summary>Removed from the session.</summary>
        Stopped,
        /// <summary>Detector failed; explicit resume retries preparation.</summary>
        Faulted
    }

    /// <summary>A reusable predicate over typed observations.</summary>
    public interface IVisualCondition
    {
        /// <summary>Stable configuration key used for duplicate-rule suppression.</summary>
        string Key { get; }
        /// <summary>Human-readable condition shown beside the live feed.</summary>
        string Description { get; }
        /// <summary>Counts matching observations, or reports their count for an event.</summary>
        int Count(VisualAnalysis analysis);
        /// <summary>Tests whether a count satisfies this condition.</summary>
        bool Matches(int count);
    }

    /// <summary>Requires a minimum count of a canonical object class above a confidence threshold.</summary>
    public sealed class ObjectCountCondition : IVisualCondition
    {
        /// <summary>Canonical semantic class, independent of detector-specific class indices.</summary>
        public string Label { get; }
        /// <summary>Minimum simultaneous matching objects.</summary>
        public int MinimumCount { get; }
        /// <summary>Detector score from zero to one; scores are model-specific.</summary>
        public double Confidence { get; }
        /// <summary>Stable configuration key used for duplicate-rule suppression.</summary>
        public string Key => FormattableString.Invariant($"objects:{Label}:{MinimumCount}:{Confidence:R}");
        /// <summary>Human-readable condition shown beside the live feed.</summary>
        public string Description => $"At least {MinimumCount} {Label} (confidence ≥ {Confidence:0.00})";
        /// <summary>Condition requiring a minimum number of objects above a confidence threshold.</summary>
        public ObjectCountCondition(string label, int minimumCount = 1, double confidence = 0.5)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("An object class is required.");
            if (minimumCount < 1 || minimumCount > 100) throw new ArgumentOutOfRangeException(nameof(minimumCount));
            if (double.IsNaN(confidence) || confidence < 0 || confidence > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
            Label = label.Trim().ToLowerInvariant(); MinimumCount = minimumCount; Confidence = confidence;
        }
        /// <summary>Counts matching observations, or reports their count for an event.</summary>
        public int Count(VisualAnalysis analysis) => analysis.Observations.Count(o => o.Kind == "object" &&
            o.Label == Label && o.Confidence >= Confidence);
        /// <summary>Tests whether a count satisfies this condition.</summary>
        public bool Matches(int count) => count >= MinimumCount;
    }

    /// <summary>A state transition backed by a successful image analysis; contains no pixels.</summary>
    public sealed class VisualEvent
    {
        /// <summary>Session rule that emitted this event.</summary>
        public Guid MonitorId { get; }
        /// <summary>Identifier shared by Started, Updated, and Ended for one presence episode.</summary>
        public Guid EpisodeId { get; }
        /// <summary>Change in the confirmed presence condition.</summary>
        public VisualEventPhase Phase { get; }
        /// <summary>Counts matching observations, or reports their count for an event.</summary>
        public int Count { get; }
        /// <summary>Typed observations and capture metadata supporting this event.</summary>
        public VisualAnalysis Evidence { get; }
        /// <summary>A state transition backed by a successful image analysis; contains no pixels.</summary>
        internal VisualEvent(Guid monitorId, Guid episodeId, VisualEventPhase phase, int count, VisualAnalysis evidence)
        { MonitorId = monitorId; EpisodeId = episodeId; Phase = phase; Count = count; Evidence = evidence; }
    }

    /// <summary>Platform action, executed away from capture/inference. Honor cancellation before side effects.</summary>
    public interface IVisualAction
    {
        /// <summary>Stable identifier used by the registry or session monitor controls.</summary>
        string Id { get; }
        /// <summary>Performs the action once; check cancellation before producing a side effect.</summary>
        Task ExecuteAsync(VisualEvent occurrence, CancellationToken cancellationToken);
    }

    /// <summary>Pure temporal state machine. Advance with successful analyses, using their monotonic capture times.</summary>
    public sealed class VisualPresenceMonitor
    {
        /// <summary>Stable identifier used by the registry or session monitor controls.</summary>
        public Guid Id { get; } = Guid.NewGuid();
        /// <summary>Predicate or human-readable condition associated with this monitor.</summary>
        public IVisualCondition Condition { get; }
        /// <summary>Whether presence has been confirmed in the current episode.</summary>
        public bool IsPresent { get; private set; }
        private Guid _episode;
        private TimeSpan? _last, _positive, _negative;
        private int _negativeSamples, _lastCount;
        /// <summary>Creates a temporal monitor for the specified observation predicate.</summary>
        public VisualPresenceMonitor(IVisualCondition condition) => Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        /// <summary>Discards episode and confirmation state without claiming absence.</summary>
        public void Reset()
        {
            IsPresent = false; _last = _positive = _negative = null;
            _negativeSamples = _lastCount = 0; _episode = Guid.Empty;
        }
        /// <summary>Processes a successful analysis, returning a meaningful transition or null. Duplicate and out-of-order times are ignored.</summary>
        public VisualEvent Advance(VisualAnalysis analysis)
        {
            var now = analysis.Timestamp;
            if (_last.HasValue && now <= _last.Value) return null; // Ignore duplicate/out-of-order results.
            if (_last.HasValue && now - _last.Value > TimeSpan.FromSeconds(2)) Reset();
            _last = now;
            int count = Condition.Count(analysis);
            if (Condition.Matches(count))
            {
                _negative = null; _negativeSamples = 0;
                if (!IsPresent)
                {
                    if (!_positive.HasValue || now - _positive.Value > TimeSpan.FromSeconds(1)) { _positive = now; return null; }
                    IsPresent = true; _episode = Guid.NewGuid(); _lastCount = count;
                    return new VisualEvent(Id, _episode, VisualEventPhase.Started, count, analysis);
                }
                if (count != _lastCount)
                {
                    _lastCount = count;
                    return new VisualEvent(Id, _episode, VisualEventPhase.Updated, count, analysis);
                }
            }
            else
            {
                _positive = null;
                if (!IsPresent) return null;
                if (!_negative.HasValue) { _negative = now; _negativeSamples = 0; }
                _negativeSamples++;
                if (_negativeSamples >= 2 && now - _negative.Value >= TimeSpan.FromSeconds(1.5))
                {
                    var ended = new VisualEvent(Id, _episode, VisualEventPhase.Ended, count, analysis);
                    Reset(); _last = now;
                    return ended;
                }
            }
            return null;
        }
    }
}
