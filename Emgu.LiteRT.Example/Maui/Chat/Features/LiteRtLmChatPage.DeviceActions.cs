//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Device actions (set_timer, set_alarm, add_calendar_event, add_reminder, open_directions): the agent's tool asks, a
// card above the reply shows what will be done with Confirm / Cancel, and only a confirmed action is done
// (DeviceActions.PerformAsync). The tool returns what happened, so the model can say so; the step line says it too and
// is saved with the chat. Not offered in the voice chat, whose full-screen view would hide the card.
public partial class LiteRtLmChatPage
{
    private static readonly HashSet<string> DeviceActionTools = new HashSet<string>
    {
        "set_timer", "set_alarm", "add_calendar_event", "add_reminder", "open_directions"
    };

    private IEnumerable<AIFunction> DeviceActionToolsForMessage()
    {
        if (_voiceMode || DeviceActions.Supported == DeviceActionKinds.None)
            return Array.Empty<AIFunction>();
        return AgentTools.DeviceActions(DeviceActions.Supported, ConfirmDeviceActionAsync);
    }

    private string DeviceActionInstructionsForMessage() =>
        _voiceMode ? null : AgentTools.DeviceActionInstructions(DeviceActions.Supported);

    // The step line's ending for a device action's result: what the user chose.
    private static string DeviceActionStepOutcome(string result)
    {
        if (result.StartsWith("Done:", StringComparison.Ordinal))
            return " - done";
        if (result.StartsWith("CANCELLED", StringComparison.Ordinal))
            return " - cancelled";
        if (result.StartsWith("It couldn't be done", StringComparison.Ordinal))
            return " - failed";
        return string.Empty;
    }

    private Task<string> ConfirmDeviceActionAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            DeviceActionCard card = AddDeviceActionCard(_agentBubble, request, confirmed => choice.TrySetResult(confirmed));
            UiStallWatchdog.Mark("Device action: waiting for confirmation of " + request.ToolName);
            SetStatus("Waiting for you to confirm...");
            bool confirmed;
            using (cancellationToken.Register(() => choice.TrySetResult(false)))
                confirmed = await choice.Task;
            if (!confirmed)
            {
                card.Finish(cancellationToken.IsCancellationRequested ? "Stopped" : "Cancelled", false);
                UiStallWatchdog.Mark("Device action cancelled");
                SetStatus("Generating...");
                return AgentTools.DeviceActionCancelled;
            }
            card.Finish("Working...", false);
            DeviceActionOutcome outcome = await DeviceActions.PerformAsync(request);
            card.Finish(outcome.ForUser, outcome.Done);
            UiStallWatchdog.Mark("Device action: " + outcome.ForUser);
            SetStatus("Generating...");
            return outcome.ForModel;
        });
    }

    private sealed class DeviceActionCard
    {
        public Border View;
        public HorizontalStackLayout Buttons;
        public Label Result;

        public void Finish(string text, bool done)
        {
            Buttons.IsVisible = false;
            Result.Text = (done ? "✓ " : "") + text;
            Result.TextColor = done ? Theme.Success : Theme.SecondaryText;
            Result.IsVisible = true;
        }
    }

    private static string DeviceActionIcon(DeviceActionRequest request) => request switch
    {
        TimerRequest => "⏲️",
        AlarmRequest => "⏰",
        CalendarEventRequest => "\U0001F4C5",
        ReminderRequest => "✅",
        McpCallRequest => "\U0001F50C",
        DirectionsRequest => "\U0001F5FA️",
        _ => "⚙️"
    };

    // The card above the reply bubble (like the step lines).
    private DeviceActionCard AddDeviceActionCard(StreamingBubble bubble, DeviceActionRequest request, Action<bool> choose)
    {
        DeviceActionCard card = new DeviceActionCard();
        VerticalStackLayout content = new VerticalStackLayout { Spacing = 4 };
        content.Children.Add(new Label
        {
            Text = DeviceActionIcon(request) + "  " + request.Title,
            FontFamily = Theme.TitleFont,
            FontSize = 15,
            TextColor = Theme.PrimaryText
        });
        foreach (string detail in request.Details)
            content.Children.Add(new Label { Text = detail, FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.PrimaryText });

        Button confirm = new Button
        {
            Text = request.ConfirmText,
            FontFamily = Theme.TitleFont,
            FontSize = 14,
            BackgroundColor = Theme.Accent,
            TextColor = Colors.White,
            CornerRadius = 12,
            Padding = new Thickness(16, 6),
            MinimumHeightRequest = 40
        };
        Button cancel = new Button
        {
            Text = "Cancel",
            FontFamily = Theme.TitleFont,
            FontSize = 14,
            BackgroundColor = Colors.Transparent,
            TextColor = Theme.Accent,
            BorderColor = Theme.Accent,
            BorderWidth = 1,
            CornerRadius = 12,
            Padding = new Thickness(16, 6),
            MinimumHeightRequest = 40
        };
        confirm.Clicked += (s, e) => choose(true);
        cancel.Clicked += (s, e) => choose(false);
        card.Buttons = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 6, 0, 0), Children = { confirm, cancel } };
        card.Result = new Label { FontFamily = Theme.BodyFont, FontSize = 13, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
        content.Children.Add(card.Buttons);
        content.Children.Add(card.Result);

        card.View = new Border
        {
            BackgroundColor = Theme.CardBackground,
            Stroke = Theme.RowBorder,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14, 12),
            Margin = new Thickness(6, 2),
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Start,
            Content = content
        };
        SemanticProperties.SetDescription(card.View, request.Title + ": " + string.Join(", ", request.Details));
        AddCard(bubble, card.View);
        return card;
    }
}

#endif
