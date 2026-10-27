//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// The kind of a block of a reply.
    /// </summary>
    public enum ReplyBlockKind
    {
        /// <summary>Text with Markdown formatting (paragraphs, lists, headings...)</summary>
        Text,
        /// <summary>A fenced code block (```lang ... ```)</summary>
        Code,
        /// <summary>Display maths on lines of its own ($$ ... $$ or \[ ... \]), as LaTeX</summary>
        Math,
        /// <summary>A table: a header row, a separator row (| --- | :---: |) and the rows, each cell still with inline Markdown</summary>
        Table
    }

    /// <summary>
    /// How a table's column is aligned, from its separator row: ":---" left (the default), ":---:" centered, "---:" right.
    /// </summary>
    public enum ReplyTableAlignment
    {
        /// <summary>Left (":---" or "---")</summary>
        Left,
        /// <summary>Centered (":---:")</summary>
        Center,
        /// <summary>Right ("---:")</summary>
        Right
    }

    /// <summary>
    /// A block of a reply: Markdown text, a code block, display maths or a table.
    /// </summary>
    public sealed class ReplyBlock
    {
        private static readonly IReadOnlyList<string> NoCells = new string[0];
        private static readonly IReadOnlyList<IReadOnlyList<string>> NoRows = new IReadOnlyList<string>[0];
        private static readonly IReadOnlyList<ReplyTableAlignment> NoAlignments = new ReplyTableAlignment[0];

        internal ReplyBlock(ReplyBlockKind kind, string text, string language)
        {
            Kind = kind;
            Text = text;
            Language = language ?? string.Empty;
            TableHeader = NoCells;
            TableRows = NoRows;
            TableAlignments = NoAlignments;
        }

        internal ReplyBlock(string text, IReadOnlyList<string> header, IReadOnlyList<ReplyTableAlignment> alignments, IReadOnlyList<IReadOnlyList<string>> rows)
            : this(ReplyBlockKind.Table, text, null)
        {
            TableHeader = header;
            TableAlignments = alignments;
            TableRows = rows;
        }

        /// <summary>A table's header cells (with inline Markdown); empty for other blocks.</summary>
        public IReadOnlyList<string> TableHeader { get; }

        /// <summary>A table's rows, each with as many cells as the header (missing ones empty, extra ones dropped); empty for other blocks.</summary>
        public IReadOnlyList<IReadOnlyList<string>> TableRows { get; }

        /// <summary>A table's columns' alignment, one per header cell; empty for other blocks.</summary>
        public IReadOnlyList<ReplyTableAlignment> TableAlignments { get; }

        /// <summary>The kind of block.</summary>
        public ReplyBlockKind Kind { get; }

        /// <summary>The Markdown text, the code, the LaTeX of the maths (without its delimiters), or a table's Markdown.</summary>
        public string Text { get; }

        /// <summary>A code block's language as written after the fence (e.g. "python"); empty if none, and for other blocks.</summary>
        public string Language { get; }
    }

    /// <summary>
    /// The kind of a line of Markdown text.
    /// </summary>
    public enum ReplyLineKind
    {
        /// <summary>An ordinary line</summary>
        Plain,
        /// <summary>A heading (# ... ######)</summary>
        Heading,
        /// <summary>A horizontal rule (---, ***, ___)</summary>
        Rule,
        /// <summary>A bullet list item (-, * or +)</summary>
        Bullet,
        /// <summary>A numbered list item (1. or 1))</summary>
        Numbered,
        /// <summary>A quote (&gt;)</summary>
        Quote
    }

    /// <summary>
    /// A line of Markdown text, with its list marker or heading level taken off.
    /// </summary>
    public sealed class ReplyLine
    {
        internal ReplyLine(ReplyLineKind kind, string content, int headingLevel = 0, string indent = "", string marker = "")
        {
            Kind = kind;
            Content = content;
            HeadingLevel = headingLevel;
            Indent = indent;
            Marker = marker;
        }

        /// <summary>The kind of line.</summary>
        public ReplyLineKind Kind { get; }

        /// <summary>The text after the marker, still with inline Markdown (see <see cref="ReplyMarkdown.ParseInline"/>); empty for a rule.</summary>
        public string Content { get; }

        /// <summary>For a heading, its level: 1 for #, 2 for ##, ...</summary>
        public int HeadingLevel { get; }

        /// <summary>For a list item, the spaces to show before its marker (three per nesting level).</summary>
        public string Indent { get; }

        /// <summary>For a list item, the marker to show: "•" for a bullet, the number for a numbered item ("2.").</summary>
        public string Marker { get; }
    }

    /// <summary>
    /// The kind of a run of inline text.
    /// </summary>
    public enum ReplyRunKind
    {
        /// <summary>Plain text</summary>
        Text,
        /// <summary>`inline code`</summary>
        Code,
        /// <summary>**bold** or __bold__</summary>
        Bold,
        /// <summary>*italic* or _italic_</summary>
        Italic,
        /// <summary>Inline maths ($...$, \(...\), $$...$$ or \[...\]), already converted to Unicode text</summary>
        Math,
        /// <summary>A link, [text](url)</summary>
        Link
    }

    /// <summary>
    /// A run of inline text, ready to show: its markup is removed and maths in it is converted to Unicode text.
    /// </summary>
    public sealed class ReplyRun
    {
        internal ReplyRun(ReplyRunKind kind, string text, string url = null)
        {
            Kind = kind;
            Text = text;
            Url = url;
        }

        /// <summary>The kind of run.</summary>
        public ReplyRunKind Kind { get; }

        /// <summary>The text to show.</summary>
        public string Text { get; }

        /// <summary>For a link, its address as written (not checked: open only http and https addresses).</summary>
        public string Url { get; }
    }

    /// <summary>
    /// Reads the Markdown that language models write in their replies, for a UI to show: the blocks of a reply
    /// (text, fenced code blocks, display maths), the lines of a text block (headings, lists, quotes, rules) and the
    /// runs of a line (bold, italic, inline code, links, maths). LaTeX maths becomes Unicode text (<see cref="MathText"/>).
    /// It works on text that is still being streamed: an unclosed code block or display maths runs to the end, and
    /// unclosed inline markup is kept as it is.
    /// </summary>
    public static class ReplyMarkdown
    {
        private static readonly Regex Fence = new Regex(@"^ {0,3}```\s*([^\s`]*)\s*$");
        // Display maths on lines of its own: $$ ... $$ or \[ ... \] (possibly over several lines).
        private static readonly Regex DisplayMathLine = new Regex(@"^\s*(\$\$|\\\[)(.*?)(\$\$|\\\])?\s*$");

        private static readonly Regex Heading = new Regex(@"^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex Bullet = new Regex(@"^(\s*)[-*+]\s+(.*)$");
        private static readonly Regex Numbered = new Regex(@"^(\s*)(\d+[.)])\s+(.*)$");
        private static readonly Regex Quote = new Regex(@"^ {0,3}>\s?(.*)$");
        private static readonly Regex Rule = new Regex(@"^ {0,3}([-*_])(\s*\1){2,}\s*$");
        // Inline: `code`, **bold** / __bold__, [text](url), *italic* / _italic_ (underscores only at word boundaries, so
        // snake_case stays as it is), and maths: \(...\), $$...$$, \[...\] and $...$. A $ only starts maths when it is
        // followed by a non-space and closed by a $ that follows a non-space and isn't followed by a digit, so prices
        // ("$5 and $10") stay as they are. Unclosed markup (e.g. while streaming) is shown as it is.
        private static readonly Regex Inline = new Regex(
            @"`([^`\n]+)`|\*\*(.+?)\*\*|(?<![\w])__(.+?)__(?![\w])|\[([^\]\n]+)\]\(([^)\s]+)\)|(?<![\w*])\*(?!\s)([^*\n]+?)\*(?![\w*])|(?<![\w])_(?!\s)([^_\n]+?)_(?![\w])" +
            @"|\\\((.+?)\\\)|\$\$([^\n]+?)\$\$|\\\[(.+?)\\\]|(?<![\\$\w])\$(?![\s$])([^$\n]+?)(?<![\s\\])\$(?![\d$])");

        // Just the maths alternatives of Inline, for text inside bold, italic or a link ("**Identify $a$ and $b$**"), and
        // for read-aloud.
        private static readonly Regex InlineMath = new Regex(
            @"\\\((.+?)\\\)|\$\$([^\n]+?)\$\$|\\\[(.+?)\\\]|(?<![\\$\w])\$(?![\s$])([^$\n]+?)(?<![\s\\])\$(?![\d$])");

        /// <summary>
        /// Split a reply into blocks: Markdown text, fenced code blocks and display maths. A code block or display maths
        /// that hasn't been closed yet (the reply is still streaming) runs to the end of the text.
        /// </summary>
        /// <param name="text">The reply, or as much of it as has been streamed</param>
        /// <returns>The blocks, in order; empty text blocks are left out</returns>
        public static IReadOnlyList<ReplyBlock> SplitBlocks(string text)
        {
            List<ReplyBlock> blocks = new List<ReplyBlock>();
            List<string> lines = new List<string>();
            bool inCode = false;
            string language = null;
            // Display maths that hasn't been closed yet: the marker that closes it ("$$" or "\]").
            string mathClose = null;

            void Flush()
            {
                string content = string.Join("\n", lines);
                if (inCode)
                    blocks.Add(new ReplyBlock(ReplyBlockKind.Code, content.TrimEnd('\n'), language));
                else if (mathClose != null)
                {
                    if (content.Trim().Length > 0)
                        blocks.Add(new ReplyBlock(ReplyBlockKind.Math, content.Trim(), null));
                }
                else if (content.Trim().Length > 0)
                    AddText(blocks, lines);
                lines.Clear();
            }

            foreach (string rawLine in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                if (mathClose == null)
                {
                    Match fence = Fence.Match(rawLine);
                    if (fence.Success)
                    {
                        Flush();
                        inCode = !inCode;
                        language = inCode ? fence.Groups[1].Value : null;
                        continue;
                    }
                }
                if (inCode)
                {
                    lines.Add(rawLine);
                    continue;
                }
                if (mathClose != null)
                {
                    // Inside display maths: up to its closing marker.
                    int end = rawLine.IndexOf(mathClose, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        lines.Add(rawLine);
                        continue;
                    }
                    lines.Add(rawLine.Substring(0, end));
                    Flush();
                    mathClose = null;
                    string rest = rawLine.Substring(end + 2).Trim();
                    if (rest.Length > 0)
                        lines.Add(rest);
                    continue;
                }
                Match math = DisplayMathLine.Match(rawLine);
                string close = math.Success && math.Groups[1].Value == "$$" ? "$$" : "\\]";
                // Display maths: the line starts with the marker and either ends with the closing one or has none (it
                // continues on the next lines); "$$x$$ and more" is maths within a line instead.
                if (math.Success && (math.Groups[3].Success || math.Groups[2].Value.IndexOf(close, StringComparison.Ordinal) < 0))
                {
                    Flush();
                    mathClose = close;
                    lines.Add(math.Groups[2].Value);
                    if (math.Groups[3].Success && math.Groups[3].Value == mathClose)
                    {
                        // Opened and closed on this line.
                        Flush();
                        mathClose = null;
                    }
                    continue;
                }
                lines.Add(rawLine);
            }
            Flush();
            return blocks;
        }

        // A table's separator row: "| --- | :---: | ---: |", the outer bars optional but at least one bar inside or outside.
        private static readonly Regex TableSeparator = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");

        // Text lines, with any tables in them as table blocks: a row with a bar, followed by a separator row, then the rows
        // with bars that follow (a table still streaming grows row by row).
        private static void AddText(List<ReplyBlock> blocks, List<string> lines)
        {
            int start = 0;
            for (int i = 0; i + 1 < lines.Count; i++)
            {
                if (lines[i].IndexOf('|') < 0 || !TableSeparator.IsMatch(lines[i + 1]) || lines[i + 1].IndexOf('|') < 0)
                    continue;
                AddPlainText(blocks, lines, start, i);
                IReadOnlyList<string> header = SplitCells(lines[i]);
                List<ReplyTableAlignment> alignments = SplitCells(lines[i + 1]).Select(Alignment).ToList();
                while (alignments.Count < header.Count)
                    alignments.Add(ReplyTableAlignment.Left);
                if (alignments.Count > header.Count)
                    alignments.RemoveRange(header.Count, alignments.Count - header.Count);
                var rows = new List<IReadOnlyList<string>>();
                int end = i + 2;
                for (; end < lines.Count && lines[end].Trim().Length > 0 && lines[end].IndexOf('|') >= 0; end++)
                {
                    List<string> cells = SplitCells(lines[end]).ToList();
                    while (cells.Count < header.Count)
                        cells.Add(string.Empty);
                    if (cells.Count > header.Count)
                        cells.RemoveRange(header.Count, cells.Count - header.Count);
                    rows.Add(cells);
                }
                blocks.Add(new ReplyBlock(string.Join("\n", lines.GetRange(i, end - i)), header, alignments, rows));
                start = end;
                i = end - 1;
            }
            AddPlainText(blocks, lines, start, lines.Count);
        }

        private static void AddPlainText(List<ReplyBlock> blocks, List<string> lines, int from, int to)
        {
            if (to <= from)
                return;
            string content = string.Join("\n", lines.GetRange(from, to - from));
            if (content.Trim().Length > 0)
                blocks.Add(new ReplyBlock(ReplyBlockKind.Text, content.Trim('\n', '\r', ' '), null));
        }

        /// <summary>A table row's cells: split at the bars (not at an escaped \|), the outer bars and spaces removed.</summary>
        /// <param name="row">The row as written, e.g. "| iPhone 16e | A18 |"</param>
        public static IReadOnlyList<string> SplitCells(string row)
        {
            string text = (row ?? string.Empty).Trim();
            if (text.StartsWith("|", StringComparison.Ordinal))
                text = text.Substring(1);
            if (text.EndsWith("|", StringComparison.Ordinal) && !text.EndsWith("\\|", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
            var cells = new List<string>();
            var cell = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|')
                {
                    cell.Append('|');
                    i++;
                }
                else if (text[i] == '|')
                {
                    cells.Add(cell.ToString().Trim());
                    cell.Clear();
                }
                else
                    cell.Append(text[i]);
            }
            cells.Add(cell.ToString().Trim());
            return cells;
        }

        private static ReplyTableAlignment Alignment(string separatorCell)
        {
            bool left = separatorCell.StartsWith(":", StringComparison.Ordinal), right = separatorCell.EndsWith(":", StringComparison.Ordinal);
            return left && right ? ReplyTableAlignment.Center : right ? ReplyTableAlignment.Right : ReplyTableAlignment.Left;
        }

        /// <summary>
        /// Read one line of a text block: a heading, a rule, a list item, a quote or a plain line.
        /// </summary>
        /// <param name="line">The line, without its line break</param>
        /// <returns>The line's kind and content</returns>
        public static ReplyLine ParseLine(string line)
        {
            line = (line ?? string.Empty).TrimEnd('\r');
            Match m;
            if ((m = Heading.Match(line)).Success)
                return new ReplyLine(ReplyLineKind.Heading, m.Groups[2].Value, headingLevel: m.Groups[1].Length);
            if (Rule.IsMatch(line))
                return new ReplyLine(ReplyLineKind.Rule, string.Empty);
            if ((m = Bullet.Match(line)).Success)
                return new ReplyLine(ReplyLineKind.Bullet, m.Groups[2].Value, indent: Indent(m.Groups[1].Value), marker: "•");
            if ((m = Numbered.Match(line)).Success)
                return new ReplyLine(ReplyLineKind.Numbered, m.Groups[3].Value, indent: Indent(m.Groups[1].Value), marker: m.Groups[2].Value);
            if ((m = Quote.Match(line)).Success)
                return new ReplyLine(ReplyLineKind.Quote, m.Groups[1].Value);
            return new ReplyLine(ReplyLineKind.Plain, line);
        }

        private static string Indent(string leading)
        {
            // Two spaces (or a tab) per nesting level.
            int width = leading.Replace("\t", "  ").Length;
            return new string(' ', width / 2 * 3);
        }

        /// <summary>
        /// Split a line's content into runs of plain, bold, italic, code, link and maths text, with the markup removed.
        /// Maths inside bold, italic or a link's text is converted too.
        /// </summary>
        /// <param name="text">The content of a line (<see cref="ReplyLine.Content"/>)</param>
        /// <returns>The runs, in order</returns>
        public static IReadOnlyList<ReplyRun> ParseInline(string text)
        {
            List<ReplyRun> runs = new List<ReplyRun>();
            text = text ?? string.Empty;
            int position = 0;
            foreach (Match m in Inline.Matches(text))
            {
                if (m.Index > position)
                    runs.Add(new ReplyRun(ReplyRunKind.Text, text.Substring(position, m.Index - position)));
                if (m.Groups[1].Success)
                    runs.Add(new ReplyRun(ReplyRunKind.Code, m.Groups[1].Value));
                else if (m.Groups[2].Success || m.Groups[3].Success)
                    runs.Add(new ReplyRun(ReplyRunKind.Bold, WithMath(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)));
                else if (m.Groups[8].Success || m.Groups[9].Success || m.Groups[10].Success || m.Groups[11].Success)
                {
                    string latex = m.Groups[8].Success ? m.Groups[8].Value : m.Groups[9].Success ? m.Groups[9].Value
                        : m.Groups[10].Success ? m.Groups[10].Value : m.Groups[11].Value;
                    runs.Add(new ReplyRun(ReplyRunKind.Math, MathText.ToUnicode(latex)));
                }
                else if (m.Groups[4].Success)
                    runs.Add(new ReplyRun(ReplyRunKind.Link, WithMath(m.Groups[4].Value), m.Groups[5].Value));
                else
                    runs.Add(new ReplyRun(ReplyRunKind.Italic, WithMath(m.Groups[6].Success ? m.Groups[6].Value : m.Groups[7].Value)));
                position = m.Index + m.Length;
            }
            if (position < text.Length)
                runs.Add(new ReplyRun(ReplyRunKind.Text, text.Substring(position)));
            return runs;
        }

        /// <summary>
        /// Replace the inline maths in a text ($...$, \(...\), $$...$$, \[...\]) with Unicode text, leaving everything
        /// else as it is - e.g. for reading a reply aloud.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>The text with its maths converted</returns>
        public static string WithMath(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;
            return InlineMath.Replace(text, m => MathText.ToUnicode(
                m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value
                : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value));
        }

        /// <summary>
        /// The end of a text that is still being streamed (e.g. the model's thinking), for a short preview: from a word
        /// boundary, with maths converted and without the Markdown markers (code fences, **, `, #) that a tail cut out
        /// of formatted text can't be laid out with. Longer texts start with "…".
        /// </summary>
        /// <param name="text">The text so far</param>
        /// <param name="maxLength">The longest preview, in characters (about three lines for the default)</param>
        /// <returns>The preview</returns>
        public static string Preview(string text, int maxLength = 180)
        {
            string preview = Regex.Replace(WithMath(text).Replace("\r", ""), @"^\s*```.*$\n?", string.Empty, RegexOptions.Multiline);
            preview = Regex.Replace(preview, @"\*\*|__|`|^#{1,6}\s+", string.Empty, RegexOptions.Multiline).Trim();
            if (preview.Length <= maxLength)
                return preview;
            string tail = preview.Substring(preview.Length - maxLength);
            int space = tail.IndexOfAny(new[] { ' ', '\n' });
            if (space > 0 && space < 40)
                tail = tail.Substring(space + 1);
            return "…" + tail;
        }
    }
}
