// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// A goal's history by period and its streaks (GoalHistory, TODO item 25).
[TestFixture]
public class UnitTestGoalHistory
{
    // A Wednesday evening; its week started on Monday the 12th.
    private static readonly DateTime Now = new DateTime(2026, 10, 14, 19, 0, 0);

    private static AgentGoal Weekly(DateTime created, double target = 3) =>
        new AgentGoal("g", "Run three times a week", target, "times", GoalPeriod.Week, created);

    // Progress of n runs on each of the days given (Monday-based week offsets from Monday the 12th).
    private static List<GoalProgress> Runs(params (int WeeksAgo, int Day, double Amount)[] entries) =>
        entries.Select(e => new GoalProgress("g", e.Amount, new DateTime(2026, 10, 12).AddDays(-7 * e.WeeksAgo + e.Day).AddHours(8))).ToList();

    [Test]
    public void WeeksAreCountedMondayToSundayWithTheCurrentOneLast()
    {
        // Created 3 weeks ago; weeks: -3 two runs, -2 three runs, -1 four runs, this week one run so far.
        var goal = Weekly(new DateTime(2026, 9, 21));
        var progress = Runs((3, 0, 1), (3, 2, 1), (2, 0, 1), (2, 2, 1), (2, 4, 1), (1, 0, 1), (1, 1, 1), (1, 3, 1), (1, 5, 1), (0, 0, 1));
        GoalHistory history = GoalHistory.Compute(goal, progress, Now);
        Assert.That(history.Periods.Select(p => p.Amount), Is.EqualTo(new[] { 2.0, 3.0, 4.0, 1.0 }));
        Assert.That(history.Periods.Select(p => p.Met), Is.EqualTo(new[] { false, true, true, false }));
        Assert.That(history.Periods.Last().IsCurrent, Is.True);
        Assert.That(history.Periods.Count(p => p.IsCurrent), Is.EqualTo(1));
        Assert.That(history.Periods[0].Start, Is.EqualTo(new DateTime(2026, 9, 21)));
        Assert.That(history.PeriodName, Is.EqualTo("week"));
        Assert.That(history.PeriodsFinished, Is.EqualTo(3));
        Assert.That(history.PeriodsMet, Is.EqualTo(2));
    }

    [Test]
    public void TheCurrentPeriodNeverBreaksAStreakAndCountsOnceReached()
    {
        var goal = Weekly(new DateTime(2026, 9, 21));
        var twoMetWeeks = Runs((2, 0, 3), (1, 0, 3));
        GoalHistory still = GoalHistory.Compute(goal, twoMetWeeks, Now);
        Assert.That(still.CurrentStreak, Is.EqualTo(2));                      // this week isn't over: the streak stands
        Assert.That(still.StreakText, Is.EqualTo("2-week streak"));
        GoalHistory reached = GoalHistory.Compute(goal, twoMetWeeks.Concat(Runs((0, 0, 3))).ToList(), Now);
        Assert.That(reached.CurrentStreak, Is.EqualTo(3));
        Assert.That(reached.BestStreak, Is.EqualTo(3));
        Assert.That(reached.StreakText, Is.EqualTo("3-week streak"));
    }

    [Test]
    public void AMissedWeekBreaksTheStreakAndTheBestIsKept()
    {
        // Met in weeks -5, -4, -3, missed -2, met -1, this week 0 so far.
        var goal = Weekly(new DateTime(2026, 9, 7));
        var progress = Runs((5, 0, 3), (4, 0, 3), (3, 0, 3), (2, 0, 1), (1, 0, 3));
        GoalHistory history = GoalHistory.Compute(goal, progress, Now);
        Assert.That(history.CurrentStreak, Is.EqualTo(1));
        Assert.That(history.BestStreak, Is.EqualTo(3));
        Assert.That(history.StreakText, Is.EqualTo("1-week streak (best: 3)"));
        // Last week missed too: no streak now, the best remains.
        GoalHistory broken = GoalHistory.Compute(goal, Runs((5, 0, 3), (4, 0, 3), (3, 0, 3)), Now);
        Assert.That(broken.CurrentStreak, Is.EqualTo(0));
        Assert.That(broken.StreakText, Is.EqualTo("No streak right now (best: 3)"));
    }

    [Test]
    public void ASinglePeriodIsNoStreakToCheerFor()
    {
        var goal = Weekly(new DateTime(2026, 10, 5));
        GoalHistory history = GoalHistory.Compute(goal, Runs((1, 0, 3)), Now);
        Assert.That(history.CurrentStreak, Is.EqualTo(1));
        Assert.That(history.StreakText, Is.Null);
    }

    [Test]
    public void DailyAndMonthlyGoalsWorkToo()
    {
        var daily = new AgentGoal("d", "Read 20 pages a day", 20, "pages", GoalPeriod.Day, new DateTime(2026, 10, 10));
        var progress = new List<GoalProgress>
        {
            new GoalProgress("d", 25, new DateTime(2026, 10, 10, 21, 0, 0)),
            new GoalProgress("d", 10, new DateTime(2026, 10, 11, 21, 0, 0)),
            new GoalProgress("d", 10, new DateTime(2026, 10, 11, 22, 0, 0)),   // two entries add up
            new GoalProgress("d", 20, new DateTime(2026, 10, 12, 8, 0, 0)),
            new GoalProgress("d", 5, new DateTime(2026, 10, 13, 8, 0, 0)),
            new GoalProgress("d", 30, new DateTime(2026, 10, 14, 8, 0, 0)),
        };
        GoalHistory history = GoalHistory.Compute(daily, progress, Now);
        Assert.That(history.Periods.Select(p => p.Met), Is.EqualTo(new[] { true, true, true, false, true }));
        Assert.That(history.CurrentStreak, Is.EqualTo(1));      // the 13th was missed, today is reached
        Assert.That(history.BestStreak, Is.EqualTo(3));
        Assert.That(history.PeriodName, Is.EqualTo("day"));

        var monthly = new AgentGoal("m", "Cycle 100 km each month", 100, "km", GoalPeriod.Month, new DateTime(2026, 8, 20));
        var months = new List<GoalProgress> { new GoalProgress("m", 120, new DateTime(2026, 8, 25)), new GoalProgress("m", 100, new DateTime(2026, 9, 15)) };
        GoalHistory m = GoalHistory.Compute(monthly, months, Now);
        Assert.That(m.Periods.Select(p => p.Start), Is.EqualTo(new[] { new DateTime(2026, 8, 1), new DateTime(2026, 9, 1), new DateTime(2026, 10, 1) }));
        Assert.That(m.CurrentStreak, Is.EqualTo(2));
        Assert.That(m.StreakText, Is.EqualTo("2-month streak"));
    }

    [Test]
    public void GoalsWithoutATargetOrAPeriodHaveNoHistory()
    {
        var none = new AgentGoal("n", "Read 12 books", 12, "books", GoalPeriod.None, Now);
        var noTarget = new AgentGoal("t", "Learn Spanish", null, "times", GoalPeriod.Week, Now);
        Assert.That(GoalHistory.Compute(none, Runs((0, 0, 1)), Now).Periods, Is.Empty);
        Assert.That(GoalHistory.Compute(noTarget, Runs((0, 0, 1)), Now).Periods, Is.Empty);
        Assert.That(GoalHistory.Compute(none, null, Now).StreakText, Is.Null);
        Assert.Throws<ArgumentNullException>(() => GoalHistory.Compute(null, null, Now));
    }

    [Test]
    public void OnlyTheLatestPeriodsAreKeptButTheStreakUsesAll()
    {
        var goal = Weekly(new DateTime(2026, 1, 5), 1);
        var progress = Enumerable.Range(0, 40).Select(w => new GoalProgress("g", 1, new DateTime(2026, 10, 12).AddDays(-7 * w).AddHours(9))).ToList();
        GoalHistory history = GoalHistory.Compute(goal, progress, Now, 8);
        Assert.That(history.Periods.Count, Is.EqualTo(8));
        Assert.That(history.Periods.Last().IsCurrent, Is.True);
        // 41 weeks since 5 January; the first has no run, the 40 after it (this one included) do.
        Assert.That(history.CurrentStreak, Is.EqualTo(40));
        Assert.That(history.BestStreak, Is.EqualTo(40));
    }

    [Test]
    public void ProgressBeforeTheGoalWasSetCountsAndLinesAreReadable()
    {
        var goal = Weekly(new DateTime(2026, 10, 13));            // set on Tuesday
        var progress = Runs((1, 0, 2), (1, 3, 1), (0, 0, 1));      // runs the week before it was set
        GoalHistory history = GoalHistory.Compute(goal, progress, Now);
        Assert.That(history.Periods.Count, Is.EqualTo(2));
        Assert.That(history.Periods[0].Met, Is.True);
        Assert.That(history.Describe(history.Periods[0], goal), Is.EqualTo("Week of 5 Oct: 3 of 3 times - reached"));
        Assert.That(history.Describe(history.Periods[1], goal), Is.EqualTo("This week: 1 of 3 times"));
    }

    [Test]
    public void TheGoalStatusMentionsAStreak()
    {
        var store = new InMemoryAgentGoalStore();
        var goal = Weekly(new DateTime(2026, 9, 21));
        string id = store.AddGoalAsync(goal).GetAwaiter().GetResult();
        foreach (var p in Runs((2, 0, 3), (1, 0, 3)))
            store.AddProgressAsync(new GoalProgress(id, p.Amount, p.When)).GetAwaiter().GetResult();
        AgentGoal saved = store.GetGoalsAsync().GetAwaiter().GetResult().First();
        string status = AgentTools.GoalStatusAsync(store, saved, Now).GetAwaiter().GetResult();
        Assert.That(status, Does.Contain("2-week-streak"));
        Assert.That(status, Does.Contain("0 of 3 times this week"));
    }
}
