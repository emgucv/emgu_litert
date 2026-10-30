//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Finds the moments of a video the user attached ("when does the dog jump in?"), the way Google AI Edge Gallery's Video
/// Moment Finder does: the video (<see cref="AttachVideoAsync"/>) is copied into the app's folder and watched in the
/// background - 2-second windows of its frames, with their sound when the search model embeds audio (EmbeddingGemma 2
/// Multimodal), embedded with the shared search model (<see cref="VideoMomentIndex"/>) - with the progress in a chip
/// above the message box. The find_video_moment tool ranks the windows against what the user asked and shows the best
/// moments in a card; tapping one plays the video from there in the sheet. The index is saved next to the video's copy
/// (the same video isn't watched again), the chat remembers its video, and the cards come back with the chat. Videos
/// longer than <see cref="ChatVideos.MaxDurationMs"/> are refused. A private chat's video is kept in the cache and deleted
/// when it is removed.
/// </summary>
public sealed class VideoMomentsFeature : ChatFeature, IStorageParticipant, ISearchModelParticipant
{
    /// <summary>The saved files' kind: the moments a search showed.</summary>
    public const string MomentsKind = "video_moments";

    /// <summary>The videos' folder in a storage catalog (<c>new StorageFolder(VideoMomentsFeature.StorageKey, "Videos", VideoMomentsFeature.Folder, ...)</c>).</summary>
    public const string StorageKey = "video_moments";

    private sealed class VideoState
    {
        public string Name;
        public string File;
        public string Path;
        public bool IsPrivate;
        public long DurationMs;
        public VideoSource Source;
        public VideoMomentIndex Index;
        public Task<bool> Ready;
        public Task Released;
        public CancellationTokenSource Cancel = new CancellationTokenSource();
        public BackgroundWork Work;
        public bool ContextGiven;
        public Border Chip;
        public Label Detail;
        public ActivityIndicator Spinner;
    }

    private readonly SearchServices _search;
    private readonly HorizontalStackLayout _videoBar = new HorizontalStackLayout { Spacing = 8, IsVisible = false };
    private VideoState _video;

    /// <summary>Create the feature.</summary>
    /// <param name="search">The search model and when indexing may run</param>
    public VideoMomentsFeature(SearchServices search)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
    }

    /// <summary>Where the videos' copies and their indexes are kept.</summary>
    public static string Folder => System.IO.Path.Combine(FileSystem.AppDataDirectory, "video_moments");

    // A private chat's video: in the cache, deleted with it.
    private static string PrivateFolder => System.IO.Path.Combine(FileSystem.CacheDirectory, "video_moments_private");

    /// <inheritdoc/>
    public override string Name => ChatVideos.GroupName;

    /// <inheritdoc/>
    public override string Title => "Video moments";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override string ToolSummary => "moments of an attached video";

    /// <summary>Whether a video is attached.</summary>
    public bool HasVideo => _video != null;

    /// <summary>The attached video's indexing: true when it can be searched; null without a video.</summary>
    public Task<bool> VideoReady => _video?.Ready;

    private SearchEmbedder Embedder => _search.Embedder;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services)
    {
        services.Add(this);
        services.Add<IStorageParticipant>(this);
    }

    /// <inheritdoc/>
    public override View BuildComposerView() => _videoBar;

    // The tool and its instructions come with every message while a video is attached, and not at all otherwise (no
    // routing group: its stub would cost every chat's messages).
    /// <inheritdoc/>
    public override IEnumerable<AITool> Tools(ChatMessageContext context) =>
        _video == null ? Array.Empty<AITool>() : new AITool[] { AgentTools.FindVideoMoment(FindAsync) };

    /// <inheritdoc/>
    public override Task<string> InstructionsAsync(ChatMessageContext context) =>
        Task.FromResult(_video == null ? null : ChatVideos.Instructions);

    // ---------- The chat's video ----------

    /// <inheritdoc/>
    public override void OnNewConversation() => RemoveVideo();

    /// <inheritdoc/>
    public override void OnChatOpened(int chatId)
    {
        string saved = Host?.Settings.Get(ChatKey(chatId), string.Empty);
        int bar = saved?.IndexOf('|') ?? -1;
        if (bar <= 0)
            return;
        string file = saved.Substring(0, bar), name = saved.Substring(bar + 1);
        string path = System.IO.Path.Combine(Folder, file);
        if (File.Exists(path))
            ShowVideo(name, file, path, false);
    }

    /// <inheritdoc/>
    public override void OnChatSaved(int chatId)
    {
        VideoState state = _video;
        if (state != null && !state.IsPrivate && !Host.IsPrivate)
            Host.Settings.Set(ChatKey(chatId), state.File + "|" + state.Name);
    }

    /// <inheritdoc/>
    public override void OnDetached()
    {
        RemoveVideo();
        base.OnDetached();
    }

    private static string ChatKey(int chatId) => "video_chat_" + chatId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Attach a video to the chat: it is copied into the app's folder and watched (indexed) in the background, with the
    /// progress in the chip above the message box.
    /// </summary>
    /// <param name="path">The video file (e.g. the picker's copy; it can be deleted afterwards)</param>
    /// <param name="name">Its name, shown</param>
    /// <returns>A task that completes once the video is copied (its watching goes on)</returns>
    public async Task AttachVideoAsync(string path, string name)
    {
        if (Host == null)
            return;
        if (!VideoSource.IsSupported)
        {
            Host.SetStatus("Searching videos isn't supported on this device.");
            return;
        }
        if (_search.IsDownloadRequired)
        {
            Host.SetStatus("The search model was deleted. Download it again in the settings before attaching a video.");
            return;
        }
        if (!_search.SupportsImages)
        {
            Host.SetStatus("The search model can't see pictures: choose EmbeddingGemma 2 Text Vision or Multimodal in the settings to search videos.");
            return;
        }
        bool isPrivate = Host.IsPrivate;
        try
        {
            Host.SetStatus("Copying the video...");
            (string file, string copy) = await Task.Run(() => CopyVideo(path, name, isPrivate ? PrivateFolder : Folder));
            Host.SetStatus(string.Empty);
            ShowVideo(string.IsNullOrEmpty(name) ? System.IO.Path.GetFileName(path) : name, file, copy, isPrivate);
        }
        catch (Exception ex)
        {
            Host.SetStatus("The video couldn't be attached: " + ex.Message);
        }
    }

    // The video copied into the folder, named by its content (the same video is copied, and watched, once).
    private static (string File, string Path) CopyVideo(string path, string name, string folder)
    {
        string extension = System.IO.Path.GetExtension(string.IsNullOrEmpty(name) ? path : name).ToLowerInvariant();
        if (extension.Length < 2 || extension.Length > 5)
            extension = ".mp4";
        string hash;
        using (FileStream stream = File.OpenRead(path))
            hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(stream)).ToLowerInvariant();
        string file = hash + extension;
        string copy = System.IO.Path.Combine(folder, file);
        if (!File.Exists(copy))
        {
            Directory.CreateDirectory(folder);
            string part = copy + ".part";
            File.Copy(path, part, true);
            File.Move(part, copy, true);
        }
        return (file, copy);
    }

    private void ShowVideo(string name, string file, string path, bool isPrivate)
    {
        RemoveVideo();
        var state = new VideoState { Name = name, File = file, Path = path, IsPrivate = isPrivate };
        BuildVideoChip(state);
        _video = state;
        _videoBar.Children.Add(state.Chip);
        _videoBar.IsVisible = true;
        state.Ready = IndexVideoAsync(state);
    }

    /// <summary>Remove the attached video (its watching is stopped); earlier answers keep their moments.</summary>
    public void RemoveVideo()
    {
        VideoState state = _video;
        if (state == null)
            return;
        _video = null;
        state.Cancel.Cancel();
        _videoBar.Children.Clear();
        _videoBar.IsVisible = false;
        state.Released = ReleaseAsync(state);
    }

    /// <summary>Remove the attached video and wait for its watching to end (and a private chat's copy to be deleted).</summary>
    public async Task RemoveVideoAsync()
    {
        VideoState state = _video;
        RemoveVideo();
        if (state?.Released != null)
            await state.Released;
    }

    // Once its indexing has ended: the file let go of, and a private chat's copy deleted (unless it is attached again,
    // e.g. to be watched with another search model).
    private async Task ReleaseAsync(VideoState state)
    {
        try { await (state.Ready ?? Task.FromResult(false)); } catch (Exception) { }
        state.Source?.Dispose();
        state.Source = null;
        if (state.IsPrivate && _video?.Path != state.Path)
            DeleteQuietly(state.Path);
    }

    private static void DeleteQuietly(string path)
    {
        try { if (path != null && File.Exists(path)) File.Delete(path); } catch (Exception) { }
    }

    private void BuildVideoChip(VideoState state)
    {
        state.Detail = new Label { Text = "Opening the video...", LineBreakMode = LineBreakMode.TailTruncation };
        ChatResources.ApplyStyle(state.Detail, "ChatListDetailStyle");
        state.Spinner = new ActivityIndicator { IsRunning = true, Color = ChatResources.FindColor("ChatAccent"), WidthRequest = 16, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
        Label remove = ChatPage.Link("✕", () =>
        {
            if (!Host.IsBusy && _video == state)
            {
                RemoveVideo();
                Host.SetStatus("The video was removed. Earlier answers keep their moments.");
            }
        });
        remove.VerticalOptions = LayoutOptions.Center;
        SemanticProperties.SetDescription(remove, "Remove the video");
        var title = new Label { Text = state.Name, LineBreakMode = LineBreakMode.MiddleTruncation };
        ChatResources.ApplyStyle(title, "ChatListTitleStyle");
        var icon = new Label { Text = "\U0001F3AC", FontSize = 20, VerticalOptions = LayoutOptions.Center };
        state.Chip = new Border
        {
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(8, 6),
            Content = new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    icon,
                    new VerticalStackLayout
                    {
                        Spacing = 0,
                        VerticalOptions = LayoutOptions.Center,
                        MaximumWidthRequest = 240,
                        Children = { title, state.Detail }
                    },
                    state.Spinner,
                    remove
                }
            }
        };
        ChatResources.SetThemeColor(state.Chip, VisualElement.BackgroundColorProperty, "ChatRowBorder");
        SemanticProperties.SetDescription(state.Chip, "The attached video " + state.Name);
    }

    private void ShowProgress(VideoState state, string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_video == state)
                state.Detail.Text = text;
        });
    }

    // The index's file: next to the video, for the search model it was made with (another model's is made again).
    private string IndexPath(VideoState state) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(state.Path),
            System.IO.Path.GetFileNameWithoutExtension(state.File) + "." + (_search.IndexModel?.Invoke() ?? "model") + ".vmi");

    // Open the video, then load its saved index, or watch it: load the search model (downloading it the first time),
    // read the sound if the model hears, and embed the windows one at a time in the background work. Returns false if
    // it failed or was removed.
    private async Task<bool> IndexVideoAsync(VideoState state)
    {
        CancellationToken cancel = state.Cancel.Token;
        try
        {
            state.Source = await VideoSource.OpenAsync(state.Path);
            state.DurationMs = state.Source.DurationMs;
            if (state.DurationMs <= 0)
                throw new IOException("its length couldn't be read");
            if (state.DurationMs > ChatVideos.MaxDurationMs)
                throw new IOException(string.Format(CultureInfo.InvariantCulture, "it is {0} long; videos up to {1} can be searched",
                    ChatVideos.FormatTime(state.DurationMs), ChatVideos.FormatTime(ChatVideos.MaxDurationMs)));
            string indexPath = IndexPath(state);
            if (File.Exists(indexPath))
            {
                VideoMomentIndex saved = await Task.Run(() =>
                {
                    using FileStream stream = File.OpenRead(indexPath);
                    return VideoMomentIndex.Load(stream);
                }, cancel);
                if (saved.IsBuilt)
                {
                    state.Index = saved;
                    await Embedder.EnsureAsync(new Progress<string>(text => ShowProgress(state, text)), cancel);
                    Finished(state);
                    return true;
                }
            }

            await _search.Workload.WaitAsync(reason => ShowProgress(state, reason == null ? "Watching the video..." : "Paused: " + reason), cancel);
            EmbeddingModel model = await Embedder.EnsureAsync(new Progress<string>(text => ShowProgress(state, text)), cancel);
            short[] sound = null;
            if (model.SupportsAudio && state.Source.HasAudio)
            {
                ShowProgress(state, "Listening to the video...");
                sound = await state.Source.ReadAudioAsync(cancel);
            }
            state.Work = await MainThread.InvokeOnMainThreadAsync(() =>
                BackgroundWork.BeginAsync("Watching the video", state.Name, () => state.Cancel.Cancel()));
            var index = new VideoMomentIndex();
            state.Index = index;
            Action<string> paused = reason => ShowProgress(state, reason == null ? "Watching the video..." : "Paused: " + reason);
            var progress = new Progress<double>(share =>
            {
                string text = string.Format(CultureInfo.InvariantCulture, "Watching the video... {0}%", (int)(share * 100));
                if (_video == state)
                    state.Detail.Text = text;
                state.Work?.Update(text);
            });
            VideoSource source = state.Source;
            await index.BuildAsync(model, state.DurationMs,
                (ms, token) => source.FrameAtAsync(ms),
                sound != null && sound.Length > 0 ? (from, to, token) => Task.FromResult(Slice(sound, from, to)) : null,
                (embed, token) => _search.Workload.RunEmbeddingAsync(Embedder, embed, paused, token),
                progress, cancel);
            if (!state.IsPrivate)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        string part = indexPath + ".part";
                        using (FileStream stream = File.Create(part))
                            index.Save(stream);
                        File.Move(part, indexPath, true);
                    });
                }
                catch (Exception)
                {
                    // Still searchable, just watched again next time.
                }
            }
            state.Work?.Finish("The video is ready to search");
            state.Work = null;
            Finished(state);
            return true;
        }
        catch (OperationCanceledException)
        {
            state.Work?.Dispose();
            state.Work = null;
            return false;
        }
        catch (Exception ex)
        {
            state.Work?.Dispose();
            state.Work = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_video == state)
                {
                    RemoveVideo();
                    Host?.SetStatus(string.Format("{0} couldn't be searched: {1}", state.Name, ex.Message));
                }
            });
            return false;
        }
    }

    private void Finished(VideoState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_video != state)
                return;
            state.Spinner.IsRunning = state.Spinner.IsVisible = false;
            state.Detail.Text = ChatVideos.FormatTime(state.DurationMs) + (state.Index.WithAudio ? " · seen and heard" : " · seen");
            if (!Host.IsBusy)
                Host.SetStatus("Ask when something happens in the video.");
            // A chat already saved now uses this video.
            if (!state.IsPrivate && !Host.IsPrivate && Host.CurrentChatId != null)
                Host.Settings.Set(ChatKey(Host.CurrentChatId.Value), state.File + "|" + state.Name);
        });
    }

    // The sound from one time to another (16 kHz samples).
    private static short[] Slice(short[] sound, long fromMs, long toMs)
    {
        long from = Math.Min(sound.Length, fromMs * 16), to = Math.Min(sound.Length, toMs * 16);
        if (to <= from)
            return null;
        var slice = new short[to - from];
        Array.Copy(sound, from, slice, 0, slice.Length);
        return slice;
    }

    /// <inheritdoc/>
    public override Task<ChatMessageAddition> PrepareMessageAsync(ChatSendRequest request, CancellationToken cancellationToken)
    {
        // The model is told about the video once (with how far it's watched), with the first message after it is attached.
        VideoState state = _video;
        if (state == null || state.ContextGiven || state.DurationMs <= 0)
            return base.PrepareMessageAsync(request, cancellationToken);
        state.ContextGiven = true;
        double indexed = state.Index == null || state.Index.Windows.Count == 0 ? 0 : (double)state.Index.IndexedWindows / state.Index.Windows.Count;
        return Task.FromResult(new ChatMessageAddition { Context = ChatVideos.Context(state.Name, state.DurationMs, indexed) });
    }

    // ---------- The tool ----------

    /// <summary>The find_video_moment tool: search the attached video, show the best moments above the reply, and tell the model their times.</summary>
    /// <param name="request">What happens in the moment, in the user's words</param>
    /// <param name="cancel">Stops the search</param>
    public async Task<string> FindAsync(string request, CancellationToken cancel)
    {
        VideoState state = _video;
        if (state == null)
            return "No video is attached to this chat. Tell the user to attach one: the attach button, then Video.";
        VideoMomentIndex index = state.Index;
        int indexed = index?.IndexedWindows ?? 0;
        if (indexed == 0)
            return state.Ready != null && state.Ready.IsCompleted
                ? "The video couldn't be watched, so it can't be searched."
                : "The video is still being watched and none of it can be searched yet - try again in a minute.";
        double share = (double)indexed / Math.Max(1, index.Windows.Count);
        EmbeddingModel model = await Embedder.EnsureAsync(null, cancel);
        IReadOnlyList<VideoMoment> found;
        using (await Embedder.EnterSearchAsync(cancel))
            found = await Task.Run(() => index.Search(model, request ?? string.Empty, 5), cancel);
        var moments = new List<ChatVideoMoment>();
        foreach (VideoMoment moment in found.Take(ChatVideos.CountShown(found.Select(m => m.Score).ToList())))
        {
            byte[] thumbnail = null;
            try
            {
                if (state.Source != null)
                    thumbnail = await state.Source.FrameAtAsync(moment.StartMs + Math.Min(1000, (moment.EndMs - moment.StartMs) / 2), 320);
            }
            catch (Exception)
            {
            }
            moments.Add(new ChatVideoMoment { StartMs = moment.StartMs, EndMs = moment.EndMs, Score = moment.Score, Thumbnail = thumbnail });
        }
        if (moments.Count > 0)
        {
            var saved = new ChatVideoMoments { VideoName = state.Name, VideoFile = state.IsPrivate ? null : state.File, Request = request, Moments = moments };
            string path = state.Path;
            await MainThread.InvokeOnMainThreadAsync(() =>
                Host.AddCardToReply(CreateMomentsCard(saved, path), new ChatSavedFile(MomentsKind, "application/json", saved.ToJson())));
        }
        return ChatVideos.Reply(request, moments, share);
    }

    // A strip of the moments - a frame, the times - above the reply; tapping one plays the video from there.
    private View CreateMomentsCard(ChatVideoMoments saved, string videoPath)
    {
        var row = new HorizontalStackLayout { Spacing = 8 };
        foreach (ChatVideoMoment moment in saved.Moments)
        {
            var image = new Image { HeightRequest = 72, WidthRequest = 128, Aspect = Aspect.AspectFill };
            if (moment.Thumbnail != null)
            {
                byte[] jpeg = moment.Thumbnail;
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
            var caption = new Label { Text = moment.ToString().Replace("-", "–"), WidthRequest = 128 };
            ChatResources.ApplyStyle(caption, "ChatListDetailStyle");
            var tile = new VerticalStackLayout { Spacing = 2, Children = { frame, caption } };
            long start = moment.StartMs;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => PlayMoment(videoPath, saved.VideoName, start);
            tile.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(tile, "Play the video from " + ChatVideos.FormatTime(start));
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

    // The video in the sheet, playing from the moment.
    private async void PlayMoment(string path, string name, long startMs)
    {
        if (path == null || !File.Exists(path))
        {
            await Host.ShowSheetAsync(name ?? "Video", ChatPage.Note("The video is no longer on this device."), "Close");
            return;
        }
        var video = new InlineVideoView { Source = path, HeightRequest = Math.Max(200, Host.PageSize.Height * 0.5) };
        var status = new Label { Text = "From " + ChatVideos.FormatTime(startMs) };
        ChatResources.ApplyStyle(status, "ChatListDetailStyle");
        Label toggle = null;
        toggle = ChatPage.Link("Pause", () =>
        {
            if (video.IsPlaying) { video.Pause(); toggle.Text = "Play"; }
            else { video.Play(); toggle.Text = "Pause"; }
        });
        Label again = ChatPage.Link("From " + ChatVideos.FormatTime(startMs) + " again", () =>
        {
            video.SeekTo((int)startMs);
            video.Play();
            toggle.Text = "Pause";
        });
        video.Prepared += (duration, width, height) =>
        {
            if (width > 0 && height > 0)
                video.HeightRequest = Math.Min(Host.PageSize.Height * 0.6, Math.Max(160, Math.Min(Host.PageSize.Width - 32, 720) * height / width));
            video.SeekTo((int)startMs);
            video.Play();
        };
        video.Completed += () => toggle.Text = "Play";
        video.Failed += message => status.Text = message;
        var content = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { video, new HorizontalStackLayout { Spacing = 16, Children = { toggle, again, status } } }
        };
        await Host.ShowSheetAsync(name ?? "Video", content, "Close");
        video.Stop();
        video.Source = null;
    }

    /// <inheritdoc/>
    public override View RestoreSavedFile(ChatSavedFile file)
    {
        if (file.Kind != MomentsKind)
            return null;
        ChatVideoMoments saved = ChatVideoMoments.FromJson(file.Data);
        if (saved == null || saved.Moments.Count == 0)
            return null;
        string path = string.IsNullOrEmpty(saved.VideoFile) ? null : System.IO.Path.Combine(Folder, saved.VideoFile);
        return CreateMomentsCard(saved, path);
    }

    // ---------- ISearchModelParticipant ----------

    /// <summary>Before the search model is switched: the watching stops.</summary>
    public async Task PrepareSearchModelChangeAsync()
    {
        VideoState state = _video;
        if (state == null || state.Ready == null || state.Ready.IsCompleted)
            return;
        state.Cancel.Cancel();
        try { await state.Ready; } catch (Exception) { }
    }

    /// <summary>The search model was switched: the video is watched again with it (or its index for it loaded).</summary>
    /// <param name="otherModel">True for another model</param>
    public Task SearchModelChangedAsync(bool otherModel)
    {
        VideoState state = _video;
        if (state != null)
        {
            if (!_search.SupportsImages)
            {
                RemoveVideo();
                Host?.SetStatus("The video was removed: the new search model can't see pictures.");
            }
            else
                ShowVideo(state.Name, state.File, state.Path, state.IsPrivate);
        }
        return Task.CompletedTask;
    }

    // ---------- IStorageParticipant ----------

    /// <summary>Before storage deletes the videos' folder or the search model: the video is removed (its watching stopped and waited for).</summary>
    public async Task PrepareStorageDeleteAsync(StorageEntry entry)
    {
        VideoState state = _video;
        if (state == null)
            return;
        bool gone = (entry.Kind == StorageKind.Folder && entry.Key == StorageKey)
            || (entry.Kind == StorageKind.SearchModel && entry.Key == _search.IndexModel?.Invoke());
        bool pending = state.Ready != null && !state.Ready.IsCompleted;
        if (gone || pending)
            RemoveVideo();
        if (pending)
        {
            try { await state.Ready; } catch (Exception) { }
        }
    }

    /// <inheritdoc/>
    public void StorageChanged(StorageEntry entry)
    {
    }
}
