using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The goal tools (Emgu.LiteRT.LM.Agents' AgentGoals.cs): targets read from the user's words, days of progress, and the
// tools with an in-memory store; no model or native library.
[TestFixture]
public class UnitTestAgentGoals
{
    // A Wednesday evening.
    private static readonly DateTime Now = new DateTime(2026, 10, 14, 19, 0, 0);

    [Test]
    public void ParsesTargets()
    {
        (string Text, double? Target, string Unit, GoalPeriod Period)[] cases =
        {
            ("Run three times a week", 3, "times", GoalPeriod.Week),
            ("I want to read 20 pages a day", 20, "pages", GoalPeriod.Day),
            ("walk 10,000 steps every day", 10000, "steps", GoalPeriod.Day),
            ("Meditate twice daily", 2, "times", GoalPeriod.Day),
            ("Go to the gym 4x per week", 4, "times", GoalPeriod.Week),
            ("Drink 8 glasses of water a day", 8, "glasses", GoalPeriod.Day),
            ("Read 12 books", 12, "books", GoalPeriod.None),
            ("Cycle 100 km each month", 100, "km", GoalPeriod.Month),
            ("Practise piano 30 minutes every evening", 30, "minutes", GoalPeriod.Day),
            ("Play tennis once a week", 1, "times", GoalPeriod.Week),
            ("Learn Spanish", null, "times", GoalPeriod.None),
        };
        foreach (var c in cases)
        {
            GoalParser.Parse(c.Text, out double? target, out string unit, out GoalPeriod period);
            Assert.That((target, unit, period), Is.EqualTo((c.Target, c.Unit, c.Period)), c.Text);
        }
        Assert.That(AgentGoal.FromText("Read 20 pages a day.", Now).TargetText, Is.EqualTo("20 pages a day"));
        Assert.That(AgentGoal.FromText("Run once a week", Now).TargetText, Is.EqualTo("1 time a week"));
    }

    [Test]
    public void ParsesPastDays()
    {
        (string Text, DateTime Expected)[] cases =
        {
            ("", Now),
            ("today", Now),
            ("this morning", Now),
            ("yesterday", new DateTime(2026, 10, 13, 12, 0, 0)),
            ("last night", new DateTime(2026, 10, 13, 12, 0, 0)),
            ("3 days ago", new DateTime(2026, 10, 11, 12, 0, 0)),
            ("Monday", new DateTime(2026, 10, 12, 12, 0, 0)),
            ("on Saturday", new DateTime(2026, 10, 10, 12, 0, 0)),
            ("Wednesday", Now),
            ("last Wednesday", new DateTime(2026, 10, 7, 12, 0, 0)),
            ("October 1", new DateTime(2026, 10, 1, 12, 0, 0)),
            ("December 24", new DateTime(2025, 12, 24, 12, 0, 0)),
        };
        foreach (var c in cases)
        {
            Assert.That(GoalParser.TryParsePastDay(c.Text, Now, out DateTime when), Is.True, c.Text);
            Assert.That(when, Is.EqualTo(c.Expected), c.Text);
        }
        Assert.That(GoalParser.TryParsePastDay("whenever", Now, out _), Is.False);
    }

    [Test]
    public async Task ToolsKeepGoalsAndProgress()
    {
        InMemoryAgentGoalStore store = new InMemoryAgentGoalStore();
        DateTime now = Now;
        Dictionary<string, AIFunction> tools = new Dictionary<string, AIFunction>();
        foreach (AIFunction tool in AgentTools.Goals(store, () => now))
            tools[tool.Name] = tool;
        Assert.That(tools.Keys, Is.EquivalentTo(new[] { "add_goal", "log_progress", "get_goals", "remove_goal" }));

        async Task<string> Call(string name, params (string, object)[] arguments)
        {
            AIFunctionArguments args = new AIFunctionArguments();
            foreach (var (key, value) in arguments)
                args[key] = value;
            return (await tools[name].InvokeAsync(args))?.ToString();
        }

        Assert.That(await Call("log_progress", ("goal", "run")), Does.Contain("no goals yet"));
        Assert.That(await Call("add_goal", ("goal", "Run three times a week")), Does.Contain("Saved the goal: Run three times a week (target: 3 times a week)"));
        Assert.That(await Call("add_goal", ("goal", "Read 20 pages a day")), Does.Contain("target: 20 pages a day"));
        Assert.That(await Call("add_goal", ("goal", "run 3 times a week")), Does.StartWith("Already a goal: Run three times a week"));

        // "ran" finds the running goal; an earlier day of this week counts, last week's doesn't.
        string result = await Call("log_progress", ("goal", "ran"), ("when", "Monday"));
        Assert.That(result, Does.Contain("Logged 1 time for \"Run three times a week\" (Monday)"));
        Assert.That(result, Does.Contain("1 of 3 times this week"));
        await Call("log_progress", ("goal", "running"), ("when", "last Wednesday"));
        // A distance for a goal counted in times is one time.
        Assert.That(await Call("log_progress", ("goal", "run"), ("amount", 5.0), ("unit", "km"), ("when", "last Wednesday")),
            Does.Contain("Logged 1 time"));
        result = await Call("log_progress", ("goal", "run"));
        Assert.That(result, Does.Contain("2 of 3 times this week, last: today"));
        result = await Call("log_progress", ("goal", "reading"), ("amount", 25.0), ("when", "yesterday"));
        Assert.That(result, Does.Contain("Logged 25 pages for \"Read 20 pages a day\" (yesterday)"));
        Assert.That(result, Does.Contain("0 of 20 pages today, last: yesterday"));
        await Call("log_progress", ("goal", "read"), ("amount", 30.0));
        Assert.That(await Call("log_progress", ("goal", "swim")), Does.Contain("No goal matches \"swim\""));

        string instructions = await AgentTools.GoalInstructionsAsync(store, now);
        Assert.That(instructions, Does.Contain("- Run three times a week - 2 of 3 times this week, last: today"));
        Assert.That(instructions, Does.Contain("- Read 20 pages a day - 30 of 20 pages today (reached), 2-day-streak, last: today"));

        // A new week starts on Monday.
        now = new DateTime(2026, 10, 19, 8, 0, 0);
        Assert.That(await Call("get_goals"), Does.Contain("Run three times a week - 0 of 3 times this week, last: Wednesday"));

        Assert.That(await Call("remove_goal", ("goal", "reading")), Does.StartWith("Deleted the goal: Read 20 pages a day"));
        Assert.That((await store.GetGoalsAsync()).Count, Is.EqualTo(1));
        Assert.That(AgentTools.Describe(new FunctionCallContent("1", "log_progress", new Dictionary<string, object> { ["goal"] = "run" })),
            Is.EqualTo("Logging progress: run"));
    }
}
