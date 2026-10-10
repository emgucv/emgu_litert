//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Chat demo backed by LiteRT-LM: downloads a .litertlm model and runs it entirely on this device
    /// through Emgu.LiteRT.LM.Models (LanguageModel, Chat). Wired up for Windows, iOS, Android and Mac
    /// Catalyst (Apple Silicon only). For models that accept images and audio (Gemma 4), the "+" button
    /// attaches a photo or an audio file to the next message, and the microphone button records a voice
    /// message: the model answers what was said (no transcription step). Replies can be read aloud.
    /// </summary>
    public class LiteRtLmChatPage : ContentPage
    {
        private enum ModelChoice
        {
            Qwen3,
            Qwen35_0_8B_VL,
            Qwen35_4B,
            Gemma4E2B,
            Gemma4E4B
        }

        private sealed class ModelOption
        {
            public string Name;
            public string Detail;
            public ModelChoice Choice;
            // Whether the model accepts images / audio (LanguageModel.SupportsImages / SupportsAudio) and can
            // think, known before the model is loaded so the controls can be shown right away.
            public bool AcceptsImages;
            public bool AcceptsAudio;
            public bool CanThink = true;
        }

        // An image or audio file attached to the message being written.
        private sealed class PendingAttachment
        {
            public ChatAttachment Attachment;
            // The (re-encoded) image, for the thumbnails; null for audio.
            public byte[] Image;
            public string Name;
            // Its chip in the composer, while pending.
            public View Chip;
        }

        // LiteRT-LM decodes audio with miniaudio, which reads WAV, MP3 and FLAC (not AAC/M4A).
        private static readonly FilePickerFileType AudioFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "com.microsoft.waveform-audio", "public.mp3", "org.xiph.flac" } },
            { DevicePlatform.MacCatalyst, new[] { "com.microsoft.waveform-audio", "public.mp3", "org.xiph.flac" } },
            { DevicePlatform.Android, new[] { "audio/wav", "audio/x-wav", "audio/mpeg", "audio/flac", "audio/x-flac" } },
            { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".flac" } },
        });

        // Sample inputs bundled with the app (Resources/Raw, plus LiteRT-LM's audio test sample).
        private static readonly string[] SampleImages = { "surfers.jpg", "tulips.jpg", "space_shuttle.jpg" };
        private const string SampleAudio = "audio_sample.wav";

        // Images are downscaled to this size before sending: the vision encoder resizes them anyway, and
        // smaller images keep the base64-encoded message small.
        private const int MaxImageSize = 1024;

        // Voice messages stop (and are sent) after this long: audio models take a limited length of audio.
        private static readonly TimeSpan MaxVoiceLength = TimeSpan.FromSeconds(30);

        // Material Symbols "mic" (Apache 2.0); the bundled icon font is a subset without it.
        private const string MicPathData = "M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z";

        private static readonly ModelOption[] Models = new[]
        {
            new ModelOption { Name = "Qwen3 0.6B", Detail = "~500 MB download. Fast, and can think before answering.", Choice = ModelChoice.Qwen3 },
            new ModelOption { Name = "Qwen3.5 0.8B", Detail = "~1.3 GB download. Newer and still fast; understands images.", Choice = ModelChoice.Qwen35_0_8B_VL, AcceptsImages = true, CanThink = false },
            new ModelOption { Name = "Qwen3.5 4B", Detail = "~2.8 GB download. Much more capable and can think before answering, but slow on the CPU.", Choice = ModelChoice.Qwen35_4B },
            new ModelOption { Name = "Gemma 4 E2B", Detail = "~2.6 GB download. Understands images and speech, and can think before answering.", Choice = ModelChoice.Gemma4E2B, AcceptsImages = true, AcceptsAudio = true },
            new ModelOption { Name = "Gemma 4 E4B", Detail = "~3.7 GB download. More capable than E2B, but slower and needs more memory.", Choice = ModelChoice.Gemma4E4B, AcceptsImages = true, AcceptsAudio = true },
        };

        private static readonly Color UserBubbleColor = Theme.Accent;
        private static readonly Color ModelBubbleColor = Theme.TileBackground;
        private static readonly Color UserTextColor = Colors.White;
        private static readonly Color ModelTextColor = Theme.PrimaryText;
        private static readonly Color ThinkingTextColor = Theme.SecondaryText;

        private readonly Picker _modelPicker;
        private readonly Label _modelDetailLabel;
        private readonly Switch _thinkingSwitch;
        private readonly Label _thinkingLabel;
        private readonly Grid _thinkingRow;
        private readonly Button _newChatButton;
        private readonly VerticalStackLayout _transcript;
        private readonly Label _emptyLabel;
        private readonly ScrollView _scroll;
        private readonly Label _statusLabel;
        private readonly Editor _promptEditor;
        private readonly Button _sendButton;
        private readonly ActivityIndicator _busyIndicator;
        private readonly Border _attachButton;
        private readonly HorizontalStackLayout _attachmentStrip;
        private readonly ScrollView _attachmentScroll;
        private readonly BottomSheet _sheet;
        private readonly List<PendingAttachment> _pending = new List<PendingAttachment>();
        private readonly Border _talkButton;
        private readonly Microsoft.Maui.Controls.Shapes.Path _micIcon;
        private readonly BoxView _stopIcon;
        private readonly Switch _readAloudSwitch;
        private readonly VoiceRecorder _recorder = new VoiceRecorder();
        private DateTime _recordingStarted;
        private CancellationTokenSource _recordingTimer;
        private CancellationTokenSource _speech;

        private LanguageModel _model;
        private Chat _chat;
        private ModelChoice? _loaded;
        private Task _generation;
        private bool _busy;

        public LiteRtLmChatPage()
        {
            Title = "LiteRT-LM Chat";
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            _modelPicker = new Picker { Title = "Model", FontFamily = Theme.TitleFont, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
            foreach (ModelOption option in Models)
                _modelPicker.Items.Add(option.Name);
            _modelPicker.SelectedIndex = 0;
            _modelPicker.SelectedIndexChanged += OnModelPickerChanged;

            _modelDetailLabel = new Label
            {
                Text = Models[0].Detail,
                FontFamily = Theme.BodyFont,
                FontSize = 13,
                TextColor = Theme.SecondaryText
            };

            _thinkingLabel = new Label
            {
                Text = "Let it think before answering",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            };
            _thinkingSwitch = new Switch { IsToggled = false, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _thinkingSwitch.Toggled += OnThinkingToggled;
            _thinkingRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            _thinkingRow.Add(_thinkingLabel, 0, 0);
            _thinkingRow.Add(_thinkingSwitch, 1, 0);

            _readAloudSwitch = new Switch { IsToggled = false, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _readAloudSwitch.Toggled += (s, e) =>
            {
                if (!e.Value)
                    StopSpeaking();
            };
            var readAloudRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            readAloudRow.Add(new Label
            {
                Text = "Read replies aloud",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            readAloudRow.Add(_readAloudSwitch, 1, 0);

            _newChatButton = Theme.SecondaryButton("New Chat", Theme.GlyphWand);
            _newChatButton.HeightRequest = 44;
            _newChatButton.FontSize = 15;
            _newChatButton.Clicked += OnNewChat;

            var optionsCard = Theme.Card(new VerticalStackLayout
            {
                Spacing = 6,
                Children = { _modelPicker, _modelDetailLabel, Theme.Divider(), _thinkingRow, readAloudRow, _newChatButton }
            }, 14);

            _emptyLabel = new Label
            {
                Text = "Ask anything - the model runs entirely on this device.",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = Theme.SecondaryText,
                Margin = new Thickness(24)
            };
            _transcript = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(4), Children = { _emptyLabel } };
            _scroll = new ScrollView { Content = _transcript };

            _statusLabel = new Label
            {
                IsVisible = false,
                Padding = new Thickness(4, 2),
                TextColor = Theme.Accent,
                FontFamily = Theme.BodyFont,
                FontSize = 14
            };

            _promptEditor = new Editor
            {
                Placeholder = "Type your message...",
                FontFamily = Theme.BodyFont,
#if WINDOWS
                // On Windows, the native TextBox's idle-state foreground brush doesn't reliably follow
                // Editor.TextColor (a known MAUI/WinUI quirk), so a dark TextColor still rendered white
                // here. Going dark end-to-end instead - a black background always shows the (effectively
                // fixed) white foreground clearly, rather than fighting it.
                BackgroundColor = Colors.Black,
                TextColor = Colors.White,
                PlaceholderColor = Colors.LightGray,
#else
                TextColor = Theme.PrimaryText,
                PlaceholderColor = Theme.SecondaryText,
#endif
                AutoSize = EditorAutoSizeOption.TextChanges,
                MinimumHeightRequest = 44,
                MaximumHeightRequest = 120
            };

            _busyIndicator = new ActivityIndicator { IsVisible = false, IsRunning = false, WidthRequest = 36, Color = Theme.Accent };
            _sendButton = new Button
            {
                Text = "Send",
                FontFamily = Theme.TitleFont,
                BackgroundColor = Theme.Accent,
                TextColor = Colors.White,
                CornerRadius = 14,
                HeightRequest = 44,
                WidthRequest = 90
            };
            _sendButton.Clicked += OnSendClicked;

            var sendArea = new Grid { VerticalOptions = LayoutOptions.End };
            sendArea.Children.Add(_sendButton);
            sendArea.Children.Add(_busyIndicator);

            _attachButton = new Border
            {
                WidthRequest = 44,
                HeightRequest = 44,
                BackgroundColor = Theme.TileBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
                VerticalOptions = LayoutOptions.End,
                IsVisible = Models[0].AcceptsImages || Models[0].AcceptsAudio,
                Content = new Label
                {
                    Text = "+",
                    FontFamily = Theme.TitleFont,
                    FontSize = 26,
                    TextColor = Theme.Accent,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            _attachButton.OnTap(OnAttachClicked);
            SemanticProperties.SetDescription(_attachButton, "Attach an image or audio");

            _micIcon = new Microsoft.Maui.Controls.Shapes.Path
            {
                Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(MicPathData),
                Fill = Theme.Accent,
                WidthRequest = 24,
                HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            _stopIcon = new BoxView
            {
                Color = Colors.White,
                CornerRadius = 3,
                WidthRequest = 16,
                HeightRequest = 16,
                IsVisible = false,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            _talkButton = new Border
            {
                WidthRequest = 44,
                HeightRequest = 44,
                BackgroundColor = Theme.TileBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
                VerticalOptions = LayoutOptions.End,
                IsVisible = Models[0].AcceptsAudio,
                Content = new Grid { Children = { _micIcon, _stopIcon } }
            };
            _talkButton.OnTap(OnTalkClicked);
            SemanticProperties.SetDescription(_talkButton, "Talk: record a voice message");

            var composerRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };
            composerRow.Add(_attachButton, 0, 0);
            composerRow.Add(_promptEditor, 1, 0);
            composerRow.Add(_talkButton, 2, 0);
            composerRow.Add(sendArea, 3, 0);

            _attachmentStrip = new HorizontalStackLayout { Spacing = 8 };
            _attachmentScroll = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                IsVisible = false,
                Content = _attachmentStrip
            };
            var composer = new VerticalStackLayout { Spacing = 8, Children = { _attachmentScroll, composerRow } };

            var root = new Grid
            {
                RowSpacing = 12,
                Padding = new Thickness(16, 12, 16, 16),
                MaximumWidthRequest = 760,
                HorizontalOptions = LayoutOptions.Fill,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            root.Add(Theme.PageHeader(this, Theme.GlyphText, "LiteRT-LM Chat", "On-device language model", true, false), 0, 0);
            root.Add(optionsCard, 0, 1);
            root.Add(_scroll, 0, 2);
            root.Add(_statusLabel, 0, 3);
            root.Add(Theme.Card(composer, 10), 0, 4);

            _sheet = new BottomSheet();
            Content = new Grid { Children = { root, _sheet } };
        }

        private ModelChoice SelectedChoice => SelectedOption.Choice;

        // ---------- Model selection ----------

        private ModelOption SelectedOption => Models[Math.Max(_modelPicker.SelectedIndex, 0)];

        private void OnModelPickerChanged(object sender, EventArgs e)
        {
            _modelDetailLabel.Text = SelectedOption.Detail;
            ModelOption option = SelectedOption;
            _attachButton.IsVisible = option.AcceptsImages || option.AcceptsAudio;
            _talkButton.IsVisible = option.AcceptsAudio;
            _thinkingRow.IsVisible = option.CanThink;
            // Drop attachments the new model can't take.
            foreach (PendingAttachment attachment in _pending.ToList())
                if (attachment.Image != null ? !option.AcceptsImages : !option.AcceptsAudio)
                    RemovePending(attachment);
        }

        private void OnThinkingToggled(object sender, ToggledEventArgs e)
        {
            if (_chat != null)
                _chat.EnableThinking = e.Value;
        }

        // ---------- Sending a message ----------

        private async void OnSendClicked(object sender, EventArgs e)
        {
            if (_busy || _recorder.IsRecording)
                return;

            string prompt = _promptEditor.Text?.Trim();
            List<PendingAttachment> attachments = _pending.ToList();
            if (string.IsNullOrEmpty(prompt))
            {
                if (attachments.Count == 0)
                    return;
                // Attachments without a question: ask for the obvious.
                prompt = attachments.Any(a => a.Image != null)
                    ? (attachments.All(a => a.Image != null) ? "Describe this image." : "Describe the image and transcribe the audio.")
                    : "Transcribe this audio.";
            }
            await SendAsync(prompt, attachments);
        }

        // Send a message (prompt may be empty for a voice message: the model answers what was said) and stream
        // the reply into the transcript.
        private async Task SendAsync(string prompt, List<PendingAttachment> attachments)
        {
            StopSpeaking();
            SetBusy(true);
            try
            {
                if (!await EnsureModelReadyAsync())
                    return;

                _promptEditor.Text = string.Empty;
                ClearPendingAttachments();
                AddUserMessage(prompt, attachments);
                SetStatus("Generating...");

                // Stream the reply into its own bubble as it's generated, rather than waiting for the
                // full answer. onChunk fires on a LiteRT-LM background thread with each incremental piece
                // of text (including any <think>...</think> block), so re-splitting the text accumulated
                // so far through ChatReply on every chunk gives the same thinking/answer split a finished
                // reply would show, just filled in live.
                StreamingBubble bubble = AddStreamingBubble();
                StringBuilder streamed = new StringBuilder();
                bool firstChunk = true;
                Action<string> onChunk = chunk =>
                {
                    streamed.Append(chunk);
                    ChatReply partial = new ChatReply(streamed.ToString());
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (firstChunk)
                        {
                            firstChunk = false;
                            SetStatus(null);
                        }
                        UpdateStreamingBubble(bubble, partial);
                    });
                };

                // Kept so teardown (OnNavigatedFrom / switching models) can wait for it instead of
                // freeing the chat/engine while LiteRT-LM is still generating into it.
                Task<ChatReply> generation = _chat.SendAsync(prompt, attachments.Select(a => a.Attachment), onChunk);
                _generation = generation;
                ChatReply reply = await generation;

                UpdateStreamingBubble(bubble, reply);
                SetStatus(null);
                if (_readAloudSwitch.IsToggled)
                    Speak(reply.Text);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<bool> EnsureModelReadyAsync()
        {
            ModelChoice choice = SelectedChoice;

            if (_model != null && _loaded != choice)
            {
                await CleanupModelAsync();
                ClearTranscript();
            }

            if (_model != null)
                return true;

            SetStatus("Preparing the model... the first run downloads it.");

            LanguageModel model;
            switch (choice)
            {
                case ModelChoice.Qwen3:
                    model = new Qwen3();
                    break;
                case ModelChoice.Qwen35_0_8B_VL:
                    model = new Qwen35_0_8B_VL();
                    break;
                case ModelChoice.Qwen35_4B:
                    model = new Qwen35_4B();
                    break;
                case ModelChoice.Gemma4E4B:
                    model = new Gemma4E4B();
                    break;
                default:
                    model = new Gemma4E2B();
                    break;
            }
            model.OnDownloadProgressChanged += OnDownloadProgressChanged;
            try
            {
                await model.Init();
            }
            catch (Exception ex)
            {
                model.Dispose();
                SetStatus("Could not load the model: " + ex.Message);
                return false;
            }

            _model = model;
            _chat = model.CreateChat();
            _chat.EnableThinking = _thinkingSwitch.IsToggled;
            _loaded = choice;
            return true;
        }

        private void OnDownloadProgressChanged(long? totalBytesToReceive, long bytesReceived, double? progressPercentage)
        {
            string message = totalBytesToReceive.HasValue
                ? string.Format("Downloading model... {0} of {1} MB ({2}%)", bytesReceived / (1024 * 1024), totalBytesToReceive.Value / (1024 * 1024), (int)(progressPercentage ?? 0))
                : string.Format("Downloading model... {0} MB", bytesReceived / (1024 * 1024));
            MainThread.BeginInvokeOnMainThread(() => SetStatus(message));
        }

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
            if (_busy)
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
            _modelPicker.IsEnabled = !recording && !_busy;
            _newChatButton.IsEnabled = !recording && !_busy;
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

        private async void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            StopSpeaking();
            CancellationTokenSource speech = new CancellationTokenSource();
            _speech = speech;
            try
            {
                await TextToSpeech.Default.SpeakAsync(text, cancelToken: speech.Token);
            }
            catch (Exception)
            {
                // Cancelled, or no speech engine
            }
        }

        private void StopSpeaking()
        {
            _speech?.Cancel();
            _speech = null;
        }

        // ---------- Attachments ----------

        private async void OnAttachClicked()
        {
            if (_busy)
                return;

            var rows = new List<(string Section, string Glyph, string Text, string Value)>();
            if (SelectedOption.AcceptsImages)
            {
                rows.Add(("IMAGE", Theme.GlyphImage, "Photo Library", "library"));
                foreach (string sample in SampleImages)
                    rows.Add((null, Theme.GlyphImage, "Sample: " + System.IO.Path.GetFileNameWithoutExtension(sample).Replace('_', ' '), "image:" + sample));
            }
            if (SelectedOption.AcceptsAudio)
            {
                rows.Add(("AUDIO (WAV, MP3, FLAC)", Theme.GlyphPlay, "Audio File", "audio"));
                rows.Add((null, Theme.GlyphPlay, "Sample: speech recording", "sampleaudio"));
            }

            string action = await _sheet.ShowAsync("Attach", rows);
            if (string.IsNullOrEmpty(action))
                return;

            try
            {
                if (action == "library")
                {
                    FileResult file = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                    if (file != null)
                        AddPendingImage(await ReadAllBytesAsync(file), file.FileName);
                }
                else if (action.StartsWith("image:"))
                {
                    string sample = action.Substring("image:".Length);
                    AddPendingImage(await ImageUtil.ReadAppFileAsync(sample), sample);
                }
                else if (action == "audio")
                {
                    FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an audio file", FileTypes = AudioFileTypes });
                    if (file != null)
                        AddPendingAudio(await ReadAllBytesAsync(file), file.FileName);
                }
                else if (action == "sampleaudio")
                {
                    AddPendingAudio(await ImageUtil.ReadAppFileAsync(SampleAudio), SampleAudio);
                }
            }
            catch (Exception ex)
            {
                SetStatus("Could not attach the file: " + ex.Message);
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(FileResult file)
        {
            using System.IO.Stream stream = await file.OpenReadAsync();
            using System.IO.MemoryStream ms = new System.IO.MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }

        // Decode the image here (any format the platform's photo picker returns that OpenCV reads), downscale it
        // and re-encode it as JPEG, which LiteRT-LM's image decoder (stb) reads.
        private void AddPendingImage(byte[] bytes, string name)
        {
            Emgu.CV.Mat image = ImageUtil.Decode(bytes);
            if (image == null)
            {
                SetStatus("That file could not be read as an image.");
                return;
            }
            byte[] jpeg;
            // Downscale disposes the original when it returns a smaller copy.
            using (Emgu.CV.Mat small = ImageUtil.Downscale(image, MaxImageSize))
            using (Emgu.CV.Util.VectorOfByte buffer = new Emgu.CV.Util.VectorOfByte())
            {
                Emgu.CV.CvInvoke.Imencode(".jpg", small, buffer);
                jpeg = buffer.ToArray();
            }
            AddPending(new PendingAttachment { Attachment = ChatAttachment.Image(jpeg), Image = jpeg, Name = name });
        }

        private void AddPendingAudio(byte[] bytes, string name)
        {
            AddPending(new PendingAttachment { Attachment = ChatAttachment.Audio(bytes), Name = name });
        }

        private void AddPending(PendingAttachment attachment)
        {
            _pending.Add(attachment);
            SetStatus(null);

            Border chip = AttachmentChip(attachment, false);
            var remove = new Label
            {
                Text = Theme.GlyphClose,
                FontFamily = Theme.IconFont,
                FontSize = 18,
                TextColor = Theme.SecondaryText,
                VerticalOptions = LayoutOptions.Center
            };
            ((HorizontalStackLayout)chip.Content).Children.Add(remove);
            remove.OnTap(() =>
            {
                if (!_busy)
                    RemovePending(attachment);
            });

            attachment.Chip = chip;
            _attachmentStrip.Children.Add(chip);
            _attachmentScroll.IsVisible = true;
        }

        private void RemovePending(PendingAttachment attachment)
        {
            _pending.Remove(attachment);
            _attachmentStrip.Children.Remove(attachment.Chip);
            _attachmentScroll.IsVisible = _pending.Count > 0;
        }

        private void ClearPendingAttachments()
        {
            _pending.Clear();
            _attachmentStrip.Children.Clear();
            _attachmentScroll.IsVisible = false;
        }

        // A thumbnail (image) or an icon and file name (audio); larger in the transcript than in the composer.
        private static Border AttachmentChip(PendingAttachment attachment, bool inTranscript)
        {
            var content = new HorizontalStackLayout { Spacing = 6 };
            if (attachment.Image != null)
            {
                byte[] image = attachment.Image;
                content.Children.Add(new Image
                {
                    Source = ImageSource.FromStream(() => new System.IO.MemoryStream(image)),
                    HeightRequest = inTranscript ? 140 : 48,
                    MaximumWidthRequest = inTranscript ? 240 : 96,
                    Aspect = Aspect.AspectFit
                });
            }
            else
            {
                content.Children.Add(Theme.MakeIcon(Theme.GlyphPlay, Theme.Accent, 20));
                content.Children.Add(new Label
                {
                    Text = attachment.Name,
                    FontFamily = Theme.BodyFont,
                    FontSize = 13,
                    TextColor = Theme.PrimaryText,
                    LineBreakMode = LineBreakMode.MiddleTruncation,
                    MaximumWidthRequest = 180,
                    VerticalOptions = LayoutOptions.Center
                });
            }
            return new Border
            {
                BackgroundColor = inTranscript ? Colors.White : Theme.TileBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = attachment.Image != null ? new Thickness(4) : new Thickness(8, 6),
                HorizontalOptions = LayoutOptions.End,
                Content = content
            };
        }

        // ---------- New chat / model switching ----------

        private async void OnNewChat(object sender, EventArgs e)
        {
            if (_busy)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            if (_chat != null)
            {
                _chat.ClearHistory();
                ClearTranscript();
            }
        }

        // Wait for any in-flight generation before freeing the chat/engine: Chat.Send and the engine it
        // runs on are native objects a background Task.Run is still reading, and disposing them out from
        // under it is a hard crash, not a catchable exception.
        private async Task CleanupModelAsync()
        {
            Task pending = _generation;
            if (pending != null && !pending.IsCompleted)
            {
                try { await pending; }
                catch { /* already reported via SetStatus where it was awaited */ }
            }

            Chat chat = _chat;
            LanguageModel model = _model;
            _chat = null;
            _model = null;
            _loaded = null;
            chat?.Dispose();
            model?.Dispose();
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            if (!Navigation.NavigationStack.Contains(this) && !Navigation.ModalStack.Contains(this))
            {
                StopSpeaking();
                _ = CancelRecordingAsync();
                _ = CleanupModelAsync();
            }
        }

        // ---------- Transcript ----------

        private void ClearTranscript()
        {
            _transcript.Children.Clear();
            _transcript.Children.Add(_emptyLabel);
        }

        // The views of a model bubble that is still streaming, so each chunk can update it in place
        // instead of adding a new bubble per chunk.
        private sealed class StreamingBubble
        {
            public Label ThinkingLabel;
            public BoxView Separator;
            public Label TextLabel;
        }

        private StreamingBubble AddStreamingBubble()
        {
            var thinkingLabel = new Label { FontFamily = Theme.BodyFont, TextColor = ThinkingTextColor, FontSize = 13, IsVisible = false };
            var separator = new BoxView { HeightRequest = 1, Color = Theme.Chevron, IsVisible = false };
            var textLabel = new Label { TextColor = ModelTextColor, FontFamily = Theme.BodyFont, FontSize = 15 };

            Border bubble = ModelBubble(new VerticalStackLayout
            {
                Spacing = 4,
                Children = { thinkingLabel, separator, textLabel }
            });
            AddBubbleRow(bubble, false);

            return new StreamingBubble { ThinkingLabel = thinkingLabel, Separator = separator, TextLabel = textLabel };
        }

        // Called on the UI thread with the thinking/answer split of everything streamed so far (or, once,
        // with the finished reply).
        private void UpdateStreamingBubble(StreamingBubble bubble, ChatReply partial)
        {
            bool hasThinking = !string.IsNullOrEmpty(partial.Thinking);
            bubble.ThinkingLabel.IsVisible = hasThinking;
            bubble.Separator.IsVisible = hasThinking;
            bubble.ThinkingLabel.Text = partial.Thinking;
            bubble.TextLabel.Text = partial.Text;
            ScrollToEnd();
        }

        private void AddUserMessage(string text, IReadOnlyList<PendingAttachment> attachments)
        {
            if (attachments.Count > 0)
            {
                // The attachments right-aligned above the message bubble.
                var media = new VerticalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.End };
                foreach (PendingAttachment attachment in attachments)
                    media.Children.Add(AttachmentChip(attachment, true));
                if (_transcript.Children.Contains(_emptyLabel))
                    _transcript.Children.Remove(_emptyLabel);
                _transcript.Children.Add(media);
            }
            if (!string.IsNullOrEmpty(text))
                AddBubbleRow(Bubble(text, UserBubbleColor, UserTextColor), true);
            else
                ScrollToEnd();
        }

        private void AddBubbleRow(Border bubble, bool isUser)
        {
            if (_transcript.Children.Contains(_emptyLabel))
                _transcript.Children.Remove(_emptyLabel);

            bubble.HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start;
            bubble.MaximumWidthRequest = 420;

            _transcript.Children.Add(bubble);
            ScrollToEnd();
        }

        private static Border Bubble(string text, Color background, Color textColor)
        {
            return new Border
            {
                BackgroundColor = background,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
                Padding = new Thickness(12, 9),
                Content = new Label { Text = text, TextColor = textColor, FontFamily = Theme.BodyFont, FontSize = 15 }
            };
        }

        private static Border ModelBubble(View content)
        {
            return new Border
            {
                BackgroundColor = ModelBubbleColor,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
                Padding = new Thickness(12, 9),
                Content = content
            };
        }

        private async void ScrollToEnd()
        {
            try
            {
                await Task.Yield();
                // Scroll to the actual bottom: Android doesn't clamp the offset, so scrolling to
                // double.MaxValue would scroll the whole transcript out of view.
                double bottom = Math.Max(0, _transcript.Height - _scroll.Height);
                await _scroll.ScrollToAsync(0, bottom, true);
            }
            catch (Exception)
            {
            }
        }

        // ---------- Busy / status ----------

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _sendButton.IsVisible = !busy;
            _busyIndicator.IsVisible = busy;
            _busyIndicator.IsRunning = busy;
            _promptEditor.IsEnabled = !busy;
            _modelPicker.IsEnabled = !busy;
            _newChatButton.IsEnabled = !busy;
            _attachButton.Opacity = busy ? 0.4 : 1;
            _talkButton.Opacity = busy ? 0.4 : 1;
        }

        private void SetStatus(string message)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _statusLabel.IsVisible = !string.IsNullOrEmpty(message);
                _statusLabel.Text = message ?? string.Empty;
            });
        }
    }
}

#endif
