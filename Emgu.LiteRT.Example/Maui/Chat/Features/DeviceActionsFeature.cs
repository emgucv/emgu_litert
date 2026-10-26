//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Mcp;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

// Device actions (set_timer, set_alarm, add_calendar_event, add_reminder, open_directions): the agent's tool asks, a
// card above the reply shows what will be done with Confirm / Cancel, and only a confirmed action is done
// (DeviceActions.PerformAsync). The tool returns what happened, so the model can say so; the step line says it too and
// is saved with the chat. A tool group of the router, with a rerun after its stub (the clock/calendar/maps stub ends the
// request and the message goes again with the tools). Not offered in a hands-free chat, whose full-screen view would
// hide the card.
internal sealed class DeviceActionsFeature : DemoFeature
{
    public override string Name => "device_actions";

    public override string Title => "Timers, alarms, calendar, reminders, directions";

    public override bool PersistEnabled => false;

    // The device actions' tool names (their step lines say what the user chose).
    public static readonly HashSet<string> ToolNames = new HashSet<string>
    {
        "set_timer", "set_alarm", "add_calendar_event", "add_reminder", "open_directions"
    };

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatToolGroups.DeviceActions, ChatToolGroups.DeviceActionsSummary, ChatToolGroups.DeviceActionsKeywords,
            () => ToolsForMessage(Host?.HandsFree == true),
            () => InstructionsForMessage(Host?.HandsFree == true),
            rerunAfterStub: true)
    };

    public IEnumerable<AITool> ToolsForMessage(bool handsFree)
    {
        if (handsFree || DeviceActions.Supported == DeviceActionKinds.None)
            return Array.Empty<AITool>();
        return AgentTools.DeviceActions(DeviceActions.Supported, ConfirmDeviceActionAsync);
    }

    public static string InstructionsForMessage(bool handsFree) =>
        handsFree ? null : AgentTools.DeviceActionInstructions(DeviceActions.Supported);

    // The step line's ending for a device action's result: what the user chose.
    public static string StepOutcome(string result)
    {
        if (result.StartsWith("Done:", StringComparison.Ordinal))
            return " - done";
        if (result.StartsWith("CANCELLED", StringComparison.Ordinal))
            return " - cancelled";
        if (result.StartsWith("It couldn't be done", StringComparison.Ordinal))
            return " - failed";
        return string.Empty;
    }

    // A device action done: what the user chose on its step line, also in the saved chat.
    public override void OnToolStepCompleted(ChatToolStep step)
    {
        if (step.Error != null || !ToolNames.Contains(step.Name))
            return;
        step.Description += StepOutcome(step.Result?.Result?.ToString() ?? string.Empty);
    }

    private Task<string> ConfirmDeviceActionAsync(DeviceActionRequest request, CancellationToken cancellationToken)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Card card = Card.Create(request, confirmed => choice.TrySetResult(confirmed));
            Host.AddCardToReply(card.View);
            UiStallWatchdog.Mark("Device action: waiting for confirmation of " + request.ToolName);
            Host.SetStatus("Waiting for you to confirm...");
            bool confirmed;
            using (cancellationToken.Register(() => choice.TrySetResult(false)))
                confirmed = await choice.Task;
            if (!confirmed)
            {
                card.Finish(cancellationToken.IsCancellationRequested ? "Stopped" : "Cancelled", false);
                UiStallWatchdog.Mark("Device action cancelled");
                Host.SetStatus("Generating...");
                return AgentTools.DeviceActionCancelled;
            }
            card.Finish("Working...", false);
            DeviceActionOutcome outcome = await DeviceActions.PerformAsync(request);
            card.Finish(outcome.ForUser, outcome.Done);
            UiStallWatchdog.Mark("Device action: " + outcome.ForUser);
            Host.SetStatus("Generating...");
            return outcome.ForModel;
        });
    }

    // The card that asks before an action is done (also the tool servers' confirmation): what will be done, Confirm / Cancel, then the
    // outcome.
    internal sealed class Card
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

        private static string Icon(DeviceActionRequest request) => request switch
        {
            TimerRequest => "⏲️",
            AlarmRequest => "⏰",
            CalendarEventRequest => "\U0001F4C5",
            ReminderRequest => "✅",
            McpCallRequest => "\U0001F50C",
            DirectionsRequest => "\U0001F5FA️",
            _ => "⚙️"
        };

        // The card, for above the reply bubble (like the step lines).
        public static Card Create(DeviceActionRequest request, Action<bool> choose)
        {
            Card card = new Card();
            VerticalStackLayout content = new VerticalStackLayout { Spacing = 4 };
            content.Children.Add(new Label
            {
                Text = Icon(request) + "  " + request.Title,
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
            return card;
        }
    }
}
