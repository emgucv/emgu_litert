// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.Models.CV.Maui;

namespace Maui.Demo.Lite;

// The old IChatHost (what the features in Chat/Features/*Feature.cs that predate the library's ChatPage were written
// against) over the library's IChatPageHost and the demo's IDemoHost, so those features go on unchanged while the page
// is Emgu.LiteRT.LM.Maui's ChatPage. Shrinks as each feature moves to the new hosts.
internal sealed class DemoChatHost : IChatHost
{
    private readonly IChatPageHost _host;
    private readonly IDemoHost _demo;

    public DemoChatHost(IChatPageHost host, IDemoHost demo)
    {
        _host = host;
        _demo = demo;
    }

    private CameraFeature Camera => _host.Services.Get<CameraFeature>();

    private DocumentsFeature Documents => _host.Services.Get<DocumentsFeature>();

    private ModelSettingsFeature ModelSettings => _host.Services.Get<ModelSettingsFeature>();

    private StorageFeature Storage => _host.Services.Get<StorageFeature>();

    void IChatHost.AddCardToReply(View card) => _host.AddCardToReply(card);

    void IChatHost.SetStatus(string message) => _host.SetStatus(message);

    Task<string> IChatHost.AskModelOnceAsync(string prompt, int maxTokens) => _host.AskModelOnceAsync(prompt, maxTokens);

    int IChatHost.ContextWindow => _host.ContextWindow;

    Func<string, int> IChatHost.TokenCounter => _host.TokenCounter;

    Task IChatHost.SaveDocumentAsync(GeneratedDocument document) => _demo.WrittenDocuments.SaveAsync(document, CancellationToken.None);

    void IChatHost.AttachDocument(byte[] bytes, string fileName)
    {
        if (ModelSettings?.SearchDownloadRequired != true)
            Documents?.AttachDocument(bytes, fileName);
    }

    Task IChatHost.SyncBackgroundServiceAsync() => Camera?.SyncBackgroundServiceAsync() ?? Task.CompletedTask;

    LiveCamera IChatHost.Camera => Camera?.Camera;

    Task<string> IChatHost.OpenCameraAsync(CancellationToken token) =>
        Camera?.OpenCameraAsync(token) ?? Task.FromResult("The camera isn't part of this chat.");

    Task IChatHost.CloseCameraIfUnusedAsync() => Camera?.CloseCameraIfUnusedAsync() ?? Task.CompletedTask;

    bool IChatHost.CameraMaximized => Camera?.CameraMaximized == true;

    void IChatHost.SetCameraOverlay(string count, string detail) => Camera?.SetCameraOverlay(count, detail);

    void IChatHost.BeginSoundMute() => Camera?.BeginSoundMute();

    void IChatHost.EndSoundMute() => Camera?.EndSoundMute();

    IAgentGoalStore IChatHost.Goals => _demo.Goals.On ? _demo.Store.Goals : null;

    bool IChatHost.DeveloperMode => _host.DeveloperMode;

    Task IChatHost.WaitForWorkAsync(bool photos, Action<string> status, CancellationToken cancel) => _demo.Workload.WaitForWorkAsync(photos, status, cancel);

    Task IChatHost.OpenStorageAsync() => Storage?.OpenStorageAsync() ?? Task.CompletedTask;

    Task<bool> IChatHost.AskAsync(string title, string message, string accept, string cancel) => _host.AskAsync(title, message, accept, cancel);

    string IChatHost.SettingsLocation => _host.SettingsLocation;

    Task<string> IChatHost.ShowSheetAsync(string title, View content, string closeText) => _host.ShowSheetAsync(title, content, closeText);

    void IChatHost.DismissSheet(string result) => _host.DismissSheet(result);

    Size IChatHost.PageSize => _host.PageSize;

    bool IChatHost.IsBusy => _host.IsBusy;

    Task IChatHost.SendMessageAsync(string text) => _host.SendMessageAsync(text);

    Microsoft.Extensions.AI.IChatClient IChatHost.ChatClient => _host.ChatClient;

    // The tools the plan may use, by name, for the planner: the core tools and the skills' (their groups' names).
    string IChatHost.ToolSummary
    {
        get
        {
            var tools = new List<string> { "date and time", "calculator", "unit and currency conversion", "weather", "Wikipedia" };
            string skills = _host.Skills.ToolSummary;
            if (!string.IsNullOrEmpty(skills))
                tools.Add(skills);
            return string.Join(", ", tools);
        }
    }

    bool IChatHost.ModelReady => _host.ModelReady;

    void IChatHost.SetBusy(bool busy) => _host.SetBusy(busy);

    ChatStore IChatHost.Store => _demo.Store;

    bool IChatHost.IsPrivateChat => _host.IsPrivate;

    bool IChatHost.MemoryOn => _host.MemoryOn;

    Task<string> IChatHost.PromptAsync(string title, string message, string accept, string cancel, string initialValue, int maxLength) =>
        _host.PromptAsync(title, message, accept, cancel, initialValue, maxLength);

    SearchEmbedder IChatHost.Embedder => ModelSettings?.Embedder;

    string IChatHost.SearchModelFile => ModelSettings?.SearchModelFile;

    bool IChatHost.SearchDownloadRequired => ModelSettings?.SearchDownloadRequired == true;

    Task<T> IChatHost.RunIndexEmbeddingAsync<T>(Func<T> embed, Action<string> paused, CancellationToken cancel, bool photos) =>
        _demo.Workload.RunIndexEmbeddingAsync(embed, paused, cancel, photos);

    bool IChatHost.CanIndexNow => _demo.PageVisible && !_host.IsReloadingModel;

    bool IChatHost.IsReplying => _host.IsReplying;

    bool IChatHost.ReloadingModels => _host.IsReloadingModel;

    bool IChatHost.SearchModelTakesImages => ModelSettings?.SupportsImages == true;

    int? IChatHost.ConversationId => _host.CurrentChatId;

    Task IChatHost.OpenChatAsync(int id) => _host.OpenChatAsync(id);
}
