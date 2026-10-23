//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Models;

namespace MauiChat;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        // Qwen3 0.6B: a 345 MB download, quick on a phone's CPU. The view downloads it (after a tap) and loads it; a
        // larger model such as Gemma4E2B (2.6 GB, images, audio and tools) works the same way.
        Chat.Model = new Qwen3();
        Chat.LoadState.DisplayName = "Qwen3 0.6B";
        Chat.LoadState.DownloadSize = 345L << 20;
        // Small models summarize poorly: drop earlier messages instead when the chat outgrows the context window.
        Chat.Conversation.Compaction = Emgu.LiteRT.LM.Chat.ChatCompaction.Drop;
    }
}
