//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Chat;

/// <summary>What says text aloud (Emgu.LiteRT.LM.Maui's <c>ChatVoice</c>): replies, the voice chat, alerts, counts.</summary>
public enum VoiceEngine
{
    /// <summary>The platform's text to speech, with the device's voices.</summary>
    System,
    /// <summary>Kokoro 82M on LiteRT-LM, on the device (Emgu.LiteRT.LM.Models' <c>Kokoro82M</c>).</summary>
    Kokoro
}

/// <summary>
/// Choosing what says text aloud: Kokoro when it was chosen and speaks the text's language (9 languages and dialects),
/// else the platform's voices; and the settings that keep the choice and a Kokoro voice per language.
/// </summary>
public static class VoiceEngines
{
    /// <summary>The settings key of the choice ("system" or "kokoro"; absent: the platform's).</summary>
    public const string SettingKey = "voice_engine";

    /// <summary>The settings key of the Kokoro voice chosen for a language (BCP-47, as <see cref="KokoroLanguage"/> gives it).</summary>
    public static string VoiceSettingKey(string kokoroLanguage) => "kokoro_voice_" + kokoroLanguage;

    /// <summary>Kokoro's languages, BCP-47 as LiteRT-LM names them, with their names to show.</summary>
    public static readonly IReadOnlyList<(string Tag, string Name)> KokoroLanguages = new[]
    {
        ("en-US", "American English"), ("en-GB", "British English"), ("es", "Spanish"), ("fr", "French"),
        ("it", "Italian"), ("pt-BR", "Brazilian Portuguese"), ("hi", "Hindi"), ("zh-CN", "Mandarin Chinese"),
        ("ja", "Japanese"),
    };

    /// <summary>
    /// The Kokoro language for a text's language, or null when Kokoro doesn't speak it: English is British English on a
    /// device in the United Kingdom, Ireland, Australia or New Zealand and American English elsewhere; Chinese is
    /// Mandarin; Portuguese is Brazilian.
    /// </summary>
    /// <param name="language">The text's language (two-letter code, or a BCP-47 tag), or null for the device's</param>
    /// <param name="deviceRegion">The device's region (two letters), or null</param>
    /// <param name="deviceLanguage">The device's language (two letters), used when language is null</param>
    public static string KokoroLanguage(string language, string deviceRegion, string deviceLanguage = null)
    {
        string tag = string.IsNullOrWhiteSpace(language) ? deviceLanguage : language;
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        string code = tag.Split('-', '_')[0].ToLowerInvariant();
        switch (code)
        {
            case "en":
                string region = tag.Contains("-") || tag.Contains("_") ? tag.Substring(3).ToUpperInvariant() : (deviceRegion ?? string.Empty).ToUpperInvariant();
                return region == "GB" || region == "UK" || region == "IE" || region == "AU" || region == "NZ" ? "en-GB" : "en-US";
            case "es": return "es";
            case "fr": return "fr";
            case "it": return "it";
            case "pt": return "pt-BR";
            case "hi": return "hi";
            case "zh": return "zh-CN";
            case "ja": return "ja";
            default: return null;
        }
    }

    /// <summary>
    /// What says a text aloud: Kokoro when it was chosen, it is here, and it speaks the text's language; else the
    /// platform's voices.
    /// </summary>
    /// <param name="choice">The user's choice</param>
    /// <param name="kokoroAvailable">Whether Kokoro can run here (its native library is in the app)</param>
    /// <param name="kokoroLanguage">The text's Kokoro language (<see cref="KokoroLanguage"/>), or null</param>
    public static VoiceEngine Choose(VoiceEngine choice, bool kokoroAvailable, string kokoroLanguage) =>
        choice == VoiceEngine.Kokoro && kokoroAvailable && kokoroLanguage != null ? VoiceEngine.Kokoro : VoiceEngine.System;

    /// <summary>The choice in a setting's value: Kokoro for "kokoro", else the platform's.</summary>
    public static VoiceEngine Parse(string value) =>
        string.Equals(value, "kokoro", StringComparison.OrdinalIgnoreCase) ? VoiceEngine.Kokoro : VoiceEngine.System;

    /// <summary>A choice's setting value.</summary>
    public static string ToSetting(VoiceEngine engine) => engine == VoiceEngine.Kokoro ? "kokoro" : "system";

    // Sentence ends: . ! ? … and the CJK ones, with what closes them (quotes, brackets).
    private static readonly Regex SentenceEnd = new Regex(@"(?<=[.!?…。！？]['""”’)\]]*)\s+|(?<=[。！？])", RegexOptions.Compiled);

    /// <summary>A text's sentences, in order, without empty ones.</summary>
    public static IReadOnlyList<string> Sentences(string speech) =>
        SentenceEnd.Split(speech ?? string.Empty).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>
    /// About how much text one of Kokoro's runs holds (its frames are a fixed size, about 10 s of speech, and every run
    /// costs about the same): 120, counting Chinese, Japanese and Korean characters twice.
    /// </summary>
    public const int PieceWeight = 120;

    /// <summary>
    /// The pieces Kokoro makes a text in: the first sentence alone (the sooner it is made, the sooner the speech
    /// starts), then the next sentences together, up to <see cref="PieceWeight"/> - fewer runs and fewer player starts
    /// than a piece per sentence. A longer sentence is a piece of its own; Kokoro splits it itself.
    /// </summary>
    public static IReadOnlyList<string> SpeechPieces(string speech)
    {
        IReadOnlyList<string> sentences = Sentences(speech);
        var pieces = new List<string>();
        var current = new StringBuilder();
        int weight = 0;
        foreach (string sentence in sentences)
        {
            int w = Weight(sentence);
            if (current.Length > 0 && (pieces.Count == 0 || weight + w > PieceWeight))
            {
                pieces.Add(current.ToString());
                current.Clear();
                weight = 0;
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
            weight += w;
        }
        if (current.Length > 0)
            pieces.Add(current.ToString());
        return pieces;
    }

    private static int Weight(string text) =>
        text.Sum(c => (c >= '\u3040' && c <= '\u9fff') || (c >= '\uac00' && c <= '\ud7af') ? 2 : 1);
}
