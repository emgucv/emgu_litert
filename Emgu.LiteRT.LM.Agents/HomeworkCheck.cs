// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
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
        "guess a digit. Do not solve anything. One line for each question, and for a question with parts (a, b, c...) one " +
        "line for each part, numbered with the part's letter, in this form:\n" +
        "1. Question: ... | Student's answer: ...\n" +
        "2a. Question: ... | Student's answer: ...\n" +
        "Write only the list." + NoteLine(note);

    /// <summary>The second request: solve the questions alone.</summary>
    /// <param name="questions">The questions, without the student's answers (<see cref="QuestionsOnly"/>)</param>
    public static string SolvePrompt(string questions) =>
        "These questions are on a homework page (the photo shows it):\n" + questions + "\n" +
        "Solve each question yourself, carefully, from the question alone. Then give only the final result of each, one per " +
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
            if (number.Success && Regex.IsMatch(text, @"\[blank\]|\(blank\)|left (it )?blank", RegexOptions.IgnoreCase))
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
