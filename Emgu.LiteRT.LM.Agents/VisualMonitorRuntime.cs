// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Emgu.LiteRT.LM.Agents.Events
{
    /// <summary>A thread-safe, immutable view of one session-only rule.</summary>
    public sealed class VisualMonitorInfo
    {
        /// <summary>Stable identifier used by the registry or session monitor controls.</summary>
        public Guid Id { get; internal set; }
        /// <summary>Registered identifier of the agent producing observations.</summary>
        public string AgentId { get; internal set; }
        /// <summary>Predicate or human-readable condition associated with this monitor.</summary>
        public string Condition { get; internal set; }
        /// <summary>Registered action to dispatch on the selected event phase.</summary>
        public string ActionId { get; internal set; }
        /// <summary>Current preparation, visibility, or execution state.</summary>
        public VisualMonitorState State { get; internal set; }
        /// <summary>Whether the user has enabled this rule; hiding the camera does not change this preference.</summary>
        public bool Enabled { get; internal set; }
        /// <summary>Latest readiness, pause, or detector error message.</summary>
        public string Status { get; internal set; }
        /// <summary>Last event summary retained in memory, or null.</summary>
        public string LastEvent { get; internal set; }
        /// <summary>Last action result retained in memory, or null.</summary>
        public string LastAction { get; internal set; }
    }

    /// <summary>
    /// Session-only orchestration: bounded latest-frame input, serialized model access, and a separate action worker.
    /// Configure the registry before use. Call SetFeedVisible(false) synchronously before hiding/releasing the source.
    /// </summary>
    public sealed class VisualMonitorRuntime
    {
        private sealed class Entry
        {
            public VisualPresenceMonitor Monitor;
            public IVisualAgent Agent;
            public IVisualAction Action;
            public VisualEventPhase Phase;
            public bool Enabled = true;
            public long Revision;
            public long AfterSequence;
            public CancellationTokenSource Stop = new CancellationTokenSource();
            public VisualMonitorState State;
            public string Status, LastEvent, LastAction;
        }
        private sealed class Dispatch
        {
            public Entry Entry;
            public long Revision, Generation;
            public VisualEvent Event;
        }
        private readonly object _gate = new object();
        private readonly VisualAgentRegistry _registry;
        private readonly Dictionary<string, IVisualAction> _actions;
        private readonly string _sourceId;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Queue<Dispatch> _dispatches = new Queue<Dispatch>();
        private readonly SemaphoreSlim _modelGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource _feedStop = new CancellationTokenSource();
        private VisualFrame _pending;
        private Task _worker, _actionWorker;
        private bool _visible, _disposed;
        private long _generation;
        private long _sequence, _pendingSequence;
        private TimeSpan? _lastAccepted;
        /// <summary>Raised off the UI thread as needed. Subscribers must marshal UI updates.</summary>
        public event Action Changed;
        /// <summary>Publishes typed event changes without invoking a language model.</summary>
        public event Action<VisualEvent> EventOccurred;
        /// <summary>Creates a session runtime using pre-registered agents and host actions.</summary>
        public VisualMonitorRuntime(VisualAgentRegistry registry, IEnumerable<IVisualAction> actions, string sourceId = "camera")
        { _registry = registry; _actions = actions.ToDictionary(a => a.Id); _sourceId = sourceId; }

        /// <summary>Returns a thread-safe snapshot of session rules; no camera pixels are retained.</summary>
        public IReadOnlyList<VisualMonitorInfo> Snapshot()
        {
            lock (_gate) return _entries.Select(Info).ToArray();
        }
        private static VisualMonitorInfo Info(Entry e) => new VisualMonitorInfo
        {
            Id = e.Monitor.Id, AgentId = e.Agent.Descriptor.Id, Condition = e.Monitor.Condition.Description,
            ActionId = e.Action.Id, Enabled = e.Enabled, State = e.State, Status = e.Status,
            LastEvent = e.LastEvent, LastAction = e.LastAction
        };
        /// <summary>Creates or returns an equivalent object-count rule. Rules start when the feed is visible; at most eight rules are retained.</summary>
        public VisualMonitorInfo CreateObjectMonitor(string label, int minimumCount = 1, double confidence = 0.5,
            string actionId = "play_sound", VisualEventPhase phase = VisualEventPhase.Started)
        {
            var condition = new ObjectCountCondition(label, minimumCount, confidence);
            var agent = _registry.SelectObjects(condition.Label);
            return CreateMonitor(agent.Descriptor.Id, condition, actionId, phase);
        }
        /// <summary>Binds a registered detector, custom observation predicate, event phase and registered action.</summary>
        public VisualMonitorInfo CreateMonitor(string agentId, IVisualCondition condition, string actionId,
            VisualEventPhase phase = VisualEventPhase.Started)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            var agent = _registry.Get(agentId);
            if (!_actions.TryGetValue(actionId, out var action)) throw new ArgumentException("Unavailable action: " + actionId);
            if (!Enum.IsDefined(typeof(VisualEventPhase), phase)) throw new ArgumentException("Invalid event phase.");
            VisualMonitorInfo result;
            lock (_gate)
            {
                ThrowIfDisposed();
                var existing = _entries.FirstOrDefault(e => e.Monitor.Condition.Key == condition.Key &&
                    e.Agent == agent && e.Action.Id == actionId && e.Phase == phase);
                if (existing != null) return Info(existing);
                if (_entries.Count >= 8) throw new InvalidOperationException("Remove a monitor before adding more (maximum 8).");
                var entry = new Entry { Monitor = new VisualPresenceMonitor(condition), Agent = agent, Action = action,
                    Phase = phase, AfterSequence = _sequence, State = _visible ? VisualMonitorState.Preparing : VisualMonitorState.Paused,
                    Status = _visible ? "Preparing detector" : "Waiting for visible live camera" };
                _entries.Add(entry); result = Info(entry);
            }
            Notify(); return result;
        }
        /// <summary>Explicitly pauses or resumes a rule with fresh confirmation state, canceling its previous actions.</summary>
        public void SetEnabled(Guid id, bool enabled)
        {
            CancellationTokenSource old;
            lock (_gate)
            {
                ThrowIfDisposed();
                var entry = Find(id);
                old = entry.Stop; entry.Stop = new CancellationTokenSource();
                entry.Enabled = enabled; entry.Revision++; entry.Monitor.Reset();
                entry.AfterSequence = _sequence;
                entry.State = enabled && _visible ? VisualMonitorState.Preparing : VisualMonitorState.Paused;
                entry.Status = enabled ? (_visible ? "Preparing detector" : "Waiting for visible live camera") : "Paused by user";
            }
            old.Cancel(); old.Dispose(); Notify();
        }
        /// <summary>Removes a rule and cancels its actions.</summary>
        public void Remove(Guid id)
        {
            Entry entry;
            lock (_gate) { entry = Find(id); entry.State = VisualMonitorState.Stopped; entry.Revision++; _entries.Remove(entry); }
            entry.Stop.Cancel(); entry.Stop.Dispose(); Notify();
        }
        /// <summary>Removes all session rules and pending work without discarding registered adapters.</summary>
        public void Clear()
        {
            Entry[] entries;
            lock (_gate) { entries = _entries.ToArray(); _entries.Clear(); _dispatches.Clear(); _pending?.Dispose(); _pending = null; }
            foreach (var entry in entries) { entry.Stop.Cancel(); entry.Stop.Dispose(); }
            Notify();
        }
        private Entry Find(Guid id) => _entries.FirstOrDefault(e => e.Monitor.Id == id)
            ?? throw new ArgumentException("No monitor with ID " + id);

        /// <summary>Pauses work and cancels actions when hidden; resumes enabled rules with fresh state when visible.</summary>
        public void SetFeedVisible(bool visible)
        {
            CancellationTokenSource old;
            lock (_gate)
            {
                if (_disposed || _visible == visible) return;
                _visible = visible; _generation++; _lastAccepted = null;
                old = _feedStop; _feedStop = new CancellationTokenSource();
                _pending?.Dispose(); _pending = null; _dispatches.Clear();
                foreach (var entry in _entries)
                {
                    entry.Monitor.Reset();
                    if (entry.State == VisualMonitorState.Faulted) continue;
                    entry.State = visible && entry.Enabled ? VisualMonitorState.Preparing : VisualMonitorState.Paused;
                    entry.Status = !entry.Enabled ? "Paused by user" : visible ? "Preparing detector" : "Waiting for visible live camera";
                }
            }
            old.Cancel(); old.Dispose(); Notify();
        }
        /// <summary>True only while a visible feed has at least one enabled, non-faulted monitor.</summary>
        public bool WantsFrames
        {
            get { lock (_gate) return !_disposed && _visible && _entries.Any(Runnable); }
        }
        private static bool Runnable(Entry e) => e.Enabled && e.State != VisualMonitorState.Faulted;
        /// <summary>Transfers ownership even if dropped. No more than five frames per second are accepted.</summary>
        public void SubmitFrame(VisualFrame frame)
        {
            lock (_gate)
            {
                if (_disposed || !_visible || frame.SourceId != _sourceId || !_entries.Any(Runnable) ||
                    (_lastAccepted.HasValue && frame.Timestamp - _lastAccepted.Value < TimeSpan.FromMilliseconds(200)))
                { frame.Dispose(); return; }
                _lastAccepted = frame.Timestamp;
                _pending?.Dispose(); _pending = frame;
                _pendingSequence = ++_sequence;
                if (_worker == null) _worker = Task.Run(ProcessFramesAsync);
            }
        }
        private async Task ProcessFramesAsync()
        {
            while (true)
            {
                VisualFrame frame; long generation; CancellationToken token; IVisualAgent[] agents;
                Dictionary<Entry, long> targets;
                lock (_gate)
                {
                    frame = _pending; _pending = null;
                    if (frame == null) { _worker = null; return; }
                    generation = _generation; token = _feedStop.Token;
                    // A rule created/resumed after this frame was submitted must wait for fresh input.
                    targets = _entries.Where(e => Runnable(e) && e.AfterSequence < _pendingSequence).ToDictionary(e => e, e => e.Revision);
                    agents = targets.Keys.Select(e => e.Agent).Distinct().ToArray();
                }
                using (frame)
                foreach (var agent in agents)
                {
                    try
                    {
                        var analysis = await AnalyzeCoreAsync(agent, frame, text => UpdatePreparing(agent, generation, targets, text), token).ConfigureAwait(false);
                        var events = new List<VisualEvent>();
                        lock (_gate)
                        {
                            if (_disposed || !_visible || generation != _generation || token.IsCancellationRequested) continue;
                            foreach (var entry in _entries.Where(e => Runnable(e) && e.Agent == agent && targets.TryGetValue(e, out long revision) && revision == e.Revision))
                            {
                                var occurrence = entry.Monitor.Advance(analysis);
                                if (occurrence == null) continue;
                                entry.LastEvent = $"{occurrence.Phase}: {occurrence.Count} at {analysis.CapturedAt.ToLocalTime():HH:mm:ss}";
                                events.Add(occurrence);
                                if (occurrence.Phase != entry.Phase) continue;
                                if (_dispatches.Count >= 16) { entry.LastAction = "Action skipped: dispatcher busy"; continue; }
                                // One transition per episode; no retries or replay queue across visibility changes.
                                _dispatches.Enqueue(new Dispatch { Entry = entry, Revision = entry.Revision,
                                    Generation = generation, Event = occurrence });
                                if (_actionWorker == null) _actionWorker = Task.Run(DispatchAsync);
                            }
                        }
                        foreach (var occurrence in events) EventOccurred?.Invoke(occurrence);
                        // Avoid rebuilding the UI for every frame; Watching is published once by UpdatePreparing below.
                        if (events.Count > 0) Notify();
                        UpdatePreparing(agent, generation, targets, null);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        lock (_gate)
                        {
                            if (_disposed || generation != _generation) continue;
                            foreach (var entry in _entries.Where(e => Runnable(e) && e.Agent == agent && targets.TryGetValue(e, out long revision) && revision == e.Revision))
                            { entry.State = VisualMonitorState.Faulted; entry.Status = "Detector failed: " + ex.Message; entry.Monitor.Reset(); entry.Revision++; }
                        }
                        Notify();
                    }
                }
            }
        }
        private void UpdatePreparing(IVisualAgent agent, long generation, Dictionary<Entry, long> targets, string text)
        {
            bool changed = false;
            lock (_gate)
            {
                if (_disposed || !_visible || generation != _generation) return;
                foreach (var entry in _entries.Where(e => Runnable(e) && e.Agent == agent && targets.TryGetValue(e, out long revision) && revision == e.Revision))
                {
                    var state = text == null ? VisualMonitorState.Watching : VisualMonitorState.Preparing;
                    string status = text ?? "Watching live camera";
                    if (entry.State != state || entry.Status != status) changed = true;
                    entry.State = state; entry.Status = status;
                }
            }
            if (changed) Notify();
        }
        private async Task<VisualAnalysis> AnalyzeCoreAsync(IVisualAgent agent, VisualFrame frame, Action<string> progress, CancellationToken token)
        {
            await _modelGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                await agent.PrepareAsync(progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var result = await agent.AnalyzeAsync(frame, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return result;
            }
            finally { _modelGate.Release(); }
        }
        /// <summary>Borrow a still image until completion. Does not advance monitors or dispatch actions.</summary>
        public async Task<VisualAnalysis> AnalyzeImageAsync(string agentId, VisualFrame frame, CancellationToken cancellationToken)
        {
            lock (_gate) ThrowIfDisposed();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token))
                return await AnalyzeCoreAsync(_registry.Get(agentId), frame, null, linked.Token).ConfigureAwait(false);
        }
        private async Task DispatchAsync()
        {
            while (true)
            {
                Dispatch item; CancellationTokenSource linked;
                lock (_gate)
                {
                    if (_dispatches.Count == 0) { _actionWorker = null; return; }
                    item = _dispatches.Dequeue();
                    if (!Current(item)) continue;
                    linked = CancellationTokenSource.CreateLinkedTokenSource(_feedStop.Token, item.Entry.Stop.Token);
                }
                string result;
                using (linked)
                {
                    try
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        await item.Entry.Action.ExecuteAsync(item.Event, linked.Token).ConfigureAwait(false);
                        result = "Completed " + item.Entry.Action.Id + " at " + DateTimeOffset.Now.ToString("HH:mm:ss");
                    }
                    catch (OperationCanceledException) when (linked.IsCancellationRequested) { continue; }
                    catch (Exception ex) { result = "Action failed: " + ex.Message; }
                }
                lock (_gate) { if (Current(item)) item.Entry.LastAction = result; }
                Notify();
            }
        }
        private bool Current(Dispatch item) => !_disposed && _visible && item.Generation == _generation &&
            _entries.Contains(item.Entry) && item.Entry.Enabled && item.Entry.Revision == item.Revision;
        private void Notify() => Changed?.Invoke();
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(VisualMonitorRuntime)); }
        /// <summary>Waits for currently queued work; useful for shutdown and deterministic host tests.</summary>
        public async Task DrainAsync()
        {
            while (true)
            {
                Task frames, actions;
                lock (_gate) { frames = _worker; actions = _actionWorker; }
                if (frames == null && actions == null) return;
                await Task.WhenAll(frames ?? Task.CompletedTask, actions ?? Task.CompletedTask).ConfigureAwait(false);
            }
        }
        /// <summary>Stops the session, drains work, and releases all model adapters; no work may be submitted afterward.</summary>
        public async Task DisposeAsync()
        {
            SetFeedVisible(false);
            lock (_gate) { if (_disposed) return; _disposed = true; }
            _lifetime.Cancel(); Clear();
            await DrainAsync().ConfigureAwait(false);
            await _modelGate.WaitAsync().ConfigureAwait(false);
            try { foreach (var agent in _registry.AllAgents) agent.Dispose(); }
            finally { _modelGate.Release(); }
        }
    }
}
