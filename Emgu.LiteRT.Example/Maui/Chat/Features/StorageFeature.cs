// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;

namespace Maui.Demo.Lite;

// Settings > "Storage": the StoragePage lists the downloaded models, caches, saved documents and chat attachments with
// their sizes, to remove them (StorageCatalog); deleting the search model turns the searches off until it is
// downloaded again (the notices under their switches, and the button here).
internal sealed class StorageFeature : DemoFeature
{
    private Button _restoreSearchButton;
    // Notices with their own download button under the photo and past-chat search switches, shown while the search
    // model is deleted - otherwise the switches are just disabled, with the way back only in the Storage section.
    private readonly List<View> _searchNotices = new List<View>();
    private readonly List<Button> _searchNoticeButtons = new List<Button>();
    private CancellationTokenSource _searchRestoreCancel;

    public override string Name => "storage";

    public override string Title => "Storage";

    public override bool PersistEnabled => false;

    private ModelSettingsFeature Search => Host.Services.Get<ModelSettingsFeature>();

    private DocumentsFeature Documents => Host.Services.Get<DocumentsFeature>();

    private bool Exporting => Host.Services.Get<ExportFeature>()?.IsExporting == true;

    private bool SearchDownloadRequired => Search?.SearchDownloadRequired == true;

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override void OnDetached()
    {
        _searchRestoreCancel?.Cancel();
        base.OnDetached();
    }

    // Leaving the page: a search-model download in progress stops.
    public void CancelRestore() => _searchRestoreCancel?.Cancel();

    // A private chat: past-chat search is off (nothing saved is read); indexing goes on again when it ends.
    public override void OnPrivateModeChanged(bool isPrivate)
    {
        UpdateSearchStorageState();
        if (!isPrivate && PageVisible) { Host.Services.Get<PhotoSearchFeature>()?.StartIndexing(); Host.Services.Get<PastChatsFeature>()?.StartIndexing(); }
    }

    // A notice for a search feature's section: shown while the search model is deleted, with a button to download it again.
    public View CreateSearchModelNotice()
    {
        var button = new Button { Text = "Download search model", HorizontalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(button, "Download the search model again");
        button.Clicked += async (_, _) => await RestoreSearchModelDownloadAsync();
        var notice = new VerticalStackLayout { Spacing = 4, IsVisible = SearchDownloadRequired, Children = {
            new Label { Text = "The search model was deleted, so this is off. Download it again to turn it on.",
                FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.Danger }, button } };
        _searchNotices.Add(notice);
        _searchNoticeButtons.Add(button);
        return notice;
    }

    public override View BuildSettingsSection()
    {
        var open = new Button { Text = "Storage...", HorizontalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(open, "Manage downloaded models, caches, documents and attachments");
        open.Clicked += async (_, _) => await OpenStorageAsync();
        _restoreSearchButton = new Button { Text = "Download search model again", IsVisible = SearchDownloadRequired };
        _restoreSearchButton.Clicked += async (_, _) => await RestoreSearchModelDownloadAsync();
        UpdateSearchStorageState();
        return new VerticalStackLayout { Spacing = 6, Children = { Theme.SectionTitle("Storage"), open,
            new Label { Text = "See sizes and free space, and remove models, caches, saved documents or chat attachments.", FontSize = 12, TextColor = Theme.SecondaryText },
            new Label { Text = BackgroundDownloads.Hint, FontSize = 12, TextColor = Theme.SecondaryText },
            _restoreSearchButton } };
    }

    // The searches follow the search model: off and disabled while it is deleted (also disabled in a private chat).
    public void UpdateSearchStorageState()
    {
        if (Demo == null) return;
        bool blocked = SearchDownloadRequired;
        if (_restoreSearchButton != null) _restoreSearchButton.IsVisible = blocked;
        foreach (View notice in _searchNotices) notice.IsVisible = blocked;
        PhotoSearchFeature photos = Host.Services.Get<PhotoSearchFeature>();
        PastChatsFeature chats = Host.Services.Get<PastChatsFeature>();
        if (blocked && photos.On) { photos.TurnOff(); photos.CancelIndexing(); }
        if (blocked && chats.On) { chats.TurnOff(); chats.CancelIndexing(); }
        photos.SetSwitchEnabled(!blocked && Search.SupportsImages);
        chats.SetSwitchEnabled(!Host.IsPrivate && !blocked);
    }

    private StorageCatalog CreateStorageCatalog()
    {
        ModelOption[] catalog = ChatModelCatalog.ModelsFor(DemoPlatform.Current);
        var models = catalog.Select(m => (m.Name, Path.GetFileName(m.File.LocalFile), false)).ToList();
        using var vision = new EmbeddingGemma2TextVision();
        using var multimodal = new EmbeddingGemma2Multimodal();
        models.Add(("EmbeddingGemma 2 Text Vision 440M", Path.GetFileName(vision.DefaultModelFile.LocalFile), true));
        models.Add(("EmbeddingGemma 2 Multimodal 740M", Path.GetFileName(multimodal.DefaultModelFile.LocalFile), true));
        using var image = new ZImageTurbo();
        return new StorageCatalog(Demo.Store, Path.GetDirectoryName(catalog[0].File.LocalFile), image.Folder,
            Path.Combine(FileSystem.CacheDirectory, "sharing-root", "chat-exports"), models);
    }

    public async Task OpenStorageAsync()
    {
        if (Host.IsBusy || Exporting) return;
        Host.DismissSheet(null);
        var catalog = CreateStorageCatalog();
        await Host.Page.Navigation.PushAsync(new StoragePage(catalog, entry => DeleteStorageAsync(catalog, entry), RestoreStorageSearchAsync,
            () => _searchRestoreCancel?.Cancel()));
    }

    private async Task DeleteStorageAsync(StorageCatalog catalog, StorageEntry entry)
    {
        if (Host.IsBusy || Exporting) throw new InvalidOperationException("Wait for active work to finish first.");
        int? reopen = null;
        // Like a model reload: nothing new starts meanwhile, and leaving the page waits for it.
        await Demo.RunReloadAsync(async () => reopen = await DeleteStorageCoreAsync(catalog, entry));
        if (reopen is int id)
        {
            // Reopen from the database, dropping deleted media held in the model's conversation and transcript.
            await Demo.ReleaseModelAsync();
            await Host.OpenChatAsync(id);
        }
    }

    // Deletes the entry; the chat to reopen afterwards (its attachments were deleted), or null.
    private async Task<int?> DeleteStorageCoreAsync(StorageCatalog catalog, StorageEntry entry)
    {
        PhotoSearchFeature photos = Host.Services.Get<PhotoSearchFeature>();
        PastChatsFeature chats = Host.Services.Get<PastChatsFeature>();
        DocumentsFeature documents = Documents;
        int? reopen = null;
        try
        {
            Task<bool> documentReady = documents?.DocumentReady;
            bool documentPending = documentReady != null && !documentReady.IsCompleted;
            // These tasks may share the same embedder load: cancel all producers before awaiting any of them.
            if (documentPending) documents.RemoveDocument();
            photos.CancelIndexing();
            chats.CancelIndexing();
            await chats.StopIndexingAsync();
            await photos.WaitForIndexingAsync();
            if (documentPending)
            {
                try { await documentReady; } catch (OperationCanceledException) { }
            }
            if (entry.Kind == StorageKind.ChatModel)
            {
                // The model in use (or loading): its engine is freed before its file goes.
                DownloadableFile file = Host.Model?.ModelFile;
                if (file != null && Path.GetFileName(file.LocalFile) == entry.Key)
                    await Demo.ReleaseModelAsync();
            }
            if (entry.Kind == StorageKind.SearchModel)
            {
                AppSettings.Chat.SetSearchModelDeleted(entry.Key, true);
                if (Search.SearchModelFile == entry.Key)
                {
                    photos.TurnOff();
                    chats.TurnOff();
                    documents?.RemoveDocument();
                    await Search.Embedder.ReleaseWithGateAsync();
                }
            }
            if (entry.Kind == StorageKind.PhotoIndex)
            {
                photos.TurnOff();
                await photos.ClearIndexAsync();
            }
            if (entry.Kind == StorageKind.ChatIndex) chats.TurnOff();
            if (entry.Kind == StorageKind.Document && documents?.DocumentId == entry.Key) documents.RemoveDocument();
            if (entry.Kind == StorageKind.ImageModel)
            {
                await Host.Services.Get<ImageFeature>().TurnOffAndResetAsync();
            }
            await Task.Run(() => catalog.DeleteAsync(entry));
            if (entry.Kind == StorageKind.Attachments && Host.CurrentChatId.HasValue)
                reopen = Host.CurrentChatId.Value;
        }
        finally
        {
            UpdateSearchStorageState();
            photos.UpdateStatus();
            Host.Services.Get<ImageFeature>()?.UpdateStatus();
            Demo.RefreshModelList();
            if (PageVisible) { chats.StartIndexing(); photos.StartIndexing(); }
        }
        return reopen;
    }

    private async Task RestoreSearchModelDownloadAsync()
    {
        if (Host.IsBusy) return;
        if (!await Host.AskAsync("Download search model?", "Download " + Search.SearchModelName + " again? Document and photo search can then be enabled.", "Download", "Cancel")) return;
        _searchRestoreCancel = new CancellationTokenSource();
        using var model = Search.CreateEmbeddingModel();
        DownloadableFile file = model.DefaultModelFile;
        string name = Search.SearchModelName;
        CancellationToken cancel = _searchRestoreCancel.Token;
        try { await Demo.RunReloadAsync(() => DownloadSearchModelCoreAsync(file, name, cancel)); }
        catch (Exception) { /* Reported by the download operation. */ }
    }

    private async Task RestoreStorageSearchAsync(StorageEntry entry)
    {
        if (Host.IsBusy) throw new InvalidOperationException("Wait for active work to finish.");
        using EmbeddingModel model = entry.Key == Path.GetFileName(EmbeddingGemma2TextVision.ModelFile.LocalFile)
            ? new EmbeddingGemma2TextVision() : new EmbeddingGemma2Multimodal();
        if (Path.GetFileName(model.DefaultModelFile.LocalFile) != entry.Key) throw new InvalidOperationException("Unknown search model.");
        _searchRestoreCancel = new CancellationTokenSource();
        DownloadableFile file = model.DefaultModelFile;
        CancellationToken cancel = _searchRestoreCancel.Token;
        await Demo.RunReloadAsync(() => DownloadSearchModelCoreAsync(file, entry.Name, cancel));
    }

    private async Task DownloadSearchModelCoreAsync(DownloadableFile file, string name, CancellationToken cancel)
    {
        if (_restoreSearchButton != null) _restoreSearchButton.IsEnabled = false;
        foreach (Button b in _searchNoticeButtons) b.IsEnabled = false;
        try
        {
            await BackgroundDownloads.DownloadFileAsync(file, name,
                (total, received, percent) => Host.SetStatus($"Downloading search model: {percent:0}%"), cancel);
            if (!await Task.Run(() => file.IsLocalFileValid, cancel)) throw new IOException("The search model failed verification.");
            AppSettings.Chat.SetSearchModelDeleted(Path.GetFileName(file.LocalFile), false);
            Host.SetStatus("Search model downloaded. You can enable photo/chat search or attach a document again.");
        }
        catch (OperationCanceledException) { Host.SetStatus("Search-model download paused."); throw; }
        catch (Exception ex) { Host.SetStatus("Search-model download failed: " + ex.Message); throw; }
        finally
        {
            if (_restoreSearchButton != null) _restoreSearchButton.IsEnabled = true;
            foreach (Button b in _searchNoticeButtons) b.IsEnabled = true;
            UpdateSearchStorageState();
        }
    }
}
