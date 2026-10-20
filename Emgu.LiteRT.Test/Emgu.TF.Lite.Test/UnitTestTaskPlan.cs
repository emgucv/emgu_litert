// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Maui.Demo.Lite;
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
}
