// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// A goal's reminder: due at the user's time only while the goal is still open, its text, and the time in words.
[TestFixture]
public class UnitTestGoalReminders
{
    // Wednesday 14 October 2026, 5 pm; its week started on Monday the 12th.
    private static readonly DateTime Now = new DateTime(2026, 10, 14, 17, 0, 0);
    private static readonly TimeSpan SixPm = new TimeSpan(18, 0, 0);

    private static AgentGoal JumpingJacks() => AgentGoal.FromText("30 jumping jacks a day", new DateTime(2026, 10, 1));

    private static GoalProgress On(DateTime day, double amount) => new GoalProgress("g", amount, day.AddHours(8));

    [Test]
    public void ADailyGoalIsRemindedOnlyOnDaysItIsntDone()
    {
        AgentGoal goal = JumpingJacks();
        Assert.That(goal.Target, Is.EqualTo(30));
        Assert.That(goal.Period, Is.EqualTo(GoalPeriod.Day));
        // Not done today, 6 pm still to come: today.
        Assert.That(GoalReminders.NextDue(goal, SixPm, new GoalProgress[0], Now), Is.EqualTo(Now.Date + SixPm));
        // Partly done (20 of 30): still today.
        Assert.That(GoalReminders.NextDue(goal, SixPm, new[] { On(Now.Date, 20) }, Now), Is.EqualTo(Now.Date + SixPm));
        // Done today: tomorrow.
        Assert.That(GoalReminders.NextDue(goal, SixPm, new[] { On(Now.Date, 20), On(Now.Date, 10) }, Now), Is.EqualTo(Now.Date.AddDays(1) + SixPm));
        // After today's time: tomorrow, across the end of the month and the year too.
        Assert.That(GoalReminders.NextDue(goal, SixPm, null, Now.Date.AddHours(18)), Is.EqualTo(Now.Date.AddDays(1) + SixPm));
        Assert.That(GoalReminders.NextDue(goal, SixPm, null, new DateTime(2026, 12, 31, 23, 0, 0)), Is.EqualTo(new DateTime(2027, 1, 1, 18, 0, 0)));
    }

    [Test]
    public void WeeklyAndMonthlyGoalsAreRemindedAtThePeriodsEndIfShort()
    {
        AgentGoal weekly = AgentGoal.FromText("Run three times a week", new DateTime(2026, 10, 1));
        // Short this week: Sunday the 18th.
        Assert.That(GoalReminders.NextDue(weekly, SixPm, new[] { On(new DateTime(2026, 10, 12), 1) }, Now), Is.EqualTo(new DateTime(2026, 10, 18, 18, 0, 0)));
        // Done this week: next Sunday.
        var three = new[] { On(new DateTime(2026, 10, 12), 1), On(new DateTime(2026, 10, 13), 1), On(new DateTime(2026, 10, 14), 1) };
        Assert.That(GoalReminders.NextDue(weekly, SixPm, three, Now), Is.EqualTo(new DateTime(2026, 10, 25, 18, 0, 0)));
        AgentGoal monthly = AgentGoal.FromText("Read 4 books a month", new DateTime(2026, 10, 1));
        Assert.That(GoalReminders.NextDue(monthly, SixPm, null, Now), Is.EqualTo(new DateTime(2026, 10, 31, 18, 0, 0)));
        Assert.That(GoalReminders.NextDue(monthly, SixPm, new[] { On(new DateTime(2026, 10, 3), 4) }, Now), Is.EqualTo(new DateTime(2026, 11, 30, 18, 0, 0)));
    }

    [Test]
    public void GoalsWithoutAPeriodOrTargetAreRemindedUntilDone()
    {
        // Every day nothing was logged; a goal without a period stops once its target is reached.
        AgentGoal books = AgentGoal.FromText("Read 12 books", new DateTime(2026, 10, 1));
        Assert.That(books.Period, Is.EqualTo(GoalPeriod.None));
        Assert.That(GoalReminders.NextDue(books, SixPm, new[] { On(Now.Date, 1) }, Now), Is.EqualTo(Now.Date.AddDays(1) + SixPm));
        Assert.That(GoalReminders.NextDue(books, SixPm, new[] { On(new DateTime(2026, 10, 2), 12) }, Now), Is.Null);
        AgentGoal spanish = AgentGoal.FromText("Learn Spanish", new DateTime(2026, 10, 1));
        Assert.That(GoalReminders.NextDue(spanish, SixPm, null, Now), Is.EqualTo(Now.Date + SixPm));
    }

    [Test]
    public void TheTextSaysWhatIsLeftAndTheStreakToKeep()
    {
        AgentGoal goal = JumpingJacks();
        // Done on the three days before today, 10 so far today.
        var progress = new[] { On(Now.Date.AddDays(-3), 30), On(Now.Date.AddDays(-2), 30), On(Now.Date.AddDays(-1), 30), On(Now.Date, 10) };
        (string title, string text) = GoalReminders.Text(goal, progress, Now.Date + SixPm);
        Assert.That(title, Is.EqualTo("30 jumping jacks a day"));
        Assert.That(text, Is.EqualTo("20 jumping jacks to go today · keep your 3-day streak"));
        Assert.That(GoalReminders.Text(goal, null, Now.Date + SixPm).Text, Is.EqualTo("30 jumping jacks to go today"));
        Assert.That(GoalReminders.Text(AgentGoal.FromText("Learn Spanish", Now), null, Now).Text, Is.EqualTo("Time for it today"));
    }

    [Test]
    public void ReadsATimeOfDay()
    {
        foreach ((string words, int hour, int minute) in new[] { ("7 pm", 19, 0), ("7:30pm", 19, 30), ("19:00", 19, 0), ("6.15", 6, 15),
            ("at 8 a.m.", 8, 0), ("12 am", 0, 0), ("12pm", 12, 0), ("noon", 12, 0), ("midnight", 0, 0), ("5", 17, 0), ("7", 7, 0), ("8 o'clock", 8, 0) })
        {
            Assert.That(GoalReminders.TryParseTime(words, out TimeSpan time), Is.True, words);
            Assert.That(time, Is.EqualTo(new TimeSpan(hour, minute, 0)), words);
        }
        foreach (string bad in new[] { "", "soon", "25:00", "13 pm", "7:75", null })
            Assert.That(GoalReminders.TryParseTime(bad, out _), Is.False, bad ?? "null");
        Assert.That(GoalReminders.FormatTime(new TimeSpan(18, 0, 0), CultureInfo.InvariantCulture), Is.EqualTo("18:00"));
    }

    [Test]
    public async Task TheToolSetsAndStopsAGoalsReminder()
    {
        var store = new InMemoryAgentGoalStore();
        await store.AddGoalAsync(JumpingJacks());
        var set = new List<(string Goal, TimeSpan? Time)>();
        AIFunction tool = AgentTools.GoalReminder(store, (goal, time) =>
        {
            set.Add((goal.Text, time));
            return Task.FromResult<DateTime?>(time == null ? null : DateTime.Now.Date + time.Value);
        });
        Assert.That(tool.Name, Is.EqualTo("set_goal_reminder"));
        string reply = (await tool.InvokeAsync(new AIFunctionArguments { ["goal"] = "jumping jacks", ["time"] = "7 pm" }))?.ToString();
        Assert.That(reply, Does.Contain("Set a reminder for \"30 jumping jacks a day\" at 19:00 every day"));
        Assert.That(set.Last(), Is.EqualTo(("30 jumping jacks a day", (TimeSpan?)new TimeSpan(19, 0, 0))));
        reply = (await tool.InvokeAsync(new AIFunctionArguments { ["goal"] = "jumping jacks", ["time"] = "off" }))?.ToString();
        Assert.That(reply, Does.StartWith("Turned off the reminder"));
        Assert.That(set.Last().Time, Is.Null);
        reply = (await tool.InvokeAsync(new AIFunctionArguments { ["goal"] = "jumping jacks", ["time"] = "soon" }))?.ToString();
        Assert.That(reply, Does.StartWith("Couldn't read"));
        reply = (await tool.InvokeAsync(new AIFunctionArguments { ["goal"] = "swimming", ["time"] = "7 pm" }))?.ToString();
        Assert.That(reply, Does.StartWith("No goal matches"));
        Assert.That(set.Count, Is.EqualTo(2));
    }

    // A goal set for a while ("for 30 days") ends: its history, streak and reminders stop, and its status says so.
    [Test]
    public void AGoalSetForAWhileEnds()
    {
        DateTime set = new DateTime(2026, 10, 1, 9, 0, 0);
        AgentGoal goal = AgentGoal.FromText("30 jumping jacks a day for 30 days", set);
        Assert.That(goal.Target, Is.EqualTo(30), "the duration isn't the target");
        Assert.That(goal.Unit, Is.EqualTo("jumping jacks"));
        Assert.That(goal.Period, Is.EqualTo(GoalPeriod.Day));
        Assert.That(goal.Ends, Is.EqualTo(new DateTime(2026, 10, 31)), "1 October and the 29 days after it");
        Assert.That(GoalParser.EndOf("Run three times a week for the next two weeks", set), Is.EqualTo(new DateTime(2026, 10, 15)));
        Assert.That(GoalParser.EndOf("Read 4 books a month for a month", set), Is.EqualTo(new DateTime(2026, 11, 1)));
        Assert.That(GoalParser.EndOf("Run for 30 minutes a day", set), Is.Null);
        Assert.That(AgentGoal.FromText("Run for 30 minutes a day", set).Ends, Is.Null);

        // Reminders up to the last day only.
        Assert.That(GoalReminders.NextDue(goal, SixPm, null, new DateTime(2026, 10, 30, 12, 0, 0)), Is.EqualTo(new DateTime(2026, 10, 30, 18, 0, 0)));
        Assert.That(GoalReminders.NextDue(goal, SixPm, null, new DateTime(2026, 10, 30, 19, 0, 0)), Is.Null);
        Assert.That(GoalReminders.NextDue(goal, SixPm, null, new DateTime(2026, 11, 5)), Is.Null);

        // Done on the last 3 days; looked at a week later: no period goes on, the streak is the last days'.
        var progress = Enumerable.Range(28, 3).Select(d => new GoalProgress("g", 30, new DateTime(2026, 10, d, 8, 0, 0))).ToList();
        GoalHistory history = GoalHistory.Compute(goal, progress, new DateTime(2026, 11, 7), 40);
        Assert.That(history.Periods.Count, Is.EqualTo(30));
        Assert.That(history.Periods.Any(p => p.IsCurrent), Is.False);
        Assert.That(history.Periods.Last().Start, Is.EqualTo(new DateTime(2026, 10, 30)));
        Assert.That(history.CurrentStreak, Is.EqualTo(3));
        Assert.That(history.PeriodsMet, Is.EqualTo(3));

        var store = new InMemoryAgentGoalStore();
        string id = store.AddGoalAsync(goal).GetAwaiter().GetResult();
        foreach (GoalProgress p in progress) store.AddProgressAsync(new GoalProgress(id, p.Amount, p.When)).GetAwaiter().GetResult();
        AgentGoal saved = store.GetGoalsAsync().GetAwaiter().GetResult().Single();
        Assert.That(AgentTools.GoalStatusAsync(store, saved, new DateTime(2026, 10, 29, 20, 0, 0)).GetAwaiter().GetResult(), Does.Contain("until 30 October"));
        string over = AgentTools.GoalStatusAsync(store, saved, new DateTime(2026, 11, 7)).GetAwaiter().GetResult();
        Assert.That(over, Does.Contain("finished on 30 October (reached 3 of 30 days)"));
        Assert.That(over, Does.Not.Contain("today"));
    }
}
