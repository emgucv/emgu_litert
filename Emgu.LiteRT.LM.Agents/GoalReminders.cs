//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// A goal's reminder, the habit trackers' way: at a time of day the user picks, only while the goal is still open - a
    /// daily goal every day it isn't done yet, a weekly or monthly one on the period's last day if it is still short, a goal
    /// without a period every day nothing was logged (until its target is reached), none after a goal set for a while is
    /// over (<see cref="AgentGoal.Ends"/>) - with the target, what is left and the
    /// streak in its text. An app schedules the next one (<see cref="NextDue"/>) and schedules again when progress is
    /// logged, so a reminder for a goal already done doesn't come.
    /// </summary>
    public static class GoalReminders
    {
        /// <summary>
        /// When the goal's next reminder is due, or null when none is (the goal has nothing left to remind of).
        /// </summary>
        /// <param name="goal">The goal</param>
        /// <param name="time">The time of day the user picked</param>
        /// <param name="progress">All the progress logged for it</param>
        /// <param name="now">The current local time</param>
        /// <returns>The local time it is due, after <paramref name="now"/></returns>
        public static DateTime? NextDue(AgentGoal goal, TimeSpan time, IEnumerable<GoalProgress> progress, DateTime now)
        {
            if (goal == null)
                throw new ArgumentNullException(nameof(goal));
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(time));
            List<GoalProgress> entries = (progress ?? Enumerable.Empty<GoalProgress>()).ToList();
            if (goal.Period == GoalPeriod.None && goal.Target != null && entries.Sum(p => p.Amount) >= goal.Target.Value)
                return null;
            // The first day at or after today whose reminder is still to come and whose period is still open; a year is plenty.
            for (int day = 0; day < 370; day++)
            {
                DateTime date = now.Date.AddDays(day);
                // A goal set for a while reminds until its last day.
                if (goal.Ends is DateTime ends && date >= ends)
                    return null;
                DateTime due = date + time;
                if (due <= now || !RemindsOn(goal, date))
                    continue;
                if (!Done(goal, entries, date))
                    return due;
            }
            return null;
        }

        // A daily goal (and one without a period) reminds every day; a weekly one on Sunday, a monthly one on its last day.
        private static bool RemindsOn(AgentGoal goal, DateTime date)
        {
            switch (goal.Period)
            {
                case GoalPeriod.Week: return date.DayOfWeek == DayOfWeek.Sunday;
                case GoalPeriod.Month: return date.AddDays(1).Month != date.Month;
                default: return true;
            }
        }

        // Whether there's nothing to remind of on that day: the period's target reached (or, without a target, something
        // logged in it). Days ahead are open: their periods haven't started, unless the day is in the current week or month.
        private static bool Done(AgentGoal goal, List<GoalProgress> entries, DateTime date)
        {
            DateTime start = goal.Period == GoalPeriod.None ? date : goal.PeriodStart(date);
            DateTime end = goal.Period == GoalPeriod.Week ? start.AddDays(7) : goal.Period == GoalPeriod.Month ? start.AddMonths(1) : start.AddDays(1);
            double amount = entries.Where(p => p.When >= start && p.When < end).Sum(p => p.Amount);
            if (goal.Target != null && goal.Period != GoalPeriod.None)
                return amount >= goal.Target.Value;
            return amount > 0;
        }

        /// <summary>
        /// The reminder's title and text: the goal, and what is left with the streak to keep, e.g. "30 jumping jacks a day" -
        /// "30 jumping jacks to go today · keep your 4-day streak".
        /// </summary>
        /// <param name="goal">The goal</param>
        /// <param name="progress">All the progress logged for it</param>
        /// <param name="due">When the reminder is due (local time)</param>
        public static (string Title, string Text) Text(AgentGoal goal, IEnumerable<GoalProgress> progress, DateTime due)
        {
            if (goal == null)
                throw new ArgumentNullException(nameof(goal));
            List<GoalProgress> entries = (progress ?? Enumerable.Empty<GoalProgress>()).Where(p => p.When <= due).ToList();
            string when = goal.Period == GoalPeriod.Day ? " today" : goal.Period == GoalPeriod.Week ? " this week"
                : goal.Period == GoalPeriod.Month ? " this month" : string.Empty;
            string text;
            if (goal.Target != null && goal.Period != GoalPeriod.None)
            {
                DateTime start = goal.PeriodStart(due);
                double done = entries.Where(p => p.When >= start).Sum(p => p.Amount);
                double left = Math.Max(0, goal.Target.Value - done);
                text = GoalParser.Amount(left, goal.Unit) + " to go" + when;
            }
            else
            {
                text = "Time for it" + (when.Length > 0 ? when : " today");
            }
            // The streak of the periods before this one: the one to keep going.
            GoalHistory history = GoalHistory.Compute(goal, entries, due);
            if (history.CurrentStreak >= 2)
                text += " · keep your " + history.CurrentStreak.ToString(CultureInfo.InvariantCulture) + "-" + history.PeriodName + " streak";
            return (goal.Text, text);
        }

        private static readonly Regex TimePattern = new Regex(
            @"^\s*(?:at\s+)?(\d{1,2})(?:[:.h](\d{2}))?\s*(a\.?m\.?|p\.?m\.?|o'?clock)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// A time of day in the user's words: "7 pm", "7:30pm", "19:00", "6.15", "noon", "midnight", "8 o'clock" (a bare
        /// hour from 1 to 6 is taken as afternoon, "7" as 7:00).
        /// </summary>
        /// <param name="text">The words</param>
        /// <param name="time">The time of day</param>
        /// <returns>False when it isn't a time</returns>
        public static bool TryParseTime(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            string t = (text ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
            if (t == "noon" || t == "midday") { time = TimeSpan.FromHours(12); return true; }
            if (t == "midnight") return true;
            Match match = TimePattern.Match(t);
            if (!match.Success)
                return false;
            int hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            string suffix = match.Groups[3].Success ? match.Groups[3].Value.Replace(".", string.Empty) : string.Empty;
            if (minute > 59 || hour > 23)
                return false;
            if (suffix == "am" || suffix == "pm")
            {
                if (hour < 1 || hour > 12)
                    return false;
                hour = hour % 12 + (suffix == "pm" ? 12 : 0);
            }
            else if (!match.Groups[2].Success && hour >= 1 && hour <= 6)
            {
                // "remind me at 5": the afternoon, not before dawn.
                hour += 12;
            }
            time = new TimeSpan(hour, minute, 0);
            return true;
        }

        /// <summary>A time of day as "7:00 PM" (or "19:00" in a 24-hour culture).</summary>
        /// <param name="time">The time of day</param>
        /// <param name="culture">The culture, or null for the current one</param>
        public static string FormatTime(TimeSpan time, CultureInfo culture = null) =>
            DateTime.Today.Add(time).ToString((culture ?? CultureInfo.CurrentCulture).DateTimeFormat.ShortTimePattern, culture ?? CultureInfo.CurrentCulture);
    }
}
