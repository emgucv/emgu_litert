//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// Prepares a reply's text to be read aloud: removes the Markdown models write (so "**" and "#" aren't spoken), links'
    /// addresses, code blocks and emoji, turns maths into words a speech engine can read (x², √2), and guesses the
    /// language from the writing system, so a voice for it can be chosen (the default voice read e.g. Chinese with an
    /// English voice).
    /// </summary>
    public static class ReplySpeechText
    {
        /// <summary>
        /// Remove Markdown formatting, code blocks, links' addresses and emoji, keeping the words; maths becomes Unicode
        /// text, and a line without closing punctuation (e.g. a list item) ends with a full stop for a pause.
        /// </summary>
        /// <param name="text">The reply's text</param>
        /// <returns>The text to speak; empty if there is nothing to say</returns>
        public static string CleanForSpeech(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string s = Regex.Replace(text, @"```.*?```", " ", RegexOptions.Singleline);  // code blocks
            // Maths as text (x², √2, ≤) rather than its LaTeX: display maths (possibly over lines), then inline.
            s = Regex.Replace(s, @"\$\$(.+?)\$\$|\\\[(.+?)\\\]",
                m => " " + MathText.ToUnicode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) + ". ", RegexOptions.Singleline);
            s = ReplyMarkdown.WithMath(s);
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
                if (CharUnicodeInfo.GetUnicodeCategory(s[i]) == UnicodeCategory.OtherSymbol)
                    continue;
                result.Append(s[i]);
            }
            // A pause between lines (e.g. list items): end a line that has no punctuation with a full stop.
            s = Regex.Replace(result.ToString(), @"([^\s\p{P}])[ \t]*\r?\n", "$1.\n");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>
        /// A language tag as a two-letter code: ML Kit's "zh-Latn" (romanized Chinese) is "zh", Apple's "zh-Hans" is "zh";
        /// null for "und" (undetermined) and for nothing.
        /// </summary>
        /// <param name="tag">The language tag</param>
        public static string TwoLetterLanguage(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || tag == "und")
                return null;
            int dash = tag.IndexOf('-');
            return (dash > 0 ? tag.Substring(0, dash) : tag).ToLowerInvariant();
        }

        /// <summary>
        /// The language (two-letter code) of the writing system most of the text's letters use, or null for Latin script
        /// (and anything else this doesn't know), whose language this can't tell.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>"zh", "ja", "ko", "ru", "ar", "he", "el", "th", "hi" or null</returns>
        public static string DetectScriptLanguage(string text)
        {
            int han = 0, kana = 0, hangul = 0, cyrillic = 0, arabic = 0, hebrew = 0, greek = 0, thai = 0, devanagari = 0, latin = 0;
            foreach (char c in text ?? string.Empty)
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
    }
}
