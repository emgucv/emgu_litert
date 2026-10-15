// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if WINDOWS || IOS || ANDROID || MACCATALYST
using System;
using System.IO;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    internal sealed class StoragePage : ContentPage
    {
        private readonly StorageCatalog _catalog;
        private readonly Func<StorageEntry, Task> _delete;
        private readonly Func<StorageEntry, Task> _download;
        private readonly VerticalStackLayout _list = new() { Spacing = 10 };
        private readonly Label _total = new() { FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText };
        private readonly Label _status = new() { FontSize = 13, TextColor = Theme.SecondaryText };
        private readonly Button _refresh = new() { Text = "Refresh" };
        private readonly Button _cancelDownload = new() { Text = "Cancel download", IsVisible = false };
        private bool _working;

        public StoragePage(StorageCatalog catalog, Func<StorageEntry, Task> delete, Func<StorageEntry, Task> download, Action cancelDownload)
        {
            _catalog = catalog;
            _delete = delete;
            _download = download;
            Shell.SetNavBarIsVisible(this, false);
            NavigationPage.SetHasNavigationBar(this, false);
            BackgroundColor = Theme.PageBackground;
            var header = new HorizontalStackLayout { Spacing = 12, Children = {
                Theme.CircleButton(Theme.GlyphChevronLeft, async () => { if (!_working) await Navigation.PopAsync(); }),
                Theme.SectionTitle("Storage"), _refresh } };
            _refresh.Clicked += async (_, _) => await RefreshAsync();
            _cancelDownload.Clicked += (_, _) => { _cancelDownload.IsEnabled = false; cancelDownload(); _status.Text = "Pausing download..."; };
            Content = new ScrollView { Content = new VerticalStackLayout { Padding = 20, Spacing = 12,
                Children = { header, _total, _status, _cancelDownload, _list } } };
        }

        protected override void OnAppearing() { base.OnAppearing(); _ = RefreshAsync(); }
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
            foreach (StorageEntry entry in snapshot.Entries)
            {
                var remove = new Button { Text = "Delete", TextColor = Theme.Danger, BackgroundColor = Colors.Transparent,
                    IsEnabled = entry.CanDelete && (entry.Bytes > 0 || entry.Kind == StorageKind.Document), MinimumHeightRequest = 44 };
                SemanticProperties.SetDescription(remove, "Delete " + entry.Name);
                remove.Clicked += async (_, _) => await DeleteAsync(entry);
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                row.Add(new VerticalStackLayout { Spacing = 3, Children = {
                    new Label { Text = entry.Name, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                    new Label { Text = Amount(entry.Bytes) + (entry.InDatabase ? " stored data" : " on disk"), FontSize = 13, TextColor = Theme.Accent },
                    new Label { Text = entry.Detail, FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText } } }, 0, 0);
                if (entry.CanDelete) row.Add(remove, 1, 0);
                if (entry.CanDownload)
                {
                    var download = new Button { Text = "Download", BackgroundColor = Colors.Transparent, TextColor = Theme.Accent };
                    SemanticProperties.SetDescription(download, "Download " + entry.Name);
                    download.Clicked += async (_, _) => await DownloadAsync(entry);
                    row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    row.Add(download, 2, 0);
                }
                _list.Children.Add(Theme.Card(row));
            }
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
                return new DriveInfo(Path.GetPathRoot(FileSystem.AppDataDirectory)).AvailableFreeSpace;
#endif
            }
            catch (Exception) { return null; }
        }
    }
}
#endif
