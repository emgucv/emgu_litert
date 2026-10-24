//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The words of a voice message. A model that hears audio (Gemma 4) is given the recording itself, so the chat only showed a
// "Voice message" chip. The platform's speech recognizer (SpeechRecognizer: Android 13+, iOS, Mac Catalyst) writes the words
// down in the background while the model answers, they are shown under the chip and kept with the message
// (MessageRecord.Transcript) - for the transcript, the chat's title and the export, not given to the model. A model that
// can't hear audio already gets the words as its message.
public partial class LiteRtLmChatPage
{
    // The words are shown under the message by the demo's template (UserMessageInfo.TranscriptText).
    private async Task TranscribeVoiceMessageAsync(UserMessageInfo item, byte[] wav)
    {
        string words = null;
        try
        {
            words = (await SpeechRecognizer.TranscribeAsync(wav, CancellationToken.None))?.Trim();
        }
        catch (Exception ex)
        {
            UiStallWatchdog.Mark("Voice message transcript failed: " + ex.Message);
        }
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            item.Transcript = string.IsNullOrEmpty(words) ? null : words;
            item.Transcribing = false;
        });
    }

    // Once the message is saved and its words are ready: keep them (and title a chat still called "Voice message" with them).
    private async Task SaveTranscriptWhenReadyAsync(int conversationId, UserMessageInfo item)
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
