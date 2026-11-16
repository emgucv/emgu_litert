// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>What checking one question of a homework page found.</summary>
public sealed class HomeworkQuestionResult
{
    /// <summary>How the answer was marked.</summary>
    public HomeworkVerdict Verdict { get; set; }
    /// <summary>The printed question, as the model read it.</summary>
    public string Question { get; set; } = string.Empty;
    /// <summary>What the student wrote ("[blank]" when nothing).</summary>
    public string StudentAnswer { get; set; } = string.Empty;
    /// <summary>How to solve the question (for a wrong answer).</summary>
    public string HowToSolve { get; set; } = string.Empty;
    /// <summary>What the student did wrong.</summary>
    public string WhatWentWrong { get; set; } = string.Empty;
    /// <summary>Why the answer couldn't be marked (<see cref="HomeworkVerdict.Unclear"/>).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Checks one question of a homework page, cut out as a picture, with a chat model: it looks for handwriting (a question with none
/// is not answered), reads the question and the student's
/// answer (with the OCR's lines to help copy the handwriting), solves the question without the answer, and judges the two
/// (<see cref="HomeworkQuestionCheck"/> has the requests). A blank answer isn't judged and an answer equal to the solution is
/// accepted, both without a request. Each request is its own, with no tools and no earlier chat.
/// </summary>
public static class HomeworkQuestionChecker
{
    /// <summary>Told each reply of the model, for diagnosing a check (null: nothing).</summary>
    public static Action<string> Trace { get; set; }

    /// <summary>Check a question.</summary>
    /// <param name="client">The chat model's client</param>
    /// <param name="label">The question's label ("3", "2a")</param>
    /// <param name="crop">The question's picture (JPEG)</param>
    /// <param name="parentCrop">The whole question's picture (JPEG; the same as <paramref name="crop"/> for a question without parts), or null</param>
    /// <param name="text">What the OCR read on each line of the picture, or null</param>
    /// <param name="note">What the user wrote with the photo, or null</param>
    /// <param name="result">Filled in as the check goes on (the question and the answer after the first request, the verdict at the end)</param>
    /// <param name="progress">Told what the check is doing ("reading...", "solving...", "checking...") and when a part of the result changed</param>
    /// <param name="token">Cancels</param>
    public static async Task CheckAsync(IChatClient client, string label, byte[] crop, byte[] parentCrop, IReadOnlyList<string> text, string note,
        HomeworkQuestionResult result, Action<string> progress, CancellationToken token)
    {
        var picture = new[] { new DataContent(crop, "image/jpeg") };
        // Is anything handwritten? Asked for the answer of a blank question the model solves it instead of saying so.
        progress?.Invoke("looking for handwriting...");
        string handwriting = await AskAsync(client, picture, HomeworkQuestionCheck.HandwritingPrompt(), 8, ReasoningEffort.None, token).ConfigureAwait(false);
        Trace?.Invoke(label + " handwriting: " + handwriting);
        if (HomeworkQuestionCheck.ParseYesNo(handwriting) == false)
        {
            result.StudentAnswer = "[blank]";
            result.Verdict = HomeworkVerdict.NotAnswered;
            progress?.Invoke("read");
            return;
        }
        progress?.Invoke("reading...");
        string prompt = HomeworkQuestionCheck.ReadPrompt(label, note, text);
        string read = await AskAsync(client, picture, prompt, 600, ReasoningEffort.None, token).ConfigureAwait(false);
        Trace?.Invoke(label + " read: " + read);
        // A reply that isn't JSON (or has no answer) is asked for once more.
        if (!HomeworkQuestionCheck.TryParseRead(read, out string question, out string student))
            read = await AskAsync(client, picture, prompt + " Reply with the JSON object only, nothing else.", 600, ReasoningEffort.None, token).ConfigureAwait(false);
        if (!HomeworkQuestionCheck.TryParseRead(read, out question, out student))
        {
            result.Verdict = HomeworkVerdict.Unclear;
            result.Reason = "the model's reading of the picture wasn't usable";
            return;
        }
        result.Question = question ?? string.Empty;
        result.StudentAnswer = student;
        progress?.Invoke("read");
        if (student.Equals("[blank]", StringComparison.OrdinalIgnoreCase))
        {
            result.Verdict = HomeworkVerdict.NotAnswered;
            return;
        }
        progress?.Invoke("solving...");
        string solution = await AskAsync(client, new[] { new DataContent(parentCrop ?? crop, "image/jpeg") },
            HomeworkQuestionCheck.SolvePrompt(label, result.Question), 1500, null, token).ConfigureAwait(false);
        if (HomeworkQuestionCheck.TryParseSolution(solution, out string answer) && HomeworkQuestionCheck.AnswersMatch(student, answer))
        {
            result.Verdict = HomeworkVerdict.Correct;
            return;
        }
        progress?.Invoke("checking...");
        string judgePrompt = HomeworkQuestionCheck.JudgePrompt(label, result.Question, student, solution);
        string judgement = await AskAsync(client, null, judgePrompt, 800, ReasoningEffort.None, token).ConfigureAwait(false);
        if (!HomeworkQuestionCheck.TryParseJudgement(judgement, out HomeworkVerdict verdict, out string how, out string wrong))
            judgement = await AskAsync(client, null, judgePrompt + " Reply with the JSON object only, nothing else.", 800, ReasoningEffort.None, token).ConfigureAwait(false);
        if (HomeworkQuestionCheck.TryParseJudgement(judgement, out verdict, out how, out wrong))
        {
            result.Verdict = verdict;
            result.HowToSolve = how;
            result.WhatWentWrong = wrong;
            if (verdict == HomeworkVerdict.Unclear)
                result.Reason = "the answer couldn't be compared with the solution";
        }
        else
        {
            result.Verdict = HomeworkVerdict.Unclear;
            result.Reason = "the check's reply wasn't usable";
        }
    }

    // One request of its own, with the pictures when given; a leading thinking block is dropped from the text.
    private static async Task<string> AskAsync(IChatClient client, IEnumerable<DataContent> images, string prompt, int maxTokens, ReasoningEffort? effort, CancellationToken token)
    {
        var contents = new List<AIContent>();
        if (images != null)
            contents.AddRange(images);
        contents.Add(new TextContent(prompt));
        var options = new ChatOptions { MaxOutputTokens = maxTokens };
        if (effort.HasValue)
            options.Reasoning = new ReasoningOptions { Effort = effort.Value };
        ChatResponse response = await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, contents) }, options, token).ConfigureAwait(false);
        return WithoutThinking(response.Text);
    }

    /// <summary>The text without a leading &lt;think&gt;...&lt;/think&gt; block.</summary>
    /// <param name="text">The model's reply</param>
    public static string WithoutThinking(string text)
    {
        string trimmed = (text ?? string.Empty).TrimStart();
        if (trimmed.StartsWith("<think>", StringComparison.Ordinal))
        {
            int end = trimmed.IndexOf("</think>", StringComparison.Ordinal);
            return end < 0 ? string.Empty : trimmed.Substring(end + "</think>".Length).Trim();
        }
        return trimmed.Trim();
    }
}
