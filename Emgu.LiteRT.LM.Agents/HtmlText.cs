//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// The readable text of a web page, for the read_web_page tool (AgentTools.ReadWebPage): its title and main text,
    /// without scripts, styles, navigation, headers, footers, asides and forms. The main text is the page's &lt;article&gt;
    /// or &lt;main&gt; element when it has one, otherwise its paragraphs, headings and list items of some length - menus and
    /// link lists are short. Block elements become line breaks; entities are decoded.
    /// </summary>
    public static class HtmlText
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        // A tag's attributes, which may contain '>' inside quotes (e.g. Wikipedia's JSON data-mw attributes).
        private const string Attributes = @"(?:[^>""']|""[^""]*""|'[^']*')*";

        private static readonly Regex Comments = new Regex(@"<!--.*?-->", Options, Timeout);
        private static readonly Regex Removed = new Regex(
            @"<(script|style|noscript|svg|template|iframe|nav|header|footer|aside|form|button|select)\b" + Attributes + @">.*?</\1\s*>",
            Options, Timeout);
        // Innermost tables first (nested tables are removed from the inside out).
        private static readonly Regex Tables = new Regex(@"<table\b" + Attributes + @">(?:(?!<table\b).)*?</table\s*>", Options, Timeout);
        private static readonly Regex Title = new Regex(@"<title\b" + Attributes + @">(.*?)</title\s*>", Options, Timeout);
        private static readonly Regex Main = new Regex(@"<(article|main)\b" + Attributes + @">(.*)</\1\s*>", Options, Timeout);
        private static readonly Regex Body = new Regex(@"<body\b" + Attributes + @">(.*)</body\s*>", Options, Timeout);
        private static readonly Regex Blocks = new Regex(@"<(p|h[1-6]|li|blockquote|pre|td|th|dt|dd)\b" + Attributes + @">(.*?)</\1\s*>", Options, Timeout);
        private static readonly Regex BlockBreaks = new Regex(
            @"</?(p|div|section|article|main|h[1-6]|li|ul|ol|blockquote|pre|tr|table|br|hr|dt|dd|figcaption)\b" + Attributes + @">", Options, Timeout);
        private static readonly Regex Tags = new Regex(@"<" + Attributes + @">", Options, Timeout);
        // Citation markers: [1], [a], [citation needed], [note 3].
        private static readonly Regex Citations = new Regex(@"\[\s*(?:\d+|[a-z]|citation needed|note \d+|edit)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
        private static readonly Regex Spaces = new Regex(@"[ \t\f\v ]+", RegexOptions.CultureInvariant, Timeout);
        private static readonly Regex BlankLines = new Regex(@"\n\s*\n+", RegexOptions.CultureInvariant, Timeout);

        /// <summary>
        /// The title and main text of an HTML page.
        /// </summary>
        /// <param name="html">The page's HTML</param>
        /// <param name="title">The page's title, or null</param>
        /// <returns>The text: paragraphs separated by empty lines</returns>
        public static string Extract(string html, out string title)
        {
            title = null;
            if (string.IsNullOrEmpty(html))
                return string.Empty;
            try
            {
                Match titleMatch = Title.Match(html);
                if (titleMatch.Success)
                    title = Clean(titleMatch.Groups[1].Value).Replace('\n', ' ').Trim();
                if (string.IsNullOrEmpty(title))
                    title = null;

                string withTables = Removed.Replace(Comments.Replace(html, " "), " ");
                // Tables (infoboxes, navigation boxes, data) only if the page has little text without them.
                string content = withTables;
                for (int i = 0; i < 10; i++)
                {
                    string next = Tables.Replace(content, "\n");
                    if (next.Length == content.Length)
                        break;
                    content = next;
                }
                if (Clean(content).Length < 200)
                    content = withTables;
                Match main = Main.Match(content);
                if (main.Success && Clean(main.Groups[2].Value).Length >= 200)
                    return Clean(main.Groups[2].Value);

                // No article element: the paragraphs, headings and list items with some text in them.
                Match body = Body.Match(content);
                string bodyHtml = body.Success ? body.Groups[1].Value : content;
                List<string> blocks = new List<string>();
                foreach (Match block in Blocks.Matches(bodyHtml))
                {
                    string text = Clean(block.Groups[2].Value);
                    bool heading = block.Groups[1].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase) && block.Groups[1].Value.Length == 2;
                    if (text.Length >= (heading ? 3 : 40))
                        blocks.Add(text);
                }
                string paragraphs = string.Join("\n\n", blocks.Distinct());
                return paragraphs.Length >= 200 ? paragraphs : Clean(bodyHtml);
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological page: just strip the tags.
                return Clean(html);
            }
        }

        // Block elements become line breaks, other tags disappear, entities are decoded, white space collapses.
        private static string Clean(string html)
        {
            string text = BlockBreaks.Replace(html, "\n");
            text = Tags.Replace(text, " ");
            text = WebUtility.HtmlDecode(text).Replace("\u200B", "").Replace("\uFEFF", "");
            text = Citations.Replace(text, "");
            text = Spaces.Replace(text, " ");
            StringBuilder lines = new StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                lines.Append(trimmed).Append('\n');
            }
            return BlankLines.Replace(lines.ToString(), "\n\n").Trim();
        }
    }
}
