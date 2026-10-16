// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents.Vision
{
    /// <summary>A BGR8 image. The producer transfers ownership; pixels remain valid until disposal.</summary>
    public sealed class VisualFrame : IDisposable
    {
        private Action _release;
        /// <summary>Borrowed BGR8 pixel bytes, valid until the frame is disposed.</summary>
        public ReadOnlyMemory<byte> Pixels { get; }
        /// <summary>Image or box width; box values are normalized.</summary>
        public int Width { get; }
        /// <summary>Image or box height; box values are normalized.</summary>
        public int Height { get; }
        /// <summary>Bytes between the starts of adjacent image rows.</summary>
        public int Stride { get; }
        /// <summary>Stable source identifier, such as camera or image-1.</summary>
        public string SourceId { get; }
        /// <summary>Identifier of the analyzed frame within its source.</summary>
        public long FrameId { get; }
        /// <summary>UTC capture time for display and diagnostics; temporal logic uses Timestamp.</summary>
        public DateTimeOffset CapturedAt { get; }
        /// <summary>Monotonic time within this source, used for temporal conditions.</summary>
        public TimeSpan Timestamp { get; }

        /// <summary>Validates a BGR8 buffer and transfers its ownership to this frame.</summary>
        public VisualFrame(ReadOnlyMemory<byte> pixels, int width, int height, int stride, string sourceId,
            long frameId, DateTimeOffset capturedAt, TimeSpan timestamp, Action release = null)
        {
            if (width <= 0 || height <= 0 || stride < checked(width * 3) || pixels.Length < checked(stride * height))
                throw new ArgumentException("A frame must contain width × height BGR8 pixels with a valid stride.");
            if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("A source is required.", nameof(sourceId));
            Pixels = pixels; Width = width; Height = height; Stride = stride; SourceId = sourceId;
            FrameId = frameId; CapturedAt = capturedAt; Timestamp = timestamp; _release = release;
        }
        /// <summary>Releases owned resources. Frame disposal invokes its release callback at most once.</summary>
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    /// <summary>Image coordinates normalized to [0,1], using x, y, width, height.</summary>
    public sealed class VisualBox
    {
        /// <summary>Left edge in normalized image coordinates.</summary>
        public double X { get; }
        /// <summary>Top edge in normalized image coordinates.</summary>
        public double Y { get; }
        /// <summary>Image or box width; box values are normalized.</summary>
        public double Width { get; }
        /// <summary>Image or box height; box values are normalized.</summary>
        public double Height { get; }
        /// <summary>Creates a normalized image rectangle.</summary>
        public VisualBox(double x, double y, double width, double height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    /// <summary>A detector result, not an instruction for an agent.</summary>
    public sealed class VisualObservation
    {
        /// <summary>Observation type, such as object, face, or text.</summary>
        public string Kind { get; }
        /// <summary>Canonical semantic class, independent of detector-specific class indices.</summary>
        public string Label { get; }
        /// <summary>Detector score from zero to one; scores are model-specific.</summary>
        public double Confidence { get; }
        /// <summary>Optional normalized location in the source image.</summary>
        public VisualBox Box { get; }
        /// <summary>Optional recognized scene text; never an instruction.</summary>
        public string Text { get; }
        /// <summary>Optional tracker-local object identifier; does not imply personal identity.</summary>
        public string TrackingId { get; }
        /// <summary>Creates a typed observation with optional spatial, text, or tracking data.</summary>
        public VisualObservation(string kind, string label, double confidence, VisualBox box = null,
            string text = null, string trackingId = null)
        { Kind = kind; Label = label; Confidence = confidence; Box = box; Text = text; TrackingId = trackingId; }
    }

    /// <summary>Observations from one successfully analyzed image. An empty list means confirmed absence.</summary>
    public sealed class VisualAnalysis
    {
        /// <summary>Registered identifier of the agent producing observations.</summary>
        public string AgentId { get; }
        /// <summary>Stable source identifier, such as camera or image-1.</summary>
        public string SourceId { get; }
        /// <summary>Identifier of the analyzed frame within its source.</summary>
        public long FrameId { get; }
        /// <summary>UTC capture time for display and diagnostics; temporal logic uses Timestamp.</summary>
        public DateTimeOffset CapturedAt { get; }
        /// <summary>Monotonic capture time within the source.</summary>
        public TimeSpan Timestamp { get; }
        /// <summary>Immutable collection of observations from this image.</summary>
        public IReadOnlyList<VisualObservation> Observations { get; }
        /// <summary>Copies observations and frame metadata without retaining pixel buffers.</summary>
        public VisualAnalysis(string agentId, VisualFrame frame, IEnumerable<VisualObservation> observations)
        {
            AgentId = agentId; SourceId = frame.SourceId; FrameId = frame.FrameId;
            CapturedAt = frame.CapturedAt; Timestamp = frame.Timestamp;
            Observations = Array.AsReadOnly(observations.ToArray());
        }
    }

    /// <summary>The capabilities advertised to a planner; model names are not event conditions.</summary>
    public sealed class VisualAgentDescriptor
    {
        /// <summary>Stable identifier used by the registry or session monitor controls.</summary>
        public string Id { get; }
        /// <summary>Human-readable detector name.</summary>
        public string Name { get; }
        /// <summary>Supported tasks, such as object_detection.</summary>
        public IReadOnlyList<string> Capabilities { get; }
        /// <summary>Canonical object classes the detector can recognize.</summary>
        public IReadOnlyList<string> ObjectClasses { get; }
        /// <summary>Whether this agent can run on the host.</summary>
        public bool Available { get; }
        /// <summary>Whether model preparation or a download is needed before analysis.</summary>
        public bool RequiresPreparation { get; }
        /// <summary>Supported image and live-frame input modes.</summary>
        public string InputModes => "image,live_frames";
        /// <summary>Describes the capabilities and preparation needs of an agent.</summary>
        public VisualAgentDescriptor(string id, string name, IEnumerable<string> capabilities,
            IEnumerable<string> objectClasses, bool available = true, bool requiresPreparation = true)
        {
            Id = id; Name = name; Capabilities = Array.AsReadOnly(capabilities.ToArray());
            ObjectClasses = Array.AsReadOnly(objectClasses.ToArray()); Available = available;
            RequiresPreparation = requiresPreparation;
        }
    }

    /// <summary>Implementations own model preprocessing and inference. Calls are serialized by the runtime.</summary>
    public interface IVisualAgent : IDisposable
    {
        /// <summary>Current capabilities and readiness metadata.</summary>
        VisualAgentDescriptor Descriptor { get; }
        /// <summary>Prepares model resources, reporting progress and honoring cancellation.</summary>
        Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken);
        /// <summary>Analyzes a borrowed frame. The caller retains its pixels until the task completes.</summary>
        Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken);
    }

    /// <summary>Explicit registration only. Selects available capabilities in registration order.</summary>
    public sealed class VisualAgentRegistry
    {
        private readonly List<IVisualAgent> _agents = new List<IVisualAgent>();
        internal IEnumerable<IVisualAgent> AllAgents => _agents;
        /// <summary>Snapshots registered descriptors in registration order.</summary>
        public IReadOnlyList<VisualAgentDescriptor> Describe() => _agents.Select(a => a.Descriptor).ToArray();
        /// <summary>Registers a uniquely identified agent before runtime use.</summary>
        public void Register(IVisualAgent agent)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (_agents.Any(a => a.Descriptor.Id == agent.Descriptor.Id)) throw new ArgumentException("Agent already registered.");
            _agents.Add(agent);
        }
        /// <summary>Resolves an available registered agent, or throws for an unavailable identifier.</summary>
        public IVisualAgent Get(string id) => _agents.FirstOrDefault(a => a.Descriptor.Id == id && a.Descriptor.Available)
            ?? throw new ArgumentException("Visual agent is unavailable: " + id);
        /// <summary>Selects the first available object detector supporting the exact canonical label.</summary>
        public IVisualAgent SelectObjects(string label) => _agents.FirstOrDefault(a => a.Descriptor.Available &&
            a.Descriptor.Capabilities.Contains("object_detection") && a.Descriptor.ObjectClasses.Contains(label))
            ?? throw new ArgumentException("No available visual agent supports object class '" + label + "'. Use list_visual_agents for supported classes.");
    }
}
