//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Skills;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// Scheduled tasks (<see cref="ScheduledTasksSkill"/>): "every morning at 7, tell me the weather in Toronto" - the agent
    /// schedules it (schedule_task), and the task runs on its own at its time, the app closed, its answer a notification
    /// that opens the run's saved chat. Settings list the tasks with their next and last runs, to run now, pause or delete.
    /// The runs are <see cref="ScheduledTaskRunner"/>'s, which the app configures when it starts; <see cref="ScheduledTaskAlarms"/>
    /// starts them on time - on Android only for now (<see cref="ScheduledTaskAlarms.IsSupported"/>; elsewhere the feature
    /// offers nothing). Not in a private chat.
    /// </summary>
    public sealed class ScheduledTasksFeature : SkillFeature<ScheduledTasksSkill>
    {
        private VerticalStackLayout _list;
        private Label _exactNote;
        private bool _askedExact;

        /// <summary>Create the feature.</summary>
        /// <param name="store">Where the tasks are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentTaskStore), the store the
        /// app gives <see cref="ScheduledTaskRunner"/></param>
        public ScheduledTasksFeature(IAgentTaskStore store) : base(new ScheduledTasksSkill(store))
        {
            Skill.Enabled = ScheduledTaskAlarms.IsSupported;
            Skill.Changed = OnTaskChangedAsync;
        }

        /// <summary>Scheduled tasks come wherever they can run, not by a switch of their own.</summary>
        public override bool PersistEnabled => false;

        private IAgentTaskStore Store => Skill.Store;

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            if (!ScheduledTaskAlarms.IsSupported)
                return;
            ScheduledTaskRunner.Completed -= OnRunCompleted;
            ScheduledTaskRunner.Completed += OnRunCompleted;
            // The alarms again (an app updated by hand, "Alarms & reminders" allowed meanwhile).
            _ = RearmAsync();
        }

        /// <inheritdoc/>
        public override void OnAppResumed()
        {
            if (!ScheduledTaskAlarms.IsSupported)
                return;
            UpdateExactNote();
            _ = RefreshListAsync();
        }

        private static async Task RearmAsync()
        {
            try { await ScheduledTaskAlarms.RearmAllAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Scheduled tasks not armed: " + ex.Message); }
        }

        private void OnRunCompleted(object sender, ScheduledTaskOutcome outcome) =>
            MainThread.BeginInvokeOnMainThread(() => _ = RefreshListAsync());

        // A task added (arm its run, asking for notifications and, once, exact alarms) or removed (cancel it).
        private async Task OnTaskChangedAsync(ScheduledTask task)
        {
            ScheduledTask saved = await Store.GetTaskAsync(task.Id);
            if (saved == null)
                ScheduledTaskAlarms.Cancel(task.Id);
            else
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await ReminderNotifications.RequestPermissionAsync();
                    await AskForExactAlarmsAsync();
                });
                ScheduledTaskAlarms.Arm(saved);
            }
            MainThread.BeginInvokeOnMainThread(() => _ = RefreshListAsync());
        }

        // Android 14+ doesn't allow exact alarms by default: without them a task waits for a tap on "Task due".
        private async Task AskForExactAlarmsAsync()
        {
            if (_askedExact || Host == null || ScheduledTaskAlarms.CanRunOnTime)
                return;
            _askedExact = true;
            if (await Host.AskAsync("Run tasks on time",
                "To run your scheduled tasks at their time while the app is closed, allow \"Alarms & reminders\" for this app. " +
                "Otherwise a notification asks you to run each one when it is due.", "Open settings", "Not now"))
                ScheduledTaskAlarms.OpenExactAlarmSettings();
        }

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            if (!ScheduledTaskAlarms.IsSupported)
                return null;
            _list = new VerticalStackLayout { Spacing = 4 };
            _exactNote = ChatPage.Note("\"Alarms & reminders\" is off for this app: a task waits for you to tap its notification. Tap to allow it.");
            var exactTap = new TapGestureRecognizer();
            exactTap.Tapped += (s, e) => ScheduledTaskAlarms.OpenExactAlarmSettings();
            _exactNote.GestureRecognizers.Add(exactTap);
            UpdateExactNote();
            _ = RefreshListAsync();
            return new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    ChatPage.SectionTitle("Scheduled tasks"),
                    ChatPage.Note("Ask the assistant to do something regularly - \"every morning at 7, tell me the weather\", \"every 6 hours, check whether ... has a new release\" - and it runs on this device at that time, even with the app closed, and tells you in a notification. Each run is saved as a chat."),
                    _exactNote,
                    _list
                }
            };
        }

        private void UpdateExactNote()
        {
            if (_exactNote != null)
                _exactNote.IsVisible = !ScheduledTaskAlarms.CanRunOnTime;
        }

        /// <inheritdoc/>
        public override void OnPrivateModeChanged(bool isPrivate)
        {
            base.OnPrivateModeChanged(isPrivate);
            if (_list != null)
                _list.IsVisible = !isPrivate;
            if (!isPrivate)
                _ = RefreshListAsync();
        }

        private async Task RefreshListAsync()
        {
            if (_list == null || Host == null || Host.IsPrivate)
                return;
            IList<ScheduledTask> tasks;
            try { tasks = await Store.GetTasksAsync(); }
            catch (Exception) { return; }
            _list.Children.Clear();
            if (tasks.Count == 0)
            {
                _list.Children.Add(ChatPage.Note("No scheduled tasks yet."));
                return;
            }
            DateTime now = DateTime.Now;
            foreach (ScheduledTask task in tasks)
            {
                ScheduledTask t = task;
                var actions = new List<(string, Action)>
                {
                    ("Run now", () => RunNow(t)),
                    (t.Paused ? "Resume" : "Pause", () => _ = SetPausedAsync(t, !t.Paused))
                };
                if (t.LastChatId is int chatId)
                    actions.Add(("Open", () => _ = Host?.OpenChatAsync(chatId)));
                _list.Children.Add(ChatPage.ListRow(t.Description, Detail(t, now), false, null, () => _ = DeleteAsync(t), "Delete this task", actions, actionsBelow: true));
            }
        }

        // When it runs, the next run and the last result.
        private static string Detail(ScheduledTask task, DateTime now)
        {
            // In the device's clock style ("8:56 p.m."), as the runs below; Describe's is the model's (24-hour).
            string schedule = task.Schedule.TimeOfDay is TimeSpan time ? "every day at " + GoalReminders.FormatTime(time) : task.Schedule.Describe();
            string detail = schedule + (task.NotifyWhen != null ? ", only when " + task.NotifyWhen : "");
            detail += task.Paused ? " · paused" : " · next " + Short(task.Schedule.Next(now), now);
            if (task.LastRun is DateTime last)
                detail += "\nLast " + Short(last, now) + (string.IsNullOrEmpty(task.LastResult) ? "" : ": " + task.LastResult);
            return detail;
        }

        private static string Short(DateTime when, DateTime now)
        {
            int days = (when.Date - now.Date).Days;
            string day = days == 0 ? "today" : days == 1 ? "tomorrow" : days == -1 ? "yesterday" : when.ToString("ddd d MMM");
            return day + " " + GoalReminders.FormatTime(when.TimeOfDay);
        }

        private void RunNow(ScheduledTask task)
        {
            ScheduledTaskAlarms.RunNow(task.Id);
            Host?.SetStatus("Running \"" + task.Description + "\"... its answer comes as a notification.");
        }

        private async Task SetPausedAsync(ScheduledTask task, bool paused)
        {
            ScheduledTask saved = await Store.GetTaskAsync(task.Id);
            if (saved == null)
                return;
            saved.Paused = paused;
            await Store.UpdateTaskAsync(saved);
            ScheduledTaskAlarms.Arm(saved);
            await RefreshListAsync();
        }

        private async Task DeleteAsync(ScheduledTask task)
        {
            if (Host == null || !await Host.AskAsync("Delete this task?", "\"" + task.Description + "\" won't run again. Its saved runs stay in your chats.", "Delete", "Cancel"))
                return;
            await Store.DeleteTaskAsync(task.Id);
            ScheduledTaskAlarms.Cancel(task.Id);
            await RefreshListAsync();
        }
    }
}
