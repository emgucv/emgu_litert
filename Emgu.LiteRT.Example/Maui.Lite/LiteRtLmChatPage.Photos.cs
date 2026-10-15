//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Agents;

namespace Maui.Demo.Lite
{
    // Photo search: with Settings > "Search my photos" on (off by default), the most recent photos are indexed in the
    // background with the document search's embedding model (PhotoIndex), and Gemma 4's find_photos tool
    // (Emgu.LiteRT.LM.Agents' AgentTools.FindPhotos) shows the matches as thumbnails above the reply - the model
    // doesn't see them. The tool is offered with the setting off too, and then tells the model where to turn it on. Everything stays on the device; turning it off deletes the index.
    public partial class LiteRtLmChatPage
    {
        private const string PhotoSearchPreference = "litert_lm_chat_photo_search";
        // How many of the most recent photos are indexed: each takes about 1 s on a Mac's CPU and several seconds on a
        // phone's, in the background.
        private static int MaxIndexedPhotos => DeviceInfo.Idiom == DeviceIdiom.Desktop ? 1000 : 200;
        private const string PhotoInstructions =
            "When the user asks you to find or show their photos or pictures, use the find_photos tool with the request in their " +
            "words; the photos it finds are shown to the user, not to you.";

        private PhotoIndex _photoIndex;
        private Switch _photoSwitch;
        private Label _photoStatus;
        private CancellationTokenSource _photoIndexing;
        private Task _photoIndexTask;
        // The reply being written, so photos a tool call finds are shown above it.
        private StreamingBubble _agentBubble;

        private bool PhotoSearchOn => _photoSwitch != null && _photoSwitch.IsToggled;

        private View BuildPhotoSection()
        {
            _photoIndex = new PhotoIndex(_store);
            _photoIndex.Changed += () => MainThread.BeginInvokeOnMainThread(UpdatePhotoStatus);
            bool on = false;
            try { on = Preferences.Default.Get(PhotoSearchPreference, false); } catch (Exception) { }
            _photoSwitch = new Switch { IsToggled = on && EmbeddingSupportsImages, IsEnabled = EmbeddingSupportsImages, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _photoSwitch.Toggled += OnPhotoSearchToggled;
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new Label
            {
                Text = "Search my photos",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            row.Add(_photoSwitch, 1, 0);
            _photoStatus = new Label { FontFamily = Theme.TitleFont, FontSize = 13, TextColor = Theme.Accent, IsVisible = false };
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    row,
                    new Label
                    {
                        Text = string.Format(CultureInfo.InvariantCulture,
                            "Lets Gemma 4 find photos by what is in them, and where, when and how they were taken (\"show me beach photos from Spain last summer\"). Your {0} most recent photos are indexed on this device in the background - the first time takes a while - and nothing leaves it: places are looked up in a list of towns in the app (GeoNames, CC BY 4.0). Turning this off deletes the index.",
                            MaxIndexedPhotos),
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    BuildPhotoChargingSetting(),
                    new Label { Text = "On phones and tablets, indexing pauses at 20% battery or lower without power. It also pauses if the device reports excessive heat, and resumes automatically when conditions improve.", FontSize = 12, TextColor = Theme.SecondaryText },
                    _photoStatus
                }
            };
        }

        private async void OnPhotoSearchToggled(object sender, ToggledEventArgs e)
        {
            try { Preferences.Default.Set(PhotoSearchPreference, e.Value); } catch (Exception) { }
            if (e.Value)
            {
                bool allowed = false;
                try { allowed = await PhotoLibrary.RequestAccessAsync(); }
                catch (Exception ex)
                {
                    SetPhotoSearchOff();
                    ShowPhotoAccessError("Could not request photo access: " + ex.Message);
                    return;
                }
                if (!allowed)
                {
                    SetPhotoSearchOff();
#if MACCATALYST
                    ShowPhotoAccessError("Allow Emgu LiteRT in System Settings > Privacy & Security > Photos, then turn on Search my photos again.");
#else
                    ShowPhotoAccessError("Photo search needs access to your photos. Allow photo access in your device's settings, then try again.");
#endif
                    return;
                }
                StartPhotoIndexing();
            }
            else
            {
                _photoIndexing?.Cancel();
                try { await (_photoIndexTask ?? Task.CompletedTask); } catch (Exception) { }
                await _photoIndex.ClearAsync();
                UpdatePhotoStatus();
            }
        }

        private void ShowPhotoAccessError(string message)
        {
            _photoStatus.Text = message;
            _photoStatus.IsVisible = true;
            SetStatus(message);
        }

        // When an indexing pass last started; returning to the page or the app starts another at most this often.
        private DateTime _photoIndexStarted = DateTime.MinValue;
        private static readonly TimeSpan PhotoReindexInterval = TimeSpan.FromMinutes(1);
        private Window _resumeWindow;

        // The page is shown again, or the app came back to the foreground: photos taken, edited or deleted in the meantime
        // are indexed or forgotten (a pass over unchanged photos only reads the library's list).
        private void RefreshPhotoIndex()
        {
            if (DateTime.UtcNow - _photoIndexStarted >= PhotoReindexInterval)
                StartPhotoIndexing();
        }

        // Window.Resumed: the app returns to the foreground (on desktops also when its window is activated). The page's
        // Window is set after OnAppearing when it is pushed in the Shell, so this follows the property.
        protected override void OnPropertyChanged(string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            if (propertyName == nameof(Window))
                WatchAppResume();
        }

        private void WatchAppResume()
        {
            if (Window == _resumeWindow)
                return;
            if (_resumeWindow != null)
                _resumeWindow.Resumed -= OnAppResumed;
            _resumeWindow = Window;
            if (_resumeWindow != null)
                _resumeWindow.Resumed += OnAppResumed;
        }

        private void OnAppResumed(object sender, EventArgs e)
        {
            if (_pageVisible)
                StartChatIndexing();
            if (_pageVisible)
                RefreshPhotoIndex();
        }

        // Index the new photos in the background (incremental: only photos not yet indexed, or edited since).
        private void StartPhotoIndexing()
        {
            if (IsPrivateChat || _reloadingCpuThreads || !EmbeddingSupportsImages || !PhotoSearchOn || (_photoIndexTask != null && !_photoIndexTask.IsCompleted))
                return;
            _photoIndexStarted = DateTime.UtcNow;
            _photoIndexing = new CancellationTokenSource();
            CancellationToken cancel = _photoIndexing.Token;
            _photoIndexTask = IndexPhotosAsync(cancel);
        }

        private async Task IndexPhotosAsync(CancellationToken cancel)
        {
            try
            {
                Progress<string> stage = new Progress<string>(text =>
                {
                    _photoStatus.IsVisible = true;
                    _photoStatus.Text = text;
                });
                await WaitForPhotoWorkAsync(cancel);
                var embedder = await EnsureEmbedderAsync(stage, cancel);
                // Waits while the chat answers, so indexing doesn't slow the reply down.
                await _photoIndex.IndexAsync(embedder, (image, token) => RunIndexEmbeddingAsync(
                    () => embedder.EmbedImage(image, visionTokensPerImage: PhotoIndex.VisionTokens),
                    reason => MainThread.BeginInvokeOnMainThread(() => { _photoPauseReason = reason; UpdatePhotoStatus(); }),
                    token, photos: true), MaxIndexedPhotos, () => _busy, cancel, WaitForPhotoWorkAsync);
                UiStallWatchdog.Mark(string.Format("Photos indexed: {0}", _photoIndex.Count));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                UiStallWatchdog.Mark("Photo indexing failed: " + ex.Message);
                _photoStatus.IsVisible = true;
                _photoStatus.Text = "Indexing stopped: " + ex.Message;
            }
            UpdatePhotoStatus();
        }

        private void UpdatePhotoStatus()
        {
            if (_photoStatus == null)
                return;
            if (!PhotoSearchOn)
            {
                _photoStatus.IsVisible = false;
                return;
            }
            _photoStatus.IsVisible = true;
            if (_photoPauseReason != null)
            {
                _photoStatus.Text = $"Photo indexing paused: {_photoPauseReason}. {_photoIndex.Count} photos indexed.";
                return;
            }
            _photoStatus.Text = _photoIndex.IsIndexing && _photoIndex.Total > 0
                ? string.Format(CultureInfo.InvariantCulture, "Indexing photos: {0} of {1}", _photoIndex.Count, _photoIndex.Total)
                : string.Format(CultureInfo.InvariantCulture, "{0} photos indexed, {1} with a place", _photoIndex.Count, _photoIndex.WithPlaceCount);
        }

        // The find_photos tool: search the index, show the matches above the reply, and tell the model what was found.
        private async Task<string> FindPhotosAsync(string request, CancellationToken cancel)
        {
            if (!PhotoSearchOn)
                return string.Format(CultureInfo.InvariantCulture,
                    "Photo search is turned off, so the user's photos can't be searched. Tell the user they can turn on \"Search my photos\" in {0}: their recent photos are then indexed on this device (the first time takes a while), and nothing leaves it.",
                    _settingsButton.IsVisible ? "this chat's settings (the gear button at the top)" : "the settings panel beside the chat");
            if (_embedder == null || _photoIndex.Count == 0)
                return _photoIndex.IsIndexing
                    ? "The photos are still being indexed - try again in a few minutes."
                    : "No photos are indexed yet.";
            // What, where, when, kind and camera, from the request; places and cameras as the indexed photos name them.
            PhotoQuery query = PhotoRequest.Parse(request, DateTime.Now, _photoIndex.KnownPlaces(), _photoIndex.KnownCameras());
            (List<(IndexedPhoto Photo, float Score)> found, int matching) = await _photoIndex.SearchAsync(_embedder, _embedderGate, query, cancel);
            // Photos deleted since the last indexing pass: forgotten, and the search run again, since the scores of the
            // rest count relative to the best one.
            for (int attempt = 0; attempt < 3 && found.Count > 0; attempt++)
            {
                HashSet<string> existing = await PhotoLibrary.GetExistingAsync(found.Select(f => f.Photo.Id).ToList());
                if (existing == null)
                    break;
                List<string> gone = found.Select(f => f.Photo.Id).Where(id => !existing.Contains(id)).ToList();
                if (gone.Count == 0)
                    break;
                UiStallWatchdog.Mark(string.Format("find_photos: {0} found photos no longer in the library", gone.Count));
                await _photoIndex.RemoveAsync(gone);
                (found, matching) = await _photoIndex.SearchAsync(_embedder, _embedderGate, query, cancel);
            }
            UiStallWatchdog.Mark(string.Format("find_photos {0}: {1} found of {2} matching{3}", query, found.Count, matching,
                found.Count > 0 && query.Description != null ? string.Format(CultureInfo.InvariantCulture, ", best {0:F3}", found[0].Score) : ""));
            if (found.Count == 0)
            {
                string why = matching == 0 && query.Place != null && _photoIndex.WithPlaceCount == 0
                    ? " None of the indexed photos has a location."
                    : matching == 0 ? " No indexed photo was taken then or there." : "";
                return string.Format(CultureInfo.InvariantCulture, "No photos {0} were found among the {1} most recent photos.{2}",
                    query, _photoIndex.Count, why);
            }
            StreamingBubble bubble = _agentBubble;
            await MainThread.InvokeOnMainThreadAsync(() => AddPhotoStrip(bubble, found.Select(f => f.Photo).ToList()));
            // When and where, for the model to say (it can't see the photos).
            string details = string.Join("; ", found.Take(5).Select(f => PhotoCaption(f.Photo, true)));
            return string.Format(CultureInfo.InvariantCulture,
                "Found {0} photo{1} {2}{3}: {4}{5}. They are now shown to the user above your reply. You can't see them: say briefly what you found (when and where, if known), without describing them.",
                found.Count, found.Count == 1 ? "" : "s", query,
                query.Description == null && matching > found.Count ? string.Format(CultureInfo.InvariantCulture, " (the {0} most recent of {1})", found.Count, matching) : "",
                details, found.Count > 5 ? "; ..." : "");
        }

        // "12 Aug 2025, Valencia" (short, under a thumbnail) or "taken 12 Aug 2025 in Valencia, Spain with a Google Pixel 8".
        private static string PhotoCaption(IndexedPhoto photo, bool full)
        {
            string date = photo.Taken.LocalDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            if (!full)
                return photo.Place != null ? date + "\n" + photo.Place.Split(',')[0] : date;
            string text = "taken " + date;
            if (photo.Place != null)
                text += " in " + photo.Place;
            if (photo.Camera != null)
                text += " with " + photo.Camera;
            if (photo.Kinds.Count > 0)
                text += " (" + string.Join(", ", photo.Kinds) + ")";
            return text;
        }

        // A row of thumbnails above the reply; tapping one shows it larger.
        private void AddPhotoStrip(StreamingBubble bubble, List<IndexedPhoto> photos)
        {
            HorizontalStackLayout row = new HorizontalStackLayout { Spacing = 8 };
            foreach (IndexedPhoto photo in photos)
            {
                Image image = new Image { HeightRequest = 96, WidthRequest = 96, Aspect = Aspect.AspectFill };
                Border frame = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Stroke = Colors.Transparent,
                    BackgroundColor = Theme.TileBackground,
                    Content = image
                };
                // When and where, under the photo.
                Label caption = new Label
                {
                    Text = PhotoCaption(photo, false),
                    FontFamily = Theme.BodyFont,
                    FontSize = 11,
                    TextColor = Theme.SecondaryText,
                    WidthRequest = 96,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2
                };
                VerticalStackLayout tile = new VerticalStackLayout { Spacing = 2, Children = { frame, caption } };
                string photoId = photo.Id;
                tile.OnTap(() => ShowPhoto(photoId));
                SemanticProperties.SetDescription(tile, "Photo " + PhotoCaption(photo, true));
                row.Children.Add(tile);
                _ = LoadThumbnailAsync(image, photoId);
            }
            ScrollView strip = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                Margin = new Thickness(6, 0),
                Content = row
            };
            int index = bubble != null ? _transcript.Children.IndexOf(bubble.Row) : -1;
            if (index < 0)
                _transcript.Children.Add(strip);
            else
                _transcript.Children.Insert(index, strip);
            ScrollToEnd();
        }

        private static async Task LoadThumbnailAsync(Image image, string photoId)
        {
            byte[] jpeg = await PhotoLibrary.LoadImageAsync(photoId, 256);
            if (jpeg != null)
                image.Source = ImageSource.FromStream(() => new System.IO.MemoryStream(jpeg));
        }

        private async void ShowPhoto(string photoId)
        {
            byte[] jpeg = await PhotoLibrary.LoadImageAsync(photoId, 1600);
            if (jpeg == null)
                return;
            Image image = new Image
            {
                Source = ImageSource.FromStream(() => new System.IO.MemoryStream(jpeg)),
                Aspect = Aspect.AspectFit,
                HeightRequest = Math.Max(240, Height * 0.6)
            };
            await _sheet.ShowViewAsync("Photo", image, "Close");
        }
    }
}

#endif
