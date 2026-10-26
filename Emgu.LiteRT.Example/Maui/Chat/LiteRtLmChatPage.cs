//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

/// <summary>
/// The chat demo: Emgu.LiteRT.LM.Maui's ChatPage with the demo's models (Maui.Demo.Lite.Core's ChatModelCatalog, and the
/// user's own server), its saved chats, memories and goals (ChatStore, SQLite), the library's built-in features and the
/// demo's own (Chat/Features: the camera and sound monitors, documents, tool servers, the voice chat, the server, storage,
/// export, photo and past-chat search, image generation, goals, device actions, meeting notes, exercise counting...).
/// The page itself adds what the demo does around a message: the document's excerpts, the task planner, the memories a
/// reply drew on, the thinking's translation, and saving each exchange with the pictures and documents it made.
/// </summary>
public sealed class LiteRtLmChatPage : ChatPage, IDemoHost
{
    private const int SummaryMaxTokens = 400;

    // The saved chats, memories, goals, documents and indexes (one database for the app).
    private static readonly ChatStore SharedStore = new ChatStore(System.IO.Path.Combine(FileSystem.AppDataDirectory, "chats"));

    // The demo's features (made before the page, so the base constructor attaches them).
    private sealed class Parts
    {
        public ChatPageOptions Options;
        public ChatModelOption ServerOption;
        public CameraFeature Camera;
        public DocumentsFeature Documents;
        public McpFeature Mcp;
        public VoiceModeFeature VoiceMode;
        public ServerFeature Server;
        public TranscriptFeature Transcripts;
        public ExportFeature Export;
        public StorageFeature Storage;
        public ModelSettingsFeature ModelSettings;
        public TaskChipFeature TaskChip;
        public TranslationFeature Translations;
        public MemoryFeature Memory;
    }

    private readonly Parts _parts;
    private readonly MenuSheet _menu = new MenuSheet();
    private DemoChatHost _host;
    private WorkloadService _workload;
    private MeetingFeature _meetingFeature;
    private ExerciseFeature _exerciseFeature;
    private ImageFeature _imageFeature;
    private WrittenDocumentsFeature _writtenDocuments;
    private TaskFeature _taskFeature;
    private GoalsFeature _goalsFeature;
    private PastChatsFeature _pastChatsFeature;
    private PhotoSearchFeature _photoSearchFeature;
    private DeviceActionsFeature _deviceActionsFeature;
    private bool _pageVisible;
    private int _sessionVersion;
    private Task _reloadTask;
    private IDisposable _privateDiagnostics;
    private ChatConversation _hooked;

    // The reply being written, so cards features add (found photos, documents, step lines...) are shown above it.
    private StreamingBubble _agentBubble;
    // The reply's bubble gets the user message's data and the document step of the send being made.
    private UserMessageInfo _pendingUserInfo;
    private string _pendingDocumentStep;

    public LiteRtLmChatPage() : this(Build())
    {
    }

    private LiteRtLmChatPage(Parts parts) : base(parts.Options)
    {
        _parts = parts;
        Chat.AttachmentSource = new DemoAttachmentSource(ShowMenuAsync, () => IsPrivate, () => _sessionVersion,
            (bytes, name) => { if (!_parts.ModelSettings.SearchDownloadRequired) _parts.Documents.AttachDocument(bytes, name); }, SetStatus);
        Chat.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChatView.Conversation))
                HookConversation();
        };
        HookConversation();
    }

    private static Parts Build()
    {
        IList<ChatModelOption> models = ChatModelOptions.FromCatalog(ChatModelCatalog.ModelsFor(DemoPlatform.Current), DemoPlatform.Current);
        var parts = new Parts
        {
            ServerOption = models.First(m => m.Id == ChatModelOptions.ServerId),
            Camera = new CameraFeature(),
            Documents = new DocumentsFeature(),
            Mcp = new McpFeature(),
            VoiceMode = new VoiceModeFeature(),
            Transcripts = new TranscriptFeature(),
            Export = new ExportFeature(),
            Storage = new StorageFeature(),
            ModelSettings = new ModelSettingsFeature(),
            TaskChip = new TaskChipFeature(),
            Translations = new TranslationFeature(),
            Memory = new MemoryFeature(SharedStore.Memories)
        };
        parts.Server = new ServerFeature(parts.ServerOption);
        var options = new ChatPageOptions
        {
            Title = "LiteRT-LM Chat",
            History = SharedStore,
            AutoSave = false,
            Settings = new ChatSettingsAdapter(),
            ShowBackButton = true,
            ShowCpuThreads = true,
            Instructions = ChatPlannerInstructions.Agent,
            ReplySpeaker = parts.Camera.ReplySpeaker,
            ConfigureEngine = settings =>
            {
                settings.EnableBenchmark();
                settings.AudioNumThreads = CpuThreadSettings.Count;
            }
        };
        foreach (ChatModelOption model in models)
            options.Models.Add(model);
        options.ChatActions.Add(parts.Export.Action);
        // The settings' order: the library's (CPU threads, read aloud, private chat), then these sections, then the
        // conversation memory and developer mode.
        foreach (ChatFeature feature in new ChatFeature[]
        {
            new VoiceLanguageFeature(),
            new CoreToolsFeature(), parts.Memory, new WebFeature(), new LocationFeature(), new ClipboardFeature(),
            parts.Camera, parts.Server, parts.Mcp, parts.ModelSettings, new DemoSectionsFeature(), parts.Storage, parts.Export,
            parts.Documents, parts.VoiceMode, parts.TaskChip, parts.Transcripts, parts.Translations, new DemoToolsFeature()
        })
            options.Features.Add(feature);
        parts.Options = options;
        return parts;
    }

    // ---------- The old-style features (IChatHost), made when first used ----------

    private DemoChatHost Host => _host ??= new DemoChatHost(this, this);

    private MeetingFeature Meetings => _meetingFeature ??= new MeetingFeature(Host);

    private ExerciseFeature Exercises => _exerciseFeature ??= new ExerciseFeature(Host);

    private ImageFeature Images => _imageFeature ??= new ImageFeature(Host);

    private WrittenDocumentsFeature WrittenDocuments => _writtenDocuments ??= new WrittenDocumentsFeature(Host);

    private TaskFeature Tasks => _taskFeature ??= new TaskFeature(Host);

    private GoalsFeature Goals => _goalsFeature ??= new GoalsFeature(Host);

    private PastChatsFeature PastChats => _pastChatsFeature ??= new PastChatsFeature(Host);

    private PhotoSearchFeature PhotoSearch => _photoSearchFeature ??= new PhotoSearchFeature(Host);

    private DeviceActionsFeature ActionTools => _deviceActionsFeature ??= new DeviceActionsFeature(Host);

    private WorkloadService Workload => _workload ??= new WorkloadService(() => _parts.ModelSettings.Embedder);

    private SearchEmbedder Embedder => _parts.ModelSettings.Embedder;

    private bool ServerSelected => SelectedModel == _parts.ServerOption;

    // ---------- IDemoHost ----------

    ChatStore IDemoHost.Store => SharedStore;

    WorkloadService IDemoHost.Workload => Workload;

    GoalsFeature IDemoHost.Goals => Goals;

    PhotoSearchFeature IDemoHost.PhotoSearch => PhotoSearch;

    PastChatsFeature IDemoHost.PastChats => PastChats;

    ImageFeature IDemoHost.Images => Images;

    MeetingFeature IDemoHost.Meetings => Meetings;

    ExerciseFeature IDemoHost.Exercises => Exercises;

    ExerciseFeature IDemoHost.ExercisesIfCreated => _exerciseFeature;

    WrittenDocumentsFeature IDemoHost.WrittenDocuments => WrittenDocuments;

    TaskFeature IDemoHost.Tasks => Tasks;

    DeviceActionsFeature IDemoHost.ActionTools => ActionTools;

    bool IDemoHost.PageVisible => _pageVisible;

    bool IDemoHost.IsRecording => Chat.IsRecording;

    async Task IDemoHost.RunReloadAsync(Func<Task> work)
    {
        using (BeginReload())
        {
            Task task = work();
            _reloadTask = task;
            try { await task; }
            finally { if (_reloadTask == task) _reloadTask = null; }
        }
    }

    Task IDemoHost.ReleaseModelAsync() => UnloadModelAsync();

    void IDemoHost.ResetRemote() => ResetRemote();

    void IDemoHost.StartNewChat() => StartNewChat();

    void IDemoHost.RefreshModelList() => RefreshModelList();

    void IDemoHost.DropPendingImages()
    {
        foreach (DataContent image in Chat.PendingAttachments.Where(a => a.MediaType != null && a.MediaType.StartsWith("image/", StringComparison.Ordinal)).ToList())
            Chat.PendingAttachments.Remove(image);
    }

    // A hands-free chat's recording: sent as a voice message, with the reply read aloud whatever the setting.
    async Task IDemoHost.SendVoiceAsync(byte[] wav, TimeSpan length)
    {
        bool readAloud = Chat.ReadAloud;
        Chat.ReadAloud = true;
        try
        {
            var recording = new DataContent(wav, "audio/wav") { Name = string.Format("Voice message ({0:m\\:ss})", length) };
            await Chat.SendAsync(string.Empty, new[] { recording }, null, voiceMessage: true);
        }
        finally
        {
            Chat.ReadAloud = readAloud;
        }
    }

    void IDemoHost.StopReply() => Chat.StopReply();

    // ---------- Lifecycle ----------

    protected override void OnPageBuilt()
    {
        base.OnPageBuilt();
        Shell.SetNavBarIsVisible(this, false);
    }

    // The "+" menu (the demo's MenuSheet): a layer over the page while it is open.
    private async Task<string> ShowMenuAsync(string title, IReadOnlyList<(string Section, string Glyph, string Text, string Value)> rows)
    {
        IChatOverlays overlays = this;
        overlays.ShowLayer(_menu);
        try
        {
            return await _menu.ShowAsync(title, rows);
        }
        finally
        {
            overlays.HideLayer(_menu);
        }
    }

    protected override void OnAppearing()
    {
        _pageVisible = true;
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pageVisible = false;
    }

    // Leaving the chat: the monitors, the camera, the microphone, the models and the indexes are let go of.
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        if (Navigation.NavigationStack.Contains(this) || Navigation.ModalStack.Contains(this))
            return;
        _sessionVersion++;
        _parts.VoiceMode.EndVoiceMode();
        Chat.ReplySpeaker?.Stop();
        _meetingFeature?.Stop("the chat was closed");
        _ = _parts.Camera.ShutdownAsync();
        _ = _parts.Mcp.ShutdownAsync();
        if (!IsPrivate)
            _ = Chat.CancelRecordingAsync();
        _parts.Documents.RemoveDocument();
        _photoSearchFeature?.CancelIndexing();
        _pastChatsFeature?.CancelIndexing();
        _parts.Storage.CancelRestore();
        _ = CloseAsync();
    }

    private async Task CloseAsync()
    {
        try
        {
            if (IsPrivate)
            {
                await Chat.CancelRecordingAsync();
                await _parts.Documents.FinishPrivateDocumentsAsync();
            }
            try { await (_reloadTask ?? Task.CompletedTask); } catch (Exception) { }
            if (_pastChatsFeature != null)
                await _pastChatsFeature.StopIndexingAsync();
            await ShutdownModelAsync();
            await Embedder.ReleaseAsync();
            if (_imageFeature != null)
                await _imageFeature.ReleaseModelAsync();
            if (IsPrivate)
            {
                ClearPrivateSessionContent();
                Conversation?.Clear();
            }
        }
        catch (Exception ex)
        {
            UiStallWatchdog.Mark("Closing the chat failed: " + ex.Message);
        }
        finally
        {
            _privateDiagnostics?.Dispose();
            _privateDiagnostics = null;
        }
    }

    // ---------- The conversation ----------

    // A message the conversation adds: the user's gets the data of the send (its language), a reply its bubble data -
    // which tools that show cards use (_agentBubble) - with the send's document step, and the status follows it.
    private void HookConversation()
    {
        ChatConversation conversation = Conversation;
        if (conversation == null || conversation == _hooked)
            return;
        _hooked = conversation;
        _agentBubble = null;
        conversation.Messages.CollectionChanged += (s, e) =>
        {
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Add || e.NewItems == null)
                return;
            foreach (ChatMessageItem item in e.NewItems)
            {
                if (item.Tag != null)
                    continue; // restored, or the page's own
                if (item.Role == ChatMessageRole.User && _pendingUserInfo != null)
                {
                    item.Tag = _pendingUserInfo;
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
                    WatchReply(item);
                }
            }
        };
        conversation.Compacted += OnConversationCompacted;
    }

    // A reply's bubble data, with its translate row.
    private StreamingBubble BubbleFor(ChatMessageItem item)
    {
        var row = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.Accent, IsVisible = false };
        var bubble = new StreamingBubble(item, row);
        row.OnTap(() => _parts.Translations.OnTranslateTapped(bubble));
        return bubble;
    }

    // While a reply streams: the status (the tool being called, "Generating..." between calls) and the device actions'
    // step lines' outcomes.
    private void WatchReply(ChatMessageItem item)
    {
        item.PropertyChanged += (s, e) =>
        {
            if ((e.PropertyName == nameof(ChatMessageItem.Text) || e.PropertyName == nameof(ChatMessageItem.Thinking))
                && item.IsStreaming && item.Steps.All(step => !step.IsRunning))
                Chat.SetStatus(null);
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
                Chat.SetStatus(step.Description + "...");
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
                    else if (step.Error == null && DeviceActionsFeature.ToolNames.Contains(step.Name))
                        // A device action: what the user chose, also in the saved chat.
                        step.Description = described + DeviceActionsFeature.StepOutcome(resultText);
                    if (item.IsStreaming)
                        Chat.SetStatus("Generating...");
                };
            }
        };
    }

    // A grey step line like the tool calls', for the page's own steps (the document's, the memories').
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
            Conversation?.AddNotice(card);
        Chat.ScrollToEnd();
    }

    // The conversation was compacted (before a message, or "compact now"): the excerpts given with the messages that
    // were summarized or dropped are no longer in the model's context, the transcript gets its divider, and the summary
    // is saved with the chat. (The library resets the tool groups and has the model re-read the conversation.)
    private async void OnConversationCompacted(object sender, ChatCompactedEventArgs e)
    {
        ChatConversation conversation = Conversation;
        if (conversation == null)
            return;
        UiStallWatchdog.Mark(string.Format("Compacted at message {0}, summarized {1}", e.ContextStart, e.Summarized));
        _parts.Documents.ResetDocumentContext();
        if (e.ContextStart < conversation.Messages.Count && conversation.Messages[e.ContextStart].Tag is UserMessageInfo info)
            info.DividerText = e.Summary != null ? "Earlier messages summarized · tap to read" : "Earlier messages dropped from the model's memory";
        // The store knows each message it saved or loaded, so it finds the last one the model no longer sees.
        if (CurrentChatId != null && !IsPrivate)
        {
            try
            {
                await SharedStore.SaveCompactionAsync(CurrentChatId.Value, conversation);
            }
            catch (Exception ex)
            {
                SetStatus("Could not save the chat: " + ex.Message);
            }
        }
    }

    // The conversation's settings for the selected model: what the instructions and the tool groups in use take, the
    // summary's size, and a server's summary options (its model may think anyway, which used up the summary's tokens).
    protected override void ConfigureConversation(ChatConversation conversation, ChatModelOption option, LanguageModel model)
    {
        base.ConfigureConversation(conversation, option, model);
        conversation.OverheadTokens = option?.CanUseTools == true ? 900 + DemoToolsFeature.GroupTokens(Skills.Router.Active) : 50;
        conversation.SummaryMaxTokens = SummaryMaxTokens;
        conversation.SummaryOptions = option == _parts.ServerOption ? _parts.Server.SummaryOptions : null;
    }

    // ---------- The model and the server ----------

    protected override IChatClient CreateChatClient(ChatModelOption option, LanguageModel model)
    {
        if (option == _parts.ServerOption)
            return _parts.Server.CreateChatClient();
        var client = new LiteRtLmChatClient(model) { EnableContextManagement = true };
        client.ContextChanged += info => UiStallWatchdog.Mark("Context: " + info);
        return client;
    }

    protected override Task<bool> ConnectRemoteAsync(ChatModelOption option, CancellationToken cancellationToken) =>
        option == _parts.ServerOption ? _parts.Server.ConnectAsync(cancellationToken) : Task.FromResult(true);

    public override Task<string> AskModelOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default) =>
        ServerSelected ? _parts.Server.AskOnceAsync(prompt) : base.AskModelOnceAsync(prompt, maxTokens, cancellationToken);

    // ---------- Sending ----------

    // Thinking goes to a server as reasoning_effort, unless it rejected that - then its model thinks as the server is set up to.
    protected override async Task<ChatOptions> ComposeOptionsAsync(ChatSendRequest request)
    {
        ChatOptions options = await base.ComposeOptionsAsync(request);
        if (ServerSelected && _parts.Server.RejectsReasoning)
            options.Reasoning = null;
        return options;
    }

    // Given to the model before a message: image references for the tools, then document excerpts; null for none.
    private static string JoinContext(string imageReferences, string document)
    {
        string context = (imageReferences ?? string.Empty) + (document ?? string.Empty);
        return context.Length > 0 ? context : null;
    }

    // A message: with a document attached, the excerpts (and maybe a picture or page) most related to it; in task mode,
    // planned and run step by step; the reply streamed, sent again when a tool group's stub asks (or a server rejected
    // the reasoning option); then the translation offer, the memories it drew on, and the exchange saved.
    protected override async Task SendCoreAsync(ChatSendRequest request, CancellationToken cancellationToken)
    {
        _parts.Camera.ClearCameraTurn();
        Images.ClearDrawn();
        WrittenDocuments.Clear();
        ChatConversation conversation = Conversation;
        if (conversation == null || !await EnsureRemoteAsync(cancellationToken))
            return;
        string prompt = request.Text ?? string.Empty;
        List<DataContent> attachments = request.Attachments?.ToList() ?? new List<DataContent>();
        UiStallWatchdog.Mark(string.Format("Send: {0} chars, {1} attachment(s), model {2}, think {3}", prompt.Length, attachments.Count,
            SelectedModel?.DisplayName, Chat.ThinkingEnabled));
        bool canUseTools = SelectedModel?.CanUseTools == true;
        Chat.SetStatus("Generating...");
        try
        {
            // With a document attached: the excerpts (and maybe a picture or page) most related to the message.
            DocumentContext document = await _parts.Documents.GetDocumentContextAsync(prompt);
            if (document?.Image != null)
                attachments.Add(document.Image);
            var info = new UserMessageInfo { Language = await TextLanguage.DetectAsync(prompt) };

            // Task mode: for this message only.
            bool planTask = _parts.TaskChip.TakePlan() && canUseTools && !HandsFree;
            ChatOptions options = await ComposeOptionsAsync(request);
            // The model is given references to the attached images for the camera tools.
            int attachedImages = attachments.Count(a => a.MediaType != null && a.MediaType.StartsWith("image/", StringComparison.Ordinal));
            string imageReferences = canUseTools && attachedImages > 0 && Emgu.LiteRT.Models.CV.Maui.LiveCamera.Supported
                ? "Attached image references for analyze_visual_image: " + string.Join(", ", Enumerable.Range(1, attachedImages).Select(i => "image-" + i)) + "\n"
                : null;
            int searchedAt = conversation.CompactionCount;
            ChatMessageItem userItem = null;
            _documentRefresh = async (message, token) =>
            {
                userItem = message;
                // Compacting may have summarized or dropped excerpts given before: they are searched again.
                if (document != null && conversation.CompactionCount != searchedAt)
                {
                    searchedAt = conversation.CompactionCount;
                    document = await _parts.Documents.RefreshDocumentContextAsync(prompt, document, message, attachments);
                    message.Context = JoinContext(imageReferences, document?.Text);
                }
            };
            _pendingUserInfo = info;
            _pendingDocumentStep = document?.Step;

            IChatClient client = planTask ? Tasks.CreateClient(prompt, attachments) : null;
            ChatMessageItem reply = null;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    reply = reply == null
                        ? await conversation.SendAsync(prompt, attachments, options, JoinContext(imageReferences, document?.Text), cancellationToken, client)
                        : await conversation.RegenerateAsync(reply, options, cancellationToken, client);
                }
                catch (System.ClientModel.ClientResultException ex) when (attempt == 0 && _parts.Server.ServerRejectedReasoning(ex))
                {
                    // The server doesn't take reasoning_effort: send it again without.
                    UiStallWatchdog.Mark("Server rejected reasoning_effort: " + ex.Message);
                    reply = conversation.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);
                    options.Reasoning = null;
                    continue;
                }
                if (attempt < 2 && reply.State == ChatMessageState.Done && Skills.TakeRerun() && !cancellationToken.IsCancellationRequested)
                {
                    // A stub ended the request (rerunAfterStub): the message again, now with its group - without the
                    // stub's turn, which the model handled worse.
                    UiStallWatchdog.Mark("Sending the message again with the tool group: " + string.Join(", ", Skills.Router.Active));
                    options = await ComposeOptionsAsync(request);
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
                bubble.TargetLanguage = TranslationFeature.Target(info.Language);
                bubble.Refresh();
                _ = _parts.Translations.CheckThinkingLanguageAsync(bubble);
            }
            _parts.Documents.MarkDocumentContextGiven(document);
            await ShowMemoriesUsedAsync(prompt, reply, bubble);
            UiStallWatchdog.Mark(string.Format("Reply done: {0} chars, {1} chars thinking", reply.Text.Length, reply.Thinking.Length));
            if (userItem != null)
                await SaveExchangeAsync(userItem, reply, bubble);
            Chat.SetStatus(null);
        }
        finally
        {
            _documentRefresh = null;
            _pendingUserInfo = null;
            _pendingDocumentStep = null;
        }
    }

    // The message about to be sent: the document's excerpts searched again after compacting, then the base's status.
    private Func<ChatMessageItem, CancellationToken, Task> _documentRefresh;

    protected override async Task OnBeforeStreamingAsync(ChatMessageItem message, CancellationToken cancellationToken)
    {
        if (_documentRefresh != null)
            await _documentRefresh(message, cancellationToken);
        await base.OnBeforeStreamingAsync(message, cancellationToken);
    }

    // Under a finished reply: the memories it drew on (AgentTools.MemoriesUsed: by their words - the model doesn't say), as a line
    // that opens them, and saved with the chat as a step line. Not for memories deleted in the same turn ("forget that...").
    private async Task ShowMemoriesUsedAsync(string prompt, ChatMessageItem reply, StreamingBubble bubble)
    {
        IList<AgentMemory> given = _parts.Memory.Skill.LastGiven;
        if (bubble == null || given == null || given.Count == 0 || reply.State != ChatMessageState.Done || !reply.HasText)
            return;
        try
        {
            HashSet<string> kept = (await SharedStore.Memories.GetMemoriesAsync()).Select(m => m.Id).ToHashSet();
            List<AgentMemory> used = AgentTools.MemoriesUsed(given, prompt, reply.Text).Where(m => kept.Contains(m.Id)).ToList();
            if (used.Count == 0)
                return;
            bubble.MemoryStep = MemoryStepText(used);
            var line = StepLine(bubble.MemoryStep);
            line.Text = "\U0001F9E0 " + bubble.MemoryStep;
            SemanticProperties.SetHint(line, "Shows these memories, to delete one");
            line.OnTap(() => _ = ShowMemoriesAsync(used.Select(m => m.Id).ToHashSet()));
            AddCard(bubble, line);
        }
        catch (Exception)
        {
        }
    }

    // "Used a memory: The user is vegetarian." / "Used 2 memories: ... · ..."
    internal static string MemoryStepText(IList<AgentMemory> used) =>
        (used.Count == 1 ? "Used a memory: " : "Used " + used.Count + " memories: ") + string.Join(" · ", used.Select(m => m.Text));

    // The memories a reply used, in a sheet, each with a button to delete it.
    private async Task ShowMemoriesAsync(HashSet<string> only)
    {
        if (IsPrivate) return;
        IList<AgentMemory> memories = (await SharedStore.Memories.GetMemoriesAsync()).Where(m => only.Contains(m.Id)).ToList();
        if (memories.Count == 0)
            return;
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (AgentMemory memory in memories)
        {
            string id = memory.Id;
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
            delete.OnTap(async () =>
            {
                await SharedStore.Memories.DeleteMemoryAsync(id);
                list.Children.Remove(row);
            });
            row.Add(delete, 1, 0);
            list.Children.Add(row);
        }
        await ((IChatPageHost)this).ShowSheetAsync("Memories this reply used", list, "Close");
        RefreshSettings();
    }

    // ---------- Saved chats ----------

    // Save a completed exchange: the message (with its attachments), any tool steps, and the reply - with the page's
    // extras: the message's language, the pictures the reply drew and the documents it wrote, the chat's document.
    private async Task SaveExchangeAsync(ChatMessageItem userItem, ChatMessageItem reply, StreamingBubble bubble)
    {
        if (IsPrivate) return; // Write nothing to SQLite or attachments.
        UserMessageInfo info = userItem.Tag as UserMessageInfo ?? new UserMessageInfo();
        try
        {
            int id = CurrentChatId ?? await SharedStore.CreateConversationAsync(ChatHistoryStore.TitleOf(userItem), SelectedModel?.Id);
            // The step lines: the document's (a card above the reply), then the tool calls, then the memories it used.
            List<string> steps = new List<string>();
            if (bubble?.DocumentStep != null)
                steps.Add(bubble.DocumentStep);
            steps.AddRange(reply.Steps.Select(step => step.Description));
            if (bubble?.MemoryStep != null)
                steps.Add(bubble.MemoryStep);
            // Pictures the reply drew are kept with it (shown again when the chat is reopened, not given to the model),
            // and the documents it wrote.
            List<(string Kind, string MediaType, byte[] Data)> drawn = Images.Drawn();
            drawn.AddRange(WrittenDocuments.Take());
            ChatSavedExchange saved = await SharedStore.SaveExchangeAsync(id, userItem, reply, drawn, steps);
            info.MessageId = saved.MessageId;
            await SharedStore.SetLanguageAsync(saved.MessageId, info.Language);
            if (bubble != null)
                bubble.MessageId = saved.ReplyId;
            // The document the chat uses now (or none), restored when it is opened again.
            await SharedStore.SetConversationDocumentAsync(id, _parts.Documents.DocumentReady?.IsCompletedSuccessfully == true ? _parts.Documents.DocumentId : null);
            SetCurrentChat(id);
            _parts.Transcripts.Track(userItem, id);
        }
        catch (Exception ex)
        {
            SetStatus("Could not save the chat: " + ex.Message);
        }
    }

    // Open a saved chat: switch to its model, show its messages with the page's data and cards, and give the model
    // the conversation so far (the chat's document comes back through DocumentsFeature.OnChatOpened).
    public override async Task OpenChatAsync(int chatId)
    {
        if (IsWorking)
            return;
        if (IsPrivate && !await SetPrivateAsync(false))
            return;
        ConversationRecord chat = (await SharedStore.GetConversationsAsync()).FirstOrDefault(c => c.Id == chatId);
        if (chat == null || IsWorking)
            return;
        UiStallWatchdog.Mark("Open saved chat " + chatId);
        SetBusy(true);
        try
        {
            PrepareForChat(new ChatHistoryEntry(chat.Id, chat.Title, chat.Model, chat.Updated));
            ChatConversation conversation = Conversation;
            if (conversation == null)
                return;
            // The messages, with each reply's step lines (ChatHistoryStore reads them back, and the model sees those
            // after the last compaction and the summary of the ones before); the page's data and cards with them.
            ChatHistory history = await SharedStore.LoadAsync<MessageRecord>(chatId);
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
                bubble.TargetLanguage = TranslationFeature.Target(record.Language ?? lastUserLanguage);
                bubble.Translation = record.ThinkingTranslation;
                bubble.ShowTranslation = !string.IsNullOrEmpty(record.ThinkingTranslation);
                foreach (ChatSavedAttachment attachment in history.AttachmentsOf(item))
                {
                    // A document the reply wrote, or a picture it drew: cards above it, not given to the model.
                    if (attachment.Kind == WrittenDocumentsFeature.Kind)
                        item.Decorations.Add(WrittenDocuments.SavedCard(attachment.MediaType, await System.IO.File.ReadAllBytesAsync(attachment.Path)));
                    else if (attachment.Kind == ImageFeature.GeneratedKind)
                        item.Decorations.Add(Images.SavedImageView(await System.IO.File.ReadAllBytesAsync(attachment.Path)));
                }
                _ = _parts.Translations.CheckThinkingLanguageAsync(bubble);
            }
            SetStatus($"Opening conversation: {history.Messages.Count} messages...");
            await Task.Yield();
            history.Load(conversation);
            _agentBubble = conversation.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant)?.Tag as StreamingBubble;
            SetCurrentChat(chatId);
            UiStallWatchdog.Mark($"Opened saved chat: {history.Messages.Count} messages");
        }
        finally
        {
            SetBusy(false);
            SetStatus(null);
        }
        OnChatOpened(chatId);
    }

    // A new chat: the page's data for the reply being written, the pictures drawn and the documents written go (the
    // features reset themselves: the camera closes, the monitors and the document go).
    public override void StartNewChat()
    {
        if (IsWorking)
        {
            SetStatus("Hang on - still answering.");
            return;
        }
        _sessionVersion++;
        _agentBubble = null;
        _imageFeature?.ClearDrawn();
        _writtenDocuments?.Clear();
        base.StartNewChat();
    }

    // ---------- Private chats ----------

    // Starting or ending a private chat: what the indexes and the search model hold is finished or released first, and
    // nothing of the session is kept (the document, the pictures, the prompt caches).
    protected override async Task<bool> OnPrivateModeChangingAsync(bool isPrivate)
    {
        _sessionVersion++;
        // Cancel every producer before awaiting the shared embedding load.
        Task<bool> documentReady = _parts.Documents.DocumentReady;
        _parts.Documents.RemoveDocument();
        PastChats.CancelIndexing();
        PhotoSearch.CancelIndexing();
        await PastChats.StopIndexingAsync();
        await PhotoSearch.WaitForIndexingAsync();
        if (documentReady != null) { try { await documentReady; } catch (OperationCanceledException) { } }
        await _parts.Documents.FinishPrivateDocumentsAsync();
        Chat.ReplySpeaker?.Stop();
        ClearPrivateSessionContent();
        // Drop generated-image prompt/caption caches as well as transcript references.
        if (IsPrivate)
        {
            await Embedder.ReleaseWithGateAsync();
            await Images.ResetModelAsync();
        }
        if (isPrivate) _privateDiagnostics ??= UiStallWatchdog.SuppressPrivateDetails();
        else { _privateDiagnostics?.Dispose(); _privateDiagnostics = null; }
        return true;
    }

    private void ClearPrivateSessionContent()
    {
        _parts.Documents.RemoveDocument();
        Chat.PendingAttachments.Clear();
        _imageFeature?.ClearDrawn();
        _writtenDocuments?.Clear();
        _agentBubble = null;
        _imageFeature?.ClearPromptCache();
        _parts.Documents.ForgetFinishedPrivateDocuments();
    }
}
