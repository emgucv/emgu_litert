//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Emgu.LiteRT.LM.Chat.Sqlite;

// Memories about the user: the instructions they go into, and the list in Settings.
namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // ---------- Memories about the user ----------

    // The memories, for the agent's instructions: the latest that fit MaxMemoryLength, oldest first. Kept in _memoriesGiven,
    // to show which ones the reply drew on.
    private async Task<string> MemoryInstructionsAsync()
    {
        _memoriesGiven = null;
        if (IsPrivateChat || !_memorySwitch.IsToggled)
            return null;
        try
        {
            _memoriesGiven = AgentTools.MemoriesForInstructions(await _store.Memories.GetMemoriesAsync(), MaxMemoryLength);
            return AgentTools.MemoryInstructions(_memoriesGiven);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The memories in the instructions of the message being sent, or null.
    private IList<AgentMemory> _memoriesGiven;

    // Under a finished reply: the memories it drew on (AgentTools.MemoriesUsed: by their words - the model doesn't say), as a line
    // that opens them, and saved with the chat as a step line. Not for memories deleted in the same turn ("forget that...").
    private async Task ShowMemoriesUsedAsync(string prompt, ChatMessageItem reply, StreamingBubble bubble)
    {
        IList<AgentMemory> given = _memoriesGiven;
        if (bubble == null || given == null || given.Count == 0 || reply.State != ChatMessageState.Done || !reply.HasText)
            return;
        try
        {
            HashSet<string> kept = (await _store.Memories.GetMemoriesAsync()).Select(m => m.Id).ToHashSet();
            List<AgentMemory> used = AgentTools.MemoriesUsed(given, prompt, reply.Text).Where(m => kept.Contains(m.Id)).ToList();
            if (used.Count == 0)
                return;
            bubble.MemoryStep = MemoryStepText(used);
            var line = StepLine(bubble.MemoryStep);
            line.Text = "\U0001F9E0 " + bubble.MemoryStep;
            SemanticProperties.SetHint(line, "Shows these memories, to delete one");
            line.OnTap(() => OnMemoriesTapped(used.Select(m => m.Id).ToHashSet()));
            AddCard(bubble, line);
        }
        catch (Exception)
        {
        }
    }

    // "Used a memory: The user is vegetarian." / "Used 2 memories: ... · ..."
    internal static string MemoryStepText(IList<AgentMemory> used) =>
        (used.Count == 1 ? "Used a memory: " : "Used " + used.Count + " memories: ") + string.Join(" · ", used.Select(m => m.Text));

    private async Task RefreshMemoryCountAsync()
    {
        if (IsPrivateChat) return;
        int count = 0;
        try
        {
            count = (await _store.Memories.GetMemoriesAsync()).Count;
        }
        catch (Exception)
        {
        }
        _memoryCountLabel.Text = count == 0 ? "No memories yet" : count == 1 ? "1 memory - show" : count + " memories - show";
    }

    // The memories in a sheet, each with a button to delete it; only these ids when given (a reply's memories).
    private void OnMemoriesTapped() => OnMemoriesTapped(null);

    private async void OnMemoriesTapped(HashSet<string> only)
    {
        if (IsPrivateChat) return;
        IList<AgentMemory> memories = await _store.Memories.GetMemoriesAsync();
        if (only != null)
            memories = memories.Where(m => only.Contains(m.Id)).ToList();
        if (memories.Count == 0)
            return;
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (AgentMemory memory in memories)
        {
            var row = new Grid
            {
                Padding = new Thickness(10, 7),
                ColumnSpacing = 6,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            row.Add(new Label { Text = memory.Text, FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 0, 0);
            var delete = new Label
            {
                Text = Theme.GlyphClose,
                FontFamily = Theme.IconFont,
                FontSize = 18,
                TextColor = Theme.SecondaryText,
                Padding = new Thickness(6),
                VerticalOptions = LayoutOptions.Center
            };
            SemanticProperties.SetDescription(delete, "Delete this memory");
            string id = memory.Id;
            delete.OnTap(async () =>
            {
                await _store.Memories.DeleteMemoryAsync(id);
                list.Children.Remove(row);
                await RefreshMemoryCountAsync();
            });
            row.Add(delete, 1, 0);
            list.Children.Add(row);
        }
        await _sheet.ShowViewAsync(only != null ? "Memories this reply used" : "Memories about you", list, "Close");
        await RefreshMemoryCountAsync();
    }
}
