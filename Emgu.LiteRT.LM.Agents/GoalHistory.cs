//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// One day, week or month of a goal: what was logged in it and whether the target was reached.
    /// </summary>
    public class GoalPeriodResult
    {
        /// <summary>When the period started (local time)</summary>
        public DateTime Start { get; set; }

        /// <summary>When the next period starts (local time)</summary>
        public DateTime End { get; set; }

        /// <summary>The total logged in the period, in the goal's unit</summary>
        public double Amount { get; set; }

        /// <summary>Whether the amount reaches the goal's target</summary>
        public bool Met { get; set; }

        /// <summary>Whether this is the period that is still going on</summary>
        public bool IsCurrent { get; set; }
    }

    /// <summary>
    /// A goal's history by period - the periods since it was set (or since its first progress), oldest first, the last one
    /// being the current period - and its streak: how many periods in a row reached the target. The current period doesn't
    /// break a streak while it is still going on, and adds to it once its target is reached.
    /// </summary>
    public class GoalHistory
    {
        /// <summary>The most recent periods, oldest first; the last is the current one</summary>
        public IList<GoalPeriodResult> Periods { get; private set; } = new List<GoalPeriodResult>();

        /// <summary>The periods in a row that reached the target, up to now</summary>
        public int CurrentStreak { get; private set; }

        /// <summary>The longest streak there has been</summary>
        public int BestStreak { get; private set; }

        /// <summary>How many of the finished periods reached the target</summary>
        public int PeriodsMet { get; private set; }

        /// <summary>How many periods are finished</summary>
        public int PeriodsFinished { get; private set; }

        /// <summary>"day", "week" or "month"</summary>
        public string PeriodName { get; private set; }

        /// <summary>
        /// Work out a goal's history. A goal without a target or without a period (nothing to count per period) has none.
        /// </summary>
        /// <param name="goal">The goal</param>
        /// <param name="progress">All the progress logged for it</param>
        /// <param name="now">The current local time</param>
        /// <param name="maxPeriods">The most recent periods to keep in <see cref="Periods"/> (the streaks use all of them)</param>
        /// <returns>The history; no periods if the goal has no target and period</returns>
        public static GoalHistory Compute(AgentGoal goal, IEnumerable<GoalProgress> progress, DateTime now, int maxPeriods = 12)
        {
            if (goal == null)
                throw new ArgumentNullException("goal");
            var history = new GoalHistory { PeriodName = goal.Period == GoalPeriod.Day ? "day" : goal.Period == GoalPeriod.Week ? "week" : "month" };
            if (goal.Target == null || goal.Period == GoalPeriod.None)
                return history;
            List<GoalProgress> entries = (progress ?? Enumerable.Empty<GoalProgress>()).Where(p => p.When <= now).ToList();
            DateTime first = goal.Created;
            if (entries.Count > 0 && entries.Min(p => p.When) < first)
                first = entries.Min(p => p.When);
            DateTime current = goal.PeriodStart(now);
            DateTime start = new AgentGoal(null, string.Empty, goal.Target, goal.Unit, goal.Period, first).PeriodStart(first);
            var periods = new List<GoalPeriodResult>();
            // A goal set in the future (clock changes) still has its current period.
            for (DateTime s = start > current ? current : start; s <= current; s = Next(s, goal.Period))
            {
                DateTime end = Next(s, goal.Period);
                double amount = entries.Where(p => p.When >= s && p.When < end).Sum(p => p.Amount);
                periods.Add(new GoalPeriodResult { Start = s, End = end, Amount = amount, Met = amount >= goal.Target.Value, IsCurrent = s == current });
            }
            int run = 0, best = 0;
            foreach (GoalPeriodResult period in periods)
            {
                run = period.Met ? run + 1 : (period.IsCurrent ? run : 0);
                if (period.Met || !period.IsCurrent)
                    best = Math.Max(best, run);
            }
            // The streak now: the unbroken run that ends with the last finished period, plus the current one if reached.
            int streak = 0;
            for (int i = periods.Count - 1; i >= 0; i--)
            {
                if (periods[i].IsCurrent && !periods[i].Met)
                    continue;
                if (!periods[i].Met)
                    break;
                streak++;
            }
            history.CurrentStreak = streak;
            history.BestStreak = Math.Max(best, streak);
            history.PeriodsFinished = periods.Count(p => !p.IsCurrent);
            history.PeriodsMet = periods.Count(p => !p.IsCurrent && p.Met);
            history.Periods = periods.Skip(Math.Max(0, periods.Count - Math.Max(1, maxPeriods))).ToList();
            return history;
        }

        private static DateTime Next(DateTime start, GoalPeriod period)
        {
            switch (period)
            {
                case GoalPeriod.Day: return start.AddDays(1);
                case GoalPeriod.Week: return start.AddDays(7);
                default: return start.AddMonths(1);
            }
        }

        /// <summary>
        /// The streak in words: "3-week streak (best: 5)", "1-day streak", or null while there isn't one (fewer than two
        /// periods in a row and none before it) - no reason to cheer for one period.
        /// </summary>
        public string StreakText
        {
            get
            {
                if (PeriodName == null || Periods.Count == 0)
                    return null;
                if (CurrentStreak < 2 && BestStreak < 2)
                    return null;
                string text = CurrentStreak >= 1
                    ? CurrentStreak.ToString(CultureInfo.InvariantCulture) + "-" + PeriodName + " streak"
                    : "No streak right now";
                if (BestStreak > CurrentStreak)
                    text += " (best: " + BestStreak.ToString(CultureInfo.InvariantCulture) + ")";
                return text;
            }
        }

        /// <summary>
        /// A line for a period of the history, e.g. "Week of 12 October: 3 of 3 times - reached", "This week: 1 of 3 times".
        /// </summary>
        /// <param name="period">The period</param>
        /// <param name="goal">The goal</param>
        public string Describe(GoalPeriodResult period, AgentGoal goal)
        {
            if (period == null)
                throw new ArgumentNullException("period");
            if (goal == null)
                throw new ArgumentNullException("goal");
            string label = PeriodName == "day" ? period.Start.ToString("ddd d MMM", CultureInfo.InvariantCulture)
                : PeriodName == "week" ? "Week of " + period.Start.ToString("d MMM", CultureInfo.InvariantCulture)
                : period.Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            if (period.IsCurrent)
                label = PeriodName == "day" ? "Today" : PeriodName == "week" ? "This week" : "This month";
            return label + ": " + period.Amount.ToString("0.##", CultureInfo.InvariantCulture) + " of "
                + GoalParser.Amount(goal.Target.Value, goal.Unit) + (period.Met ? " - reached" : string.Empty);
        }
    }
}
