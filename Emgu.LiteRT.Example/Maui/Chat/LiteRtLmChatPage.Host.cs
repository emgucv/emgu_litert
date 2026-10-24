// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using Emgu.LiteRT.LM.Agents;

namespace Maui.Demo.Lite;

// The page as the host of its feature classes (IChatHost), and those features.
public partial class LiteRtLmChatPage : IChatHost
{
    private MeetingFeature _meetingFeature;

    private MeetingFeature Meetings => _meetingFeature ??= new MeetingFeature(this);

    private ExerciseFeature _exerciseFeature;

    private ExerciseFeature Exercises => _exerciseFeature ??= new ExerciseFeature(this);

    private ImageFeature _imageFeature;

    private ImageFeature Images => _imageFeature ??= new ImageFeature(this);

    private WrittenDocumentsFeature _writtenDocuments;

    private WrittenDocumentsFeature WrittenDocuments => _writtenDocuments ??= new WrittenDocumentsFeature(this);

    private TaskFeature _taskFeature;

    private TaskFeature Tasks => _taskFeature ??= new TaskFeature(this);

    private TranslationFeature _translationFeature;

    private TranslationFeature Translations => _translationFeature ??= new TranslationFeature(this);

    private GoalsFeature _goalsFeature;

    private GoalsFeature Goals => _goalsFeature ??= new GoalsFeature(this);

    private PastChatsFeature _pastChatsFeature;

    private PastChatsFeature PastChats => _pastChatsFeature ??= new PastChatsFeature(this);

    private DeviceActionsFeature _deviceActionsFeature;

    // Timers, alarms, calendar events, reminders and directions, each confirmed on a card (the static DeviceActions does them).
    private DeviceActionsFeature ActionTools => _deviceActionsFeature ??= new DeviceActionsFeature(this);

    void IChatHost.AddCardToReply(View card) => AddCard(_agentBubble, card);

    void IChatHost.SetStatus(string message) => SetStatus(message);

    Task<string> IChatHost.AskModelOnceAsync(string prompt, int maxTokens) => AskModelOnceAsync(prompt, maxTokens);

    int IChatHost.ContextWindow => ContextWindow;

    Func<string, int> IChatHost.TokenCounter => TokenCounter;

    Task IChatHost.SaveDocumentAsync(GeneratedDocument document) => WrittenDocuments.SaveAsync(document, CancellationToken.None);

    void IChatHost.AttachDocument(byte[] bytes, string fileName)
    {
        if (!SearchDownloadRequired)
            AttachDocument(bytes, fileName);
    }

    Task IChatHost.SyncBackgroundServiceAsync() => SyncMonitoringServiceAsync();

    LiveCamera IChatHost.Camera => _camera;

    Task<string> IChatHost.OpenCameraAsync(CancellationToken token) => OpenCameraAsync(token);

    async Task IChatHost.CloseCameraIfUnusedAsync()
    {
        if (!_visualRuntime.Snapshot().Any())
            await CloseCameraAsync();
    }

    bool IChatHost.CameraMaximized => _cameraMaximized;

    void IChatHost.SetCameraOverlay(string count, string detail)
    {
        if (_cameraOverlay == null) return;
        _cameraOverlay.IsVisible = count != null;
        if (count == null) return;
        _cameraOverlayCount.Text = count;
        _cameraOverlayDetail.Text = detail;
    }

    void IChatHost.BeginSoundMute() => BeginSoundMute();

    void IChatHost.EndSoundMute() => EndSoundMute();

    IAgentGoalStore IChatHost.Goals => Goals.On ? _store : null;

    bool IChatHost.DeveloperMode => DeveloperMode;

    Task IChatHost.WaitForWorkAsync(bool photos, Action<string> status, CancellationToken cancel) => WaitForWorkAsync(photos, status, cancel);

    Task IChatHost.OpenStorageAsync() => OpenStorageAsync();

    async Task<bool> IChatHost.AskAsync(string title, string message, string accept, string cancel)
    {
        if (cancel == null)
        {
            await DisplayAlertAsync(title, message, accept);
            return true;
        }
        return await DisplayAlertAsync(title, message, accept, cancel);
    }

    string IChatHost.SettingsLocation => _settingsButton.IsVisible ? "this chat's settings (the gear button at the top)" : "the settings panel beside the chat";

    Task<string> IChatHost.ShowSheetAsync(string title, View content, string closeText) => _sheet.ShowViewAsync(title, content, closeText);

    void IChatHost.DismissSheet(string result) => _sheet.Dismiss(result);

    Size IChatHost.PageSize => new Size(Width, Height);

    bool IChatHost.IsBusy => _busy || _reloadingCpuThreads;

    Task IChatHost.SendMessageAsync(string text) => SendAsync(text, new List<PendingAttachment>());

    Microsoft.Extensions.AI.IChatClient IChatHost.ChatClient => _clientProxy;

    string IChatHost.ToolSummary => TaskToolSummary();

    bool IChatHost.ModelReady => ModelReady;

    void IChatHost.SetBusy(bool busy) => SetBusy(busy);

    ChatStore IChatHost.Store => _store;

    SearchEmbedder IChatHost.Embedder => Embedder;

    string IChatHost.SearchModelFile => SelectedEmbeddingFile;

    bool IChatHost.SearchDownloadRequired => SearchDownloadRequired;

    Task<T> IChatHost.RunIndexEmbeddingAsync<T>(Func<T> embed, Action<string> paused, CancellationToken cancel, bool photos) =>
        RunIndexEmbeddingAsync(embed, paused, cancel, photos);

    bool IChatHost.CanIndexNow => _pageVisible && !_reloadingCpuThreads;

    bool IChatHost.IsReplying => _busy;

    int? IChatHost.ConversationId => _conversationId;

    async Task IChatHost.OpenChatAsync(int id)
    {
        if (!CanChangeModel) { SetStatus("Wait for the reply to finish before opening a saved chat."); return; }
        await OpenConversationAsync(id);
    }

    bool IChatHost.IsPrivateChat => IsPrivateChat;

    bool IChatHost.MemoryOn => _memorySwitch.IsToggled;

    Task<string> IChatHost.PromptAsync(string title, string message, string accept, string cancel, string initialValue, int maxLength) =>
        DisplayPromptAsync(title, message, accept, cancel, initialValue: initialValue, maxLength: maxLength);

    async Task IChatHost.SaveThinkingTranslationAsync(int messageId, string language, string translation)
    {
        if (!IsPrivateChat)
            await _store.SetThinkingTranslationAsync(messageId, language, translation);
    }
}
#endif
