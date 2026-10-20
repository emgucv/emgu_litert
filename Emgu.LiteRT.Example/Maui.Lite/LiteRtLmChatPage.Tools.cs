//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

// The agent's tools by group (Emgu.LiteRT.LM.Agents' ToolRouter). The core tools - date and time, calculator, units,
// memories, location and, with the Web search chip, the web tools - come with every message; the camera, device
// actions, goals, photo search, image generation, document search and past-chat search come when a message needs them
// (its words, or the camera being open) and stay for the rest of the conversation, while every other group is a
// one-line stub the model can call to get it in the same request. With all of them in every message, the instructions
// and ~25 tool definitions took ~4k tokens: Gemma 4 E2B on a phone's 4k window failed every message.
public partial class LiteRtLmChatPage
{
    private ToolRouter _toolRouter;
    // What the groups' tools need from the message being sent.
    private List<PendingAttachment> _routeAttachments = new List<PendingAttachment>();
    private IList<AgentGoal> _routeGoals = new List<AgentGoal>();
    private string _routeGoalInstructions;
    private string _routePrompt;

    private ToolRouter ToolRouter => _toolRouter ??= CreateToolRouter();

    private ToolRouter CreateToolRouter()
    {
        var groups = new List<ToolGroup>
        {
            new ToolGroup(ChatToolGroups.Camera, ChatToolGroups.CameraSummary, ChatToolGroups.CameraKeywords,
                CameraTools,
                () => JoinInstructions(SelectedOption.AcceptsImages ? CameraInstructions : null, VisualInstructions)),
            new ToolGroup(ChatToolGroups.Sound, ChatToolGroups.SoundSummary, ChatToolGroups.SoundKeywords,
                SoundTools,
                () => SoundInstructions),
            new ToolGroup(ChatToolGroups.DeviceActions, ChatToolGroups.DeviceActionsSummary, ChatToolGroups.DeviceActionsKeywords,
                DeviceActionToolsForMessage,
                DeviceActionInstructionsForMessage,
                rerunAfterStub: true),
            new ToolGroup(ChatToolGroups.Goals, ChatToolGroups.GoalsSummary, ChatToolGroups.GoalsKeywords,
                GoalToolsForMessage,
                () => _routeGoalInstructions,
                text => AgentTools.MentionsGoal(_routeGoals, text)),
            new ToolGroup(ChatToolGroups.Photos, ChatToolGroups.PhotosSummary, ChatToolGroups.PhotosKeywords,
                () => new AITool[] { AgentTools.FindPhotos(FindPhotosAsync) },
                () => PhotoInstructions),
            new ToolGroup(ChatToolGroups.Documents, ChatToolGroups.DocumentsSummary, ChatToolGroups.DocumentsKeywords,
                () => IsPrivateChat ? Array.Empty<AITool>() : new AITool[] { AgentTools.SearchDocuments(SearchDocumentsAsync) },
                () => ChatPlannerInstructions.Documents),
            new ToolGroup(ChatToolGroups.PastChats, ChatToolGroups.PastChatsSummary, ChatToolGroups.PastChatsKeywords,
                () => IsPrivateChat ? Array.Empty<AITool>() : new AITool[] { AgentTools.SearchChats(SearchChatsAsync) },
                () => ChatSearchInstructions),
        };
        groups.Add(new ToolGroup(ChatToolGroups.WriteDocument, ChatToolGroups.WriteDocumentSummary, ChatToolGroups.WriteDocumentKeywords,
            () => new AITool[] { AgentTools.CreateDocument(SaveWrittenDocumentAsync, () => _routePrompt) },
            () => DocumentWritingInstructions));
        if (ImageGenerationSupported)
            groups.Add(new ToolGroup(ChatToolGroups.ImageGeneration, ChatToolGroups.ImageGenerationSummary, ChatToolGroups.ImageGenerationKeywords,
                () => new AITool[] { AgentTools.GenerateImage(GenerateImageAsync) },
                () => ImageInstructions));
        groups.AddRange(McpGroups());
        // The clock/calendar/maps stub ends the request and RunAgentAsync sends the message again (rerunAfterStub).
        var router = new ToolRouter(groups, () => FunctionInvokingChatClient.CurrentContext?.Options,
            () => FunctionInvokingChatClient.CurrentContext.Terminate = true);
        router.GroupRequested += name => UiStallWatchdog.Mark("Tool group requested by the model: " + name);
        return router;
    }

    private IEnumerable<AITool> CameraTools()
    {
        if (!LiveCamera.Supported)
            return Array.Empty<AITool>();
        var tools = new List<AITool> { AgentTools.OpenCamera(OpenCameraAsync) };
        if (SelectedOption.AcceptsImages)
            tools.Add(AgentTools.CaptureCameraFrame(CaptureCameraFrameAsync));
        tools.AddRange(VisualTools(_routeAttachments));
        return tools;
    }

    // The groups in use, for the context estimate before a send (LiteRtLmChatClient counts the real size): the camera
    // group's tools and instructions are ~1,100 tokens, the others ~150-500.
    private int ToolGroupTokens => _toolRouter == null ? 0
        : _toolRouter.Active.Sum(name => name == ChatToolGroups.Camera ? 1100 : name == ChatToolGroups.DeviceActions || name == ChatToolGroups.Goals ? 500
            : name.StartsWith("mcp_", StringComparison.Ordinal) ? 400 : name == ChatToolGroups.Sound ? 600 : 200);

    // Choose the groups for a message (before its tools are put together): its words, and the app's state.
    private async Task RouteToolsAsync(string prompt, List<PendingAttachment> attachments)
    {
        _routeAttachments = attachments;
        _routePrompt = prompt;
        try { _routeGoals = GoalsOn ? await _store.GetGoalsAsync() : new List<AgentGoal>(); }
        catch (Exception) { _routeGoals = new List<AgentGoal>(); }
        _routeGoalInstructions = await GoalInstructionsAsync();
        ToolRouter router = ToolRouter;
        if (_camera?.IsRunning == true || _visualRuntime.Snapshot().Any())
            router.Activate(ChatToolGroups.Camera);
        if (_soundRuntime?.Snapshot().Count > 0)
            router.Activate(ChatToolGroups.Sound);
        IList<string> added = router.Route(prompt);
        if (added.Count > 0)
            UiStallWatchdog.Mark("Tool groups: " + string.Join(", ", router.Active));
    }
}

#endif
