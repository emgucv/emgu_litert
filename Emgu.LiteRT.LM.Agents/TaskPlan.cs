// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents;

// Task mode (TODO item 23): a request is first broken into a short numbered plan, shown as a checklist, then each step runs
// on its own, with the plan and the earlier steps' short results - not their tool output - so a small model's window isn't
// filled. No MAUI types: linked into the tests.
public sealed class TaskPlan
{
    public const int MaxSteps = 8;
    // What of a step's reply is kept for the later steps and the final answer.
    public const int ResultLength = 700;

    public enum State { Pending, Running, Done, Skipped, Failed }

    public sealed class Step
    {
        public string Text;
        public State State;
        public string Result;
    }

    public List<Step> Steps { get; } = new List<Step>();

    public TaskPlan(IEnumerable<string> steps)
    {
        foreach (string text in steps)
            Steps.Add(new Step { Text = text });
    }

    private static readonly Regex ListLine = new Regex(@"^\s*(?:\d{1,2}\s*[.)]|[-*•])\s+(?<text>\S.*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The steps of a plan the model wrote: its numbered (or bulleted) lines, without Markdown emphasis; other lines - an
    /// introduction, "Here is the plan:" - are left out, and a line that continues the one before it joins it. At most
    /// <see cref="MaxSteps"/>, each at most 200 characters. Empty if the text has no list.
    /// </summary>
    public static List<string> Parse(string text)
    {
        var steps = new List<string>();
        foreach (string raw in (text ?? string.Empty).Replace("\r", "").Split('\n'))
        {
            Match match = ListLine.Match(raw);
            if (match.Success)
            {
                string step = Clean(match.Groups["text"].Value);
                if (step.Length > 0)
                    steps.Add(step);
            }
            else if (steps.Count > 0 && raw.Length > 0 && char.IsWhiteSpace(raw[0]) && raw.Trim().Length > 0)
                steps[steps.Count - 1] = Clean(steps[steps.Count - 1] + " " + raw.Trim()); // an indented continuation line
        }
        return steps.Select(s => s.Length > 200 ? s.Substring(0, 199).TrimEnd() + "…" : s).Take(MaxSteps).ToList();
    }

    private static string Clean(string step) =>
        Regex.Replace(Regex.Replace(step, @"\*\*|__|`", string.Empty), @"\s+", " ").Trim().TrimEnd(':');

    /// <summary>The steps as the lines of the editor: one per line.</summary>
    public static string ToLines(IEnumerable<string> steps) => string.Join("\n", steps);

    /// <summary>The steps of an edited list - one per line, numbers and bullets allowed.</summary>
    public static List<string> FromLines(string lines) =>
        (lines ?? string.Empty).Replace("\r", "").Split('\n')
            .Select(l => Clean(Regex.Replace(l, @"^\s*(?:\d{1,2}\s*[.)]|[-*•])\s*", string.Empty)))
            .Where(l => l.Length > 0).Take(MaxSteps).ToList();

    /// <summary>What the model is asked to write the plan: the tools it will have, then the request.</summary>
    public static string PlanPrompt(string request, string tools) =>
        "You plan tasks for an assistant. Break the request below into 3 to 7 short steps, each one concrete action the " +
        "assistant can do with its tools (" + tools + ") or by writing, whose result the next steps or the answer need. " +
        "Put research and checks first, and last the step that produces what the user asked for (the document, the list, the " +
        "recommendation). Write the steps in the language of the request. Reply with only the numbered list, one line per " +
        "step, nothing else.\n\nRequest: " + request;

    /// <summary>The message for the step at this index (0-based): the task, the plan, the results so far and the step.</summary>
    public string StepPrompt(string request, int index)
    {
        var text = new StringBuilder();
        text.AppendLine("You are carrying out a task one step at a time.");
        text.AppendLine("Task: " + request);
        text.AppendLine("Plan:");
        for (int i = 0; i < Steps.Count; i++)
            text.AppendLine((i + 1) + ". " + Steps[i].Text + (Steps[i].State == State.Skipped ? " (skipped)" : string.Empty));
        List<Step> before = Steps.Take(index).Where(s => s.State == State.Done && !string.IsNullOrWhiteSpace(s.Result)).ToList();
        if (before.Count > 0)
        {
            text.AppendLine("Results of the earlier steps:");
            for (int i = 0; i < index; i++)
                if (Steps[i].State == State.Done && !string.IsNullOrWhiteSpace(Steps[i].Result))
                    text.AppendLine("Step " + (i + 1) + ": " + Steps[i].Result);
        }
        text.AppendLine("Now do step " + (index + 1) + ": " + Steps[index].Text);
        text.Append("Use your tools when they help. Then reply with only the result of this step - the facts, numbers or names " +
            "the next steps need - in at most 5 short sentences, in the language of the task. If the step is to create a " +
            "document, create it with the tool and say what it contains.");
        return text.ToString();
    }

    /// <summary>The message that writes the final answer from the results.</summary>
    public string FinalPrompt(string request)
    {
        var text = new StringBuilder();
        text.AppendLine("The user asked: " + request);
        text.AppendLine("These steps were carried out, with their results:");
        for (int i = 0; i < Steps.Count; i++)
        {
            Step step = Steps[i];
            string result = step.State == State.Done ? (string.IsNullOrWhiteSpace(step.Result) ? "(done)" : step.Result)
                : step.State == State.Skipped ? "(skipped by the user)" : "(failed: " + (step.Result ?? "no result") + ")";
            text.AppendLine((i + 1) + ". " + step.Text + " - " + result);
        }
        text.Append("Now write the final answer for the user: complete and well organised, in the language of the request. " +
            "Use only these results, and say so where a step was skipped or failed. Don't call tools unless something " +
            "essential is missing.");
        return text.ToString();
    }

    /// <summary>A step's reply kept short for the later steps.</summary>
    public static string ShortResult(string reply)
    {
        string text = Regex.Replace(reply ?? string.Empty, @"\s+", " ").Trim();
        return text.Length <= ResultLength ? text : text.Substring(0, ResultLength - 1).TrimEnd() + "…";
    }

    /// <summary>What the user is told when they stop the task: how far it got, without calling the model again.</summary>
    public string StoppedSummary()
    {
        var text = new StringBuilder("I stopped the task here. ");
        List<Step> done = Steps.Where(s => s.State == State.Done).ToList();
        if (done.Count == 0)
            return text.Append("No step was finished.").ToString();
        text.AppendLine("What was done so far:").AppendLine();
        foreach (Step step in done)
            text.AppendLine("- " + step.Text + (string.IsNullOrWhiteSpace(step.Result) ? string.Empty : ": " + step.Result));
        return text.ToString().TrimEnd();
    }
}
