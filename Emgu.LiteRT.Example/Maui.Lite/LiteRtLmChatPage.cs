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
using Emgu.LiteRT.Util;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents;
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
    public partial class LiteRtLmChatPage : ContentPage
    {
        private enum ModelChoice
        {
            Qwen3,
            Qwen35_0_8B_VL,
            Qwen35_4B,
            Gemma4E2B,
            Gemma4E4B,
            Gemma4_12B
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
            // Whether the model writes useful summaries: when a chat outgrows its context window, earlier messages
            // are summarized; otherwise (the small models) they are dropped.
            public bool CanSummarize;
            // The context window on desktops (Mac Catalyst, Windows), or 0 for LanguageModel's default (4096) - used on
            // phones too. Gemma 4's files allow up to ~32k, but the model re-reads the whole conversation when a saved
            // chat is opened or after compacting, which grows with the window: a full 4k chat took 2 minutes on a Pixel
            // Fold (8k: almost 6, with 1.5 GB of its RAM left), 8k 27 s (E2B) / 61 s (E4B) on an Apple Silicon Mac.
            public int DesktopContextTokens;
            // The largest context window the model's file allows, which Settings > Context window can choose up to; 0 for
            // a model with a fixed window (the Qwen files: 4096).
            public int MaxContextTokens;
            // Whether the model runs on the GPU (when "Use the GPU" is on): on Android with LiteRT's OpenCL accelerator
            // (if the device has OpenCL; only Gemma 4 E2B has been tested there), on Mac Catalyst with its Metal one.
            // Gemma4 uses 32-bit activations on the GPU - see Performance.md.
            public bool GpuOnAndroid;
            public bool GpuOnMac;
            public bool GpuOnWindows;
            // The model file (to tell whether it is downloaded) and its download size, for the download prompt.
            public DownloadableFile File;
            public string Size;
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
            new ModelOption { Name = "Qwen3 0.6B", File = Qwen3.ModelFile, Size = "350 MB", Detail = "~350 MB download. Fast, and can think before answering.", Choice = ModelChoice.Qwen3, GpuOnWindows = true },
            new ModelOption { Name = "Qwen3.5 0.8B", File = Qwen35_0_8B_VL.ModelFile, Size = "1.3 GB", Detail = "~1.3 GB download. Newer and still fast; understands images.", Choice = ModelChoice.Qwen35_0_8B_VL, AcceptsImages = true, CanThink = false, GpuOnWindows = true },
            new ModelOption { Name = "Qwen3.5 4B", File = Qwen35_4B.ModelFile, Size = "2.8 GB", Detail = "~2.8 GB download. Much more capable and can think before answering, but slow on the CPU.", Choice = ModelChoice.Qwen35_4B, CanSummarize = true, GpuOnWindows = true },
            new ModelOption { Name = "Gemma 4 E2B", File = Gemma4E2B.ModelFile, Size = "2.6 GB", Detail = "~2.6 GB download. Understands images and speech, and can think before answering.", Choice = ModelChoice.Gemma4E2B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true, CanSummarize = true, DesktopContextTokens = 8192, MaxContextTokens = 32000, GpuOnAndroid = true, GpuOnMac = true, GpuOnWindows = true },
            new ModelOption { Name = "Gemma 4 E4B", File = Gemma4E4B.ModelFile, Size = "3.7 GB", Detail = "~3.7 GB download. More capable than E2B, but slower and needs more memory.", Choice = ModelChoice.Gemma4E4B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true, CanSummarize = true, DesktopContextTokens = 8192, MaxContextTokens = 32000, GpuOnMac = true, GpuOnWindows = true },
#if MACCATALYST || WINDOWS
            // Large desktop model; GPU entries are enabled only for verified platform runtimes.
            new ModelOption { Name = "Gemma 4 12B", File = Gemma4_12B.ModelFile, Size = "6.9 GB",
#if WINDOWS
                Detail = "~6.9 GB download. Large desktop model for code and harder questions. CPU only on Windows: GPU image inference failed compatibility testing. Needs substantial system memory.",
#else
                Detail = "~6.9 GB download. The most capable - best for code and harder questions. Needs a Mac with 16 GB of memory.",
#endif
                Choice = ModelChoice.Gemma4_12B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true, CanSummarize = true, DesktopContextTokens = 8192, MaxContextTokens = 32000, GpuOnMac = true },
#endif
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

        // Web search: an agent (Microsoft Agent Framework) over the model's IChatClient, with the tools of ChatAgentTools.
        private const string TavilyKeyStorageName = "tavily_api_key";
        private const string AgentInstructions =
            "You are a helpful assistant running on the user's device. You can look things up with your tools: use them " +
            "for facts you are not sure about, recent events and anything that depends on today's date or, when you have the " +
            "location tool, on where the user is. Use the calculate tool for any arithmetic, and the convert tools for units and money. When the user gives a link, read it with read_web_page. When the user tells you " +
            "something lasting about themselves, save it with the remember tool. Base your answer on what the tools " +
            "return, mention where it came from, and keep it short.";
        private readonly ToggleChip _webSearchSwitch;
        private readonly Entry _tavilyKeyEntry;
        // Whether the agent gets the location tool (Settings, off by default): the user's town and approximate
        // coordinates, which leave the device in web searches that use them.
        private const string ShareLocationPreference = "litert_lm_chat_share_location";
        private readonly Switch _shareLocationSwitch;
        // Memories about the user (ChatStore): with "Remember things about me" on (the default; they stay on the device),
        // the agent can save and delete them, and the latest are given to it with every message.
        private const string MemoryPreference = "litert_lm_chat_memory";
        private const int MaxMemoryLength = 1200;
        private readonly Switch _memorySwitch;
        private readonly Label _memoryCountLabel;
        private string _tavilyKey = string.Empty;
        private IChatClient _chatClient;
        private ChatClientAgent _agent;
        private AgentSession _session;

        private LanguageModel _model;
        private Chat _chat;
        private ModelChoice? _loaded;
        // GPU (Android): whether the loaded model runs on it, whether the one loading will, and whether it failed to
        // load there (then the CPU is used for the rest of the session).
        private const string UseGpuPreference = "litert_lm_chat_use_gpu";
        private bool _selectedGpu;
        private static string BackendPreference => UseGpuPreference + "_" + DeviceInfo.Platform;
        private bool _loadedOnGpu;
        private bool _loadingOnGpu;
        // Context window (Settings, Gemma 4): Automatic (0: 8k on desktops, 4k on phones) or a size; the loaded model's
        // and the loading one's, to reload the model when it changes.
        private const string ContextWindowPreference = "litert_lm_chat_context_tokens";
        private static readonly int[] ContextWindowChoices = { 0, 4096, 8192, 16384, 32000 };
        private int _contextSetting;
        private int _loadedContextTokens;
        private int _loadingContextTokens;
        private readonly List<ToggleChip> _contextChips = new List<ToggleChip>();
        private readonly VerticalStackLayout _contextSection;
        private readonly Label _contextNote;
        private bool _updatingContextChips;
        private bool _gpuUnavailable;

        // Saved chats (ChatStore, SQLite): the current chat's id once its first exchange is saved, and its messages,
        // used to give the model the earlier conversation when a saved chat is opened (or the agent's tools change).
        private const string HistoryIconPath = "M13 3c-4.97 0-9 4.03-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42C8.27 19.99 10.51 21 13 21c4.97 0 9-4.03 9-9s-4.03-9-9-9zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z";
        private readonly ChatStore _store = new ChatStore();
        private int? _conversationId;
        private readonly List<HistoryItem> _historyItems = new List<HistoryItem>();
        private List<AIChatMessage> _agentPrimeHistory;
        private readonly List<string> _sendSteps = new List<string>();
        private readonly Border _historyButton;
        private readonly VerticalStackLayout _sideChatList;

        // A message of the current chat, as given to the model.
        private sealed class HistoryItem
        {
            public string Role;
            public string Text;
            // Given to the model before the text of a user message: excerpts of the attached document (or null).
            public string Context;
            public string ModelText => string.IsNullOrEmpty(Context) ? Text : Context + Text;
            public List<(string Kind, string MediaType, byte[] Data)> Attachments = new List<(string, string, byte[])>();
            // Its id in the ChatStore (0 if not saved).
            public int MessageId;
            // Its size in the model's context (estimated, see EstimateTokens), or -1 until counted; ExtraTokens is
            // what the model's own conversation holds on top until it is rebuilt: the reply's thinking, tool calls
            // and their results.
            public int Tokens = -1;
            public int ExtraTokens;
            // A user message's first view in the transcript, to put a compaction divider above it.
            public View View;
        }

        // Context window: a chat that outgrows it fails ("Input token ids are too long"), so before a message is sent
        // that would make it too long, the earlier messages are summarized by the model (CanSummarize) or dropped, and
        // the model continues from the summary and the latest messages. The transcript keeps everything. Sizes are
        // estimates: text is counted with the model's tokenizer, plus measured costs of images and audio.
        private const int ReplyReserveTokens = 1024;      // room left for the reply
        private const int ChatOverheadTokens = 50;        // chat template
        private const int AgentOverheadTokens = 900;      // the agent's instructions, tool definitions and memories
        private const int MessageOverheadTokens = 8;      // per message (role markers)
        private const int ImageTokens = 270;              // measured: Gemma 4 and Qwen3.5 VL, 1024x1024 image
        private const int AudioTokensPerSecond = 25;      // measured: Gemma 4
        private const int ToolStepTokens = 450;           // a tool call and its (truncated) result
        private const int SummaryMaxTokens = 400;
        private const string SummaryIntro = "Here is a summary of our conversation so far:\n";
        private const string SummaryAcknowledgement = "Thanks, I have the summary and will continue from there.";
        // The first message of _historyItems the model sees; the ones before it are covered by _summary (if any).
        private int _contextStart;
        private string _summary;
        private int _summaryTokens;
        // The model has to be given the conversation again (after compacting, or when a saved chat is opened).
        private bool _needsPrime;
        private readonly Label _memoryLabel;
        private readonly Label _compactButton;

        // Loading: models load in the background as soon as they are selected (when already downloaded), shown in a
        // card at the top of the conversation; a message sent meanwhile waits for it.
        private const string LastModelPreference = "litert_lm_chat_model";
        private const string ThinkPreference = "litert_lm_chat_think";
        private const string WebSearchPreference = "litert_lm_chat_web_search";
        private Task<bool> _loadTask;
        private ModelChoice? _loadingChoice;
        private CancellationTokenSource _loadCancel;
        private bool _pageStarted;
        private bool _pageVisible;
        private readonly Border _loadCard;
        private readonly Label _loadTitle;
        private readonly Label _loadStage;
        private readonly ProgressBar _loadProgress;
        private readonly ActivityIndicator _loadSpinner;
        private readonly Button _loadPrimaryButton;
        private readonly Button _loadCancelButton;
        private readonly ActivityIndicator _chipSpinner;
        private Action _loadPrimaryAction;
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
            // Spins in the model chip while the model loads.
            _chipSpinner = new ActivityIndicator
            {
                IsRunning = false,
                IsVisible = false,
                Color = Theme.Accent,
                WidthRequest = 16,
                HeightRequest = 16,
                Margin = new Thickness(4, 0, 0, 0),
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
                    Children = { _modelChipLabel, _chipSpinner, Theme.MakeIcon(Theme.GlyphExpandMore, Theme.SecondaryText, 22) }
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
            _historyButton = new Border
            {
                WidthRequest = 44,
                HeightRequest = 44,
                BackgroundColor = Theme.CardBackground,
                Stroke = Theme.RowBorder,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
                VerticalOptions = LayoutOptions.Center,
                Content = new Microsoft.Maui.Controls.Shapes.Path
                {
                    Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(HistoryIconPath),
                    Fill = Theme.PrimaryText,
                    WidthRequest = 22,
                    HeightRequest = 22,
                    Aspect = Stretch.Uniform,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            _historyButton.OnTap(OnHistoryTapped);
            SemanticProperties.SetDescription(_historyButton, "Saved chats");
            _exportChatButton = BuildExportChatButton();
            var header = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            header.Add(Theme.CircleButton(Theme.GlyphChevronLeft, async () => await Navigation.PopAsync()), 0, 0);
            header.Add(new Grid { Children = { _modelChip, _titleLabel } }, 1, 0);
            header.Add(_historyButton, 2, 0);
            header.Add(_newChatButton, 3, 0);
            header.Add(_exportChatButton, 4, 0);
            header.Add(_settingsButton, 5, 0);

            // ---- Per-message toggles, shown above the message box for models that support them ----
            _thinkingSwitch = new ToggleChip("\u2728 Think");
            // On by default (thinking first gives better answers, at the cost of time and context), then as last set.
            bool think = true;
            try { think = Preferences.Default.Get(ThinkPreference, true); } catch (Exception) { }
            _thinkingSwitch.IsToggled = think;
            _thinkingSwitch.Toggled += OnThinkingToggled;
            _webSearchSwitch = new ToggleChip("\U0001F50E Web search");
            bool webSearch = false;
            try { webSearch = Preferences.Default.Get(WebSearchPreference, false); } catch (Exception) { }
            _webSearchSwitch.IsToggled = webSearch;
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
            bool shareLocation = false;
            try { shareLocation = Preferences.Default.Get(ShareLocationPreference, false); } catch (Exception) { }
            _shareLocationSwitch = new Switch { IsToggled = shareLocation, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _shareLocationSwitch.Toggled += (s, e) =>
            {
                try { Preferences.Default.Set(ShareLocationPreference, e.Value); } catch (Exception) { }
            };
            var locationRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            locationRow.Add(new Label
            {
                Text = "Share my location",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            locationRow.Add(_shareLocationSwitch, 1, 0);
            bool memoryOn = true;
            try { memoryOn = Preferences.Default.Get(MemoryPreference, true); } catch (Exception) { }
            _memorySwitch = new Switch { IsToggled = memoryOn, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _memorySwitch.Toggled += (s, e) =>
            {
                try { Preferences.Default.Set(MemoryPreference, e.Value); } catch (Exception) { }
            };
            var memoryRow = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            memoryRow.Add(new Label
            {
                Text = "Remember things about me",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            memoryRow.Add(_memorySwitch, 1, 0);
            _memoryCountLabel = new Label
            {
                FontFamily = Theme.TitleFont,
                FontSize = 14,
                TextColor = Theme.Accent,
                Padding = new Thickness(0, 4),
                HorizontalOptions = LayoutOptions.Start
            };
            _memoryCountLabel.OnTap(OnMemoriesTapped);
            _ = RefreshMemoryCountAsync();
            _tavilyKeyEntry.Completed += (s, e) => OnTavilyKeyChanged();
            _tavilyKeyEntry.Unfocused += (s, e) => OnTavilyKeyChanged();
            _ = LoadTavilyKeyAsync();
            // How full the model's context window is, and compacting it by hand.
            _memoryLabel = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
            _compactButton = new Label
            {
                Text = "Compact now",
                FontFamily = Theme.TitleFont,
                FontSize = 14,
                TextColor = Theme.Accent,
                Padding = new Thickness(0, 4),
                HorizontalOptions = LayoutOptions.Start
            };
            SemanticProperties.SetDescription(_compactButton, "Summarize the earlier messages now");
            _compactButton.OnTap(OnCompactTapped);
            UpdateMemoryLabel();
            try { _contextSetting = Preferences.Default.Get(ContextWindowPreference, 0); } catch (Exception) { }
            if (Array.IndexOf(ContextWindowChoices, _contextSetting) < 0)
                _contextSetting = 0;
            var contextChips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (int choice in ContextWindowChoices)
            {
                int value = choice;
                ToggleChip chip = new ToggleChip(ContextWindowName(value)) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == _contextSetting };
                chip.Toggled += (s, e) => OnContextWindowChipToggled(value, e.Value);
                _contextChips.Add(chip);
                contextChips.Children.Add(chip);
            }
            _contextNote = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
            _contextSection = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = "Context window", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    contextChips,
                    _contextNote
                }
            };
            _settingsView = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    CpuThreadSettings.CreateView(ReloadCpuModelsAsync),
                    Theme.Divider(),
                    readAloudRow,
                    Theme.Divider(),
                    new Label { Text = "Web search", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    new Label
                    {
                        Text = "With \U0001F50E Web search on, Gemma 4 can look things up on Wikipedia, read links you give it, check the weather and convert currencies - and, with a Tavily API key (free at tavily.com), search the web. Your searches leave the device.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _tavilyKeyEntry,
                    Theme.Divider(),
                    locationRow,
                    new Label
                    {
                        Text = "Lets Gemma 4 look up your town and approximate position, for questions such as \"what's the weather here?\". Your device asks for permission the first time. The place name comes from Apple's or Google's map service, and web searches that use it leave the device.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    Theme.Divider(),
                    BuildEmbeddingModelSection(),
                    Theme.Divider(),
                    BuildPhotoSection(),
                    Theme.Divider(),
                    BuildChatSearchSection(),
                    Theme.Divider(),
                    BuildImageSection(),
                    Theme.Divider(),
                    BuildStorageSection(),
                    Theme.Divider(),
                    new Label { Text = "Memories about you", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    memoryRow,
                    new Label
                    {
                        Text = "Gemma 4 saves lasting facts you tell it - your name, preferences, plans - and knows them in later chats. They stay on this device; ask it to forget something, or delete it here.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _memoryCountLabel,
                    Theme.Divider(),
                    new Label { Text = "Conversation memory", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    _memoryLabel,
                    _compactButton,
                    _contextSection
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

#if WINDOWS
            ConfigurePromptKeyboard();
#endif
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
            SemanticProperties.SetDescription(_attachButton, "Attach a document, an image or audio");

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
            _documentBar = new HorizontalStackLayout { Spacing = 8, IsVisible = false };
            var composer = new VerticalStackLayout { Spacing = 8, Children = { _documentBar, _attachmentScroll, composerRow } };

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
            _loadTitle = new Label { FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText };
            _loadStage = new Label { FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText };
            _loadProgress = new ProgressBar { ProgressColor = Theme.Accent, IsVisible = false };
            _loadSpinner = new ActivityIndicator { Color = Theme.Accent, WidthRequest = 22, HeightRequest = 22, VerticalOptions = LayoutOptions.Center };
            _loadPrimaryButton = Theme.PrimaryButton("Download", null);
            _loadPrimaryButton.ImageSource = null;
            _loadPrimaryButton.HeightRequest = 40;
            _loadPrimaryButton.FontSize = 14;
            _loadPrimaryButton.Clicked += (s, e) => _loadPrimaryAction?.Invoke();
            _loadCancelButton = Theme.SecondaryButton("Cancel", null);
            _loadCancelButton.ImageSource = null;
            _loadCancelButton.HeightRequest = 40;
            _loadCancelButton.FontSize = 14;
            _loadCancelButton.Clicked += (s, e) => CancelLoad();
            var loadTitleRow = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            loadTitleRow.Add(_loadSpinner, 0, 0);
            loadTitleRow.Add(_loadTitle, 1, 0);
            var loadButtons = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            loadButtons.Add(_loadPrimaryButton, 0, 0);
            loadButtons.Add(_loadCancelButton, 1, 0);
            _loadCard = Theme.Card(new VerticalStackLayout
            {
                Spacing = 8,
                Children = { loadTitleRow, _loadStage, _loadProgress, loadButtons }
            }, 14);
            _loadCard.IsVisible = false;
            _loadCard.VerticalOptions = LayoutOptions.Start;
            _loadCard.Margin = new Thickness(0, 4, 0, 0);
            _mainColumn.Add(new Grid { Children = { _scroll, _loadCard } }, 0, 1);
            _mainColumn.Add(_statusLabel, 0, 2);
            _mainColumn.Add(composerCard, 0, 3);

            // ---- Side panel (wide screens): the model list and the settings ----
            _sideChatList = new VerticalStackLayout { Spacing = 2 };
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
                            Theme.SectionTitle("Chats"),
                            _sideChatList,
                            Theme.Divider(),
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
            _historyButton.IsVisible = !wide;
            if (wide)
            {
                MoveTo(_settingsView, _sideSettingsHost);
                RefreshModelList();
                _ = RefreshChatListAsync();
            }
        }

        private static void MoveTo(View view, Layout newParent)
        {
            if (view.Parent is Layout oldParent)
                oldParent.Children.Remove(view);
            newParent.Children.Add(view);
        }

        private bool? _cpuModelsExpanded;
        private bool? _gpuModelsExpanded;

        // The model list: collapsible backend groups, with a check mark on the current model.
        private View BuildModelList(bool inSheet)
        {
            _modelDownloadRows.RemoveAll(row => row.InSheet == inSheet);
            var list = new VerticalStackLayout { Spacing = 4 };
            foreach (bool gpu in new[] { true, false })
            {
                if (gpu && !Models.Any(HasGpu)) continue;
                string title = gpu ? "GPU Models" : "CPU Models";
                var models = new VerticalStackLayout
                {
                    Spacing = 4,
                    IsVisible = (gpu ? _gpuModelsExpanded : _cpuModelsExpanded) ?? (gpu == _selectedGpu)
                };
                var header = new Button
                {
                    FontFamily = Theme.TitleFont,
                    FontSize = 15,
                    TextColor = Theme.PrimaryText,
                    BackgroundColor = Colors.Transparent,
                    HorizontalOptions = LayoutOptions.Fill,
                    Padding = new Thickness(10, 8)
                };
                void UpdateHeader()
                {
                    header.Text = (models.IsVisible ? "▼ " : "▶ ") + title;
                    SemanticProperties.SetDescription(header, title + (models.IsVisible ? ", expanded" : ", collapsed"));
                    SemanticProperties.SetHint(header, models.IsVisible ? "Collapse model list" : "Expand model list");
                }
                UpdateHeader();
                header.Clicked += (_, _) =>
                {
                    models.IsVisible = !models.IsVisible;
                    if (gpu) _gpuModelsExpanded = models.IsVisible;
                    else _cpuModelsExpanded = models.IsVisible;
                    UpdateHeader();
                    if (inSheet) RefreshModelList();
                };
                list.Children.Add(header);
                list.Children.Add(models);
                for (int i = 0; i < Models.Length; i++)
                {
                    int index = i;
                    ModelOption option = Models[i];
                    if (gpu && !HasGpu(option)) continue;
                    bool selected = index == _selectedModel && gpu == _selectedGpu;
                    var detail = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
                    var status = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.Accent };
                    var resume = new Button { Text = "Continue", FontFamily = Theme.TitleFont, FontSize = 12, HorizontalOptions = LayoutOptions.Start };
                    var downloadRow = new ModelDownloadRow { Option = option, Detail = detail, Status = status, Continue = resume, InSheet = inSheet };
                    _modelDownloadRows.Add(downloadRow);
                    UpdateModelDownloadRow(downloadRow);
                    SemanticProperties.SetDescription(resume, "Continue downloading " + option.Name);
                    resume.Clicked += (_, _) =>
                    {
                        if (!CanChangeModel) return;
                        if (inSheet) _sheet.Dismiss(index + (gpu ? ":gpu" : ":cpu"));
                        SelectModel(index, gpu);
                        _ = PrepareModelAsync(true);
                    };
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
                            new Label { Text = option.Name + (gpu ? " · GPU" : " · CPU"), FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                            status,
                            detail,
                            resume
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
                            _sheet.Dismiss(index + (gpu ? ":gpu" : ":cpu"));
                        else if (CanChangeModel)
                            SelectModel(index, gpu);
                    });
                    models.Children.Add(cell);
                }
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

        private bool CanChangeModel => !_busy && !_reloadingCpuThreads && !_recorder.IsRecording;

        private async void OnModelChipTapped()
        {
            if (!CanChangeModel)
                return;
            string choice = await _sheet.ShowViewAsync("Model", BuildModelList(true), "Cancel");
            string[] parts = choice?.Split(':');
            if (parts?.Length == 2 && int.TryParse(parts[0], out int index) && CanChangeModel)
                SelectModel(index, parts[1] == "gpu");
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
        private void SelectModel(int index, bool? gpu = null)
        {
            UiStallWatchdog.Mark("Select model " + Models[index].Name);
            // A chat belongs to a model: another model starts a new chat.
            if (_pageStarted && index != _selectedModel)
                StartNewChat();
            _selectedModel = index;
            ModelOption option = SelectedOption;
            _selectedGpu = (gpu ?? _selectedGpu) && HasGpu(option);
            if (_selectedGpu) _gpuModelsExpanded = true;
            else _cpuModelsExpanded = true;
            _gpuUnavailable = false;
            _modelChipLabel.Text = option.Name + (_selectedGpu ? " · GPU" : " · CPU");
            RefreshModelList();
            _talkButton.IsVisible = option.AcceptsAudio;
            _thinkingSwitch.IsVisible = option.CanThink;
            _webSearchSwitch.IsVisible = option.CanUseTools;
            _toggleRow.IsVisible = option.CanThink || option.CanUseTools;
            UpdateContextSection();
            // Drop attachments the new model can't take.
            foreach (PendingAttachment attachment in _pending.ToList())
                if (attachment.Image != null ? !option.AcceptsImages : !option.AcceptsAudio)
                    RemovePending(attachment);
            if (_pageStarted)
            {
                // Constructor UI setup must not overwrite the saved backend before OnAppearing restores it.
                try { Preferences.Default.Set(BackendPreference, _selectedGpu); } catch (Exception) { }
                try { Preferences.Default.Set(LastModelPreference, option.Choice.ToString()); } catch (Exception) { }
                _ = PrepareModelAsync(false);
            }
        }

        // ---------- Loading the model ----------

        // Open with the model used last time, and start loading it if it is already downloaded.
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _pageVisible = true;
            StartChatIndexing();
            if (_pageStarted)
            {
                RefreshPhotoIndex();
                return;
            }
            _pageStarted = true;
            int index = 0;
            try
            {
                string last = Preferences.Default.Get(LastModelPreference, null as string);
                int found = Array.FindIndex(Models, m => m.Choice.ToString() == last);
                if (found >= 0)
                    index = found;
            }
            catch (Exception)
            {
            }
            try { _selectedGpu = Preferences.Default.Get(BackendPreference,
                Preferences.Default.Get(UseGpuPreference, DeviceInfo.Platform != DevicePlatform.WinUI)); } catch (Exception) { }
            SelectModel(index);
            // Photo search: index photos added since the app last ran.
            StartPhotoIndexing();
            // Saved document indexes no chat uses (attached, then the chat was never saved).
            _ = _store.DeleteUnusedDocumentsAsync();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _pageVisible = false;
        }

        private static bool IsDownloaded(ModelOption option)
        {
            // A file that exists but isn't verified yet (e.g. downloaded by an older version) is checked when loading.
            try
            {
                string path = option.File.LocalFile;
                return option.File.IsLocalFileVerified || (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 0);
            }
            catch (System.IO.IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // Get the selected model ready: nothing to do if it's loaded or loading; load it if it's downloaded (or the
        // download is confirmed); otherwise offer the download. Returns the load task, or null when waiting for the
        // user to confirm a download.
        private Task<bool> PrepareModelAsync(bool downloadConfirmed)
        {
            ModelOption option = SelectedOption;
            if (ModelReady)
            {
                HideLoadCard();
                return Task.FromResult(true);
            }
            if (_loadTask != null && _loadingChoice == option.Choice && _loadingOnGpu == WantsGpu(option)
                && _loadingContextTokens == ContextTokensFor(option))
                return _loadTask;
            if (!downloadConfirmed && !IsDownloaded(option))
            {
                CancelLoad();
                ShowDownloadPrompt(option);
                return null;
            }
            _loadCancel?.Cancel();
            _loadCancel = new CancellationTokenSource();
            _loadingChoice = option.Choice;
            _loadingOnGpu = WantsGpu(option);
            _loadingContextTokens = ContextTokensFor(option);
            _loadTask = LoadModelAsync(option, _loadTask, _loadCancel.Token);
            return _loadTask;
        }

        private static void ConfigureCpuThreads(Emgu.LiteRT.LM.EngineSettings settings)
        {
            int count = CpuThreadSettings.Count;
            settings.NumThreads = count;
            settings.AudioNumThreads = count;
        }

        private async Task<bool> LoadModelAsync(ModelOption option, Task<bool> previousLoad, CancellationToken token)
        {
            UiStallWatchdog.Mark("Loading model " + option.Name);
            ShowLoadCard(option, "Starting...", null);
            // Let a cancelled load finish first (an engine being created can't be interrupted), so two models are never
            // in memory at once, then release the model that was loaded before.
            if (previousLoad != null)
            {
                try { await previousLoad; } catch (Exception) { }
            }
            // (PrepareModelAsync only gets here when the loaded model isn't the one wanted: another model, or the same one
            // on another backend.)
            if (_model != null)
                await CleanupModelAsync();
            if (token.IsCancellationRequested)
                return false;

            bool onGpu = WantsGpu(option);
            int contextTokens = ContextTokensFor(option);
            ModelLoadStage lastStage = ModelLoadStage.CheckingFile;
            LanguageModel NewModel()
            {
                LanguageModel created = CreateModel(option.Choice);
                if (contextTokens > 0)
                    created.MaxNumTokens = contextTokens;
                created.LoadStageChanged += (s, stage) =>
                {
                    lastStage = stage;
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested)
                            ShowLoadStage(option, stage);
                    });
                };
                created.OnDownloadProgressChanged += (total, received, percent) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!token.IsCancellationRequested)
                        ShowDownloadProgress(total, received);
                });
                return created;
            }
            LanguageModel model = NewModel();
            try
            {
                if (onGpu)
                {
                    try
                    {
                        // Gemma4 uses 32-bit activations on the GPU: with LiteRT-LM's default 16-bit ones, Gemma 4 E2B
                        // answered wrongly after a long conversation on a Pixel Fold and an M1 Mac (Performance.md).
                        await model.Init(backend: "gpu", configureSettings: ConfigureCpuThreads, cancellationToken: token);
                    }
                    catch (Exception ex) when (lastStage == ModelLoadStage.LoadingEngine && !token.IsCancellationRequested)
                    {
                        UiStallWatchdog.Mark("GPU unavailable: " + ex.Message);
                        bool useCpu = await MainThread.InvokeOnMainThreadAsync(() => DisplayAlertAsync(
                            "GPU model could not load", ex.Message + "\n\nRun this model on CPU instead?", "Run on CPU", "Cancel"));
                        if (!useCpu) throw;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            _selectedGpu = false;
                            try { Preferences.Default.Set(BackendPreference, false); } catch (Exception) { }
                            _modelChipLabel.Text = option.Name + " · CPU";
                            RefreshModelList();
                        });
                        onGpu = false;
                        _loadingOnGpu = false;
                        await Task.Run(() => model.Dispose());
                        model = NewModel();
                        await model.Init(configureSettings: ConfigureCpuThreads, cancellationToken: token);
                    }
                }
                else
                {
                    await model.Init(configureSettings: ConfigureCpuThreads, cancellationToken: token);
                }
            }
            catch (Exception ex)
            {
                await Task.Run(() => model.Dispose());
                if (token.IsCancellationRequested)
                {
                    if (_downloadingChoice == option.Choice) _downloadingChoice = null;
                    UpdateModelDownloadRows();
                    if (SelectedChoice == option.Choice && _loadingChoice == null && !IsDownloaded(option))
                        ShowDownloadPrompt(option);
                    return false;
                }
                UiStallWatchdog.Mark("Loading failed: " + ex.Message);
                ShowLoadError(option, ex.Message);
                ClearLoading(option.Choice);
                return false;
            }
            if (token.IsCancellationRequested)
            {
                await Task.Run(() => model.Dispose());
                return false;
            }

            _model = model;
            _chat = model.CreateChat();
            _chat.EnableThinking = _thinkingSwitch.IsToggled && option.CanThink;
            _loaded = option.Choice;
            _loadedOnGpu = onGpu;
            _loadedContextTokens = contextTokens;
            // Reloaded on another backend in the middle of a chat: the new engine is given the conversation.
            if (_historyItems.Count > 0 || _summary != null)
                _needsPrime = true;
            if (_gpuUnavailable && WantsGpuSetting(option))
                SetStatus("The GPU isn't available on this device - using the CPU.");
            ClearLoading(option.Choice);
            HideLoadCard();
            UpdateMemoryLabel();
            UiStallWatchdog.Mark("Model ready: " + option.Name);
            return true;
        }

        // The selected model is loaded, on the backend it should run on.
        private bool ModelReady => _model != null && _loaded == SelectedChoice && _loadedOnGpu == WantsGpu(SelectedOption)
            && _loadedContextTokens == ContextTokensFor(SelectedOption);

        // Whether the model should run on the GPU: the setting, unless the GPU failed to load this session.
        private bool WantsGpu(ModelOption option) => WantsGpuSetting(option) && !_gpuUnavailable;

        private bool WantsGpuSetting(ModelOption option)
        {
            return HasGpu(option) && _selectedGpu;
        }

        // Whether the model can run on this platform's GPU.
        private static bool HasGpu(ModelOption option)
        {
#if ANDROID
            return option.GpuOnAndroid;
#elif MACCATALYST
            return option.GpuOnMac;
#elif WINDOWS_WEBGPU
            return option.GpuOnWindows;
#else
            return false;
#endif
        }

        // ---------- Context window ----------

        // The context window to load a model with, or 0 for LanguageModel's default (4096): the setting, or Automatic -
        // the model's desktop size on Mac Catalyst and Windows, the default on phones (re-reading a full 8k chat took a
        // Pixel Fold almost 6 minutes and left it 1.5 GB of memory) - capped at what the model's file allows.
        private int ContextTokensFor(ModelOption option)
        {
            if (option.MaxContextTokens <= 0)
                return 0;
            int wanted = _contextSetting > 0 ? _contextSetting : AutomaticContextTokens(option);
            return wanted > 0 ? Math.Min(wanted, option.MaxContextTokens) : 0;
        }

        private static int AutomaticContextTokens(ModelOption option)
        {
#if MACCATALYST || WINDOWS
            return option.DesktopContextTokens;
#else
            return 0;
#endif
        }

        private static string ContextWindowName(int tokens)
        {
            // Thousands, as people say them: 4096 is "4k", 32000 "32k".
            return tokens <= 0 ? "Automatic" : (int)Math.Round(tokens / 1000.0) + "k";
        }

        // The chips act as one choice: tapping the selected one keeps it selected.
        private void OnContextWindowChipToggled(int value, bool on)
        {
            if (_updatingContextChips)
                return;
            if (!on && value == _contextSetting)
            {
                SetContextChips();
                return;
            }
            if (!on)
                return;
            _contextSetting = value;
            try { Preferences.Default.Set(ContextWindowPreference, value); } catch (Exception) { }
            SetContextChips();
            UpdateContextSection();
            UiStallWatchdog.Mark("Context window: " + ContextWindowName(value));
            // Reload the selected model with the new window; the chat continues (compacted on the next message if it no
            // longer fits).
            if (_pageStarted && SelectedOption.MaxContextTokens > 0 && (_model != null || _loadTask != null))
                _ = PrepareModelAsync(false);
        }

        private void SetContextChips()
        {
            _updatingContextChips = true;
            for (int i = 0; i < _contextChips.Count; i++)
                _contextChips[i].IsToggled = ContextWindowChoices[i] == _contextSetting;
            _updatingContextChips = false;
        }

        // Shown for the models whose window can change; the note says what Automatic means here and what a size costs.
        private void UpdateContextSection()
        {
            if (_contextSection == null)
                return;
            ModelOption option = SelectedOption;
            _contextSection.IsVisible = option.MaxContextTokens > 0;
            int automatic = AutomaticContextTokens(option);
            _contextChips[0].Text = "Automatic (" + ContextWindowName(automatic > 0 ? automatic : 4096) + ")";
            string note = "How much of the chat the model keeps in mind. A larger window summarizes the chat less often, " +
                "but the model takes longer to read it again (when a saved chat is opened, or after summarizing) and needs " +
                "more memory. Changing it reloads the model; the chat continues.";
#if ANDROID || IOS
            if (_contextSetting >= 16384)
                note += " On a phone, 16k and more can take many minutes to read a long chat again, and may run out of memory.";
#else
            if (_contextSetting >= 32000)
                note += " At 32k, reading a full chat again took about 4 minutes on an Apple Silicon Mac's CPU.";
#endif
            _contextNote.Text = note;
        }

        // LiteRT's GPU accelerator keeps its compiled programs and converted weights next to the model
        // (<model file>_<...>_mldrift_program_cache.bin / _mldrift_weight_cache.bin, ~0.8 GB for Gemma 4 E2B).
        private static void DeleteGpuCache(ModelOption option)
        {
            try
            {
                string modelFile = option.File.LocalFile;
                string folder = System.IO.Path.GetDirectoryName(modelFile);
                if (!System.IO.Directory.Exists(folder))
                    return;
                foreach (string file in System.IO.Directory.GetFiles(folder, System.IO.Path.GetFileName(modelFile) + "_*_mldrift_*"))
                    System.IO.File.Delete(file);
            }
            catch (Exception)
            {
            }
        }

        private static LanguageModel CreateModel(ModelChoice choice)
        {
            switch (choice)
            {
                case ModelChoice.Qwen3: return new Qwen3();
                case ModelChoice.Qwen35_0_8B_VL: return new Qwen35_0_8B_VL();
                case ModelChoice.Qwen35_4B: return new Qwen35_4B();
                case ModelChoice.Gemma4E4B: return new Gemma4E4B();
                case ModelChoice.Gemma4_12B: return new Gemma4_12B();
                default: return new Gemma4E2B();
            }
        }

        private void ClearLoading(ModelChoice choice)
        {
            if (_loadingChoice == choice)
            {
                _loadingChoice = null;
                _loadTask = null;
            }
            if (_downloadingChoice == choice) _downloadingChoice = null;
            UpdateModelDownloadRows();
            _chipSpinner.IsVisible = _chipSpinner.IsRunning = _loadTask != null;
        }

        // Cancel the load in progress; downloaded bytes remain available for Continue.
        private void CancelLoad()
        {
            if (_loadTask == null)
                return;
            UiStallWatchdog.Mark("Cancel loading");
            _loadCancel?.Cancel();
            _downloadingChoice = null;
            UpdateModelDownloadRows();
            _loadingChoice = null;
            _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
            ModelOption option = SelectedOption;
            if (ModelReady)
                HideLoadCard();
            else if (!IsDownloaded(option))
                ShowDownloadPrompt(option);
            else
                ShowLoadError(option, "Loading was cancelled.");
            // _loadTask is kept until it finishes, so the next load waits for it.
        }

        private void ShowDownloadPrompt(ModelOption option)
        {
            bool wifi = true;
            try
            {
                var profiles = Connectivity.Current.ConnectionProfiles;
                wifi = profiles.Contains(ConnectionProfile.WiFi) || profiles.Contains(ConnectionProfile.Ethernet);
            }
            catch (Exception)
            {
            }
            bool paused = option.File.PartialDownloadBytes > 0;
            string message = paused ? PausedDownload(option) : string.Format("{0} needs a one-time {1} download.", option.Name, option.Size);
            ShowLoadCard(option, message + (wifi ? "" : " You're not on Wi-Fi - mobile data may be used."), null);
            _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
            _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
            SetLoadButtons(paused ? "Continue" : "Download " + option.Size, () => _ = PrepareModelAsync(true), "Not now");
        }

        private void ShowLoadError(ModelOption option, string message)
        {
            if (_downloadingChoice == option.Choice) _downloadingChoice = null;
            UpdateModelDownloadRows();
            bool paused = option.File.PartialDownloadBytes > 0;
            ShowLoadCard(option, (paused ? PausedDownload(option) + "\n" : "") + "Could not load the model: " + message, null);
            _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
            SetLoadButtons(paused ? "Continue" : "Retry", () => _ = PrepareModelAsync(true), null);
        }

        private void ShowLoadStage(ModelOption option, ModelLoadStage stage)
        {
            switch (stage)
            {
                case ModelLoadStage.CheckingFile:
                    ShowLoadCard(option, "Checking the model file...", null);
                    break;
                case ModelLoadStage.Downloading:
                    _downloadingChoice = option.Choice;
                    _downloadReceived = option.File.PartialDownloadBytes;
                    _downloadTotal = option.File.PartialDownloadTotalBytes;
                    UpdateModelDownloadRows();
                    ShowLoadCard(option, "Downloading...", 0);
                    break;
                case ModelLoadStage.VerifyingFile:
                    _downloadingChoice = null;
                    UpdateModelDownloadRows();
                    ShowLoadCard(option, "Checking the download...", null);
                    break;
                case ModelLoadStage.LoadingEngine:
#if WINDOWS
                    ShowLoadCard(option, _loadingOnGpu ? "Loading onto the GPU (this can take several minutes)..." : "Loading into memory...", null);
#else
                    ShowLoadCard(option, _loadingOnGpu ? "Loading onto the GPU (about 30 s the first time)..." : "Loading into memory...", null);
#endif
                    break;
            }
        }

        private void ShowDownloadProgress(long? total, long received)
        {
            _downloadReceived = received;
            _downloadTotal = total;
            UpdateModelDownloadRows();
            const double GB = 1024.0 * 1024 * 1024;
            if (total.HasValue && total.Value > 0)
            {
                _loadStage.Text = string.Format("Downloading... {0:0.00} / {1:0.00} GB ({2:0}%)", received / GB, total.Value / GB, 100.0 * received / total.Value);
                _loadProgress.IsVisible = true;
                _loadProgress.Progress = (double)received / total.Value;
            }
            else
            {
                _loadStage.Text = string.Format("Downloading... {0:0.00} GB", received / GB);
            }
        }

        // The card while loading: a spinner, the stage and (for a download) a progress bar, with Cancel.
        private void ShowLoadCard(ModelOption option, string stage, double? progress)
        {
            _loadCard.IsVisible = true;
            _loadTitle.Text = option.Name;
            _loadStage.Text = stage;
            _loadProgress.IsVisible = progress.HasValue;
            if (progress.HasValue)
                _loadProgress.Progress = progress.Value;
            _loadSpinner.IsVisible = _loadSpinner.IsRunning = true;
            _chipSpinner.IsVisible = _chipSpinner.IsRunning = true;
            SetLoadButtons(null, null, "Cancel");
        }

        private void SetLoadButtons(string primaryText, Action primaryAction, string cancelText)
        {
            _loadPrimaryAction = primaryAction;
            _loadPrimaryButton.IsVisible = primaryText != null;
            if (primaryText != null)
                _loadPrimaryButton.Text = primaryText;
            _loadCancelButton.IsVisible = cancelText != null;
            if (cancelText != null)
                _loadCancelButton.Text = cancelText;
        }

        private void HideLoadCard()
        {
            _loadCard.IsVisible = false;
            _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
        }

        private void OnThinkingToggled(object sender, ToggledEventArgs e)
        {
            try { Preferences.Default.Set(ThinkPreference, e.Value); } catch (Exception) { }
            if (_chat != null)
                _chat.EnableThinking = e.Value && SelectedOption.CanThink;
        }

        // ---------- Sending a message ----------

        private async void OnSendClicked(object sender, EventArgs e)
        {
            if (_busy || _reloadingCpuThreads || _recorder.IsRecording)
                return;

            string prompt = _promptEditor.Text?.Trim();
            List<PendingAttachment> attachments = _pending.ToList();
            if (string.IsNullOrEmpty(prompt))
            {
                if (attachments.Count == 0)
                {
                    if (_document == null)
                        return;
                    prompt = "Summarize this document.";
                }
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
            _sendSteps.Clear();
            lock (_sendImages)
                _sendImages.Clear();
            UiStallWatchdog.Mark(string.Format("Send: {0} chars, {1} attachment(s), model {2}, agent {3}, think {4}, web {5}",
                prompt.Length, attachments.Count, SelectedOption.Name, UseAgent, _thinkingSwitch.IsToggled, _webSearchSwitch.IsToggled));
            StopSpeaking();
            SetBusy(true);
            try
            {
                if (!await EnsureModelReadyAsync())
                    return;

                _promptEditor.Text = string.Empty;
                ClearPendingAttachments();
                // With a document attached: the excerpts (and maybe a picture or page) most related to the message.
                DocumentContext document = await GetDocumentContextAsync(prompt);
                if (document?.Image != null)
                    attachments.Add(document.Image);
                // Where a "summarized" divider goes if this message makes the chat compact.
                int dividerIndex = _transcript.Children.Contains(_emptyLabel) ? 0 : _transcript.Children.Count;
                AddUserMessage(prompt, attachments);
                var userItem = new HistoryItem { Role = MessageRecord.UserRole, Text = prompt, Context = document?.Text };
                if (dividerIndex < _transcript.Children.Count)
                    userItem.View = _transcript.Children[dividerIndex] as View;
                foreach (PendingAttachment attachment in attachments)
                    userItem.Attachments.Add((attachment.Image != null ? "image" : "audio", attachment.MediaType, attachment.Data));
                // Re-reading the earlier conversation (an opened chat, or after compacting) is what takes the time.
                int compactions = _compactions;
                bool rereading = await PrepareContextAsync(userItem, dividerIndex);
                if (document != null && _compactions != compactions)
                    document = await RefreshDocumentContextAsync(prompt, document, userItem, attachments);
                SetStatus(rereading ? "Reading the conversation so far..." : "Generating...");

                // Stream the reply into its own bubble as it's generated, rather than waiting for the
                // full answer. onChunk fires on a LiteRT-LM background thread with each incremental piece
                // of text (including any <think>...</think> block), so re-splitting the text accumulated
                // so far through ChatReply on every chunk gives the same thinking/answer split a finished
                // reply would show, just filled in live.
                StreamingBubble bubble = AddStreamingBubble();
                if (document?.Step != null)
                {
                    AddStepLine(bubble, document.Step);
                    _sendSteps.Add(document.Step);
                }
                string modelPrompt = userItem.ModelText;
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

                ChatReply reply;
                for (int attempt = 0; ; attempt++)
                {
                    // Kept so teardown (OnNavigatedFrom / switching models) can wait for it instead of
                    // freeing the chat/engine while LiteRT-LM is still generating into it.
                    Task<ChatReply> generation = UseAgent
                        ? RunAgentAsync(modelPrompt, attachments, bubble)
                        : _chat.SendAsync(modelPrompt, attachments.Select(a => a.Attachment), onChunk);
                    _generation = generation;
                    try
                    {
                        reply = await generation;
                        break;
                    }
                    catch (Exception ex) when (attempt == 0 && IsContextFull(ex))
                    {
                        // The estimate was short, so don't rely on it: drop the earlier messages (a summary made before
                        // stays) and try once more.
                        UiStallWatchdog.Mark("Context full: " + ex.Message);
                        streamed.Clear();
                        _sendSteps.Clear();
                        await CompactAsync(false, 0, dividerIndex);
                        PrimeModel();
                        SetStatus("Generating...");
                    }
                }

                UpdateStreamingBubble(bubble, reply, true);
                MarkDocumentContextGiven(document);
                UiStallWatchdog.Mark(string.Format("Reply done: {0} chars, {1} chars thinking", reply.Text.Length, reply.Thinking.Length));
                await SaveExchangeAsync(userItem, reply);
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
                UpdateMemoryLabel();
                // The agent may have saved or deleted a memory.
                _ = RefreshMemoryCountAsync();
            }
        }

        // ---------- Web search (agent) ----------

        // Models that can call tools always chat through the agent, with the tools chosen per message (the date and time,
        // the location if shared, the search tools with the Web search chip on), so changing them keeps the conversation.
        private bool UseAgent => SelectedOption.CanUseTools;

        // Send a message through the agent, streaming its reply into the bubble and showing each tool call as a step
        // line above it. Runs on the UI thread (the await foreach resumes on it).
        private async Task<ChatReply> RunAgentAsync(string prompt, List<PendingAttachment> attachments, StreamingBubble bubble)
        {
            _agentBubble = bubble;
            await EnsureAgentAsync();

            List<AIContent> contents = new List<AIContent>();
            foreach (PendingAttachment attachment in attachments)
                contents.Add(new DataContent(attachment.Data, attachment.MediaType));
            contents.Add(new TextContent(prompt));
            // The tools for this message: the date and time always, the location if the user shares it, and the search
            // tools with the Web search chip on.
            ChatOptions options = new ChatOptions
            {
                Reasoning = new ReasoningOptions { Effort = _thinkingSwitch.IsToggled && SelectedOption.CanThink ? ReasoningEffort.Medium : ReasoningEffort.None },
                Tools = ChatAgentTools.Create(_tavilyKey, _webSearchSwitch.IsToggled, _shareLocationSwitch.IsToggled,
                    _memorySwitch.IsToggled ? _store : null, FindPhotosAsync, ImageGenerationSupported ? GenerateImageAsync : null,
                    SearchChatsAsync),
                // Local search and generation tools remain available with their settings off, so the model can explain how to
                // turn them on.
                Instructions = JoinInstructions(await MemoryInstructionsAsync(), PhotoInstructions, ChatSearchInstructions,
                    ImageGenerationSupported ? ImageInstructions : null),
                ToolMode = ChatToolMode.Auto
            };

            StringBuilder thinking = new StringBuilder();
            StringBuilder answer = new StringBuilder();
            Dictionary<string, Label> steps = new Dictionary<string, Label>();
            // A restored chat: the first message of the session carries the earlier conversation.
            List<AIChatMessage> messages = new List<AIChatMessage>();
            if (_agentPrimeHistory != null)
            {
                messages.AddRange(_agentPrimeHistory);
                _agentPrimeHistory = null;
            }
            messages.Add(new AIChatMessage(ChatRole.User, contents));
            await foreach (AgentResponseUpdate update in _agent.RunStreamingAsync(
                messages, _session, new ChatClientAgentRunOptions(options)))
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
                            UiStallWatchdog.Mark("Tool call: " + step);
                            steps[call.CallId] = AddStepLine(bubble, step + "...");
                            _sendSteps.Add(step);
                            SetStatus(step + "...");
                            break;
                        case FunctionResultContent result:
                            UiStallWatchdog.Mark("Tool result" + (result.Exception != null ? ": " + result.Exception.Message : ""));
                            if (steps.TryGetValue(result.CallId, out Label line))
                            {
                                // A fact the model tried to save again isn't a new memory: say so instead of "Remembering".
                                string resultText = result.Result?.ToString() ?? string.Empty;
                                if (resultText.StartsWith("Already remembered", StringComparison.Ordinal))
                                {
                                    line.Text = "\U0001F50E Already remembered";
                                    int index = _sendSteps.FindIndex(t => t.StartsWith("Remembering", StringComparison.Ordinal));
                                    if (index >= 0)
                                        _sendSteps[index] = "Already remembered";
                                }
                                else
                                    line.Text = line.Text.TrimEnd('.') + (result.Exception != null ? " - failed: " + result.Exception.Message : "");
                            }
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

        private static string JoinInstructions(params string[] parts)
        {
            string joined = string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
            return joined.Length > 0 ? joined : null;
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
            // The tools are passed with each message (RunAgentAsync), so the settings and the Web search chip can change
            // them during a conversation.
            if (_agent == null)
            {
                _agent = new ChatClientAgent(_chatClient, AgentInstructions, "Assistant", null, null);
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
            try { Preferences.Default.Set(WebSearchPreference, e.Value); } catch (Exception) { }
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
            // The tools are chosen per message, so the conversation simply continues.
            if (!_busy)
                SetStatus(key.Length > 0 ? "Web search with Tavily is on." : "Tavily key removed.");
        }

        // Wait for the selected model before sending: loads it if needed. If it still needs downloading, the card asks
        // for the download first and the message isn't sent.
        private async Task<bool> EnsureModelReadyAsync()
        {
            if (ModelReady)
                return true;
            Task<bool> load = PrepareModelAsync(false);
            if (load == null)
            {
                SetStatus("Download the model first (above).");
                return false;
            }
            SetStatus("Waiting for the model to load...");
            bool ready = await load;
            SetStatus(null);
            return ready && ModelReady;
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
            UiStallWatchdog.Mark(string.Format("Speak: {0} chars", text.Length));
            CancellationTokenSource speech = new CancellationTokenSource();
            _speech = speech;
            try
            {
                // With a voice for the reply's language, and without its Markdown.
                await ReplySpeech.SpeakAsync(text, speech.Token);
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
            if (_busy || _reloadingCpuThreads)
                return;

            var rows = new List<(string Section, string Glyph, string Text, string Value)>();
            rows.Add(("DOCUMENT (PDF, WORD, TEXT)", Theme.GlyphText, "Document", "document"));
            if (SelectedOption.AcceptsImages)
            {
                rows.Add(("IMAGE", Theme.GlyphImage, "Photo Library", "library"));
                if (MediaPicker.Default.IsCaptureSupported)
                    rows.Add((null, Theme.GlyphCamera, "Take Photo", "camera"));
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
                if (action == "document")
                {
                    FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a document", FileTypes = DocumentReader.FileTypes });
                    if (file != null)
                        AttachDocument(await ReadAllBytesAsync(file), file.FileName);
                }
                else if (action == "library")
                {
                    FileResult file = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                    if (file != null)
                        AddPendingImage(await ReadAllBytesAsync(file), file.FileName);
                }
                else if (action == "camera")
                {
                    // The device's camera app; the photo is downscaled and re-encoded like a library photo (OpenCV
                    // applies its EXIF orientation when decoding).
                    FileResult file = await MediaPicker.Default.CapturePhotoAsync();
                    if (file != null)
                        AddPendingImage(await ReadAllBytesAsync(file), "Photo");
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
            UiStallWatchdog.Mark(string.Format("Attach image {0}: {1} bytes (decode on UI thread)", name, bytes.Length));
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
            UiStallWatchdog.Mark("New chat");
            if (_busy || _reloadingCpuThreads)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            StartNewChat();
            RemoveDocument();
        }

        // Clear the transcript and the model's conversation; the next message starts a new saved chat.
        private void StartNewChat()
        {
            _chat?.ClearHistory();
            _session = null;
            _conversationId = null;
            UpdateExportChatButton();
            _historyItems.Clear();
            _agentPrimeHistory = null;
            _contextStart = 0;
            _summary = null;
            _summaryTokens = 0;
            _needsPrime = false;
            ResetDocumentContext();
            ClearTranscript();
            UpdateMemoryLabel();
        }

        // ---------- Context window ----------

        private int ContextWindow => _model != null && _model.MaxNumTokens > 0 ? _model.MaxNumTokens : 4096;

        // What the conversation may take before a message is sent: the window, less room for the reply and what the
        // chat template - or the agent's instructions and tools - take.
        private int ContextBudget => ContextWindow - ReplyReserveTokens - (UseAgent ? AgentOverheadTokens : ChatOverheadTokens);

        // The estimated size of a message in the model's context. Runs the tokenizer: call it off the UI thread.
        private static int EstimateTokens(LanguageModel model, HistoryItem item)
        {
            int tokens = model.CountTokens(item.ModelText ?? string.Empty) + MessageOverheadTokens;
            foreach (var attachment in item.Attachments)
                tokens += attachment.Kind == "image" ? ImageTokens : AudioTokens(attachment.Data);
            return tokens;
        }

        // Audio by its length, from the WAV header; other formats (MP3, FLAC) count as the longest voice message.
        private static int AudioTokens(byte[] data)
        {
            double seconds = MaxVoiceLength.TotalSeconds;
            if (data != null && data.Length > 44
                && Encoding.ASCII.GetString(data, 0, 4) == "RIFF" && Encoding.ASCII.GetString(data, 8, 4) == "WAVE")
            {
                int byteRate = BitConverter.ToInt32(data, 28);
                if (byteRate > 0)
                    seconds = (data.Length - 44) / (double)byteRate;
            }
            return (int)Math.Ceiling(seconds * AudioTokensPerSecond) + 10;
        }

        private static int Cost(HistoryItem item) => Math.Max(0, item.Tokens) + item.ExtraTokens;

        // The size of what the model sees now, without the next message.
        private int ContextTokens()
        {
            int tokens = _summary != null ? _summaryTokens : 0;
            foreach (HistoryItem item in _historyItems.Skip(_contextStart))
                tokens += Cost(item);
            return tokens;
        }

        // Count the messages the model sees that aren't counted yet (e.g. of a saved chat that was opened), the summary,
        // and the next message.
        private async Task CountTokensAsync(HistoryItem next)
        {
            LanguageModel model = _model;
            List<HistoryItem> items = _historyItems.Skip(_contextStart).Where(i => i.Tokens < 0).ToList();
            if (next != null && next.Tokens < 0)
                items.Add(next);
            string summary = _summary != null && _summaryTokens == 0 ? _summary : null;
            if (items.Count == 0 && summary == null)
                return;
            int summaryTokens = await Task.Run(() =>
            {
                foreach (HistoryItem item in items)
                    item.Tokens = EstimateTokens(model, item);
                return summary == null ? 0
                    : model.CountTokens(SummaryIntro + summary + SummaryAcknowledgement) + 2 * MessageOverheadTokens;
            });
            if (summary != null && summary == _summary)
                _summaryTokens = summaryTokens;
        }

        // Before a message is sent: compact the conversation if the message would make it too long for the model, and
        // give the model the conversation again if it needs it. Returns true if the model has to read earlier messages
        // again.
        private async Task<bool> PrepareContextAsync(HistoryItem next, int dividerIndex)
        {
            try
            {
                await CountTokensAsync(next);
                int budget = ContextBudget - next.Tokens;
                if (ContextTokens() > budget)
                {
                    // Summarizing keeps the latest exchanges up to a quarter of the window, so it doesn't happen again
                    // soon; dropping keeps as many as fit.
                    bool summarize = SelectedOption.CanSummarize;
                    int keep = summarize
                        ? Math.Min(budget - SummaryMaxTokens - 2 * MessageOverheadTokens, ContextWindow / 4)
                        : budget - _summaryTokens;
                    await CompactAsync(summarize, keep, dividerIndex);
                }
            }
            catch (Exception ex)
            {
                // Sent anyway; if it's too long, SendAsync drops more and retries.
                UiStallWatchdog.Mark("Preparing the context failed: " + ex.Message);
            }
            if (!_needsPrime)
                return false;
            PrimeModel();
            return _summary != null || _contextStart < _historyItems.Count;
        }

        // LiteRT-LM's errors for a conversation longer than the context window: "Input token ids are too long..." for a
        // new conversation, "Prefill input length exceeds available state entries..." for one that is continued.
        private static bool IsContextFull(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e.Message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0
                    || e.Message.IndexOf("exceeds available state", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        // Give the model the conversation: the summary and the messages since. The agent starts a new session with
        // them; Chat replaces its history.
        private void PrimeModel()
        {
            List<HistoryItem> items = ContextItems();
            foreach (HistoryItem item in _historyItems)
                item.ExtraTokens = 0;
            if (UseAgent)
            {
                _session = null;
                _agentPrimeHistory = ToAgentMessages(items);
            }
            else
            {
                _chat?.LoadHistory(ToChatMessages(items));
            }
            _needsPrime = false;
        }

        // Make room in the context: the messages before the latest exchanges that fit keepTokens are summarized - into
        // one summary with the previous one - or dropped. dividerIndex is where the divider goes if no message is kept.
        // Returns false if there is nothing to compact.
        private async Task<bool> CompactAsync(bool summarize, int keepTokens, int dividerIndex)
        {
            await CountTokensAsync(null);
            int keepStart = _historyItems.Count;
            int kept = 0;
            while (keepStart - 2 >= _contextStart)
            {
                int exchange = Cost(_historyItems[keepStart - 2]) + Cost(_historyItems[keepStart - 1]);
                if (keepTokens <= 0 || kept + exchange > keepTokens)
                    break;
                kept += exchange;
                keepStart -= 2;
            }
            if (keepStart <= _contextStart)
                return false;

            List<HistoryItem> older = _historyItems.GetRange(_contextStart, keepStart - _contextStart);
            UiStallWatchdog.Mark(string.Format("Compacting {0} messages, summarize {1}", older.Count, summarize));
            string summary = _summary;
            bool summarized = false;
            if (summarize)
            {
                SetStatus("Summarizing the earlier conversation...");
                try
                {
                    Task<string> task = SummarizeAsync(_summary, older);
                    _generation = task;
                    summary = await task;
                    summarized = true;
                }
                catch (Exception ex)
                {
                    // Dropped instead.
                    UiStallWatchdog.Mark("Summary failed: " + ex.Message);
                }
            }
            _summary = summary;
            _summaryTokens = 0;
            _contextStart = keepStart;
            _needsPrime = true;
            // Excerpts given with the messages that were summarized or dropped are no longer in the model's context.
            ResetDocumentContext();
            _compactions++;
            await CountTokensAsync(null);
            // Above the first message the model still sees, or (when it sees none) where the next one goes.
            View firstKept = keepStart < _historyItems.Count ? _historyItems[keepStart].View : null;
            int index = firstKept != null ? _transcript.Children.IndexOf(firstKept) : -1;
            AddCompactionDivider(index >= 0 ? index : dividerIndex, summarized ? summary : null);
            int upTo = older[older.Count - 1].MessageId;
            if (_conversationId != null && upTo > 0)
            {
                try
                {
                    await _store.SetSummaryAsync(_conversationId.Value, _summary, upTo);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not save the chat: " + ex.Message);
                }
            }
            UpdateMemoryLabel();
            return true;
        }

        // Have the model summarize the messages (and the summary of those before them) in a conversation of its own.
        private async Task<string> SummarizeAsync(string previous, List<HistoryItem> messages)
        {
            const string Instructions =
                "Summarize the conversation below between a user and an assistant, so the assistant can continue it " +
                "without seeing it again. Keep what may matter later: facts about the user, names, numbers, decisions, " +
                "code and file names, and open questions. Write short bullet points that are full sentences starting " +
                "with who is meant (\"The user ...\" or \"The assistant ...\"), at most 150 words, and write only the " +
                "summary.";
            LanguageModel model = _model;
            // Release the chat's and the agent's conversations first; they are given the conversation again afterwards.
            ResetAgent();
            _chat?.LoadHistory(Array.Empty<Emgu.LiteRT.LM.Models.ChatMessage>());
            int inputBudget = ContextWindow - SummaryMaxTokens - 200;
            string prompt = await Task.Run(() =>
            {
                string head = Instructions
                    + (previous != null ? "\n\nSummary of the conversation before this part:\n" + previous : string.Empty)
                    + "\n\nConversation:\n";
                List<string> lines = messages.Select(DescribeForSummary).ToList();
                // If it's still too long for the model, the oldest messages go.
                while (lines.Count > 1 && model.CountTokens(head + string.Join("\n", lines)) > inputBudget)
                    lines.RemoveAt(0);
                return head + string.Join("\n", lines);
            });
            Chat summarizer = model.CreateChat();
            try
            {
                summarizer.MaxOutputTokens = SummaryMaxTokens;
                summarizer.EnableThinking = false;
                ChatReply reply = await summarizer.SendAsync(prompt);
                string summary = reply.Text.Trim();
                if (summary.Length == 0)
                    throw new Exception("The summary is empty");
                return summary;
            }
            finally
            {
                await Task.Run(() => summarizer.Dispose());
            }
        }

        private static string DescribeForSummary(HistoryItem item)
        {
            string text = item.Text ?? string.Empty;
            if (text.Length > 2000)
                text = text.Substring(0, 2000) + "...";
            string media = string.Concat(item.Attachments.Select(a => a.Kind == "image" ? "[image] " : "[voice message] "));
            return (item.Role == MessageRecord.UserRole ? "User: " : "Assistant: ") + media + text;
        }

        // Compact now (Settings): everything but the latest exchange.
        private async void OnCompactTapped()
        {
            if (_busy || _reloadingCpuThreads)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            if (_model == null || _loaded != SelectedChoice)
            {
                SetStatus("Load the model first.");
                return;
            }
            SetBusy(true);
            try
            {
                await CountTokensAsync(null);
                int count = _historyItems.Count;
                int keep = count - 2 >= _contextStart ? Cost(_historyItems[count - 2]) + Cost(_historyItems[count - 1]) : 0;
                if (!await CompactAsync(SelectedOption.CanSummarize, keep, _transcript.Children.Count))
                    SetStatus("Nothing to compact yet.");
                else
                    SetStatus(null);
                ScrollToEnd();
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

        private void UpdateMemoryLabel()
        {
            if (_memoryLabel == null)
                return;
            string text = string.Format(
                "The model takes in about {0:N0} tokens. Longer chats are summarized - or, for the small models, their " +
                "earliest messages are dropped - so it keeps up; the conversation here stays complete.", ContextWindow);
            int visible = _historyItems.Count - _contextStart;
            if (_model != null && visible > 0 && _historyItems.Skip(_contextStart).All(i => i.Tokens >= 0))
            {
                // With what the chat template, or the agent's instructions and tools, take.
                int used = ContextTokens() + ContextWindow - ReplyReserveTokens - ContextBudget;
                text += string.Format(" This chat uses about {0:N0} ({1:P0}).", used, Math.Min(1.0, used / (double)ContextWindow));
            }
            _memoryLabel.Text = text;
            _compactButton.IsVisible = visible >= 4;
        }

        // A line in the transcript where the model's memory of the chat starts; tap it to read the summary.
        private void AddCompactionDivider(int index, string summary)
        {
            var label = new Label
            {
                Text = summary != null ? "Earlier messages summarized · tap to read" : "Earlier messages dropped from the model's memory",
                FontFamily = Theme.BodyFont,
                FontSize = 12,
                TextColor = Theme.SecondaryText,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center
            };
            var row = new Grid
            {
                ColumnSpacing = 8,
                Margin = new Thickness(8, 4),
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };
            row.Add(new BoxView { HeightRequest = 1, Color = Theme.Chevron, VerticalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(label, 1, 0);
            row.Add(new BoxView { HeightRequest = 1, Color = Theme.Chevron, VerticalOptions = LayoutOptions.Center }, 2, 0);
            if (summary != null)
            {
                SemanticProperties.SetDescription(row, "Earlier messages were summarized. Tap to read the summary.");
                row.OnTap(async () => await DisplayAlertAsync("Summary of earlier messages", summary, "OK"));
            }
            if (_transcript.Children.Contains(_emptyLabel))
                _transcript.Children.Remove(_emptyLabel);
            _transcript.Children.Insert(Math.Max(0, Math.Min(index, _transcript.Children.Count)), row);
        }

        // ---------- Memories about the user ----------

        // The memories, for the agent's instructions: the latest that fit MaxMemoryLength, oldest first.
        private async Task<string> MemoryInstructionsAsync()
        {
            if (!_memorySwitch.IsToggled)
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

        // ---------- Saved chats ----------

        // Save a completed exchange: the message (with its attachments), any tool steps, and the reply.
        private async Task SaveExchangeAsync(HistoryItem userItem, ChatReply reply)
        {
            var replyItem = new HistoryItem { Role = MessageRecord.AssistantRole, Text = reply.Text };
            _historyItems.Add(userItem);
            _historyItems.Add(replyItem);
            try
            {
                LanguageModel model = _model;
                // The document step line isn't a tool call: its excerpts are counted with the message.
                int steps = _sendSteps.Count(IsToolStep);
                await Task.Run(() =>
                {
                    replyItem.Tokens = EstimateTokens(model, replyItem);
                    // Until the conversation is rebuilt, the model's own conversation also holds the thinking and the
                    // tool calls.
                    replyItem.ExtraTokens = (string.IsNullOrEmpty(reply.Thinking) ? 0 : model.CountTokens(reply.Thinking))
                        + steps * ToolStepTokens;
                });
            }
            catch (Exception)
            {
                replyItem.Tokens = reply.Text.Length / 3 + MessageOverheadTokens;
            }
            try
            {
                if (_conversationId == null)
                {
                    string title = !string.IsNullOrWhiteSpace(userItem.Text) ? userItem.Text
                        : userItem.Attachments.Any(a => a.Kind == "image") ? "Image" : "Voice message";
                    _conversationId = await _store.CreateConversationAsync(title, SelectedChoice.ToString());
                }
                int id = _conversationId.Value;
                userItem.MessageId = await _store.AddMessageAsync(id, MessageRecord.UserRole, userItem.Text, null, userItem.Attachments, userItem.Context);
                foreach (string step in _sendSteps)
                    await _store.AddMessageAsync(id, MessageRecord.StepRole, step, null);
                // Pictures the reply drew are kept with it (shown again when the chat is reopened, not given to the model).
                List<(string Kind, string MediaType, byte[] Data)> drawn;
                lock (_sendImages)
                    drawn = _sendImages.Select(png => (GeneratedKind, "image/png", png)).ToList();
                replyItem.MessageId = await _store.AddMessageAsync(id, MessageRecord.AssistantRole, reply.Text, reply.Thinking, drawn);
                // The document the chat uses now (or none), restored when it is opened again.
                await _store.SetConversationDocumentAsync(id, _document?.Index != null ? _document.Id : null);
                UpdateExportChatButton();
                await RefreshChatListAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Could not save the chat: " + ex.Message);
            }
        }

        // The conversation as the model is given it: the summary of the earlier messages (if any), then the messages
        // from _contextStart on.
        private List<HistoryItem> ContextItems()
        {
            List<HistoryItem> items = new List<HistoryItem>();
            if (_summary != null)
            {
                items.Add(new HistoryItem { Role = MessageRecord.UserRole, Text = SummaryIntro + _summary });
                items.Add(new HistoryItem { Role = MessageRecord.AssistantRole, Text = SummaryAcknowledgement });
            }
            items.AddRange(_historyItems.Skip(_contextStart));
            return items;
        }

        private static List<AIChatMessage> ToAgentMessages(List<HistoryItem> items)
        {
            List<AIChatMessage> messages = new List<AIChatMessage>();
            foreach (HistoryItem item in items)
            {
                List<AIContent> contents = new List<AIContent>();
                foreach (var attachment in item.Attachments)
                    if (attachment.Data != null)
                        contents.Add(new DataContent(attachment.Data, attachment.MediaType));
                contents.Add(new TextContent(item.ModelText ?? string.Empty));
                messages.Add(new AIChatMessage(item.Role == MessageRecord.UserRole ? ChatRole.User : ChatRole.Assistant, contents));
            }
            return messages.Count > 0 ? messages : null;
        }

        private static List<Emgu.LiteRT.LM.Models.ChatMessage> ToChatMessages(List<HistoryItem> items)
        {
            List<Emgu.LiteRT.LM.Models.ChatMessage> messages = new List<Emgu.LiteRT.LM.Models.ChatMessage>();
            foreach (HistoryItem item in items)
            {
                List<ChatAttachment> attachments = new List<ChatAttachment>();
                foreach (var attachment in item.Attachments)
                    if (attachment.Data != null)
                        attachments.Add(attachment.Kind == "image" ? ChatAttachment.Image(attachment.Data) : ChatAttachment.Audio(attachment.Data));
                messages.Add(new Emgu.LiteRT.LM.Models.ChatMessage(item.Role, item.ModelText ?? string.Empty, attachments));
            }
            return messages;
        }

        private async void OnHistoryTapped()
        {
            if (!CanChangeModel)
                return;
            List<ConversationRecord> chats = await _store.GetConversationsAsync();
            string action = await _sheet.ShowViewAsync("Chats", BuildChatList(chats, true), "Close");
            await HandleChatListActionAsync(action, true);
        }

        private async Task HandleChatListActionAsync(string action, bool reopenSheet)
        {
            if (string.IsNullOrEmpty(action))
                return;
            int id = int.Parse(action.Substring(action.IndexOf(':') + 1));
            if (action.StartsWith("export:", StringComparison.Ordinal))
            {
                await ExportChatAsync(id);
                return;
            }
            if (action.StartsWith("delete:"))
            {
                // A deleted chat can't be restored, and the cross is next to the row that opens it: ask first.
                string title = (await _store.GetConversationsAsync()).FirstOrDefault(c => c.Id == id)?.Title;
                bool confirmed = await DisplayAlertAsync("Delete this chat?",
                    string.Format("\"{0}\" and its photos and recordings will be removed from this device.", title),
                    "Delete", "Cancel");
                if (!confirmed)
                {
                    if (reopenSheet)
                        OnHistoryTapped();
                    return;
                }
                await _store.DeleteConversationAsync(id);
                if (_conversationId == id)
                {
                    StartNewChat();
                    RemoveDocument();
                }
                // Its document, unless another chat (or the one open now) uses it.
                await _store.DeleteUnusedDocumentsAsync(_document?.Id);
                await RefreshChatListAsync();
                if (reopenSheet)
                    OnHistoryTapped();
            }
            else
            {
                await OpenConversationAsync(id);
            }
        }

        // The saved chats: title, model and date; tapping opens one, the cross deletes it.
        private View BuildChatList(List<ConversationRecord> chats, bool inSheet)
        {
            var list = new VerticalStackLayout { Spacing = 2 };
            if (chats.Count == 0)
            {
                list.Children.Add(new Label
                {
                    Text = "No saved chats yet - they appear here after the first reply.",
                    FontFamily = Theme.BodyFont,
                    FontSize = 13,
                    TextColor = Theme.SecondaryText,
                    Margin = new Thickness(10, 6)
                });
                return list;
            }
            foreach (ConversationRecord chat in chats)
            {
                int id = chat.Id;
                string modelName = Models.FirstOrDefault(m => m.Choice.ToString() == chat.Model)?.Name ?? chat.Model;
                var row = new Grid
                {
                    Padding = new Thickness(10, 7),
                    ColumnSpacing = 6,
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }
                };
                var details = new VerticalStackLayout
                {
                    Children =
                    {
                        new Label { Text = chat.Title, FontFamily = Theme.TitleFont, FontSize = 14, TextColor = Theme.PrimaryText, LineBreakMode = LineBreakMode.TailTruncation },
                        new Label { Text = modelName + " \u00B7 " + chat.Updated.ToLocalTime().ToString("g"), FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText }
                    }
                };
                row.Add(details, 0, 0);
                var export = new Button { Text = "Export", FontFamily = Theme.BodyFont, FontSize = 12,
                    Padding = new Thickness(6), MinimumHeightRequest = 44, VerticalOptions = LayoutOptions.Center,
                    BackgroundColor = Colors.Transparent, TextColor = Theme.Accent };
                SemanticProperties.SetDescription(export, "Export chat " + chat.Title + " with attachments");
                export.Clicked += (_, _) =>
                {
                    if (inSheet) _sheet.Dismiss("export:" + id);
                    else if (CanChangeModel) _ = HandleChatListActionAsync("export:" + id, false);
                };
                row.Add(export, 1, 0);
                var delete = new Label
                {
                    Text = Theme.GlyphClose,
                    FontFamily = Theme.IconFont,
                    FontSize = 18,
                    TextColor = Theme.SecondaryText,
                    Padding = new Thickness(6),
                    VerticalOptions = LayoutOptions.Center
                };
                SemanticProperties.SetDescription(delete, "Delete this chat");
                row.Add(delete, 2, 0);
                var cell = new Border
                {
                    BackgroundColor = id == _conversationId ? Theme.TileBackground : Colors.Transparent,
                    Stroke = Colors.Transparent,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Content = row
                };
                delete.OnTap(() =>
                {
                    if (inSheet)
                        _sheet.Dismiss("delete:" + id);
                    else if (CanChangeModel)
                        _ = HandleChatListActionAsync("delete:" + id, false);
                });
                details.OnTap(() =>
                {
                    if (inSheet)
                        _sheet.Dismiss("open:" + id);
                    else if (CanChangeModel)
                        _ = HandleChatListActionAsync("open:" + id, false);
                });
                list.Children.Add(cell);
            }
            return list;
        }

        private async Task RefreshChatListAsync()
        {
            if (_wide != true)
                return;
            try
            {
                List<ConversationRecord> chats = await _store.GetConversationsAsync();
                _sideChatList.Children.Clear();
                _sideChatList.Children.Add(BuildChatList(chats, false));
            }
            catch (Exception)
            {
            }
        }

        // Open a saved chat: switch to its model, show its messages, and give the model the conversation so far.
        private async Task OpenConversationAsync(int id)
        {
            if (!CanChangeModel)
                return;
            ConversationRecord chat = (await _store.GetConversationsAsync()).FirstOrDefault(c => c.Id == id);
            if (chat == null)
                return;
            UiStallWatchdog.Mark("Open saved chat " + id);
            int index = Array.FindIndex(Models, m => m.Choice.ToString() == chat.Model);
            if (index >= 0 && index != _selectedModel)
                SelectModel(index);
            StartNewChat();
            // The document belongs to the chat it was attached to (what the model was given of it is saved with it).
            RemoveDocument();
            _conversationId = id;
            UpdateExportChatButton();
            _summary = chat.Summary;
            bool contextStartFound = false;

            foreach (MessageRecord message in await _store.GetMessagesAsync(id))
            {
                // The model sees the messages after the last compaction (and the summary of those before).
                if (!contextStartFound && message.Id > chat.SummaryUpTo && message.Role != MessageRecord.StepRole)
                {
                    contextStartFound = true;
                    _contextStart = _historyItems.Count;
                    if (chat.SummaryUpTo > 0)
                        AddCompactionDivider(_transcript.Children.Count, chat.Summary);
                }
                if (message.Role == MessageRecord.StepRole)
                {
                    AddSavedStep(message.Text);
                    continue;
                }
                var item = new HistoryItem { Role = message.Role, Text = message.Text, Context = message.Context, MessageId = message.Id };
                List<PendingAttachment> shown = new List<PendingAttachment>();
                foreach (SavedAttachment attachment in _store.GetAttachments(message))
                {
                    byte[] data = await System.IO.File.ReadAllBytesAsync(attachment.Path);
                    if (attachment.Kind == GeneratedKind)
                    {
                        // A picture the reply drew: shown above it, not part of what the model is given.
                        AddSavedGeneratedImage(data);
                        continue;
                    }
                    item.Attachments.Add((attachment.Kind, attachment.MediaType, data));
                    shown.Add(new PendingAttachment
                    {
                        Image = attachment.Kind == "image" ? data : null,
                        Data = data,
                        MediaType = attachment.MediaType,
                        Name = System.IO.Path.GetFileName(attachment.Path)
                    });
                }
                _historyItems.Add(item);
                if (message.Role == MessageRecord.UserRole)
                {
                    int viewIndex = _transcript.Children.Contains(_emptyLabel) ? 0 : _transcript.Children.Count;
                    AddUserMessage(message.Text, shown);
                    if (viewIndex < _transcript.Children.Count)
                        item.View = _transcript.Children[viewIndex] as View;
                }
                else
                {
                    StreamingBubble bubble = AddStreamingBubble();
                    UpdateStreamingBubble(bubble, new ChatReply(string.IsNullOrEmpty(message.Thinking)
                        ? message.Text : "<think>" + message.Thinking + "</think>" + message.Text), true);
                }
            }

            if (!contextStartFound)
            {
                _contextStart = _historyItems.Count;
                if (chat.SummaryUpTo > 0)
                    AddCompactionDivider(_transcript.Children.Count, chat.Summary);
            }
            // The document the chat used, from its saved index.
            if (!string.IsNullOrEmpty(chat.DocumentId))
                _ = RestoreDocumentAsync(chat.DocumentId);
            // The model is given the conversation with the next message, once its size is known (compacting it first if
            // it doesn't fit, e.g. a long chat saved before compaction existed).
            _needsPrime = true;
            UpdateMemoryLabel();
            await RefreshChatListAsync();
            ScrollToEnd();
        }

        private void AddSavedStep(string text)
        {
            if (_transcript.Children.Contains(_emptyLabel))
                _transcript.Children.Remove(_emptyLabel);
            _transcript.Children.Add(new Label
            {
                Text = "\U0001F50E " + text,
                FontFamily = Theme.BodyFont,
                FontSize = 13,
                TextColor = Theme.SecondaryText,
                Margin = new Thickness(6, 0)
            });
        }

        // Wait for any in-flight generation before freeing the chat/engine: Chat.Send and the engine it
        // runs on are native objects a background Task.Run is still reading, and disposing them out from
        // under it is a hard crash, not a catchable exception.
        private async Task CleanupModelAsync()
        {
            UiStallWatchdog.Mark("Releasing model " + (_model == null ? "(none)" : _model.GetType().Name));
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
            // Freeing an engine can take a moment: not on the UI thread.
            await Task.Run(() =>
            {
                chat?.Dispose();
                model?.Dispose();
            });
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            if (!Navigation.NavigationStack.Contains(this) && !Navigation.ModalStack.Contains(this))
            {
                StopSpeaking();
                _loadCancel?.Cancel();
                _ = CancelRecordingAsync();
                RemoveDocument();
                _photoIndexing?.Cancel();
                _chatIndexing?.Cancel();
                _chatExportCancel?.Cancel();
                _searchRestoreCancel?.Cancel();
                _ = CleanupAfterCpuReloadAsync();
            }
        }

        // ---------- Transcript ----------

        private void ClearTranscript()
        {
            _transcript.Children.Clear();
            _transcript.Children.Add(_emptyLabel);
        }

        // The views of a model bubble that is still streaming, so each chunk can update it in place
        // instead of adding a new bubble per chunk. The thinking takes little room: while the model thinks, a
        // "Thinking..." header and the last few lines of it; once the answer starts, only a "Thought for N s" header,
        // which expands the whole thinking when tapped.
        private sealed class StreamingBubble
        {
            public Grid ThinkingHeader;
            public ActivityIndicator ThinkingSpinner;
            public Label ThinkingTitle;
            public Image ThinkingChevron;
            // The last few lines while the model thinks (plain text, maths converted - a tail cut out of a code block
            // or list can't be laid out), and the whole thinking, formatted like the answer, when expanded.
            public Label ThinkingLabel;
            public ReplyBody ThinkingBody;
            public BoxView Separator;
            // The answer, with code blocks shown as code (ReplyBody).
            public ReplyBody Body;
            // The bubble in the transcript, so tool steps can be inserted above it.
            public View Row;
            // The last thinking / answer shown, whether the whole thinking is shown, and how long the thinking took
            // (from its first text to the answer's; null if unknown, e.g. for a reopened chat).
            public ChatReply Reply;
            public bool Expanded;
            public bool Done;
            public DateTime? ThinkingStarted;
            public TimeSpan? ThinkingTime;
        }

        // While thinking, this much of the end of the thinking is shown (about three lines).
        private const int ThinkingPreviewLength = 180;

        private StreamingBubble AddStreamingBubble()
        {
            var bubbleViews = new StreamingBubble
            {
                ThinkingSpinner = new ActivityIndicator { IsRunning = false, IsVisible = false, Color = Theme.SecondaryText, WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center },
                ThinkingTitle = new Label { FontFamily = Theme.TitleFont, FontSize = 13, TextColor = ThinkingTextColor, VerticalOptions = LayoutOptions.Center },
                ThinkingChevron = Theme.MakeIcon(Theme.GlyphChevronRight, Theme.SecondaryText, 16),
                ThinkingLabel = new Label { FontFamily = Theme.BodyFont, TextColor = ThinkingTextColor, FontSize = 13, IsVisible = false },
                ThinkingBody = new ReplyBody(ThinkingTextColor, 13) { IsVisible = false },
                Separator = new BoxView { HeightRequest = 1, Color = Theme.Chevron, IsVisible = false },
                Body = new ReplyBody(ModelTextColor)
            };
            var header = new Grid
            {
                ColumnSpacing = 6,
                IsVisible = false,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }
            };
            header.Add(bubbleViews.ThinkingSpinner, 0, 0);
            header.Add(bubbleViews.ThinkingTitle, 1, 0);
            header.Add(bubbleViews.ThinkingChevron, 2, 0);
            header.OnTap(() =>
            {
                if (!bubbleViews.ThinkingChevron.IsVisible)
                    return;
                bubbleViews.Expanded = !bubbleViews.Expanded;
                ShowThinking(bubbleViews);
            });
            bubbleViews.ThinkingHeader = header;

            Border bubble = ModelBubble(new VerticalStackLayout
            {
                Spacing = 4,
                Children = { header, bubbleViews.ThinkingLabel, bubbleViews.ThinkingBody, bubbleViews.Separator, bubbleViews.Body }
            });
            AddBubbleRow(bubble, false);
            bubbleViews.Row = bubble;
            return bubbleViews;
        }

        // Called on the UI thread with the thinking/answer split of everything streamed so far, or with the finished
        // reply (done; also for the replies of a reopened chat).
        private void UpdateStreamingBubble(StreamingBubble bubble, ChatReply partial, bool done = false)
        {
            if (!string.IsNullOrEmpty(partial.Thinking) && bubble.ThinkingStarted == null && !done)
                bubble.ThinkingStarted = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(partial.Text) && bubble.ThinkingStarted != null && bubble.ThinkingTime == null)
                bubble.ThinkingTime = DateTime.UtcNow - bubble.ThinkingStarted.Value;
            bubble.Reply = partial;
            bubble.Done = done;
            ShowThinking(bubble);
            bubble.Body.SetText(partial.Text);
            // A reply with code may use the full width, so less of the code wraps.
            if (bubble.Body.HasCode && bubble.Row.MaximumWidthRequest < double.PositiveInfinity)
                bubble.Row.MaximumWidthRequest = double.PositiveInfinity;
            ScrollToEnd();
        }

        private void ShowThinking(StreamingBubble bubble)
        {
            string thinking = bubble.Reply?.Thinking;
            string answer = bubble.Reply?.Text;
            bool hasThinking = !string.IsNullOrEmpty(thinking);
            bool hasAnswer = !string.IsNullOrEmpty(answer);
            bubble.ThinkingHeader.IsVisible = hasThinking;
            if (!hasThinking)
            {
                bubble.ThinkingLabel.IsVisible = false;
                bubble.ThinkingBody.IsVisible = false;
                bubble.Separator.IsVisible = false;
                return;
            }
            // Still thinking: a spinner and the end of the thinking so far.
            bool thinkingNow = !hasAnswer && !bubble.Done;
            bubble.ThinkingSpinner.IsVisible = bubble.ThinkingSpinner.IsRunning = thinkingNow;
            bubble.ThinkingChevron.IsVisible = !thinkingNow;
            if (thinkingNow)
            {
                bubble.ThinkingTitle.Text = "Thinking...";
                bubble.ThinkingLabel.IsVisible = true;
                bubble.ThinkingLabel.Text = ThinkingPreview(ReplyBody.WithMath(thinking));
                bubble.ThinkingBody.IsVisible = false;
                bubble.Separator.IsVisible = false;
                return;
            }
            // A reply that ended without an answer (e.g. cut off while thinking) shows its thinking.
            bool expanded = bubble.Expanded || !hasAnswer;
            bubble.ThinkingTitle.Text = bubble.ThinkingTime != null
                ? string.Format("Thought for {0:N0} s", Math.Max(1, bubble.ThinkingTime.Value.TotalSeconds))
                : "Thoughts";
            bubble.ThinkingChevron.Source = Theme.MakeGlyphSource(expanded ? Theme.GlyphExpandMore : Theme.GlyphChevronRight, Theme.SecondaryText, 16);
            SemanticProperties.SetDescription(bubble.ThinkingHeader, bubble.ThinkingTitle.Text + (expanded ? ", shown. Tap to hide." : ", hidden. Tap to show."));
            bubble.ThinkingLabel.IsVisible = false;
            bubble.ThinkingBody.IsVisible = expanded;
            if (expanded)
                bubble.ThinkingBody.SetText(thinking.Trim());
            // A reply with code in its thinking may use the full width too.
            if (expanded && bubble.ThinkingBody.HasCode && bubble.Row.MaximumWidthRequest < double.PositiveInfinity)
                bubble.Row.MaximumWidthRequest = double.PositiveInfinity;
            bubble.Separator.IsVisible = expanded && hasAnswer;
        }

        // The end of the thinking so far, from a word boundary, without the Markdown markers (code fences, **, `, #) a
        // tail cut out of formatted text can't be laid out with.
        private static string ThinkingPreview(string thinking)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(thinking.Replace("\r", ""), @"^\s*```.*$\n?", string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*|__|`|^#{1,6}\s+", string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline).Trim();
            if (text.Length <= ThinkingPreviewLength)
                return text;
            string tail = text.Substring(text.Length - ThinkingPreviewLength);
            int space = tail.IndexOfAny(new[] { ' ', '\n' });
            if (space > 0 && space < 40)
                tail = tail.Substring(space + 1);
            return "\u2026" + tail;
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
            UpdateExportChatButton();
            UpdateModelDownloadRows();
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
