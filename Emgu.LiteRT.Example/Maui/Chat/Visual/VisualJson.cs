// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Text.Json.Serialization;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Maui.Demo.Lite;

internal sealed class VisualImageSummary
{
    public string AgentId { get; set; }
    public string SourceId { get; set; }
    public long FrameId { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public int Count { get; set; }
    public double ConfidenceThreshold { get; set; } = 0.5;
    public VisualObservation[] Observations { get; set; }
    public bool Truncated { get; set; }
}

[JsonSerializable(typeof(VisualAgentDescriptor[]))]
[JsonSerializable(typeof(VisualActionDescriptor[]))]
[JsonSerializable(typeof(VisualActionRequest[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(VisualMonitorInfo[]))]
[JsonSerializable(typeof(VisualMonitorInfo))]
[JsonSerializable(typeof(VisualImageSummary))]
internal partial class VisualJson : JsonSerializerContext { }
#endif
