// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>
/// The requests of the chat's "Check homework" chip, on a photo of a homework page: read the questions and what the
/// student wrote, solve the questions alone (without the student's answers, so they don't sway the solution), and compare
/// the two. Three small requests instead of one, so a small model does one thing at a time.
/// </summary>
public static class HomeworkCheck
{
    /// <summary>The first request: read the questions and the student's answers off the photo.</summary>
    /// <param name="note">What the user wrote with the photo (the grade, which questions), or empty</param>
    public static string ReadPrompt(string note) =>
        "This is a photo of a student's homework. List every question you can see. For each one write its number, the question " +
        "(shortened if it is long) and exactly what the student wrote as the answer: copy it as it is written, even when it " +
        "looks wrong. If the student left it empty write [blank]. If you cannot read a character write [?] in its place: never " +
        "guess a digit. A question can refer to a table, chart or figure beside it: copy what it shows (the labels and the numbers) " +
        "into the question, in the table's order from the top, for example \"Table: Blue Whale 33.58, Great White Shark 7.92\". " +
        "Copy a list of numbers with the commas the student wrote (\"3, 4, 2, 1\", not \"3, 4, 21\"). Do not solve anything. One line for each question, and for a question with parts (a, b, c...) one " +
        "line for each part, numbered with the part's letter, in this form:\n" +
        "1. Question: ... | Student's answer: ...\n" +
        "2a. Question: ... | Student's answer: ...\n" +
        "Write only the list." + NoteLine(note);

    /// <summary>The second request: solve the questions alone.</summary>
    /// <param name="questions">The questions, without the student's answers (<see cref="QuestionsOnly"/>)</param>
    public static string SolvePrompt(string questions) =>
        "These questions are on a homework page (the photo shows it):\n" + questions + "\n" +
        "Solve each question yourself, carefully, from the question alone; when a question refers to a table, chart or figure, read it from the photo. Then give only the final result of each, one per " +
        "line, in this form:\n1. <your answer>";

    /// <summary>The third request: compare the student's answers with the model's own.</summary>
    /// <param name="read">The list of the first request</param>
    /// <param name="solved">The answers of the second request</param>
    public static string ComparePrompt(string read, string solved) =>
        "A student's homework is being checked. These are the questions with what the student wrote:\n" + read + "\n\n" +
        "These are the answers worked out separately:\n" + solved + "\n\n" +
        "For each question write one line, with its number and part letter exactly as listed above (1, 2a, 2b...): \"<number>. ✅ Correct\", or " +
        "\"<number>. ❌ Not the same: the student wrote X, the answer is Y\" with a short hint on how to get Y, or " +
        "\"<number>. ❓ Can't tell\" with the reason (the answer was unreadable, or the two can't be compared). Answers that mean " +
        "the same are correct (0.5 and 1/2, \"12 cm\" and 12, a capital letter). A question that has many right answers (for example " +
        "\"a decimal between 5.63 and 5.68\") is correct when the student's answer fits it, even if it differs from the worked-out one. " +
        "An ordering question can be answered with the numbers of the table's rows in the right order (the rows counted from the top: " +
        "\"3, 4, 2, 1\" is the third row, then the fourth, the second and the first): work out the order of the rows and compare " +
        "it with the student's numbers. " +
        "Do not mark a question wrong because of a [?]. " +
        "Write nothing else: no score, no introduction.";

    /// <summary>
    /// The comparison as the reply shows it: a question the student left blank is "❓ Not answered" (the model marked
    /// blanks wrong), and a score line the model added anyway is dropped (the score is counted by <see cref="Summary"/>).
    /// </summary>
    /// <param name="check">The comparison the third request wrote</param>
    public static string TidyCheck(string check)
    {
        var lines = new List<string>();
        foreach (string line in (check ?? string.Empty).Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0 || Regex.IsMatch(text, @"^\W*Score\b", RegexOptions.IgnoreCase))
                continue;
            Match number = Regex.Match(text, @"^(\d+[a-z]?[.)]|[a-z]\))\s*");
            // A "can't tell" that says blank in any words ("left this blank", "was blank"); on a ✅ or ❌ line only the marked forms
            // count, since "fill in the blank" can be in a question's hint.
            if (number.Success && Regex.IsMatch(text, text.Contains("❓") ? @"\bblank\b" : @"\[blank\]|\(blank\)|left (it )?blank", RegexOptions.IgnoreCase))
                text = number.Value.Trim() + " ❓ Not answered";
            lines.Add(text);
        }
        return string.Join("\n", lines);
    }

    /// <summary>The marks of comparisons counted: ✅ correct, ❌ not the same, ❓ can't tell or not answered.</summary>
    /// <param name="checks">The tidied comparisons (<see cref="TidyCheck"/>) of every page</param>
    public static (int Correct, int Wrong, int Unclear) Count(IEnumerable<string> checks)
    {
        int correct = 0, wrong = 0, unclear = 0;
        foreach (string check in checks)
            foreach (string line in (check ?? string.Empty).Split('\n'))
            {
                if (line.IndexOf('✅') >= 0)
                    correct++;
                else if (line.IndexOf('❌') >= 0)
                    wrong++;
                else if (line.IndexOf('❓') >= 0)
                    unclear++;
            }
        return (correct, wrong, unclear);
    }

    /// <summary>The line that ends the reply: how many answers were right (counted here, not by the model).</summary>
    /// <param name="checks">The tidied comparisons of every page</param>
    public static string Summary(IEnumerable<string> checks)
    {
        var (correct, wrong, unclear) = Count(checks);
        int total = correct + wrong + unclear;
        if (total == 0)
            return string.Empty;
        string text = correct + " of " + total + " correct";
        if (wrong > 0)
            text += ", " + wrong + " not the same";
        if (unclear > 0)
            text += ", " + unclear + " not answered or can't tell";
        return "**Score: " + text + "**";
    }

    /// <summary>
    /// The ordering questions checked in code, over the model's comparison: a question whose read line has a copied table
    /// ("Table: Blue Whale 33.58, Great White Shark 7.92, ..."), says which way to order ("least to greatest", "shortest to
    /// longest", "descending"...), and whose answer is the table's rows - their numbers counted from the top ("3, 4, 2, 1") or
    /// their labels. Its line becomes ✅ or ❌ by sorting the table's numbers; any other question keeps the model's line. Gemma 4 12B
    /// worked out 4a's rows right in its own words and then wrote another order (FEATURES.md, "4a, third round").
    /// </summary>
    /// <param name="read">The list the first request wrote</param>
    /// <param name="check">The tidied comparison (<see cref="TidyCheck"/>)</param>
    public static string CheckOrderings(string read, string check)
    {
        var verdicts = new Dictionary<string, string>();
        foreach (string line in (read ?? string.Empty).Split('\n'))
        {
            Match item = Regex.Match(line.Trim(), @"^(\d+[a-z]?)[.)]\s*(.*?)\|\s*Student's answer:\s*(.*)$", RegexOptions.IgnoreCase);
            if (!item.Success)
                continue;
            string verdict = OrderingVerdict(item.Groups[2].Value, item.Groups[3].Value.Trim());
            if (verdict != null)
                verdicts[item.Groups[1].Value.ToLowerInvariant()] = item.Groups[1].Value + ". " + verdict;
        }
        if (verdicts.Count == 0)
            return check;
        var lines = new List<string>();
        foreach (string line in (check ?? string.Empty).Split('\n'))
        {
            Match number = Regex.Match(line.Trim(), @"^(\d+[a-z]?)[.)]", RegexOptions.IgnoreCase);
            string key = number.Success ? number.Groups[1].Value.ToLowerInvariant() : null;
            if (key != null && verdicts.TryGetValue(key, out string verdict))
            {
                lines.Add(verdict);
                verdicts.Remove(key);
            }
            else
                lines.Add(line);
        }
        // A question the comparison left out gets its line at the end.
        lines.AddRange(verdicts.Values);
        return string.Join("\n", lines);
    }

    private static readonly string[] Ascending = { "least to greatest", "smallest to largest", "shortest to longest", "lightest to heaviest",
        "lowest to highest", "slowest to fastest", "youngest to oldest", "fewest to most", "ascending", "increasing" };
    private static readonly string[] Descending = { "greatest to least", "largest to smallest", "longest to shortest", "heaviest to lightest",
        "highest to lowest", "fastest to slowest", "oldest to youngest", "most to fewest", "descending", "decreasing" };

    // "✅ Correct ..." or "❌ Not the same ..." for an ordering of a copied table's rows, or null when it can't be told for sure.
    private static string OrderingVerdict(string question, string answer)
    {
        Match table = Regex.Match(question, @"Table:\s*(.*)$", RegexOptions.IgnoreCase);
        if (!table.Success)
            return null;
        string stem = question.Substring(0, table.Index).ToLowerInvariant();
        bool up = Ascending.Any(stem.Contains), down = Descending.Any(stem.Contains);
        if (up == down)
            return null;
        var rows = new List<(string Label, double Value)>();
        foreach (string entry in table.Groups[1].Value.TrimEnd(' ', '.', '|').Split(',', ';'))
        {
            Match row = Regex.Match(entry.Trim(), @"^(.*?[^\d\s.].*?)\s*:?\s+(-?\d+(?:\.\d+)?)\s*\S*$");
            if (!row.Success)
                return null;
            rows.Add((row.Groups[1].Value.Trim(), double.Parse(row.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (rows.Count < 2 || rows.Select(r => r.Value).Distinct().Count() != rows.Count)
            return null;
        List<int> order = Enumerable.Range(1, rows.Count).OrderBy(i => rows[i - 1].Value).ToList();
        if (down)
            order.Reverse();
        List<int> student = StudentRows(answer, rows.Select(r => r.Label).ToList());
        if (student == null)
            return null;
        string right = string.Join(", ", order);
        string values = string.Join(", ", order.Select(i => rows[i - 1].Label + " " + rows[i - 1].Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return student.SequenceEqual(order)
            ? "✅ Correct (" + values + ")"
            : "❌ Not the same: the student wrote " + answer + ", the answer is " + right + " (" + values + "): sort the table's numbers " +
              (down ? "from the greatest" : "from the least") + " and write each one's row";
    }

    // The rows the student listed, by number (1 is the table's top row) or by label; null unless every row is there once.
    private static List<int> StudentRows(string answer, List<string> labels)
    {
        string[] parts = answer.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        if (parts.Length != labels.Count)
            return null;
        var rows = new List<int>();
        foreach (string part in parts)
        {
            int row;
            if (Regex.IsMatch(part, @"^\d+$"))
                row = int.Parse(part);
            else
            {
                int[] matches = Enumerable.Range(1, labels.Count).Where(i => labels[i - 1].IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0
                    || part.IndexOf(labels[i - 1], StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (matches.Length != 1)
                    return null;
                row = matches[0];
            }
            rows.Add(row);
        }
        return rows.Distinct().Count() == labels.Count && rows.All(r => r >= 1 && r <= labels.Count) ? rows : null;
    }

    private static string NoteLine(string note) =>
        string.IsNullOrWhiteSpace(note) ? string.Empty : "\nThe user said: " + note.Trim();

    /// <summary>The questions of the first request's list without the student's answers (a line without the marker stays as it is).</summary>
    /// <param name="read">The list the first request wrote</param>
    public static string QuestionsOnly(string read)
    {
        var lines = new List<string>();
        foreach (string line in (read ?? string.Empty).Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0)
                continue;
            int answer = text.IndexOf("Student's answer", StringComparison.OrdinalIgnoreCase);
            if (answer > 0)
                text = text.Substring(0, answer).TrimEnd(' ', '|', '-', '–');
            lines.Add(Regex.Replace(text, @"^(\d+[a-z]?[.)]\s*)Question:\s*", "$1", RegexOptions.IgnoreCase));
        }
        return string.Join("\n", lines);
    }
}
