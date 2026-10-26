//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

// Goals (TODO item 25): the agent's add_goal / log_progress / get_goals / remove_goal tools (Emgu.LiteRT.LM.Agents'
// AgentTools.Goals) keep the user's goals in ChatStore, with the memories - on with "Remember things about me", never
// in a private chat. Each message's instructions list the goals with this period's progress, so "I ran today" is
// logged without asking. Settings lists them with their progress, streak and history by period (GoalHistory), to edit, undo
// the last entry or delete. A tool group of the router (with the exercise counter's tools, which log toward the goals),
// matched by a message that names a goal. (The library's GoalsFeature has no Undo last: this one stays.)
internal sealed class GoalsFeature : DemoFeature
{
    private const int MaxGoalLength = 800;
    private Label _goalsLabel;
    // The goals and the instructions loaded for the message being sent (the group's match and instructions are synchronous).
    private IList<AgentGoal> _goals = new List<AgentGoal>();
    private string _instructions;

    public override string Name => "goals";

    public override string Title => "Your goals";

    public override bool PersistEnabled => false;

    private ChatStore Store => Demo.Store;

    private ExerciseFeature Exercises => Host?.Services.Get<ExerciseFeature>();

    // Whether goals are kept: with "Remember things about me" on, never in a private chat.
    public bool On => Host != null && !Host.IsPrivate && Host.MemoryOn;

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override async Task PrepareAsync(ChatMessageContext context)
    {
        await base.PrepareAsync(context);
        try { _goals = On ? await Store.Goals.GetGoalsAsync() : new List<AgentGoal>(); }
        catch (Exception) { _goals = new List<AgentGoal>(); }
        _instructions = await InstructionsAsync();
    }

    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatToolGroups.Goals, ChatToolGroups.GoalsSummary, ChatToolGroups.GoalsKeywords,
            // Counting jumping jacks comes with the goals: it logs toward them, and a group of its own would add a stub to every message.
            () => ToolsForMessage().Concat((Exercises?.Tools(Host?.HandsFree == true) ?? Array.Empty<AITool>())),
            () => JoinInstructions(_instructions, Exercises?.Tools(Host?.HandsFree == true).Any() == true ? ExerciseFeature.Instructions : null),
            text => AgentTools.MentionsGoal(_goals, text))
    };

    public IEnumerable<AITool> ToolsForMessage() =>
        On ? AgentTools.Goals(Store.Goals) : Array.Empty<AITool>();

    public async Task<string> InstructionsAsync()
    {
        if (!On)
            return null;
        try
        {
            return await AgentTools.GoalInstructionsAsync(Store.Goals, DateTime.Now, MaxGoalLength);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The agent may have saved or deleted a goal.
    public override void OnReplyCompleted(ChatExchangeEventArgs e) => _ = RefreshCountAsync();

    public override View BuildSettingsSection()
    {
        _goalsLabel = new Label
        {
            FontFamily = Theme.TitleFont,
            FontSize = 14,
            TextColor = Theme.Accent,
            Padding = new Thickness(0, 4),
            HorizontalOptions = LayoutOptions.Start
        };
        _goalsLabel.OnTap(OnGoalsTapped);
        _ = RefreshCountAsync();
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = "Your goals", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                new Label
                {
                    Text = "Tell Gemma 4 a goal - \"I want to run three times a week\", \"read 20 pages a day\" - and what you did toward it (\"I ran today\"); it keeps count and can tell you how you're doing. Kept on this device, with \"Remember things about me\" on.",
                    FontFamily = Theme.BodyFont,
                    FontSize = 12,
                    TextColor = Theme.SecondaryText
                },
                _goalsLabel
            }
        };
    }

    public async Task RefreshCountAsync()
    {
        if (_goalsLabel == null || Host == null || Host.IsPrivate) return;
        int count = 0;
        try
        {
            count = (await Store.Goals.GetGoalsAsync()).Count;
        }
        catch (Exception)
        {
        }
        _goalsLabel.Text = count == 0 ? "No goals yet" : count == 1 ? "1 goal - show" : count + " goals - show";
    }

    // The goals in a sheet, each with its progress and Edit / Undo last / Delete.
    private async void OnGoalsTapped()
    {
        if (Host.IsPrivate) return;
        IList<AgentGoal> goals = await Store.Goals.GetGoalsAsync();
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
        var status = new Label { FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.PrimaryText };
        // The streak, a strip with a block for each of the latest periods (filled: reached, empty: missed, outlined: the one
        // going on) and, tapping the strip, a line for each.
        var streak = new Label { FontFamily = Theme.TitleFont, FontSize = 13, TextColor = Theme.Accent, IsVisible = false };
        var strip = new HorizontalStackLayout { Spacing = 3, IsVisible = false, Padding = new Thickness(0, 2) };
        var historyLines = new VerticalStackLayout { Spacing = 1, IsVisible = false };
        strip.OnTap(() => historyLines.IsVisible = !historyLines.IsVisible);
        async Task Refresh()
        {
            try
            {
                status.Text = await AgentTools.GoalStatusAsync(Store.Goals, goal, DateTime.Now);
                GoalHistory history = GoalHistory.Compute(goal, await Store.Goals.GetProgressAsync(goal.Id, DateTime.MinValue), DateTime.Now, 12);
                streak.Text = history.StreakText == null ? string.Empty : "\U0001F525 " + history.StreakText;
                streak.IsVisible = history.StreakText != null;
                strip.Children.Clear();
                historyLines.Children.Clear();
                strip.IsVisible = history.Periods.Count > 1;
                foreach (GoalPeriodResult period in history.Periods)
                {
                    strip.Children.Add(period.IsCurrent && !period.Met
                        ? new Border { WidthRequest = 14, HeightRequest = 14, Stroke = Theme.Accent, StrokeThickness = 1, BackgroundColor = Colors.Transparent, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(3) } }
                        : new Border { WidthRequest = 14, HeightRequest = 14, Stroke = Colors.Transparent, BackgroundColor = period.Met ? Theme.Accent : Theme.Chevron, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(3) } });
                    historyLines.Children.Insert(0, new Label { Text = history.Describe(period, goal), FontFamily = Theme.BodyFont, FontSize = 12, TextColor = period.Met ? Theme.PrimaryText : Theme.SecondaryText });
                }
                SemanticProperties.SetDescription(strip, string.Format("The last {0} {1}s: {2} reached the target. Tap to show them.",
                    history.Periods.Count, history.PeriodName, history.Periods.Count(p => p.Met)));
            }
            catch (Exception ex) { status.Text = goal.Text + " (" + ex.Message + ")"; }
        }
        await Refresh();
        Label Action(string text, string description)
        {
            var label = new Label { Text = text, FontFamily = Theme.TitleFont, FontSize = 13, TextColor = Theme.Accent, Padding = new Thickness(0, 4, 12, 4) };
            SemanticProperties.SetDescription(label, description);
            return label;
        }
        var edit = Action("Edit", "Edit this goal");
        var undo = Action("Undo last", "Delete the last progress logged for this goal");
        var delete = Action("Delete", "Delete this goal and its progress");
        var row = new VerticalStackLayout
        {
            Padding = new Thickness(10, 7),
            Spacing = 2,
            Children = { status, streak, strip, historyLines, new HorizontalStackLayout { Children = { edit, undo, delete } } }
        };
        edit.OnTap(async () =>
        {
            string text = await Host.PromptAsync("Edit goal", "The goal, with how much and how often, e.g. \"Read 20 pages a day\".",
                "Save", "Cancel", goal.Text, 200);
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == goal.Text)
                return;
            AgentGoal parsed = AgentGoal.FromText(text, goal.Created);
            goal = new AgentGoal(goal.Id, parsed.Text, parsed.Target, parsed.Unit, parsed.Period, goal.Created);
            await Store.Goals.UpdateGoalAsync(goal);
            await Refresh();
        });
        undo.OnTap(async () =>
        {
            if (await Store.Goals.DeleteLastProgressAsync(goal.Id))
                await Refresh();
        });
        delete.OnTap(async () =>
        {
            if (!await Host.AskAsync("Delete goal", "Delete \"" + goal.Text + "\" and its progress?", "Delete", "Cancel"))
                return;
            await Store.Goals.DeleteGoalAsync(goal.Id);
            list.Children.Remove(row);
            await RefreshCountAsync();
        });
        return row;
    }
}
