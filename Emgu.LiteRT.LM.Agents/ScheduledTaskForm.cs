// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>
/// Adds a scheduled task from what a settings form gives (the task in words, a time of day or the hours between runs, and
/// when to tell the user), with the same limits as the schedule_task tool (<see cref="AgentTools.ScheduledTasks"/>) and
/// messages written for the user instead of the model.
/// </summary>
public static class ScheduledTaskForm
{
    /// <summary>
    /// Check the form and add the task.
    /// </summary>
    /// <param name="store">Where the tasks are kept</param>
    /// <param name="changed">Called after the task is added, to arm its run (the app's <see cref="Skills.ScheduledTasksSkill.Changed"/>); or null.
    /// A task it throws for is taken back.</param>
    /// <param name="description">What to do each time, in the user's words</param>
    /// <param name="dailyAt">The time of day of a daily task; null for a task every few hours</param>
    /// <param name="everyHours">The hours between runs of a task every few hours (text, as typed); ignored for a daily task</param>
    /// <param name="notifyWhen">When to tell the user, or empty to tell them every time</param>
    /// <param name="now">The current local time</param>
    /// <returns>Null when the task was added, else what is wrong, for the user</returns>
    public static async Task<string> AddAsync(IAgentTaskStore store, Func<ScheduledTask, Task> changed, string description,
        TimeSpan? dailyAt, string everyHours, string notifyWhen, DateTime now)
    {
        if (store == null)
            throw new ArgumentNullException(nameof(store));
        description = (description ?? string.Empty).Trim();
        if (description.Length == 0)
            return "Write what the task should do each time.";
        TaskSchedule schedule;
        if (dailyAt is TimeSpan at)
            schedule = TaskSchedule.Daily(at);
        else
        {
            if (!int.TryParse((everyHours ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int hours))
                return "Enter the hours between runs as a whole number, from " + TaskSchedule.MinHours + " to " + TaskSchedule.MaxHours + ".";
            if (hours < TaskSchedule.MinHours)
                return "A task can run at most every " + TaskSchedule.MinHours + " hours, to spare the battery.";
            if (hours > TaskSchedule.MaxHours)
                return "A task can run at most every " + TaskSchedule.MaxHours + " hours (a week).";
            // The first run in that many hours, on the minute, as the schedule_task tool does.
            DateTime first = now.AddHours(hours);
            schedule = TaskSchedule.Every(hours, new DateTime(first.Year, first.Month, first.Day, first.Hour, first.Minute, 0));
        }
        IList<ScheduledTask> tasks = await store.GetTasksAsync().ConfigureAwait(false);
        if (tasks.Count >= AgentTools.MaxScheduledTasks)
            return "You already have " + AgentTools.MaxScheduledTasks + " scheduled tasks, the most there can be. Delete one first.";
        var task = new ScheduledTask(null, description, schedule, notifyWhen, now);
        task.Id = await store.AddTaskAsync(task).ConfigureAwait(false);
        if (changed != null)
        {
            try { await changed(task).ConfigureAwait(false); }
            catch (Exception ex)
            {
                await store.DeleteTaskAsync(task.Id).ConfigureAwait(false);
                return "The task couldn't be scheduled: " + ex.Message;
            }
        }
        return null;
    }
}
