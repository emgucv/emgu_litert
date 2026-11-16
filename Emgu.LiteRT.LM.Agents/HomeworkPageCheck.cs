// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>One question or part of a page check: what the model read, solved and marked for it.</summary>
public sealed class HomeworkPageItem
{
    /// <summary>"3" or "2a".</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>The top-level question's number: "3" for "3", "2" for "2a".</summary>
    public string Question { get; set; } = string.Empty;
    /// <summary>The part's letter ("a"), or empty for a question without parts.</summary>
    public string Part { get; set; } = string.Empty;
    /// <summary>The printed question as read.</summary>
    public string QuestionText { get; set; } = string.Empty;
    /// <summary>What the student wrote ("[blank]" when nothing).</summary>
    public string StudentAnswer { get; set; } = string.Empty;
    /// <summary>The model's own answer.</summary>
    public string MyAnswer { get; set; } = string.Empty;
    /// <summary>The mark.</summary>
    public HomeworkVerdict Verdict { get; set; }
    /// <summary>The comparison's words after the mark: for a wrong answer what the student wrote, the answer and a hint; for ❓ the reason.</summary>
    public string Detail { get; set; } = string.Empty;
}

/// <summary>A top-level question of a page check: its parts together (Q2 with a, b and c is one section) and one mark for all.</summary>
public sealed class HomeworkSection
{
    /// <summary>The top-level number: "2".</summary>
    public string Question { get; set; } = string.Empty;
    /// <summary>The parts' items in page order (one item for a question without parts; none for a region the check did not list).</summary>
    public List<HomeworkPageItem> Items { get; } = new List<HomeworkPageItem>();
    /// <summary>Whether a region of the page (a crop) has this number.</summary>
    public bool HasRegion { get; set; }

    /// <summary>
    /// One mark for the question: wrong when any part is, correct when every part is, not answered when every part is blank, and
    /// can't tell otherwise (including a question the check did not list).
    /// </summary>
    public HomeworkVerdict Verdict
    {
        get
        {
            if (Items.Count == 0)
                return HomeworkVerdict.Unclear;
            if (Items.Any(i => i.Verdict == HomeworkVerdict.Wrong))
                return HomeworkVerdict.Wrong;
            if (Items.All(i => i.Verdict == HomeworkVerdict.Correct))
                return HomeworkVerdict.Correct;
            if (Items.All(i => i.Verdict == HomeworkVerdict.NotAnswered))
                return HomeworkVerdict.NotAnswered;
            return HomeworkVerdict.Unclear;
        }
    }
}

/// <summary>
/// Turns the three texts of the whole-page check (<see cref="HomeworkCheck"/>: the read list, the model's own answers and the
/// comparison) into one item per question or part, and the items into one section per top-level question, so each section can be
/// shown with the picture of its region of the page.
/// </summary>
public static class HomeworkPageCheck
{
    // "1. ...", "2a. ...", "Question 3: ...", "**4b)** ..."; a number before a decimal ("1.5") is not a label.
    private static readonly Regex Numbered = new Regex(@"^(?:question\s+)?(\d{1,2})\s*([a-h])?\s*[.):](?!\d)\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // "a) ..." under a "1." line.
    private static readonly Regex PartOnly = new Regex(@"^\(?([a-h])[.)]\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Mark = new Regex(@"^(✅|❌|❓)\s*(correct|not the same|not right|incorrect|can'?t tell|not answered)?\s*[:\-–—]?\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The items of a page check, in the order of the read list (a question only the solve or the comparison lists comes after).
    /// </summary>
    /// <param name="read">The read list: "1. Question: ... | Student's answer: ..."</param>
    /// <param name="solved">The model's own answers: "1. ..."</param>
    /// <param name="check">The tidied comparison (<see cref="HomeworkCheck.TidyCheck"/>): "1. ✅ Correct"</param>
    public static IReadOnlyList<HomeworkPageItem> Parse(string read, string solved, string check)
    {
        var items = new List<HomeworkPageItem>();
        var byLabel = new Dictionary<string, HomeworkPageItem>();
        HomeworkPageItem Get(string label, string question, string part)
        {
            if (!byLabel.TryGetValue(label, out HomeworkPageItem item))
            {
                item = new HomeworkPageItem { Label = label, Question = question, Part = part };
                byLabel[label] = item;
                items.Add(item);
            }
            return item;
        }

        foreach ((string label, string question, string part, string rest) in Lines(read))
        {
            HomeworkPageItem item = Get(label, question, part);
            int answer = rest.IndexOf("Student's answer", StringComparison.OrdinalIgnoreCase);
            string text = answer >= 0 ? rest.Substring(0, answer) : rest;
            item.QuestionText = Regex.Replace(text.Trim().TrimEnd('|', '-', '–', ' '), @"^Question:\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
            if (answer >= 0)
            {
                string given = rest.Substring(answer + "Student's answer".Length).TrimStart(':', ' ', '\t').Trim();
                item.StudentAnswer = Regex.IsMatch(given, @"^\W*\[?\(?blank\)?\]?\W*$", RegexOptions.IgnoreCase) ? "[blank]" : given;
            }
        }
        foreach ((string label, string question, string part, string rest) in Lines(solved))
            Get(label, question, part).MyAnswer = rest;
        foreach ((string label, string question, string part, string rest) in Lines(check))
        {
            HomeworkPageItem item = Get(label, question, part);
            Match mark = Mark.Match(rest);
            if (!mark.Success)
                continue;
            string detail = rest.Substring(mark.Length).Trim();
            string word = mark.Groups[2].Value;
            item.Detail = detail;
            if (mark.Groups[1].Value == "✅")
                item.Verdict = HomeworkVerdict.Correct;
            else if (mark.Groups[1].Value == "❌")
                item.Verdict = HomeworkVerdict.Wrong;
            else
                item.Verdict = word.StartsWith("not answered", StringComparison.OrdinalIgnoreCase) ? HomeworkVerdict.NotAnswered : HomeworkVerdict.Unclear;
        }
        foreach (HomeworkPageItem item in items)
            if (item.Verdict == HomeworkVerdict.Pending)
                item.Verdict = item.StudentAnswer == "[blank]" ? HomeworkVerdict.NotAnswered : HomeworkVerdict.Unclear;
        return items;
    }

    /// <summary>
    /// One section per top-level question: those with a region of the page first, in the page's order, then the questions the check
    /// listed that no region has.
    /// </summary>
    /// <param name="items">The page check's items (<see cref="Parse"/>)</param>
    /// <param name="regionQuestions">The top-level numbers of the page's regions (crops), in page order</param>
    public static IReadOnlyList<HomeworkSection> Group(IEnumerable<HomeworkPageItem> items, IEnumerable<string> regionQuestions)
    {
        var sections = new List<HomeworkSection>();
        var byQuestion = new Dictionary<string, HomeworkSection>();
        HomeworkSection Get(string question)
        {
            if (!byQuestion.TryGetValue(question, out HomeworkSection section))
            {
                section = new HomeworkSection { Question = question };
                byQuestion[question] = section;
                sections.Add(section);
            }
            return section;
        }
        foreach (string question in regionQuestions ?? Enumerable.Empty<string>())
            Get(question).HasRegion = true;
        foreach (HomeworkPageItem item in items ?? Enumerable.Empty<HomeworkPageItem>())
            Get(item.Question).Items.Add(item);
        // A question with parts is listed by the model once for its stem ("2. Question: ...") and once for each part: the stem
        // line has no answer of its own (none, or the model's "[blank]") and is no part to mark, whatever the solve and the
        // comparison said about it (the model gives a stem an answer and a "can't tell" too).
        foreach (HomeworkSection section in sections)
            if (section.Items.Any(i => i.Part.Length > 0))
                section.Items.RemoveAll(i => i.Part.Length == 0 && (i.StudentAnswer.Length == 0 || i.StudentAnswer == "[blank]"));
        return sections;
    }

    /// <summary>
    /// The line of a section's verdict for the score: a mark for each section, as <see cref="HomeworkCheck.Summary"/> counts them.
    /// </summary>
    /// <param name="sections">The sections of every page</param>
    public static string Marks(IEnumerable<HomeworkSection> sections) =>
        string.Join("\n", sections.Select(s => s.Verdict == HomeworkVerdict.Correct ? "✅" : s.Verdict == HomeworkVerdict.Wrong ? "❌" : "❓"));

    // The labeled lines of a list: the label, the top-level number, the part letter and the rest of the line.
    private static IEnumerable<(string Label, string Question, string Part, string Text)> Lines(string text)
    {
        string lastQuestion = null;
        foreach (string raw in (text ?? string.Empty).Split('\n'))
        {
            string line = raw.Replace("**", string.Empty).Trim().TrimStart('-', '*', '•', '#', ' ', '\t');
            if (line.Length == 0)
                continue;
            Match numbered = Numbered.Match(line);
            if (numbered.Success)
            {
                string question = numbered.Groups[1].Value, part = numbered.Groups[2].Value.ToLowerInvariant();
                lastQuestion = question;
                yield return (question + part, question, part, numbered.Groups[3].Value.Trim());
                continue;
            }
            Match partOnly = PartOnly.Match(line);
            if (partOnly.Success && lastQuestion != null)
            {
                string part = partOnly.Groups[1].Value.ToLowerInvariant();
                yield return (lastQuestion + part, lastQuestion, part, partOnly.Groups[2].Value.Trim());
            }
        }
    }
}
