// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Text.RegularExpressions;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents.Vision;

/// <summary>
/// The classes the visual agents of this library report (<see cref="CocoVisualAgent"/>, <see cref="HandVisualAgent"/>, <see cref="PoseVisualAgent"/>,
/// <see cref="FaceVisualAgent"/>, <see cref="HolisticVisualAgent"/>), the words people use for them, and the class a camera monitor's label means: a
/// language model often gives a monitor's label in other words ("thumbs up", "raise my hand"), as the agent's name, or as the person class, so the
/// host reads the label - and, when the label is no use, the user's request - with these.
/// </summary>
public static class VisualClasses
{
    /// <summary>The 80 COCO classes the object detectors report</summary>
    public static readonly string[] ObjectClasses = ("person,bicycle,car,motorcycle,airplane,bus,train,truck,boat,traffic light," +
        "fire hydrant,stop sign,parking meter,bench,bird,cat,dog,horse,sheep,cow,elephant,bear,zebra,giraffe,backpack," +
        "umbrella,handbag,tie,suitcase,frisbee,skis,snowboard,sports ball,kite,baseball bat,baseball glove,skateboard," +
        "surfboard,tennis racket,bottle,wine glass,cup,fork,knife,spoon,bowl,banana,apple,sandwich,orange,broccoli," +
        "carrot,hot dog,pizza,donut,cake,chair,couch,potted plant,bed,dining table,toilet,tv,laptop,mouse,remote," +
        "keyboard,cell phone,microwave,oven,toaster,sink,refrigerator,book,clock,vase,scissors,teddy bear,hair drier,toothbrush").Split(',');

    /// <summary>The COCO class for a detector's label, with the older VOC-style names some models use ("motorbike", "tvmonitor") mapped to it.</summary>
    public static string CanonicalObject(string label) => (label ?? "").Trim().ToLowerInvariant() switch
    {
        "motorbike" => "motorcycle", "aeroplane" => "airplane", "sofa" => "couch",
        "pottedplant" => "potted plant", "diningtable" => "dining table", "tvmonitor" => "tv", var value => value
    };

    /// <summary>MediaPipe's gesture classes, as the hand-gesture agent reports them</summary>
    public static readonly string[] Gestures = { "thumb up", "thumb down", "victory", "open palm", "closed fist", "pointing up", "i love you" };

    // The words people use for each gesture (and the emoji), besides the class names and MediaPipe's own labels (Thumb_Up, ILoveYou...).
    // "stop", "stop sign" and "like" are left out: a stop sign is a COCO class.
    private static readonly Dictionary<string, string> GestureWords = Words(add =>
    {
        add("thumb up", new[] { "thumb up", "thumbs up", "thumbsup", "thumb up sign", "thumbs up sign", "thumb up gesture", "thumbs up gesture", "👍" });
        add("thumb down", new[] { "thumb down", "thumbs down", "thumbsdown", "thumb down sign", "thumbs down sign", "dislike", "👎" });
        add("victory", new[] { "victory", "victory sign", "victory gesture", "peace", "peace sign", "v sign", "two fingers", "✌", "✌️" });
        add("open palm", new[] { "open palm", "palm", "open hand", "flat hand", "high five", "hand up", "✋", "🖐" });
        add("closed fist", new[] { "closed fist", "fist", "clenched fist", "fist bump", "✊" });
        add("pointing up", new[] { "pointing up", "point up", "pointing finger", "finger up", "index finger", "☝", "☝️" });
        add("i love you", new[] { "i love you", "iloveyou", "ily", "love sign", "🤟" });
    });

    /// <summary>The confidence a gesture monitor gets when the planner passes the usual default (0.5): the classifier's scores for a real thumbs up sat
    /// just above it (a median of 0.57 in front of a real camera), so 0.5 sat on the edge.</summary>
    public const double GestureDefaultConfidence = 0.4;

    /// <summary>The confidence for a gesture monitor: <see cref="GestureDefaultConfidence"/> for the planner's default of 0.5, else the user's choice.</summary>
    public static double GestureConfidence(double requested) => Math.Abs(requested - 0.5) < 1e-9 ? GestureDefaultConfidence : requested;

    /// <summary>The gesture class for a MediaPipe label or the words a person used (case, underscores and punctuation ignored), or null if it isn't one.</summary>
    public static string CanonicalGesture(string text) => Lookup(GestureWords, text, "hand gestures ", "hand gesture ");

    /// <summary>The postures the body-pose agent reports (<see cref="PoseGestures.Names"/>)</summary>
    public static readonly string[] PoseClasses = PoseGestures.Names;

    // The words people use for each posture. Nothing maps to a fall: the model's card rules out life-critical use.
    private static readonly Dictionary<string, string> PoseWords = Words(add =>
    {
        add("arms raised", new[] { "arms raised", "arms up", "raise arms", "raising arms", "raised arms", "both arms up", "both arms raised", "hands up",
            "both hands up", "hands raised", "hands in the air", "arms in the air", "🙌" });
        add("hand raised", new[] { "hand raised", "raised hand", "raise hand", "raise a hand", "raise my hand", "raising a hand", "raising hand",
            "hand in the air", "arm raised", "raise arm", "🙋" });
        add("waving", new[] { "waving", "wave", "waves", "a wave", "wave hello", "waving hello", "wave goodbye", "waving goodbye", "wave hi", "waving hand", "👋" });
        add("standing", new[] { "standing", "stand", "stands", "standing up", "stand up", "stands up", "on their feet" });
        add("sitting", new[] { "sitting", "sit", "sits", "sitting down", "sit down", "sits down", "seated" });
        add("lying down", new[] { "lying down", "lying", "lie down", "lies down", "laying down", "lay down", "lying flat" });
    });

    /// <summary>The posture class for the words a person used (case and punctuation ignored, an agent prefix tolerated), or null if it isn't one.</summary>
    public static string CanonicalPose(string text) => Lookup(PoseWords, text, "body pose ", "pose ");

    // What the request itself says, for when the model's label is no use: Gemma 4 E2B gave "body-pose" (the agent) as the label for "raises their hand" and
    // "person" for "lying down". Only unmistakable phrases count - "stands at the door" is a person, "stands up" a posture - and the first in the text wins.
    private static readonly (string Pose, Regex Pattern)[] PoseRequestPatterns =
    {
        ("arms raised", new Regex(@"\b(both )?(arms|hands) (are |is )?(up|raised|in the air)\b|\braise[sd]? (both |their |his |her |my |your )?(arms|hands)\b", RegexOptions.IgnoreCase)),
        ("hand raised", new Regex(@"\braise[sd]?( up)? (a |one |their |his |her |my |your |the )?(hand|arm)\b|\braising (a |one |their |his |her |my |your |the )?(hand|arm)\b|\b(hand|arm) (raised|in the air)\b", RegexOptions.IgnoreCase)),
        ("waving", new Regex(@"\bwav(e|es|ed|ing)\b", RegexOptions.IgnoreCase)),
        ("lying down", new Regex(@"\b(lying|lies|lie|laying|lays|lay) down\b", RegexOptions.IgnoreCase)),
        ("standing", new Regex(@"\bstand(s|ing)? up\b", RegexOptions.IgnoreCase)),
        ("sitting", new Regex(@"\bsit(s|ting)? down\b", RegexOptions.IgnoreCase))
    };

    /// <summary>The posture a request names in so many words ("take a picture when she raises her hand"), or null.</summary>
    public static string PoseFromRequest(string request) => FirstMatch(PoseRequestPatterns, request);

    /// <summary>
    /// The posture class for a monitor's label: the words it holds if they are one, else - when the label is one the model puts there by mistake
    /// (the person class, an agent's name, anything that isn't a class of any agent) - what the user's request says.
    /// </summary>
    public static string PostureForMonitor(string label, string request)
    {
        string pose = CanonicalPose(label);
        if (pose != null) return pose;
        string normalized = Normalize(label);
        bool mistaken = normalized.Length == 0 || normalized is "person" or "people" or "human" or "body pose" or "hand gesture" or "hand gestures" or "pose" or "body" ||
            !(ObjectClasses.Contains(normalized) || Gestures.Contains(normalized) || FaceClasses.Contains(normalized));
        return mistaken ? PoseFromRequest(request) : null;
    }

    /// <summary>The expressions the face-expression agent reports (<see cref="FaceExpressions.Names"/>)</summary>
    public static readonly string[] FaceClasses = FaceExpressions.Names;

    // The words people use for each expression. Nothing maps to tiredness, attention or who someone is: the model cards rule that out. A blink is too brief
    // for a monitor's hold time, so it isn't "eyes closed".
    private static readonly Dictionary<string, string> ExpressionWords = Words(add =>
    {
        add("smiling", new[] { "smiling", "smile", "smiles", "a smile", "smiling face", "big smile", "grin", "grins", "grinning", "😀", "😃", "😄", "😁", "😊", "🙂" });
        add("eyes closed", new[] { "eyes closed", "closed eyes", "close eyes", "closes eyes", "closing eyes", "eyes shut", "shut eyes", "both eyes closed" });
        add("winking", new[] { "winking", "wink", "winks", "a wink", "😉" });
        add("mouth open", new[] { "mouth open", "open mouth", "opens mouth", "opening mouth", "mouth wide open", "wide open mouth", "jaw dropped", "😮", "😲", "😯" });
        add("eyebrows raised", new[] { "eyebrows raised", "raised eyebrows", "raise eyebrows", "raises eyebrows", "raising eyebrows", "eyebrows up", "brows raised",
            "raised brows", "raise brows" });
    });

    /// <summary>The expression class for the words a person used (case and punctuation ignored, an agent prefix tolerated), or null if it isn't one.</summary>
    public static string CanonicalExpression(string text) => Lookup(ExpressionWords, text, "face expressions ", "face expression ", "face ");

    private const string Whose = "(my |their |his |her |your |the |both )?";

    // What the request itself says, for when the model gives the person class, "face" or an agent's name as the label. The first in the text wins.
    private static readonly (string Expression, Regex Pattern)[] ExpressionRequestPatterns =
    {
        ("smiling", new Regex(@"\bsmil(e|es|ed|ing)\b|\bgrin(s|ned|ning)?\b", RegexOptions.IgnoreCase)),
        ("eyes closed", new Regex(@"\b(close[sd]?|closing|shut(s|ting)?) " + Whose + @"eyes\b|\beyes (are |is )?(closed|shut)\b", RegexOptions.IgnoreCase)),
        ("winking", new Regex(@"\bwink(s|ed|ing)?\b", RegexOptions.IgnoreCase)),
        ("mouth open", new Regex(@"\bopen(s|ed|ing)? " + Whose + @"mouth\b|\bmouth (is )?(wide )?open\b", RegexOptions.IgnoreCase)),
        ("eyebrows raised", new Regex(@"\brais(e|es|ed|ing) " + Whose + @"(eye)?brows\b|\b(eye)?brows (are |go )?(raised|up)\b", RegexOptions.IgnoreCase))
    };

    /// <summary>The expression a request names in so many words ("take a picture when I smile"), or null.</summary>
    public static string ExpressionFromRequest(string request) => FirstMatch(ExpressionRequestPatterns, request);

    /// <summary>
    /// The expression class for a monitor's label: the words it holds if they are one, else - when the label is one the model puts there by mistake
    /// (the person class, "face", an agent's name, anything that isn't a class or posture word of another agent) - what the user's request says.
    /// </summary>
    public static string ExpressionForMonitor(string label, string request)
    {
        string expression = CanonicalExpression(label);
        if (expression != null) return expression;
        string normalized = Normalize(label);
        bool mistaken = normalized.Length == 0 || normalized is "person" or "people" or "human" or "face" or "faces" or "face expression" or "face expressions" or "expression" ||
            !(ObjectClasses.Contains(normalized) || Gestures.Contains(normalized) || CanonicalGesture(normalized) != null || CanonicalPose(normalized) != null);
        return mistaken ? ExpressionFromRequest(request) : null;
    }

    /// <summary>What the holistic agent reports: the hand-and-face interactions and every posture combined with every expression
    /// (<see cref="HolisticGestures"/>). The plain postures and expressions are the body-pose and face-expression agents', which are cheaper.</summary>
    public static readonly string[] HolisticClasses = HolisticGestures.InteractionNames.Concat(HolisticGestures.CombinationNames).ToArray();

    private static readonly Dictionary<string, string> InteractionWords = Words(add =>
    {
        add("covering eyes", new[] { "covering eyes", "cover eyes", "covers eyes", "covered eyes", "eyes covered", "hands over eyes", "hand over eyes", "hiding eyes",
            "peekaboo", "peek a boo", "🙈" });
        add("hand over mouth", new[] { "hand over mouth", "hands over mouth", "covering mouth", "cover mouth", "covers mouth", "mouth covered", "🤭" });
        add("hand on face", new[] { "hand on face", "hands on face", "touching face", "touch face", "touches face", "hand on cheek", "hand on chin", "chin on hand",
            "cheek on hand", "facepalm", "face palm", "🤦" });
    });

    /// <summary>The interaction class for the words a person used ("peekaboo", "touching face"), or null.</summary>
    public static string CanonicalInteraction(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (InteractionWords.TryGetValue(text.Trim(), out string emoji)) return emoji;
        string normalized = Normalize(text);
        if (normalized.StartsWith("holistic ", StringComparison.Ordinal)) normalized = normalized.Substring("holistic ".Length);
        if (InteractionWords.TryGetValue(normalized, out string named)) return named;
        // "my", "their"... between the verb and the body part.
        normalized = Regex.Replace(normalized, @"\b(my|their|his|her|your|the|both|one|a)\b ?", "").Trim();
        return InteractionWords.TryGetValue(normalized, out string interaction) ? interaction : null;
    }

    /// <summary>The combination class ("hand raised and smiling") for a label naming one posture and one expression in either order, or null.</summary>
    public static string CanonicalCombination(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string normalized = Normalize(text.Replace("&", " and ").Replace("+", " and "));
        if (normalized.StartsWith("holistic ", StringComparison.Ordinal)) normalized = normalized.Substring("holistic ".Length);
        string[] parts = Regex.Split(normalized, @"\b(?:and|while|with|plus)\b").Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        if (parts.Length != 2) return null;
        string posture = CanonicalPose(parts[0]) ?? CanonicalPose(parts[1]);
        string expression = CanonicalExpression(parts[0]) ?? CanonicalExpression(parts[1]);
        return posture != null && expression != null ? HolisticGestures.Combine(posture, expression) : null;
    }

    private static readonly (string Interaction, Regex Pattern)[] InteractionRequestPatterns =
    {
        ("covering eyes", new Regex(@"\b(cover(s|ed|ing)?|hid(e|es|ing)) " + Whose + @"eyes\b|\bhands? over " + Whose + @"eyes\b|\bpeek[ -]?a[ -]?boo\b", RegexOptions.IgnoreCase)),
        ("hand over mouth", new Regex(@"\bcover(s|ed|ing)? " + Whose + @"mouth\b|\bhands? (is |are )?over " + Whose + @"mouth\b", RegexOptions.IgnoreCase)),
        ("hand on face", new Regex(@"\btouch(es|ed|ing)? " + Whose + @"face\b|\bhands? (is |are )?on " + Whose + @"(face|cheek|chin)\b|\bface ?palm|\b(chin|cheek) on " + Whose + @"hand\b|\brest(s|ed|ing)? " + Whose + @"(chin|cheek|head) on\b", RegexOptions.IgnoreCase))
    };

    /// <summary>The interaction a request names in so many words ("take a picture when I cover my eyes"), or null.</summary>
    public static string InteractionFromRequest(string request) => FirstMatch(InteractionRequestPatterns, request);

    /// <summary>
    /// The holistic class for a monitor's label: a combination or interaction it names, else - when the request names both a posture and an expression
    /// ("raise your hand and smile") and the label is one of them, or the label is one the model puts there by mistake - what the request says.
    /// Check it before <see cref="ExpressionForMonitor"/> and <see cref="PostureForMonitor"/>, which would take only half of a combination.
    /// </summary>
    public static string HolisticForMonitor(string label, string request)
    {
        string named = CanonicalCombination(label) ?? CanonicalInteraction(label);
        if (named != null) return named;
        string normalized = Normalize(label);
        bool mistaken = normalized.Length == 0 || normalized is "person" or "people" or "human" or "face" or "body" or "hands" or "hand" or "holistic" ||
            !(ObjectClasses.Contains(normalized) || Gestures.Contains(normalized) || CanonicalGesture(normalized) != null ||
              CanonicalPose(normalized) != null || CanonicalExpression(normalized) != null);
        string posture = PoseFromRequest(request), expression = ExpressionFromRequest(request);
        if (posture != null && expression != null &&
            (mistaken || CanonicalPose(normalized) == posture || CanonicalExpression(normalized) == expression))
            return HolisticGestures.Combine(posture, expression);
        return mistaken ? InteractionFromRequest(request) : null;
    }

    private static Dictionary<string, string> Words(Action<Action<string, string[]>> build)
    {
        var words = new Dictionary<string, string>();
        build((name, phrases) => { foreach (string phrase in phrases) words[phrase] = name; });
        return words;
    }

    // Lower case, letters and digits only, single spaces.
    private static string Normalize(string text) => string.Join(" ", new string((text ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
        .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

    // The class for the text as it is (an emoji) or normalized, with an agent's name in front tolerated (a model sometimes joins the agent's id and the
    // class: "hand-gesture.thumb up").
    private static string Lookup(Dictionary<string, string> words, string text, params string[] prefixes)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string trimmed = text.Trim();
        if (words.TryGetValue(trimmed, out string emoji)) return emoji;
        string normalized = Normalize(trimmed);
        foreach (string prefix in prefixes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)) { normalized = normalized.Substring(prefix.Length); break; }
        return words.TryGetValue(normalized, out string name) ? name : null;
    }

    private static string FirstMatch((string Name, Regex Pattern)[] patterns, string request)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;
        string best = null;
        int position = int.MaxValue;
        foreach (var (name, pattern) in patterns)
        {
            var match = pattern.Match(request);
            if (match.Success && match.Index < position) { best = name; position = match.Index; }
        }
        return best;
    }
}
