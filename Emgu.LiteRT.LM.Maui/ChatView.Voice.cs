//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Specialized;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// How <see cref="ChatView"/>'s microphone sends a voice message.
    /// </summary>
    public enum ChatVoiceMessages
    {
        /// <summary>
        /// As audio when the model takes audio (<see cref="ChatView.EffectiveAttachmentKinds"/>), else as the words the
        /// platform's speech recognizer writes down where it can (<see cref="SpeechRecognizer.IsSupported"/>), else no
        /// microphone.
        /// </summary>
        Auto,

        /// <summary>As audio (for a model or client that takes it).</summary>
        Audio,

        /// <summary>As the words the speech recognizer writes down (no microphone where it can't).</summary>
        Text,

        /// <summary>No microphone.</summary>
        Off
    }

    public partial class ChatView
    {
        /// <summary>How the microphone sends a voice message; <see cref="ChatVoiceMessages.Auto"/> by default.</summary>
        public static readonly BindableProperty VoiceMessagesProperty = BindableProperty.Create(
            nameof(VoiceMessages), typeof(ChatVoiceMessages), typeof(ChatView), ChatVoiceMessages.Auto,
            propertyChanged: (b, o, n) => ((ChatView)b).UpdateTalk());

        /// <summary>The longest voice message; recording stops and the message is sent then. 30 s by default.</summary>
        public static readonly BindableProperty MaxVoiceLengthProperty = BindableProperty.Create(
            nameof(MaxVoiceLength), typeof(TimeSpan), typeof(ChatView), TimeSpan.FromSeconds(30));

        /// <summary>
        /// True (the default) to write down the words of a voice message sent as audio, where the platform can, and show
        /// them under it (<see cref="ChatMessageItem.Transcript"/>; not given to the model).
        /// </summary>
        public static readonly BindableProperty TranscribeVoiceMessagesProperty = BindableProperty.Create(
            nameof(TranscribeVoiceMessages), typeof(bool), typeof(ChatView), true);

        private View _talk;
        private bool _talkStyled;
        private VoiceRecorder _recorder;
        private DateTime _recordingStarted;
        private CancellationTokenSource _recordingTimer;
        private bool _transcribing;

        /// <summary>How the microphone sends a voice message; <see cref="ChatVoiceMessages.Auto"/> by default.</summary>
        public ChatVoiceMessages VoiceMessages
        {
            get => (ChatVoiceMessages)GetValue(VoiceMessagesProperty);
            set => SetValue(VoiceMessagesProperty, value);
        }

        /// <summary>The longest voice message; recording stops and the message is sent then. 30 s by default.</summary>
        public TimeSpan MaxVoiceLength
        {
            get => (TimeSpan)GetValue(MaxVoiceLengthProperty);
            set => SetValue(MaxVoiceLengthProperty, value);
        }

        /// <summary>
        /// True (the default) to write down the words of a voice message sent as audio, where the platform can, and show
        /// them under it (<see cref="ChatMessageItem.Transcript"/>; not given to the model).
        /// </summary>
        public bool TranscribeVoiceMessages
        {
            get => (bool)GetValue(TranscribeVoiceMessagesProperty);
            set => SetValue(TranscribeVoiceMessagesProperty, value);
        }

        /// <summary>
        /// How a voice message is sent now: <see cref="ChatVoiceMessages.Audio"/>, <see cref="ChatVoiceMessages.Text"/>,
        /// or <see cref="ChatVoiceMessages.Off"/> (no microphone).
        /// </summary>
        public ChatVoiceMessages EffectiveVoiceMessages
        {
            get
            {
                switch (VoiceMessages)
                {
                    case ChatVoiceMessages.Audio:
                        return ChatVoiceMessages.Audio;
                    case ChatVoiceMessages.Text:
                        return SpeechRecognizer.IsSupported ? ChatVoiceMessages.Text : ChatVoiceMessages.Off;
                    case ChatVoiceMessages.Auto:
                        if (EffectiveAttachmentKinds.HasFlag(ChatAttachmentKinds.Audio))
                            return ChatVoiceMessages.Audio;
                        return SpeechRecognizer.IsSupported ? ChatVoiceMessages.Text : ChatVoiceMessages.Off;
                    default:
                        return ChatVoiceMessages.Off;
                }
            }
        }

        /// <summary>True while a voice message is being recorded.</summary>
        public bool IsRecording => _recorder?.IsRecording == true;

        private void ApplyTalkPart(View talk)
        {
            _talk = talk;
            // A template that styles its microphone keeps its style; otherwise the view switches between the two styles.
            _talkStyled = talk != null && talk.Style != null;
            Hook(talk, "ChatTalkButtonStyle", () => _ = TalkAsync());
            UpdateTalk();
        }

        private void UpdateTalk()
        {
            if (_talk == null)
                return;
            bool recording = IsRecording;
            _talk.IsVisible = recording || EffectiveVoiceMessages != ChatVoiceMessages.Off;
            _talk.IsEnabled = recording || (_replyCancel == null && !_transcribing);
            if (!_talkStyled && ChatResources.Find<Style>(recording ? "ChatTalkStopButtonStyle" : "ChatTalkButtonStyle") is Style style
                && style.TargetType.IsInstanceOfType(_talk))
                _talk.Style = style;
            SemanticProperties.SetDescription(_talk, recording ? "Stop and send the voice message" : "Record a voice message");
            if (_send != null)
                _send.IsEnabled = !recording;
            if (_attach != null)
                _attach.IsEnabled = !recording;
        }

        // Tap to start recording, tap again to send.
        private async Task TalkAsync()
        {
            if (IsRecording)
            {
                await StopRecordingAndSendAsync();
                return;
            }
            if (_replyCancel != null || _transcribing || Conversation == null || EffectiveVoiceMessages == ChatVoiceMessages.Off)
                return;
            ReplySpeaker?.Stop();
            _recorder ??= new VoiceRecorder();
            try
            {
                if (!await _recorder.StartAsync())
                {
                    SetStatus("Microphone access is needed to record a voice message.");
                    return;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't start recording: " + ex.Message);
                return;
            }
            _recordingStarted = DateTime.UtcNow;
            UpdateTalk();
            CancellationTokenSource timer = new CancellationTokenSource();
            _recordingTimer = timer;
            _ = ShowRecordingAsync(timer.Token);
        }

        // "Listening... 0:03", and the message is sent at MaxVoiceLength.
        private async Task ShowRecordingAsync(CancellationToken token)
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
                    SetStatus(string.Format("Listening... {0:m\\:ss} - tap the microphone again to send", elapsed));
                    await Task.Delay(250, token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task StopRecordingAndSendAsync()
        {
            if (!IsRecording)
                return;
            _recordingTimer?.Cancel();
            _recordingTimer = null;
            TimeSpan length = DateTime.UtcNow - _recordingStarted;
            byte[] wav;
            try
            {
                wav = await _recorder.StopAsync();
            }
            catch (Exception ex)
            {
                UpdateTalk();
                SetStatus("Couldn't record: " + ex.Message);
                return;
            }
            UpdateTalk();
            SetStatus(null);
            if (wav == null || length < TimeSpan.FromSeconds(0.5))
            {
                SetStatus("That was too short - tap the microphone, speak, then tap it again.");
                return;
            }

            string typed = _input?.Text?.Trim() ?? string.Empty;
            List<DataContent> attachments = PendingAttachments.ToList();
            if (EffectiveVoiceMessages == ChatVoiceMessages.Text)
            {
                // The words are the message (with whatever was typed).
                string words;
                _transcribing = true;
                UpdateTalk();
                SetStatus("Writing down what you said...");
                try
                {
                    words = (await SpeechRecognizer.TranscribeAsync(wav))?.Trim();
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                    return;
                }
                finally
                {
                    _transcribing = false;
                    UpdateTalk();
                }
                if (string.IsNullOrEmpty(words))
                {
                    SetStatus("I didn't catch any words - please try again.");
                    return;
                }
                SetStatus(null);
                ClearInput();
                await SendAsync(typed.Length > 0 ? typed + " " + words : words, attachments, voiceMessage: true);
                return;
            }

            // The recording is the message; its words are written down for the transcript meanwhile.
            DataContent recording = new DataContent(wav, "audio/wav") { Name = string.Format("Voice message ({0:m\\:ss})", length) };
            attachments.Add(recording);
            Task<string> transcript = TranscribeVoiceMessages && SpeechRecognizer.IsSupported ? TranscribeQuietlyAsync(wav) : null;
            ClearInput();
            using (TranscriptOf(recording, transcript))
                await SendAsync(typed, attachments, voiceMessage: true);
        }

        private void ClearInput()
        {
            if (_input != null)
                _input.Text = string.Empty;
            PendingAttachments.Clear();
        }

        // The words, or null if they couldn't be written down (the message went to the model as audio anyway).
        private static async Task<string> TranscribeQuietlyAsync(byte[] wav)
        {
            try
            {
                return await SpeechRecognizer.TranscribeAsync(wav);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Puts the words on the user message that carries the recording, once both exist.
        private IDisposable TranscriptOf(DataContent recording, Task<string> transcript)
        {
            ChatConversation conversation = Conversation;
            if (transcript == null || conversation == null)
                return new Unsubscriber(null);
            NotifyCollectionChangedEventHandler onAdded = null;
            onAdded = (s, e) =>
            {
                ChatMessageItem message = e.NewItems?.OfType<ChatMessageItem>()
                    .FirstOrDefault(m => m.Role == ChatMessageRole.User && m.Attachments.Contains(recording));
                if (message == null)
                    return;
                conversation.Messages.CollectionChanged -= onAdded;
                _ = transcript.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion && !string.IsNullOrWhiteSpace(t.Result))
                        Dispatcher.Dispatch(() => message.Transcript = t.Result);
                }, TaskScheduler.Default);
            };
            conversation.Messages.CollectionChanged += onAdded;
            return new Unsubscriber(() => conversation.Messages.CollectionChanged -= onAdded);
        }

        // Discards a recording in progress (the view left the page).
        private async Task CancelRecordingAsync()
        {
            if (!IsRecording)
                return;
            _recordingTimer?.Cancel();
            _recordingTimer = null;
            try { await _recorder.StopAsync(); }
            catch (Exception) { }
            UpdateTalk();
            SetStatus(null);
        }
    }
}
