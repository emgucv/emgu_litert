//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// How often a goal's target is to be reached.
    /// </summary>
    public enum GoalPeriod
    {
        /// <summary>No period: the target counts everything logged since the goal was set ("read 12 books")</summary>
        None,
        /// <summary>Every day</summary>
        Day,
        /// <summary>Every week (Monday to Sunday)</summary>
        Week,
        /// <summary>Every month</summary>
        Month
    }

    /// <summary>
    /// A goal of the user's: "Run three times a week" - 3 times a week; "Read 20 pages a day" - 20 pages a day; "Learn
    /// Spanish" - no target.
    /// </summary>
    public class AgentGoal
    {
        /// <summary>
        /// Create a goal
        /// </summary>
        /// <param name="id">The store's id of the goal (null for a new one)</param>
        /// <param name="text">The goal in the user's words</param>
        /// <param name="target">How much per period, or null if there is nothing to count</param>
        /// <param name="unit">What is counted: "times", "pages", "km"...</param>
        /// <param name="period">How often the target is to be reached</param>
        /// <param name="created">When the goal was set (local time)</param>
        public AgentGoal(String id, String text, double? target, String unit, GoalPeriod period, DateTime created)
        {
            Id = id;
            Text = text;
            Target = target;
            Unit = String.IsNullOrEmpty(unit) ? "times" : unit;
            Period = period;
            Created = created;
        }

        /// <summary>The store's id of the goal</summary>
        public String Id { get; }

        /// <summary>The goal in the user's words</summary>
        public String Text { get; }

        /// <summary>How much per period, or null if there is nothing to count</summary>
        public double? Target { get; }

        /// <summary>What is counted: "times", "pages", "km"...</summary>
        public String Unit { get; }

        /// <summary>How often the target is to be reached</summary>
        public GoalPeriod Period { get; }

        /// <summary>When the goal was set (local time)</summary>
        public DateTime Created { get; }

        /// <summary>
        /// A goal set now from the user's words, with its target, unit and period read by GoalParser.
        /// </summary>
        /// <param name="text">The goal, e.g. "Read 20 pages a day"</param>
        /// <param name="now">The current local time</param>
        /// <returns>The goal, without an id</returns>
        public static AgentGoal FromText(String text, DateTime now)
        {
            text = (text ?? String.Empty).Trim().TrimEnd('.');
            GoalParser.Parse(text, out double? target, out string unit, out GoalPeriod period);
            return new AgentGoal(null, text, target, unit, period, now);
        }

        /// <summary>
        /// The target in words, e.g. "3 times a week", "20 pages a day", "12 books", or null without a target.
        /// </summary>
        public String TargetText
        {
            get
            {
                if (Target == null)
                    return null;
                string text = GoalParser.Amount(Target.Value, Unit);
                switch (Period)
                {
                    case GoalPeriod.Day: return text + " a day";
                    case GoalPeriod.Week: return text + " a week";
                    case GoalPeriod.Month: return text + " a month";
                    default: return text;
                }
            }
        }

        /// <summary>
        /// When the current period started: today, this Monday or the 1st of this month; for a goal without a period,
        /// DateTime.MinValue (everything logged counts, also days before the goal was set).
        /// </summary>
        /// <param name="now">The current local time</param>
        /// <returns>The start of the period, local time</returns>
        public DateTime PeriodStart(DateTime now)
        {
            switch (Period)
            {
                case GoalPeriod.Day: return now.Date;
                case GoalPeriod.Week: return now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
                case GoalPeriod.Month: return new DateTime(now.Year, now.Month, 1);
                default: return DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// Progress logged toward a goal: an amount in the goal's unit on a day.
    /// </summary>
    public class GoalProgress
    {
        /// <summary>
        /// Create a progress entry
        /// </summary>
        /// <param name="goalId">The goal's id</param>
        /// <param name="amount">How much, in the goal's unit</param>
        /// <param name="when">When it was done (local time)</param>
        public GoalProgress(String goalId, double amount, DateTime when)
        {
            GoalId = goalId;
            Amount = amount;
            When = when;
        }

        /// <summary>The goal's id</summary>
        public String GoalId { get; }

        /// <summary>How much, in the goal's unit</summary>
        public double Amount { get; }

        /// <summary>When it was done (local time)</summary>
        public DateTime When { get; }
    }

    /// <summary>
    /// Where the goal tools (AgentTools.Goals) keep the user's goals and their progress - e.g. a database of the app.
    /// </summary>
    public interface IAgentGoalStore
    {
        /// <summary>All goals, oldest first</summary>
        Task<IList<AgentGoal>> GetGoalsAsync();

        /// <summary>Save a new goal.</summary>
        /// <param name="goal">The goal (its Id is ignored)</param>
        /// <returns>The new goal's id</returns>
        Task<String> AddGoalAsync(AgentGoal goal);

        /// <summary>Change a goal's text, target, unit or period.</summary>
        /// <param name="goal">The goal, with its id</param>
        Task UpdateGoalAsync(AgentGoal goal);

        /// <summary>Delete a goal and its progress.</summary>
        /// <param name="id">Its id</param>
        Task DeleteGoalAsync(String id);

        /// <summary>A goal's progress logged at or after a time, oldest first.</summary>
        /// <param name="goalId">The goal's id</param>
        /// <param name="since">The earliest time (local)</param>
        Task<IList<GoalProgress>> GetProgressAsync(String goalId, DateTime since);

        /// <summary>Log progress toward a goal.</summary>
        /// <param name="progress">The progress</param>
        Task AddProgressAsync(GoalProgress progress);
    }

    /// <summary>
    /// Goals kept in memory, e.g. for a console app or a test. Thread-safe.
    /// </summary>
    public class InMemoryAgentGoalStore : IAgentGoalStore
    {
        private readonly List<AgentGoal> _goals = new List<AgentGoal>();
        private readonly List<GoalProgress> _progress = new List<GoalProgress>();
        private int _nextId = 1;

        /// <inheritdoc />
        public Task<IList<AgentGoal>> GetGoalsAsync()
        {
            lock (_goals)
                return Task.FromResult<IList<AgentGoal>>(_goals.ToList());
        }

        /// <inheritdoc />
        public Task<String> AddGoalAsync(AgentGoal goal)
        {
            lock (_goals)
            {
                string id = (_nextId++).ToString(CultureInfo.InvariantCulture);
                _goals.Add(new AgentGoal(id, goal.Text, goal.Target, goal.Unit, goal.Period, goal.Created));
                return Task.FromResult(id);
            }
        }

        /// <inheritdoc />
        public Task UpdateGoalAsync(AgentGoal goal)
        {
            lock (_goals)
            {
                int index = _goals.FindIndex(g => g.Id == goal.Id);
                if (index >= 0)
                    _goals[index] = goal;
            }
            return Task.FromResult(0);
        }

        /// <inheritdoc />
        public Task DeleteGoalAsync(String id)
        {
            lock (_goals)
            {
                _goals.RemoveAll(g => g.Id == id);
                _progress.RemoveAll(p => p.GoalId == id);
            }
            return Task.FromResult(0);
        }

        /// <inheritdoc />
        public Task<IList<GoalProgress>> GetProgressAsync(String goalId, DateTime since)
        {
            lock (_goals)
                return Task.FromResult<IList<GoalProgress>>(_progress.Where(p => p.GoalId == goalId && p.When >= since).OrderBy(p => p.When).ToList());
        }

        /// <inheritdoc />
        public Task AddProgressAsync(GoalProgress progress)
        {
            lock (_goals)
                _progress.Add(progress);
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Reads a goal's target from the user's words: "run three times a week" - 3 times a week, "read 20 pages a day" -
    /// 20 pages a day, "walk 10,000 steps every day", "meditate twice daily", "read 12 books this year" (12 books, no
    /// period), "learn Spanish" - no target.
    /// </summary>
    public static class GoalParser
    {
        private static readonly Regex PeriodPattern = new Regex(
            @"\b(?:(?:a|an|per|each|every)\s+(day|night|morning|evening|week|month)|(daily|nightly|weekly|monthly|everyday|weekdays))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AmountPattern = new Regex(
            @"(?<![\w.])(\d+(?:[.,]\d+)*|once|twice|thrice|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|fifteen|twenty|thirty|forty|fifty|sixty|hundred)(?:x\b|(?!\w)(?:\s+(?:more\s+)?(\p{L}+))?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, double> Words = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "once", 1 }, { "twice", 2 }, { "thrice", 3 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 },
            { "five", 5 }, { "six", 6 }, { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 },
            { "twelve", 12 }, { "fifteen", 15 }, { "twenty", 20 }, { "thirty", 30 }, { "forty", 40 }, { "fifty", 50 },
            { "sixty", 60 }, { "hundred", 100 }
        };

        // Words after a number that aren't what is counted.
        private static readonly HashSet<string> NotUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "per", "each", "every", "day", "days", "week", "weeks", "month", "months", "daily", "weekly",
            "monthly", "this", "next", "in", "by", "before", "until", "of", "and", "or", "to", "at", "on", "x"
        };

        /// <summary>
        /// Read a goal's target, unit and period.
        /// </summary>
        /// <param name="text">The goal in the user's words</param>
        /// <param name="target">The target, or null if there is no number</param>
        /// <param name="unit">What is counted ("times" if nothing else is named)</param>
        /// <param name="period">How often</param>
        /// <returns>True if a target was found</returns>
        public static bool Parse(String text, out double? target, out String unit, out GoalPeriod period)
        {
            text = text ?? String.Empty;
            target = null;
            unit = "times";
            period = GoalPeriod.None;
            Match periodMatch = PeriodPattern.Match(text);
            if (periodMatch.Success)
            {
                string word = (periodMatch.Groups[1].Success ? periodMatch.Groups[1].Value : periodMatch.Groups[2].Value).ToLowerInvariant();
                period = word.StartsWith("week", StringComparison.Ordinal) && word != "weekdays" ? GoalPeriod.Week
                    : word.StartsWith("month", StringComparison.Ordinal) ? GoalPeriod.Month : GoalPeriod.Day;
            }
            // The period's words aren't the target's unit.
            string rest = periodMatch.Success ? text.Remove(periodMatch.Index, periodMatch.Length).Insert(periodMatch.Index, " , ") : text;
            foreach (Match match in AmountPattern.Matches(rest))
            {
                if (!TryNumber(match.Groups[1].Value, out double number) || number <= 0)
                    continue;
                target = number;
                string word = match.Groups[2].Success ? match.Groups[2].Value.ToLowerInvariant() : null;
                string first = match.Groups[1].Value.ToLowerInvariant();
                if (first == "once" || first == "twice" || first == "thrice")
                    unit = "times";
                else if (word == "time" || word == "times" || word == null || NotUnits.Contains(word))
                    unit = "times";
                else
                    unit = word;
                return true;
            }
            return false;
        }

        /// <summary>
        /// An amount with its unit in words: "1 time", "3 times", "20 pages", "2.5 km".
        /// </summary>
        /// <param name="amount">The amount</param>
        /// <param name="unit">The unit</param>
        /// <returns>The text</returns>
        public static String Amount(double amount, String unit)
        {
            string number = amount.ToString("0.##", CultureInfo.InvariantCulture);
            if (unit == "times" || unit == "time")
                return number + (amount == 1 ? " time" : " times");
            // A crude singular for 1 ("1 page", "1 book"); units such as "km" stay.
            if (amount == 1 && unit.Length > 3 && unit.EndsWith("s", StringComparison.Ordinal) && !unit.EndsWith("ss", StringComparison.Ordinal))
                return number + " " + unit.Substring(0, unit.Length - 1);
            return number + " " + unit;
        }

        private static bool TryNumber(string text, out double number)
        {
            if (Words.TryGetValue(text, out number))
                return true;
            // "10,000" and "2.5"; a comma before exactly three digits groups thousands, else it is a decimal comma.
            string digits = Regex.IsMatch(text, @"^\d{1,3}(,\d{3})+$") ? text.Replace(",", "") : text.Replace(',', '.');
            return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>
        /// Reads the day something was done: "today", "this morning", "tonight", "yesterday", "last night", "2 days ago",
        /// a weekday ("Monday": the last one, today included), or a date that WhenParser reads (moved back a year if it
        /// would be in the future). Empty is now.
        /// </summary>
        /// <param name="text">When, in the user's words</param>
        /// <param name="now">The current local time</param>
        /// <param name="when">The time, if read</param>
        /// <returns>True if the text was read as a time not in the future</returns>
        public static bool TryParsePastDay(String text, DateTime now, out DateTime when)
        {
            string t = Regex.Replace((text ?? String.Empty).Trim().ToLowerInvariant(), @"[.!,]", " ").Trim();
            t = Regex.Replace(t, @"\s+", " ");
            when = now;
            if (t.Length == 0 || t == "now" || t == "today" || t == "just now" || t == "this morning" || t == "this afternoon"
                || t == "this evening" || t == "tonight" || t == "earlier" || t == "earlier today")
                return true;
            if (t == "yesterday" || t == "last night" || t.StartsWith("yesterday ", StringComparison.Ordinal))
            {
                when = now.Date.AddDays(-1).AddHours(12);
                return true;
            }
            Match ago = Regex.Match(t, @"^(\w+) days? ago$");
            if (ago.Success && TryNumber(ago.Groups[1].Value, out double days))
            {
                when = now.Date.AddDays(-days).AddHours(12);
                return true;
            }
            if (t == "the day before yesterday")
            {
                when = now.Date.AddDays(-2).AddHours(12);
                return true;
            }
            string day = Regex.Replace(t, @"^(on |last |this past |past )", "");
            for (int i = 0; i < 7; i++)
            {
                DayOfWeek dayOfWeek = (DayOfWeek)i;
                if (day == dayOfWeek.ToString().ToLowerInvariant())
                {
                    int back = ((int)now.DayOfWeek - i + 7) % 7;
                    if (back == 0 && t.StartsWith("last ", StringComparison.Ordinal))
                        back = 7;
                    when = back == 0 ? now : now.Date.AddDays(-back).AddHours(12);
                    return true;
                }
            }
            if (WhenParser.TryParseDateTime(text, now, out DateTime value, out bool hasTime))
            {
                // WhenParser looks ahead ("March 3" is the next one): progress is in the past.
                if (value > now && value.Date > now.Date)
                    value = value.AddYears(-1);
                if (value > now)
                    value = hasTime ? value.AddDays(-1) : now;
                when = hasTime ? value : value.Date.AddHours(12);
                return true;
            }
            return false;
        }
    }

    public static partial class AgentTools
    {
        /// <summary>
        /// add_goal, log_progress, get_goals and remove_goal: the user's goals ("run three times a week", "read 20 pages a
        /// day") and the progress toward them. Each takes the goal in the user's words: GoalParser reads its target and
        /// period, and log_progress finds the goal by its words. Give the agent the goals with each message too
        /// (GoalInstructionsAsync), so "I ran today" is logged without asking.
        /// </summary>
        /// <param name="store">Where the goals are kept</param>
        /// <param name="now">The current local time; DateTime.Now if null (for tests)</param>
        /// <returns>The tools</returns>
        public static IList<AIFunction> Goals(IAgentGoalStore store, Func<DateTime> now = null)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            Func<DateTime> clock = now ?? (() => DateTime.Now);
            return new List<AIFunction>
            {
                AIFunctionFactory.Create(
                    ([Description("The goal in the user's words, with its number and how often, e.g. \"Run three times a week\", \"Read 20 pages a day\"")] string goal,
                        CancellationToken cancellationToken) => AddGoalAsync(store, goal, clock()),
                    "add_goal",
                    "Saves a new goal the user sets themselves, such as a habit or a target (\"I want to run three times a week\"), so their progress can be tracked across chats."),
                AIFunctionFactory.Create(
                    ([Description("Words from the goal it counts toward, e.g. \"run\", \"read\"")] string goal,
                        [Description("How much, in the goal's unit: for a goal counted in times, how many times (one run is 1, however far); for pages, the pages. 1 if not said")] double amount = 1,
                        [Description("The unit the amount is in, e.g. \"times\", \"pages\", \"km\"; empty for the goal's own")] string unit = "",
                        [Description("When it was done, in the user's words, e.g. \"yesterday\", \"Monday\"; empty for today")] string when = "",
                        CancellationToken cancellationToken = default) => LogProgressAsync(store, goal, amount, unit, when, clock()),
                    "log_progress",
                    "Logs something the user did toward one of their goals (\"I ran today\", \"read 30 pages last night\"). Use it whenever the user says they did something one of their goals counts, without asking first."),
                AIFunctionFactory.Create(
                    (CancellationToken cancellationToken) => GetGoalsAsync(store, clock()),
                    "get_goals",
                    "Lists the user's goals with their progress in the current day, week or month, and when progress was last logged. Use it when the user asks how they are doing."),
                AIFunctionFactory.Create(
                    ([Description("Words from the goal to delete, e.g. \"running\"")] string goal,
                        CancellationToken cancellationToken) => RemoveGoalAsync(store, goal),
                    "remove_goal",
                    "Deletes one of the user's goals and its progress, when the user gives it up or asks to remove it.")
            };
        }

        /// <summary>
        /// The user's goals with their progress in the current period, as instructions for the agent (e.g.
        /// ChatOptions.Instructions), or null if there are none.
        /// </summary>
        /// <param name="store">Where the goals are kept</param>
        /// <param name="now">The current local time</param>
        /// <param name="maxLength">The most characters of goals to include</param>
        /// <returns>The instructions</returns>
        public static async Task<string> GoalInstructionsAsync(IAgentGoalStore store, DateTime now, int maxLength = 800)
        {
            if (store == null)
                return null;
            IList<AgentGoal> goals = await store.GetGoalsAsync().ConfigureAwait(false);
            if (goals.Count == 0)
                return null;
            List<string> lines = new List<string>();
            int length = 0;
            foreach (AgentGoal goal in goals)
            {
                string line = "- " + await GoalStatusAsync(store, goal, now).ConfigureAwait(false);
                if (length + line.Length > maxLength)
                    break;
                lines.Add(line);
                length += line.Length + 1;
            }
            return "The user's goals - when the user says they did something one of them counts (\"I ran today\", \"read 30 " +
                   "pages\"), log it with log_progress without asking; mention a goal only when it is relevant:\n" + string.Join("\n", lines);
        }

        /// <summary>
        /// A goal and its progress in the current period, e.g. "Run three times a week - 1 of 3 times this week (last:
        /// yesterday)".
        /// </summary>
        /// <param name="store">Where the goals are kept</param>
        /// <param name="goal">The goal</param>
        /// <param name="now">The current local time</param>
        /// <returns>The text</returns>
        public static async Task<string> GoalStatusAsync(IAgentGoalStore store, AgentGoal goal, DateTime now)
        {
            IList<GoalProgress> all = await store.GetProgressAsync(goal.Id, DateTime.MinValue).ConfigureAwait(false);
            DateTime start = goal.PeriodStart(now);
            double done = all.Where(p => p.When >= start).Sum(p => p.Amount);
            string text = goal.Text;
            if (goal.Target != null)
            {
                string period = goal.Period == GoalPeriod.Day ? " today" : goal.Period == GoalPeriod.Week ? " this week"
                    : goal.Period == GoalPeriod.Month ? " this month" : " so far";
                text += " - " + done.ToString("0.##", CultureInfo.InvariantCulture) + " of " + GoalParser.Amount(goal.Target.Value, goal.Unit) + period;
                if (done >= goal.Target.Value)
                    text += " (reached)";
            }
            else if (all.Count > 0)
            {
                text += " - logged " + all.Count.ToString(CultureInfo.InvariantCulture) + (all.Count == 1 ? " time" : " times");
            }
            if (all.Count > 0)
                text += goal.Target != null ? ", last: " + DayText(all.Last().When, now) : " (last: " + DayText(all.Last().When, now) + ")";
            else
                text += goal.Target != null ? ", nothing logged yet" : " - nothing logged yet";
            return text;
        }

        private static string DayText(DateTime when, DateTime now)
        {
            int days = (now.Date - when.Date).Days;
            if (days == 0) return "today";
            if (days == 1) return "yesterday";
            if (days < 7) return when.ToString("dddd", CultureInfo.InvariantCulture);
            return when.ToString("d MMMM", CultureInfo.InvariantCulture);
        }

        private const string GoalReplyInstruction = "\nTell the user in one short, encouraging sentence.";

        private static async Task<string> AddGoalAsync(IAgentGoalStore store, string text, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "No goal given: ask the user what their goal is.";
            AgentGoal goal = AgentGoal.FromText(text, now);
            AgentGoal existing = await FindGoalAsync(store, goal.Text).ConfigureAwait(false);
            if (existing != null && ContentWords(goal.Text).IsSubsetOf(ContentWords(existing.Text)))
                return "Already a goal: " + await GoalStatusAsync(store, existing, now).ConfigureAwait(false) + GoalReplyInstruction;
            await store.AddGoalAsync(goal).ConfigureAwait(false);
            return "Saved the goal: " + goal.Text + (goal.TargetText != null ? " (target: " + goal.TargetText + ")" : " (no number to count)")
                + GoalReplyInstruction;
        }

        private static async Task<string> LogProgressAsync(IAgentGoalStore store, string text, double amount, string unit, string when, DateTime now)
        {
            AgentGoal goal = await FindGoalAsync(store, text).ConfigureAwait(false);
            if (goal == null)
            {
                IList<AgentGoal> goals = await store.GetGoalsAsync().ConfigureAwait(false);
                return goals.Count == 0
                    ? "The user has no goals yet. Ask whether they want to set one (add_goal)."
                    : "No goal matches \"" + text + "\". The user's goals: " + string.Join("; ", goals.Select(g => g.Text)) + ".";
            }
            if (amount <= 0 || double.IsNaN(amount) || double.IsInfinity(amount))
                amount = 1;
            // A goal counted in times takes one per activity: Gemma 4 E2B passed the 5 of "a run this morning, 5 km",
            // which logged 5 of 3 runs.
            string given = (unit ?? string.Empty).Trim().ToLowerInvariant();
            if (goal.Unit == "times" && given.Length > 0 && given != "time" && given != "times")
                amount = 1;
            if (!GoalParser.TryParsePastDay(when, now, out DateTime day))
                return "Couldn't read \"" + when + "\" as a day: pass e.g. \"today\", \"yesterday\" or \"Monday\".";
            if (day > now)
                return "That is in the future: progress can only be logged once it is done.";
            await store.AddProgressAsync(new GoalProgress(goal.Id, amount, day)).ConfigureAwait(false);
            string status = await GoalStatusAsync(store, goal, now).ConfigureAwait(false);
            return "Logged " + GoalParser.Amount(amount, goal.Unit) + " for \"" + goal.Text + "\" (" + DayText(day, now) + "). Now: "
                + status + GoalReplyInstruction;
        }

        private static async Task<string> GetGoalsAsync(IAgentGoalStore store, DateTime now)
        {
            IList<AgentGoal> goals = await store.GetGoalsAsync().ConfigureAwait(false);
            if (goals.Count == 0)
                return "The user has no goals yet.";
            List<string> lines = new List<string>();
            foreach (AgentGoal goal in goals)
                lines.Add("- " + await GoalStatusAsync(store, goal, now).ConfigureAwait(false));
            return "Today is " + now.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture) + ". The user's goals:\n" + string.Join("\n", lines);
        }

        private static async Task<string> RemoveGoalAsync(IAgentGoalStore store, string text)
        {
            AgentGoal goal = await FindGoalAsync(store, text).ConfigureAwait(false);
            if (goal == null)
                return "No goal matches \"" + text + "\".";
            await store.DeleteGoalAsync(goal.Id).ConfigureAwait(false);
            return "Deleted the goal: " + goal.Text + "\nTell the user in one short sentence.";
        }

        // The goal whose words best match the text - the most shared content words, alike by SameWord ("ran" - "run",
        // "reading" - "read"), not counting numbers and periods ("this week" would match every weekly goal) - or the
        // only goal when the text has no such words.
        private static async Task<AgentGoal> FindGoalAsync(IAgentGoalStore store, string text)
        {
            IList<AgentGoal> goals = await store.GetGoalsAsync().ConfigureAwait(false);
            if (goals.Count == 0)
                return null;
            HashSet<string> words = GoalWords(text);
            AgentGoal best = null;
            int bestScore = 0;
            foreach (AgentGoal goal in goals)
            {
                HashSet<string> goalWords = GoalWords(goal.Text);
                int score = words.Count(w => goalWords.Any(g => SameWord(w, g)));
                if (score > bestScore)
                {
                    best = goal;
                    bestScore = score;
                }
            }
            if (best == null && goals.Count == 1 && words.Count == 0)
                return goals[0];
            return best;
        }

        /// <summary>
        /// Whether a text mentions one of the goals by its words ("I finished my book" - "Read 12 books"), e.g. to give
        /// the agent the goal tools for it (ToolRouter).
        /// </summary>
        /// <param name="goals">The user's goals</param>
        /// <param name="text">The text</param>
        /// <returns>True if a goal's word is in the text</returns>
        public static bool MentionsGoal(IEnumerable<AgentGoal> goals, string text)
        {
            HashSet<string> words = GoalWords(text);
            return words.Count > 0 && goals != null && goals.Any(g => GoalWords(g.Text).Any(w => words.Any(t => SameWord(t, w))));
        }

        private static HashSet<string> GoalWords(string text)
        {
            HashSet<string> words = ContentWords(text);
            words.RemoveWhere(w => GoalNoiseWords.Contains(w) || char.IsDigit(w[0]));
            return words;
        }

        private static readonly HashSet<string> GoalNoiseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "time", "once", "twice", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "twenty",
            "day", "daily", "week", "weekly", "month", "monthly", "year", "every", "each", "per", "want", "goal", "today",
            "yesterday", "morning", "evening", "night", "last", "did", "done", "again", "more", "least"
        };

        // Words alike enough for a goal: equal, one a prefix of the other ("read" / "reading"), or irregular forms of
        // common habit verbs.
        private static bool SameWord(string a, string b)
        {
            if (a == b)
                return true;
            if (a.Length >= 3 && b.Length >= 3 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)))
                return true;
            string Base(string w) => IrregularVerbs.TryGetValue(w, out string v) ? v : w;
            return Base(a) == Base(b);
        }

        private static readonly Dictionary<string, string> IrregularVerbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ran", "run" }, { "running", "run" }, { "swam", "swim" }, { "swimming", "swim" }, { "rode", "ride" },
            { "ridden", "ride" }, { "wrote", "write" }, { "written", "write" }, { "drank", "drink" }, { "drunk", "drink" },
            { "ate", "eat" }, { "eaten", "eat" }, { "went", "go" }, { "gone", "go" }, { "slept", "sleep" },
            { "spoke", "speak" }, { "spoken", "speak" }, { "sang", "sing" }, { "sung", "sing" }, { "did", "do" },
            { "done", "do" }, { "practised", "practise" }, { "practiced", "practice" }, { "meditated", "meditate" },
            { "cycled", "cycle" }, { "biked", "bike" }, { "walked", "walk" }, { "jogged", "jog" }, { "studied", "study" }
        };
    }
}
