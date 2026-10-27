//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.IO;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// Settings > "Storage": the <see cref="StoragePage"/> lists what the app keeps on the device - the models with their
    /// caches, the app's own folders, the saved documents, the search indexes, the chat attachments, the database and
    /// the shared exports - with their sizes, to delete them (a <see cref="StorageCatalog"/> the app gives). Before an
    /// entry goes, the features with a stake in it (<see cref="IStorageParticipant"/>s in the page's services) stop
    /// their work on it; the model in use is released before its file; deleting the search model turns the searches
    /// off until it is downloaded again (the notices under their switches, and the button here), which this feature
    /// remembers in the settings ("search_deleted_&lt;file&gt;") and tells the <see cref="SearchServices"/>
    /// (<see cref="SearchServices.DownloadRequired"/>, <see cref="SearchServices.CreateDownloadNotice"/>).
    /// </summary>
    public sealed class StorageFeature : ChatFeature
    {
        /// <summary>The settings key prefix of a deleted search model (the model's file name follows).</summary>
        public const string DeletedSettingPrefix = "search_deleted_";

        private readonly Func<StorageCatalog> _catalog;
        private readonly SearchServices _search;
        private Button _restoreSearchButton;
        // Notices with their own download button under the photo and past-chat search switches, shown while the search
        // model is deleted - otherwise the switches are just disabled, with the way back only in the Storage section.
        private readonly List<View> _searchNotices = new List<View>();
        private readonly List<Button> _searchNoticeButtons = new List<Button>();
        private CancellationTokenSource _searchRestoreCancel;

        /// <summary>Create the feature.</summary>
        /// <param name="catalog">Makes the catalog of what the app keeps (read when the page opens)</param>
        /// <param name="search">The searches' shared services, for the search model that can be deleted and downloaded again; null without one</param>
        public StorageFeature(Func<StorageCatalog> catalog, SearchServices search = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _search = search;
        }

        /// <inheritdoc/>
        public override string Name => "storage";

        /// <inheritdoc/>
        public override string Title => "Storage";

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <summary>Whether the user deleted a search model (it isn't downloaded again until they ask).</summary>
        /// <param name="settings">The page's settings</param>
        /// <param name="modelFile">The model's file name</param>
        public static bool IsSearchModelDeleted(IChatPageSettings settings, string modelFile) =>
            settings != null && !string.IsNullOrEmpty(modelFile) && settings.Get(DeletedSettingPrefix + modelFile, false);

        /// <summary>Remember whether a search model was deleted.</summary>
        /// <param name="settings">The page's settings</param>
        /// <param name="modelFile">The model's file name</param>
        /// <param name="deleted">True when deleted, false once downloaded again</param>
        public static void SetSearchModelDeleted(IChatPageSettings settings, string modelFile, bool deleted)
        {
            if (settings != null && !string.IsNullOrEmpty(modelFile))
                settings.Set(DeletedSettingPrefix + modelFile, deleted);
        }

        private bool SearchDownloadRequired => _search != null && _search.IsDownloadRequired;

        private bool Exporting => Host.Services.Get<ExportFeature>()?.IsExporting == true;

        private IReadOnlyList<IStorageParticipant> Participants => Host.Services.GetAll<IStorageParticipant>().ToList();

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            if (_search == null)
                return;
            // The searches learn of a deleted model from here, and show the way back under their switches.
            Func<bool> previous = _search.DownloadRequired;
            _search.DownloadRequired = () => IsSearchModelDeleted(Host?.Settings, _search.IndexModel?.Invoke()) || previous?.Invoke() == true;
            _search.CreateDownloadNotice ??= CreateSearchModelNotice;
        }

        /// <inheritdoc/>
        public override void OnDetached()
        {
            _searchRestoreCancel?.Cancel();
            base.OnDetached();
        }

        /// <summary>Stop a search-model download in progress (leaving the page).</summary>
        public void CancelRestore() => _searchRestoreCancel?.Cancel();

        /// <summary>A notice for a search feature's section: shown while the search model is deleted, with a button to download it again.</summary>
        public View CreateSearchModelNotice()
        {
            var button = new Button { Text = "Download search model", HorizontalOptions = LayoutOptions.Start };
            SemanticProperties.SetDescription(button, "Download the search model again");
            button.Clicked += async (_, _) => await RestoreSearchModelDownloadAsync();
            var text = new Label { Text = "The search model was deleted, so this is off. Download it again to turn it on." };
            ChatResources.ApplyStyle(text, "ChatSectionNoteStyle");
            text.TextColor = ChatResources.FindColor("ChatDanger");
            var notice = new VerticalStackLayout { Spacing = 4, IsVisible = SearchDownloadRequired, Children = { text, button } };
            _searchNotices.Add(notice);
            _searchNoticeButtons.Add(button);
            return notice;
        }

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            var open = new Button { Text = "Storage...", HorizontalOptions = LayoutOptions.Fill };
            SemanticProperties.SetDescription(open, "Manage downloaded models, caches, documents and attachments");
            open.Clicked += async (_, _) => await OpenStorageAsync();
            var section = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    ChatPage.SectionTitle("Storage"),
                    open,
                    ChatPage.Note("See sizes and free space, and remove models, caches, saved documents or chat attachments."),
                    ChatPage.Note(BackgroundDownloads.Hint)
                }
            };
            if (_search != null)
            {
                _restoreSearchButton = new Button { Text = "Download search model again", IsVisible = SearchDownloadRequired };
                _restoreSearchButton.Clicked += async (_, _) => await RestoreSearchModelDownloadAsync();
                section.Children.Add(_restoreSearchButton);
            }
            RefreshSearchState();
            return section;
        }

        /// <summary>
        /// The search model's availability changed (another model chosen, or this feature's own deletion or download):
        /// the notices and the button follow, and the participants show their state again.
        /// </summary>
        public void RefreshSearchState() => NotifyChanged(null);

        private void NotifyChanged(StorageEntry entry)
        {
            if (Host == null)
                return;
            bool blocked = SearchDownloadRequired;
            if (_restoreSearchButton != null) _restoreSearchButton.IsVisible = blocked;
            foreach (View notice in _searchNotices) notice.IsVisible = blocked;
            foreach (IStorageParticipant participant in Participants)
            {
                try { participant.StorageChanged(entry); }
                catch (Exception) { }
            }
        }

        /// <summary>Open the storage page (nothing while busy or exporting).</summary>
        public async Task OpenStorageAsync()
        {
            if (Host.IsBusy || Exporting) return;
            Host.DismissSheet(null);
            StorageCatalog catalog = _catalog();
            await Host.Page.Navigation.PushAsync(new StoragePage(catalog, entry => DeleteStorageAsync(catalog, entry), RestoreStorageSearchAsync,
                () => _searchRestoreCancel?.Cancel()));
        }

        private async Task DeleteStorageAsync(StorageCatalog catalog, StorageEntry entry)
        {
            if (Host.IsBusy || Exporting) throw new InvalidOperationException("Wait for active work to finish first.");
            int? reopen = null;
            // Like a model reload: nothing new starts meanwhile, and leaving the page waits for it.
            await Host.RunReloadAsync(async () => reopen = await DeleteStorageCoreAsync(catalog, entry));
            if (reopen is int id)
            {
                // Reopen from the database, dropping deleted media held in the model's conversation and transcript.
                await Host.ReleaseModelAsync();
                await Host.OpenChatAsync(id);
            }
        }

        // Deletes the entry; the chat to reopen afterwards (its attachments were deleted), or null.
        private async Task<int?> DeleteStorageCoreAsync(StorageCatalog catalog, StorageEntry entry)
        {
            int? reopen = null;
            try
            {
                // Every participant at once: each cancels its work first and waits after (they may share one model load).
                await Task.WhenAll(Participants.Select(participant => participant.PrepareStorageDeleteAsync(entry)));
                if (entry.Kind == StorageKind.ChatModel)
                {
                    // The model in use (or loading): its engine is freed before its file goes.
                    DownloadableFile file = Host.Model?.ModelFile;
                    if (file != null && Path.GetFileName(file.LocalFile) == entry.Key)
                        await Host.ReleaseModelAsync();
                }
                if (entry.Kind == StorageKind.SearchModel)
                {
                    SetSearchModelDeleted(Host.Settings, entry.Key, true);
                    if (_search?.Embedder != null && _search.IndexModel?.Invoke() == entry.Key)
                        await _search.Embedder.ReleaseWithGateAsync();
                }
                await Task.Run(() => catalog.DeleteAsync(entry));
                if (entry.Kind == StorageKind.Attachments && Host.CurrentChatId.HasValue)
                    reopen = Host.CurrentChatId.Value;
            }
            finally
            {
                NotifyChanged(entry);
                Host.RefreshModelList();
            }
            return reopen;
        }

        private async Task RestoreSearchModelDownloadAsync()
        {
            if (Host.IsBusy || _search?.Choice == null) return;
            string name = _search.Choice.ModelName;
            if (!await Host.AskAsync("Download search model?", "Download " + name + " again? Document and photo search can then be enabled.", "Download", "Cancel")) return;
            _searchRestoreCancel = new CancellationTokenSource();
            using EmbeddingModel model = _search.Choice.CreateModel();
            DownloadableFile file = model.DefaultModelFile;
            CancellationToken cancel = _searchRestoreCancel.Token;
            try { await Host.RunReloadAsync(() => DownloadSearchModelCoreAsync(file, name, cancel)); }
            catch (Exception) { /* Reported by the download operation. */ }
        }

        // The storage page's Download button: the catalog's model with that file.
        private async Task RestoreStorageSearchAsync(StorageEntry entry)
        {
            if (Host.IsBusy) throw new InvalidOperationException("Wait for active work to finish.");
            StorageModel model = _catalog().Models.FirstOrDefault(m => m.IsSearch && m.File == entry.Key && m.Download != null)
                ?? throw new InvalidOperationException("Unknown search model.");
            _searchRestoreCancel = new CancellationTokenSource();
            CancellationToken cancel = _searchRestoreCancel.Token;
            await Host.RunReloadAsync(() => DownloadSearchModelCoreAsync(model.Download, entry.Name, cancel));
        }

        private async Task DownloadSearchModelCoreAsync(DownloadableFile file, string name, CancellationToken cancel)
        {
            if (_restoreSearchButton != null) _restoreSearchButton.IsEnabled = false;
            foreach (Button button in _searchNoticeButtons) button.IsEnabled = false;
            string key = Path.GetFileName(file.LocalFile);
            try
            {
                await BackgroundDownloads.DownloadFileAsync(file, name,
                    (total, received, percent) => Host.SetStatus($"Downloading search model: {percent:0}%"), cancel);
                if (!await Task.Run(() => file.IsLocalFileValid, cancel)) throw new IOException("The search model failed verification.");
                SetSearchModelDeleted(Host.Settings, key, false);
                Host.SetStatus("Search model downloaded. You can enable photo/chat search or attach a document again.");
            }
            catch (OperationCanceledException) { Host.SetStatus("Search-model download paused."); throw; }
            catch (Exception ex) { Host.SetStatus("Search-model download failed: " + ex.Message); throw; }
            finally
            {
                if (_restoreSearchButton != null) _restoreSearchButton.IsEnabled = true;
                foreach (Button button in _searchNoticeButtons) button.IsEnabled = true;
                NotifyChanged(new StorageEntry { Kind = StorageKind.SearchModel, Key = key, Name = name });
            }
        }
    }
}
