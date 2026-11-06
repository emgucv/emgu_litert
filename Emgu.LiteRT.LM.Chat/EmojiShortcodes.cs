//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// Turns emoji shortcodes (":slightly_smiling_face:", ":+1:", as Slack, GitHub and Discord write them, and as text
    /// copied from them arrives) into the emoji. The names are iamcal/emoji-data's, aliases included
    /// (tools/make_emoji_shortcodes.py makes the table); a name that isn't in it (":partyparrot:", a workspace's own
    /// emoji, or the ":30:" of a time) is left as written.
    /// </summary>
    public static class EmojiShortcodes
    {
        // A code fence or an inline code span (left as written), or a shortcode.
        private static readonly Regex Pattern = new Regex(@"(```.*?```|`[^`\r\n]*`)|:([a-z0-9_+\-]{1,64}):",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Lazy<Dictionary<string, string>> Table = new Lazy<Dictionary<string, string>>(Load);

        private static Dictionary<string, string> Load()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using (Stream resource = typeof(EmojiShortcodes).Assembly.GetManifestResourceStream("emoji_shortcodes.tsv.gz"))
                {
                    if (resource == null)
                        return table;
                    using (var gzip = new GZipStream(resource, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gzip, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int tab = line.IndexOf('\t');
                            if (tab > 0)
                                table[line.Substring(0, tab)] = line.Substring(tab + 1);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // No emoji table: the text stays as written.
            }
            return table;
        }

        /// <summary>
        /// Replace the shortcodes it knows with their emoji. Code blocks and inline code are left alone; a name it doesn't
        /// know, and a shortcode that isn't closed yet (a reply still streaming: ":smi"), stay as written.
        /// </summary>
        /// <param name="text">The text: a message the user wrote, or a model's reply (all of it so far, not a part)</param>
        /// <returns>The text with the emoji; the same text when there is nothing to replace; empty for null</returns>
        public static string Replace(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            if (text.IndexOf(':') < 0)
                return text;
            Dictionary<string, string> table = Table.Value;
            if (table.Count == 0)
                return text;

            StringBuilder result = null;
            int copied = 0;
            int at = 0;
            while (at < text.Length)
            {
                Match m = Pattern.Match(text, at);
                if (!m.Success)
                    break;
                if (m.Groups[1].Success)
                {
                    at = m.Index + m.Length;
                    continue;
                }
                string name = m.Groups[2].Value;
                if (table.TryGetValue(name.ToLowerInvariant(), out string emoji))
                {
                    result ??= new StringBuilder(text.Length);
                    result.Append(text, copied, m.Index - copied).Append(emoji);
                    copied = at = m.Index + m.Length;
                }
                else
                {
                    // Not an emoji: its closing colon may open the next one (":foo:smile:").
                    at = m.Index + m.Length - 1;
                }
            }
            if (result == null)
                return text;
            return result.Append(text, copied, text.Length - copied).ToString();
        }
    }
}
