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

// Voice messages: recording them, and writing them down for models that can't hear audio.
namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // ---------- Voice messages ----------

    // Tap to start recording, tap again to send. The recording goes to the model as audio, with whatever
    // is typed in the editor and any pending attachments.
    private async void OnTalkClicked()
    {
        if (_recorder.IsRecording)
        {
            await StopRecordingAndSendAsync();
            return;
        }
        if (_busy || _reloadingCpuThreads)
            return;

        StopSpeaking();
        try
        {
            if (!await _recorder.StartAsync())
            {
                SetStatus("Microphone access is needed to talk to the model.");
                return;
            }
        }
        catch (Exception ex)
        {
            SetStatus("Could not start recording: " + ex.Message);
            return;
        }

        _recordingStarted = DateTime.UtcNow;
        SetRecording(true);
        _recordingTimer = new CancellationTokenSource();
        _ = UpdateRecordingStatusAsync(_recordingTimer.Token);
    }

    private async Task UpdateRecordingStatusAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan elapsed = DateTime.UtcNow - _recordingStarted;
                if (elapsed >= MaxVoiceLength)
                {
                    await StopRecordingAndSendAsync();
                    return;
                }
                SetStatus(string.Format("Listening... {0:m\\:ss} - tap the button again to send", elapsed));
                await Task.Delay(250, token);
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async Task StopRecordingAndSendAsync()
    {
        int sessionVersion = _sessionVersion;
        UiStallWatchdog.Mark("Stop recording");
        if (!_recorder.IsRecording)
            return;
        _recordingTimer?.Cancel();
        TimeSpan length = DateTime.UtcNow - _recordingStarted;
        byte[] wav;
        try
        {
            wav = await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            SetRecording(false);
            SetStatus("Could not record: " + ex.Message);
            return;
        }
        SetRecording(false);
        if (sessionVersion != _sessionVersion) return;
        SetStatus(null);
        if (wav == null || length < TimeSpan.FromSeconds(0.5))
        {
            SetStatus("That was too short - tap the microphone, speak, then tap it again.");
            return;
        }

        List<PendingAttachment> attachments = _pending.ToList();
        attachments.Add(new PendingAttachment
        {
            Attachment = ChatAttachment.Audio(wav),
            Data = wav,
            MediaType = "audio/wav",
            Name = string.Format("Voice message ({0:m\\:ss})", length)
        });
        await SendAsync(_promptEditor.Text?.Trim() ?? string.Empty, attachments);
    }

    private void SetRecording(bool recording)
    {
        _talkButton.BackgroundColor = recording ? Theme.Danger : Theme.TileBackground;
        _micIcon.IsVisible = !recording;
        _stopIcon.IsVisible = recording;
        SemanticProperties.SetDescription(_talkButton, recording ? "Stop and send the voice message" : "Talk: record a voice message");
        _sendButton.IsEnabled = !recording;
        _attachButton.IsEnabled = !recording;
        _modelChip.Opacity = CanChangeModel ? 1 : 0.5;
        _newChatButton.Opacity = CanChangeModel ? 1 : 0.5;
    }

    // Discard a recording in progress (leaving the page).
    private async Task CancelRecordingAsync()
    {
        if (!_recorder.IsRecording)
            return;
        _recordingTimer?.Cancel();
        try { await _recorder.StopAsync(); }
        catch { }
        SetRecording(false);
    }

    // ---------- Voice for models without audio ----------

    // A voice message can be sent to the model: as audio, or as text the platform's speech recognizer wrote.
    private static bool TakesVoice(ModelOption option) => option.AcceptsAudio || SpeechRecognizer.IsSupported;

    private static bool IsVoiceRecording(PendingAttachment attachment) =>
        attachment.Image == null && attachment.MediaType != null && attachment.MediaType.StartsWith("audio/", StringComparison.Ordinal);

    // Replace the voice recordings among the attachments with their words; null (with the status saying why) if there
    // were none or recognition failed.
    private async Task<string> TranscribeVoiceAsync(List<PendingAttachment> attachments, CancellationToken token)
    {
        SetStatus("Writing down what you said...");
        UiStallWatchdog.Mark("Speech recognition");
        var words = new List<string>();
        try
        {
            foreach (PendingAttachment recording in attachments.Where(IsVoiceRecording).ToList())
            {
                string text = (await SpeechRecognizer.TranscribeAsync(recording.Data, token))?.Trim();
                if (!string.IsNullOrEmpty(text))
                    words.Add(text);
                attachments.Remove(recording);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            UiStallWatchdog.Mark("Speech recognition failed: " + ex.Message);
            SetStatus(ex.Message);
            return null;
        }
        UiStallWatchdog.Mark("Speech recognized: " + words.Sum(w => w.Length) + " chars");
        if (words.Count == 0)
        {
            SetStatus("I didn't catch any words - please try again.");
            return null;
        }
        SetStatus(null);
        return string.Join(" ", words);
    }
}
