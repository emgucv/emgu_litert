//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.IO;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The storage page the storage feature pushes (<see cref="Features.StorageFeature"/>): what the app keeps on the
    /// device - the models with their caches, the app's own folders, the saved documents, the search indexes, the chat
    /// attachments, the database and the shared exports - with their sizes and the free space, each with a Delete
    /// button (and Download for a search model that was deleted), from a <see cref="StorageCatalog"/>. Styled with the
    /// chat's theme keys.
    /// </summary>
    public sealed class StoragePage : ContentPage
    {
        private readonly StorageCatalog _catalog;
        private readonly Func<StorageEntry, Task> _delete;
        private readonly Func<StorageEntry, Task> _download;
        private readonly VerticalStackLayout _list = new VerticalStackLayout { Spacing = 10 };
        private readonly Label _total = new Label();
        private readonly Label _status = new Label();
        private readonly Button _refresh = new Button { Text = "Refresh" };
        private readonly Button _cancelDownload = new Button { Text = "Cancel download", IsVisible = false };
        private bool _working;

        /// <summary>Create the page.</summary>
        /// <param name="catalog">What is kept on the device</param>
        /// <param name="delete">Deletes an entry (after the work that uses it stopped)</param>
        /// <param name="download">Downloads a search model again (an entry with <see cref="StorageEntry.CanDownload"/>)</param>
        /// <param name="cancelDownload">Cancels the download</param>
        public StoragePage(StorageCatalog catalog, Func<StorageEntry, Task> delete, Func<StorageEntry, Task> download, Action cancelDownload)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _delete = delete ?? throw new ArgumentNullException(nameof(delete));
            _download = download ?? (entry => Task.CompletedTask);
            ChatResources.EnsureTheme();
            Shell.SetNavBarIsVisible(this, false);
            NavigationPage.SetHasNavigationBar(this, false);
            ChatResources.SetThemeColor(this, BackgroundColorProperty, "ChatPageBackground");
            ChatResources.ApplyStyle(_total, "ChatListTitleStyle");
            ChatResources.ApplyStyle(_status, "ChatSectionNoteStyle");
            ChatResources.ApplyStyle(_refresh, "ChatHeaderButtonStyle");
            ChatResources.ApplyStyle(_cancelDownload, "ChatHeaderButtonStyle");
            var back = new Button { Text = "‹ Back" };
            ChatResources.ApplyStyle(back, "ChatHeaderButtonStyle");
            SemanticProperties.SetDescription(back, "Back to the chat");
            back.Clicked += async (_, _) => { if (!_working) await Navigation.PopAsync(); };
            var header = new HorizontalStackLayout { Spacing = 12, Children = { back, ChatPage.SectionTitle("Storage"), _refresh } };
            _refresh.Clicked += async (_, _) => await RefreshAsync();
            _cancelDownload.Clicked += (_, _) => { _cancelDownload.IsEnabled = false; cancelDownload?.Invoke(); _status.Text = "Pausing download..."; };
            Content = new ScrollView
            {
                Content = new VerticalStackLayout { Padding = 20, Spacing = 12, Children = { header, _total, _status, _cancelDownload, _list } }
            };
        }

        /// <inheritdoc/>
        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = RefreshAsync();
        }

        /// <inheritdoc/>
        protected override bool OnBackButtonPressed() => _working || base.OnBackButtonPressed();

        private async Task RefreshAsync()
        {
            if (_working) return;
            _working = true;
            _refresh.IsEnabled = _list.IsEnabled = false;
            _status.Text = "Measuring storage...";
            try { await RenderAsync(); }
            catch (Exception ex) { _status.Text = "Could not read storage: " + ex.Message; }
            finally { _working = false; _refresh.IsEnabled = _list.IsEnabled = true; }
        }

        private async Task RenderAsync()
        {
            StorageSnapshot snapshot = await Task.Run(_catalog.ReadAsync);
            long? free = FreeBytes();
            _total.Text = "Managed storage: " + Amount(snapshot.DiskBytes) + " · Free: " + (free.HasValue ? Amount(free.Value) : "Unavailable");
            _status.Text = "Model sizes include partial downloads and CPU/GPU caches. Database items show stored-data estimates; the database file is counted once in the total. Deleting a loaded model releases it first.";
            _list.Children.Clear();
            Color accent = ChatResources.FindColor("ChatAccent");
            foreach (StorageEntry entry in snapshot.Entries)
            {
                // Nothing to delete for an item that isn't on the device (e.g. a model never downloaded).
                bool hasData = entry.Bytes > 0 || entry.Kind == StorageKind.Document;
                var remove = new Button { Text = "Delete", TextColor = ChatResources.FindColor("ChatDanger"), BackgroundColor = Colors.Transparent,
                    IsEnabled = entry.CanDelete && hasData, MinimumHeightRequest = 44 };
                SemanticProperties.SetDescription(remove, "Delete " + entry.Name);
                remove.Clicked += async (_, _) => await DeleteAsync(entry);
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                var name = new Label { Text = entry.Name };
                ChatResources.ApplyStyle(name, "ChatListTitleStyle");
                var amount = new Label { Text = hasData ? Amount(entry.Bytes) + (entry.InDatabase ? " stored data" : " on disk") : "Not on this device", FontSize = 13, TextColor = accent };
                var detail = new Label { Text = entry.Detail, LineBreakMode = LineBreakMode.WordWrap };
                ChatResources.ApplyStyle(detail, "ChatListDetailStyle");
                row.Add(new VerticalStackLayout { Spacing = 3, Children = { name, amount, detail } }, 0, 0);
                if (entry.CanDelete && hasData) row.Add(remove, 1, 0);
                if (entry.CanDownload)
                {
                    var download = new Button { Text = "Download", BackgroundColor = Colors.Transparent, TextColor = accent };
                    SemanticProperties.SetDescription(download, "Download " + entry.Name);
                    download.Clicked += async (_, _) => await DownloadAsync(entry);
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    row.Add(download, 2, 0);
                }
                _list.Children.Add(Card(row));
            }
        }

        private static Border Card(View content)
        {
            var card = new Border
            {
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Padding = new Thickness(16),
                Content = content
            };
            ChatResources.SetThemeColor(card, BackgroundColorProperty, "ChatSurface");
            return card;
        }

        private async Task DeleteAsync(StorageEntry entry)
        {
            if (_working) return;
            _working = true;
            _refresh.IsEnabled = _list.IsEnabled = false;
            try
            {
                if (!await DisplayAlertAsync("Delete " + entry.Name + "?", entry.Detail + "\n\n" + Amount(entry.Bytes)
                    + (entry.InDatabase ? " of stored data. Database space will be compacted after deletion." : " on disk."), "Delete", "Cancel")) return;
                _status.Text = "Stopping active work and deleting " + entry.Name + "...";
                await _delete(entry);
                await RenderAsync();
            }
            catch (Exception ex)
            {
                _status.Text = "Deletion could not finish: " + ex.Message + " Refresh to see remaining files.";
                await DisplayAlertAsync("Could not delete storage", ex.Message, "OK");
            }
            finally { _working = false; _refresh.IsEnabled = _list.IsEnabled = true; }
        }

        private async Task DownloadAsync(StorageEntry entry)
        {
            if (_working) return;
            _working = true;
            _refresh.IsEnabled = _list.IsEnabled = false;
            try
            {
                if (!await DisplayAlertAsync("Download search model?", "Download " + entry.Name + " over the internet? It is about 400–500 MB. Search can then be enabled again.", "Download", "Cancel")) return;
                _status.Text = "Downloading " + entry.Name + "...";
                _cancelDownload.IsVisible = _cancelDownload.IsEnabled = true;
                await _download(entry);
                await RenderAsync();
            }
            catch (OperationCanceledException) { _status.Text = "Download paused. Download again to continue."; }
            catch (Exception ex) { _status.Text = "Download failed: " + ex.Message; }
            finally { _working = false; _refresh.IsEnabled = _list.IsEnabled = true; _cancelDownload.IsVisible = false; }
        }

        private static string Amount(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.00} GB"
            : bytes >= 1_000_000 ? $"{bytes / 1e6:0.0} MB" : bytes >= 1000 ? $"{bytes / 1e3:0.0} KB" : bytes + " B";

        private static long? FreeBytes()
        {
            try
            {
#if ANDROID
                using var stats = new Android.OS.StatFs(FileSystem.AppDataDirectory);
                return stats.AvailableBytes;
#elif IOS || MACCATALYST
                return checked((long)Foundation.NSFileManager.DefaultManager.GetFileSystemAttributes(FileSystem.AppDataDirectory).FreeSize);
#else
                return new DriveInfo(System.IO.Path.GetPathRoot(FileSystem.AppDataDirectory)).AvailableFreeSpace;
#endif
            }
            catch (Exception) { return null; }
        }
    }
}
