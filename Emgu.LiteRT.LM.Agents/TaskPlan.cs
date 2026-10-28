// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents;

// Task mode (TODO item 23): a request is first broken into a short numbered plan, shown as a checklist, then each step runs
// on its own, with the plan and the earlier steps' short results - not their tool output - so a small model's window isn't
// filled. Research mode is the same with research prompts: the question becomes sub-questions, each one is searched and a
// page read, its notes kept with the pages' addresses (numbered sources), and the answer is written from the notes, citing
// them, with the sources listed after it. No MAUI types: linked into the tests.
public sealed class TaskPlan
{
    public const int MaxSteps = 8;
    // Research: at most this many sub-questions, and at most this many tool calls (searches and page reads) in one.
    public const int MaxResearchSteps = 6;
    public const int MaxResearchToolCalls = 6;
    // A Tavily search at "advanced" depth costs 2 credits, a page read about 0.4: two of each per sub-question, at most.
    public const int ResearchCreditsPerStep = 5;
    // What of a step's reply is kept for the later steps and the final answer (research notes keep more).
    public const int ResultLength = 700;
    public const int ResearchNotesLength = 1200;

    public enum State { Pending, Running, Done, Skipped, Failed }

    public sealed class Step
    {
        public string Text;
        public State State;
        public string Result;
    }

    public List<Step> Steps { get; } = new List<Step>();

    // Research mode (the prompts, the notes' length, the sources).
    public bool Research { get; }

    public sealed class Source
    {
        public string Title;
        public string Url;
    }

    // The pages the research drew on, numbered from 1 in the order first used; each step's are SourcesOf(step).
    public List<Source> Sources { get; } = new List<Source>();
    private readonly Dictionary<Step, List<int>> _stepSources = new Dictionary<Step, List<int>>();

    public TaskPlan(IEnumerable<string> steps, bool research = false)
    {
        Research = research;
        foreach (string text in steps)
            Steps.Add(new Step { Text = text });
    }

    /// <summary>Record pages a step drew on; each address gets a number (the same one if an earlier step used it).</summary>
    public void AddSources(Step step, IEnumerable<(string Title, string Url)> pages)
    {
        if (!_stepSources.TryGetValue(step, out List<int> numbers))
            _stepSources[step] = numbers = new List<int>();
        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Url))
                continue;
            int index = Sources.FindIndex(s => string.Equals(s.Url, page.Url, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                Sources.Add(new Source { Title = string.IsNullOrWhiteSpace(page.Title) ? HostOf(page.Url) : page.Title.Trim(), Url = page.Url });
                index = Sources.Count - 1;
            }
            else if (!string.IsNullOrWhiteSpace(page.Title) && Sources[index].Title == HostOf(page.Url))
                Sources[index].Title = page.Title.Trim();
            if (!numbers.Contains(index + 1))
                numbers.Add(index + 1);
        }
    }

    /// <summary>The numbers (from 1) of the sources a step drew on.</summary>
    public IReadOnlyList<int> SourcesOf(Step step) => _stepSources.TryGetValue(step, out List<int> numbers) ? numbers : new List<int>();

    private static string HostOf(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri.Host.Replace("www.", string.Empty) : url;

    private static readonly Regex SearchResultPage = new Regex(@"^Page: (?<title>.*) \((?<url>https?://[^\s)]+)\)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>The pages a search_web result lists ("Page: title (address)" lines), in its order.</summary>
    public static List<(string Title, string Url)> SearchResultPages(string result) =>
        SearchResultPage.Matches(result ?? string.Empty).Cast<Match>().Select(m => (m.Groups["title"].Value, m.Groups["url"].Value)).ToList();

    /// <summary>What the model is asked to break a question into sub-questions to research.</summary>
    public static string ResearchPlanPrompt(string question) =>
        "You plan web research. Break the question below into 3 to " + MaxResearchSteps + " sub-questions - 4 is usually right - " +
        "each one that a web search and reading one page can answer, together covering what the question needs (for a " +
        "comparison: each item's specifications, then price and availability, then what reviews say). Write them in the " +
        "language of the question. Reply with only the numbered list, one line per sub-question, nothing else.\n\nQuestion: " + question;

    private static readonly Regex ListLine = new Regex(@"^\s*(?:\d{1,2}\s*[.)]|[-*•])\s+(?<text>\S.*)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The steps of a plan the model wrote: its numbered (or bulleted) lines, without Markdown emphasis; other lines - an
    /// introduction, "Here is the plan:" - are left out, and a line that continues the one before it joins it. At most
    /// <see cref="MaxSteps"/>, each at most 200 characters. Empty if the text has no list.
    /// </summary>
    public static List<string> Parse(string text) => Parse(text, MaxSteps);

    /// <summary>The steps of a plan, at most <paramref name="max"/> (see <see cref="Parse(string)"/>).</summary>
    public static List<string> Parse(string text, int max)
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
        return steps.Select(s => s.Length > 200 ? s.Substring(0, 199).TrimEnd() + "…" : s).Take(max).ToList();
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
        if (Research)
            return ResearchStepPrompt(request, index);
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

    // A sub-question's message: the question, the sub-questions, the notes so far (short), and how to research this one.
    private string ResearchStepPrompt(string question, int index)
    {
        var text = new StringBuilder();
        text.AppendLine("You are researching a question on the web, one part at a time.");
        text.AppendLine("Question: " + question);
        text.AppendLine("Parts:");
        for (int i = 0; i < Steps.Count; i++)
            text.AppendLine((i + 1) + ". " + Steps[i].Text + (Steps[i].State == State.Skipped ? " (skipped)" : string.Empty));
        text.AppendLine("Now research part " + (index + 1) + ": " + Steps[index].Text);
        text.Append("Search the web with search_web (add \"specs\", \"review\" or \"price\" where it fits), then read the most " +
            "relevant page with read_web_page, saying in lookFor what you need. Use at most 2 searches and 2 page reads. Then reply " +
            "with only your notes for this part: the facts, numbers and names you found, in at most 8 short sentences, in the " +
            "language of the question. Write \"not found\" for what the pages don't give; never guess.");
        return text.ToString();
    }

    /// <summary>The message that writes the final answer from the results.</summary>
    public string FinalPrompt(string request)
    {
        if (Research)
            return ResearchFinalPrompt(request);
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

    // The answer from the notes: each part's notes with its sources' numbers; cite [n]; the list itself is added after it.
    private string ResearchFinalPrompt(string question)
    {
        var text = new StringBuilder();
        text.AppendLine("The user asked: " + question);
        text.AppendLine("Notes from web research, part by part, with the numbers of the pages they came from:");
        for (int i = 0; i < Steps.Count; i++)
        {
            Step step = Steps[i];
            IReadOnlyList<int> sources = SourcesOf(step);
            string from = sources.Count > 0 ? " [sources " + string.Join(", ", sources) + "]" : string.Empty;
            string notes = step.State == State.Done ? (string.IsNullOrWhiteSpace(step.Result) ? "(nothing found)" : step.Result)
                : step.State == State.Skipped ? "(skipped by the user)" : "(failed: " + (step.Result ?? "no result") + ")";
            text.AppendLine((i + 1) + ". " + step.Text + from + ": " + notes);
        }
        text.Append("Now write the answer for the user, in the language of the question: a two-sentence summary first, then the " +
            "details - as a Markdown table when comparing things. Use only these notes. After a fact, cite its page as [n], using " +
            "only the numbers above. Write \"not found\" where the notes have nothing, and don't add facts they don't give. " +
            "Don't list the sources at the end - they are added after your answer. Don't call tools.");
        return text.ToString();
    }

    /// <summary>The sources, as a Markdown list to put after the answer (a blank line, "**Sources**", then "1. [title](address)"); empty without sources.</summary>
    public string SourcesMarkdown()
    {
        if (Sources.Count == 0)
            return string.Empty;
        var text = new StringBuilder("\n\n**Sources**\n");
        for (int i = 0; i < Sources.Count; i++)
            text.Append(i + 1).Append(". [").Append(Sources[i].Title.Replace("[", "(").Replace("]", ")")).Append("](").Append(Sources[i].Url).Append(")\n");
        return text.ToString().TrimEnd();
    }

    private static readonly Regex SourcesHeading = new Regex(@"^\s*(#{1,6}\s*)?(\*\*|__)?\s*(sources|references)\s*:?\s*(\*\*|__)?\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a line of the report starts a sources section the model wrote itself ("Sources:", "**References**", "## Sources")
    /// although it was told not to: the real list (<see cref="SourcesMarkdown"/>) follows the answer, so it and the rest are
    /// dropped - Gemma 4 E2B wrote "Sources:" and bare "[1]" lines above it.
    /// </summary>
    public static bool IsSourcesHeading(string line) => line != null && SourcesHeading.IsMatch(line);

    /// <summary>A step's reply kept short for the later steps (research notes longer).</summary>
    public string ShortResultFor(string reply) => Shorten(reply, Research ? ResearchNotesLength : ResultLength);

    /// <summary>A step's reply kept short for the later steps.</summary>
    public static string ShortResult(string reply) => Shorten(reply, ResultLength);

    private static string Shorten(string reply, int length)
    {
        string text = Regex.Replace(reply ?? string.Empty, @"\s+", " ").Trim();
        return text.Length <= length ? text : text.Substring(0, length - 1).TrimEnd() + "…";
    }

    /// <summary>What the user is told when they stop the task: how far it got, without calling the model again.</summary>
    public string StoppedSummary()
    {
        var text = new StringBuilder(Research ? "I stopped the research here. " : "I stopped the task here. ");
        List<Step> done = Steps.Where(s => s.State == State.Done).ToList();
        if (done.Count == 0)
            return text.Append("No step was finished.").ToString();
        text.AppendLine("What was done so far:").AppendLine();
        foreach (Step step in done)
            text.AppendLine("- " + step.Text + (string.IsNullOrWhiteSpace(step.Result) ? string.Empty : ": " + step.Result));
        return text.ToString().TrimEnd() + (Research ? SourcesMarkdown() : string.Empty);
    }
}
