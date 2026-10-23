//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.LM.Models;

namespace MauiChat;

public partial class MainPage : ContentPage
{
    private const string LastChatKey = "last_chat";

    // The saved chats, in a folder of the sample's own: on Mac Catalyst the app data folder can be ~/Library itself.
    private readonly ChatHistoryStore _history = new ChatHistoryStore(Path.Combine(FileSystem.AppDataDirectory, "MauiChat", "chats"));
    private ChatHistoryBinding _saving;

    public MainPage()
    {
        InitializeComponent();
        // Qwen3 0.6B: a 345 MB download, quick on a phone's CPU. The view downloads it (after a tap) and loads it; a
        // larger model such as Gemma4E2B (2.6 GB, images, audio and tools) works the same way.
        Chat.Model = new Qwen3();
        Chat.LoadState.DisplayName = "Qwen3 0.6B";
        Chat.LoadState.DownloadSize = 345L << 20;
        // Small models summarize poorly: drop earlier messages instead when the chat outgrows the context window.
        Chat.Conversation.Compaction = ChatCompaction.Drop;
        ToolbarItems.Add(new ToolbarItem("New chat", null, NewChat));
        _ = ReopenLastChatAsync();
    }

    // The chat from last time, shown and continued (the model is given it with the next message).
    private async Task ReopenLastChatAsync()
    {
        int chatId = Preferences.Default.Get(LastChatKey, 0);
        ChatHistory history = null;
        try
        {
            if (chatId > 0)
                history = await _history.LoadAsync(chatId);
        }
        catch (Exception)
        {
        }
        if (history != null && !Chat.Conversation.IsBusy && Chat.Conversation.Messages.Count == 0)
            history.Load(Chat.Conversation);
        else
            chatId = 0;
        SaveFromNowOn(chatId);
    }

    // Every exchange is saved; the first one of a new chat creates it.
    private void SaveFromNowOn(int chatId)
    {
        _saving?.Dispose();
        _saving = _history.Attach(Chat.Conversation, chatId, "Qwen3");
        _saving.ChatCreated += (s, e) => Preferences.Default.Set(LastChatKey, ((ChatHistoryBinding)s).ChatId);
    }

    private void NewChat()
    {
        if (Chat.Conversation.IsBusy)
            return;
        Chat.Conversation.Clear();
        Preferences.Default.Remove(LastChatKey);
        SaveFromNowOn(0);
    }
}
