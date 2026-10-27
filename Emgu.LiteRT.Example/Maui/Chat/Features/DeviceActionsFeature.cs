//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
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
            ConfirmationCard card = ConfirmationCard.Create(request, confirmed => choice.TrySetResult(confirmed));
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
}
