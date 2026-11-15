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
        "guess a digit. Do not solve anything. One question per line, in this form:\n" +
        "1. Question: ... | Student's answer: ...\n" +
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
        "For each question write one line: \"<number>. ✅ Correct\", or \"<number>. ❌ Not the same: the student wrote X, the " +
        "answer is Y\" with a short hint on how to get Y, or \"<number>. ❓ Can't tell\" with the reason (the answer was blank or " +
        "unreadable, or the two answers can't be compared). Answers that mean the same are correct (0.5 and 1/2, \"12 cm\" and " +
        "12, a capital letter). Do not mark a question wrong because of a [?]. End with a line \"Score: N of M\".";

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
            lines.Add(Regex.Replace(text, @"^(\d+[.)]\s*)Question:\s*", "$1", RegexOptions.IgnoreCase));
        }
        return string.Join("\n", lines);
    }
}
