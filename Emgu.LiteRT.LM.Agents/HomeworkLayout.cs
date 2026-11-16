// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>A line of text found on a homework page: its bounding box in the page's pixels and what the first few characters read.</summary>
public sealed class LayoutLine
{
    /// <summary>Create one.</summary>
    /// <param name="index">The line's number (the caller's list index), returned in the units' lines</param>
    /// <param name="x">Left edge of the bounding box</param>
    /// <param name="y">Top edge of the bounding box</param>
    /// <param name="width">Width of the bounding box</param>
    /// <param name="height">Height of the bounding box</param>
    /// <param name="start">What the recognizer read at the start of the line</param>
    /// <param name="leftStart">What it read at the start of the line including a little to its left, where a question's number sits when the detector left it out of the line; null when not read (only lines at the left margin need it)</param>
    public LayoutLine(int index, double x, double y, double width, double height, string start, string leftStart = null)
    {
        Index = index;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Start = start ?? string.Empty;
        LeftStart = leftStart;
    }

    /// <summary>The line's number.</summary>
    public int Index { get; }
    /// <summary>Left edge.</summary>
    public double X { get; }
    /// <summary>Top edge.</summary>
    public double Y { get; }
    /// <summary>Width.</summary>
    public double Width { get; }
    /// <summary>Height.</summary>
    public double Height { get; }
    /// <summary>The first characters read.</summary>
    public string Start { get; }
    /// <summary>The first characters read, with a little to the left of the line too; null when not read.</summary>
    public string LeftStart { get; }
    /// <summary>Vertical centre.</summary>
    public double CenterY => Y + Height / 2;
    /// <summary>Bottom edge.</summary>
    public double Bottom => Y + Height;
}

/// <summary>One thing to check on a page: a question, or a part of one (2a), with the lines of text that belong to it.</summary>
public sealed class HomeworkUnit
{
    /// <summary>Create one.</summary>
    /// <param name="question">The question's number ("2")</param>
    /// <param name="part">The part's letter ("a"), or null for a question without parts</param>
    /// <param name="lines">The lines that belong to this unit (the question's printed text and the handwriting next to it)</param>
    /// <param name="parentLines">All the lines of the question: its stem and every part, for the context the unit needs (a table, a figure)</param>
    /// <param name="slotLeft">For a part with another part beside it on its row: where its slot of the row begins (its label's left edge); otherwise null</param>
    /// <param name="slotRight">Where its slot ends (the next part's label, or the page's right-most text); otherwise null</param>
    public HomeworkUnit(string question, string part, IReadOnlyList<int> lines, IReadOnlyList<int> parentLines, double? slotLeft = null, double? slotRight = null)
    {
        Question = question;
        Part = part;
        Lines = lines;
        ParentLines = parentLines;
        SlotLeft = slotLeft;
        SlotRight = slotRight;
    }

    /// <summary>For a part beside another on its row, the left edge of its slot of the row (a crop is widened to it, since the detector can miss thin boxes the answer is written in); otherwise null.</summary>
    public double? SlotLeft { get; }

    /// <summary>The right edge of its slot of the row; otherwise null.</summary>
    public double? SlotRight { get; }

    /// <summary>The question's number.</summary>
    public string Question { get; }
    /// <summary>The part's letter, or null.</summary>
    public string Part { get; }
    /// <summary>"2" or "2a".</summary>
    public string Label => Question + (Part ?? string.Empty);
    /// <summary>The lines of this unit (indices given to <see cref="LayoutLine"/>).</summary>
    public IReadOnlyList<int> Lines { get; }
    /// <summary>The lines of the whole question.</summary>
    public IReadOnlyList<int> ParentLines { get; }
}

/// <summary>
/// Groups the lines of text of a photographed homework page into the questions (and their parts) to check. A question or part
/// begins at a line whose start reads as its label ("1.", "2)", "a)") near the left margin; every other line, printed or
/// handwritten, belongs to the last label above it - so a handwritten answer in the gap between two questions goes to the
/// question above it - and, for parts side by side on one row ("a) ... b) ..."), to the nearest label to its left. A line far
/// below the last line of a question (a footer) belongs to none. Pure geometry and text, tested without models.
/// </summary>
public static class HomeworkLayout
{
    private static readonly Regex NumberLabel = new Regex(@"^\s*(\d{1,2})\s*[.)]\s*(?!\d)", RegexOptions.CultureInvariant);
    private static readonly Regex BareNumber = new Regex(@"^\s*(\d{1,2})\s*(?=[A-Za-z])", RegexOptions.CultureInvariant);
    private static readonly Regex PartLabel = new Regex(@"^\s*\(?([a-h])\s*[.)]", RegexOptions.CultureInvariant);

    /// <summary>Whether a text starts with a question's number ("12." or "3)" and not a decimal such as "3.48").</summary>
    /// <param name="start">What a line starts with</param>
    /// <param name="number">The number</param>
    public static bool TryParseNumber(string start, out string number)
    {
        Match match = NumberLabel.Match(start ?? string.Empty);
        number = match.Success ? match.Groups[1].Value : null;
        return match.Success;
    }

    /// <summary>Whether a text starts with a part's letter ("a)" or "(b)").</summary>
    /// <param name="start">What a line starts with</param>
    /// <param name="letter">The letter</param>
    public static bool TryParsePart(string start, out string letter)
    {
        Match match = PartLabel.Match(start ?? string.Empty);
        letter = match.Success ? match.Groups[1].Value : null;
        return match.Success;
    }

    private sealed class Start
    {
        public LayoutLine Line;
        public string Question;
        public string Part;      // null for a question's own number
        public List<LayoutLine> Members = new List<LayoutLine>();
    }

    /// <summary>Group a page's lines into units, in reading order.</summary>
    /// <param name="lines">The lines found on the page</param>
    /// <param name="pageWidth">The page's width in pixels</param>
    /// <param name="pageHeight">The page's height in pixels</param>
    /// <returns>The questions and parts to check; empty when no label was found</returns>
    public static IReadOnlyList<HomeworkUnit> Segment(IReadOnlyList<LayoutLine> lines, double pageWidth, double pageHeight)
    {
        if (lines == null || lines.Count == 0)
            return new List<HomeworkUnit>();
        double lineHeight = Median(lines.Select(l => l.Height).ToList());
        double band = LeftMargin(lines, pageWidth) + 0.06 * pageWidth;
        // 1. The starts: question numbers at the margin, then part letters at the margin or on a row of an earlier start.
        var starts = new List<Start>();
        foreach (LayoutLine line in lines.OrderBy(l => l.Y).ThenBy(l => l.X))
        {
            if (line.X <= band && (TryParseNumber(line.Start, out string number) || TryParseNumber(line.LeftStart, out number)))
            {
                Start duplicate = starts.LastOrDefault(s => s.Part == null && s.Question == number && Math.Abs(s.Line.Y - line.Y) < 2 * lineHeight);
                if (duplicate == null)
                    starts.Add(new Start { Line = line, Question = number });
            }
        }
        // A number whose dot the recognizer lost ("6Jacob walks") counts when it fits between the numbers around it.
        foreach (LayoutLine line in lines.Where(l => l.X <= band && l.LeftStart != null).OrderBy(l => l.Y).ThenBy(l => l.X))
        {
            Match bare = BareNumber.Match(line.LeftStart);
            if (!bare.Success || starts.Any(s => s.Line == line) || TryParseNumber(line.Start, out _))
                continue;
            int n = int.Parse(bare.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var numbers = starts.Where(s => s.Part == null).Select(s => new { s.Line, N = int.Parse(s.Question, System.Globalization.CultureInfo.InvariantCulture) }).ToList();
            int before = numbers.Where(x => x.Line.Y < line.Y).Select(x => x.N).DefaultIfEmpty(0).Max();
            int after = numbers.Where(x => x.Line.Y > line.Y).Select(x => x.N).DefaultIfEmpty(int.MaxValue).Min();
            if (n > before && n < after && !numbers.Any(x => x.N == n))
                starts.Add(new Start { Line = line, Question = n.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        // Part letters: those at the margin first, then those on a row of a start already found (parts side by side).
        var candidates = lines.Where(l => (TryParsePart(l.Start, out _) && !TryParseNumber(l.Start, out _)) || (l.X <= band && TryParsePart(l.LeftStart, out _) && !TryParseNumber(l.LeftStart, out _))).OrderBy(l => l.Y).ThenBy(l => l.X).ToList();
        for (int pass = 0; pass < 2; pass++)
            foreach (LayoutLine line in candidates)
            {
                if (!TryParsePart(line.Start, out string letter))
                    TryParsePart(line.LeftStart, out letter);
                Start question = starts.Where(s => s.Part == null && s.Line.Y <= line.CenterY).OrderBy(s => s.Line.Y).LastOrDefault();
                if (question == null || starts.Any(s => s.Line == line || (s.Question == question.Question && s.Part == letter)))
                    continue;
                bool onARow = starts.Any(s => Math.Abs(s.Line.CenterY - line.CenterY) < 0.6 * Math.Max(s.Line.Height, line.Height) && s.Line.X < line.X);
                if ((pass == 0 && line.X <= band) || (pass == 1 && onARow))
                    starts.Add(new Start { Line = line, Question = question.Question, Part = letter });
            }
        if (starts.Count == 0)
            return new List<HomeworkUnit>();
        starts = starts.OrderBy(s => s.Line.Y).ThenBy(s => s.Line.X).ToList();
        // Rows of starts: starts whose lines are on the same row (parts side by side) are told apart by x.
        var rows = new List<List<Start>>();
        foreach (Start start in starts)
        {
            List<Start> row = rows.LastOrDefault();
            if (row != null && Math.Abs(row[0].Line.CenterY - start.Line.CenterY) < 0.6 * Math.Max(row[0].Line.Height, start.Line.Height))
                row.Add(start);
            else
                rows.Add(new List<Start> { start });
        }
        // 2. Every other line goes to the last row of starts above it, and in that row to the nearest start on its left.
        double maxGap = 4 * lineHeight;
        var ordered = lines.OrderBy(l => l.CenterY).ThenBy(l => l.X).ToList();
        var lastBottom = new Dictionary<Start, double>();
        foreach (LayoutLine line in ordered)
        {
            Start start = starts.FirstOrDefault(s => s.Line == line);
            if (start != null)
            {
                start.Members.Add(line);
                lastBottom[start] = Math.Max(lastBottom.ContainsKey(start) ? lastBottom[start] : 0, line.Bottom);
                continue;
            }
            List<Start> row = rows.LastOrDefault(r => r.Min(s => s.Line.Y) <= line.CenterY);
            if (row == null)
                continue;   // above the first label: the page's header
            Start owner = row.Where(s => s.Line.X <= line.X + 0.5 * line.Height).OrderBy(s => s.Line.X).LastOrDefault() ?? row.OrderBy(s => s.Line.X).First();
            double bottom = lastBottom.ContainsKey(owner) ? lastBottom[owner] : owner.Line.Bottom;
            if (line.Y - bottom > maxGap)
                continue;   // far below the question's last line: a footer, or the next page's lines
            owner.Members.Add(line);
            lastBottom[owner] = Math.Max(bottom, line.Bottom);
        }
        // 3. The units: the parts of a question, or the question itself without parts.
        var units = new List<HomeworkUnit>();
        foreach (var group in starts.GroupBy(s => s.Question).OrderBy(g => g.Min(s => s.Line.Y)))
        {
            var parent = group.SelectMany(s => s.Members).OrderBy(l => l.CenterY).ThenBy(l => l.X).Select(l => l.Index).ToList();
            List<Start> parts = group.Where(s => s.Part != null).OrderBy(s => s.Part, StringComparer.Ordinal).ToList();   // lettered: a, b, c
            if (parts.Count == 0)
                units.Add(new HomeworkUnit(group.Key, null, parent, parent));
            else
                foreach (Start part in parts)
                {
                    List<Start> row = rows.First(r => r.Contains(part)).OrderBy(s => s.Line.X).ToList();
                    int at = row.IndexOf(part);
                    double? left = null, right = null;
                    if (row.Count > 1)
                    {
                        left = part.Line.X;
                        right = at + 1 < row.Count ? row[at + 1].Line.X : lines.Max(l => l.X + l.Width);
                    }
                    units.Add(new HomeworkUnit(group.Key, part.Part, part.Members.OrderBy(l => l.CenterY).ThenBy(l => l.X).Select(l => l.Index).ToList(), parent, left, right));
                }
        }
        return units;
    }

    // The left edge of the printed text: a low percentile of the left edges of the long lines that read as words (a title or a
    // binder's shadow can stick out further left).
    private static double LeftMargin(IReadOnlyList<LayoutLine> lines, double pageWidth)
    {
        var edges = lines.Where(l => l.Width >= 0.25 * pageWidth && Regex.IsMatch(l.Start, @"[A-Za-z]{3}")).Select(l => l.X).OrderBy(x => x).ToList();
        if (edges.Count == 0)
            edges = lines.Select(l => l.X).OrderBy(x => x).ToList();
        return edges[(int)(0.25 * (edges.Count - 1))];
    }

    private static double Median(IList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }
}
