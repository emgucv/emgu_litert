// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The words of a voice message. A model that hears audio (Gemma 4) is given the recording itself; Emgu.LiteRT.LM.Maui's
// ChatView has the platform's speech recognizer write the words down in the background while the model answers, and
// shows them under the message (ChatMessageItem.Transcript) - for the transcript, the chat's title and the export, not
// given to the model. The page saves its chats itself (ChatPageOptions.AutoSave off), so this keeps the words with the
// saved message once both exist (Track), and titles a chat still called "Voice message" with them.
internal sealed class TranscriptFeature : DemoFeature
{
    public override string Name => "transcripts";

    public override string Title => "Voice message transcripts";

    public override bool PersistEnabled => false;

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    // The message was saved in a chat: its words go to the store when they are there (now, or when they arrive).
    public void Track(ChatMessageItem message, int chatId)
    {
        if (message == null || Host.IsPrivate)
            return;
        if (message.HasTranscript)
        {
            _ = SaveAsync(message, chatId);
            return;
        }
        System.ComponentModel.PropertyChangedEventHandler handler = null;
        handler = (s, e) =>
        {
            if (e.PropertyName != nameof(ChatMessageItem.Transcript) || !message.HasTranscript)
                return;
            message.PropertyChanged -= handler;
            _ = SaveAsync(message, chatId);
        };
        message.PropertyChanged += handler;
    }

    private async Task SaveAsync(ChatMessageItem message, int chatId)
    {
        try
        {
            int messageId = Demo.Store.GetMessageId(message);
            if (messageId <= 0 || string.IsNullOrWhiteSpace(message.Transcript))
                return;
            await Demo.Store.SetTranscriptAsync(chatId, messageId, message.Transcript);
            Host.RefreshChatList();
        }
        catch (Exception ex)
        {
            UiStallWatchdog.Mark("Saving the voice message transcript failed: " + ex.Message);
        }
    }
}
