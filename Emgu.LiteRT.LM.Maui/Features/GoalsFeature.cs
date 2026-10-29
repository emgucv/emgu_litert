//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Skills;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// The user's goals (<see cref="GoalsSkill"/>): "I want to run three times a week", "I ran today" - the agent keeps
    /// count. The settings list them with their progress, streak and history by period (<see cref="GoalHistory"/>), to
    /// edit, undo the last entry of or delete. With "Remember things about me" on, never in a private chat. Another
    /// feature adds tools to the goals' group through the skill (<see cref="GoalsSkill.ExtraTools"/>; e.g. counting
    /// exercises, which logs toward the goals), found through <see cref="IChatPageHost.Services"/>. A goal can have a reminder
    /// at a time of day (Settings > Your goals, or "remind me at 7 pm" through set_goal_reminder): a notification
    /// (<see cref="ReminderNotifications"/>) only while the goal is still open (<see cref="GoalReminders"/>), scheduled again
    /// when a reply is done, a goal is changed and the app comes to the front, so one already done doesn't come.
    /// </summary>
    public sealed class GoalsFeature : SkillFeature<GoalsSkill>
    {
        private Label _countLabel;

        /// <summary>Create the feature.</summary>
        /// <param name="store">Where the goals are kept (e.g. Emgu.LiteRT.LM.Chat.Sqlite's SqliteAgentGoalStore)</param>
        public GoalsFeature(IAgentGoalStore store) : base(new GoalsSkill(store))
        {
            if (ReminderNotifications.IsSupported)
                Skill.SetReminder = SetReminderAsync;
        }

        // A goal's reminder time in the page's settings ("18:00"; empty: none), and the notification's key.
        private static string ReminderSetting(AgentGoal goal) => "goal_reminder_" + goal.Id;

        private static string ReminderKey(AgentGoal goal) => "goal_" + goal.Id;

        private TimeSpan? ReminderTime(AgentGoal goal) =>
            TimeSpan.TryParseExact(Host?.Settings.Get(ReminderSetting(goal), string.Empty) ?? string.Empty, @"hh\:mm",
                System.Globalization.CultureInfo.InvariantCulture, out TimeSpan time) ? time : null;

        /// <summary>
        /// Keep a goal's reminder time (null: none) and schedule its next reminder, asking for the permission to notify
        /// first; returns when it is next due, or null.
        /// </summary>
        /// <param name="goal">The goal</param>
        /// <param name="time">The time of day, or null to turn it off</param>
        /// <exception cref="InvalidOperationException">Notifications aren't allowed</exception>
        public async Task<DateTime?> SetReminderAsync(AgentGoal goal, TimeSpan? time)
        {
            if (Host == null || goal == null)
                return null;
            if (time != null && !await MainThread.InvokeOnMainThreadAsync(ReminderNotifications.RequestPermissionAsync))
                throw new InvalidOperationException("notifications aren't allowed for this app: they can be turned on in the system's settings");
            Host.Settings.Set(ReminderSetting(goal), time?.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            return await RescheduleAsync(goal);
        }

        // The goal's next reminder scheduled (or cancelled: none set, or nothing left to remind of); when it is due.
        private async Task<DateTime?> RescheduleAsync(AgentGoal goal)
        {
            TimeSpan? time = ReminderTime(goal);
            if (time == null)
            {
                ReminderNotifications.Cancel(ReminderKey(goal));
                return null;
            }
            IList<GoalProgress> progress = await Store.GetProgressAsync(goal.Id, DateTime.MinValue);
            DateTime now = DateTime.Now;
            DateTime? due = GoalReminders.NextDue(goal, time.Value, progress, now);
            if (due == null)
            {
                ReminderNotifications.Cancel(ReminderKey(goal));
                return null;
            }
            (string title, string text) = GoalReminders.Text(goal, progress, due.Value);
            ReminderRepeat repeat = goal.Period == GoalPeriod.Week ? ReminderRepeat.Week : goal.Period == GoalPeriod.Month ? ReminderRepeat.Month : ReminderRepeat.Day;
            // Later ones (while the app isn't opened): nothing logged since, so the whole target is left.
            string repeatText = GoalReminders.Text(goal, null, ReminderNotifications.Next(due.Value, repeat)).Text;
            ReminderNotifications.Schedule(ReminderKey(goal), due.Value, title, text, repeat, repeatText);
            return due;
        }

        // Every goal's reminder scheduled again (after a reply - "I did 30 jumping jacks" - or with the app back in front).
        private async Task RescheduleAllAsync()
        {
            if (!ReminderNotifications.IsSupported || Host == null || Host.IsPrivate)
                return;
            try
            {
                foreach (AgentGoal goal in await Store.GetGoalsAsync())
                    await RescheduleAsync(goal);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Goal reminders not scheduled: " + ex.Message); }
        }

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            _ = RescheduleAllAsync();
        }

        /// <inheritdoc/>
        public override void OnAppResumed() => _ = RescheduleAllAsync();

        /// <summary>Goals follow the memory switch, not a switch of their own.</summary>
        public override bool PersistEnabled => false;

        private IAgentGoalStore Store => Skill.Store;

        /// <summary>Whether goals are kept right now: with "Remember things about me" on, not in a private chat.</summary>
        public bool On => Host != null && Host.MemoryOn && !Host.IsPrivate;

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            _countLabel = ChatPage.Link("", () => _ = ShowGoalsAsync());
            _ = RefreshCountAsync();
            return new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    ChatPage.SectionTitle("Your goals"),
                    ChatPage.Note("Tell the assistant a goal - \"I want to run three times a week\", \"read 20 pages a day\" - and what you did toward it (\"I ran today\"); it keeps count and can tell you how you're doing. Kept on this device, with \"Remember things about me\" on."),
                    _countLabel
                }
            };
        }

        /// <inheritdoc/>
        public override void OnReplyCompleted(ChatExchangeEventArgs e)
        {
            _ = RefreshCountAsync();
            _ = RescheduleAllAsync();
        }

        /// <inheritdoc/>
        public override void OnPrivateModeChanged(bool isPrivate)
        {
            base.OnPrivateModeChanged(isPrivate);
            if (_countLabel != null)
                _countLabel.IsVisible = !isPrivate;
            if (!isPrivate)
                _ = RefreshCountAsync();
        }

        private async Task RefreshCountAsync()
        {
            if (_countLabel == null || Host == null || Host.IsPrivate)
                return;
            int count = 0;
            try { count = (await Store.GetGoalsAsync()).Count; }
            catch (Exception) { }
            _countLabel.Text = count == 0 ? "No goals yet" : count == 1 ? "1 goal - show" : count + " goals - show";
        }

        // The goals in a sheet, each with its progress and Edit / Delete.
        private async Task ShowGoalsAsync()
        {
            if (Host == null || Host.IsPrivate)
                return;
            IList<AgentGoal> goals = await Store.GetGoalsAsync();
            if (goals.Count == 0)
                return;
            var list = new VerticalStackLayout { Spacing = 2 };
            foreach (AgentGoal goal in goals)
                list.Children.Add(await BuildGoalRowAsync(goal, list));
            await Host.ShowSheetAsync("Your goals", list, "Close");
            await RefreshCountAsync();
        }

        private async Task<View> BuildGoalRowAsync(AgentGoal goal, VerticalStackLayout list)
        {
            var status = new Label();
            ChatResources.ApplyStyle(status, "ChatSectionTextStyle");
            // The streak, a strip with a block for each of the latest periods (filled: reached, empty: missed, outlined: the one
            // going on) and, tapping the strip, a line for each.
            var streak = new Label { IsVisible = false };
            ChatResources.ApplyStyle(streak, "ChatLinkStyle");
            var strip = new HorizontalStackLayout { Spacing = 3, IsVisible = false, Padding = new Thickness(0, 2) };
            var historyLines = new VerticalStackLayout { Spacing = 1, IsVisible = false };
            TapGestureRecognizer stripTap = new TapGestureRecognizer();
            stripTap.Tapped += (s, e) => historyLines.IsVisible = !historyLines.IsVisible;
            strip.GestureRecognizers.Add(stripTap);
            Color accent = ChatResources.FindColor("ChatAccent");
            async Task Refresh()
            {
                try
                {
                    status.Text = await AgentTools.GoalStatusAsync(Store, goal, DateTime.Now);
                    GoalHistory history = GoalHistory.Compute(goal, await Store.GetProgressAsync(goal.Id, DateTime.MinValue), DateTime.Now, 12);
                    streak.Text = history.StreakText == null ? string.Empty : "\U0001F525 " + history.StreakText;
                    streak.IsVisible = history.StreakText != null;
                    strip.Children.Clear();
                    historyLines.Children.Clear();
                    strip.IsVisible = history.Periods.Count > 1;
                    foreach (GoalPeriodResult period in history.Periods)
                    {
                        var block = new Border { WidthRequest = 14, HeightRequest = 14, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) } };
                        if (period.IsCurrent && !period.Met)
                        {
                            block.Stroke = accent;
                            block.BackgroundColor = Colors.Transparent;
                        }
                        else
                        {
                            block.Stroke = Colors.Transparent;
                            if (period.Met)
                                block.BackgroundColor = accent;
                            else
                                ChatResources.SetThemeColor(block, VisualElement.BackgroundColorProperty, "ChatRowBorder");
                        }
                        strip.Children.Add(block);
                        var line = new Label { Text = history.Describe(period, goal) };
                        ChatResources.ApplyStyle(line, period.Met ? "ChatListTitleStyle" : "ChatListDetailStyle");
                        historyLines.Children.Insert(0, line);
                    }
                    SemanticProperties.SetDescription(strip, string.Format("The last {0} {1}s: {2} reached the target. Tap to show them.",
                        history.Periods.Count, history.PeriodName, history.Periods.Count(p => p.Met)));
                }
                catch (Exception ex) { status.Text = goal.Text + " (" + ex.Message + ")"; }
            }
            await Refresh();
            VerticalStackLayout row = null;
            Label edit = ChatPage.Link("Edit", async () =>
            {
                string text = await Host.PromptAsync("Edit goal", "The goal, with how much and how often, e.g. \"Read 20 pages a day\".",
                    "Save", "Cancel", goal.Text, 200);
                if (string.IsNullOrWhiteSpace(text) || text.Trim() == goal.Text)
                    return;
                AgentGoal parsed = AgentGoal.FromText(text, goal.Created);
                goal = new AgentGoal(goal.Id, parsed.Text, parsed.Target, parsed.Unit, parsed.Period, goal.Created);
                await Store.UpdateGoalAsync(goal);
                await Refresh();
                await RescheduleAsync(goal);
            });
            Label undo = ChatPage.Link("Undo last", async () =>
            {
                if (await Store.DeleteLastProgressAsync(goal.Id))
                {
                    await Refresh();
                    await RescheduleAsync(goal);
                }
            });
            Label delete = ChatPage.Link("Delete", async () =>
            {
                if (!await Host.AskAsync("Delete goal", "Delete \"" + goal.Text + "\" and its progress?", "Delete", "Cancel"))
                    return;
                await Store.DeleteGoalAsync(goal.Id);
                ReminderNotifications.Cancel(ReminderKey(goal));
                Host.Settings.Set(ReminderSetting(goal), string.Empty);
                list.Children.Remove(row);
                await RefreshCountAsync();
            });
            // "Remind me at 6:00 PM": a time in the user's words, or off.
            Label remind = null;
            string RemindText() => ReminderTime(goal) is TimeSpan at ? "Remind me at " + GoalReminders.FormatTime(at) : "Remind me…";
            remind = ChatPage.Link(RemindText(), async () =>
            {
                string text = await Host.PromptAsync("Reminder", "A time of day for a reminder while \"" + goal.Text + "\" isn't done yet, e.g. \"7 pm\"; empty for none.",
                    "Save", "Cancel", ReminderTime(goal) is TimeSpan at ? GoalReminders.FormatTime(at) : string.Empty, 20);
                if (text == null)
                    return;
                TimeSpan? time = null;
                if (text.Trim().Length > 0)
                {
                    if (!GoalReminders.TryParseTime(text, out TimeSpan parsed))
                    {
                        await Host.AskAsync("Reminder", "\"" + text + "\" isn't a time of day: try e.g. \"7 pm\" or \"19:30\".", "OK", null);
                        return;
                    }
                    time = parsed;
                }
                try { await SetReminderAsync(goal, time); }
                catch (Exception ex) { await Host.AskAsync("Reminder", "The reminder couldn't be set: " + ex.Message + ".", "OK", null); }
                remind.Text = RemindText();
            });
            remind.IsVisible = ReminderNotifications.IsSupported;
            SemanticProperties.SetDescription(remind, "Set a daily reminder for this goal");
            edit.Padding = undo.Padding = delete.Padding = new Thickness(0, 4, 12, 4);
            remind.Padding = new Thickness(0, 4, 0, 0);
            SemanticProperties.SetDescription(edit, "Edit this goal");
            SemanticProperties.SetDescription(undo, "Delete the last progress logged for this goal");
            SemanticProperties.SetDescription(delete, "Delete this goal and its progress");
            row = new VerticalStackLayout
            {
                Padding = new Thickness(10, 7),
                Spacing = 2,
                Children = { status, streak, strip, historyLines, remind, new HorizontalStackLayout { Children = { edit, undo, delete } } }
            };
            return row;
        }
    }
}
