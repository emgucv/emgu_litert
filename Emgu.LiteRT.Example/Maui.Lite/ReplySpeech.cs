//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Reads a chat reply aloud with a voice for its language. TextToSpeech otherwise uses the device's default voice,
    /// so e.g. a Chinese reply on an English device was read with an English voice. The language is guessed from the
    /// writing system most of the reply's letters use, and the Markdown the models write (**bold**, lists, code) is
    /// removed so it isn't read out.
    /// </summary>
    internal static class ReplySpeech
    {
        private static IReadOnlyList<Locale> _locales;

        public static async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            string speech = CleanForSpeech(text);
            if (speech.Length == 0)
                return;
            Locale locale = await ChooseLocaleAsync(DetectLanguage(speech));
            await TextToSpeech.Default.SpeakAsync(speech, new SpeechOptions { Locale = locale }, cancellationToken);
        }

        /// <summary>
        /// Remove Markdown formatting, links' addresses and emoji, keeping the words.
        /// </summary>
        public static string CleanForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string s = Regex.Replace(text, @"```.*?```", " ", RegexOptions.Singleline);  // code blocks
            s = Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1");                          // [text](url) -> text
            s = Regex.Replace(s, @"https?://\S+", " ");                                     // bare URLs
            s = Regex.Replace(s, @"^\s{0,3}(#{1,6}|>|[-*+]|\d+[.)])\s+", "", RegexOptions.Multiline); // headings, quotes, list markers
            s = Regex.Replace(s, @"[*_`~|]+", "");                                          // emphasis, code, tables
            StringBuilder result = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                // Drop emoji and other symbols outside the Basic Multilingual Plane, and the BMP's pictographs.
                if (char.IsSurrogate(s[i]))
                    continue;
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(s[i]);
                if (category == UnicodeCategory.OtherSymbol)
                    continue;
                result.Append(s[i]);
            }
            // A pause between lines (e.g. list items): end a line that has no punctuation with a full stop.
            s = Regex.Replace(result.ToString(), @"([^\s\p{P}])[ \t]*\r?\n", "$1.\n");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>
        /// The language (two-letter code) of the writing system most of the text's letters use, or null for Latin
        /// script (and anything else this doesn't know), which the device's default voice reads.
        /// </summary>
        public static string DetectLanguage(string text)
        {
            int han = 0, kana = 0, hangul = 0, cyrillic = 0, arabic = 0, hebrew = 0, greek = 0, thai = 0, devanagari = 0, latin = 0;
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF || c >= 0xF900 && c <= 0xFAFF) han++;
                else if (c >= 0x3040 && c <= 0x30FF) kana++;
                else if (c >= 0xAC00 && c <= 0xD7AF || c >= 0x1100 && c <= 0x11FF) hangul++;
                else if (c >= 0x0400 && c <= 0x04FF) cyrillic++;
                else if (c >= 0x0600 && c <= 0x06FF) arabic++;
                else if (c >= 0x0590 && c <= 0x05FF) hebrew++;
                else if (c >= 0x0370 && c <= 0x03FF) greek++;
                else if (c >= 0x0E00 && c <= 0x0E7F) thai++;
                else if (c >= 0x0900 && c <= 0x097F) devanagari++;
                else if (c < 0x0250 && char.IsLetter(c)) latin++;
            }
            // Japanese mixes kanji (Han) with kana; any kana means Japanese.
            if (kana > 0 && kana + han >= latin)
                return "ja";
            var scripts = new (string Language, int Count)[]
            {
                ("zh", han), ("ko", hangul), ("ru", cyrillic), ("ar", arabic), ("he", hebrew), ("el", greek),
                ("th", thai), ("hi", devanagari)
            };
            var top = scripts.OrderByDescending(s => s.Count).First();
            // A CJK character carries about as much as a word of Latin letters, so compare it with a third of them.
            int latinWeight = top.Language == "zh" || top.Language == "ko" ? latin / 3 : latin;
            return top.Count > 0 && top.Count >= latinWeight ? top.Language : null;
        }

        /// <summary>
        /// A voice for the language, preferring the device's own region (e.g. zh-TW on a Taiwanese device); null for the
        /// device's default voice.
        /// </summary>
        private static async Task<Locale> ChooseLocaleAsync(string language)
        {
            CultureInfo ui = CultureInfo.CurrentUICulture;
            if (language == null)
            {
                // Latin text: the default voice, unless the device's own language uses another script (e.g. a Chinese
                // device reading an English reply), then an English voice.
                if (DetectLanguage(ui.NativeName) == null)
                    return null;
                language = "en";
            }
            IReadOnlyList<Locale> locales = await GetLocalesAsync();
            List<Locale> matches = locales.Where(l => SameLanguage(l, language)).ToList();
            if (matches.Count == 0)
                return null;
            string region = RegionOf(ui);
            Locale sameRegion = SameLanguage(ui.TwoLetterISOLanguageName, language)
                ? matches.FirstOrDefault(l => string.Equals(l.Country, region, StringComparison.OrdinalIgnoreCase))
                : null;
            if (sameRegion != null)
                return sameRegion;
            // Otherwise the main variant: zh-CN, en-US.
            string preferred = language == "zh" ? "CN" : language == "en" ? "US" : null;
            return matches.FirstOrDefault(l => preferred != null && string.Equals(l.Country, preferred, StringComparison.OrdinalIgnoreCase))
                ?? matches[0];
        }

        private static async Task<IReadOnlyList<Locale>> GetLocalesAsync()
        {
            if (_locales == null)
            {
                try
                {
                    _locales = (await TextToSpeech.Default.GetLocalesAsync()).ToList();
                }
                catch (Exception)
                {
                    _locales = Array.Empty<Locale>();
                }
            }
            return _locales;
        }

        // Locale.Language is a two-letter code on most platforms but may include the region (e.g. "zh-CN").
        private static bool SameLanguage(Locale locale, string language) => SameLanguage(locale.Language, language);

        private static bool SameLanguage(string code, string language) =>
            !string.IsNullOrEmpty(code)
            && (string.Equals(code, language, StringComparison.OrdinalIgnoreCase)
                || code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase));

        private static string RegionOf(CultureInfo culture)
        {
            try
            {
                return new RegionInfo(culture.Name).TwoLetterISORegionName;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

#endif
