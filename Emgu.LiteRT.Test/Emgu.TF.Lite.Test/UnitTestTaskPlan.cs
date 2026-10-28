// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Task mode's plan: reading what the model wrote, and the messages of the steps (TODO item 23).
[TestFixture]
public class UnitTestTaskPlan
{
    [Test]
    public void ReadsNumberedAndBulletedLinesAndIgnoresTheRest()
    {
        var steps = TaskPlan.Parse("Here is the plan:\n\n1. **Check the weather** in Paris for Sunday:\n2) Find three museums open on Sunday\n- Write the packing list as a document\n\nHope this helps!");
        Assert.That(steps, Is.EqualTo(new[] { "Check the weather in Paris for Sunday", "Find three museums open on Sunday", "Write the packing list as a document" }));
        Assert.That(TaskPlan.Parse("no list here"), Is.Empty);
        Assert.That(TaskPlan.Parse(null), Is.Empty);
        Assert.That(TaskPlan.Parse("3."), Is.Empty);
    }

    [Test]
    public void ContinuationLinesJoinAndLongPlansAreCut()
    {
        var steps = TaskPlan.Parse("1. Look up the opening hours\n   of the Louvre and the Orsay\n2. Summarize");
        Assert.That(steps, Is.EqualTo(new[] { "Look up the opening hours of the Louvre and the Orsay", "Summarize" }));
        string many = string.Join("\n", Enumerable.Range(1, 12).Select(i => i + ". step " + i));
        Assert.That(TaskPlan.Parse(many).Count, Is.EqualTo(TaskPlan.MaxSteps));
        Assert.That(TaskPlan.Parse("1. " + new string('x', 400))[0].Length, Is.EqualTo(200));
    }

    [Test]
    public void TheEditorsLinesRoundTrip()
    {
        var steps = new[] { "Check the weather", "Pick museums" };
        Assert.That(TaskPlan.FromLines(TaskPlan.ToLines(steps)), Is.EqualTo(steps));
        Assert.That(TaskPlan.FromLines("1. a\n\n  2) b  \n- c"), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(TaskPlan.FromLines("   \n"), Is.Empty);
    }

    [Test]
    public void StepPromptsCarryThePlanAndTheEarlierResultsOnly()
    {
        var plan = new TaskPlan(new[] { "Check the weather", "Find museums", "Write the list" });
        plan.Steps[0].State = TaskPlan.State.Done;
        plan.Steps[0].Result = "Sunny, 21 C.";
        plan.Steps[1].State = TaskPlan.State.Skipped;
        string prompt = plan.StepPrompt("Plan a Paris weekend", 2);
        Assert.That(prompt, Does.Contain("Task: Plan a Paris weekend"));
        Assert.That(prompt, Does.Contain("2. Find museums (skipped)"));
        Assert.That(prompt, Does.Contain("Step 1: Sunny, 21 C."));
        Assert.That(prompt, Does.Not.Contain("Step 2:"));
        Assert.That(prompt, Does.Contain("Now do step 3: Write the list"));
        Assert.That(plan.StepPrompt("x", 0), Does.Not.Contain("Results of the earlier steps"));
    }

    [Test]
    public void TheFinalPromptListsEveryStepsFate()
    {
        var plan = new TaskPlan(new[] { "a", "b", "c" });
        plan.Steps[0].State = TaskPlan.State.Done; plan.Steps[0].Result = "ra";
        plan.Steps[1].State = TaskPlan.State.Skipped;
        plan.Steps[2].State = TaskPlan.State.Failed; plan.Steps[2].Result = "no network";
        string prompt = plan.FinalPrompt("do it");
        Assert.That(prompt, Does.Contain("1. a - ra"));
        Assert.That(prompt, Does.Contain("2. b - (skipped by the user)"));
        Assert.That(prompt, Does.Contain("3. c - (failed: no network)"));
        Assert.That(prompt, Does.Contain("The user asked: do it"));
    }

    [Test]
    public void ResultsAreShortAndAStoppedTaskSaysWhatWasDone()
    {
        Assert.That(TaskPlan.ShortResult("  many   spaces\nand lines ").Length, Is.EqualTo("many spaces and lines".Length));
        string result = TaskPlan.ShortResult(new string('y', 2000));
        Assert.That(result.Length, Is.EqualTo(TaskPlan.ResultLength));
        Assert.That(result, Does.EndWith("…"));
        var plan = new TaskPlan(new[] { "a", "b" });
        Assert.That(plan.StoppedSummary(), Does.Contain("No step was finished"));
        plan.Steps[0].State = TaskPlan.State.Done; plan.Steps[0].Result = "ra";
        Assert.That(plan.StoppedSummary(), Does.Contain("- a: ra"));
        Assert.That(TaskPlan.PlanPrompt("req", "weather, search"), Does.Contain("Request: req").And.Contain("(weather, search)"));
    }

    // Research mode: sub-questions, notes with numbered sources, the answer citing them and the sources after it.

    [Test]
    public void ResearchHasAtMostSixParts()
    {
        string written = string.Join("\n", Enumerable.Range(1, 9).Select(i => i + ". Part " + i));
        Assert.That(TaskPlan.Parse(written, TaskPlan.MaxResearchSteps), Has.Count.EqualTo(6));
        Assert.That(TaskPlan.ResearchPlanPrompt("Compare A and B"), Does.Contain("Question: Compare A and B").And.Contain("from 1 to 6"));
        Assert.That(TaskPlan.ResearchPlanPrompt("x"), Does.Contain("never how or where to find it").And.Not.Contain("stock tool"));
        Assert.That(TaskPlan.ResearchPlanPrompt("x", new[] { "search_web", "get_stock_quote" }), Does.Contain("one sub-question per stock"));
    }

    [Test]
    public void ResearchStepsUseTheToolsThatAnswerDirectly()
    {
        var plan = new TaskPlan(new[] { "What was Intel's closing price yesterday?" }, research: true);
        Assert.That(plan.StepPrompt("Intel yesterday", 0), Does.Not.Contain("get_stock_history"), "no stock tools, no hint");
        plan.ToolNames = new[] { "search_web", "read_web_page", "get_stock_quote", "get_stock_history", "get_weather" };
        string prompt = plan.StepPrompt("Intel yesterday", 0);
        Assert.That(prompt, Does.Contain("get_stock_history (a past day or period").And.Contain("get_weather").And.Not.Contain("convert_currency"));
        Assert.That(prompt, Does.Contain("Otherwise search the web"));
        Assert.That(TaskPlan.ToolSource("get_stock_history"), Is.EqualTo(("Twelve Data (stock prices)", "https://twelvedata.com")));
        Assert.That(TaskPlan.ToolSource("search_web"), Is.Null);
        plan.AddSources(plan.Steps[0], new[] { TaskPlan.ToolSource("get_stock_history").Value });
        Assert.That(plan.SourcesMarkdown(), Does.Contain("1. [Twelve Data (stock prices)](https://twelvedata.com)"));
    }

    [Test]
    public void ASearchResultsPagesAreRead()
    {
        string result = "Today is 28 October 2026.\n\nPage: iPhone 17e review (https://example.com/17e)\nPublished: 2026-03-02\nIt has an A19.\n\n" +
            "Page: Compare (16e vs 17e) (https://example.org/compare)\nText.";
        Assert.That(TaskPlan.SearchResultPages(result), Is.EqualTo(new[] { ("iPhone 17e review", "https://example.com/17e"), ("Compare (16e vs 17e)", "https://example.org/compare") }));
        Assert.That(TaskPlan.SearchResultPages("No results found for \"x\"."), Is.Empty);
    }

    [Test]
    public void SourcesAreNumberedOnceAndCitedByPart()
    {
        var plan = new TaskPlan(new[] { "iPhone 16e specs", "iPhone 17e specs" }, research: true);
        plan.AddSources(plan.Steps[0], new[] { ("Apple 16e", "https://apple.com/16e"), ("", "https://www.gsmarena.com/compare") });
        plan.AddSources(plan.Steps[1], new[] { ("GSMArena comparison", "https://www.gsmarena.com/compare"), ("Apple 17e", "https://apple.com/17e") });
        Assert.That(plan.Sources.Select(s => s.Url), Is.EqualTo(new[] { "https://apple.com/16e", "https://www.gsmarena.com/compare", "https://apple.com/17e" }));
        Assert.That(plan.Sources[1].Title, Is.EqualTo("GSMArena comparison"), "a page first seen without a title gets one later");
        Assert.That(plan.SourcesOf(plan.Steps[1]), Is.EqualTo(new[] { 2, 3 }), "the same page keeps its number");

        plan.Steps[0].State = TaskPlan.State.Done;
        plan.Steps[0].Result = "A18, 128 GB base.";
        plan.Steps[1].State = TaskPlan.State.Done;
        plan.Steps[1].Result = "A19, 256 GB base.";
        string final = plan.FinalPrompt("Compare the iPhone 16e and 17e");
        Assert.That(final, Does.Contain("1. iPhone 16e specs [sources 1, 2]: A18, 128 GB base."));
        Assert.That(final, Does.Contain("2. iPhone 17e specs [sources 2, 3]: A19, 256 GB base."));
        Assert.That(final, Does.Contain("[n]").And.Contain("Don't list the sources"));
        Assert.That(plan.SourcesMarkdown(), Is.EqualTo("\n\n**Sources**\n1. [Apple 16e](https://apple.com/16e)\n2. [GSMArena comparison](https://www.gsmarena.com/compare)\n3. [Apple 17e](https://apple.com/17e)"));
        Assert.That(plan.StoppedSummary(), Does.StartWith("I stopped the research here.").And.Contain("**Sources**"));
    }

    [TestCase("Sources:", true)]
    [TestCase("**Sources:**", true)]
    [TestCase("**Sources**", true)]
    [TestCase("## References", true)]
    [TestCase("  sources  ", true)]
    [TestCase("Sources: [1], [2]", false)]
    [TestCase("The sources disagree on the price.", false)]
    [TestCase("| Sources | [1] |", false)]
    public void TheModelsOwnSourcesSectionIsRecognised(string line, bool heading)
    {
        Assert.That(TaskPlan.IsSourcesHeading(line), Is.EqualTo(heading));
    }

    [Test]
    public void ResearchStepsAskForSearchesAndNotesAndKeepMore()
    {
        var plan = new TaskPlan(new[] { "Prices" }, research: true);
        Assert.That(plan.StepPrompt("Compare A and B", 0), Does.Contain("search_web").And.Contain("read_web_page").And.Contain("not found"));
        string long1500 = new string('x', 1500);
        Assert.That(plan.ShortResultFor(long1500).Length, Is.EqualTo(TaskPlan.ResearchNotesLength));
        Assert.That(new TaskPlan(new[] { "a" }).ShortResultFor(long1500).Length, Is.EqualTo(TaskPlan.ResultLength));
        Assert.That(new TaskPlan(new[] { "a" }, research: true).SourcesMarkdown(), Is.Empty);
    }
}
