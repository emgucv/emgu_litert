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
using Emgu.LiteRT.LM.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Chat demo backed by LiteRT-LM: downloads a .litertlm model and runs it entirely on this device
    /// through Emgu.LiteRT.LM.Models (LanguageModel, Chat). Wired up for Windows, iOS, Android and Mac
    /// Catalyst (Apple Silicon only). For models that accept images and audio (Gemma 4), the "+" button
    /// attaches a photo or an audio file to the next message, and the microphone button records a voice
    /// message: the model answers what was said (no transcription step). Replies can be read aloud. With "Allow web
    /// search" on (Gemma 4), messages go through a Microsoft Agent Framework agent over the model's IChatClient
    /// (Emgu.LiteRT.LM.Extensions.AI), which can search Wikipedia and - with a Tavily API key - the web (AgentTools).
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
            // Whether the model can call tools (LanguageModel.SupportsToolCalling), for web search.
            public bool CanUseTools;
        }

        // An image or audio file attached to the message being written.
        private sealed class PendingAttachment
        {
            public ChatAttachment Attachment;
            // The (re-encoded) image, for the thumbnails; null for audio.
            public byte[] Image;
            public string Name;
            // The encoded image or audio and its media type, for the agent (Microsoft.Extensions.AI DataContent).
            public byte[] Data;
            public string MediaType;
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
            new ModelOption { Name = "Gemma 4 E2B", Detail = "~2.6 GB download. Understands images and speech, and can think before answering.", Choice = ModelChoice.Gemma4E2B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true },
            new ModelOption { Name = "Gemma 4 E4B", Detail = "~3.7 GB download. More capable than E2B, but slower and needs more memory.", Choice = ModelChoice.Gemma4E4B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true },
        };

        private static readonly Color UserBubbleColor = Theme.Accent;
        private static readonly Color ModelBubbleColor = Theme.TileBackground;
        private static readonly Color UserTextColor = Colors.White;
        private static readonly Color ModelTextColor = Theme.PrimaryText;
        private static readonly Color ThinkingTextColor = Theme.SecondaryText;

        // Layout: a one-line header, the conversation, and the message box with per-message toggle chips. The model
        // list and the settings open in bottom sheets on narrow screens, and sit in a side panel on wide ones.
        private const double WideLayoutWidth = 820;
        private const double SidePanelWidth = 300;
        private int _selectedModel;
        private bool? _wide;
        private readonly Grid _pageGrid;
        private readonly Grid _mainColumn;
        private readonly Border _sidePanel;
        private readonly VerticalStackLayout _sideModelList;
        private readonly VerticalStackLayout _sideSettingsHost;
        private readonly VerticalStackLayout _settingsView;
        private readonly Border _modelChip;
        private readonly Label _modelChipLabel;
        private readonly Label _titleLabel;
        private readonly Border _settingsButton;
        private readonly Border _newChatButton;
        private readonly HorizontalStackLayout _toggleRow;
        private readonly ToggleChip _thinkingSwitch;
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

        // Web search: an agent (Microsoft Agent Framework) over the model's IChatClient, with the AgentTools.
        private const string TavilyKeyStorageName = "tavily_api_key";
        private const string AgentInstructions =
            "You are a helpful assistant running on the user's device. You can look things up with your tools: use them " +
            "for facts you are not sure about, recent events and anything that depends on today's date. Base your answer " +
            "on what the tools return, mention where it came from, and keep it short.";
        private readonly ToggleChip _webSearchSwitch;
        private readonly Entry _tavilyKeyEntry;
        private string _tavilyKey = string.Empty;
        private IChatClient _chatClient;
        private ChatClientAgent _agent;
        private string _agentToolsKey;
        private AgentSession _session;

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

            // ---- Header: back, the model (a chip that opens the model list), new chat, settings ----
            _modelChipLabel = new Label
            {
                FontFamily = Theme.TitleFont,
                FontSize = 16,
                TextColor = Theme.PrimaryText,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center
            };
            _modelChip = new Border
            {
                BackgroundColor = Theme.CardBackground,
                Stroke = Theme.RowBorder,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
                Padding = new Thickness(14, 0, 8, 0),
                HeightRequest = 40,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                Content = new HorizontalStackLayout
                {
                    Spacing = 2,
                    Children = { _modelChipLabel, Theme.MakeIcon(Theme.GlyphExpandMore, Theme.SecondaryText, 22) }
                }
            };
            _modelChip.OnTap(OnModelChipTapped);
            SemanticProperties.SetDescription(_modelChip, "Choose the model");
            _titleLabel = new Label
            {
                Text = "LiteRT-LM Chat",
                FontFamily = Theme.TitleFont,
                FontSize = 20,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center,
                IsVisible = false
            };
            _newChatButton = Theme.CircleButton(Theme.GlyphWand, () => OnNewChat(this, EventArgs.Empty));
            SemanticProperties.SetDescription(_newChatButton, "New chat");
            _settingsButton = Theme.CircleButton(Theme.GlyphSettings, OnSettingsTapped);
            SemanticProperties.SetDescription(_settingsButton, "Settings");
            var header = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            header.Add(Theme.CircleButton(Theme.GlyphChevronLeft, async () => await Navigation.PopAsync()), 0, 0);
            header.Add(new Grid { Children = { _modelChip, _titleLabel } }, 1, 0);
            header.Add(_newChatButton, 2, 0);
            header.Add(_settingsButton, 3, 0);

            // ---- Per-message toggles, shown above the message box for models that support them ----
            _thinkingSwitch = new ToggleChip("\u2728 Think");
            _thinkingSwitch.Toggled += OnThinkingToggled;
            _webSearchSwitch = new ToggleChip("\U0001F50E Web search");
            _webSearchSwitch.Toggled += OnWebSearchToggled;
            _toggleRow = new HorizontalStackLayout { Spacing = 8, Children = { _thinkingSwitch, _webSearchSwitch } };

            // ---- Settings that are set once (a bottom sheet, or the side panel on wide screens) ----
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
            _tavilyKeyEntry = new Entry
            {
                Placeholder = "Tavily API key (optional)",
                IsPassword = true,
                FontFamily = Theme.BodyFont,
                FontSize = 14,
                TextColor = Theme.PrimaryText,
                PlaceholderColor = Theme.SecondaryText
            };
            _tavilyKeyEntry.Completed += (s, e) => OnTavilyKeyChanged();
            _tavilyKeyEntry.Unfocused += (s, e) => OnTavilyKeyChanged();
            _ = LoadTavilyKeyAsync();
            _settingsView = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    readAloudRow,
                    Theme.Divider(),
                    new Label { Text = "Web search", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    new Label
                    {
                        Text = "With \U0001F50E Web search on, Gemma 4 can look things up on Wikipedia - and, with a Tavily API key (free at tavily.com), on the web. Your searches leave the device.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _tavilyKeyEntry
                }
            };

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

            var composerCard = Theme.Card(new VerticalStackLayout { Spacing = 8, Children = { _toggleRow, composer } }, 10);

            // ---- Main column ----
            _mainColumn = new Grid
            {
                RowSpacing = 10,
                Padding = new Thickness(16, 12, 16, 16),
                MaximumWidthRequest = 760,
                HorizontalOptions = LayoutOptions.Fill,
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            _mainColumn.Add(header, 0, 0);
            _mainColumn.Add(_scroll, 0, 1);
            _mainColumn.Add(_statusLabel, 0, 2);
            _mainColumn.Add(composerCard, 0, 3);

            // ---- Side panel (wide screens): the model list and the settings ----
            _sideModelList = new VerticalStackLayout { Spacing = 4 };
            _sideSettingsHost = new VerticalStackLayout();
            _sidePanel = new Border
            {
                BackgroundColor = Theme.CardBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0) },
                IsVisible = false,
                Content = new ScrollView
                {
                    Content = new VerticalStackLayout
                    {
                        Padding = new Thickness(16, 20),
                        Spacing = 10,
                        Children =
                        {
                            Theme.SectionTitle("Model"),
                            _sideModelList,
                            Theme.Divider(),
                            Theme.SectionTitle("Settings"),
                            _sideSettingsHost
                        }
                    }
                }
            };

            _pageGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(new GridLength(0)), new ColumnDefinition(GridLength.Star) }
            };
            _pageGrid.Add(_sidePanel, 0, 0);
            _pageGrid.Add(_mainColumn, 1, 0);

            _sheet = new BottomSheet();
            Content = new Grid { Children = { _pageGrid, _sheet } };

            SizeChanged += (s, e) => UpdateLayout();
            SelectModel(0);
        }

        // ---------- Layout ----------

        // Wide screens (desktop, the inner screen of a foldable, tablets) show the side panel; narrow ones the header
        // chip and settings button, which open bottom sheets.
        private void UpdateLayout()
        {
            if (Width <= 0)
                return;
            bool wide = Width >= WideLayoutWidth;
            if (_wide == wide)
                return;
            _wide = wide;
            _pageGrid.ColumnDefinitions[0].Width = wide ? new GridLength(SidePanelWidth) : new GridLength(0);
            _sidePanel.IsVisible = wide;
            _modelChip.IsVisible = !wide;
            _titleLabel.IsVisible = wide;
            _settingsButton.IsVisible = !wide;
            if (wide)
            {
                MoveTo(_settingsView, _sideSettingsHost);
                RefreshModelList();
            }
        }

        private static void MoveTo(View view, Layout newParent)
        {
            if (view.Parent is Layout oldParent)
                oldParent.Children.Remove(view);
            newParent.Children.Add(view);
        }

        // The model list: name, description and a check mark on the current model. Tapping a row selects it.
        private View BuildModelList(bool inSheet)
        {
            var list = new VerticalStackLayout { Spacing = 4 };
            for (int i = 0; i < Models.Length; i++)
            {
                int index = i;
                ModelOption option = Models[i];
                bool selected = index == _selectedModel;
                var row = new Grid
                {
                    Padding = new Thickness(10, 8),
                    ColumnSpacing = 8,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
                };
                row.Add(new VerticalStackLayout
                {
                    Children =
                    {
                        new Label { Text = option.Name, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                        new Label { Text = option.Detail, FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText }
                    }
                }, 0, 0);
                if (selected)
                    row.Add(Theme.MakeIcon(Theme.GlyphCheck, Theme.Accent, 22), 1, 0);
                var cell = new Border
                {
                    BackgroundColor = selected ? Theme.TileBackground : Colors.Transparent,
                    Stroke = Colors.Transparent,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                    Content = row
                };
                cell.OnTap(() =>
                {
                    if (inSheet)
                        _sheet.Dismiss(index.ToString());
                    else if (CanChangeModel)
                        SelectModel(index);
                });
                list.Children.Add(cell);
            }
            return list;
        }

        private void RefreshModelList()
        {
            if (_wide != true)
                return;
            _sideModelList.Children.Clear();
            _sideModelList.Children.Add(BuildModelList(false));
        }

        private bool CanChangeModel => !_busy && !_recorder.IsRecording;

        private async void OnModelChipTapped()
        {
            if (!CanChangeModel)
                return;
            string choice = await _sheet.ShowViewAsync("Model", BuildModelList(true), "Cancel");
            if (int.TryParse(choice, out int index))
                SelectModel(index);
        }

        private async void OnSettingsTapped()
        {
            // The settings view may still be in the side panel (or a previous sheet).
            if (_settingsView.Parent is Layout parent)
                parent.Children.Remove(_settingsView);
            await _sheet.ShowViewAsync("Settings", _settingsView);
        }

        private ModelChoice SelectedChoice => SelectedOption.Choice;

        // ---------- Model selection ----------

        private ModelOption SelectedOption => Models[_selectedModel];

        // Select a model (loaded on the next message) and show the controls it supports.
        private void SelectModel(int index)
        {
            _selectedModel = index;
            ModelOption option = SelectedOption;
            _modelChipLabel.Text = option.Name;
            RefreshModelList();
            _attachButton.IsVisible = option.AcceptsImages || option.AcceptsAudio;
            _talkButton.IsVisible = option.AcceptsAudio;
            _thinkingSwitch.IsVisible = option.CanThink;
            _webSearchSwitch.IsVisible = option.CanUseTools;
            _toggleRow.IsVisible = option.CanThink || option.CanUseTools;
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
                Task<ChatReply> generation = UseAgent
                    ? RunAgentAsync(prompt, attachments, bubble)
                    : _chat.SendAsync(prompt, attachments.Select(a => a.Attachment), onChunk);
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

        // ---------- Web search (agent) ----------

        // Models that can call tools always chat through the agent; the Web search chip decides per message whether it
        // may use them (ChatToolMode.Auto or None), so switching it keeps the conversation.
        private bool UseAgent => SelectedOption.CanUseTools;

        // Send a message through the agent, streaming its reply into the bubble and showing each tool call as a step
        // line above it. Runs on the UI thread (the await foreach resumes on it).
        private async Task<ChatReply> RunAgentAsync(string prompt, List<PendingAttachment> attachments, StreamingBubble bubble)
        {
            await EnsureAgentAsync();

            List<AIContent> contents = new List<AIContent>();
            foreach (PendingAttachment attachment in attachments)
                contents.Add(new DataContent(attachment.Data, attachment.MediaType));
            contents.Add(new TextContent(prompt));
            ChatOptions options = new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = _thinkingSwitch.IsToggled ? ReasoningEffort.Medium : ReasoningEffort.None },
                ToolMode = _webSearchSwitch.IsToggled ? ChatToolMode.Auto : ChatToolMode.None
            };

            StringBuilder thinking = new StringBuilder();
            StringBuilder answer = new StringBuilder();
            Dictionary<string, Label> steps = new Dictionary<string, Label>();
            await foreach (AgentResponseUpdate update in _agent.RunStreamingAsync(
                new AIChatMessage(ChatRole.User, contents), _session, new ChatClientAgentRunOptions(options)))
            {
                foreach (AIContent content in update.Contents)
                {
                    switch (content)
                    {
                        case TextReasoningContent reasoning:
                            thinking.Append(reasoning.Text);
                            break;
                        case TextContent text:
                            answer.Append(text.Text);
                            break;
                        case FunctionCallContent call:
                            string step = AgentTools.Describe(call);
                            steps[call.CallId] = AddStepLine(bubble, step + "...");
                            SetStatus(step + "...");
                            break;
                        case FunctionResultContent result:
                            if (steps.TryGetValue(result.CallId, out Label line))
                                line.Text = line.Text.TrimEnd('.') + (result.Exception != null ? " - failed: " + result.Exception.Message : "");
                            SetStatus("Generating...");
                            break;
                    }
                }
                if (thinking.Length > 0 || answer.Length > 0)
                {
                    if (!steps.Values.Any(l => l.Text.EndsWith("...")))
                        SetStatus(null);
                    UpdateStreamingBubble(bubble, ToReply(thinking, answer));
                }
            }
            return ToReply(thinking, answer);
        }

        private static ChatReply ToReply(StringBuilder thinking, StringBuilder answer)
        {
            return new ChatReply(thinking.Length > 0 ? "<think>" + thinking + "</think>" + answer : answer.ToString());
        }

        // The agent, its tools and its session (the conversation) are created on first use; a new Tavily key means
        // new tools, and so a new agent.
        private async Task EnsureAgentAsync()
        {
            if (_chatClient == null)
                _chatClient = _model.AsIChatClient();
            if (_agent == null || _agentToolsKey != _tavilyKey)
            {
                _agent = new ChatClientAgent(_chatClient, AgentInstructions, "Assistant", null, AgentTools.Create(_tavilyKey));
                _agentToolsKey = _tavilyKey;
                _session = null;
            }
            if (_session == null)
                _session = await _agent.CreateSessionAsync();
        }

        private void ResetAgent()
        {
            _agent = null;
            _session = null;
            _chatClient?.Dispose();
            _chatClient = null;
        }

        // A grey line above the reply bubble, describing a tool call.
        private Label AddStepLine(StreamingBubble bubble, string text)
        {
            Label line = new Label
            {
                Text = "\U0001F50E " + text,
                FontFamily = Theme.BodyFont,
                FontSize = 13,
                TextColor = Theme.SecondaryText,
                Margin = new Thickness(6, 0)
            };
            int index = _transcript.Children.IndexOf(bubble.Row);
            if (index < 0)
                _transcript.Children.Add(line);
            else
                _transcript.Children.Insert(index, line);
            ScrollToEnd();
            return line;
        }

        private void OnWebSearchToggled(object sender, ToggledEventArgs e)
        {
            // Applies from the next message; the conversation continues.
            if (e.Value && !_busy)
                SetStatus(string.IsNullOrEmpty(_tavilyKey)
                    ? "Web search on: Wikipedia. Add a Tavily key in Settings to search the web."
                    : "Web search on: Wikipedia and the web.");
            else if (!_busy)
                SetStatus(null);
        }

        private async Task LoadTavilyKeyAsync()
        {
            try
            {
                _tavilyKey = await SecureStorage.Default.GetAsync(TavilyKeyStorageName) ?? string.Empty;
                _tavilyKeyEntry.Text = _tavilyKey;
            }
            catch (Exception)
            {
                // No secure storage (e.g. no keychain access): the key is kept for this session only.
            }
        }

        private async void OnTavilyKeyChanged()
        {
            string key = _tavilyKeyEntry.Text?.Trim() ?? string.Empty;
            if (key == _tavilyKey)
                return;
            _tavilyKey = key;
            try
            {
                if (key.Length > 0)
                    await SecureStorage.Default.SetAsync(TavilyKeyStorageName, key);
                else
                    SecureStorage.Default.Remove(TavilyKeyStorageName);
            }
            catch (Exception)
            {
                // Kept for this session only.
            }
            // New tools mean a new agent conversation.
            if (_session != null && !_busy)
            {
                _session = null;
                ClearTranscript();
                SetStatus(key.Length > 0 ? "Web search with Tavily is on - started a new chat." : "Tavily key removed - started a new chat.");
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
            AddPending(new PendingAttachment { Attachment = ChatAttachment.Image(jpeg), Image = jpeg, Data = jpeg, MediaType = "image/jpeg", Name = name });
        }

        private void AddPendingAudio(byte[] bytes, string name)
        {
            AddPending(new PendingAttachment { Attachment = ChatAttachment.Audio(bytes), Data = bytes, MediaType = AudioMediaType(name), Name = name });
        }

        private static string AudioMediaType(string fileName)
        {
            switch (System.IO.Path.GetExtension(fileName)?.ToLowerInvariant())
            {
                case ".mp3": return "audio/mpeg";
                case ".flac": return "audio/flac";
                default: return "audio/wav";
            }
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
                _session = null;
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

            // The chat client's conversation must be released before the model's engine.
            ResetAgent();
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
            // The bubble in the transcript, so tool steps can be inserted above it.
            public View Row;
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

            return new StreamingBubble { ThinkingLabel = thinkingLabel, Separator = separator, TextLabel = textLabel, Row = bubble };
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
            _modelChip.Opacity = CanChangeModel ? 1 : 0.5;
            _newChatButton.Opacity = CanChangeModel ? 1 : 0.5;
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
