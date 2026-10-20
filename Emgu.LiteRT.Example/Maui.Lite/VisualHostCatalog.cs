// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.Models;
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

    // The hand-gesture agent (HandVisualAgent): MediaPipe's gesture classes as the object classes a monitor can watch for.
    internal static readonly string[] Gestures = { "thumb up", "thumb down", "victory", "open palm", "closed fist", "pointing up", "i love you" };

    internal static VisualAgentDescriptor HandDetector(bool requiresPreparation = true, string backend = null) => new(
        "hand-gesture", "Hand gestures · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "hand_gestures" }, Gestures, requiresPreparation: requiresPreparation);

    // The words people use for each gesture (and the emoji), besides the class names and MediaPipe's own labels (Thumb_Up, ILoveYou...).
    // "stop", "stop sign" and "like" are left out: a stop sign is a COCO class.
    private static readonly Dictionary<string, string> GestureWords = BuildGestureWords();

    private static Dictionary<string, string> BuildGestureWords()
    {
        var words = new Dictionary<string, string>();
        void Add(string gesture, params string[] names) { foreach (string name in names) words[name] = gesture; }
        Add("thumb up", "thumb up", "thumbs up", "thumbsup", "thumb up sign", "thumbs up sign", "thumb up gesture", "thumbs up gesture", "👍");
        Add("thumb down", "thumb down", "thumbs down", "thumbsdown", "thumb down sign", "thumbs down sign", "dislike", "👎");
        Add("victory", "victory", "victory sign", "victory gesture", "peace", "peace sign", "v sign", "two fingers", "✌", "✌️");
        Add("open palm", "open palm", "palm", "open hand", "flat hand", "high five", "hand up", "✋", "🖐");
        Add("closed fist", "closed fist", "fist", "clenched fist", "fist bump", "✊");
        Add("pointing up", "pointing up", "point up", "pointing finger", "finger up", "index finger", "☝", "☝️");
        Add("i love you", "i love you", "iloveyou", "ily", "love sign", "🤟");
        return words;
    }

    /// <summary>The gesture class for a MediaPipe label or the words a person used (case, underscores and punctuation ignored), or null if it isn't one.</summary>
    internal static string CanonicalGesture(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (GestureWords.TryGetValue(trimmed, out string emoji)) return emoji;
        string normalized = string.Join(" ", new string(trimmed.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        // A model sometimes joins the agent's id and the class ("hand-gesture.thumb up").
        foreach (string prefix in new[] { "hand gestures ", "hand gesture " })
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) { normalized = normalized.Substring(prefix.Length); break; }
        return GestureWords.TryGetValue(normalized, out string gesture) ? gesture : null;
    }

    /// <summary>A recognised gesture of each hand as an observation of that class, with the hand's box (from its landmarks); "none" is not reported.</summary>
    internal static IEnumerable<VisualObservation> GestureObservations(IReadOnlyList<Hand> hands)
    {
        foreach (Hand hand in hands)
        {
            string gesture = CanonicalGesture(hand.Gesture);
            if (gesture == null || hand.GestureScore < 0 || hand.GestureScore > 1) continue;
            double left = 1, top = 1, right = 0, bottom = 0;
            foreach (float[] point in hand.Landmarks)
            {
                left = Math.Min(left, point[0]); right = Math.Max(right, point[0]);
                top = Math.Min(top, point[1]); bottom = Math.Max(bottom, point[1]);
            }
            left = Math.Clamp(left, 0, 1); top = Math.Clamp(top, 0, 1);
            yield return new VisualObservation("object", gesture, hand.GestureScore,
                new VisualBox(left, top, Math.Max(0, Math.Clamp(right, 0, 1) - left), Math.Max(0, Math.Clamp(bottom, 0, 1) - top)));
        }
    }

    internal static readonly VisualActionDescriptor Chime = new("play_sound", "Chime", "Play the bundled alert chime once.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));

    internal static readonly VisualActionDescriptor Speech = new("speak_text", "Spoken message",
        "Speak fixed requested words once. Uses the system voice and existing language selection, independently of chat read-aloud.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}},\"required\":[\"text\"],\"additionalProperties\":false}"));

    internal static readonly VisualActionDescriptor Snapshot = new("take_snapshot", "Snapshot",
        "Capture one fresh JPEG from the visible live camera after confirmed appearance and show it in this chat session. " +
        "Not sent to the model or saved automatically. The user can open the image and save it manually.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));
    internal static readonly VisualActionDescriptor Photo = new("take_photo", "Full-resolution photo",
        "Take one full-resolution photo (the camera's largest JPEG, after exposure and focus settle) from the visible live camera after confirmed " +
        "appearance and show it in this chat session. Slower than take_snapshot, which is a small frame. Not sent to the model or saved automatically. " +
        "The user can open, share or save it. Android only.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));

    internal const int MaximumRequestLength = 300;

    // The user's own words behind a monitor, for its card: one line of text, at most 300 characters (an ellipsis if cut); null if blank.
    internal static string CleanRequest(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string line = string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaximumRequestLength ? line : line.Substring(0, MaximumRequestLength - 1).TrimEnd() + "…";
    }

    // Which camera the user's words ask for: "front" (selfie, front-facing...), "back" (rear, back-facing, main camera...) or null. The one mentioned last wins.
    internal static string CameraFacingRequest(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string said = " " + string.Join(" ", new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + " ";
        string result = null; int at = -1;
        void Look(string facing, params string[] phrases)
        {
            foreach (string phrase in phrases)
            {
                int index = said.LastIndexOf(" " + phrase + " ", StringComparison.Ordinal);
                if (index > at) { at = index; result = facing; }
            }
        }
        Look("front", "selfie", "selfie camera", "front camera", "front cam", "front facing", "front facing camera", "user facing", "forward camera", "camera facing me", "facing me");
        Look("back", "back camera", "back cam", "rear camera", "rear cam", "rear facing", "back facing", "main camera", "outward camera");
        return result;
    }

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
