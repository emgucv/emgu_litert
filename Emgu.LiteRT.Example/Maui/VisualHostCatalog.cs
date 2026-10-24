// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.LM.Agents.Audio;

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

    // The confidence a gesture monitor gets when the planner passes the usual default (0.5): the classifier's scores for a real thumbs up sat just above it
    // (a median of 0.57 in front of a real camera, TODO item 47), so 0.5 sat on the edge. Any other value is the user's choice and is kept.
    internal const double GestureDefaultConfidence = 0.4;

    internal static double GestureConfidence(double requested) => Math.Abs(requested - 0.5) < 1e-9 ? GestureDefaultConfidence : requested;

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

    // The body-pose agent (PoseVisualAgent, TODO item 52): postures read from MediaPipe's 33 pose landmarks as the object classes a monitor can watch for.
    internal static readonly string[] PoseClasses = PoseGestures.Names;

    internal static VisualAgentDescriptor PoseDescriptor(bool requiresPreparation = true, string backend = null) => new(
        "body-pose", "Body pose · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "body_pose" }, PoseClasses, requiresPreparation: requiresPreparation);

    // The words people use for each posture. Nothing maps to a fall: the model's card rules out life-critical use.
    private static readonly Dictionary<string, string> PoseWords = BuildPoseWords();

    private static Dictionary<string, string> BuildPoseWords()
    {
        var words = new Dictionary<string, string>();
        void Add(string pose, params string[] names) { foreach (string name in names) words[name] = pose; }
        Add("arms raised", "arms raised", "arms up", "raise arms", "raising arms", "raised arms", "both arms up", "both arms raised", "hands up", "both hands up",
            "hands raised", "hands in the air", "arms in the air", "🙌");
        Add("hand raised", "hand raised", "raised hand", "raise hand", "raise a hand", "raise my hand", "raising a hand", "raising hand", "hand in the air",
            "arm raised", "raise arm", "🙋");
        Add("waving", "waving", "wave", "waves", "a wave", "wave hello", "waving hello", "wave goodbye", "waving goodbye", "wave hi", "waving hand", "👋");
        Add("standing", "standing", "stand", "stands", "standing up", "stand up", "stands up", "on their feet");
        Add("sitting", "sitting", "sit", "sits", "sitting down", "sit down", "sits down", "seated");
        Add("lying down", "lying down", "lying", "lie down", "lies down", "laying down", "lay down", "lying flat");
        return words;
    }

    /// <summary>The posture class for the words a person used (case and punctuation ignored, an agent prefix tolerated), or null if it isn't one.</summary>
    internal static string CanonicalPose(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (PoseWords.TryGetValue(trimmed, out string emoji)) return emoji;
        string normalized = string.Join(" ", new string(trimmed.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (string prefix in new[] { "body pose ", "pose " })
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) { normalized = normalized.Substring(prefix.Length); break; }
        return PoseWords.TryGetValue(normalized, out string pose) ? pose : null;
    }

    // What the request itself says, for when the model's label is no use: Gemma 4 E2B gave "body-pose" (the agent) as the label for "raises their hand" and
    // "person" for "lying down". Only unmistakable phrases count - "stands at the door" is a person, "stands up" a posture - and the first in the text wins.
    private static readonly (string Pose, System.Text.RegularExpressions.Regex Pattern)[] PoseRequestPatterns =
    {
        ("arms raised", new(@"\b(both )?(arms|hands) (are |is )?(up|raised|in the air)\b|\braise[sd]? (both |their |his |her |my |your )?(arms|hands)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("hand raised", new(@"\braise[sd]?( up)? (a |one |their |his |her |my |your |the )?(hand|arm)\b|\braising (a |one |their |his |her |my |your |the )?(hand|arm)\b|\b(hand|arm) (raised|in the air)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("waving", new(@"\bwav(e|es|ed|ing)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("lying down", new(@"\b(lying|lies|lie|laying|lays|lay) down\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("standing", new(@"\bstand(s|ing)? up\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("sitting", new(@"\bsit(s|ting)? down\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
    };

    /// <summary>The posture a request names in so many words ("take a picture when she raises her hand"), or null.</summary>
    internal static string PoseFromRequest(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;
        string best = null;
        int position = int.MaxValue;
        foreach (var (pose, pattern) in PoseRequestPatterns)
        {
            var match = pattern.Match(request);
            if (match.Success && match.Index < position) { best = pose; position = match.Index; }
        }
        return best;
    }

    /// <summary>
    /// The posture class for a monitor's label: the words it holds if they are one, else - when the label is one the model puts there by mistake
    /// (the person class, an agent's name, anything that isn't a class of any agent) - what the user's request says.
    /// </summary>
    internal static string PostureForMonitor(string label, string request)
    {
        string pose = CanonicalPose(label);
        if (pose != null) return pose;
        string normalized = string.Join(" ", new string((label ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        bool mistaken = normalized.Length == 0 || normalized is "person" or "people" or "human" or "body pose" or "hand gesture" or "hand gestures" or "pose" or "body" ||
            !(Classes.Contains(normalized) || Gestures.Contains(normalized) || FaceClasses.Contains(normalized));
        return mistaken ? PoseFromRequest(request) : null;
    }

    /// <summary>
    /// The postures of the person in a frame (and waving, from the detector's recent frames) as observations of their classes, each with the
    /// box of the visible landmarks. <paramref name="pose"/> null (nobody in view) resets the wave detector.
    /// </summary>
    internal static IReadOnlyList<VisualObservation> PoseObservations(Pose pose, int width, int height, PoseGestures.WaveDetector wave, TimeSpan time) =>
        PoseObservations(pose?.Landmarks, width, height, wave, time);

    // Not an iterator: the wave detector must see every frame, whether or not the caller reads the result.
    internal static IReadOnlyList<VisualObservation> PoseObservations(float[][] landmarks, int width, int height, PoseGestures.WaveDetector wave, TimeSpan time)
    {
        var readings = new List<PoseReading>();
        if (landmarks != null) readings.AddRange(PoseGestures.Classify(landmarks, width, height));
        PoseReading waving = wave?.Update(time, landmarks, width, height);
        if (waving != null) readings.Add(waving);
        var observations = new List<VisualObservation>();
        if (landmarks == null) return observations;
        float[] box = PoseGestures.BoxOf(landmarks);
        foreach (PoseReading reading in readings)
            observations.Add(new VisualObservation("object", reading.Name, Math.Clamp(reading.Score, 0f, 1f),
                new VisualBox(box[0], box[1], Math.Max(0, box[2] - box[0]), Math.Max(0, box[3] - box[1]))));
        return observations;
    }

    // The face-expression agent (FaceVisualAgent): expressions read from MediaPipe's face blendshapes as the object classes a monitor can watch for. Their
    // scores are scaled so the expression's threshold is 0.5, the confidence the planner passes by default.
    internal static readonly string[] FaceClasses = FaceExpressions.Names;

    internal static VisualAgentDescriptor FaceDescriptor(bool requiresPreparation = true, string backend = null) => new(
        "face-expression", "Face expressions · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "face_expression" }, FaceClasses, requiresPreparation: requiresPreparation);

    // The words people use for each expression. Nothing maps to tiredness, attention or who someone is: the model cards rule that out. A blink is too brief
    // for a monitor's hold time, so it isn't "eyes closed".
    private static readonly Dictionary<string, string> ExpressionWords = BuildExpressionWords();

    private static Dictionary<string, string> BuildExpressionWords()
    {
        var words = new Dictionary<string, string>();
        void Add(string expression, params string[] names) { foreach (string name in names) words[name] = expression; }
        Add("smiling", "smiling", "smile", "smiles", "a smile", "smiling face", "big smile", "grin", "grins", "grinning", "😀", "😃", "😄", "😁", "😊", "🙂");
        Add("eyes closed", "eyes closed", "closed eyes", "close eyes", "closes eyes", "closing eyes", "eyes shut", "shut eyes", "both eyes closed");
        Add("winking", "winking", "wink", "winks", "a wink", "😉");
        Add("mouth open", "mouth open", "open mouth", "opens mouth", "opening mouth", "mouth wide open", "wide open mouth", "jaw dropped", "😮", "😲", "😯");
        Add("eyebrows raised", "eyebrows raised", "raised eyebrows", "raise eyebrows", "raises eyebrows", "raising eyebrows", "eyebrows up", "brows raised",
            "raised brows", "raise brows");
        return words;
    }

    /// <summary>The expression class for the words a person used (case and punctuation ignored, an agent prefix tolerated), or null if it isn't one.</summary>
    internal static string CanonicalExpression(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (ExpressionWords.TryGetValue(trimmed, out string emoji)) return emoji;
        string normalized = string.Join(" ", new string(trimmed.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        foreach (string prefix in new[] { "face expressions ", "face expression ", "face " })
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) { normalized = normalized.Substring(prefix.Length); break; }
        return ExpressionWords.TryGetValue(normalized, out string expression) ? expression : null;
    }

    private const string Whose = "(my |their |his |her |your |the |both )?";

    // What the request itself says, for when the model gives the person class, "face" or an agent's name as the label. The first in the text wins.
    private static readonly (string Expression, System.Text.RegularExpressions.Regex Pattern)[] ExpressionRequestPatterns =
    {
        ("smiling", new(@"\bsmil(e|es|ed|ing)\b|\bgrin(s|ned|ning)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("eyes closed", new(@"\b(close[sd]?|closing|shut(s|ting)?) " + Whose + @"eyes\b|\beyes (are |is )?(closed|shut)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("winking", new(@"\bwink(s|ed|ing)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("mouth open", new(@"\bopen(s|ed|ing)? " + Whose + @"mouth\b|\bmouth (is )?(wide )?open\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
        ("eyebrows raised", new(@"\brais(e|es|ed|ing) " + Whose + @"(eye)?brows\b|\b(eye)?brows (are |go )?(raised|up)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
    };

    /// <summary>The expression a request names in so many words ("take a picture when I smile"), or null.</summary>
    internal static string ExpressionFromRequest(string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;
        string best = null;
        int position = int.MaxValue;
        foreach (var (expression, pattern) in ExpressionRequestPatterns)
        {
            var match = pattern.Match(request);
            if (match.Success && match.Index < position) { best = expression; position = match.Index; }
        }
        return best;
    }

    /// <summary>
    /// The expression class for a monitor's label: the words it holds if they are one, else - when the label is one the model puts there by mistake
    /// (the person class, "face", an agent's name, anything that isn't a class or posture word of another agent) - what the user's request says.
    /// </summary>
    internal static string ExpressionForMonitor(string label, string request)
    {
        string expression = CanonicalExpression(label);
        if (expression != null) return expression;
        string normalized = string.Join(" ", new string((label ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        bool mistaken = normalized.Length == 0 || normalized is "person" or "people" or "human" or "face" or "faces" or "face expression" or "face expressions" or "expression" ||
            !(Classes.Contains(normalized) || Gestures.Contains(normalized) || CanonicalGesture(normalized) != null || CanonicalPose(normalized) != null);
        return mistaken ? ExpressionFromRequest(request) : null;
    }

    /// <summary>The expressions of a face as observations of their classes, each with the box of the face's landmarks; none without a face.</summary>
    internal static IReadOnlyList<VisualObservation> FaceObservations(Face face)
    {
        var observations = new List<VisualObservation>();
        if (face == null) return observations;
        float[] box = FaceExpressions.BoxOf(face);
        foreach (FaceExpressionReading reading in FaceExpressions.Classify(face))
            observations.Add(new VisualObservation("object", reading.Name, Math.Clamp(reading.Score, 0f, 1f),
                new VisualBox(box[0], box[1], Math.Max(0, box[2] - box[0]), Math.Max(0, box[3] - box[1]))));
        return observations;
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

    // The play_file action (AlertSoundLibrary, TODO item 53): one of the app's alert sounds - the names change as the user adds some, so the descriptor is built on request.
    internal static VisualActionDescriptor PlayFile(IReadOnlyList<string> names) => new("play_file", "Play a sound",
        "Play one of the app's alert sounds when the alert fires: soft white noise, a lullaby, or sounds the user added. It plays once, or repeated for " +
        "`seconds` (1 to 300) - use that for a lullaby or white noise meant to go on. The volume is the device's media volume. Available sounds: " +
        (names.Count == 0 ? "none yet" : string.Join(", ", names)) + ".",
        VisualActionParameters.Parse("{\"type\":\"object\",\"properties\":{\"sound\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":60}," +
            "\"seconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":300}},\"required\":[\"sound\"],\"additionalProperties\":false}"),
        names.Count > 0);

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
