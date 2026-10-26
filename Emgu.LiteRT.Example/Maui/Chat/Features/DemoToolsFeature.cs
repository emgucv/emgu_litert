// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

// The tool groups of the features that still take the old IChatHost (device actions, goals with the exercise counter,
// photo search, past-chat search, writing documents with the meeting notes, image generation), as groups of the
// library's router: each comes when a message needs it (its words, or a stub the model calls) and stays for the
// conversation. The camera's, the sounds', the documents' and the tool servers' groups are their features' own.
internal sealed class DemoToolsFeature : DemoFeature
{
    private IList<AgentGoal> _routeGoals = new List<AgentGoal>();
    private string _routeGoalInstructions;

    public override string Name => "demo_tools";

    public override string Title => "Device actions, goals, photos, past chats, documents, images";

    public override bool PersistEnabled => false;

    private bool HandsFree => Host?.HandsFree == true;

    // What the groups' tools need from the message being sent: the goals, for the goals group to match.
    public override async Task PrepareAsync(ChatMessageContext context)
    {
        await base.PrepareAsync(context);
        try { _routeGoals = Demo.Goals.On ? await Demo.Store.Goals.GetGoalsAsync() : new List<AgentGoal>(); }
        catch (Exception) { _routeGoals = new List<AgentGoal>(); }
        _routeGoalInstructions = await Demo.Goals.InstructionsAsync();
    }

    public override IEnumerable<ToolGroup> ToolGroups()
    {
        var groups = new List<ToolGroup>
        {
            new ToolGroup(ChatToolGroups.DeviceActions, ChatToolGroups.DeviceActionsSummary, ChatToolGroups.DeviceActionsKeywords,
                () => Demo.ActionTools.ToolsForMessage(HandsFree),
                () => DeviceActionsFeature.InstructionsForMessage(HandsFree),
                rerunAfterStub: true),
            new ToolGroup(ChatToolGroups.Goals, ChatToolGroups.GoalsSummary, ChatToolGroups.GoalsKeywords,
                // Counting jumping jacks comes with the goals: it logs toward them, and a group of its own would add a stub to every message.
                () => Demo.Goals.ToolsForMessage().Cast<AITool>().Concat(Demo.Exercises.Tools(HandsFree)),
                () => JoinInstructions(_routeGoalInstructions, Demo.Exercises.Tools(HandsFree).Any() ? ExerciseFeature.Instructions : null),
                text => AgentTools.MentionsGoal(_routeGoals, text)),
            new ToolGroup(ChatToolGroups.Photos, ChatToolGroups.PhotosSummary, ChatToolGroups.PhotosKeywords,
                () => new AITool[] { AgentTools.FindPhotos(Demo.PhotoSearch.FindAsync) },
                () => PhotoSearchFeature.Instructions),
            new ToolGroup(ChatToolGroups.PastChats, ChatToolGroups.PastChatsSummary, ChatToolGroups.PastChatsKeywords,
                () => Host.IsPrivate ? Array.Empty<AITool>() : new AITool[] { AgentTools.SearchChats(Demo.PastChats.SearchAsync) },
                () => PastChatsFeature.Instructions),
            new ToolGroup(ChatToolGroups.WriteDocument, ChatToolGroups.WriteDocumentSummary, ChatToolGroups.WriteDocumentKeywords,
                () => new AITool[] { AgentTools.CreateDocument(Demo.WrittenDocuments.SaveAsync, () => Prompt) }.Concat(Demo.Meetings.Tools(HandsFree)),
                () => JoinInstructions(WrittenDocumentsFeature.Instructions, Demo.Meetings.Tools(HandsFree).Any() ? MeetingFeature.Instructions : null))
        };
        if (ImageFeature.Supported)
            groups.Add(new ToolGroup(ChatToolGroups.ImageGeneration, ChatToolGroups.ImageGenerationSummary, ChatToolGroups.ImageGenerationKeywords,
                () => new AITool[] { AgentTools.GenerateImage(Demo.Images.GenerateAsync) },
                () => ImageFeature.Instructions));
        return groups;
    }

    // The groups in use, for the context estimate before a send (LiteRtLmChatClient counts the real size): the camera
    // group's tools and instructions are ~1,100 tokens, the others ~150-500.
    public static int GroupTokens(IEnumerable<string> active) => active.Sum(name =>
        name == ChatToolGroups.Camera ? 1100 : name == ChatToolGroups.DeviceActions || name == ChatToolGroups.Goals ? 500
        : name.StartsWith("mcp_", StringComparison.Ordinal) ? 400 : name == ChatToolGroups.Sound ? 600 : 200);
}
