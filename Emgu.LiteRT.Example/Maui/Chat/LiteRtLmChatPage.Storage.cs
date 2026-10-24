// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if WINDOWS || IOS || ANDROID || MACCATALYST
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;

namespace Maui.Demo.Lite
{
    public partial class LiteRtLmChatPage
    {
        private const string SearchDeletedPreference = "litert_lm_chat_search_deleted_";
        private Button _restoreSearchButton;
        // Notices with their own download button under the photo and past-chat search switches, shown while the search
        // model is deleted - otherwise the switches are just disabled, with the way back only in the Storage section.
        private readonly List<View> _searchNotices = new List<View>();
        private readonly List<Button> _searchNoticeButtons = new List<Button>();

        private View CreateSearchModelNotice()
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
        private CancellationTokenSource _searchRestoreCancel;
        private bool SearchDownloadRequired
        {
            get { try { return Preferences.Default.Get(SearchDeletedPreference + SelectedEmbeddingFile, false); } catch (Exception) { return false; } }
        }

        private View BuildStorageSection()
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

        private void UpdateSearchStorageState()
        {
            bool blocked = SearchDownloadRequired;
            if (_restoreSearchButton != null) _restoreSearchButton.IsVisible = blocked;
            foreach (View notice in _searchNotices) notice.IsVisible = blocked;
            if (blocked && _photoSwitch?.IsToggled == true) { SetPhotoSearchOff(); _photoIndexing?.Cancel(); }
            if (blocked && PastChats.On) { PastChats.TurnOff(); PastChats.CancelIndexing(); }
            if (_photoSwitch != null) _photoSwitch.IsEnabled = !blocked && EmbeddingSupportsImages;
            PastChats.SetSwitchEnabled(!IsPrivateChat && !blocked);
        }

        private StorageCatalog CreateStorageCatalog()
        {
            var models = Models.Select(m => (m.Name, Path.GetFileName(m.File.LocalFile), false)).ToList();
            using var vision = new EmbeddingGemma2TextVision();
            using var multimodal = new EmbeddingGemma2Multimodal();
            models.Add(("EmbeddingGemma 2 Text Vision 440M", Path.GetFileName(vision.DefaultModelFile.LocalFile), true));
            models.Add(("EmbeddingGemma 2 Multimodal 740M", Path.GetFileName(multimodal.DefaultModelFile.LocalFile), true));
            using var image = new ZImageTurbo();
            return new StorageCatalog(_store, Path.GetDirectoryName(Models[0].File.LocalFile), image.Folder,
                Path.Combine(FileSystem.CacheDirectory, "sharing-root", "chat-exports"), models);
        }

        private async Task OpenStorageAsync()
        {
            if (!CanChangeModel || _exportingChat) return;
            _sheet.Dismiss(null);
            var catalog = CreateStorageCatalog();
            await Navigation.PushAsync(new StoragePage(catalog, entry => DeleteStorageAsync(catalog, entry), RestoreStorageSearchAsync,
                () => _searchRestoreCancel?.Cancel()));
        }

        private async Task DeleteStorageAsync(StorageCatalog catalog, StorageEntry entry)
        {
            if (!CanChangeModel || _exportingChat) throw new InvalidOperationException("Wait for active work to finish first.");
            _reloadingCpuThreads = true;
            // Keep navigation cleanup aware of this operation, just like a model reload.
            _cpuReloadTask = DeleteStorageCoreAsync(catalog, entry);
            await _cpuReloadTask;
        }

        private async Task DeleteStorageCoreAsync(StorageCatalog catalog, StorageEntry entry)
        {
            try
            {
                DocumentState document = _document;
                bool documentPending = document?.Ready != null && !document.Ready.IsCompleted;
                // These tasks may share the same embedder load: cancel all producers before awaiting any of them.
                if (documentPending) RemoveDocument();
                _photoIndexing?.Cancel();
                PastChats.CancelIndexing();
                await PastChats.StopIndexingAsync();
                try { await (_photoIndexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
                if (documentPending)
                {
                    try { await document.Ready; } catch (OperationCanceledException) { }
                }
                if (entry.Kind == StorageKind.ChatModel)
                {
                    // A previous cancelled load may still hold an engine until it completes.
                    _loadCancel?.Cancel();
                    if (_loadTask != null) { try { await _loadTask; } catch (OperationCanceledException) { } }
                    _loadTask = null;
                    _loadingChoice = null;
                    ModelOption loaded = _loaded.HasValue ? Models.FirstOrDefault(m => m.Choice == _loaded.Value) : null;
                    if (loaded != null && Path.GetFileName(loaded.File.LocalFile) == entry.Key)
                        await CleanupModelAsync();
                }
                if (entry.Kind == StorageKind.SearchModel)
                {
                    Preferences.Default.Set(SearchDeletedPreference + entry.Key, true);
                    if (SelectedEmbeddingFile == entry.Key)
                    {
                        SetPhotoSearchOff();
                        PastChats.TurnOff();
                        RemoveDocument();
                        await Embedder.ReleaseWithGateAsync();
                    }
                }
                if (entry.Kind == StorageKind.PhotoIndex)
                {
                    SetPhotoSearchOff();
                    await _photoIndex.ClearAsync();
                }
                if (entry.Kind == StorageKind.ChatIndex) PastChats.TurnOff();
                if (entry.Kind == StorageKind.Document && _document?.Id == entry.Key) RemoveDocument();
                if (entry.Kind == StorageKind.ImageModel)
                {
                    await Images.TurnOffAndResetAsync();
                }
                await Task.Run(() => catalog.DeleteAsync(entry));
                if (entry.Kind == StorageKind.Attachments && _conversationId.HasValue)
                {
                    int id = _conversationId.Value;
                    // Reopen from the database, dropping deleted media held in the model's conversation and transcript.
                    _reloadingCpuThreads = false;
                    await CleanupModelAsync();
                    await OpenConversationAsync(id);
                    _reloadingCpuThreads = true;
                }
            }
            finally
            {
                _reloadingCpuThreads = false;
                UpdateSearchStorageState();
                UpdatePhotoStatus();
                Images.UpdateStatus();
                RefreshModelList();
                UpdateModelDownloadRows();
                _chipSpinner.IsVisible = _chipSpinner.IsRunning = _loadTask != null;
                UpdateExportChatButton();
                if (!ModelReady && !IsDownloaded(SelectedOption)) ShowDownloadPrompt(SelectedOption);
                if (_pageVisible) { PastChats.StartIndexing(); StartPhotoIndexing(); }
            }
        }

        private void SetPhotoSearchOff()
        {
            _photoSwitch.Toggled -= OnPhotoSearchToggled;
            _photoSwitch.IsToggled = false;
            _photoSwitch.Toggled += OnPhotoSearchToggled;
            Preferences.Default.Set(PhotoSearchPreference, false);
        }

        private async Task RestoreSearchModelDownloadAsync()
        {
            if (!CanChangeModel) return;
            if (!await DisplayAlertAsync("Download search model?", "Download " + SelectedEmbeddingName + " again? Document and photo search can then be enabled.", "Download", "Cancel")) return;
            _reloadingCpuThreads = true;
            _searchRestoreCancel = new CancellationTokenSource();
            using var model = CreateEmbeddingModel();
            _cpuReloadTask = DownloadSearchModelCoreAsync(model.DefaultModelFile, SelectedEmbeddingName, _searchRestoreCancel.Token);
            try { await _cpuReloadTask; } catch (Exception) { /* Reported by the download operation. */ }
        }

        private async Task RestoreStorageSearchAsync(StorageEntry entry)
        {
            if (!CanChangeModel) throw new InvalidOperationException("Wait for active work to finish.");
            using EmbeddingModel model = entry.Key == Path.GetFileName(EmbeddingGemma2TextVision.ModelFile.LocalFile)
                ? new EmbeddingGemma2TextVision() : new EmbeddingGemma2Multimodal();
            if (Path.GetFileName(model.DefaultModelFile.LocalFile) != entry.Key) throw new InvalidOperationException("Unknown search model.");
            _reloadingCpuThreads = true;
            _searchRestoreCancel = new CancellationTokenSource();
            _cpuReloadTask = DownloadSearchModelCoreAsync(model.DefaultModelFile, entry.Name, _searchRestoreCancel.Token);
            await _cpuReloadTask;
        }

        private async Task DownloadSearchModelCoreAsync(DownloadableFile file, string name, CancellationToken cancel)
        {
            _restoreSearchButton.IsEnabled = false;
            foreach (Button b in _searchNoticeButtons) b.IsEnabled = false;
            try
            {
                await BackgroundDownloads.DownloadFileAsync(file, name,
                    (total, received, percent) => SetStatus($"Downloading search model: {percent:0}%"), cancel);
                if (!await Task.Run(() => file.IsLocalFileValid, cancel)) throw new IOException("The search model failed verification.");
                Preferences.Default.Set(SearchDeletedPreference + Path.GetFileName(file.LocalFile), false);
                SetStatus("Search model downloaded. You can enable photo/chat search or attach a document again.");
            }
            catch (OperationCanceledException) { SetStatus("Search-model download paused."); throw; }
            catch (Exception ex) { SetStatus("Search-model download failed: " + ex.Message); throw; }
            finally
            {
                _reloadingCpuThreads = false;
                _restoreSearchButton.IsEnabled = true;
                foreach (Button b in _searchNoticeButtons) b.IsEnabled = true;
                UpdateSearchStorageState();
            }
        }
    }
}
#endif
