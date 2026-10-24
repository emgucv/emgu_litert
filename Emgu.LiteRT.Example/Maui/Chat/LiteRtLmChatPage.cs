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
        private Grid _mainColumn;
        private Border _sidePanel;
        private VerticalStackLayout _sideModelList;
        private Button _sideModelHeader;
        private bool _modelsExpanded = true;
        private const string ModelsExpandedPreference = "litert_lm_chat_models_expanded";
        private const string CpuModelsExpandedPreference = "litert_lm_chat_cpu_models_expanded";
        private const string GpuModelsExpandedPreference = "litert_lm_chat_gpu_models_expanded";
        private VerticalStackLayout _sideSettingsHost;
        private VerticalStackLayout _settingsView;
        private Border _modelChip;
        private Label _modelChipLabel;
        private Label _titleLabel;
        private Border _settingsButton;
        private Border _newChatButton;
        private HorizontalStackLayout _toggleRow;
        private ToggleChip _thinkingSwitch;
        private Label _statusLabel;
        private Editor _promptEditor;
        private Button _sendButton;
        private ActivityIndicator _busyIndicator;
        private Border _attachButton;
        private HorizontalStackLayout _attachmentStrip;
        private ScrollView _attachmentScroll;
        private readonly BottomSheet _sheet;
        private readonly List<PendingAttachment> _pending = new List<PendingAttachment>();
        private Border _talkButton;
        private Microsoft.Maui.Controls.Shapes.Path _micIcon;
        private BoxView _stopIcon;
        private Switch _readAloudSwitch;
        private readonly VoiceRecorder _recorder = new VoiceRecorder();
        private DateTime _recordingStarted;
        private CancellationTokenSource _recordingTimer;

        // Web search: an agent (Microsoft Agent Framework) over the model's IChatClient, with the tools of ChatAgentTools.
        private const string TavilyKeyStorageName = "tavily_api_key";
        private const string AgentInstructions = ChatPlannerInstructions.Agent;
        private ToggleChip _webSearchSwitch;
        // Task mode (LiteRtLmChatPage.Task.cs): the next message is planned and run step by step; off again after it is sent.
        private ToggleChip _taskChip;
        private Label _webSearchNotice;
        private Entry _tavilyKeyEntry;
        private Label _tavilyKeyStatus;
        private Button _saveTavilyKeyButton;
        private readonly SemaphoreSlim _tavilyKeySaveGate = new SemaphoreSlim(1, 1);
        private string _savedTavilyKey;
        // Whether the agent gets the location tool (Settings, off by default): the user's town and approximate
        // coordinates, which leave the device in web searches that use them.
        private const string ShareLocationPreference = "litert_lm_chat_share_location";
        private Switch _shareLocationSwitch;
        // Memories about the user (ChatStore): with "Remember things about me" on (the default; they stay on the device),
        // the agent can save and delete them, and the latest are given to it with every message.
        private const string MemoryPreference = "litert_lm_chat_memory";
        private const int MaxMemoryLength = 1200;
        private Switch _memorySwitch;
        private Label _memoryCountLabel;
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
        private VerticalStackLayout _contextSection;
        private Label _contextNote;
        private bool _updatingContextChips;
        private bool _gpuUnavailable;

        // Saved chats (ChatStore, SQLite): the current chat's id once its first exchange is saved.
        private const string HistoryIconPath = "M13 3c-4.97 0-9 4.03-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42C8.27 19.99 10.51 21 13 21c4.97 0 9-4.03 9-9s-4.03-9-9-9zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z";
        private readonly ChatStore _store = new ChatStore(System.IO.Path.Combine(FileSystem.AppDataDirectory, "chats"));
        private int? _conversationId;
        private Border _historyButton;
        private VerticalStackLayout _sideChatList;

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
        private Label _memoryLabel;
        private Label _compactButton;

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
        private Border _loadCard;
        private Label _loadTitle;
        private Label _loadStage;
        private ProgressBar _loadProgress;
        private ActivityIndicator _loadSpinner;
        private Button _loadPrimaryButton;
        private Button _loadCancelButton;
        private ActivityIndicator _chipSpinner;
        private Action _loadPrimaryAction;
        private Task _generation;
        private bool _busy;

        public LiteRtLmChatPage()
        {
            Title = "LiteRT-LM Chat";
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            View header = BuildHeader();
            BuildMessageToggles();
            BuildSettingsView();

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

            View composerCard = BuildComposer();
            BuildMainColumn(header, composerCard);
            BuildCameraPanel();
            BuildSidePanel();

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

        // Open with the model used last time, and start loading it if it is already downloaded.
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _pageVisible = true;
            PastChats.StartIndexing();
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
                _exerciseFeature?.Dispose();
                _meetingFeature?.Stop("the chat was closed");
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
                PastChats.CancelIndexing();
                _chatExportCancel?.Cancel();
                _searchRestoreCancel?.Cancel();
                if (IsPrivateChat) _ = CleanupPrivateChatAsync();
                else _ = CleanupAfterCpuReloadAsync();
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
