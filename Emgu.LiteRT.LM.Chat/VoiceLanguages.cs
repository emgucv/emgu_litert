//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.LM.Chat;

/// <summary>
/// Languages for a speech recognizer that writes down voice messages (Emgu.LiteRT.LM.Maui's <c>SpeechRecognizer</c>),
/// e.g. for a language setting: the recognizer listens in the device's language unless told otherwise, so Chinese
/// speech wasn't recognized on an English phone.
/// </summary>
public static class VoiceLanguages
{
    /// <summary>BCP-47 tags with the names people know them by (in the language itself, so they can be found).</summary>
    public static readonly IReadOnlyList<(string Tag, string Name)> All = new (string, string)[]
    {
        ("en-US", "English"),
        ("zh-CN", "中文 (普通话)"),
        ("zh-TW", "中文 (繁體)"),
        ("yue-HK", "粵語"),
        ("ja-JP", "日本語"),
        ("ko-KR", "한국어"),
        ("es-ES", "Español"),
        ("fr-FR", "Français"),
        ("de-DE", "Deutsch"),
        ("it-IT", "Italiano"),
        ("pt-BR", "Português"),
        ("ru-RU", "Русский"),
        ("hi-IN", "हिन्दी"),
        ("ar-SA", "العربية"),
        ("tr-TR", "Türkçe"),
        ("nl-NL", "Nederlands"),
        ("pl-PL", "Polski"),
        ("vi-VN", "Tiếng Việt"),
        ("id-ID", "Bahasa Indonesia"),
        ("th-TH", "ไทย"),
    };

    /// <summary>The name of a language tag, or the tag itself if it isn't in the list.</summary>
    public static string NameOf(string tag)
    {
        foreach (var language in All)
            if (string.Equals(language.Tag, tag, StringComparison.OrdinalIgnoreCase))
                return language.Name;
        return tag;
    }

    /// <summary>
    /// The language to recognize: the one chosen, else the device's. Android's tags use a dash ("zh-CN"); a device can report
    /// an underscore or a script ("zh-Hans-CN"), made plain: language and region.
    /// </summary>
    public static string Resolve(string chosen, string device)
    {
        string tag = string.IsNullOrWhiteSpace(chosen) ? device : chosen;
        if (string.IsNullOrWhiteSpace(tag))
            return "en-US";
        string[] parts = tag.Trim().Replace('_', '-').Split('-');
        string language = parts[0].ToLowerInvariant();
        // The region is the two-letter or three-digit part (not a four-letter script such as Hans or Hant).
        string region = parts.Skip(1).FirstOrDefault(p => p.Length == 2 || (p.Length == 3 && p.All(char.IsDigit)));
        // Hant (traditional) without a region: Taiwan.
        if (region == null && parts.Skip(1).Any(p => string.Equals(p, "Hant", StringComparison.OrdinalIgnoreCase)) && language == "zh")
            region = "TW";
        // Android's recognizers list Mandarin as "cmn-Hans-CN" / "cmn-Hant-TW": the zh-CN / zh-TW of the picker.
        if (language == "cmn")
            language = "zh";
        return region == null ? language : language + "-" + region.ToUpperInvariant();
    }

    /// <summary>
    /// Whether the tag is among the languages a recognizer lists: the same tag, or - for a tag with no region - any region of
    /// the language. Case and dash/underscore don't matter.
    /// </summary>
    public static bool IsListed(IEnumerable<string> listed, string tag)
    {
        if (listed == null || string.IsNullOrWhiteSpace(tag))
            return false;
        string wanted = Resolve(tag, null);
        bool noRegion = wanted.IndexOf('-') < 0;
        foreach (string item in listed)
        {
            string other = Resolve(item, null);
            if (string.Equals(other, wanted, StringComparison.OrdinalIgnoreCase))
                return true;
            if (noRegion && string.Equals(other.Split('-')[0], wanted, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
