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
    /// When a scheduled task runs: every day at a time of day, or every so many hours from a start.
    /// </summary>
    public sealed class TaskSchedule
    {
        /// <summary>The fewest hours between two runs of a task: at most 8 runs a day.</summary>
        public const int MinHours = 3;

        /// <summary>The most hours between two runs of a task: a week.</summary>
        public const int MaxHours = 24 * 7;

        private TaskSchedule(TimeSpan? timeOfDay, int hours, DateTime start)
        {
            TimeOfDay = timeOfDay;
            Hours = hours;
            Start = start;
        }

        /// <summary>Every day at a time of day.</summary>
        /// <param name="time">The time of day</param>
        public static TaskSchedule Daily(TimeSpan time)
        {
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(time));
            return new TaskSchedule(time, 0, DateTime.MinValue);
        }

        /// <summary>Every so many hours, the first run at a start.</summary>
        /// <param name="hours">The hours between two runs, from <see cref="MinHours"/> to <see cref="MaxHours"/></param>
        /// <param name="start">The first run (local time)</param>
        public static TaskSchedule Every(int hours, DateTime start)
        {
            if (hours < MinHours || hours > MaxHours)
                throw new ArgumentOutOfRangeException(nameof(hours));
            return new TaskSchedule(null, hours, start);
        }

        /// <summary>The time of day of a daily task, or null.</summary>
        public TimeSpan? TimeOfDay { get; }

        /// <summary>The hours between two runs, or 0 for a daily task.</summary>
        public int Hours { get; }

        /// <summary>The first run of an every-so-many-hours task (local time).</summary>
        public DateTime Start { get; }

        /// <summary>The first run after a time (local time).</summary>
        /// <param name="after">The time</param>
        public DateTime Next(DateTime after)
        {
            if (TimeOfDay is TimeSpan time)
            {
                // A clock time, not a 24-hour step, so a daylight-saving change keeps it at 7:00.
                DateTime today = after.Date + time;
                return today > after ? today : after.Date.AddDays(1) + time;
            }
            if (Start > after)
                return Start;
            long steps = (long)Math.Floor((after - Start).TotalHours / Hours) + 1;
            return Start.AddHours(steps * Hours);
        }

        /// <summary>The schedule in words: "every day at 7:00 AM", "every 6 hours".</summary>
        public string Describe() => TimeOfDay is TimeSpan time
            ? "every day at " + GoalReminders.FormatTime(time, CultureInfo.InvariantCulture)
            : "every " + Hours.ToString(CultureInfo.InvariantCulture) + " hours";

        /// <summary>The schedule as a line of text, for a store: "daily 07:00" or "every 6 2026-11-05T07:00:00".</summary>
        public string Serialize() => TimeOfDay is TimeSpan time
            ? "daily " + time.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : "every " + Hours.ToString(CultureInfo.InvariantCulture) + " " + Start.ToString("s", CultureInfo.InvariantCulture);

        /// <summary>Read a schedule written by <see cref="Serialize"/>.</summary>
        /// <param name="text">The text</param>
        /// <returns>The schedule, or null if it isn't one</returns>
        public static TaskSchedule Parse(string text)
        {
            string[] parts = (text ?? string.Empty).Split(' ');
            if (parts.Length == 2 && parts[0] == "daily"
                && TimeSpan.TryParseExact(parts[1], @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan time))
                return Daily(time);
            if (parts.Length == 3 && parts[0] == "every"
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours)
                && hours >= MinHours && hours <= MaxHours
                && DateTime.TryParseExact(parts[2], "s", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime start))
                return Every(hours, start);
            return null;
        }
    }

    /// <summary>
    /// A task the agent runs on its own on a schedule ("every morning at 7, the weather in Toronto"), telling the user
    /// with a notification - always, or only when something they asked for holds ("only if it will snow").
    /// </summary>
    public sealed class ScheduledTask
    {
        /// <summary>Create a task.</summary>
        /// <param name="id">Its id in the store, or null for a new one</param>
        /// <param name="description">What to do, in the user's words, e.g. "Tell me the weather in Toronto"</param>
        /// <param name="schedule">When it runs</param>
        /// <param name="notifyWhen">When to tell the user, e.g. "it will snow"; null or empty for every time</param>
        /// <param name="created">When it was made (local time)</param>
        public ScheduledTask(string id, string description, TaskSchedule schedule, string notifyWhen, DateTime created)
        {
            Id = id;
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
            NotifyWhen = string.IsNullOrWhiteSpace(notifyWhen) ? null : notifyWhen.Trim();
            Created = created;
        }

        /// <summary>Its id in the store.</summary>
        public string Id { get; set; }

        /// <summary>What to do, in the user's words.</summary>
        public string Description { get; }

        /// <summary>When it runs.</summary>
        public TaskSchedule Schedule { get; }

        /// <summary>When to tell the user, or null for every time.</summary>
        public string NotifyWhen { get; }

        /// <summary>When it was made (local time).</summary>
        public DateTime Created { get; }

        /// <summary>Whether the user paused it: it doesn't run until resumed.</summary>
        public bool Paused { get; set; }

        /// <summary>When it last ran or was skipped (local time), or null.</summary>
        public DateTime? LastRun { get; set; }

        /// <summary>What happened the last time: the answer's first words, or why it was skipped.</summary>
        public string LastResult { get; set; }

        /// <summary>The saved chat of its last run, or null.</summary>
        public int? LastChatId { get; set; }

        /// <summary>The next run after a time, or null while paused.</summary>
        /// <param name="after">The time (local)</param>
        public DateTime? NextRun(DateTime after) => Paused ? (DateTime?)null : Schedule.Next(after);
    }

    /// <summary>
    /// Where the scheduled tasks (AgentTools.ScheduledTasks) are kept - e.g. a database of the app.
    /// </summary>
    public interface IAgentTaskStore
    {
        /// <summary>All tasks, oldest first</summary>
        Task<IList<ScheduledTask>> GetTasksAsync();

        /// <summary>A task, or null if it is gone.</summary>
        /// <param name="id">Its id</param>
        Task<ScheduledTask> GetTaskAsync(string id);

        /// <summary>Save a new task.</summary>
        /// <param name="task">The task (its Id is ignored)</param>
        /// <returns>The new task's id</returns>
        Task<string> AddTaskAsync(ScheduledTask task);

        /// <summary>Save a task's state: paused, its last run and result.</summary>
        /// <param name="task">The task, with its id</param>
        Task UpdateTaskAsync(ScheduledTask task);

        /// <summary>Delete a task.</summary>
        /// <param name="id">Its id</param>
        Task DeleteTaskAsync(string id);
    }

    /// <summary>
    /// A scheduled task's run without the user: its instructions, and reading the answer - whether to tell the user and
    /// what the notification says.
    /// </summary>
    public static class ScheduledTaskRun
    {
        /// <summary>What an answer starts with when the user's condition doesn't hold: no notification.</summary>
        public const string NothingToReport = "NOTHING TO REPORT";

        /// <summary>The longest notification text, in characters.</summary>
        public const int MaxNotificationLength = 240;

        /// <summary>
        /// The instructions for a run, added to the agent's own: the user isn't there to answer questions, nothing outside
        /// the app may be changed, and - with a condition - how to say that there is nothing to tell.
        /// </summary>
        /// <param name="task">The task</param>
        /// <param name="now">When it runs (local time)</param>
        public static string Instructions(ScheduledTask task, DateTime now)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            string text = "This message is a task the user scheduled earlier (" + task.Schedule.Describe() + "); it is running on its own on " +
                now.ToString("dddd d MMMM yyyy 'at' h:mm tt", CultureInfo.InvariantCulture) + ", and the user isn't here. " +
                "Do the task with your tools and answer in two or three short sentences, which the user reads in a notification. " +
                "Don't ask questions or offer anything, and don't set alarms, add events or change anything outside this app.";
            if (task.NotifyWhen != null)
                text += " The user only wants to hear about it when: " + task.NotifyWhen + ". If that isn't so now, start your answer with \"" +
                    NothingToReport + "\" and say in one sentence why.";
            return text;
        }

        /// <summary>Whether to tell the user: the answer has something to say and doesn't start with <see cref="NothingToReport"/>.</summary>
        /// <param name="task">The task</param>
        /// <param name="answer">The run's answer</param>
        public static bool ShouldNotify(ScheduledTask task, string answer)
        {
            string text = Plain(answer);
            if (text.Length == 0)
                return false;
            return task?.NotifyWhen == null || !StartsWithNothing(text);
        }

        /// <summary>The notification's text: the answer's first sentences, without markup and the marker, at most <see cref="MaxNotificationLength"/> characters.</summary>
        /// <param name="answer">The run's answer</param>
        public static string NotificationText(string answer)
        {
            string text = Plain(answer);
            if (StartsWithNothing(text))
                text = text.Substring(NothingToReport.Length).TrimStart(' ', ':', '.', '-', '–', '—', ',');
            if (text.Length <= MaxNotificationLength)
                return text;
            // Whole sentences while they fit, else words.
            int end = -1;
            foreach (Match m in SentenceEnd.Matches(text))
            {
                if (m.Index + 1 > MaxNotificationLength - 1)
                    break;
                end = m.Index + 1;
            }
            if (end > 0)
                return text.Substring(0, end);
            int space = text.LastIndexOf(' ', MaxNotificationLength - 2);
            return text.Substring(0, space > 0 ? space : MaxNotificationLength - 1).TrimEnd(',', ';', ':') + "…";
        }

        /// <summary>The title of a run's saved chat: "⏰ " and the task.</summary>
        /// <param name="task">The task</param>
        public static string ChatTitle(ScheduledTask task) => "⏰ " + task.Description;

        private static readonly Regex SentenceEnd = new Regex(@"[.!?](?=\s|$)", RegexOptions.CultureInvariant);
        private static readonly Regex Markup = new Regex(@"[*_`#>]+", RegexOptions.CultureInvariant);
        private static readonly Regex Space = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private static string Plain(string answer) =>
            Space.Replace(Markup.Replace(answer ?? string.Empty, string.Empty), " ").Trim();

        private static bool StartsWithNothing(string text) => text.StartsWith(NothingToReport, StringComparison.OrdinalIgnoreCase);
    }

    public static partial class AgentTools
    {
        /// <summary>The most scheduled tasks at a time.</summary>
        public const int MaxScheduledTasks = 10;

        /// <summary>
        /// schedule_task, list_tasks and remove_task: tasks the agent runs on its own on a schedule (every day at a time, or
        /// every few hours) and tells the user about with a notification - always, or only when something holds. The app
        /// runs them (e.g. Emgu.LiteRT.LM.Maui's ScheduledTaskRunner) and arms the next run in <paramref name="changed"/>.
        /// </summary>
        /// <param name="store">Where the tasks are kept</param>
        /// <param name="changed">Called after a task is added (with it) or removed (with it, and the store no longer has it):
        /// the app schedules or cancels its run; or null</param>
        /// <param name="now">The current local time; DateTime.Now if null (for tests)</param>
        /// <returns>The tools</returns>
        public static IList<AIFunction> ScheduledTasks(IAgentTaskStore store, Func<ScheduledTask, Task> changed = null, Func<DateTime> now = null)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            Func<DateTime> clock = now ?? (() => DateTime.Now);
            return new List<AIFunction>
            {
                AIFunctionFactory.Create(
                    ([Description("What to do each time, in the user's words, e.g. \"Tell me the weather in Toronto\", \"Check whether example.com mentions a new release\"")] string task,
                        [Description("For a daily task, the time of day, e.g. \"7 am\", \"19:30\"; empty if every_hours is given")] string time = "",
                        [Description("For a task every few hours, the hours between runs (3 or more); 0 for a daily task")] int every_hours = 0,
                        [Description("Only tell the user when this holds, e.g. \"it will snow\", \"there is a new release\"; empty to tell them every time")] string notify_when = "",
                        CancellationToken cancellationToken = default) => ScheduleTaskAsync(store, changed, task, time, every_hours, notify_when, clock()),
                    "schedule_task",
                    "Schedules a task to run on its own every day at a time, or every few hours, even when the app is closed; the answer " +
                    "comes as a notification. Use it when the user asks for something regularly (\"every morning tell me...\", \"check every " +
                    "6 hours whether...\"), not for a one-time reminder."),
                AIFunctionFactory.Create(
                    (CancellationToken cancellationToken) => ListTasksAsync(store, clock()),
                    "list_tasks",
                    "Lists the user's scheduled tasks with when they run next and what happened last time."),
                AIFunctionFactory.Create(
                    ([Description("Words from the task to delete, e.g. \"weather\"")] string task,
                        CancellationToken cancellationToken) => RemoveTaskAsync(store, changed, task),
                    "remove_task",
                    "Deletes one of the user's scheduled tasks, when they ask to stop or remove it.")
            };
        }

        private const string TaskReplyInstruction = "\nTell the user in one short sentence.";

        private static async Task<string> ScheduleTaskAsync(IAgentTaskStore store, Func<ScheduledTask, Task> changed, string description,
            string time, int everyHours, string notifyWhen, DateTime now)
        {
            description = (description ?? string.Empty).Trim();
            if (description.Length == 0)
                return "Pass the task: what to do each time, in the user's words.";
            bool hasTime = !string.IsNullOrWhiteSpace(time);
            TaskSchedule schedule;
            if (everyHours > 0)
            {
                if (everyHours < TaskSchedule.MinHours)
                    return "A task can run at most every " + TaskSchedule.MinHours + " hours (to spare the battery). Ask the user whether every " +
                        TaskSchedule.MinHours + " hours will do.";
                if (everyHours > TaskSchedule.MaxHours)
                    return "Pass every_hours of at most " + TaskSchedule.MaxHours + " (a week), or a time of day for a daily task.";
                // The first run in the hours given, or at the time given if there is one.
                DateTime start = now.AddHours(everyHours);
                if (hasTime && GoalReminders.TryParseTime(time, out TimeSpan first))
                    start = now.Date + first > now ? now.Date + first : now.Date.AddDays(1) + first;
                schedule = TaskSchedule.Every(everyHours, new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0));
            }
            else if (hasTime)
            {
                if (!GoalReminders.TryParseTime(time, out TimeSpan at))
                    return "Couldn't read \"" + time + "\" as a time of day: pass e.g. \"7 am\" or \"19:30\".";
                schedule = TaskSchedule.Daily(at);
            }
            else
                return "Pass when to run it: a time of day (time) for a daily task, or every_hours. Ask the user if they didn't say.";
            IList<ScheduledTask> tasks = await store.GetTasksAsync().ConfigureAwait(false);
            if (tasks.Count >= MaxScheduledTasks)
                return "The user already has " + MaxScheduledTasks + " scheduled tasks, the most there can be: " +
                    string.Join("; ", tasks.Select(t => t.Description)) + ". Ask which one to remove first.";
            ScheduledTask task = new ScheduledTask(null, description, schedule, notifyWhen, now);
            task.Id = await store.AddTaskAsync(task).ConfigureAwait(false);
            if (changed != null)
            {
                try { await changed(task).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    await store.DeleteTaskAsync(task.Id).ConfigureAwait(false);
                    return "The task couldn't be scheduled: " + ex.Message + ". Tell the user.";
                }
            }
            DateTime next = schedule.Next(now);
            return "Scheduled \"" + description + "\" " + schedule.Describe() + (task.NotifyWhen != null ? ", telling the user only when " + task.NotifyWhen : "") +
                "; the first run: " + RunText(next, now) + ". It runs on this device even when the app is closed, and the answer comes as a notification." +
                TaskReplyInstruction;
        }

        private static async Task<string> ListTasksAsync(IAgentTaskStore store, DateTime now)
        {
            IList<ScheduledTask> tasks = await store.GetTasksAsync().ConfigureAwait(false);
            if (tasks.Count == 0)
                return "The user has no scheduled tasks.";
            return string.Join("\n", tasks.Select(t => "- " + DescribeTask(t, now)));
        }

        /// <summary>
        /// A task in a line: what, when, the next run and the last result, e.g. for a list or the settings.
        /// </summary>
        /// <param name="task">The task</param>
        /// <param name="now">The current local time</param>
        public static string DescribeTask(ScheduledTask task, DateTime now)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));
            string text = task.Description + " - " + task.Schedule.Describe() + (task.NotifyWhen != null ? ", only when " + task.NotifyWhen : "");
            text += task.Paused ? "; paused" : "; next: " + RunText(task.Schedule.Next(now), now);
            if (task.LastRun is DateTime last)
                text += "; last: " + RunText(last, now) + (string.IsNullOrEmpty(task.LastResult) ? "" : " - " + task.LastResult);
            return text;
        }

        private static async Task<string> RemoveTaskAsync(IAgentTaskStore store, Func<ScheduledTask, Task> changed, string text)
        {
            IList<ScheduledTask> tasks = await store.GetTasksAsync().ConfigureAwait(false);
            ScheduledTask task = FindTask(tasks, text);
            if (task == null)
                return tasks.Count == 0 ? "The user has no scheduled tasks."
                    : "No task matches \"" + text + "\". The user's tasks: " + string.Join("; ", tasks.Select(t => t.Description)) + ".";
            await store.DeleteTaskAsync(task.Id).ConfigureAwait(false);
            if (changed != null)
            {
                try { await changed(task).ConfigureAwait(false); }
                catch (Exception) { }
            }
            return "Removed the task \"" + task.Description + "\"." + TaskReplyInstruction;
        }

        // The task sharing the most content words with the text (alike by SameWord), or the only task when the text has none.
        private static ScheduledTask FindTask(IList<ScheduledTask> tasks, string text)
        {
            if (tasks.Count == 0)
                return null;
            HashSet<string> words = ContentWords(text);
            ScheduledTask best = null;
            int bestScore = 0;
            foreach (ScheduledTask task in tasks)
            {
                HashSet<string> taskWords = ContentWords(task.Description + " " + task.NotifyWhen);
                int score = words.Count(w => taskWords.Any(t => SameWord(w, t)));
                if (score > bestScore)
                {
                    best = task;
                    bestScore = score;
                }
            }
            return best ?? (tasks.Count == 1 && words.Count == 0 ? tasks[0] : null);
        }

        // "today at 7:00 AM", "tomorrow at 7:00 AM", "Friday at 7:00 AM", "yesterday at ...".
        private static string RunText(DateTime when, DateTime now)
        {
            int days = (when.Date - now.Date).Days;
            string day = days == 0 ? "today" : days == 1 ? "tomorrow" : days == -1 ? "yesterday"
                : Math.Abs(days) < 7 ? when.ToString("dddd", CultureInfo.InvariantCulture) : when.ToString("d MMMM", CultureInfo.InvariantCulture);
            return day + " at " + GoalReminders.FormatTime(when.TimeOfDay, CultureInfo.InvariantCulture);
        }
    }
}
