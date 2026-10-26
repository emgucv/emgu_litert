//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// Photo search: with the settings' "Search my photos" on (off by default), the most recent photos are indexed in the
    /// background with the shared search model (<see cref="SearchServices"/>; the model must take images), and the
    /// find_photos tool (<see cref="AgentTools.FindPhotos"/>) shows the matches as thumbnails above the reply - the model
    /// doesn't see them. The tool is offered with the setting off too, and then tells the model where to turn it on.
    /// Everything stays on the device: places are looked up in an embedded list of towns (GeoNames, CC BY 4.0); turning
    /// the search off deletes the index, kept in the chats' store (<see cref="IPhotoIndexStore"/>). A tool group that
    /// comes when the message is about photos; the indexing runs when the page shows and when the app comes back (photos
    /// taken meanwhile), while no model is reloading, and on a phone pauses on a hot device or a low battery - or, with
    /// "Index photos only while charging" on, without a charger. The app asks the user for photo access through the
    /// platform (iOS and Mac Catalyst: NSPhotoLibraryUsageDescription in Info.plist; Android: the permissions this
    /// assembly declares).
    /// </summary>
    public sealed class PhotoSearchFeature : ChatFeature
    {
        /// <summary>The switch's settings key.</summary>
        public const string SettingKey = "photo_search";

        /// <summary>The "Index photos only while charging" settings key.</summary>
        public const string ChargingSettingKey = "photos_only_charging";

        private readonly IPhotoIndexStore _store;
        private readonly SearchServices _search;
        private bool _visible;
        private bool _onlyWhileCharging;

        /// <summary>Create the feature.</summary>
        /// <param name="store">The chats' store (e.g. Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore)</param>
        /// <param name="search">The search model and when indexing may run</param>
        public PhotoSearchFeature(IPhotoIndexStore store, SearchServices search)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _search = search ?? throw new ArgumentNullException(nameof(search));
        }

    /// <summary>How many of the most recent photos are indexed (each takes about 1 s on a Mac's CPU and several seconds on a phone's, in the background): 1,000 on a desktop, 200 elsewhere.</summary>
    public int MaxIndexedPhotos { get; set; } = DeviceInfo.Idiom == DeviceIdiom.Desktop ? 1000 : 200;

    private string _photoPauseReason;
    private PhotoIndex _photoIndex;
    private Switch _photoSwitch;
    private Label _photoStatus;
    private CancellationTokenSource _photoIndexing;
    private Task _photoIndexTask;
    private bool _started;

    /// <inheritdoc/>
    public override string Name => ChatPhotos.GroupName;

    /// <inheritdoc/>
    public override string Title => "Search my photos";

    /// <summary>The switch is the feature's own setting, not the page's feature switch.</summary>
    public override bool PersistEnabled => false;

    private SearchEmbedder Embedder => _search.Embedder;

    /// <summary>Whether the search is on (the switch).</summary>
    public bool On => _photoSwitch != null && _photoSwitch.IsToggled;

    private bool SwitchAllowed => _search.SupportsImages && !_search.IsDownloadRequired;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        _onlyWhileCharging = host.Settings.Get(ChargingSettingKey, false);
        _photoIndex = new PhotoIndex(_store);
        _photoIndex.Changed += () => MainThread.BeginInvokeOnMainThread(UpdateStatus);
    }

    /// <inheritdoc/>
    public override void OnDetached()
    {
        CancelIndexing();
        base.OnDetached();
    }

    /// <inheritdoc/>
    public override IEnumerable<ToolGroup> ToolGroups()
    {
        yield return new ToolGroup(ChatPhotos.GroupName, ChatPhotos.GroupSummary, ChatPhotos.GroupKeywords,
            () => new AITool[] { AgentTools.FindPhotos(FindAsync) },
            () => ChatPhotos.Instructions);
    }

    // The page shown: photos added since the app last ran are indexed; shown again or the app back: those taken meanwhile.
    /// <inheritdoc/>
    public override void OnAppearing()
    {
        _visible = true;
        if (_started)
            RefreshIndex();
        else
        {
            _started = true;
            StartIndexing();
        }
    }

    /// <inheritdoc/>
    public override void OnDisappearing()
    {
        _visible = false;
    }

    /// <inheritdoc/>
    public override void OnAppResumed()
    {
        if (_visible)
            RefreshIndex();
    }

    /// <inheritdoc/>
    public override void OnPrivateModeChanged(bool isPrivate) => SetSwitchEnabled(!isPrivate && SwitchAllowed);

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        bool on = Host.Settings.Get(SettingKey, false);
        bool images = _search.SupportsImages;
        _photoSwitch = new Switch { IsToggled = on && images, IsEnabled = SwitchAllowed && !Host.IsPrivate, OnColor = ChatResources.FindColor("ChatAccent"), VerticalOptions = LayoutOptions.Center };
        _photoSwitch.Toggled += OnPhotoSearchToggled;
        SemanticProperties.SetDescription(_photoSwitch, Title);
        var label = new Label { Text = Title };
        ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        row.Add(label, 0, 0);
        row.Add(_photoSwitch, 1, 0);
        _photoStatus = new Label { IsVisible = false };
        ChatResources.ApplyStyle(_photoStatus, "ChatSectionNoteStyle");
        var section = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                row,
                ChatPage.Note(string.Format(CultureInfo.InvariantCulture,
                    "Lets the assistant find photos by what is in them, and where, when and how they were taken (\"show me beach photos from Spain last summer\"). Your {0} most recent photos are indexed on this device in the background - the first time takes a while - and nothing leaves it: places are looked up in a list of towns in the app (GeoNames, CC BY 4.0). Turning this off deletes the index.",
                    MaxIndexedPhotos))
            }
        };
        View notice = _search.CreateDownloadNotice?.Invoke();
        if (notice != null)
            section.Children.Add(notice);
        // The charging rule (phones only: a desktop is always plugged in).
        View charging = ChatPage.SwitchRow("Index photos only while charging", null, _onlyWhileCharging, value =>
        {
            _onlyWhileCharging = value;
            Host.Settings.Set(ChargingSettingKey, value);
        });
        charging.IsVisible = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
        section.Children.Add(charging);
        section.Children.Add(ChatPage.Note("On phones and tablets, indexing pauses at 20% battery or lower without power. It also pauses if the device reports excessive heat, and resumes automatically when conditions improve."));
        section.Children.Add(_photoStatus);
        UpdateStatus();
        return section;
    }

    private async void OnPhotoSearchToggled(object sender, ToggledEventArgs e)
    {
        Host.Settings.Set(SettingKey, e.Value);
        if (e.Value)
        {
            bool allowed = false;
            try { allowed = await PhotoLibrary.RequestAccessAsync(); }
            catch (Exception ex)
            {
                TurnOff();
                ShowPhotoAccessError("Could not request photo access: " + ex.Message);
                return;
            }
            if (!allowed)
            {
                TurnOff();
#if MACCATALYST
                ShowPhotoAccessError("Allow this app in System Settings > Privacy & Security > Photos, then turn on Search my photos again.");
#else
                ShowPhotoAccessError("Photo search needs access to your photos. Allow photo access in your device's settings, then try again.");
#endif
                return;
            }
            StartIndexing();
        }
        else
        {
            _photoIndexing?.Cancel();
            try { await (_photoIndexTask ?? Task.CompletedTask); } catch (Exception) { }
            await _photoIndex.ClearAsync();
            UpdateStatus();
        }
    }

    private void ShowPhotoAccessError(string message)
    {
        _photoStatus.Text = message;
        _photoStatus.IsVisible = true;
        Host.SetStatus(message);
    }

    // When an indexing pass last started; returning to the page or the app starts another at most this often.
    private DateTime _photoIndexStarted = DateTime.MinValue;
    private static readonly TimeSpan PhotoReindexInterval = TimeSpan.FromMinutes(1);

    /// <summary>The page is shown again, or the app came back to the foreground: photos taken, edited or deleted in the meantime are indexed or forgotten (at most once a minute; a pass over unchanged photos only reads the library's list).</summary>
    public void RefreshIndex()
    {
        if (DateTime.UtcNow - _photoIndexStarted >= PhotoReindexInterval)
            StartIndexing();
    }

    /// <summary>Index the new photos in the background (incremental: only photos not yet indexed, or edited since).</summary>
    public void StartIndexing()
    {
        if (Host == null || Host.IsPrivate || Host.IsReloadingModel || !_search.SupportsImages || !On || _search.Embedder == null || (_photoIndexTask != null && !_photoIndexTask.IsCompleted))
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
                if (_photoStatus == null) return;
                _photoStatus.IsVisible = true;
                _photoStatus.Text = text;
            });
            await WaitForPhotoWorkAsync(cancel);
            var embedder = await Embedder.EnsureAsync(stage, cancel);
            // Waits while the chat answers, so indexing doesn't slow the reply down.
            await _photoIndex.IndexAsync(embedder, (image, token) => _search.Workload.RunEmbeddingAsync(Embedder,
                () => embedder.EmbedImage(image, visionTokensPerImage: PhotoIndex.VisionTokens),
                reason => MainThread.BeginInvokeOnMainThread(() => { _photoPauseReason = reason; UpdateStatus(); }),
                token, _onlyWhileCharging), MaxIndexedPhotos, () => Host.IsReplying, cancel, WaitForPhotoWorkAsync);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_photoStatus != null)
            {
                _photoStatus.IsVisible = true;
                _photoStatus.Text = "Indexing stopped: " + ex.Message;
            }
        }
        UpdateStatus();
    }

    private Task WaitForPhotoWorkAsync(CancellationToken cancel) => _search.Workload.WaitAsync(reason =>
        MainThread.BeginInvokeOnMainThread(() => { _photoPauseReason = reason; UpdateStatus(); }), cancel, _onlyWhileCharging);

    /// <summary>Show the index's state in the settings (how many photos, paused and why).</summary>
    public void UpdateStatus()
    {
        if (_photoStatus == null)
            return;
        if (!On)
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

    /// <summary>The find_photos tool: search the index, show the matches above the reply, and tell the model what was found.</summary>
    /// <param name="request">What to find, in the user's words</param>
    /// <param name="cancel">Stops the search</param>
    public async Task<string> FindAsync(string request, CancellationToken cancel)
    {
        if (!On)
            return string.Format(CultureInfo.InvariantCulture,
                "Photo search is turned off, so the user's photos can't be searched. Tell the user they can turn on \"Search my photos\" in {0}: their recent photos are then indexed on this device (the first time takes a while), and nothing leaves it.",
                Host.SettingsLocation);
        if (Embedder.Model == null || _photoIndex.Count == 0)
            return _photoIndex.IsIndexing
                ? "The photos are still being indexed - try again in a few minutes."
                : "No photos are indexed yet.";
        // What, where, when, kind and camera, from the request; places and cameras as the indexed photos name them.
        PhotoQuery query = PhotoRequest.Parse(request, DateTime.Now, _photoIndex.KnownPlaces(), _photoIndex.KnownCameras());
        (List<(IndexedPhoto Photo, float Score)> found, int matching) = await _photoIndex.SearchAsync(Embedder.Model, Embedder.Gate, query, cancel);
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
            await _photoIndex.RemoveAsync(gone);
            (found, matching) = await _photoIndex.SearchAsync(Embedder.Model, Embedder.Gate, query, cancel);
        }
        if (found.Count == 0)
        {
            string why = matching == 0 && query.Place != null && _photoIndex.WithPlaceCount == 0
                ? " None of the indexed photos has a location."
                : matching == 0 ? " No indexed photo was taken then or there." : "";
            return string.Format(CultureInfo.InvariantCulture, "No photos {0} were found among the {1} most recent photos.{2}",
                query, _photoIndex.Count, why);
        }
        await MainThread.InvokeOnMainThreadAsync(() => Host.AddCardToReply(CreatePhotoStrip(found.Select(f => f.Photo).ToList())));
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
    private View CreatePhotoStrip(List<IndexedPhoto> photos)
    {
        HorizontalStackLayout row = new HorizontalStackLayout { Spacing = 8 };
        foreach (IndexedPhoto photo in photos)
        {
            Image image = new Image { HeightRequest = 96, WidthRequest = 96, Aspect = Aspect.AspectFill };
            Border frame = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Stroke = Colors.Transparent,
                Content = image
            };
            ChatResources.SetThemeColor(frame, VisualElement.BackgroundColorProperty, "ChatRowBorder");
            // When and where, under the photo.
            Label caption = new Label { Text = PhotoCaption(photo, false), WidthRequest = 96, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            ChatResources.ApplyStyle(caption, "ChatListDetailStyle");
            VerticalStackLayout tile = new VerticalStackLayout { Spacing = 2, Children = { frame, caption } };
            string photoId = photo.Id;
            TapGestureRecognizer tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => ShowPhoto(photoId);
            tile.GestureRecognizers.Add(tap);
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
        return strip;
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
            HeightRequest = Math.Max(240, Host.PageSize.Height * 0.6)
        };
        await Host.ShowSheetAsync("Photo", image, "Close");
    }

    /// <summary>Stop indexing without waiting.</summary>
    public void CancelIndexing() => _photoIndexing?.Cancel();

    /// <summary>Wait for the indexing to end.</summary>
    public async Task WaitForIndexingAsync()
    {
        try { await (_photoIndexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
    }

    /// <summary>Forget every indexed photo (the search model changed, or the index was deleted).</summary>
    public Task ClearIndexAsync() => _photoIndex.ClearAsync();

    /// <summary>Turn the search off without deleting the index again (the index or the search model was deleted elsewhere).</summary>
    public void TurnOff()
    {
        Host?.Settings.Set(SettingKey, false);
        if (_photoSwitch == null)
            return;
        _photoSwitch.Toggled -= OnPhotoSearchToggled;
        _photoSwitch.IsToggled = false;
        _photoSwitch.Toggled += OnPhotoSearchToggled;
    }

    /// <summary>Turn the search off as the user would, deleting the index.</summary>
    public void SwitchOff()
    {
        if (_photoSwitch != null) _photoSwitch.IsToggled = false;
    }

    /// <summary>Enable or disable the switch (a private chat, the search model deleted or one without images).</summary>
    /// <param name="enabled">Whether it can be used</param>
    public void SetSwitchEnabled(bool enabled)
    {
        if (_photoSwitch != null) _photoSwitch.IsEnabled = enabled;
    }
    }
}
