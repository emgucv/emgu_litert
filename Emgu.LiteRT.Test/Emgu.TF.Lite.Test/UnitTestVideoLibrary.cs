// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// "Search my videos" without a model: the library index's passes (newest first, the limit, skipping what is indexed,
// dropping what is gone, unreadable videos), its ranking, and find_videos' texts.
[TestFixture]
public class UnitTestVideoLibrary
{
    private string _folder;
    private ChatHistoryStore _store;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "EmguVideoLibraryTest_" + Guid.NewGuid().ToString("N"));
        _store = new ChatHistoryStore(_folder);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _store.CloseAsync();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    // An embedding model that never loads: the windows' vectors come from runEmbedding.
    private sealed class NoModel : EmbeddingModel
    {
        public override DownloadableFile DefaultModelFile => null;
        public override bool SupportsImages => true;
    }

    private sealed class FakeReader : ILibraryVideoReader
    {
        private readonly FakeLibrary _library;
        private readonly string _id;

        public FakeReader(FakeLibrary library, string id, long durationMs)
        {
            _library = library;
            _id = id;
            DurationMs = durationMs;
        }

        public long DurationMs { get; }
        public bool HasAudio => false;

        public Task<byte[]> FrameAtAsync(long ms, int maxSize, CancellationToken cancel)
        {
            _library.LastFrame = (_id, ms);
            return Task.FromResult(new byte[] { 1, (byte)maxSize });
        }

        public Task<short[]> ReadAudioAsync(long fromMs, long toMs, CancellationToken cancel) => Task.FromResult<short[]>(null);
        public void Dispose() { }
    }

    private sealed class FakeLibrary : IVideoLibrarySource
    {
        public readonly List<LibraryVideo> Videos = new List<LibraryVideo>();
        public readonly HashSet<string> Unreadable = new HashSet<string>();
        public (string Id, long Ms) LastFrame;

        public Task<IReadOnlyList<LibraryVideo>> GetRecentVideosAsync(int max) =>
            Task.FromResult<IReadOnlyList<LibraryVideo>>(Videos.OrderByDescending(v => v.Taken).Take(max > 0 ? max : int.MaxValue).ToList());

        public Task<ILibraryVideoReader> OpenAsync(string id, CancellationToken cancel) =>
            Task.FromResult<ILibraryVideoReader>(Unreadable.Contains(id) ? null : new FakeReader(this, id, Videos.Single(v => v.Id == id).DurationMs));
    }

    private static LibraryVideo Video(string id, int daysAgo, long durationMs, long version = 1) => new LibraryVideo
    {
        Id = id, Name = id + ".mp4", Taken = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero).AddDays(-daysAgo), DurationMs = durationMs, Version = version
    };

    [Test]
    public async Task IndexesTheNewestVideosSparselyAndOnlyOnce()
    {
        var library = new FakeLibrary();
        library.Videos.Add(Video("old", 9, 30000));
        library.Videos.Add(Video("new", 1, 35000));
        library.Videos.Add(Video("middle", 5, 12000));
        var index = new VideoLibraryIndex(_store, library);
        using var model = new NoModel();
        var embedded = new List<(string, long)>();
        Func<Func<float[]>, CancellationToken, Task<float[]>> run = (embed, token) =>
        {
            embedded.Add(library.LastFrame);
            return Task.FromResult(new float[] { 1, 0 });
        };
        var statuses = new List<string>();

        // The 2 newest: a frame in the middle of a 2 s window every 10 s.
        await index.IndexAsync(model, "m1", run, 2, statuses.Add, CancellationToken.None);
        Assert.That(index.Count, Is.EqualTo(2));
        Assert.That(index.Total, Is.EqualTo(2));
        Assert.That(embedded, Is.EqualTo(new[] { ("new", 1000L), ("new", 11000L), ("new", 21000L), ("new", 31000L), ("middle", 1000L), ("middle", 11000L) }));
        Assert.That(statuses, Is.EqualTo(new[] { "Indexing video 1 of 2", "Indexing video 2 of 2" }));
        ChatVideoEntry entry = index.EntryOf("new");
        Assert.That((entry.DurationMs, entry.IndexedMs, entry.SettingsVersion, entry.Name), Is.EqualTo((35000L, 32000L, VideoLibraryIndex.SettingsVersion, "new.mp4")));
        Assert.That(entry.Thumbnail, Is.EqualTo(new byte[] { 1, VideoLibraryIndex.ThumbnailSize }));

        // Saved: a new index over the same store skips both; an edited video (a new version) is indexed again.
        embedded.Clear();
        library.Videos[1].Version = 2;
        var reopened = new VideoLibraryIndex(_store, library);
        await reopened.IndexAsync(model, "m1", run, 2, null, CancellationToken.None);
        Assert.That(embedded.Select(e => e.Item1).Distinct(), Is.EqualTo(new[] { "new" }));
        Assert.That(reopened.WindowsOf("middle").Select(w => w.StartMs), Is.EqualTo(new long[] { 0, 10000 }));

        // Another model indexes everything again; a lower limit drops the older video.
        embedded.Clear();
        await reopened.IndexAsync(model, "m2", run, 1, null, CancellationToken.None);
        Assert.That(embedded.Select(e => e.Item1).Distinct(), Is.EqualTo(new[] { "new" }));
        Assert.That(reopened.Count, Is.EqualTo(1));
        Assert.That((await _store.GetVideoEntriesAsync()).Select(e => e.VideoId), Is.EqualTo(new[] { "new" }));
        Assert.That((await _store.GetVideoWindowsAsync()).Count, Is.EqualTo(4));
    }

    [Test]
    public async Task CountsVideosItCantRead()
    {
        var library = new FakeLibrary();
        library.Videos.Add(Video("cloud", 1, 20000));
        library.Videos.Add(Video("local", 2, 20000));
        library.Unreadable.Add("cloud");
        var index = new VideoLibraryIndex(_store, library);
        using var model = new NoModel();
        await index.IndexAsync(model, "m", (embed, token) => Task.FromResult(new float[] { 1, 0 }), 0, null, CancellationToken.None);
        Assert.That((index.Count, index.UnreadableCount), Is.EqualTo((1, 1)));
    }

    private async Task<VideoLibraryIndex> IndexWith(Dictionary<(string, long), float[]> vectors, params LibraryVideo[] videos)
    {
        var library = new FakeLibrary();
        library.Videos.AddRange(videos);
        var index = new VideoLibraryIndex(_store, library);
        using var model = new NoModel();
        await index.IndexAsync(model, "m", (embed, token) =>
            Task.FromResult(vectors.TryGetValue(library.LastFrame, out float[] v) ? v : new float[] { 0, 0, 1 }), 0, null, CancellationToken.None);
        return index;
    }

    [Test]
    public async Task ShowsTheVideoThatStandsOutWithItsMoment()
    {
        // The query is along x, the baseline "a video" along y. The dog video's windows at 10 s and 20 s match (a
        // moment 00:10-00:22: sparse windows 8 s apart merge); the beach video matches the baseline as much as the query.
        var vectors = new Dictionary<(string, long), float[]>
        {
            [("dog", 11000)] = new float[] { 0.9f, 0.3f, 0.3f },
            [("dog", 21000)] = new float[] { 0.85f, 0.3f, 0.3f },
            [("beach", 1000)] = new float[] { 0.6f, 0.6f, 0.5f },
        };
        VideoLibraryIndex index = await IndexWith(vectors, Video("dog", 1, 40000), Video("beach", 2, 20000));
        var candidates = (await _store.GetVideoEntriesAsync()).ToList();
        List<VideoLibraryMatch> found = index.Rank(candidates, new float[] { 1, 0, 0 }, new float[] { 0, 1, 0 });
        Assert.That(found.Select(m => m.Video.VideoId), Is.EqualTo(new[] { "dog" }));
        Assert.That(found[0].Moments.First().ToString(), Is.EqualTo("00:10-00:22"));

        // Nothing stands out: nothing is shown.
        Assert.That(index.Rank(candidates, new float[] { 0, 0, 1 }, new float[] { 0, 0, 1 }), Is.Empty);

        // Two wordings: the first matches nothing (opposite to every window), the second the dog video - judged by the one it stands out for.
        List<VideoLibraryMatch> either = index.Rank(candidates, new[] { new float[] { -1, 0, 0 }, new float[] { 1, 0, 0 } }, new float[] { 0, 1, 0 });
        Assert.That(either.Select(m => m.Video.VideoId), Is.EqualTo(new[] { "dog" }));
        Assert.That(either[0].Moments.First().ToString(), Is.EqualTo("00:10-00:22"));
    }

    [Test]
    public void ToldTheModelWhatWasFound()
    {
        var now = new DateTime(2026, 10, 30, 15, 0, 0, DateTimeKind.Local);
        var video = new ChatFoundVideo { VideoId = "v", Name = "dog.mp4", Taken = now.AddDays(-3).ToUniversalTime(), DurationMs = 80000, StartMs = 40000, EndMs = 42000 };
        string reply = ChatVideoLibrary.Reply("the dog", new[] { video }, 5, 20, now);
        Assert.That(reply, Does.StartWith("Found 1 video for “the dog”, best first: “dog.mp4”, taken 3 days ago, 01:20 long, best at 00:40 (only 5 of the 20 newest videos are indexed so far)."));
        Assert.That(reply, Does.Contain("you can't see the videos"));
        Assert.That(ChatVideoLibrary.Reply("a cat", new ChatFoundVideo[0], 20, 20, now), Is.EqualTo("No video matches “a cat”."));
        Assert.That(ChatVideoLibrary.Ago(now.AddDays(-1).ToUniversalTime(), now), Is.EqualTo("yesterday"));
        Assert.That(ChatVideoLibrary.Ago(new DateTime(2025, 5, 12, 12, 0, 0, DateTimeKind.Utc), now), Is.EqualTo("on 12 May 2025"));

        var saved = new ChatFoundVideos { Request = "the dog", Videos = { video } };
        ChatFoundVideos loaded = ChatFoundVideos.FromJson(saved.ToJson());
        Assert.That((loaded.Request, loaded.Videos.Single().StartMs, loaded.Videos.Single().Name), Is.EqualTo(("the dog", 40000L, "dog.mp4")));
        Assert.That(ChatFoundVideos.FromJson(new byte[] { 1, 2 }), Is.Null);
    }

    [Test]
    public void ShowsOnlyVideosThatStandOut()
    {
        // Like photos: above the score and lift, close to the best, at most MaxResults.
        Assert.That(ChatVideoLibrary.SelectMatches(new[] { 0.7f, 0.65f, 0.5f }, new[] { 0.6f, 0.62f, 0.3f }), Is.EqualTo(new[] { 0 }));
        Assert.That(ChatVideoLibrary.SelectMatches(new[] { 0.7f, 0.71f }, new[] { 0.6f, 0.6f }), Is.EqualTo(new[] { 1, 0 }));
        Assert.Throws<ArgumentException>(() => ChatVideoLibrary.SelectMatches(new[] { 0.7f }, new float[0]));
    }
}
