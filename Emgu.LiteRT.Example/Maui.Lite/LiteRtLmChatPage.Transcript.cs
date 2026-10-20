//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

// The words of a voice message. A model that hears audio (Gemma 4) is given the recording itself, so the chat only showed a
// "Voice message" chip. The platform's speech recognizer (SpeechToText: Android 13+, iOS, Mac Catalyst) writes the words
// down in the background while the model answers, they are shown under the chip and kept with the message
// (MessageRecord.Transcript) - for the transcript, the chat's title and the export, not given to the model. A model that
// can't hear audio already gets the words as its message.
public partial class LiteRtLmChatPage
{
    private async Task TranscribeVoiceMessageAsync(HistoryItem item, byte[] wav, Label label)
    {
        string words = null;
        try
        {
            words = (await SpeechToText.TranscribeAsync(wav, CancellationToken.None))?.Trim();
        }
        catch (Exception ex)
        {
            UiStallWatchdog.Mark("Voice message transcript failed: " + ex.Message);
        }
        item.Transcript = string.IsNullOrEmpty(words) ? null : words;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            label.Text = item.Transcript == null ? string.Empty : "\u201C" + item.Transcript + "\u201D";
            label.IsVisible = item.Transcript != null;
        });
    }

    // Once the message is saved and its words are ready: keep them (and title a chat still called "Voice message" with them).
    private async Task SaveTranscriptWhenReadyAsync(int conversationId, HistoryItem item)
    {
        try
        {
            await item.TranscriptTask;
            if (!string.IsNullOrEmpty(item.Transcript) && item.MessageId > 0)
            {
                await _store.SetTranscriptAsync(conversationId, item.MessageId, item.Transcript);
                await RefreshChatListAsync();
            }
        }
        catch (Exception ex)
        {
            UiStallWatchdog.Mark("Saving the voice message transcript failed: " + ex.Message);
        }
    }
}

#endif
