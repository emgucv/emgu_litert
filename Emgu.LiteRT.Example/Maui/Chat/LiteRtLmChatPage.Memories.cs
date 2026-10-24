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

    // The memories, for the agent's instructions: the latest that fit MaxMemoryLength, oldest first.
    private async Task<string> MemoryInstructionsAsync()
    {
        if (IsPrivateChat || !_memorySwitch.IsToggled)
            return null;
        try
        {
            return await AgentTools.MemoryInstructionsAsync(_store, MaxMemoryLength);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task RefreshMemoryCountAsync()
    {
        if (IsPrivateChat) return;
        int count = 0;
        try
        {
            count = (await _store.GetMemoriesAsync()).Count;
        }
        catch (Exception)
        {
        }
        _memoryCountLabel.Text = count == 0 ? "No memories yet" : count == 1 ? "1 memory - show" : count + " memories - show";
    }

    // The memories in a sheet, each with a button to delete it.
    private async void OnMemoriesTapped()
    {
        if (IsPrivateChat) return;
        List<MemoryRecord> memories = await _store.GetMemoriesAsync();
        if (memories.Count == 0)
            return;
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (MemoryRecord memory in memories)
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
            int id = memory.Id;
            delete.OnTap(async () =>
            {
                await _store.DeleteMemoryAsync(id);
                list.Children.Remove(row);
                await RefreshMemoryCountAsync();
            });
            row.Add(delete, 1, 0);
            list.Children.Add(row);
        }
        await _sheet.ShowViewAsync("Memories about you", list, "Close");
        await RefreshMemoryCountAsync();
    }
}
