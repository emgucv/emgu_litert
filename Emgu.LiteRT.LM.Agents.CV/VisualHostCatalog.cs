// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.LM.Agents.Audio;

namespace Emgu.LiteRT.LM.Agents.CV;

/// <summary>
/// A chat's camera planner instructions, its alert actions' descriptors and the words of its monitors, shared with model
/// regressions, without camera or audio platform dependencies. The visual agents, their classes and the words for those
/// are <see cref="VisualClasses"/>'.
/// </summary>
public static class VisualHostCatalog
{
    /// <summary>The camera planner's instructions for the visual agents and the alert actions. The camera switching, clip and full-resolution photo sentences are for Android, the only platform with them.</summary>
    /// <param name="android">Whether the app runs on Android</param>
    public static string PlannerInstructions(bool android) => VisualAgentTools.Instructions +
        " Prefer coco-ssd-mobilenet-v3 for ordinary object monitors. Use the experimental coco-yolox-nano when the user asks for YOLOX." +
        " For a hand gesture use the hand-gesture agent and pick the label that matches the user's words: thumbs up = thumb up, thumbs down = thumb down, peace sign or V sign = victory, " +
        "open hand or high five = open palm, fist = closed fist, pointing finger = pointing up, rock sign = i love you. The label is just the class, never prefixed by the agent id, and never person. Raising or waving a hand is not a gesture: that is the body-pose agent. " +
        "It sees upright hands, a gesture held for about a second." +
        " To play a recorded sound when an alert fires - a lullaby, white noise, one of the user's own sounds - select play_file with parameters {sound: its name from list_visual_actions, seconds: how long to keep repeating it (60 if the user wants it to go on without saying for how long; leave out to play it once)}. " +
        " For what a person's body is doing - waving, raising a hand or both arms, standing, sitting, lying down - use the body-pose agent, not hand-gesture and not person: the label is exactly one of " +
        "arms raised (both arms up, hands in the air), hand raised (raising a hand or arm), waving, standing, sitting, lying down. " +
        "It sees one person with the head in view, within a few metres; waving takes about two seconds to show; standing and sitting need the legs in view. It can't detect falls or tell people apart." +
        " For a facial expression - smiling, closing the eyes, winking, opening the mouth, raising the eyebrows - use the face-expression agent, not person: the label is exactly one of " +
        "smiling, eyes closed, winking, mouth open, eyebrows raised. It reads one face looking at the camera, within about two metres. It can't recognise who someone is, " +
        "and it is not for checking whether someone is tired, drowsy or paying attention: decline such requests." +
        " For a posture and an expression at once (raise a hand and smile, wave while smiling) use the holistic agent: the label is the posture, \"and\", then the expression, " +
        "e.g. hand raised and smiling, waving and smiling, arms raised and mouth open. For covering the eyes (peekaboo), a hand over the mouth or a hand on the face use the holistic agent " +
        "with exactly covering eyes, hand over mouth or hand on face." +
        (android
            ? " The app switches between the back camera and the selfie (front) camera by itself when the user's request says so: don't refuse such a request, use the camera tools as usual and say which camera is used." +
              " For record a video or clip when an object appears, select record_clip with parameters {seconds: the requested length, default 10, from 3 to 30}. " +
              "It records from the live camera with sound and shows the clip in this chat session; no automatic saving or model analysis. A request for both a picture and a clip uses take_snapshot and record_clip. " +
              "For a full resolution, high resolution or high quality photo when an object appears, select take_photo (parameters {}), not take_snapshot; it is slower and shows the full-size photo in this chat session."
            : "");

    /// <summary>The play_sound action: the bundled chime once.</summary>
    public static readonly VisualActionDescriptor Chime = new("play_sound", "Chime", "Play the bundled alert chime once.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));

    /// <summary>The speak_text action: fixed words spoken once.</summary>
    public static readonly VisualActionDescriptor Speech = new("speak_text", "Spoken message",
        "Speak fixed requested words once. Uses the system voice and existing language selection, independently of chat read-aloud.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":200}},\"required\":[\"text\"],\"additionalProperties\":false}"));

    /// <summary>The take_snapshot action: a small frame from the live camera, shown in the chat.</summary>
    public static readonly VisualActionDescriptor Snapshot = new("take_snapshot", "Snapshot",
        "Capture one fresh JPEG from the visible live camera after confirmed appearance and show it in this chat session. " +
        "Not sent to the model or saved automatically. The user can open the image and save it manually.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));
    /// <summary>The take_photo action: a full-resolution photo, shown in the chat (Android).</summary>
    public static readonly VisualActionDescriptor Photo = new("take_photo", "Full-resolution photo",
        "Take one full-resolution photo (the camera's largest JPEG, after exposure and focus settle) from the visible live camera after confirmed " +
        "appearance and show it in this chat session. Slower than take_snapshot, which is a small frame. Not sent to the model or saved automatically. " +
        "The user can open, share or save it. Android only.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"));

    /// <summary>The play_file action: one of the app's alert sounds - the names change as the user adds some, so the descriptor is built on request.</summary>
    /// <param name="names">The sounds' names</param>
    public static VisualActionDescriptor PlayFile(IReadOnlyList<string> names) => new("play_file", "Play a sound",
        "Play one of the app's alert sounds when the alert fires: soft white noise, a lullaby, or sounds the user added. It plays once, or repeated for " +
        "`seconds` (1 to 300) - use that for a lullaby or white noise meant to go on. The volume is the device's media volume. Available sounds: " +
        (names.Count == 0 ? "none yet" : string.Join(", ", names)) + ".",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"sound\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":60}," +
            "\"seconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":300}},\"required\":[\"sound\"],\"additionalProperties\":false}"),
        names.Count > 0);

    /// <summary>The most characters of the user's request kept with a monitor.</summary>
    public const int MaximumRequestLength = 300;

    /// <summary>The user's own words behind a monitor, for its card: one line of text, at most 300 characters (an ellipsis if cut); null if blank.</summary>
    /// <param name="text">The request</param>
    public static string CleanRequest(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string line = string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaximumRequestLength ? line : line.Substring(0, MaximumRequestLength - 1).TrimEnd() + "…";
    }

    /// <summary>Which camera the user's words ask for: "front" (selfie, front-facing...), "back" (rear, back-facing, main camera...) or null. The one mentioned last wins.</summary>
    /// <param name="text">The request</param>
    public static string CameraFacingRequest(string text)
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

    /// <summary>A clip's length in seconds when none is given.</summary>
    public const int DefaultClipSeconds = 10;
    /// <summary>The shortest clip, in seconds.</summary>
    public const int MinimumClipSeconds = 3;
    /// <summary>The longest clip, in seconds.</summary>
    public const int MaximumClipSeconds = 30;

    /// <summary>The record_clip action: a short video clip from the live camera, shown in the chat (Android).</summary>
    public static readonly VisualActionDescriptor Clip = new("record_clip", "Video clip",
        "Record a short video clip (with sound if the microphone is allowed) from the visible live camera when the alert fires, " +
        "and show it in this chat session. Not sent to the model or saved automatically. The user can play, share or save it. Android only.",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"seconds\":{\"type\":\"integer\",\"minimum\":" + MinimumClipSeconds +
            ",\"maximum\":" + MaximumClipSeconds + "}},\"additionalProperties\":false}"));

    /// <summary>The clip's length in seconds (the default if none is given) and its normalized parameters; rejects anything else.</summary>
    /// <param name="parameters">The action's parameters</param>
    /// <param name="normalized">The parameters as kept</param>
    public static int ClipSeconds(System.Text.Json.JsonElement parameters, out System.Text.Json.JsonElement normalized)
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

    /// <summary>A monitor's state for the model: its id, state, status, condition and actions.</summary>
    /// <param name="monitor">The monitor</param>
    public static string MonitorResult(VisualMonitorInfo monitor) =>
        "Monitor " + monitor.Id + ": " + monitor.State + ". " + monitor.Status + ". " + monitor.Condition +
        ". Actions: " + string.Join(", ", monitor.Actions.Select(a => a.ActionId)) + ".";

}
