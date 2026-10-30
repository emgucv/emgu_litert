//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>A video in the device's library: its id, when it was taken, a version that changes when it is edited, its length and name.</summary>
    public sealed class LibraryVideo
    {
        /// <summary>The platform's id (a MediaStore content URI on Android, a PhotoKit local identifier on iOS, a path on Windows)</summary>
        public string Id { get; set; }

        /// <summary>When it was taken (or added)</summary>
        public DateTimeOffset Taken { get; set; }

        /// <summary>Changes when the video changes</summary>
        public long Version { get; set; }

        /// <summary>Its length, in milliseconds (0 if unknown until opened)</summary>
        public long DurationMs { get; set; }

        /// <summary>Its file name, or a description</summary>
        public string Name { get; set; }
    }

    /// <summary>A library video opened for indexing: its frames and sound.</summary>
    public interface ILibraryVideoReader : IDisposable
    {
        /// <summary>Its length, in milliseconds</summary>
        long DurationMs { get; }

        /// <summary>Whether it has a sound track</summary>
        bool HasAudio { get; }

        /// <summary>The frame at a time as a JPEG at most maxSize pixels on its longer side, or null</summary>
        Task<byte[]> FrameAtAsync(long ms, int maxSize, CancellationToken cancel);

        /// <summary>The sound between two times, 16 kHz mono, or null</summary>
        Task<short[]> ReadAudioAsync(long fromMs, long toMs, CancellationToken cancel);
    }

    /// <summary>The device's video library, for <see cref="VideoLibraryIndex"/> (Emgu.LiteRT.LM.Maui's VideoLibrary on a device).</summary>
    public interface IVideoLibrarySource
    {
        /// <summary>The newest videos, newest first, at most max (0 for all); null without access</summary>
        Task<IReadOnlyList<LibraryVideo>> GetRecentVideosAsync(int max);

        /// <summary>Open a video to read its frames and sound; null if it can't be read (e.g. only in the cloud)</summary>
        Task<ILibraryVideoReader> OpenAsync(string id, CancellationToken cancel);
    }

    /// <summary>A library video's index entry, as saved: what was indexed, with which model, and its thumbnail.</summary>
    public sealed class ChatVideoEntry
    {
        /// <summary>The library's id</summary>
        public string VideoId { get; set; }

        /// <summary>The search model's file name</summary>
        public string EmbeddingModel { get; set; }

        /// <summary>The library's version of the video when it was indexed</summary>
        public long Version { get; set; }

        /// <summary>The index's settings (<see cref="VideoLibraryIndex.SettingsVersion"/>) when it was indexed</summary>
        public int SettingsVersion { get; set; }

        /// <summary>When it was taken, UTC</summary>
        public DateTime Taken { get; set; }

        /// <summary>Its length, in milliseconds</summary>
        public long DurationMs { get; set; }

        /// <summary>How much of it was indexed, from the start, in milliseconds</summary>
        public long IndexedMs { get; set; }

        /// <summary>Its name</summary>
        public string Name { get; set; }

        /// <summary>Whether its sound was embedded with the frames</summary>
        public bool WithAudio { get; set; }

        /// <summary>A small JPEG of its first indexed frame</summary>
        public byte[] Thumbnail { get; set; }
    }

    /// <summary>One indexed window of a library video: its times and embedding (float32 bytes).</summary>
    public sealed class ChatVideoWindow
    {
        /// <summary>The video's id</summary>
        public string VideoId { get; set; }

        /// <summary>Where the window starts, in milliseconds</summary>
        public long StartMs { get; set; }

        /// <summary>Where it ends</summary>
        public long EndMs { get; set; }

        /// <summary>Its embedding, as float32 bytes</summary>
        public byte[] Embedding { get; set; }
    }

    /// <summary>Where the video library index is kept (Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore).</summary>
    public interface IVideoIndexStore
    {
        /// <summary>Every indexed video</summary>
        Task<IReadOnlyList<ChatVideoEntry>> GetVideoEntriesAsync();

        /// <summary>Every indexed window</summary>
        Task<IReadOnlyList<ChatVideoWindow>> GetVideoWindowsAsync();

        /// <summary>Save a video's entry and windows, replacing what it had</summary>
        Task SaveVideoAsync(ChatVideoEntry entry, IReadOnlyList<ChatVideoWindow> windows);

        /// <summary>Remove videos and their windows</summary>
        Task DeleteVideosAsync(IEnumerable<string> videoIds);

        /// <summary>Remove everything</summary>
        Task ClearVideoIndexAsync();
    }

    /// <summary>A video a search found: its entry, how well it matched, and its best moments.</summary>
    public sealed class VideoLibraryMatch
    {
        /// <summary>The video</summary>
        public ChatVideoEntry Video { get; set; }

        /// <summary>Its best window's similarity</summary>
        public float Score { get; set; }

        /// <summary>Its best moments, best first (sparse windows close together merged)</summary>
        public IReadOnlyList<VideoMoment> Moments { get; set; }
    }

    /// <summary>
    /// The index of the device's video library ("Search my videos"): a sparse window of each video - one frame and its
    /// 2 s of sound every 10 s, the first 10 minutes - embedded by the search model, so a whole library can be indexed
    /// on a phone (about a minute per minute of video on a Galaxy S10, where an attached video's dense windows took
    /// 7.5). The newest videos first, up to a limit; videos gone or past the limit are dropped.
    /// </summary>
    public sealed class VideoLibraryIndex
    {
        /// <summary>The window's length, in milliseconds.</summary>
        public const int WindowMs = 2000;

        /// <summary>How far apart windows start, in milliseconds.</summary>
        public const int StrideMs = 10000;

        /// <summary>The most windows per video: its first 10 minutes.</summary>
        public const int MaxWindows = 60;

        /// <summary>The longest side of the frames embedded.</summary>
        public const int FrameSize = 512;

        /// <summary>The longest side of a video's thumbnail.</summary>
        public const int ThumbnailSize = 240;

        /// <summary>The vision tokens per frame.</summary>
        public const int VisionTokensPerFrame = 70;

        /// <summary>Changes when the settings above change, so videos are indexed again.</summary>
        public const int SettingsVersion = 1;

        /// <summary>How far below a video's best window another may be to be one of its moments.</summary>
        public const float MomentBelowBest = 0.04f;

        /// <summary>The most moments kept per video found.</summary>
        public const int MomentsPerVideo = 3;

        private readonly IVideoIndexStore _store;
        private readonly IVideoLibrarySource _library;
        private readonly object _lock = new object();
        private Dictionary<string, ChatVideoEntry> _entries;
        private Dictionary<string, List<(VideoWindow Window, float[] Embedding)>> _windows;

        /// <summary>Create the index over a store and a library.</summary>
        public VideoLibraryIndex(IVideoIndexStore store, IVideoLibrarySource library)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>Raised after a video is indexed or removed.</summary>
        public event Action Changed;

        /// <summary>How many videos are indexed.</summary>
        public int Count
        {
            get { lock (_lock) return _entries?.Count ?? 0; }
        }

        /// <summary>How many videos the last pass found to index (up to the limit).</summary>
        public int Total { get; private set; }

        /// <summary>How many videos the last pass couldn't read.</summary>
        public int UnreadableCount { get; private set; }

        /// <summary>Whether a pass is running.</summary>
        public bool IsIndexing { get; private set; }

        /// <summary>Read the saved index, once.</summary>
        public async Task LoadAsync()
        {
            lock (_lock)
                if (_entries != null)
                    return;
            IReadOnlyList<ChatVideoEntry> entries = await _store.GetVideoEntriesAsync().ConfigureAwait(false);
            IReadOnlyList<ChatVideoWindow> windows = await _store.GetVideoWindowsAsync().ConfigureAwait(false);
            var byVideo = new Dictionary<string, List<(VideoWindow, float[])>>();
            foreach (ChatVideoWindow w in windows)
            {
                if (!byVideo.TryGetValue(w.VideoId, out var list))
                    byVideo[w.VideoId] = list = new List<(VideoWindow, float[])>();
                list.Add((ToWindow(w.StartMs, w.EndMs), ToFloats(w.Embedding)));
            }
            lock (_lock)
            {
                if (_entries != null)
                    return;
                _entries = entries.ToDictionary(e => e.VideoId);
                _windows = byVideo;
            }
        }

        /// <summary>
        /// Index the newest videos, up to maxVideos (0 for all), skipping those already indexed with this model and these
        /// settings, and removing those gone or past the limit. A video's windows are saved once it is done.
        /// </summary>
        /// <param name="model">The search model (images; sound too when it hears)</param>
        /// <param name="modelName">The model's key for the index (its file name)</param>
        /// <param name="runEmbedding">Runs one embedding, e.g. through the app's gate and workload</param>
        /// <param name="maxVideos">The most videos, newest first; 0 for all</param>
        /// <param name="status">Told the progress ("Indexing video 3 of 20"), or null</param>
        /// <param name="cancel">Stops between windows</param>
        /// <param name="waitForWork">Waited before each video (e.g. for a charger), or null</param>
        public async Task IndexAsync(EmbeddingModel model, string modelName,
            Func<Func<float[]>, CancellationToken, Task<float[]>> runEmbedding, int maxVideos, Action<string> status,
            CancellationToken cancel, Func<CancellationToken, Task> waitForWork = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (runEmbedding == null) throw new ArgumentNullException(nameof(runEmbedding));
            IsIndexing = true;
            try
            {
                await LoadAsync().ConfigureAwait(false);
                IReadOnlyList<LibraryVideo> videos = await _library.GetRecentVideosAsync(maxVideos).ConfigureAwait(false);
                if (videos == null)
                    return;
                Total = videos.Count;
                UnreadableCount = 0;
                var keep = new HashSet<string>(videos.Select(v => v.Id));
                List<string> gone;
                lock (_lock)
                    gone = _entries.Keys.Where(id => !keep.Contains(id)).ToList();
                if (gone.Count > 0)
                {
                    await _store.DeleteVideosAsync(gone).ConfigureAwait(false);
                    lock (_lock)
                        foreach (string id in gone)
                        {
                            _entries.Remove(id);
                            _windows.Remove(id);
                        }
                    Changed?.Invoke();
                }
                for (int i = 0; i < videos.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    LibraryVideo video = videos[i];
                    if (IsCurrent(video, modelName, model.SupportsAudio))
                        continue;
                    if (waitForWork != null)
                        await waitForWork(cancel).ConfigureAwait(false);
                    status?.Invoke(String.Format(CultureInfo.InvariantCulture, "Indexing video {0} of {1}", i + 1, videos.Count));
                    if (!await IndexVideoAsync(model, modelName, video, runEmbedding, cancel).ConfigureAwait(false))
                        UnreadableCount++;
                }
            }
            finally
            {
                IsIndexing = false;
            }
        }

        private bool IsCurrent(LibraryVideo video, string modelName, bool hears)
        {
            lock (_lock)
                return _entries.TryGetValue(video.Id, out ChatVideoEntry entry) && entry.Version == video.Version
                    && entry.EmbeddingModel == modelName && entry.SettingsVersion == SettingsVersion;
        }

        private async Task<bool> IndexVideoAsync(EmbeddingModel model, string modelName, LibraryVideo video,
            Func<Func<float[]>, CancellationToken, Task<float[]>> runEmbedding, CancellationToken cancel)
        {
            ILibraryVideoReader reader;
            try
            {
                reader = await _library.OpenAsync(video.Id, cancel).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                return false;
            }
            if (reader == null || reader.DurationMs <= 0)
            {
                reader?.Dispose();
                return false;
            }
            using (reader)
            {
                var index = new VideoMomentIndex
                {
                    WindowMs = WindowMs,
                    FramesPerWindow = 1,
                    StrideMs = StrideMs,
                    MaxWindows = MaxWindows,
                    VisionTokensPerFrame = VisionTokensPerFrame,
                    IncludeAudio = true
                };
                try
                {
                    await index.BuildAsync(model, reader.DurationMs,
                        (ms, token) => reader.FrameAtAsync(ms, FrameSize, token),
                        reader.HasAudio ? (Func<long, long, CancellationToken, Task<short[]>>)reader.ReadAudioAsync : null,
                        runEmbedding, null, cancel).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    return false;
                }
                byte[] thumbnail = null;
                if (index.Windows.Count > 0)
                {
                    try { thumbnail = await reader.FrameAtAsync(index.Windows[0].FrameTimesMs[0], ThumbnailSize, cancel).ConfigureAwait(false); }
                    catch (Exception e) when (!(e is OperationCanceledException)) { }
                }
                var entry = new ChatVideoEntry
                {
                    VideoId = video.Id,
                    EmbeddingModel = modelName,
                    Version = video.Version,
                    SettingsVersion = SettingsVersion,
                    Taken = video.Taken.UtcDateTime,
                    DurationMs = reader.DurationMs,
                    IndexedMs = index.Windows.Count == 0 ? 0 : index.Windows[index.Windows.Count - 1].EndMs,
                    Name = video.Name,
                    WithAudio = index.WithAudio,
                    Thumbnail = thumbnail
                };
                var windows = index.Windows.Where(w => w.Embedding != null).Select(w => new ChatVideoWindow
                {
                    VideoId = video.Id,
                    StartMs = w.StartMs,
                    EndMs = w.EndMs,
                    Embedding = ToBytes(w.Embedding)
                }).ToList();
                await _store.SaveVideoAsync(entry, windows).ConfigureAwait(false);
                lock (_lock)
                {
                    _entries[video.Id] = entry;
                    _windows[video.Id] = index.Windows.Where(w => w.Embedding != null).Select(w => (w, w.Embedding)).ToList();
                }
                Changed?.Invoke();
                return true;
            }
        }

        /// <summary>
        /// The videos that match a description, best first, with their best moments - those taken between from and to
        /// when given; without a description, the newest. A video's score is its best window's; it is shown only if
        /// it stands out from the baseline "a video" (<see cref="ChatVideoLibrary.SelectMatches"/>).
        /// </summary>
        /// <param name="model">The search model</param>
        /// <param name="modelName">Its key (only videos indexed with it are searched)</param>
        /// <param name="enterSearch">Takes the model for the search (the app's gate), or null</param>
        /// <param name="description">What to find, or null</param>
        /// <param name="from">Taken from (inclusive), or null</param>
        /// <param name="to">Taken before (exclusive), or null</param>
        /// <param name="cancel">Stops waiting for the model</param>
        public Task<List<VideoLibraryMatch>> SearchAsync(EmbeddingModel model, string modelName,
            Func<CancellationToken, Task<IDisposable>> enterSearch, string description, DateTime? from, DateTime? to,
            CancellationToken cancel) =>
            SearchAsync(model, modelName, enterSearch, new[] { description }, from, to, cancel);

        /// <summary>
        /// The videos that match any of several wordings of a request (e.g. the request as said and with its dates and
        /// filler words taken out): each video is judged by the wording it stands out most for. On a Galaxy S10 the
        /// shortened "woman military uniform" stood out from the baseline by 0.022 and the request as said by 0.053.
        /// </summary>
        /// <param name="model">The search model</param>
        /// <param name="modelName">Its key</param>
        /// <param name="enterSearch">Takes the model for the search, or null</param>
        /// <param name="descriptions">The wordings; empty or blank ones are skipped, none for the newest videos</param>
        /// <param name="from">Taken from (inclusive), or null</param>
        /// <param name="to">Taken before (exclusive), or null</param>
        /// <param name="cancel">Stops waiting for the model</param>
        public async Task<List<VideoLibraryMatch>> SearchAsync(EmbeddingModel model, string modelName,
            Func<CancellationToken, Task<IDisposable>> enterSearch, IReadOnlyList<string> descriptions, DateTime? from, DateTime? to,
            CancellationToken cancel)
        {
            List<string> wordings = (descriptions ?? new string[0]).Where(d => !String.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await LoadAsync().ConfigureAwait(false);
            List<ChatVideoEntry> candidates;
            lock (_lock)
                candidates = _entries.Values.Where(e => e.EmbeddingModel == modelName
                    && (from == null || e.Taken >= from.Value.ToUniversalTime())
                    && (to == null || e.Taken < to.Value.ToUniversalTime())
                    && _windows.ContainsKey(e.VideoId)).ToList();
            if (candidates.Count == 0)
                return new List<VideoLibraryMatch>();
            if (wordings.Count == 0)
                return candidates.OrderByDescending(e => e.Taken).Take(ChatVideoLibrary.MaxResults)
                    .Select(e => new VideoLibraryMatch { Video = e, Moments = new List<VideoMoment>() }).ToList();

            var queries = new List<float[]>();
            float[] baseline;
            IDisposable held = enterSearch == null ? null : await enterSearch(cancel).ConfigureAwait(false);
            try
            {
                foreach (string wording in wordings)
                    queries.Add(await Task.Run(() => model.EmbedQuery(wording), cancel).ConfigureAwait(false));
                baseline = await Task.Run(() => model.EmbedQuery(ChatVideoLibrary.BaselineQuery), cancel).ConfigureAwait(false);
            }
            finally
            {
                held?.Dispose();
            }
            return Rank(candidates, queries, baseline);
        }

        /// <summary>Rank indexed videos against one query's embedding (see the overload for several).</summary>
        public List<VideoLibraryMatch> Rank(List<ChatVideoEntry> candidates, float[] query, float[] baseline) =>
            Rank(candidates, new[] { query }, baseline);

        /// <summary>
        /// Rank indexed videos' windows against a query's and the baseline's embeddings: the videos to show, best first,
        /// with their best moments.
        /// </summary>
        public List<VideoLibraryMatch> Rank(List<ChatVideoEntry> candidates, IReadOnlyList<float[]> queries, float[] baseline)
        {
            var scores = new List<float>();
            var baselineScores = new List<float>();
            var scored = new List<List<KeyValuePair<VideoWindow, float>>>();
            foreach (ChatVideoEntry entry in candidates)
            {
                List<(VideoWindow Window, float[] Embedding)> windows;
                lock (_lock)
                    windows = _windows[entry.VideoId];
                // Each wording's windows, its best window, and how far that stands out from the baseline; the video
                // is judged by the wording it stands out most for.
                List<KeyValuePair<VideoWindow, float>> chosen = new List<KeyValuePair<VideoWindow, float>>();
                float chosenScore = 0, chosenBaseline = 0, chosenLift = float.MinValue;
                foreach (float[] query in queries)
                {
                    var list = windows.Select(w => new KeyValuePair<VideoWindow, float>(w.Window, EmbeddingModel.CosineSimilarity(query, w.Embedding))).ToList();
                    if (list.Count == 0)
                        continue;
                    int best = 0;
                    for (int i = 1; i < list.Count; i++)
                        if (list[i].Value > list[best].Value)
                            best = i;
                    float baselineScore = EmbeddingModel.CosineSimilarity(baseline, windows[best].Embedding);
                    if (list[best].Value - baselineScore > chosenLift)
                    {
                        chosen = list;
                        chosenScore = list[best].Value;
                        chosenBaseline = baselineScore;
                        chosenLift = chosenScore - baselineScore;
                    }
                }
                scored.Add(chosen);
                scores.Add(chosenScore);
                baselineScores.Add(chosenBaseline);
            }
            return ChatVideoLibrary.SelectMatches(scores, baselineScores).Select(i => new VideoLibraryMatch
            {
                Video = candidates[i],
                Score = scores[i],
                // Only windows close to the video's best make its moments, so a weak one next to them isn't merged in.
                Moments = VideoMomentIndex.Rank(scored[i].Where(w => w.Value >= scores[i] - MomentBelowBest), MomentsPerVideo, StrideMs)
                    .Take(MomentsPerVideo).ToList()
            }).ToList();
        }

        /// <summary>The windows of an indexed video (for tests and the card).</summary>
        public IReadOnlyList<VideoWindow> WindowsOf(string videoId)
        {
            lock (_lock)
                return _windows != null && _windows.TryGetValue(videoId, out var list) ? list.Select(w => w.Window).ToList() : new List<VideoWindow>();
        }

        /// <summary>An indexed video's entry, or null.</summary>
        public ChatVideoEntry EntryOf(string videoId)
        {
            lock (_lock)
                return _entries != null && _entries.TryGetValue(videoId, out ChatVideoEntry entry) ? entry : null;
        }

        /// <summary>Forget the index (the store too).</summary>
        public async Task ClearAsync()
        {
            await _store.ClearVideoIndexAsync().ConfigureAwait(false);
            lock (_lock)
            {
                _entries = new Dictionary<string, ChatVideoEntry>();
                _windows = new Dictionary<string, List<(VideoWindow, float[])>>();
            }
            Changed?.Invoke();
        }

        private static VideoWindow ToWindow(long start, long end) => new VideoWindow(start, end, new[] { start + (end - start) / 2 });

        /// <summary>Floats as their bytes.</summary>
        public static byte[] ToBytes(float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>Bytes as floats.</summary>
        public static float[] ToFloats(byte[] bytes)
        {
            var values = new float[(bytes?.Length ?? 0) / sizeof(float)];
            if (values.Length > 0)
                Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
            return values;
        }
    }

    /// <summary>A video a search found, as saved with the reply: enough to show its card again and play it.</summary>
    public sealed class ChatFoundVideo
    {
        /// <summary>The library's id</summary>
        public string VideoId { get; set; }

        /// <summary>Its name</summary>
        public string Name { get; set; }

        /// <summary>When it was taken, UTC</summary>
        public DateTime Taken { get; set; }

        /// <summary>Its length, in milliseconds</summary>
        public long DurationMs { get; set; }

        /// <summary>Where its best moment starts, in milliseconds</summary>
        public long StartMs { get; set; }

        /// <summary>Where its best moment ends</summary>
        public long EndMs { get; set; }

        /// <summary>How well it matched</summary>
        public float Score { get; set; }

        /// <summary>A JPEG of the video (its first indexed frame)</summary>
        public byte[] Thumbnail { get; set; }
    }

    /// <summary>The videos a search found, saved with the reply.</summary>
    public sealed class ChatFoundVideos
    {
        /// <summary>What was asked</summary>
        public string Request { get; set; }

        /// <summary>The videos, best first</summary>
        public List<ChatFoundVideo> Videos { get; set; } = new List<ChatFoundVideo>();

        /// <summary>As JSON (UTF-8).</summary>
        public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this);

        /// <summary>From ToJson's bytes, or null.</summary>
        public static ChatFoundVideos FromJson(byte[] json)
        {
            if (json == null || json.Length == 0)
                return null;
            try { return JsonSerializer.Deserialize<ChatFoundVideos>(json); }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>The find_videos tool's words and rules ("Search my videos").</summary>
    public static class ChatVideoLibrary
    {
        /// <summary>The routing group of an app without photo search (with it, find_videos joins its group).</summary>
        public const string GroupName = "video_search";

        /// <summary>The group's summary.</summary>
        public const string GroupSummary = "find and play the user's own videos on this device";

        /// <summary>Words that bring the tool (added to the photo group's).</summary>
        public static readonly string[] GroupKeywords = { "video", "clip", "footage", "recording", "filmed", "movie of" };

        /// <summary>The instructions that go with the tool.</summary>
        public const string Instructions = "When the user asks you to find or show their videos or clips, use the find_videos tool with the request in their " +
            "words; the videos it finds are shown to the user, who can play them - you don't see them.";

        /// <summary>The baseline a video must stand out from.</summary>
        public const string BaselineQuery = "a video";

        /// <summary>The lowest similarity a video's best window must reach.</summary>
        public const float MinScore = 0.6f;

        /// <summary>
        /// How far above the baseline it must be: lower than photos' 0.035, since a video's windows (frames with their
        /// sound) are all closer to "a video". On four synthetic clips (EmbeddingGemma 2 Multimodal, 2026-10-30) the
        /// right clip was 0.033-0.095 above it and every other clip at or below it.
        /// </summary>
        public const float MinLift = 0.02f;

        /// <summary>How far below the best video's lift another may be.</summary>
        public const float MaxBelowBestLift = 0.04f;

        /// <summary>The most videos shown.</summary>
        public const int MaxResults = 6;

        /// <summary>Which videos to show, best first, by photos' rules (<see cref="ChatPhotos.SelectMatches"/>) with <see cref="MinLift"/>.</summary>
        public static List<int> SelectMatches(IReadOnlyList<float> scores, IReadOnlyList<float> baselineScores, int maxResults = MaxResults)
        {
            if (scores == null)
                throw new ArgumentNullException(nameof(scores));
            if (baselineScores == null || baselineScores.Count != scores.Count)
                throw new ArgumentException("One baseline score per video is needed.", nameof(baselineScores));
            var candidates = new List<(int Index, float Lift)>();
            for (int i = 0; i < scores.Count; i++)
            {
                float lift = scores[i] - baselineScores[i];
                if (scores[i] >= MinScore && lift >= MinLift)
                    candidates.Add((i, lift));
            }
            if (candidates.Count == 0)
                return new List<int>();
            float best = candidates.Max(c => c.Lift);
            return candidates.Where(c => c.Lift >= best - MaxBelowBestLift).OrderByDescending(c => c.Lift)
                .Take(maxResults).Select(c => c.Index).ToList();
        }

        /// <summary>
        /// What find_videos tells the model: the videos found (when taken, length, best moment), that the user sees and
        /// can play them, and how much of the library is indexed while it still is.
        /// </summary>
        /// <param name="request">What was asked</param>
        /// <param name="found">The videos, best first</param>
        /// <param name="indexed">How many videos are indexed</param>
        /// <param name="total">How many there are to index</param>
        /// <param name="now">Now, for "3 days ago"</param>
        public static string Reply(string request, IReadOnlyList<ChatFoundVideo> found, int indexed, int total, DateTime now)
        {
            string partial = indexed < total
                ? String.Format(CultureInfo.InvariantCulture, " (only {0} of the {1} newest videos are indexed so far)", indexed, total)
                : string.Empty;
            if (found == null || found.Count == 0)
                return "No video matches “" + request + "”" + partial + ".";
            var text = new StringBuilder();
            text.Append("Found ").Append(found.Count == 1 ? "1 video" : found.Count.ToString(CultureInfo.InvariantCulture) + " videos")
                .Append(" for “").Append(request).Append("”, best first: ");
            text.Append(String.Join("; ", found.Select(v => Describe(v, now))));
            text.Append(partial).Append(". They are shown to the user, who can tap one to play it from that moment; you can't see the videos. " +
                "Tell the user in one short sentence what you found.");
            return text.ToString();
        }

        /// <summary>"“IMG_0042.MOV”, taken 3 days ago, 01:20 long, best at 00:40" - a video for the model.</summary>
        public static string Describe(ChatFoundVideo video, DateTime now)
        {
            string name = String.IsNullOrWhiteSpace(video.Name) ? string.Empty : "“" + video.Name + "”, ";
            return name + "taken " + Ago(video.Taken, now) + ", " + VideoMomentIndex.FormatTime(video.DurationMs) + " long, best at " +
                VideoMomentIndex.FormatTime(video.StartMs);
        }

        /// <summary>"today", "yesterday", "3 days ago", "on 12 May 2025".</summary>
        public static string Ago(DateTime takenUtc, DateTime now)
        {
            DateTime taken = takenUtc.Kind == DateTimeKind.Utc ? takenUtc.ToLocalTime() : takenUtc;
            int days = (now.Date - taken.Date).Days;
            if (days <= 0) return "today";
            if (days == 1) return "yesterday";
            if (days < 7) return days.ToString(CultureInfo.InvariantCulture) + " days ago";
            return "on " + taken.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        }
    }
}
