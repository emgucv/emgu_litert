//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// "Search my videos": with the setting on (off by default), the newest videos on the device are indexed in the
    /// background with the shared search model - sparsely, one frame and its 2 s of sound every 10 s
    /// (<see cref="VideoLibraryIndex"/>; the sound only with a model that hears, EmbeddingGemma 2 Multimodal) - and the
    /// find_videos tool (<see cref="AgentTools.FindVideos"/>) shows the matches as a card above the reply; tapping one
    /// plays it from its best moment, and "Find the exact moment" attaches it to the chat for the attached-video search
    /// (<see cref="VideoMomentsFeature"/>), which looks at every 2 s. The tool joins the photo search's tool group when
    /// the page has <see cref="PhotoSearchFeature"/> (a group of its own would make every message longer), else it has
    /// its own. The index is kept in the chats' store (<see cref="IVideoIndexStore"/>); turning the search off deletes
    /// it. The app asks for video access through the platform (Android: READ_MEDIA_VIDEO, declared by this assembly;
    /// iOS and Mac Catalyst: NSPhotoLibraryUsageDescription).
    /// </summary>
    public sealed class VideoSearchFeature : ChatFeature, IStorageParticipant, ISearchModelParticipant
    {
        /// <summary>The switch's settings key.</summary>
        public const string SettingKey = "video_search";

        /// <summary>The "Index videos only while charging" settings key.</summary>
        public const string ChargingSettingKey = "videos_only_charging";

        /// <summary>The "Videos to index" settings key: how many of the newest videos, 0 for all.</summary>
        public const string VideoLimitKey = "videos_to_index";

        /// <summary>The choices of "Videos to index" (0: all).</summary>
        public static readonly IReadOnlyList<int> VideoLimitChoices = new[] { 20, 100, 500, 0 };

        /// <summary>The kind of the file saved with a reply that found videos.</summary>
        public const string FoundKind = "video_search";

        private readonly IVideoIndexStore _store;
        private readonly SearchServices _search;
        private readonly List<ToggleChip> _limitChips = new List<ToggleChip>();
        private VideoLibraryIndex _index;
        private Switch _switch;
        private Label _status;
        private CancellationTokenSource _indexing;
        private Task _indexTask;
        private Task<BackgroundWork> _work;
        private bool _visible;
        private bool _started;
        private bool _onlyWhileCharging;
        private bool _updatingLimitChips;
        private string _pauseReason;
        private string _progress;
        private string _error;
        private DateTime _indexStarted = DateTime.MinValue;
        private static readonly TimeSpan ReindexInterval = TimeSpan.FromMinutes(1);

        /// <summary>Create the feature.</summary>
        /// <param name="store">The chats' store (e.g. Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore)</param>
        /// <param name="search">The search model and when indexing may run</param>
        public VideoSearchFeature(IVideoIndexStore store, SearchServices search)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _search = search ?? throw new ArgumentNullException(nameof(search));
        }

        /// <summary>
        /// How many of the newest videos are indexed until the user chooses in "Videos to index": 100 on a desktop, 20
        /// elsewhere (on a Galaxy S10 a minute of video takes about a minute); 0 for all.
        /// </summary>
        public int MaxIndexedVideos { get; set; } = DeviceInfo.Idiom == DeviceIdiom.Desktop ? 100 : 20;

        private int VideoLimit => Host?.Settings.Get(VideoLimitKey, MaxIndexedVideos) ?? MaxIndexedVideos;

        /// <inheritdoc/>
        public override string Name => ChatVideoLibrary.GroupName;

        /// <inheritdoc/>
        public override string Title => "Search my videos";

        /// <summary>The switch is the feature's own setting, not the page's feature switch.</summary>
        public override bool PersistEnabled => false;

        private SearchEmbedder Embedder => _search.Embedder;

        /// <summary>Whether the search is on (the switch).</summary>
        public bool On => _switch != null ? _switch.IsToggled : Host?.Settings.Get(SettingKey, false) == true;

        private bool SwitchAllowed => VideoLibrary.IsSupported && _search.SupportsImages && !_search.IsDownloadRequired;

        private string ModelName => _search.IndexModel?.Invoke() ?? "model";

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services)
        {
            services.Add(this);
            services.Add<IStorageParticipant>(this);
        }

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            _onlyWhileCharging = host.Settings.Get(ChargingSettingKey, DeviceInfo.Idiom != DeviceIdiom.Desktop);
            _index = new VideoLibraryIndex(_store, new VideoLibrary());
            _index.Changed += () => MainThread.BeginInvokeOnMainThread(UpdateStatus);
        }

        /// <inheritdoc/>
        public override void OnDetached()
        {
            CancelIndexing();
            base.OnDetached();
        }

        /// <summary>The find_videos tool, for the photo search's group (<see cref="PhotoSearchFeature"/> adds it).</summary>
        public IEnumerable<AITool> Tools() => new AITool[] { AgentTools.FindVideos(FindAsync) };

        /// <inheritdoc/>
        public override IEnumerable<ToolGroup> ToolGroups()
        {
            // With photo search, find_videos is in its group; else a group of its own.
            if (Host?.Services.Get<PhotoSearchFeature>() != null)
                yield break;
            yield return new ToolGroup(ChatVideoLibrary.GroupName, ChatVideoLibrary.GroupSummary, ChatVideoLibrary.GroupKeywords,
                Tools, () => ChatVideoLibrary.Instructions);
        }

        /// <inheritdoc/>
        public override void OnAppearing()
        {
            _visible = true;
            if (_started)
                RefreshIndex();
            else
            {
                _started = true;
                _ = LoadIndexAsync();
                StartIndexing();
            }
        }

        private async Task LoadIndexAsync()
        {
            try
            {
                await _index.LoadAsync();
            }
            catch (Exception ex)
            {
                _error = "the saved index couldn't be read: " + ex.Message;
            }
            MainThread.BeginInvokeOnMainThread(UpdateStatus);
        }

        /// <inheritdoc/>
        public override void OnDisappearing() => _visible = false;

        /// <inheritdoc/>
        public override void OnAppResumed()
        {
            if (_visible)
                RefreshIndex();
        }

        /// <inheritdoc/>
        public override void OnPrivateModeChanged(bool isPrivate)
        {
            SetSwitchEnabled(!isPrivate && SwitchAllowed);
            if (!isPrivate && _visible)
                StartIndexing();
        }

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            bool on = Host.Settings.Get(SettingKey, false);
            _switch = new Switch { IsToggled = on && SwitchAllowed, IsEnabled = SwitchAllowed && !Host.IsPrivate, OnColor = ChatResources.FindColor("ChatAccent"), VerticalOptions = LayoutOptions.Center };
            _switch.Toggled += OnToggled;
            SemanticProperties.SetDescription(_switch, Title);
            var label = new Label { Text = Title };
            ChatResources.ApplyStyle(label, "ChatSectionTextStyle");
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(label, 0, 0);
            row.Add(_switch, 1, 0);
            _status = new Label { IsVisible = false };
            ChatResources.ApplyStyle(_status, "ChatSectionNoteStyle");
            var section = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    row,
                    ChatPage.Note("Lets the assistant find your videos by what is seen in them - and, with EmbeddingGemma 2 Multimodal, what is heard (\"the video where the kids sing happy birthday\"). Your newest videos (as many as chosen below) are indexed on this device in the background, a frame and a moment of sound every 10 seconds of their first 10 minutes; nothing leaves the device. Found videos play from their best moment, and \"Find the exact moment\" looks at the whole video. Turning this off deletes the index."),
                    BuildLimitChoice()
                }
            };
            View notice = _search.CreateDownloadNotice?.Invoke();
            if (notice != null)
                section.Children.Add(notice);
            View charging = ChatPage.SwitchRow("Index videos only while charging", null, _onlyWhileCharging, value =>
            {
                _onlyWhileCharging = value;
                Host.Settings.Set(ChargingSettingKey, value);
            });
            charging.IsVisible = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
            section.Children.Add(charging);
            section.Children.Add(_status);
            UpdateStatus();
            return section;
        }

        private View BuildLimitChoice()
        {
            var title = new Label { Text = "Videos to index" };
            ChatResources.ApplyStyle(title, "ChatSectionTextStyle");
            var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            _limitChips.Clear();
            foreach (int choice in VideoLimitChoices)
            {
                int value = choice;
                string name = value <= 0 ? "All" : value.ToString("N0", CultureInfo.InvariantCulture);
                var chip = new ToggleChip(name) { Margin = new Thickness(0, 0, 6, 6) };
                SemanticProperties.SetDescription(chip, value <= 0 ? "Index all your videos" : "Index your " + name + " newest videos");
                chip.Toggled += (_, e) => OnLimitToggled(value, e.Value);
                _limitChips.Add(chip);
                chips.Children.Add(chip);
            }
            SetLimitChips();
            return new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    title,
                    chips,
                    ChatPage.Note("A minute of video takes a few seconds on a computer and about a minute on a phone, so many videos can take hours the first time; after that only new videos are indexed. Fewer removes the older videos from the index.")
                }
            };
        }

        private void SetLimitChips()
        {
            _updatingLimitChips = true;
            int limit = VideoLimit;
            for (int i = 0; i < _limitChips.Count; i++)
                _limitChips[i].IsToggled = VideoLimitChoices[i] == limit || (VideoLimitChoices[i] == 0 && limit < 0);
            _updatingLimitChips = false;
        }

        private void OnLimitToggled(int value, bool on)
        {
            if (_updatingLimitChips)
                return;
            if (!on || value == VideoLimit)
            {
                SetLimitChips();
                return;
            }
            Host.Settings.Set(VideoLimitKey, value);
            SetLimitChips();
            if (On && _visible)
                RestartIndexing();
        }

        private async void OnToggled(object sender, ToggledEventArgs e)
        {
            Host.Settings.Set(SettingKey, e.Value);
            if (e.Value)
            {
                bool allowed = false;
                try { allowed = await VideoLibrary.RequestAccessAsync(); }
                catch (Exception ex)
                {
                    TurnOff();
                    ShowError("Could not request video access: " + ex.Message);
                    return;
                }
                if (!allowed)
                {
                    TurnOff();
#if MACCATALYST
                    ShowError("Allow this app in System Settings > Privacy & Security > Photos, then turn on Search my videos again.");
#else
                    ShowError("Video search needs access to your videos. Allow it in your device's settings, then try again.");
#endif
                    return;
                }
                StartIndexing();
            }
            else
            {
                CancelIndexing();
                await WaitForIndexingAsync();
                await _index.ClearAsync();
                UpdateStatus();
            }
        }

        private void ShowError(string message)
        {
            if (_status != null)
            {
                _status.Text = message;
                _status.IsVisible = true;
            }
            Host.SetStatus(message);
        }

        private async void RestartIndexing()
        {
            CancelIndexing();
            await WaitForIndexingAsync();
            StartIndexing();
        }

        /// <summary>The page is shown again or the app came back: new, edited or deleted videos are indexed or forgotten (at most once a minute).</summary>
        public void RefreshIndex()
        {
            if (DateTime.UtcNow - _indexStarted >= ReindexInterval)
                StartIndexing();
        }

        /// <summary>Index the new videos in the background (only those not indexed yet, or edited since).</summary>
        public void StartIndexing()
        {
            if (Host == null || Host.IsPrivate || Host.IsReloadingModel || !SwitchAllowed || !On || Embedder == null || (_indexTask != null && !_indexTask.IsCompleted))
                return;
            _indexStarted = DateTime.UtcNow;
            _error = null;
            _indexing = new CancellationTokenSource();
            _indexTask = IndexAsync(_indexing.Token);
        }

        private async Task IndexAsync(CancellationToken cancel)
        {
            try
            {
                var stage = new Progress<string>(text => { _progress = text; UpdateStatus(); });
                await WaitForWorkAsync(cancel);
                EmbeddingModel model = await Embedder.EnsureAsync(stage, cancel);
                await _index.IndexAsync(model, ModelName,
                    (embed, token) => _search.Workload.RunEmbeddingAsync(Embedder, embed,
                        reason => MainThread.BeginInvokeOnMainThread(() => { _pauseReason = reason; UpdateStatus(); }), token, _onlyWhileCharging),
                    VideoLimit <= 0 ? 0 : VideoLimit,
                    text =>
                    {
                        _progress = text;
                        MainThread.BeginInvokeOnMainThread(UpdateStatus);
                        _ = UpdateWorkAsync(text);
                    },
                    cancel, WaitForWorkAsync);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
            finally
            {
                _progress = null;
                Task<BackgroundWork> work = _work;
                _work = null;
                if (work != null)
                    try { (await work).Dispose(); } catch (Exception) { }
            }
            MainThread.BeginInvokeOnMainThread(UpdateStatus);
        }

        // The background work (a notification with Stop on Android) while a video is being indexed.
        private async Task UpdateWorkAsync(string text)
        {
            try
            {
                if (_work == null)
                    _work = BackgroundWork.BeginAsync("Indexing your videos", text, CancelIndexing);
                else
                    (await _work).Update(text);
            }
            catch (Exception)
            {
            }
        }

        private Task WaitForWorkAsync(CancellationToken cancel) => _search.Workload.WaitAsync(reason =>
            MainThread.BeginInvokeOnMainThread(() => { _pauseReason = reason; UpdateStatus(); }), cancel, _onlyWhileCharging);

        /// <summary>Show the index's state in the settings.</summary>
        public void UpdateStatus()
        {
            if (_status == null)
                return;
            if (!On)
            {
                _status.IsVisible = false;
                return;
            }
            _status.IsVisible = true;
            string unreadable = _index.UnreadableCount > 0
                ? string.Format(CultureInfo.InvariantCulture, "; {0} couldn't be read (e.g. only in the cloud)", _index.UnreadableCount)
                : string.Empty;
            if (_error != null && !_index.IsIndexing)
                _status.Text = string.Format(CultureInfo.InvariantCulture, "Indexing stopped: {0}. {1} videos indexed.", _error.TrimEnd('.'), _index.Count);
            else if (_pauseReason != null && _index.IsIndexing)
                _status.Text = string.Format(CultureInfo.InvariantCulture, "Video indexing paused: {0}. {1} videos indexed.", _pauseReason, _index.Count);
            else if (_index.IsIndexing && _progress != null)
                _status.Text = _progress + " · " + _index.Count + " indexed" + unreadable;
            else
                _status.Text = string.Format(CultureInfo.InvariantCulture, "{0} videos indexed", _index.Count) + unreadable;
        }

        /// <summary>The find_videos tool: search the index, show the matches above the reply, and tell the model what was found.</summary>
        /// <param name="request">What to find, in the user's words</param>
        /// <param name="cancel">Stops the search</param>
        public async Task<string> FindAsync(string request, CancellationToken cancel)
        {
            if (!On)
                return string.Format(CultureInfo.InvariantCulture,
                    "Video search is turned off, so the user's videos can't be searched. Tell the user they can turn on \"Search my videos\" in {0}: their newest videos are then indexed on this device (the first time takes a while), and nothing leaves it.",
                    Host.SettingsLocation);
            await _index.LoadAsync();
            if (_index.Count == 0)
                return _index.IsIndexing ? "The videos are still being indexed - try again in a few minutes." : "No videos are indexed yet.";
            // The dates, as for photos; the rest is what to find. Videos have no places or kinds, so what the photo
            // parser took for one goes back into the description ("a woman in a military uniform" isn't a place).
            PhotoQuery query = PhotoRequest.Parse(request, DateTime.Now, Array.Empty<string>(), Array.Empty<string>());
            string description = string.Join(" ", new[] { query.Description, query.Place == null ? null : "in " + query.Place, query.Kind, query.Camera }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            EmbeddingModel model = await Embedder.EnsureAsync(null, cancel);
            // The shortened description and the request as said (when it has no dates, which only the first leaves
            // out): each video is judged by the one it matches best.
            var wordings = new List<string> { description };
            if (query.From == null && query.To == null)
                wordings.Add(request);
            List<VideoLibraryMatch> matches = await _index.SearchAsync(model, ModelName, Embedder.EnterSearchAsync,
                wordings, query.From, query.To, cancel);
            var found = new ChatFoundVideos { Request = request };
            foreach (VideoLibraryMatch match in matches)
            {
                VideoMoment best = match.Moments.FirstOrDefault();
                // The card shows the frame of the moment found (the saved thumbnail is the video's first indexed frame).
                byte[] thumbnail = match.Video.Thumbnail;
                if (best != null)
                {
                    try
                    {
                        using VideoSource source = await VideoLibrary.OpenSourceAsync(match.Video.VideoId, cancel);
                        if (source != null)
                            thumbnail = await source.FrameAtAsync((best.StartMs + Math.Min(best.EndMs, best.StartMs + VideoLibraryIndex.WindowMs)) / 2,
                                VideoLibraryIndex.ThumbnailSize) ?? thumbnail;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                    }
                }
                found.Videos.Add(new ChatFoundVideo
                {
                    VideoId = match.Video.VideoId,
                    Name = match.Video.Name,
                    Taken = match.Video.Taken,
                    DurationMs = match.Video.DurationMs,
                    StartMs = best?.StartMs ?? 0,
                    EndMs = best?.EndMs ?? 0,
                    Score = match.Score,
                    Thumbnail = thumbnail
                });
            }
            if (found.Videos.Count > 0)
                await MainThread.InvokeOnMainThreadAsync(() => Host.AddCardToReply(CreateCard(found),
                    new ChatSavedFile(FoundKind, "application/json", found.ToJson())));
            return ChatVideoLibrary.Reply(request, found.Videos, _index.Count, Math.Max(_index.Count, _index.Total), DateTime.Now);
        }

        // A strip of the found videos above the reply; tapping one plays it from its best moment.
        private View CreateCard(ChatFoundVideos found)
        {
            var row = new HorizontalStackLayout { Spacing = 8 };
            foreach (ChatFoundVideo video in found.Videos)
            {
                var image = new Image { HeightRequest = 90, WidthRequest = 160, Aspect = Aspect.AspectFill };
                if (video.Thumbnail != null)
                {
                    byte[] jpeg = video.Thumbnail;
                    image.Source = ImageSource.FromStream(() => new MemoryStream(jpeg));
                }
                var frame = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Stroke = Colors.Transparent,
                    Content = new Grid
                    {
                        Children =
                        {
                            image,
                            new Label
                            {
                                Text = "▶", TextColor = Colors.White, FontSize = 22, Shadow = new Shadow { Opacity = 0.8f, Radius = 4 },
                                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
                            }
                        }
                    }
                };
                ChatResources.SetThemeColor(frame, VisualElement.BackgroundColorProperty, "ChatRowBorder");
                var caption = new Label
                {
                    Text = VideoMomentIndex.FormatTime(video.StartMs) + " · " + ChatVideoLibrary.Ago(video.Taken, DateTime.Now),
                    WidthRequest = 160, LineBreakMode = LineBreakMode.TailTruncation
                };
                ChatResources.ApplyStyle(caption, "ChatListDetailStyle");
                var tile = new VerticalStackLayout { Spacing = 2, Children = { frame, caption } };
                ChatFoundVideo shown = video;
                var tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) => Play(shown);
                tile.GestureRecognizers.Add(tap);
                SemanticProperties.SetDescription(tile, "Play the video taken " + ChatVideoLibrary.Ago(video.Taken, DateTime.Now) + " from " + VideoMomentIndex.FormatTime(video.StartMs));
                row.Children.Add(tile);
            }
            return new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                Margin = new Thickness(6, 0),
                Content = row
            };
        }

        // The video in the sheet, from its best moment; "Find the exact moment" attaches it to the chat.
        private async void Play(ChatFoundVideo found)
        {
            var video = new InlineVideoView { Source = found.VideoId, HeightRequest = Math.Max(200, Host.PageSize.Height * 0.5) };
            var status = new Label { Text = "From " + VideoMomentIndex.FormatTime(found.StartMs) };
            ChatResources.ApplyStyle(status, "ChatListDetailStyle");
            Label toggle = null;
            toggle = ChatPage.Link("Pause", () =>
            {
                if (video.IsPlaying) { video.Pause(); toggle.Text = "Play"; }
                else { video.Play(); toggle.Text = "Pause"; }
            });
            Label again = ChatPage.Link("From " + VideoMomentIndex.FormatTime(found.StartMs) + " again", () =>
            {
                video.SeekTo((int)found.StartMs);
                video.Play();
                toggle.Text = "Pause";
            });
            VideoMomentsFeature moments = Host.Services.Get<VideoMomentsFeature>();
            Label exact = null;
            exact = ChatPage.Link("Find the exact moment", async () =>
            {
                exact.IsEnabled = false;
                status.Text = "Attaching the video to the chat...";
                status.Text = await AttachAsync(found, moments);
            });
            exact.IsVisible = moments != null && found.DurationMs <= ChatVideos.MaxDurationMs;
            video.Prepared += (duration, width, height) =>
            {
                if (width > 0 && height > 0)
                    video.HeightRequest = Math.Min(Host.PageSize.Height * 0.6, Math.Max(160, Math.Min(Host.PageSize.Width - 32, 720) * height / width));
                video.SeekTo((int)found.StartMs);
                video.Play();
            };
            video.Completed += () => toggle.Text = "Play";
            video.Failed += message => status.Text = message + " It may have been deleted, or be only in the cloud.";
            var content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    video,
                    new HorizontalStackLayout { Spacing = 16, Children = { toggle, again } },
                    exact,
                    status
                }
            };
            await Host.ShowSheetAsync(found.Name ?? "Video", content, "Close");
            video.Stop();
            video.Source = null;
        }

        // A copy of the library video attached to the chat, so find_video_moment searches all of it.
        private async Task<string> AttachAsync(ChatFoundVideo found, VideoMomentsFeature moments)
        {
            string copy = null;
            try
            {
                copy = await VideoLibrary.ExportAsync(found.VideoId, System.IO.Path.Combine(FileSystem.CacheDirectory, "library_videos"));
                if (copy == null)
                    return "The video couldn't be read: it may be only in the cloud.";
                await moments.AttachVideoAsync(copy, found.Name ?? "Video");
                return "Attached to the chat: close this and ask when something happens in it.";
            }
            catch (Exception ex)
            {
                return "The video couldn't be attached: " + ex.Message;
            }
            finally
            {
                // The moment search keeps its own copy; this export is only the way there (a Windows path is the video itself).
                if (copy != null && copy != found.VideoId)
                    try { File.Delete(copy); } catch (Exception) { }
            }
        }

        /// <inheritdoc/>
        public override View RestoreSavedFile(ChatSavedFile file)
        {
            if (file.Kind != FoundKind)
                return null;
            ChatFoundVideos found = ChatFoundVideos.FromJson(file.Data);
            return found == null || found.Videos.Count == 0 ? null : CreateCard(found);
        }

        /// <summary>Stop indexing without waiting.</summary>
        public void CancelIndexing() => _indexing?.Cancel();

        /// <summary>Wait for the indexing to end.</summary>
        public async Task WaitForIndexingAsync()
        {
            try { await (_indexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
        }

        /// <summary>Forget every indexed video.</summary>
        public Task ClearIndexAsync() => _index.ClearAsync();

        /// <summary>Turn the search off without deleting the index again.</summary>
        public void TurnOff()
        {
            Host?.Settings.Set(SettingKey, false);
            if (_switch == null)
                return;
            _switch.Toggled -= OnToggled;
            _switch.IsToggled = false;
            _switch.Toggled += OnToggled;
        }

        /// <summary>Turn the search off as the user would, deleting the index.</summary>
        public void SwitchOff()
        {
            if (_switch != null) _switch.IsToggled = false;
        }

        /// <summary>Enable or disable the switch.</summary>
        public void SetSwitchEnabled(bool enabled)
        {
            if (_switch != null) _switch.IsEnabled = enabled;
        }

        // ---------- ISearchModelParticipant ----------

        /// <summary>Before the search model is switched: the indexing stops.</summary>
        public async Task PrepareSearchModelChangeAsync()
        {
            CancelIndexing();
            await WaitForIndexingAsync();
        }

        /// <summary>The search model was switched: the videos are indexed again with it (the index keys on the model).</summary>
        /// <param name="otherModel">True for another model</param>
        public async Task SearchModelChangedAsync(bool otherModel)
        {
            if (otherModel)
                await ClearIndexAsync();
            if (!_search.SupportsImages && On)
                SwitchOff();
            else if (_visible)
                StartIndexing();
        }

        // ---------- IStorageParticipant ----------

        /// <inheritdoc/>
        public async Task PrepareStorageDeleteAsync(StorageEntry entry)
        {
            CancelIndexing();
            if (entry.Kind == StorageKind.SearchModel && entry.Key == _search.IndexModel?.Invoke())
                TurnOff();
            if (entry.Kind == StorageKind.VideoIndex)
            {
                TurnOff();
                await ClearIndexAsync();
            }
            await WaitForIndexingAsync();
        }

        /// <inheritdoc/>
        public void StorageChanged(StorageEntry entry)
        {
            if (Host == null)
                return;
            if (_search.IsDownloadRequired && On)
            {
                TurnOff();
                CancelIndexing();
            }
            SetSwitchEnabled(!Host.IsPrivate && SwitchAllowed);
            UpdateStatus();
            if (_visible)
                StartIndexing();
        }
    }
}
