//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;

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
            Locale locale = await ChooseLocaleAsync(await TextLanguage.DetectAsync(speech));
            cancellationToken.ThrowIfCancellationRequested();
            await TextToSpeech.Default.SpeakAsync(speech, new SpeechOptions { Locale = locale }, cancellationToken);
        }

        /// <summary>
        /// Remove Markdown formatting, links' addresses and emoji, keeping the words.
        /// </summary>
        public static string CleanForSpeech(string text) => ReplySpeechText.CleanForSpeech(text);

        /// <summary>
        /// The language (two-letter code) of the writing system most of the text's letters use, or null for Latin
        /// script (and anything else this doesn't know), which the device's default voice reads.
        /// </summary>
        public static string DetectLanguage(string text) => ReplySpeechText.DetectScriptLanguage(text);

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
