// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>How one question's answer was marked.</summary>
public enum HomeworkVerdict
{
    /// <summary>Not checked yet.</summary>
    Pending,
    /// <summary>The student's answer is right.</summary>
    Correct,
    /// <summary>The student's answer is not.</summary>
    Wrong,
    /// <summary>It can't be told (an unreadable answer, a question the model couldn't solve).</summary>
    Unclear,
    /// <summary>The student left it blank.</summary>
    NotAnswered
}

/// <summary>
/// The requests that check one question (or one part of a question) of a homework page, cut out as a picture (the
/// "Check homework" chip): read the question and what the student wrote, solve the question alone, and judge the two. The
/// student's answer is never shown to the request that solves, so it can't sway it.
/// </summary>
public static class HomeworkQuestionCheck
{
    /// <summary>
    /// The first request, on the question's picture: is there any handwriting in it? A model asked for the answer of a question
    /// the student left blank solves it and reports that as the student's answer; asked only whether anything is handwritten it
    /// said no for every blank question and yes for every other one (23 questions of two worksheets, Gemma 4 12B).
    /// </summary>
    public static string HandwritingPrompt() =>
        "This picture is part of a student's homework page. The question is printed. Look for anything the student wrote by hand " +
        "(pen or pencil), such as an answer, numbers or working. Is there any handwriting in this picture? Reply with exactly one word: yes or no.";

    /// <summary>Read a yes or no.</summary>
    /// <param name="reply">The model's reply</param>
    /// <returns>True for yes, false for no, null when it is neither</returns>
    public static bool? ParseYesNo(string reply)
    {
        Match match = Regex.Match(reply ?? string.Empty, @"^\W*(yes|no)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Equals("yes", StringComparison.OrdinalIgnoreCase) : (bool?)null;
    }

    /// <summary>
    /// The second request, on the question's picture: the student's answer and the question. The OCR's lines, when given, help it
    /// copy the characters of handwriting and tell the handwriting from the printed question.
    /// </summary>
    /// <param name="label">The question's label ("3", "2a")</param>
    /// <param name="note">What the user wrote with the photo, or empty</param>
    /// <param name="text">What an OCR read on each line of the picture, or null</param>
    public static string ReadPrompt(string label, string note, IReadOnlyList<string> text = null)
    {
        var prompt = new StringBuilder();
        prompt.Append("This picture is question ").Append(label).Append(" of a student's homework: the printed question and, in the student's ")
            .Append("handwriting, the answer and the working. The printed text is the question; the student's answer is what is handwritten ")
            .Append("(pen or pencil), next to the question or below it, often the last thing written. ");
        bool lines = text != null && text.Count > 0;
        if (lines)
        {
            prompt.Append("An OCR read these lines of the picture, top to bottom. For a clear line it is right about the characters, ")
                .Append("even of handwriting: copy them. A line marked [unsure] may be misread or split (digits written in boxes often ")
                .Append("are): trust the picture for those. The picture tells you which lines are handwritten:\n");
            for (int i = 0; i < text.Count; i++)
                prompt.Append(i + 1).Append(". ").Append(text[i].Trim()).Append('\n');
        }
        prompt.Append("Reply with JSON only: {");
        if (lines)
            prompt.Append("\"answer_lines\": [the numbers of the lines that hold the student's handwritten answer], ");
        prompt.Append("\"student_answer\": \"exactly what the student wrote as the answer, copied character by character as written even ")
            .Append("when it looks wrong (only the handwritten part of a line)\", \"question\": \"the printed question, as text (shortened if it ")
            .Append("is long)\"}. Do not solve the question and do not correct anything. Write [?] for a character you cannot read: ")
            .Append("never guess a digit. If you find no handwritten answer after all, student_answer is [blank].");
        if (!string.IsNullOrWhiteSpace(note))
            prompt.Append(" The user said: ").Append(note.Trim());
        return prompt.ToString();
    }

    /// <summary>The third request, on the whole question's picture (a part's table or figure is in it): solve it alone.</summary>
    /// <param name="label">The question's label</param>
    /// <param name="question">The question as the first request read it</param>
    public static string SolvePrompt(string label, string question) =>
        "This picture is a homework question. Solve only " + (Regex.IsMatch(label, "[a-z]$") ? "part " : "question ") + label +
        ": " + question + "\nIgnore any handwriting in the picture; solve it yourself, carefully, from the printed question. Follow the question's own " +
        "instructions exactly: use the values it gives (such as \"\u221a6 = 2.45\") instead of exact ones, the method and hint it " +
        "names, and the rounding it asks for (such as \"to 3 significant digits\"). " +
        "Give the method in a few short steps, then the final answer on a last line that starts with \"Answer:\".";

    /// <summary>The fourth request, without a picture: judge the student's answer against the worked-out one.</summary>
    /// <param name="label">The question's label</param>
    /// <param name="question">The question</param>
    /// <param name="student">What the student wrote</param>
    /// <param name="solution">The worked-out solution (method and answer)</param>
    public static string JudgePrompt(string label, string question, string student, string solution) =>
        "A student's homework is being checked. Question " + label + ": " + question + "\nThe student wrote: " + student +
        "\nA worked-out solution:\n" + solution + "\n\nReply with JSON only: {\"verdict\": \"correct\" or \"wrong\" or \"unclear\", " +
        "\"how_to_solve\": \"how you would solve it, in a few short steps ending with the answer\", \"what_went_wrong\": \"what " +
        "the student did wrong, in one or two sentences (empty when correct)\"}. Answers that mean the same are correct (0.5 and " +
        "1/2, \"12 cm\" and 12). A question with many right answers (\"a decimal between 5.63 and 5.68\") is correct when the " +
        "student's answer fits it, even if it differs from the worked-out one. A question that asks for a certain rounding " +
        "(\"to 3 significant digits\") is wrong when the answer is not rounded so. Use \"unclear\" when the student's answer " +
        "has a [?] you cannot judge, or the worked-out solution looks wrong.";

    /// <summary>Read the first request's JSON.</summary>
    /// <param name="reply">The model's reply</param>
    /// <param name="question">The question text</param>
    /// <param name="studentAnswer">What the student wrote ("[blank]" when nothing)</param>
    /// <returns>False when no JSON with an answer could be read</returns>
    public static bool TryParseRead(string reply, out string question, out string studentAnswer)
    {
        question = studentAnswer = null;
        JsonElement root = Json(reply);
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        question = Text(root, "question");
        studentAnswer = Text(root, "student_answer");
        if (studentAnswer == null)
            return false;
        studentAnswer = studentAnswer.Trim();
        if (studentAnswer.Length == 0)
            studentAnswer = "[blank]";
        return true;
    }

    /// <summary>Read the second request: the line that starts with "Answer:" (the last one) is the answer.</summary>
    /// <param name="reply">The model's reply</param>
    /// <param name="answer">The final answer, or null when there is no such line</param>
    public static bool TryParseSolution(string reply, out string answer)
    {
        answer = null;
        foreach (string line in Enumerable.Reverse((reply ?? string.Empty).Split('\n')))
        {
            Match match = Regex.Match(line.Trim().TrimStart('*', '#', ' '), @"^\**\s*(final\s+)?answer\s*:?\**\s*:?\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                answer = match.Groups[2].Value.Trim().Trim('*').Trim();
                return answer.Length > 0;
            }
        }
        return false;
    }

    /// <summary>Read the third request's JSON.</summary>
    /// <param name="reply">The model's reply</param>
    /// <param name="verdict">The verdict (<see cref="HomeworkVerdict.Unclear"/> when it isn't one of the three)</param>
    /// <param name="howToSolve">How to solve it</param>
    /// <param name="whatWentWrong">What the student did wrong</param>
    /// <returns>False when no JSON could be read</returns>
    public static bool TryParseJudgement(string reply, out HomeworkVerdict verdict, out string howToSolve, out string whatWentWrong)
    {
        verdict = HomeworkVerdict.Unclear;
        howToSolve = whatWentWrong = null;
        JsonElement root = Json(reply);
        if (root.ValueKind != JsonValueKind.Object)
            return false;
        string word = (Text(root, "verdict") ?? string.Empty).Trim().ToLowerInvariant();
        verdict = word == "correct" || word == "right" ? HomeworkVerdict.Correct : word == "wrong" || word == "incorrect" ? HomeworkVerdict.Wrong : HomeworkVerdict.Unclear;
        howToSolve = Text(root, "how_to_solve") ?? string.Empty;
        whatWentWrong = Text(root, "what_went_wrong") ?? string.Empty;
        return word.Length > 0;
    }

    /// <summary>
    /// Whether two answers are the same without asking a model: equal after dropping case, spaces and a unit, or, when each has
    /// a number, the same number ("15.7 cm²" and "15.70", "1,234" and "1234").
    /// </summary>
    /// <param name="student">What the student wrote</param>
    /// <param name="solved">The worked-out answer</param>
    public static bool AnswersMatch(string student, string solved)
    {
        if (string.IsNullOrWhiteSpace(student) || string.IsNullOrWhiteSpace(solved))
            return false;
        string a = Normalize(student), b = Normalize(solved);
        if (a.Length > 0 && a == b)
            return true;
        return TryNumber(student, out double x) && TryNumber(solved, out double y) && Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)));
    }

    private static string Normalize(string text) =>
        Regex.Replace(Regex.Replace(text.ToLowerInvariant().Replace("$", string.Empty), @"[\s,]+", string.Empty), @"[^0-9a-z./^²³-]", string.Empty);

    // The answer's only number ("15.7 cm²" is 15.7); an answer with several numbers, a fraction or an expression isn't compared.
    private static bool TryNumber(string text, out double number)
    {
        number = 0;
        MatchCollection numbers = Regex.Matches(text.Replace(",", string.Empty), @"-?\d+(\.\d+)?");
        if (numbers.Count != 1 || text.Contains("/") && !Regex.IsMatch(text, @"/\s*(h|hr|hour|min|s|sec)\b", RegexOptions.IgnoreCase))
            return false;
        string rest = Regex.Replace(text, @"-?\d+([.,]\d+)?", string.Empty);
        // Only a unit around the number (letters, a currency sign, ², %); not "=" or other arithmetic.
        if (Regex.IsMatch(rest, @"[=+*×x÷^√()]") && !Regex.IsMatch(rest, @"^[\s$€£a-zA-Z/².%°-]*$"))
            return false;
        return double.TryParse(numbers[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    private static JsonElement Json(string reply)
    {
        string text = reply ?? string.Empty;
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return default;
        try
        {
            using JsonDocument document = JsonDocument.Parse(text.Substring(start, end - start + 1), new JsonDocumentOptions { AllowTrailingCommas = true });
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;
}
