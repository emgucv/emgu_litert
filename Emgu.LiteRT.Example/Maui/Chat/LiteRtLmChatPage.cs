//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Maui.Documents;
using Emgu.LiteRT.LM.Maui.Mcp;
using Emgu.LiteRT.LM.Maui.OpenAI;
using Emgu.LiteRT.LM.Maui.Images;
using Emgu.LiteRT.LM.Maui.CV;
using Emgu.LiteRT.LM.Agents.CV;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Models;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

/// <summary>
/// The chat demo: Emgu.LiteRT.LM.Maui's ChatPage with the library's models (Emgu.LiteRT.LM.Chat's ChatModelCatalog, and the
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
        public CameraFeature Camera;
        public DocumentsFeature Documents;
        public McpFeature Mcp;
        public VoiceModeFeature VoiceMode;
        public ServerFeature Server;
        public TranscriptFeature Transcripts;
        public ExportFeature Export;
        public StorageFeature Storage;
        public ModelSettingsFeature ModelSettings;
        public TaskFeature Tasks;
        public ImageFeature Images;
        public TranslationFeature Translations;
        public DeviceActionsFeature DeviceActions;
        public WrittenDocumentsFeature WrittenDocuments;
        public GoalsFeature Goals;
        public PastChatsFeature PastChats;
        public PhotoSearchFeature PhotoSearch;
        public MeetingFeature Meetings;
        public ExerciseFeature Exercises;
        public MemoryFeature Memory;
        public IIndexingWorkload Workload;
        public SearchServices Search;
    }

    private readonly Parts _parts;
    private readonly MenuSheet _menu = new MenuSheet();
    private bool _pageVisible;
    private int _sessionVersion;
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
        _building = null;
        Chat.AttachmentSource = new DemoAttachmentSource(ShowMenuAsync, () => IsPrivate, () => _sessionVersion,
            (bytes, name) => { if (!_parts.ModelSettings.SearchDownloadRequired) _parts.Documents.AttachDocument(bytes, name); }, SetStatus);
        Chat.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ChatView.Conversation))
                HookConversation();
        };
        HookConversation();
    }

    // The parts being built, for the base constructor: it applies the template, which builds the features' settings
    // sections before _parts is set (see Workload).
    [ThreadStatic]
    private static Parts _building;

    private static Parts Build()
    {
        IList<ChatModelOption> models = ChatModelCatalog.For(ChatDevicePlatform.Current);
        var parts = new Parts
        {
            Camera = new CameraFeature(new VisualDetectors(FileSystem.CacheDirectory, DetectorFactory.ValidationImage)),
            Mcp = new McpFeature(),
            VoiceMode = new VoiceModeFeature(),
            Transcripts = new TranscriptFeature(),
            Export = new ExportFeature((id, stream, cancel) => ChatExporter.ExportAsync(SharedStore, id, stream, cancel)),
            ModelSettings = new ModelSettingsFeature(),
            Tasks = new TaskFeature(),
            Translations = new TranslationFeature(),
            DeviceActions = new DeviceActionsFeature(),
            WrittenDocuments = new WrittenDocumentsFeature(),
            Goals = new GoalsFeature(SharedStore.Goals),
            Meetings = new MeetingFeature(),
            Exercises = new ExerciseFeature(),
            Memory = new MemoryFeature(SharedStore.Memories)
        };
        // The user's own server: its remote option goes last in the model list ("Server", the id saved chats have).
        parts.Server = new ServerFeature();
        models.Add(parts.Server.Option);
        parts.Workload = new DeviceIndexingWorkload();
        // Pictures drawn with Z-Image-Turbo: the demo's model selector above the switch, and a Save As dialog.
        parts.Images = new ImageFeature(parts.Workload)
        {
            ModelSelector = () => parts.ModelSettings.BuildImageModelSelector(),
            PictureSaver = async (png, fileName) =>
            {
                using var stream = new System.IO.MemoryStream(png, writable: false);
                var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
                if (result.IsSuccessful)
                    return "Saved to " + result.FilePath;
                if (!PictureGallery.SaveWasCancelled(result.Exception))
                    throw result.Exception ?? new System.IO.IOException("The image could not be saved.");
                return null;
            }
        };
        // What the library's searches share: the demo's search model and workload (the storage feature adds the
        // deleted-model notice and check).
        parts.Search = new SearchServices
        {
            Choice = parts.ModelSettings,
            Embedder = parts.ModelSettings.Embedder,
            Workload = parts.Workload,
            IndexModel = () => parts.ModelSettings.SearchModelFile,
            DownloadRequired = () => parts.ModelSettings.SearchDownloadRequired,
            SupportsImages = parts.ModelSettings.SupportsImages
        };
        parts.Storage = new StorageFeature(CreateStorageCatalog, parts.Search);
        parts.PastChats = new PastChatsFeature(SharedStore, parts.Search);
        parts.Documents = new DocumentsFeature(SharedStore, parts.Search);
        parts.PhotoSearch = new PhotoSearchFeature(SharedStore, parts.Search);
        // Snapshots the user keeps go through a Save As dialog, as the generated pictures do.
        parts.Camera.SnapshotSaver = async (jpeg, fileName) =>
        {
            using var stream = new System.IO.MemoryStream(jpeg, writable: false);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
            if (result.IsSuccessful)
                return "Saved to " + result.FilePath;
            if (!PictureGallery.SaveWasCancelled(result.Exception))
                throw result.Exception ?? new System.IO.IOException("The snapshot could not be saved.");
            return null;
        };
        // Meetings keep the camera feature's foreground service running (Android) and attach the transcript to the chat.
        parts.Meetings.SyncBackgroundService = () => parts.Camera.SyncBackgroundServiceAsync();
        parts.Meetings.TranscriptAttacher = (bytes, name) =>
        {
            if (!parts.ModelSettings.SearchDownloadRequired)
                parts.Documents.AttachDocument(bytes, name);
        };
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
            parts.Camera, parts.Server, parts.Mcp, parts.ModelSettings, parts.Images, parts.Storage, parts.Export,
            parts.Documents, parts.VoiceMode, parts.Tasks, parts.Transcripts, parts.Translations, parts.DeviceActions,
            parts.WrittenDocuments, parts.Goals, parts.PastChats, parts.PhotoSearch, parts.Meetings, parts.Exercises
        })
            options.Features.Add(feature);
        parts.Options = options;
        _building = parts;
        return parts;
    }

    // ---------- Services ----------

    private IIndexingWorkload Workload => (_parts ?? _building).Workload;

    private SearchEmbedder Embedder => _parts.ModelSettings.Embedder;

    // ---------- IDemoHost ----------

    ChatStore IDemoHost.Store => SharedStore;

    IIndexingWorkload IDemoHost.Workload => Workload;

    bool IDemoHost.PageVisible => _pageVisible;

    // What the storage page lists: the catalog's models, the two search models (downloadable again from the page), the
    // image model's folder, and the store's documents, indexes and attachments.
    private static StorageCatalog CreateStorageCatalog()
    {
        IList<ChatModelOption> catalog = ChatModelCatalog.For(ChatDevicePlatform.Current);
        var models = catalog.Select(m => new StorageModel(m.DisplayName, System.IO.Path.GetFileName(m.ModelFile.LocalFile))).ToList();
        using var vision = new EmbeddingGemma2TextVision();
        using var multimodal = new EmbeddingGemma2Multimodal();
        models.Add(new StorageModel("EmbeddingGemma 2 Text Vision 440M", System.IO.Path.GetFileName(vision.DefaultModelFile.LocalFile), isSearch: true) { Download = vision.DefaultModelFile });
        models.Add(new StorageModel("EmbeddingGemma 2 Multimodal 740M", System.IO.Path.GetFileName(multimodal.DefaultModelFile.LocalFile), isSearch: true) { Download = multimodal.DefaultModelFile });
        using var image = new ZImageTurbo();
        return new StorageCatalog(SharedStore, System.IO.Path.GetDirectoryName(catalog[0].ModelFile.LocalFile), ExportFeature.ExportFolder, models,
            new[] { new StorageFolder(ImageFeature.StorageKey, "Z-Image-Turbo", image.Folder, "Image model, host tensors, and prepared weight caches. Deleting turns image generation off.") });
    }

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
        _parts.Meetings.Stop("the chat was closed");
        _ = _parts.Camera.ShutdownAsync();
        _ = _parts.Mcp.ShutdownAsync();
        if (!IsPrivate)
            _ = Chat.CancelRecordingAsync();
        _parts.Documents.RemoveDocument();
        _parts.PhotoSearch.CancelIndexing();
        _parts.PastChats.CancelIndexing();
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
            try { await PendingReload; } catch (Exception) { }
            await _parts.PastChats.StopIndexingAsync();
            await ShutdownModelAsync();
            await Embedder.ReleaseAsync();
            await _parts.Images.ReleaseModelAsync();
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

    // While a reply streams: the status (the tool being called, "Generating..." between calls) and the memory tool's
    // step line.
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
                step.PropertyChanged += (o, p) =>
                {
                    if (p.PropertyName != nameof(ChatToolStep.IsRunning) || step.IsRunning)
                        return;
                    UiStallWatchdog.Mark("Tool result" + (step.Error != null ? ": " + step.Error : ""));
                    string resultText = step.Result?.Result?.ToString() ?? string.Empty;
                    // A fact the model tried to save again isn't a new memory: say so instead of "Remembering".
                    if (resultText.StartsWith("Already remembered", StringComparison.Ordinal))
                        step.Description = "Already remembered";
                    if (item.IsStreaming)
                        Chat.SetStatus("Generating...");
                };
            }
        };
    }

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

    // The conversation's settings for the selected model: what the instructions and the tool groups in use take, and the
    // summary's size (the base gives a server its own summary options).
    protected override void ConfigureConversation(ChatConversation conversation, ChatModelOption option, LanguageModel model)
    {
        base.ConfigureConversation(conversation, option, model);
        conversation.OverheadTokens = option?.CanUseTools == true ? 900 + GroupTokens(Skills.Router.Active) : 50;
        conversation.SummaryMaxTokens = SummaryMaxTokens;
    }

    // The groups in use, for the context estimate before a send (LiteRtLmChatClient counts the real size): the camera
    // group's tools and instructions are ~1,100 tokens, the others ~150-500.
    private static int GroupTokens(IEnumerable<string> active) => active.Sum(name =>
        name == ChatCamera.GroupName ? 1100 : name == ChatDeviceActions.GroupName || name == Emgu.LiteRT.LM.Agents.Skills.GoalsSkill.GroupName ? 500
        : name.StartsWith("mcp_", StringComparison.Ordinal) ? 400 : name == ChatCamera.SoundGroupName ? 600 : 200);

    // ---------- The model ----------

    // The model on the device, with the watchdog told about its context management (the base makes a server's client).
    protected override IChatClient CreateChatClient(ChatModelOption option, LanguageModel model)
    {
        if (option?.Remote != null)
            return base.CreateChatClient(option, model);
        var client = new LiteRtLmChatClient(model) { EnableContextManagement = true };
        client.ContextChanged += info => UiStallWatchdog.Mark("Context: " + info);
        return client;
    }

    // ---------- Sending ----------

    // Given to the model before a message: image references for the tools, then document excerpts; null for none.
    private static string JoinContext(string imageReferences, string document)
    {
        string context = (imageReferences ?? string.Empty) + (document ?? string.Empty);
        return context.Length > 0 ? context : null;
    }

    // A message: the features' additions (the document's excerpts as the message's context, and maybe a picture or page),
    // in task mode planned and run step by step; the reply streamed, sent again when a tool group's stub asks (or a
    // server rejected the reasoning option); then the translation offer, the memories it drew on, and the exchange saved.
    protected override async Task SendCoreAsync(ChatSendRequest request, CancellationToken cancellationToken)
    {
        _parts.Camera.ClearCameraTurn();
        ChatConversation conversation = Conversation;
        if (conversation == null || !await EnsureRemoteAsync(cancellationToken))
            return;
        string prompt = request.Text ?? string.Empty;
        UiStallWatchdog.Mark(string.Format("Send: {0} chars, {1} attachment(s), model {2}, think {3}", prompt.Length, request.Attachments.Count,
            SelectedModel?.DisplayName, Chat.ThinkingEnabled));
        bool canUseTools = SelectedModel?.CanUseTools == true;
        Chat.SetStatus("Generating...");
        try
        {
            // The model is given references to the attached images for the camera tools, before the features' additions.
            int attachedImages = request.Attachments.Count(a => a.MediaType != null && a.MediaType.StartsWith("image/", StringComparison.Ordinal));
            string imageReferences = canUseTools && attachedImages > 0 && Emgu.LiteRT.Models.CV.Maui.LiveCamera.Supported
                ? "Attached image references for analyze_visual_image: " + string.Join(", ", Enumerable.Range(1, attachedImages).Select(i => "image-" + i)) + "\n"
                : null;
            // The features' additions: the document's excerpts (and maybe a picture or page) most related to the message.
            ChatSendRequest prepared = await PrepareRequestAsync(
                new ChatSendRequest(prompt, request.Attachments, JoinContext(imageReferences, request.Context)) { IsVoiceMessage = request.IsVoiceMessage },
                cancellationToken);
            List<DataContent> attachments = prepared.Attachments.ToList();
            var info = new UserMessageInfo { Language = await TextLanguage.DetectAsync(prompt) };

            ChatOptions options = await ComposeOptionsAsync(request);
            _sendingUserItem = null;
            _pendingUserInfo = info;
            _pendingDocumentStep = PendingStepLines.FirstOrDefault();

            // A feature's client for this message (task mode's, with the Plan chip on), or none.
            IChatClient client = CreateMessageClient(prepared);
            ChatMessageItem reply = null;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    reply = reply == null
                        ? await conversation.SendAsync(prompt, attachments, options, prepared.Context, cancellationToken, client)
                        : await conversation.RegenerateAsync(reply, options, cancellationToken, client);
                }
                catch (Exception ex) when (attempt == 0 && !(ex is OperationCanceledException) && ShouldResend(ex, options))
                {
                    // The server doesn't take reasoning_effort: send it again without (ShouldResend took it out).
                    UiStallWatchdog.Mark("Request refused, sending it again: " + ex.Message);
                    reply = conversation.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);
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
            await ShowMemoriesUsedAsync(prompt, reply, bubble);
            UiStallWatchdog.Mark(string.Format("Reply done: {0} chars, {1} chars thinking", reply.Text.Length, reply.Thinking.Length));
            // The features' progress during the reply ("Step 2 of 3", "Waiting for you to confirm...") is on the page's
            // status line; the save's error, if any, replaces it.
            SetStatus(null);
            if (_sendingUserItem != null)
                await SaveExchangeAsync(_sendingUserItem, reply, bubble);
            Chat.SetStatus(null);
        }
        finally
        {
            _sendingUserItem = null;
            _pendingUserInfo = null;
            _pendingDocumentStep = null;
        }
    }

    // The message being sent, as the conversation holds it (for the save).
    private ChatMessageItem _sendingUserItem;

    protected override async Task OnBeforeStreamingAsync(ChatMessageItem message, CancellationToken cancellationToken)
    {
        _sendingUserItem = message;
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
            // The pictures the reply drew and the documents it wrote are the reply's SavedFiles (kept with it, shown
            // again when the chat is reopened, not given to the model).
            ChatSavedExchange saved = await SharedStore.SaveExchangeAsync(id, userItem, reply, null, steps);
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
                // The documents the reply wrote and the pictures it drew: cards above it, not given to the model.
                RestoreSavedFiles(item);
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
        _parts.PastChats.CancelIndexing();
        _parts.PhotoSearch.CancelIndexing();
        await _parts.PastChats.StopIndexingAsync();
        await _parts.PhotoSearch.WaitForIndexingAsync();
        if (documentReady != null) { try { await documentReady; } catch (OperationCanceledException) { } }
        await _parts.Documents.FinishPrivateDocumentsAsync();
        Chat.ReplySpeaker?.Stop();
        ClearPrivateSessionContent();
        // Drop generated-image prompt/caption caches as well as transcript references.
        if (IsPrivate)
        {
            await Embedder.ReleaseWithGateAsync();
            await _parts.Images.ResetModelAsync();
        }
        if (isPrivate) _privateDiagnostics ??= UiStallWatchdog.SuppressPrivateDetails();
        else { _privateDiagnostics?.Dispose(); _privateDiagnostics = null; }
        return true;
    }

    private void ClearPrivateSessionContent()
    {
        _parts.Documents.RemoveDocument();
        Chat.PendingAttachments.Clear();
        _agentBubble = null;
        _parts.Images.ClearPromptCache();
        _parts.Documents.ForgetFinishedPrivateDocuments();
    }
}
