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
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Emgu.LiteRT.LM.Chat.Sqlite;

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
            Gemma4_12B,
            // A model on the user's own server (LiteRtLmChatPage.Server.cs).
            Server
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
            // The context window Automatic picks, or 0 for LanguageModel's default (4096). Gemma 4's files allow up to
            // ~32k, but the model re-reads the whole conversation when a saved chat is opened or after compacting, which
            // grows with the window: a full 4k chat took 2 minutes on a Pixel Fold's CPU (8k: almost 6, with 1.5 GB of
            // its RAM left; on its GPU 8 s at 4k), 8k 27 s (E2B) / 61 s (E4B) on an Apple Silicon Mac. Phones use it too
            // for models with tools: the agent's instructions and tool definitions alone grew to ~4k tokens (goals
            // pushed them past a 4k window, so every message failed).
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
        private readonly Button _sideModelHeader;
        private bool _modelsExpanded = true;
        private const string ModelsExpandedPreference = "litert_lm_chat_models_expanded";
        private const string CpuModelsExpandedPreference = "litert_lm_chat_cpu_models_expanded";
        private const string GpuModelsExpandedPreference = "litert_lm_chat_gpu_models_expanded";
        private readonly VerticalStackLayout _sideSettingsHost;
        private readonly VerticalStackLayout _settingsView;
        private readonly Border _modelChip;
        private readonly Label _modelChipLabel;
        private readonly Label _titleLabel;
        private readonly Border _settingsButton;
        private readonly Border _newChatButton;
        private readonly HorizontalStackLayout _toggleRow;
        private readonly ToggleChip _thinkingSwitch;
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

        // Web search: an agent (Microsoft Agent Framework) over the model's IChatClient, with the tools of ChatAgentTools.
        private const string TavilyKeyStorageName = "tavily_api_key";
        private const string AgentInstructions = ChatPlannerInstructions.Agent;
        private readonly ToggleChip _webSearchSwitch;
        // Task mode (LiteRtLmChatPage.Task.cs): the next message is planned and run step by step; off again after it is sent.
        private readonly ToggleChip _taskChip;
        private readonly Label _webSearchNotice;
        private readonly Entry _tavilyKeyEntry;
        private readonly Label _tavilyKeyStatus;
        private readonly Button _saveTavilyKeyButton;
        private readonly SemaphoreSlim _tavilyKeySaveGate = new SemaphoreSlim(1, 1);
        private string _savedTavilyKey;
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
        // The model's (or the server's) client with function invocation, which _conversation reaches through
        // _clientProxy; made again for another model or backend, while the conversation goes on.
        private IChatClient _chatClient;

        private LanguageModel _model;
        private ModelChoice? _loaded;
        // GPU (Android): whether the loaded model runs on it, whether the one loading will, and whether it failed to
        // load there (then the CPU is used for the rest of the session).
        private const string UseGpuPreference = "litert_lm_chat_use_gpu";
        // Settings > "Voice language": a BCP-47 tag, or "" for the device's language.
        private const string VoiceLanguagePreference = "litert_lm_chat_voice_language";
        private bool _selectedGpu;
        private static string BackendPreference => UseGpuPreference + "_" + DeviceInfo.Platform;
        private bool _loadedOnGpu;
        private bool _loadingOnGpu;
        // Context window (Settings, Gemma 4): Automatic (0: the model's DesktopContextTokens, 8k) or a size; the loaded model's
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

        // Saved chats (ChatStore, SQLite): the current chat's id once its first exchange is saved.
        private const string HistoryIconPath = "M13 3c-4.97 0-9 4.03-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42C8.27 19.99 10.51 21 13 21c4.97 0 9-4.03 9-9s-4.03-9-9-9zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z";
        private readonly ChatStore _store = new ChatStore();
        private int? _conversationId;
        private readonly Border _historyButton;
        private readonly VerticalStackLayout _sideChatList;

        // The conversation (Emgu.LiteRT.LM.Chat's ChatConversation): its messages - shown by _chatView, Emgu.LiteRT.LM.Maui's
        // ChatView with the demo's templates (ChatTemplates.xaml) - streaming the replies, and keeping what the model is
        // given within its context window: before a message that would make it too long, the earlier messages are
        // summarized by the model (CanSummarize) or dropped, and the model continues from the summary and the latest
        // messages, while the transcript keeps everything. Sizes are estimates (the model's tokenizer, plus measured costs
        // of images, audio and tool calls - the conversation's defaults).
        private readonly ChatView _chatView;
        private readonly PageChatClient _clientProxy;
        private readonly ChatConversation _conversation;
        // The model reads the whole conversation with the next message (a reopened chat, after compacting, or a model
        // loaded again): the status says so.
        private bool _rereadNext;

        // The page's own data for a user message, in its ChatMessageItem.Tag (the demo's message template binds to it).
        private sealed class UserMessageInfo : System.ComponentModel.INotifyPropertyChanged
        {
            private string _transcript;
            private bool _transcribing;
            private string _dividerText;

            // Its language (two-letter code), or null (LiteRtLmChatPage.Translation.cs).
            public string Language;
            // Its id in the ChatStore (0 if not saved).
            public int MessageId;
            // The task writing down the words of a voice message (LiteRtLmChatPage.Transcript.cs).
            public Task TranscriptTask;

            // The words of a voice message the model was given as audio, written down by the platform's speech recognizer
            // for the transcript (not given to the model).
            public string Transcript
            {
                get => _transcript;
                set { _transcript = value; Raise(nameof(TranscriptText)); Raise(nameof(HasTranscript)); }
            }

            public bool Transcribing
            {
                get => _transcribing;
                set { _transcribing = value; Raise(nameof(TranscriptText)); Raise(nameof(HasTranscript)); }
            }

            public string TranscriptText => !string.IsNullOrWhiteSpace(_transcript) ? "\u201C" + _transcript.Trim() + "\u201D"
                : _transcribing ? "Writing down what you said..." : string.Empty;

            public bool HasTranscript => !string.IsNullOrWhiteSpace(_transcript) || _transcribing;

            // Above the first message the model still sees after compacting: whether the earlier ones were summarized.
            public string DividerText
            {
                get => _dividerText ?? "Earlier messages summarized \u00B7 tap to read";
                set { _dividerText = value; Raise(nameof(DividerText)); }
            }

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

            private void Raise(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        // The conversation's client: forwards to the current model's or server's client (_chatClient), which is made again
        // when the model is reloaded or changed - the conversation and its messages stay.
        private sealed class PageChatClient : IChatClient
        {
            private readonly Func<IChatClient> _current;

            public PageChatClient(Func<IChatClient> current)
            {
                _current = current;
            }

            public Task<ChatResponse> GetResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions options = null,
                CancellationToken cancellationToken = default)
                => _current().GetResponseAsync(messages, options, cancellationToken);

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AIChatMessage> messages,
                ChatOptions options = null, CancellationToken cancellationToken = default)
                => _current().GetStreamingResponseAsync(messages, options, cancellationToken);

            public object GetService(Type serviceType, object serviceKey = null) => null;

            public void Dispose()
            {
            }
        }

        // The messages of the chat (not the page's notices).
        private IEnumerable<ChatMessageItem> ChatMessages => _conversation.Messages.Where(m => m.Role != ChatMessageRole.Notice);

        // Room for what the model's conversation holds besides the messages: the chat template, or the agent's
        // instructions, tool definitions and memories (LiteRtLmChatClient budgets the real sizes when it sends).
        private const int ChatOverheadTokens = 50;
        private const int AgentOverheadTokens = 900;
        private const int SummaryMaxTokens = 400;
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
            _privateChatBadge = new Label { Text = "Private · Not saved", FontFamily = Theme.TitleFont, FontSize = 12,
                TextColor = Theme.Accent, IsVisible = false };
            SemanticProperties.SetDescription(_privateChatBadge, "Private chat. Messages are not saved. Memories and past-chat search are unavailable.");
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
            header.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center,
                Children = { new Grid { Children = { _modelChip, _titleLabel } }, _privateChatBadge } }, 1, 0);
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
            _taskChip = new ToggleChip("\U0001F4CB Plan");
            SemanticProperties.SetDescription(_taskChip, "Plan the next message as a task and run it step by step");
            _toggleRow = new HorizontalStackLayout { Spacing = 8, Children = { _thinkingSwitch, _webSearchSwitch, _taskChip, CreateVoiceChip() } };
            _webSearchNotice = new Label
            {
                Text = "Web search requires a Tavily API key. Add one in Settings > Web search. Without a key, Wikipedia, links you provide, weather and currency tools are still available.",
                FontFamily = Theme.BodyFont,
                FontSize = 12,
                TextColor = Theme.SecondaryText,
                IsVisible = false
            };

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
            // The language voice messages are recognized in (Emgu.LiteRT.LM.Maui's SpeechRecognizer): the device's
            // language unless chosen.
            string voiceLanguage = string.Empty;
            try { voiceLanguage = Preferences.Default.Get(VoiceLanguagePreference, string.Empty); } catch (Exception) { }
            SpeechRecognizer.Language = voiceLanguage;
            SpeechRecognizer.ChooseLanguageHint = "Choose the language in Settings > Voice language.";
            var voiceLanguagePicker = new Picker
            {
                Title = "Voice language",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                TitleColor = Theme.SecondaryText
            };
            voiceLanguagePicker.Items.Add("Device language");
            foreach (var language in VoiceLanguages.All)
                voiceLanguagePicker.Items.Add(language.Name);
            voiceLanguagePicker.SelectedIndex = Math.Max(0, VoiceLanguages.All.ToList().FindIndex(l => string.Equals(l.Tag, voiceLanguage, StringComparison.OrdinalIgnoreCase)) + 1);
            voiceLanguagePicker.SelectedIndexChanged += (s, e) =>
            {
                string tag = voiceLanguagePicker.SelectedIndex <= 0 ? string.Empty : VoiceLanguages.All[voiceLanguagePicker.SelectedIndex - 1].Tag;
                try { Preferences.Default.Set(VoiceLanguagePreference, tag); } catch (Exception) { }
                SpeechRecognizer.Language = tag;
            };
            SemanticProperties.SetDescription(voiceLanguagePicker, "The language you speak in voice messages");
            var voiceLanguageRow = new VerticalStackLayout
            {
                Spacing = 2,
                IsVisible = SpeechRecognizer.IsSupported,
                Children =
                {
                    new Label { Text = "Voice language", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText },
                    voiceLanguagePicker,
                    new Label
                    {
                        Text = "The language of your voice messages, for the speech recognizer that writes them down - the words models that can't hear audio (Qwen, your own server) are given, and the transcript under a voice message. By default it listens in the device's language, so choose yours when it differs. The phone may need to download that language for offline use.",
                        FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText
                    }
                }
            };
            _tavilyKeyEntry = new Entry
            {
                Placeholder = "Tavily API key (required for web search)",
                IsEnabled = false,
                IsPassword = true,
                FontFamily = Theme.BodyFont,
                FontSize = 14,
                TextColor = Theme.PrimaryText,
                PlaceholderColor = Theme.SecondaryText
            };
            _tavilyKeyStatus = new Label
            {
                Text = "Loading saved API key...",
                FontFamily = Theme.BodyFont,
                FontSize = 12,
                TextColor = Theme.SecondaryText
            };
            _saveTavilyKeyButton = new Button { Text = "Save API key", IsEnabled = false };
            _saveTavilyKeyButton.Clicked += (s, e) => OnTavilyKeyChanged();
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
                    voiceLanguageRow,
                    Theme.Divider(),
                    new Label { Text = "Web search", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    new Label
                    {
                        Text = "Searching the web for news and recent events requires a Tavily API key from tavily.com. With \U0001F50E Web search on, Wikipedia, reading links you provide, weather and currency conversion work without a key. Your searches leave the device.",
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _tavilyKeyEntry,
                    _saveTavilyKeyButton,
                    _tavilyKeyStatus,
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
#if ANDROID
                    BuildCameraSoundSection(),
                    Theme.Divider(),
#endif
                    BuildAlertSoundsSection(),
                    Theme.Divider(),
                    BuildServerSection(),
                    Theme.Divider(),
                    BuildMcpSection(),
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
                    BuildPrivateChatSection(),
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
                    BuildGoalsSection(),
                    Theme.Divider(),
                    new Label { Text = "Conversation memory", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText, Margin = new Thickness(0, 6, 0, 0) },
                    _memoryLabel,
                    _compactButton,
                    _contextSection,
                    Theme.Divider(),
                    BuildDeveloperSection()
                }
            };

            // The transcript: the conversation's messages in Emgu.LiteRT.LM.Maui's ChatView, with the demo's templates
            // (ChatTemplates.xaml: its ChatViewTemplate has only the transcript - the page has its own message box).
            _clientProxy = new PageChatClient(() => EnsureClient());
            _conversation = new ChatConversation(_clientProxy)
            {
                DescribeToolCall = call => McpStepFor(call) ?? AgentTools.Describe(call)
            };
            _conversation.Messages.CollectionChanged += OnConversationMessagesChanged;
            _conversation.Compacted += OnConversationCompacted;
            _conversation.Summarizing += (s, summarizing) => SetStatus(summarizing ? "Summarizing the earlier conversation..." : "Generating...");
            _chatView = new ChatView { Conversation = _conversation };

            _statusLabel = new Label
            {
                IsVisible = false,
                Padding = new Thickness(4, 2),
                TextColor = Theme.Accent,
                FontFamily = Theme.BodyFont,
                FontSize = 14
            };

#if MACCATALYST
            _promptEditor = new ChatPromptEditor
#else
            _promptEditor = new Editor
#endif
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
#elif MACCATALYST
            SemanticProperties.SetHint(_promptEditor, "Return sends your message. Shift+Return or Option+Return adds a new line.");
            ((ChatPromptEditor)_promptEditor).SendRequested += OnSendClicked;
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

            var composerCard = Theme.Card(new VerticalStackLayout { Spacing = 8, Children = { _toggleRow, _webSearchNotice, composer } }, 10);

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
            _mainColumn.Add(new Grid { Children = { _chatView, _loadCard } }, 0, 1);
            _mainColumn.Add(_statusLabel, 0, 2);
            _mainColumn.Add(composerCard, 0, 3);
            BuildCameraPanel();

            // ---- Side panel (wide screens): the model list and the settings ----
            _sideChatList = new VerticalStackLayout { Spacing = 2 };
            _sideModelList = new VerticalStackLayout { Spacing = 4 };
            try
            {
                _modelsExpanded = Preferences.Default.Get(ModelsExpandedPreference, true);
                _cpuModelsExpanded = ReadModelGroupPreference(CpuModelsExpandedPreference);
                _gpuModelsExpanded = ReadModelGroupPreference(GpuModelsExpandedPreference);
            }
            catch (Exception) { }
            _sideModelHeader = new Button
            {
                FontFamily = Theme.TitleFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                BackgroundColor = Colors.Transparent,
                HorizontalOptions = LayoutOptions.Fill,
                MinimumHeightRequest = 44,
                Padding = new Thickness(0, 8)
            };
            _sideModelHeader.Clicked += (_, _) =>
            {
                _modelsExpanded = !_modelsExpanded;
                try { Preferences.Default.Set(ModelsExpandedPreference, _modelsExpanded); } catch (Exception) { }
                UpdateModelSectionHeader();
            };
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
                            _sideModelHeader,
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
            Content = new Grid { Children = { _pageGrid, BuildVoiceView(), _sheet } };

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

        private static bool? ReadModelGroupPreference(string key) =>
            Preferences.Default.ContainsKey(key) ? Preferences.Default.Get(key, false) : null;

        private void UpdateModelSectionHeader()
        {
            string selected = SelectedOption.Name + (_selectedGpu ? " · GPU" : " · CPU");
            _sideModelList.IsVisible = _modelsExpanded;
            _sideModelHeader.Text = _modelsExpanded ? "▼ Model" : "▶ Model · " + selected;
            SemanticProperties.SetDescription(_sideModelHeader,
                "Model, " + (_modelsExpanded ? "expanded" : "collapsed") + ", selected " + selected);
            SemanticProperties.SetHint(_sideModelHeader, _modelsExpanded ? "Collapse model section" : "Expand model section");
        }

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
                    try { Preferences.Default.Set(gpu ? GpuModelsExpandedPreference : CpuModelsExpandedPreference, models.IsVisible); } catch (Exception) { }
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
                    var statusIcon = Theme.MakePathIcon(Theme.PathCloudDownload, Theme.SecondaryText, 14);
                    var downloadRow = new ModelDownloadRow { Option = option, Detail = detail, Status = status, StatusIcon = statusIcon, Continue = resume, InSheet = inSheet };
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
                            new HorizontalStackLayout { Spacing = 6, Children = { statusIcon, status } },
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
            list.Children.Add(BuildServerModelRow(inSheet));
            return list;
        }

        private void RefreshModelList()
        {
            UpdateModelSectionHeader();
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

        private ModelOption SelectedOption => OptionAt(_selectedModel);

        // Select a model (loaded on the next message) and show the controls it supports.
        private void SelectModel(int index, bool? gpu = null)
        {
            UiStallWatchdog.Mark("Select model " + OptionAt(index).Name);
            // A chat belongs to a model: another model starts a new chat.
            if (_pageStarted && index != _selectedModel)
                StartNewChat();
            _selectedModel = index;
            ModelOption option = SelectedOption;
            _selectedGpu = (gpu ?? _selectedGpu) && HasGpu(option);
            _gpuUnavailable = false;
            _modelChipLabel.Text = option.Choice == ModelChoice.Server ? ServerModelLabel : option.Name + (_selectedGpu ? " · GPU" : " · CPU");
            RefreshModelList();
            _talkButton.IsVisible = TakesVoice(option);
            _thinkingSwitch.IsVisible = option.CanThink;
            _webSearchSwitch.IsVisible = option.CanUseTools;
            _taskChip.IsVisible = option.CanUseTools && !_voiceMode;
            _voiceChip.IsVisible = VoiceModeSupported;
            _toggleRow.IsVisible = option.CanThink || option.CanUseTools || _voiceChip.IsVisible;
            UpdateWebSearchNotice();
            UpdateContextSection();
            // Drop attachments the new model can't take.
            foreach (PendingAttachment attachment in _pending.ToList())
                if (attachment.Image != null ? !option.AcceptsImages : !option.AcceptsAudio)
                    RemovePending(attachment);
            if (_pageStarted)
            {
                // Constructor UI setup must not overwrite the saved backend before OnAppearing restores it.
                // (The server has no backend: the local models keep theirs.)
                if (option.Choice != ModelChoice.Server)
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
            _ = ConnectMcpServersAsync();
            int index = 0;
            try
            {
                string last = Preferences.Default.Get(LastModelPreference, null as string);
                int found = ModelIndexOf(last);
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
            EndVoiceMode();
            _ = CancelRecordingAsync();
            _ = CloseCameraAsync();
        }

        private static bool IsDownloaded(ModelOption option)
        {
            // A file that exists but isn't verified yet (e.g. downloaded by an older version) is checked when loading.
            if (option.File == null)
                return true; // The server: nothing to download.
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
            settings.EnableBenchmark();
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
            if (_model != null || _serverConnected != null || _chatClient != null)
                await CleanupModelAsync();
            if (token.IsCancellationRequested)
                return false;
            if (option.Choice == ModelChoice.Server)
                return await ConnectServerAsync(option, token);

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
                if (!option.File.IsLocalFileVerified)
                    ShowLoadStage(option, ModelLoadStage.Downloading);
                await BackgroundDownloads.DownloadFileAsync(option.File, option.Name,
                    (total, received, percent) => MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!token.IsCancellationRequested) ShowDownloadProgress(total, received);
                    }), token);
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
                if (token.IsCancellationRequested || ex is OperationCanceledException)
                {
                    if (_downloadingChoice == option.Choice) _downloadingChoice = null;
                    ClearLoading(option.Choice);
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
            _loaded = option.Choice;
            _loadedOnGpu = onGpu;
            _loadedContextTokens = contextTokens;
            // Reloaded on another backend in the middle of a chat: a new client for the new engine, which is given the
            // conversation with the next message.
            ResetAgent();
            _rereadNext = ChatMessages.Any();
            if (_gpuUnavailable && WantsGpuSetting(option))
                SetStatus("The GPU isn't available on this device - using the CPU.");
            ClearLoading(option.Choice);
            HideLoadCard();
            UpdateMemoryLabel();
            UiStallWatchdog.Mark("Model ready: " + option.Name);
            return true;
        }

        // The selected model is loaded, on the backend it should run on.
        private bool ModelReady => (SelectedChoice == ModelChoice.Server ? _serverConnected == ServerConnectionKey : _model != null)
            && _loaded == SelectedChoice && _loadedOnGpu == WantsGpu(SelectedOption)
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
        // the model's DesktopContextTokens; on phones only for models with tools, whose instructions and tool
        // definitions need more than 4k - capped at what the model's file allows.
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
            return option.CanUseTools ? option.DesktopContextTokens : 0;
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
            message += " " + BackgroundDownloads.Hint;
            ShowLoadCard(option, message + (wifi ? "" : " You're not on Wi-Fi - mobile data may be used."), null);
            _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
            _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
            SetLoadButtons(paused ? "Continue" : "Download " + option.Size, () => _ = PrepareModelAsync(true), "Not now");
        }

        private void ShowLoadError(ModelOption option, string message)
        {
            if (_downloadingChoice == option.Choice) _downloadingChoice = null;
            UpdateModelDownloadRows();
            bool paused = option.File != null && option.File.PartialDownloadBytes > 0;
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
            ClearCameraTurn();
            lock (_sendImages)
                _sendImages.Clear();
            ClearWrittenDocuments();
            UiStallWatchdog.Mark(string.Format("Send: {0} chars, {1} attachment(s), model {2}, agent {3}, think {4}, web {5}",
                prompt.Length, attachments.Count, SelectedOption.Name, UseAgent, _thinkingSwitch.IsToggled, _webSearchSwitch.IsToggled));
            // With read-aloud on (or in a voice chat) the reply is read while it is written.
            ReplyReader reader = StartReplyReader();
            // Cancelled when the user interrupts the reply (voice chat).
            CancellationTokenSource replyCancel = new CancellationTokenSource();
            _replyCancel = replyCancel;
            SetBusy(true);
            try
            {
                if (!await EnsureModelReadyAsync())
                    return;
                // A model that doesn't take audio is given the words of a voice message instead.
                if (!SelectedOption.AcceptsAudio && attachments.Any(IsVoiceRecording))
                {
                    string heard = await TranscribeVoiceAsync(attachments, replyCancel.Token);
                    if (heard == null)
                        return;
                    prompt = string.IsNullOrWhiteSpace(prompt) ? heard : prompt + " " + heard;
                }

                _promptEditor.Text = string.Empty;
                ClearPendingAttachments();
                // With a document attached: the excerpts (and maybe a picture or page) most related to the message.
                DocumentContext document = await GetDocumentContextAsync(prompt);
                if (document?.Image != null)
                    attachments.Add(document.Image);
                // A voice message the model hears as audio gets its words written down for the transcript (the model doesn't
                // see them), instead of just "Voice message".
                bool transcribe = string.IsNullOrWhiteSpace(prompt) && SpeechRecognizer.IsSupported && attachments.Any(IsVoiceRecording);
                var info = new UserMessageInfo { Language = await TextLanguage.DetectAsync(prompt), Transcribing = transcribe };

                // Task mode: for this message only.
                bool planTask = _taskChip.IsToggled && UseAgent && !_voiceMode;
                _taskChip.IsToggled = false;
                ChatOptions options = await MessageOptionsAsync(prompt, attachments);
                ConfigureConversation();
                // The model is given references to the attached images for the camera tools.
                int attachedImages = attachments.Count(a => a.Image != null);
                string imageReferences = UseAgent && attachedImages > 0 && LiveCamera.Supported
                    ? "Attached image references for analyze_visual_image: " + string.Join(", ", Enumerable.Range(1, attachedImages).Select(i => "image-" + i)) + "\n"
                    : null;
                int compactionsBefore = _conversation.CompactionCount;
                int searchedAt = compactionsBefore;
                ChatMessageItem userItem = null;
                _conversation.BeforeStreaming = async (message, token) =>
                {
                    userItem = message;
                    // Compacting may have summarized or dropped excerpts given before: they are searched again.
                    if (document != null && _conversation.CompactionCount != searchedAt)
                    {
                        searchedAt = _conversation.CompactionCount;
                        document = await RefreshDocumentContextAsync(prompt, document, message, attachments);
                        message.Context = JoinContext(imageReferences, document?.Text);
                    }
                    // Re-reading the earlier conversation (an opened chat, after compacting) is what takes the time.
                    SetStatus(_rereadNext || _conversation.CompactionCount != compactionsBefore ? "Reading the conversation so far..." : "Generating...");
                    _rereadNext = false;
                };
                // The reply's bubble is made when it is added (OnConversationMessagesChanged); these go with it.
                _pendingUserInfo = info;
                _pendingVoice = transcribe ? attachments.First(IsVoiceRecording).Data : null;
                _pendingDocumentStep = document?.Step;
                _pendingReader = reader;

                List<DataContent> contents = attachments.Select(a => new DataContent(a.Data, a.MediaType) { Name = a.Name }).ToList();
                IChatClient client = planTask ? new TaskModeClient(this, prompt, contents) : null;
                ChatMessageItem reply = null;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Task<ChatMessageItem> generation = reply == null
                            ? _conversation.SendAsync(prompt, contents, options, JoinContext(imageReferences, document?.Text), replyCancel.Token, client)
                            : _conversation.RegenerateAsync(reply, options, replyCancel.Token, client);
                        // Kept so teardown (OnNavigatedFrom / switching models) can wait for it instead of freeing the
                        // engine while LiteRT-LM is still generating into it.
                        _generation = generation;
                        reply = await generation;
                    }
                    catch (System.ClientModel.ClientResultException ex) when (attempt == 0 && ServerRejectedReasoning(ex))
                    {
                        // The server doesn't take reasoning_effort: send it again without.
                        UiStallWatchdog.Mark("Server rejected reasoning_effort: " + ex.Message);
                        reply = ChatMessages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);
                        options.Reasoning = null;
                        continue;
                    }
                    if (attempt == 0 && reply.State == ChatMessageState.Done && ToolRouter.TakeRerun() && !replyCancel.IsCancellationRequested)
                    {
                        // A stub ended the request (ToolRouter, rerunAfterStub): the message again, now with its group -
                        // without the stub's turn, which the model handled worse.
                        UiStallWatchdog.Mark("Sending the message again with the tool group: " + string.Join(", ", ToolRouter.Active));
                        options = await MessageOptionsAsync(prompt, attachments);
                        continue;
                    }
                    break;
                }

                bool interrupted = reply.State == ChatMessageState.Interrupted;
                if (interrupted)
                    UiStallWatchdog.Mark("Reply interrupted");
                StreamingBubble bubble = reply.Tag as StreamingBubble;
                if (bubble != null)
                {
                    bubble.TargetLanguage = TranslationTarget(info.Language);
                    bubble.Refresh();
                    _ = CheckThinkingLanguageAsync(bubble);
                }
                MarkDocumentContextGiven(document);
                UiStallWatchdog.Mark(string.Format("Reply done: {0} chars, {1} chars thinking", reply.Text.Length, reply.Thinking.Length));
                // The camera frames the tools took are given to the model with the conversation from now on.
                foreach (byte[] jpeg in _cameraTurnFrames)
                    userItem?.AddAttachment(new DataContent(jpeg, "image/jpeg"));
                ClearCameraTurn();
                if (userItem != null)
                    await SaveExchangeAsync(userItem, reply, bubble);
                SetStatus(null);
                if (!interrupted)
                    reader?.Finish(reply.Text);
            }
            catch (Exception ex)
            {
                reader?.Stop();
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                _conversation.BeforeStreaming = null;
                _pendingUserInfo = null;
                _pendingVoice = null;
                _pendingDocumentStep = null;
                _pendingReader = null;
                // Not read aloud if the model wasn't ready (the reader has nothing to read).
                reader?.Finish(string.Empty);
                if (ReferenceEquals(_replyCancel, replyCancel))
                    _replyCancel = null;
                replyCancel.Dispose();
                ClearCameraTurn();
                SetBusy(false);
                UpdateMemoryLabel();
                // The agent may have saved or deleted a memory or a goal.
                _ = RefreshMemoryCountAsync();
                _ = RefreshGoalCountAsync();
                _ = RefreshServerContextAsync();
            }
        }

        // Given to the model before a message: image references for the tools, then document excerpts; null for none.
        private static string JoinContext(string imageReferences, string document)
        {
            string context = (imageReferences ?? string.Empty) + (document ?? string.Empty);
            return context.Length > 0 ? context : null;
        }

        // The conversation's settings for the selected model: its context window and tokenizer, what the instructions and
        // tools take, and how it compacts (summaries by the model - CanSummarize - or dropping, for the small models).
        private void ConfigureConversation()
        {
            _conversation.CountTokens = text => TokenCounter(text);
            _conversation.ContextWindow = ContextWindow;
            _conversation.OverheadTokens = UseAgent ? AgentOverheadTokens + ToolGroupTokens : ChatOverheadTokens;
            _conversation.Compaction = SelectedOption.CanSummarize ? ChatCompaction.Summarize : ChatCompaction.Drop;
            _conversation.SummaryMaxTokens = SummaryMaxTokens;
            // A server's model may think anyway (LM Studio runs Gemma 4 with thinking on), which used up the summary's tokens.
            _conversation.SummaryOptions = SelectedChoice == ModelChoice.Server
                ? new ChatOptions
                {
                    MaxOutputTokens = SummaryMaxTokens + 1600,
                    Reasoning = _serverRejectsReasoning ? null : new ReasoningOptions { Effort = ReasoningEffort.None }
                }
                : null;
        }

        // The reply being written: its bubble gets the user message's data, the document step and the reader of this send.
        private UserMessageInfo _pendingUserInfo;
        private byte[] _pendingVoice;
        private string _pendingDocumentStep;
        private ReplyReader _pendingReader;

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

        // ---------- Web search (agent) ----------

        // Models that can call tools always chat through the agent, with the tools chosen per message (the date and time,
        // the location if shared, the search tools with the Web search chip on), so changing them keeps the conversation.
        private bool UseAgent => SelectedOption.CanUseTools;

        // The options of a message: for models that use tools (the agent), its tools - the core ones (date and time,
        // calculator, units, memories, the location if the user shares it, the web tools with the Web search chip on), then
        // the groups the conversation needs (LiteRtLmChatPage.Tools.cs), with a stub for each other group - and the
        // instructions; thinking for all (Think chip). A voice chat answers without thinking first: a spoken reply should
        // start quickly. (Sent to a server as reasoning_effort, unless it rejected that - then its model thinks as the
        // server is set up to.)
        private async Task<ChatOptions> MessageOptionsAsync(string prompt, List<PendingAttachment> attachments)
        {
            ChatOptions options = new ChatOptions
            {
                Reasoning = SelectedChoice == ModelChoice.Server && _serverRejectsReasoning ? null
                    : new ReasoningOptions { Effort = _thinkingSwitch.IsToggled && SelectedOption.CanThink && !_voiceMode ? ReasoningEffort.Medium : ReasoningEffort.None }
            };
            if (!UseAgent)
                return options;
            await RouteToolsAsync(prompt, attachments);
            options.Tools = ChatAgentTools.Create(_tavilyKey, _webSearchSwitch.IsToggled, _shareLocationSwitch.IsToggled,
                !IsPrivateChat && _memorySwitch.IsToggled ? _store : null, privateChat: IsPrivateChat,
                readClipboard: AgentTools.MentionsClipboard(prompt));
            foreach (AITool tool in ToolRouter.Tools())
                options.Tools.Add(tool);
            options.Instructions = JoinInstructions(IsPrivateChat ? PrivateInstructions : AgentInstructions,
                await MemoryInstructionsAsync(), WebSearchInstructions(), IsPrivateChat ? PrivateInstructions : null,
                ToolRouter.Instructions());
            options.ToolMode = ChatToolMode.Auto;
            return options;
        }

        private static string JoinInstructions(params string[] parts)
        {
            string joined = string.Join("\n\n", parts.Where(p => !string.IsNullOrEmpty(p)));
            return joined.Length > 0 ? joined : null;
        }

        // The client of the selected model - LiteRT-LM's, or the server's - with function invocation for the tools passed
        // with each message (so the settings and the Web search chip can change them during a conversation); made on first
        // use, and again for another model or backend.
        private IChatClient EnsureClient()
        {
            if (_chatClient != null)
                return _chatClient;
            IChatClient inner;
            if (SelectedChoice == ModelChoice.Server)
                inner = CreateServerChatClient();
            else
            {
                if (_model == null)
                    throw new InvalidOperationException("The model isn't loaded.");
                var localClient = new LiteRtLmChatClient(_model);
                localClient.EnableContextManagement = true;
                localClient.ContextChanged += info => UiStallWatchdog.Mark("Context: " + info);
                inner = localClient;
            }
            _chatClient = new ChatClientBuilder(inner).UseFunctionInvocation().Build();
            return _chatClient;
        }

        // Release the client (and with it the model's open conversation); the conversation's messages stay.
        private void ResetAgent()
        {
            _chatClient?.Dispose();
            _chatClient = null;
        }

        private void OnWebSearchToggled(object sender, ToggledEventArgs e)
        {
            try { Preferences.Default.Set(WebSearchPreference, e.Value); } catch (Exception) { }
            UpdateWebSearchNotice();
            // Applies from the next message; the conversation continues.
            if (e.Value && !_busy)
                SetStatus(string.IsNullOrEmpty(_tavilyKey)
                    ? "Web search requires a Tavily API key in Settings > Web search. Wikipedia and other keyless tools are available."
                    : "Web search on: Wikipedia and the web.");
            else if (!_busy)
                SetStatus(null);
        }

        private void UpdateWebSearchNotice()
        {
            _webSearchNotice.IsVisible = SelectedOption.CanUseTools && _webSearchSwitch.IsToggled
                && string.IsNullOrWhiteSpace(_tavilyKey);
        }

        private string WebSearchInstructions()
        {
            if (!_webSearchSwitch.IsToggled)
                return "Web search is turned off. If a request needs current online information, explain that the user must enable Web search; general web search also requires a Tavily API key in Settings > Web search. Do not invent current facts.";
            if (string.IsNullOrWhiteSpace(_tavilyKey))
                return "General web search is unavailable because no Tavily API key is configured. Wikipedia, reading URLs the user supplies, weather and currency conversion are available without a key. For news, recent stock movements or other requests needing general web search, explicitly tell the user: Add a Tavily API key in Settings > Web search, then retry. Do not merely say you lack internet or real-time access, and do not invent current facts.";
            return "General web search is available through search_web. Use it for news and recent events, and cite the sources returned.";
        }

        private async Task LoadTavilyKeyAsync()
        {
            try
            {
                _tavilyKey = await SecureStorage.Default.GetAsync(TavilyKeyStorageName) ?? string.Empty;
                _savedTavilyKey = _tavilyKey;
                _tavilyKeyEntry.Text = _tavilyKey;
                _tavilyKeyStatus.Text = _tavilyKey.Length > 0 ? "API key loaded from secure storage." : "No API key saved.";
            }
            catch (Exception)
            {
                _tavilyKeyStatus.Text = "Could not load the saved API key from secure storage. Enter it again and select Save API key to retry.";
            }
            _tavilyKeyEntry.IsEnabled = true;
            _saveTavilyKeyButton.IsEnabled = true;
            UpdateWebSearchNotice();
        }

        private async void OnTavilyKeyChanged()
        {
            await _tavilyKeySaveGate.WaitAsync();
            try
            {
                string key = _tavilyKeyEntry.Text?.Trim() ?? string.Empty;
                if (key == _savedTavilyKey) return;
                _tavilyKey = key;
                UpdateWebSearchNotice();
                _saveTavilyKeyButton.IsEnabled = false;
                _tavilyKeyStatus.Text = "Saving API key to secure storage...";
                if (key.Length > 0)
                    await SecureStorage.Default.SetAsync(TavilyKeyStorageName, key);
                else
                {
                    bool removed = SecureStorage.Default.Remove(TavilyKeyStorageName);
                    if (!removed && !string.IsNullOrEmpty(_savedTavilyKey))
                        throw new InvalidOperationException("Secure storage removal failed.");
                }

                // Read back before reporting success: some platform keychain failures don't throw.
                string stored = await SecureStorage.Default.GetAsync(TavilyKeyStorageName) ?? string.Empty;
                if (!string.Equals(stored, key, StringComparison.Ordinal))
                    throw new InvalidOperationException("Secure storage verification failed.");
                _savedTavilyKey = key;
                _tavilyKeyStatus.Text = key.Length > 0
                    ? "API key saved securely. It will be available after restarting the app."
                    : "API key removed from secure storage.";
                if (!_busy)
                    SetStatus(key.Length > 0
                        ? (_webSearchSwitch.IsToggled ? "Tavily API key saved. Web search is on." : "Tavily API key saved. Enable Web search to use it.")
                        : "Tavily key removed. General web search requires a Tavily API key.");
            }
            catch (Exception)
            {
                // Never include the key or the exception in UI, logs or ordinary preferences.
                _savedTavilyKey = null;
                _tavilyKeyStatus.Text = _tavilyKey.Length > 0
                    ? "API key could not be saved securely. It is available for this session only. Select Save API key to retry."
                    : "Could not confirm removal from secure storage. A previously saved key may return after restarting. Select Save API key to retry.";
#if MACCATALYST
                _tavilyKeyStatus.Text += " This Mac build needs code signing and a provisioning profile with Keychain access enabled.";
#endif
                SetStatus(_tavilyKeyStatus.Text);
            }
            finally
            {
                _saveTavilyKeyButton.IsEnabled = true;
                _tavilyKeySaveGate.Release();
            }
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

        // ---------- Attachments ----------

        private async void OnAttachClicked()
        {
            if (_busy || _reloadingCpuThreads)
                return;
            int sessionVersion = _sessionVersion;
            bool privateRead = IsPrivateChat;

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
            if (sessionVersion != _sessionVersion || string.IsNullOrEmpty(action))
                return;

            try
            {
                if (action == "document")
                {
                    FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a document", FileTypes = DocumentReader.FileTypes });
                    if (file != null)
                        AttachDocument(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
                }
                else if (action == "library")
                {
                    FileResult file = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                    if (file != null)
                        AddPendingImage(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
                }
                else if (action == "camera")
                {
                    // The device's camera app; the photo is downscaled and re-encoded like a library photo (OpenCV
                    // applies its EXIF orientation when decoding).
                    FileResult file = await MediaPicker.Default.CapturePhotoAsync();
                    if (file != null)
                        AddPendingImage(await ReadAllBytesAsync(file, sessionVersion, privateRead), "Photo");
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
                        AddPendingAudio(await ReadAllBytesAsync(file, sessionVersion, privateRead), file.FileName);
                }
                else if (action == "sampleaudio")
                {
                    AddPendingAudio(await ImageUtil.ReadAppFileAsync(SampleAudio), SampleAudio);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetStatus("Could not attach the file: " + ex.Message);
            }
        }

        private async Task<byte[]> ReadAllBytesAsync(FileResult file, int sessionVersion, bool privateRead)
        {
            try
            {
                if (sessionVersion != _sessionVersion) throw new OperationCanceledException();
                using System.IO.Stream stream = await file.OpenReadAsync();
                using System.IO.MemoryStream ms = new System.IO.MemoryStream();
                await stream.CopyToAsync(ms);
                if (sessionVersion != _sessionVersion) throw new OperationCanceledException();
                return ms.ToArray();
            }
            finally
            {
                if (privateRead)
                    try { StorageFiles.DeleteTemporaryFile(FileSystem.CacheDirectory, file.FullPath); }
                    catch (Exception ex) { SetStatus("Could not remove a temporary picker copy: " + ex.Message); }
            }
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
            _ = CloseCameraAsync();
            _cameraFront = false;
#if ANDROID
            UpdateCameraSwitch();
#endif
            _visualRuntime?.Clear();
            ClearSoundMonitors();
            ForgetMonitorRequests();
            ClearCameraTurn();
            _sessionVersion++;
            if (IsPrivateChat) { ClearPrivateSessionContent(); ResetAgent(); }
            _conversationId = null;
            UpdateExportChatButton();
            _rereadNext = false;
            // A new conversation for the model: the tool groups start again from what its messages need.
            _toolRouter?.Reset();
            ResetDocumentContext();
            ClearTranscript();
            UpdateMemoryLabel();
        }

        // ---------- Context window ----------

        private int ContextWindow => SelectedChoice == ModelChoice.Server ? _serverContextTokens
            : _model != null && _model.MaxNumTokens > 0 ? _model.MaxNumTokens : 4096;

        // Counts tokens with the loaded model's tokenizer, or estimates them for a server's model. Call it off the UI
        // thread.
        private Func<string, int> TokenCounter
        {
            get
            {
                LanguageModel model = _model;
                if (SelectedChoice == ModelChoice.Server || model == null)
                    return ApproximateTokens;
                return model.CountTokens;
            }
        }

        // The conversation was compacted (before a message, or "compact now"): the excerpts given with the messages that
        // were summarized or dropped are no longer in the model's context, the tool groups start again, the transcript gets
        // its divider, and the summary is saved with the chat.
        private async void OnConversationCompacted(object sender, ChatCompactedEventArgs e)
        {
            UiStallWatchdog.Mark(string.Format("Compacted at message {0}, summarized {1}", e.ContextStart, e.Summarized));
            ResetDocumentContext();
            _toolRouter?.Reset();
            _rereadNext = true;
            if (e.ContextStart < _conversation.Messages.Count && _conversation.Messages[e.ContextStart].Tag is UserMessageInfo info)
                info.DividerText = e.Summary != null ? "Earlier messages summarized \u00B7 tap to read" : "Earlier messages dropped from the model's memory";
            // The store knows each message it saved or loaded, so it finds the last one the model no longer sees.
            if (_conversationId != null && !IsPrivateChat)
            {
                try
                {
                    await _store.SaveCompactionAsync(_conversationId.Value, _conversation);
                }
                catch (Exception ex)
                {
                    SetStatus("Could not save the chat: " + ex.Message);
                }
            }
            UpdateMemoryLabel();
        }

        // Compact now (Settings): everything but the latest exchange.
        private async void OnCompactTapped()
        {
            if (_busy || _reloadingCpuThreads)
            {
                SetStatus("Hang on - still answering.");
                return;
            }
            if (!ModelReady)
            {
                SetStatus("Load the model first.");
                return;
            }
            SetBusy(true);
            try
            {
                ConfigureConversation();
                if (!await _conversation.CompactAsync())
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

        private void UpdateMemoryLabel() => _ = UpdateMemoryLabelAsync();

        private async Task UpdateMemoryLabelAsync()
        {
            if (_memoryLabel == null)
                return;
            string text = string.Format(
                "The model takes in about {0:N0} tokens. Longer chats are summarized - or, for the small models, their " +
                "earliest messages are dropped - so it keeps up; the conversation here stays complete.", ContextWindow);
            int visible = ChatMessages.Skip(Math.Min(_conversation.ContextStart, _conversation.Messages.Count)).Count();
            if (ModelReady && visible > 0 && !_busy)
            {
                try
                {
                    // With what the chat template, or the agent's instructions and tools, take.
                    ConfigureConversation();
                    ChatContextUsage usage = await _conversation.GetContextUsageAsync();
                    text += string.Format(" This chat uses about {0:N0} ({1:P0}).", usage.UsedTokens, usage.Fraction);
                }
                catch (Exception)
                {
                    // Not counted (e.g. the model is reloading).
                }
            }
            _memoryLabel.Text = text;
            _compactButton.IsVisible = visible >= 4;
        }

        // ---------- Memories about the user ----------

        // The memories, for the agent's instructions: the latest that fit MaxMemoryLength, oldest first.
        private async Task<string> MemoryInstructionsAsync()
        {
            if (IsPrivateChat || !_memorySwitch.IsToggled)
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
            if (IsPrivateChat) return;
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
            if (IsPrivateChat) return;
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
        private async Task SaveExchangeAsync(ChatMessageItem userItem, ChatMessageItem reply, StreamingBubble bubble = null)
        {
            if (IsPrivateChat) return; // Write nothing to SQLite or attachments.
            UserMessageInfo info = userItem.Tag as UserMessageInfo ?? new UserMessageInfo();
            try
            {
                // The message, its images and audio, the step lines and the reply (an interrupted one ends with "…") are
                // saved by Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore; the language, the chat's document and the cards are the page's.
                _conversationId ??= await _store.CreateConversationAsync(ChatHistoryStore.TitleOf(userItem), SelectedChoice.ToString());
                int id = _conversationId.Value;
                // The step lines: the document's (a card above the reply), then the tool calls.
                List<string> steps = new List<string>();
                if (bubble?.DocumentStep != null)
                    steps.Add(bubble.DocumentStep);
                steps.AddRange(reply.Steps.Select(step => step.Description));
                // Pictures the reply drew are kept with it (shown again when the chat is reopened, not given to the model).
                List<(string Kind, string MediaType, byte[] Data)> drawn;
                lock (_sendImages)
                    drawn = _sendImages.Select(png => (GeneratedKind, "image/png", png)).ToList();
                // And the documents it wrote.
                drawn.AddRange(TakeWrittenDocuments());
                ChatSavedExchange saved = await _store.SaveExchangeAsync(id, userItem, reply, drawn, steps);
                info.MessageId = saved.MessageId;
                await _store.SetLanguageAsync(saved.MessageId, info.Language);
                if (info.TranscriptTask != null)
                    _ = SaveTranscriptWhenReadyAsync(id, info);
                if (bubble != null)
                    bubble.MessageId = saved.ReplyId;
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

        private async void OnHistoryTapped()
        {
            if (!CanChangeModel)
                return;
            if (!await LeavePrivateChatAsync()) return;
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
            if (!CanChangeModel || !await LeavePrivateChatAsync()) return;
            // Ending a private chat can await confirmation; another action may have started in the meantime.
            if (!CanChangeModel) return;
            SetBusy(true);
            _restoringTranscript = true;
            try
            {
                ConversationRecord chat = (await _store.GetConversationsAsync()).FirstOrDefault(c => c.Id == id);
                if (chat == null)
                    return;
                UiStallWatchdog.Mark("Open saved chat " + id);
                int index = ModelIndexOf(chat.Model);
                if (index >= 0 && index != _selectedModel)
                    SelectModel(index);
                StartNewChat();
                // The document belongs to the chat it was attached to (what the model was given of it is saved with it).
                RemoveDocument();
                _conversationId = id;
                UpdateExportChatButton();

                // The messages, with each reply's step lines (ChatHistoryStore reads them back, and the model sees those
                // after the last compaction and the summary of the ones before); the page's data and cards with them.
                ChatHistory history = await _store.LoadAsync<MessageRecord>(id);
                if (history == null)
                    return;
                string lastUserLanguage = null;
                foreach (ChatMessageItem item in history.Messages)
                {
                    var record = (MessageRecord)history.RecordOf(item);
                    if (item.Role == ChatMessageRole.User)
                    {
                        var info = new UserMessageInfo
                        {
                            // The language of a user message (detected now for chats saved before it was kept).
                            Language = record.Language ?? await TextLanguage.DetectAsync(item.Text),
                            Transcript = item.Transcript,
                            MessageId = record.Id
                        };
                        if (chat.Summary == null)
                            info.DividerText = "Earlier messages dropped from the model's memory";
                        lastUserLanguage = info.Language;
                        item.Tag = info;
                        continue;
                    }
                    StreamingBubble bubble = BubbleFor(item);
                    bubble.MessageId = record.Id;
                    bubble.TargetLanguage = TranslationTarget(record.Language ?? lastUserLanguage);
                    bubble.Translation = record.ThinkingTranslation;
                    bubble.ShowTranslation = !string.IsNullOrEmpty(record.ThinkingTranslation);
                    foreach (ChatSavedAttachment attachment in history.AttachmentsOf(item))
                    {
                        // A document the reply wrote, or a picture it drew: cards above it, not given to the model.
                        if (attachment.Kind == DocumentKind)
                            AddSavedDocumentCard(bubble, attachment.MediaType, await System.IO.File.ReadAllBytesAsync(attachment.Path));
                        else if (attachment.Kind == GeneratedKind)
                            AddSavedGeneratedImage(bubble, await System.IO.File.ReadAllBytesAsync(attachment.Path));
                    }
                    _ = CheckThinkingLanguageAsync(bubble);
                }
                SetStatus($"Opening conversation: {history.Messages.Count} messages...");
                await Task.Yield();
                history.Load(_conversation);
                int messageCount = history.Messages.Count;
                // The model is given the conversation with the next message, once its size is known (compacting it first if
                // it doesn't fit, e.g. a long chat saved before compaction existed).
                _rereadNext = messageCount > 0;
                // The document the chat used, from its saved index.
                if (!string.IsNullOrEmpty(chat.DocumentId))
                    _ = RestoreDocumentAsync(chat.DocumentId);
                UpdateMemoryLabel();
                await RefreshChatListAsync();
                SetStatus(null);
                UiStallWatchdog.Mark($"Opened saved chat: {messageCount} messages");
            }
            finally
            {
                _restoringTranscript = false;
                SetBusy(false);
                ScrollToEnd(false);
            }
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
            LanguageModel model = _model;
            _model = null;
            _loaded = null;
            _serverConnected = null;
            // Freeing an engine can take a moment: not on the UI thread.
            await Task.Run(() => model?.Dispose());
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            if (!Navigation.NavigationStack.Contains(this) && !Navigation.ModalStack.Contains(this))
            {
                _ = DisposeVisualMonitoringAsync();
                DisposeExerciseCounting();
                ReleaseAlertSounds();
                _ = DisposeSoundMonitoringAsync();
                _sessionVersion++;
                if (_resumeWindow != null)
                {
                    _resumeWindow.Resumed -= OnAppResumed;
                    _resumeWindow.Stopped -= CameraWindowStopped;
                    _resumeWindow.Resumed -= ServerAppResumed;
                    _resumeWindow.Stopped -= ServerAppStopped;
                    _resumeWindow = null;
                }
                EndVoiceMode();
                StopSpeaking();
                _loadCancel?.Cancel();
                if (!IsPrivateChat) _ = CancelRecordingAsync();
                RemoveDocument();
                _photoIndexing?.Cancel();
                _chatIndexing?.Cancel();
                _chatExportCancel?.Cancel();
                _searchRestoreCancel?.Cancel();
                if (IsPrivateChat) _ = CleanupPrivateChatAsync();
                else _ = CleanupAfterCpuReloadAsync();
            }
        }

        // ---------- Transcript ----------

        private void ClearTranscript()
        {
            ClearVisualSnapshots();
#if ANDROID
            ClearVisualClips();
            ClearVisualPhotos();
#endif
            _agentBubble = null;
            _conversation.Clear();
        }

        // The page's own data for a reply, in its ChatMessageItem.Tag (the demo's reply template binds to it): the translate
        // row under the thinking's header and the thinking shown (the translation, when chosen); and the page's handle for
        // the cards that features put above the reply (AddCard).
        private sealed class StreamingBubble : System.ComponentModel.INotifyPropertyChanged
        {
            public StreamingBubble(ChatMessageItem item, Label translateRow)
            {
                Item = item;
                TranslateRow = translateRow;
                item.Tag = this;
            }

            public ChatMessageItem Item { get; }

            // The row that offers the translation of the thinking (LiteRtLmChatPage.Translation.cs), the ThinkingView's accessory.
            public Label TranslateRow { get; }

            public string Thinking => Item.Thinking;

            // Translating the thinking (LiteRtLmChatPage.Translation.cs): the language of the message it answers when
            // that isn't English (null: no translation offered), the translation, whether it is shown, whether one is
            // being made, and the reply's id in the chat store.
            public string TargetLanguage;
            public string Translation;
            public bool ShowTranslation;
            public bool Translating;
            // The thinking's own language has been checked (it isn't the language it would be translated into).
            public bool TranslateReady;
            public int MessageId;
            // The document's step line ("Looked in report.pdf..."), a card above the reply.
            public string DocumentStep;

            // What the thinking shows when opened: the translation, when chosen; null for the thinking itself.
            public string ThinkingDisplayText => ShowTranslation && Translation != null ? Translation.Trim() : null;

            public void Refresh() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ThinkingDisplayText)));

            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        }

        // A reply's bubble data, with its translate row.
        private StreamingBubble BubbleFor(ChatMessageItem item)
        {
            var row = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.Accent, IsVisible = false };
            var bubble = new StreamingBubble(item, row);
            row.OnTap(() => OnTranslateTapped(bubble));
            return bubble;
        }

        // A message the conversation adds: the user's gets the data of the send (its language, its voice message's words),
        // a reply its bubble data - which tools that show cards use (_agentBubble) - with the send's document step, and
        // updates the status, the read-aloud and the step lines while it streams.
        private void OnConversationMessagesChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems == null)
                return;
            foreach (ChatMessageItem item in e.NewItems)
            {
                if (item.Tag != null)
                    continue; // restored, or the page's own
                if (item.Role == ChatMessageRole.User && _pendingUserInfo != null)
                {
                    UserMessageInfo info = _pendingUserInfo;
                    item.Tag = info;
                    if (_pendingVoice != null)
                        info.TranscriptTask = TranscribeVoiceMessageAsync(info, _pendingVoice);
                }
                else if (item.Role == ChatMessageRole.Assistant)
                {
                    StreamingBubble bubble = BubbleFor(item);
                    _agentBubble = bubble;
                    if (_pendingDocumentStep != null)
                    {
                        bubble.DocumentStep = _pendingDocumentStep;
                        AddCard(bubble, StepLine(_pendingDocumentStep));
                    }
                    WatchReply(item, _pendingReader);
                }
            }
        }

        // While a reply streams: the status ("Generating..." until it starts, the tool being called), the read-aloud, and
        // the step lines' outcomes.
        private void WatchReply(ChatMessageItem item, ReplyReader reader)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ChatMessageItem.Text))
                {
                    if (item.IsStreaming && item.Steps.All(step => !step.IsRunning))
                        SetStatus(null);
                    reader?.Update(item.Text);
                }
                else if (e.PropertyName == nameof(ChatMessageItem.Thinking) && item.Text.Length == 0 && item.IsStreaming && item.Steps.All(step => !step.IsRunning))
                    SetStatus(null);
            };
            item.Steps.CollectionChanged += (s, e) =>
            {
                if (e.NewItems == null)
                    return;
                foreach (ChatToolStep step in e.NewItems)
                {
                    if (step.Call == null)
                        continue;
                    UiStallWatchdog.Mark("Tool call: " + step.Description);
                    SetStatus(step.Description + "...");
                    string described = step.Description;
                    step.PropertyChanged += (o, p) =>
                    {
                        if (p.PropertyName != nameof(ChatToolStep.IsRunning) || step.IsRunning)
                            return;
                        UiStallWatchdog.Mark("Tool result" + (step.Error != null ? ": " + step.Error : ""));
                        string resultText = step.Result?.Result?.ToString() ?? string.Empty;
                        // A fact the model tried to save again isn't a new memory: say so instead of "Remembering".
                        if (resultText.StartsWith("Already remembered", StringComparison.Ordinal))
                            step.Description = "Already remembered";
                        else if (step.Error == null && (DeviceActionTools.Contains(step.Name) || IsMcpFunction(step.Name)))
                            // A device action or a tool server's tool: what the user chose, also in the saved chat.
                            step.Description = described + (IsMcpFunction(step.Name) ? McpStepOutcome(resultText) : DeviceActionStepOutcome(resultText));
                        SetStatus("Generating...");
                    };
                }
            };
        }

        // A grey step line like the tool calls', for the page's own steps (the document's).
        private static Label StepLine(string text) => new Label
        {
            Text = "\U0001F50E " + text,
            FontFamily = Theme.BodyFont,
            FontSize = 13,
            TextColor = Theme.SecondaryText,
            Margin = new Thickness(6, 0)
        };

        // A card above a reply (one of its decorations); without a reply, a notice of its own in the transcript.
        private void AddCard(StreamingBubble bubble, View card)
        {
            if (bubble != null)
                bubble.Item.Decorations.Add(card);
            else
                _conversation.AddNotice(card);
            ScrollToEnd();
        }

        // Take a card out of the transcript again. (A notice left empty stays - it shows nothing - since removing it would
        // move the conversation's ContextStart index.)
        private void RemoveCard(View card)
        {
            foreach (ChatMessageItem item in _conversation.Messages)
                if (item.Decorations.Remove(card))
                    return;
        }

        private bool _restoringTranscript;

        private void ScrollToEnd(bool animated = true)
        {
            if (!_restoringTranscript)
                _chatView.ScrollToEnd(animated);
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
