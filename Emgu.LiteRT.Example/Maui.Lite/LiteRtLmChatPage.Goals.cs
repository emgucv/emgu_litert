//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;

namespace Maui.Demo.Lite;

// Goals (TODO item 25): the agent's add_goal / log_progress / get_goals / remove_goal tools (Emgu.LiteRT.LM.Agents'
// AgentTools.Goals) keep the user's goals in ChatStore, with the memories - on with "Remember things about me", never
// in a private chat. Each message's instructions list the goals with this period's progress, so "I ran today" is
// logged without asking. Settings lists them with their progress, to edit, undo the last entry or delete.
public partial class LiteRtLmChatPage
{
    private const int MaxGoalLength = 800;
    private Label _goalsLabel;

    private bool GoalsOn => !IsPrivateChat && _memorySwitch.IsToggled;

    private IEnumerable<Microsoft.Extensions.AI.AIFunction> GoalToolsForMessage() =>
        GoalsOn ? AgentTools.Goals(_store) : Array.Empty<Microsoft.Extensions.AI.AIFunction>();

    private async Task<string> GoalInstructionsAsync()
    {
        if (!GoalsOn)
            return null;
        try
        {
            return await AgentTools.GoalInstructionsAsync(_store, DateTime.Now, MaxGoalLength);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private View BuildGoalsSection()
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
        _ = RefreshGoalCountAsync();
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

    private async Task RefreshGoalCountAsync()
    {
        if (_goalsLabel == null || IsPrivateChat) return;
        int count = 0;
        try
        {
            count = (await _store.GetGoalsAsync()).Count;
        }
        catch (Exception)
        {
        }
        _goalsLabel.Text = count == 0 ? "No goals yet" : count == 1 ? "1 goal - show" : count + " goals - show";
    }

    // The goals in a sheet, each with its progress and Edit / Undo last / Delete.
    private async void OnGoalsTapped()
    {
        if (IsPrivateChat) return;
        IList<AgentGoal> goals = await _store.GetGoalsAsync();
        if (goals.Count == 0)
            return;
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (AgentGoal goal in goals)
            list.Children.Add(await BuildGoalRowAsync(goal, list));
        await _sheet.ShowViewAsync("Your goals", list, "Close");
        await RefreshGoalCountAsync();
    }

    private async Task<View> BuildGoalRowAsync(AgentGoal goal, VerticalStackLayout list)
    {
        var status = new Label { FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.PrimaryText };
        async Task Refresh()
        {
            try { status.Text = await AgentTools.GoalStatusAsync(_store, goal, DateTime.Now); }
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
            Children = { status, new HorizontalStackLayout { Children = { edit, undo, delete } } }
        };
        edit.OnTap(async () =>
        {
            string text = await DisplayPromptAsync("Edit goal", "The goal, with how much and how often, e.g. \"Read 20 pages a day\".",
                "Save", "Cancel", initialValue: goal.Text, maxLength: 200);
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == goal.Text)
                return;
            AgentGoal parsed = AgentGoal.FromText(text, goal.Created);
            goal = new AgentGoal(goal.Id, parsed.Text, parsed.Target, parsed.Unit, parsed.Period, goal.Created);
            await _store.UpdateGoalAsync(goal);
            await Refresh();
        });
        undo.OnTap(async () =>
        {
            if (await _store.DeleteLastProgressAsync(goal.Id))
                await Refresh();
        });
        delete.OnTap(async () =>
        {
            if (!await DisplayAlertAsync("Delete goal", "Delete \"" + goal.Text + "\" and its progress?", "Delete", "Cancel"))
                return;
            await _store.DeleteGoalAsync(goal.Id);
            list.Children.Remove(row);
            await RefreshGoalCountAsync();
        });
        return row;
    }
}

#endif
