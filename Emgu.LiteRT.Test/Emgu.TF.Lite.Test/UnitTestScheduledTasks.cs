// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Skills;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using SQLite;

namespace Emgu.TF.Lite.Test;

// Scheduled tasks (TODO item 30, Emgu.LiteRT.LM.Agents' AgentTasks.cs): the schedules, the tools with an in-memory store,
// the run's instructions and notification text, the skill's routing, and the SQLite store. No model or native library.
[TestFixture]
public class UnitTestScheduledTasks
{
    // A Wednesday evening.
    private static readonly DateTime Now = new DateTime(2026, 10, 14, 19, 0, 0);

    [Test]
    public void DailyRunsAtItsClockTime()
    {
        TaskSchedule daily = TaskSchedule.Daily(new TimeSpan(7, 0, 0));
        Assert.That(daily.Next(Now), Is.EqualTo(new DateTime(2026, 10, 15, 7, 0, 0)));
        Assert.That(daily.Next(new DateTime(2026, 10, 15, 6, 59, 0)), Is.EqualTo(new DateTime(2026, 10, 15, 7, 0, 0)));
        // At its time exactly: the next day's.
        Assert.That(daily.Next(new DateTime(2026, 10, 15, 7, 0, 0)), Is.EqualTo(new DateTime(2026, 10, 16, 7, 0, 0)));
        // Across a daylight-saving change (1 November 2026 in North America) still at 7:00 by the clock.
        Assert.That(daily.Next(new DateTime(2026, 10, 31, 8, 0, 0)), Is.EqualTo(new DateTime(2026, 11, 1, 7, 0, 0)));
        Assert.That(daily.Describe(), Is.EqualTo("every day at 07:00"));
    }

    [Test]
    public void EveryFewHoursStepsFromItsStart()
    {
        DateTime start = new DateTime(2026, 10, 14, 9, 0, 0);
        TaskSchedule every = TaskSchedule.Every(6, start);
        Assert.That(every.Next(new DateTime(2026, 10, 14, 8, 0, 0)), Is.EqualTo(start));
        Assert.That(every.Next(start), Is.EqualTo(start.AddHours(6)));
        Assert.That(every.Next(Now), Is.EqualTo(new DateTime(2026, 10, 14, 21, 0, 0)));
        Assert.That(every.Next(new DateTime(2026, 10, 16, 3, 30, 0)), Is.EqualTo(new DateTime(2026, 10, 16, 9, 0, 0)));
        Assert.That(every.Describe(), Is.EqualTo("every 6 hours"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskSchedule.Every(2, start));
        Assert.Throws<ArgumentOutOfRangeException>(() => TaskSchedule.Every(TaskSchedule.MaxHours + 1, start));
    }

    [Test]
    public void SchedulesAreWrittenAndReadBack()
    {
        foreach (TaskSchedule schedule in new[] { TaskSchedule.Daily(new TimeSpan(19, 30, 0)), TaskSchedule.Every(8, new DateTime(2026, 10, 14, 9, 15, 0)) })
        {
            TaskSchedule read = TaskSchedule.Parse(schedule.Serialize());
            Assert.That((read.TimeOfDay, read.Hours, read.Start), Is.EqualTo((schedule.TimeOfDay, schedule.Hours, schedule.Start)), schedule.Serialize());
        }
        Assert.That(TaskSchedule.Daily(new TimeSpan(7, 5, 0)).Serialize(), Is.EqualTo("daily 07:05"));
        foreach (string bad in new[] { null, "", "weekly 07:00", "daily 7am", "every 1 2026-10-14T09:00:00", "every 6 tomorrow" })
            Assert.That(TaskSchedule.Parse(bad), Is.Null, bad ?? "null");
    }

    [Test]
    public void APausedTaskHasNoNextRun()
    {
        var task = new ScheduledTask("1", "Tell me the weather", TaskSchedule.Daily(new TimeSpan(7, 0, 0)), "", Now);
        Assert.That(task.NotifyWhen, Is.Null);
        Assert.That(task.NextRun(Now), Is.EqualTo(new DateTime(2026, 10, 15, 7, 0, 0)));
        task.Paused = true;
        Assert.That(task.NextRun(Now), Is.Null);
    }

    [Test]
    public async Task ToolsScheduleListAndRemove()
    {
        var store = new InMemoryAgentTaskStore();
        var changed = new List<string>();
        Dictionary<string, AIFunction> tools = AgentTools.ScheduledTasks(store, t => { changed.Add(t.Id); return Task.CompletedTask; }, () => Now)
            .ToDictionary(t => t.Name);
        Assert.That(tools.Keys, Is.EquivalentTo(new[] { "schedule_task", "list_tasks", "remove_task" }));

        async Task<string> Call(string name, params (string, object)[] arguments)
        {
            AIFunctionArguments args = new AIFunctionArguments();
            foreach (var (key, value) in arguments)
                args[key] = value;
            return (await tools[name].InvokeAsync(args))?.ToString();
        }

        Assert.That(await Call("list_tasks"), Is.EqualTo("The user has no scheduled tasks."));
        string result = await Call("schedule_task", ("task", "Tell me the weather in Toronto"), ("time", "7 am"));
        Assert.That(result, Does.Contain("Scheduled \"Tell me the weather in Toronto\" every day at 07:00; the first run: tomorrow at 07:00."));
        Assert.That(changed, Is.EqualTo(new[] { "1" }));
        result = await Call("schedule_task", ("task", "Check whether example.com mentions a new release"), ("every_hours", 6),
            ("notify_when", "there is a new release"));
        Assert.That(result, Does.Contain("every 6 hours, telling the user only when there is a new release; the first run: tomorrow at 01:00"));
        ScheduledTask release = (await store.GetTasksAsync()).Last();
        Assert.That(release.Schedule.Start, Is.EqualTo(Now.AddHours(6)));
        Assert.That(release.NotifyWhen, Is.EqualTo("there is a new release"));

        string list = await Call("list_tasks");
        Assert.That(list, Does.Contain("- Tell me the weather in Toronto - every day at 07:00; next: tomorrow at 07:00"));
        Assert.That(list, Does.Contain("only when there is a new release"));

        Assert.That(await Call("remove_task", ("task", "the Toronto weather")), Does.Contain("Removed the task \"Tell me the weather in Toronto\""));
        Assert.That(changed, Is.EqualTo(new[] { "1", "2", "1" }));
        Assert.That((await store.GetTasksAsync()).Select(t => t.Id), Is.EqualTo(new[] { "2" }));
        Assert.That(await Call("remove_task", ("task", "piano")), Does.StartWith("No task matches \"piano\""));
    }

    [Test]
    public async Task ToolsRefuseWhatTheyCantSchedule()
    {
        var store = new InMemoryAgentTaskStore();
        Dictionary<string, AIFunction> tools = AgentTools.ScheduledTasks(store, null, () => Now).ToDictionary(t => t.Name);

        async Task<string> Schedule(params (string, object)[] arguments)
        {
            AIFunctionArguments args = new AIFunctionArguments();
            foreach (var (key, value) in arguments)
                args[key] = value;
            return (await tools["schedule_task"].InvokeAsync(args))?.ToString();
        }

        Assert.That(await Schedule(("task", "Check the news"), ("every_hours", 1)), Does.Contain("at most every 3 hours"));
        Assert.That(await Schedule(("task", "Check the news"), ("every_hours", 500)), Does.Contain("at most 168"));
        Assert.That(await Schedule(("task", "Check the news"), ("time", "whenever")), Does.Contain("Couldn't read \"whenever\" as a time of day"));
        Assert.That(await Schedule(("task", "Check the news")), Does.Contain("Pass when to run it"));
        Assert.That(await Schedule(("task", " "), ("time", "7 am")), Does.Contain("Pass the task"));
        Assert.That(await store.GetTasksAsync(), Is.Empty);

        for (int i = 0; i < AgentTools.MaxScheduledTasks; i++)
            Assert.That(await Schedule(("task", "Task " + i), ("time", "8 am")), Does.StartWith("Scheduled"));
        Assert.That(await Schedule(("task", "One more"), ("time", "8 am")), Does.Contain("already has 10 scheduled tasks"));

        // A schedule that can't be armed isn't kept.
        var failing = new InMemoryAgentTaskStore();
        AIFunction schedule = AgentTools.ScheduledTasks(failing, t => throw new InvalidOperationException("no alarms"), () => Now).First(t => t.Name == "schedule_task");
        string result = (await schedule.InvokeAsync(new AIFunctionArguments { ["task"] = "Weather", ["time"] = "7 am" }))?.ToString();
        Assert.That(result, Does.Contain("couldn't be scheduled: no alarms"));
        Assert.That(await failing.GetTasksAsync(), Is.Empty);
    }

    [Test]
    public void TheRunSaysWhetherToNotify()
    {
        var always = new ScheduledTask("1", "Tell me the weather in Toronto", TaskSchedule.Daily(new TimeSpan(7, 0, 0)), null, Now);
        var onlySnow = new ScheduledTask("2", "Check the forecast for Toronto", TaskSchedule.Daily(new TimeSpan(7, 0, 0)), "it will snow", Now);

        string instructions = ScheduledTaskRun.Instructions(onlySnow, new DateTime(2026, 10, 15, 7, 0, 0));
        Assert.That(instructions, Does.Contain("every day at 07:00"));
        Assert.That(instructions, Does.Contain("Thursday 15 October 2026 at 7:00 AM"));
        Assert.That(instructions, Does.Contain("start your answer with \"NOTHING TO REPORT\""));
        Assert.That(ScheduledTaskRun.Instructions(always, Now), Does.Not.Contain("NOTHING TO REPORT"));

        Assert.That(ScheduledTaskRun.ShouldNotify(always, "Sunny, 12 °C."), Is.True);
        Assert.That(ScheduledTaskRun.ShouldNotify(always, "  "), Is.False);
        // Without a condition the marker doesn't stop it: the user wants every run.
        Assert.That(ScheduledTaskRun.ShouldNotify(always, "NOTHING TO REPORT"), Is.True);
        Assert.That(ScheduledTaskRun.ShouldNotify(onlySnow, "NOTHING TO REPORT - rain and 8 °C, no snow."), Is.False);
        Assert.That(ScheduledTaskRun.ShouldNotify(onlySnow, "**Nothing to report**: no snow today."), Is.False);
        Assert.That(ScheduledTaskRun.ShouldNotify(onlySnow, "Snow is expected this afternoon, 5 cm."), Is.True);

        Assert.That(ScheduledTaskRun.NotificationText("NOTHING TO REPORT - rain and 8 °C, no snow."), Is.EqualTo("rain and 8 °C, no snow."));
        Assert.That(ScheduledTaskRun.NotificationText("**Toronto:** sunny,\n\n12 °C."), Is.EqualTo("Toronto: sunny, 12 °C."));
        string longAnswer = string.Join(" ", Enumerable.Repeat("The forecast is clear with a light wind.", 10));
        string text = ScheduledTaskRun.NotificationText(longAnswer);
        Assert.That(text.Length, Is.LessThanOrEqualTo(ScheduledTaskRun.MaxNotificationLength));
        Assert.That(text, Does.EndWith("wind."));
        string words = ScheduledTaskRun.NotificationText(new string('a', 10) + " " + string.Join(" ", Enumerable.Repeat("word", 80)));
        Assert.That(words.Length, Is.LessThanOrEqualTo(ScheduledTaskRun.MaxNotificationLength));
        Assert.That(words, Does.EndWith("…"));

        Assert.That(ScheduledTaskRun.ChatTitle(always), Is.EqualTo("⏰ Tell me the weather in Toronto"));
    }

    [Test]
    public void StepLinesSayWhatTheToolsDo()
    {
        var call = new FunctionCallContent("1", "schedule_task", new Dictionary<string, object> { ["task"] = "Tell me the weather in Toronto" });
        Assert.That(AgentTools.Describe(call), Is.EqualTo("Scheduling: Tell me the weather in Toronto"));
        Assert.That(Emgu.LiteRT.LM.Chat.ChatToolStep.Done(AgentTools.Describe(call)), Is.EqualTo("Scheduled: Tell me the weather in Toronto"));
        Assert.That(Emgu.LiteRT.LM.Chat.ChatToolStep.Done(AgentTools.Describe(new FunctionCallContent("2", "list_tasks"))), Is.EqualTo("Checked your scheduled tasks"));
        Assert.That(Emgu.LiteRT.LM.Chat.ChatToolStep.Done(AgentTools.Describe(new FunctionCallContent("3", "remove_task", new Dictionary<string, object> { ["task"] = "weather" }))),
            Is.EqualTo("Removed the scheduled task: weather"));
    }

    [Test]
    public async Task TheSkillComesForRegularRequestsOnly()
    {
        var store = new InMemoryAgentTaskStore();
        var skills = new ChatSkillSet();
        skills.Add(new ScheduledTasksSkill(store, () => Now));
        string[] regular =
        {
            "Every morning at 7, tell me the weather in Toronto", "Check every 6 hours whether example.com has a new release",
            "Give me a daily summary of the news", "Let me know when the price of AMD drops below 100", "What are my scheduled tasks?",
            "Every day at 8 pm remind me what's on tomorrow", "keep an eye on the Toronto forecast"
        };
        string[] other = { "What is 12 times 7?", "I want to read every day", "Remind me to call mum at 5", "What's the weather in Paris?" };
        foreach (string message in regular)
            Assert.That(skills.Router.Match(message), Does.Contain(ScheduledTasksSkill.GroupName), message);
        foreach (string message in other)
            Assert.That(skills.Router.Match(message), Does.Not.Contain(ScheduledTasksSkill.GroupName), message);

        // No tools in a private chat.
        ChatSkillComposition composition = await skills.ComposeAsync(new ChatMessageContext { Prompt = regular[0], IsPrivate = true }, null);
        Assert.That(composition.Tools.Select(t => t.Name), Does.Not.Contain("schedule_task"));
        skills.Reset();
        composition = await skills.ComposeAsync(new ChatMessageContext { Prompt = regular[0] }, null);
        Assert.That(composition.Tools.Select(t => t.Name), Is.SupersetOf(new[] { "schedule_task", "list_tasks", "remove_task" }));
    }

    [Test]
    public async Task TheSqliteStoreKeepsTasksAndTheirRuns()
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "scheduled-tasks-test-" + Guid.NewGuid().ToString("N"));
        var chats = new ChatHistoryStore(folder);
        try
        {
            var tasks = new SqliteAgentTaskStore(chats);
            Assert.That(await tasks.GetTasksAsync(), Is.Empty);
            string daily = await tasks.AddTaskAsync(new ScheduledTask(null, "Tell me the weather", TaskSchedule.Daily(new TimeSpan(7, 0, 0)), null, Now));
            string every = await tasks.AddTaskAsync(new ScheduledTask(null, "Check for a release", TaskSchedule.Every(6, Now.AddHours(6)), "there is one", Now));
            ScheduledTask read = await tasks.GetTaskAsync(every);
            Assert.That((read.Description, read.Schedule.Hours, read.Schedule.Start, read.NotifyWhen, read.Created),
                Is.EqualTo(("Check for a release", 6, Now.AddHours(6), "there is one", Now)));

            read.LastRun = Now.AddHours(6);
            read.LastResult = "Nothing to report";
            read.LastChatId = 42;
            read.Paused = true;
            await tasks.UpdateTaskAsync(read);
            read = await tasks.GetTaskAsync(every);
            Assert.That((read.LastRun, read.LastResult, read.LastChatId, read.Paused), Is.EqualTo(((DateTime?)Now.AddHours(6), "Nothing to report", (int?)42, true)));

            // While a chat is private: nothing listed or added, but a due run is read and saved.
            chats.IsPrivate = true;
            Assert.That(await tasks.GetTasksAsync(), Is.Empty);
            Assert.ThrowsAsync<InvalidOperationException>(() => tasks.AddTaskAsync(new ScheduledTask(null, "Secret", TaskSchedule.Daily(TimeSpan.Zero), null, Now)));
            ScheduledTask due = await tasks.GetTaskAsync(daily);
            Assert.That(due.Description, Is.EqualTo("Tell me the weather"));
            due.LastResult = "Sunny";
            await tasks.UpdateTaskAsync(due);
            await tasks.DeleteTaskAsync(daily);
            chats.IsPrivate = false;
            Assert.That((await tasks.GetTasksAsync()).Select(t => t.Description), Is.EqualTo(new[] { "Tell me the weather", "Check for a release" }));
            Assert.That((await tasks.GetTaskAsync(daily)).LastResult, Is.EqualTo("Sunny"));

            await tasks.DeleteTaskAsync(daily);
            Assert.That((await tasks.GetTasksAsync()).Select(t => t.Id), Is.EqualTo(new[] { every }));
            Assert.That(await tasks.GetTaskAsync("not an id"), Is.Null);
            Assert.That(await (await chats.GetConnectionAsync()).ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ScheduledTask"), Is.EqualTo(1));
        }
        finally
        {
            await chats.CloseAsync();
            SQLiteAsyncConnection.ResetPool();
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }

    // The tasks in a list, as a database would keep them.
    private sealed class InMemoryAgentTaskStore : IAgentTaskStore
    {
        private readonly List<ScheduledTask> _tasks = new List<ScheduledTask>();
        private int _next = 1;

        public Task<IList<ScheduledTask>> GetTasksAsync() => Task.FromResult<IList<ScheduledTask>>(_tasks.ToList());

        public Task<ScheduledTask> GetTaskAsync(string id) => Task.FromResult(_tasks.FirstOrDefault(t => t.Id == id));

        public Task<string> AddTaskAsync(ScheduledTask task)
        {
            task.Id = (_next++).ToString();
            _tasks.Add(task);
            return Task.FromResult(task.Id);
        }

        public Task UpdateTaskAsync(ScheduledTask task) => Task.CompletedTask;

        public Task DeleteTaskAsync(string id)
        {
            _tasks.RemoveAll(t => t.Id == id);
            return Task.CompletedTask;
        }
    }
}
