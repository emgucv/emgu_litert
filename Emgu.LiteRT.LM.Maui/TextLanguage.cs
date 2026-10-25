//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The language of a text, as a two-letter code ("es", "zh"), or null when it isn't known. A script check
    /// (<see cref="ReplySpeechText.DetectScriptLanguage"/>) settles Chinese, Japanese, Korean, Russian, Arabic and the other
    /// non-Latin scripts; Latin script - Spanish, French, German, English... - which it can't tell apart, goes to
    /// <see cref="IdentifyLatin"/> if the app set it, else to Apple's NaturalLanguage recognizer on iOS and Mac Catalyst (nothing to
    /// ship). Android and Windows have no built-in identifier: an app can set <see cref="IdentifyLatin"/> to e.g. ML Kit's language
    /// identification. <see cref="TextToSpeechReplySpeaker"/> uses it to choose a voice.
    /// </summary>
    public static class TextLanguage
    {
        /// <summary>
        /// Identifies Latin-script text's language (a language tag such as "es" or "zh-Latn", or null if unsure) instead of the
        /// platform; or null.
        /// </summary>
        public static Func<string, Task<string>> IdentifyLatin { get; set; }

        /// <summary>The text's language as a two-letter code, or null when it isn't known.</summary>
        public static async Task<string> DetectAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            string script = ReplySpeechText.DetectScriptLanguage(text);
            if (script != null)
                return script;
            Func<string, Task<string>> identify = IdentifyLatin;
            if (identify != null)
            {
                try
                {
                    return ReplySpeechText.TwoLetterLanguage(await identify(text));
                }
                catch (Exception)
                {
                    return null; // The voice falls back to the device's default.
                }
            }
#if IOS || MACCATALYST
            return DetectWithApple(text);
#else
            return null;
#endif
        }

#if IOS || MACCATALYST
        // Apple's NaturalLanguage recognizer: the likeliest language if it is at least 60% sure.
        private static string DetectWithApple(string text)
        {
            try
            {
                using var recognizer = new NaturalLanguage.NLLanguageRecognizer();
                recognizer.Process(text);
                var best = recognizer.GetNativeLanguageHypotheses(1);
                foreach (var pair in best)
                    if (pair.Value is Foundation.NSNumber probability && probability.DoubleValue >= 0.6)
                        return ReplySpeechText.TwoLetterLanguage(pair.Key.ToString());
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }
#endif
    }
}
