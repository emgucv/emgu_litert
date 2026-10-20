// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Maui.Demo.Lite;

// Host descriptors shared with model regressions, without camera/audio platform dependencies.
internal static class VisualHostCatalog
{
    internal static readonly string[] Classes = ("person,bicycle,car,motorcycle,airplane,bus,train,truck,boat,traffic light," +
        "fire hydrant,stop sign,parking meter,bench,bird,cat,dog,horse,sheep,cow,elephant,bear,zebra,giraffe,backpack," +
        "umbrella,handbag,tie,suitcase,frisbee,skis,snowboard,sports ball,kite,baseball bat,baseball glove,skateboard," +
        "surfboard,tennis racket,bottle,wine glass,cup,fork,knife,spoon,bowl,banana,apple,sandwich,orange,broccoli," +
        "carrot,hot dog,pizza,donut,cake,chair,couch,potted plant,bed,dining table,toilet,tv,laptop,mouse,remote," +
        "keyboard,cell phone,microwave,oven,toaster,sink,refrigerator,book,clock,vase,scissors,teddy bear,hair drier,toothbrush").Split(',');

    internal static VisualAgentDescriptor Detector(bool requiresPreparation = true, string backend = null) => new(
        "coco-ssd-mobilenet-v3", "COCO SSD MobileNet V3" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection" }, Classes, requiresPreparation: requiresPreparation);

    internal static VisualAgentDescriptor YoloXDetector(bool requiresPreparation = true, string backend = null) => new(
        "coco-yolox-nano", "YOLOX Nano (experimental)" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection" }, Classes, requiresPreparation: requiresPreparation);

    internal static readonly VisualActionDescriptor Chime = new("play_sound", "Chime", "Play the bundled alert chime once.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));

    internal static readonly VisualActionDescriptor Speech = new("speak_text", "Spoken message",
        "Speak fixed requested words once. Uses the system voice and existing language selection, independently of chat read-aloud.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}},\"required\":[\"text\"],\"additionalProperties\":false}"));

    internal static readonly VisualActionDescriptor Snapshot = new("take_snapshot", "Snapshot",
        "Capture one fresh JPEG from the visible live camera after confirmed appearance and show it in this chat session. " +
        "Not sent to the model or saved automatically. The user can open the image and save it manually.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));
    internal const int DefaultClipSeconds = 10, MinimumClipSeconds = 3, MaximumClipSeconds = 30;

    internal static readonly VisualActionDescriptor Clip = new("record_clip", "Video clip",
        "Record a short video clip (with sound if the microphone is allowed) from the visible live camera when the alert fires, " +
        "and show it in this chat session. Not sent to the model or saved automatically. The user can play, share or save it. Android only.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"seconds\":{\"type\":\"integer\",\"minimum\":" + MinimumClipSeconds +
            ",\"maximum\":" + MaximumClipSeconds + "}},\"additionalProperties\":false}"));

    // The clip's length in seconds (the default if none is given) and its normalized parameters; rejects anything else.
    internal static int ClipSeconds(System.Text.Json.JsonElement parameters, out System.Text.Json.JsonElement normalized)
    {
        VisualActionParameters.AllowOnly(parameters, "seconds");
        int seconds = DefaultClipSeconds;
        if (parameters.TryGetProperty("seconds", out var value))
        {
            if (value.ValueKind != System.Text.Json.JsonValueKind.Number || !value.TryGetInt32(out seconds) ||
                seconds < MinimumClipSeconds || seconds > MaximumClipSeconds)
                throw new ArgumentException("seconds must be a whole number from " + MinimumClipSeconds + " to " + MaximumClipSeconds + ".");
        }
        normalized = VisualActionParameters.Parse("{\"seconds\":" + seconds + "}");
        return seconds;
    }

    internal static string MonitorResult(VisualMonitorInfo monitor) =>
        "Monitor " + monitor.Id + ": " + monitor.State + ". " + monitor.Status + ". " + monitor.Condition +
        ". Actions: " + string.Join(", ", monitor.Actions.Select(a => a.ActionId)) + ".";

}
