//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Device actions (set_timer, set_alarm, add_calendar_event, add_reminder, open_directions - the ones this platform can
/// do, <see cref="DeviceActions.Supported"/>): the agent's tool asks, a <see cref="ConfirmationCard"/> above the reply
/// shows what will be done with Confirm / Cancel, and only a confirmed action is done (<see cref="DeviceActions.PerformAsync"/>).
/// The tool returns what happened, so the model can say so; the step line says it too and is saved with the chat. A
/// tool group of the router (<see cref="ChatDeviceActions"/>), with a rerun after its stub (the clock/calendar/maps
/// stub ends the request and the message goes again with the tools). Not offered in a hands-free chat, whose
/// full-screen view would hide the card. Android needs no permission (the clock, calendar and maps apps are opened);
/// iOS and Mac Catalyst need NSCalendarsWriteOnlyAccessUsageDescription and NSRemindersFullAccessUsageDescription in
/// Info.plist.
/// </summary>
public sealed class DeviceActionsFeature : ChatFeature
{
    /// <inheritdoc/>
    public override string Name => "device_actions";

    /// <inheritdoc/>
    public override string Title => "Timers, alarms, calendar, reminders, directions";

    /// <summary>Always on where the platform can do something (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <summary>The device actions' tool names (their step lines say what the user chose).</summary>
    public static readonly HashSet<string> ToolNames = new HashSet<string>
    {
        "set_timer", "set_alarm", "add_calendar_event", "add_reminder", "open_directions"
    };

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatDeviceActions.GroupName, ChatDeviceActions.GroupSummary, ChatDeviceActions.GroupKeywords,
            () => ToolsForMessage(Host?.HandsFree == true),
            () => InstructionsForMessage(Host?.HandsFree == true),
            rerunAfterStub: true)
    };

    /// <summary>The tools for a message: none hands-free, or where the platform can do nothing.</summary>
    /// <param name="handsFree">Whether the chat is hands-free</param>
    public IEnumerable<AITool> ToolsForMessage(bool handsFree)
    {
        if (handsFree || DeviceActions.Supported == DeviceActionKinds.None)
            return Array.Empty<AITool>();
        return AgentTools.DeviceActions(DeviceActions.Supported, ConfirmDeviceActionAsync);
    }

    /// <summary>The group's instructions for a message: none hands-free.</summary>
    /// <param name="handsFree">Whether the chat is hands-free</param>
    public static string InstructionsForMessage(bool handsFree) =>
        handsFree ? null : AgentTools.DeviceActionInstructions(DeviceActions.Supported);

    /// <summary>The step line's ending for a device action's result: what the user chose (" - done", " - cancelled", " - failed").</summary>
    /// <param name="result">The tool's result</param>
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
    /// <inheritdoc/>
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
            Host.SetStatus("Waiting for you to confirm...");
            bool confirmed;
            using (cancellationToken.Register(() => choice.TrySetResult(false)))
                confirmed = await choice.Task;
            if (!confirmed)
            {
                card.Finish(cancellationToken.IsCancellationRequested ? "Stopped" : "Cancelled", false);
                Host.SetStatus("Generating...");
                return AgentTools.DeviceActionCancelled;
            }
            card.Finish("Working...", false);
            DeviceActionOutcome outcome = await DeviceActions.PerformAsync(request);
            card.Finish(outcome.ForUser, outcome.Done);
            System.Diagnostics.Debug.WriteLine("Device action: " + outcome.ForUser);
            Host.SetStatus("Generating...");
            return outcome.ForModel;
        });
    }
}
