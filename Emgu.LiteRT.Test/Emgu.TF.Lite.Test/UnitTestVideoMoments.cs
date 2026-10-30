// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// VideoMomentIndex without a model: windows and their frame times, the sound split per frame, the token budget, merging
// the best windows into moments, building through the app's runEmbedding, and saving an index; and the chat's
// find_video_moment tool and its texts.
[TestFixture]
public class UnitTestVideoMoments
{
    [Test]
    public void CutsAVideoIntoWindows()
    {
        // 5 s in 2 s windows of 2 frames: 0-2, 2-4 and a short 4-5, frames at each end.
        List<VideoWindow> windows = VideoMomentIndex.Split(5000);
        Assert.That(windows.Select(w => (w.StartMs, w.EndMs)), Is.EqualTo(new[] { (0L, 2000L), (2000L, 4000L), (4000L, 5000L) }));
        Assert.That(windows[0].FrameTimesMs, Is.EqualTo(new long[] { 0, 2000 }));
        Assert.That(windows[2].FrameTimesMs, Is.EqualTo(new long[] { 4000, 5000 }));
        // One frame: the middle. 4 frames over 6 s.
        Assert.That(VideoMomentIndex.Split(2000, 2000, 1)[0].FrameTimesMs, Is.EqualTo(new long[] { 1000 }));
        Assert.That(VideoMomentIndex.Split(6000, 6000, 4)[0].FrameTimesMs, Is.EqualTo(new long[] { 0, 2000, 4000, 6000 }));
        // Overlap: 4 s windows a second apart overlapping by 3; never closer than a second.
        Assert.That(VideoMomentIndex.Split(6000, 4000, 2, 3000).Select(w => w.StartMs), Is.EqualTo(new long[] { 0, 1000, 2000, 3000, 4000, 5000 }));
        Assert.That(VideoMomentIndex.Split(3000, 2000, 2, 1900).Select(w => w.StartMs), Is.EqualTo(new long[] { 0, 1000, 2000 }));
        Assert.That(VideoMomentIndex.Split(0), Is.Empty);
    }

    [Test]
    public void SplitsTheSoundPerFrame()
    {
        // 2 s at 16 kHz into 2 slices of a second each.
        short[] pcm = Enumerable.Range(0, 32000).Select(i => (short)(i / 16000)).ToArray();
        short[][] slices = VideoMomentIndex.SplitAudio(pcm, 4000, 6000, 2);
        Assert.That(slices.Select(s => s.Length), Is.EqualTo(new[] { 16000, 16000 }));
        Assert.That(slices[1].All(v => v == 1), Is.True);
        // The sound ends after 1.5 s: the second slice is cut short, a third would be missing.
        short[][] cut = VideoMomentIndex.SplitAudio(pcm.Take(24000).ToArray(), 0, 3000, 3);
        Assert.That(cut[0].Length, Is.EqualTo(16000));
        Assert.That(cut[1].Length, Is.EqualTo(8000));
        Assert.That(cut[2], Is.Null);
        Assert.That(VideoMomentIndex.SplitAudio(null, 0, 2000, 2), Is.EqualTo(new short[][] { null, null }));
    }

    [Test]
    public void EstimatesAWindowsTokens()
    {
        // 2 frames of 70 tokens and 2 one-second slices: 140 + 13 + 4 x 8 + 2.
        Assert.That(VideoMomentIndex.EstimateTokens(2, 70, 2, 1f), Is.EqualTo(187));
        // 6 frames at the default 140 tokens with sound still fit 1024; 8 don't.
        Assert.That(VideoMomentIndex.EstimateTokens(6, 140, 6, 1f), Is.LessThanOrEqualTo(1024));
        Assert.That(VideoMomentIndex.EstimateTokens(8, 140, 8, 1f), Is.GreaterThan(1024));
    }

    private static KeyValuePair<VideoWindow, float> Scored(long start, float score) =>
        new KeyValuePair<VideoWindow, float>(new VideoWindow(start, start + 2000, new[] { start, start + 2000 }), score);

    [Test]
    public void MergesTheBestWindowsIntoMoments()
    {
        var scored = new[] { Scored(0, 0.1f), Scored(2000, 0.5f), Scored(4000, 0.6f), Scored(6000, 0.2f), Scored(10000, 0.55f), Scored(14000, 0.3f) };
        // The best 3: 2-4 and 4-6 touch, 10-12 is alone; the moment with the best window first.
        IReadOnlyList<VideoMoment> moments = VideoMomentIndex.Rank(scored, 3);
        Assert.That(moments.Select(m => m.ToString()), Is.EqualTo(new[] { "00:02-00:06", "00:10-00:12" }));
        Assert.That(moments[0].Score, Is.EqualTo(0.6f));
        Assert.That(moments[0].Windows.Count, Is.EqualTo(2));
        // Within half a second still merges; a second apart doesn't.
        Assert.That(VideoMomentIndex.Rank(new[] { Scored(0, 1), Scored(2400, 1) }).Count, Is.EqualTo(1));
        Assert.That(VideoMomentIndex.Rank(new[] { Scored(0, 1), Scored(3000, 1) }).Count, Is.EqualTo(2));
        Assert.That(VideoMomentIndex.FormatTime(3_725_000), Is.EqualTo("1:02:05"));
    }

    [Test]
    public void SavesAndLoadsAnIndex()
    {
        var index = new VideoMomentIndex { WindowMs = 4000, FramesPerWindow = 3 };
        using var saved = new MemoryStream();
        index.Save(saved);
        saved.Position = 0;
        VideoMomentIndex loaded = VideoMomentIndex.Load(saved);
        Assert.That(loaded.WindowMs, Is.EqualTo(4000));
        Assert.That(loaded.FramesPerWindow, Is.EqualTo(3));
        Assert.That(loaded.IsBuilt, Is.False);

        byte[] wav = VideoMomentIndex.ToWav(new short[] { 1, -1, 300 }, 16000);
        Assert.That(wav.Length, Is.EqualTo(50));
        Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
        Assert.That(BitConverter.ToInt16(wav, 48), Is.EqualTo(300));
    }

    // An embedding model that never loads: BuildAsync's runEmbedding below returns the vectors instead.
    private sealed class NoModel : EmbeddingModel
    {
        public override DownloadableFile DefaultModelFile => null;
        public override bool SupportsImages => true;
    }

    [Test]
    public async Task BuildsThroughTheAppsEmbedding()
    {
        // 5 s: three windows, each embedded once through runEmbedding (the app's gate), with progress after each.
        var asked = new List<long>();
        var progress = new List<double>();
        int runs = 0;
        var index = new VideoMomentIndex();
        using var model = new NoModel();
        await index.BuildAsync(model, 5000,
            (ms, token) => { asked.Add(ms); return Task.FromResult(new byte[] { 1 }); },
            null,
            (embed, token) => { runs++; return Task.FromResult(new float[] { runs, 0 }); },
            new SynchronousProgress(progress.Add));
        Assert.That(runs, Is.EqualTo(3));
        Assert.That(asked, Is.EqualTo(new long[] { 0, 2000, 2000, 4000, 4000, 5000 }));
        Assert.That(progress.Last(), Is.EqualTo(1.0));
        Assert.That(index.IsBuilt, Is.True);
        Assert.That(index.IndexedWindows, Is.EqualTo(3));
        Assert.That(index.WithAudio, Is.False, "no sound given");
        Assert.That(index.Windows[2].Embedding, Is.EqualTo(new float[] { 3, 0 }));

        // Saved and loaded with its embeddings.
        using var saved = new MemoryStream();
        index.Save(saved);
        saved.Position = 0;
        VideoMomentIndex loaded = VideoMomentIndex.Load(saved);
        Assert.That(loaded.IsBuilt, Is.True);
        Assert.That(loaded.DurationMs, Is.EqualTo(5000));
        Assert.That(loaded.Windows.Select(w => w.Embedding[0]), Is.EqualTo(new float[] { 1, 2, 3 }));

        // A window without any frame stops the build.
        Assert.ThrowsAsync<InvalidDataException>(() => new VideoMomentIndex().BuildAsync(model, 2000,
            (ms, token) => Task.FromResult<byte[]>(null), null, (embed, token) => Task.FromResult(new float[1])));
    }

    // Progress<T> reports on the synchronization context later; this one reports at once.
    private sealed class SynchronousProgress : IProgress<double>
    {
        private readonly Action<double> _report;
        public SynchronousProgress(Action<double> report) => _report = report;
        public void Report(double value) => _report(value);
    }

    [Test]
    public async Task TheToolFindsMomentsAndSaysTheirTimes()
    {
        string asked = null;
        AIFunction tool = AgentTools.FindVideoMoment((request, token) => { asked = request; return Task.FromResult("found"); });
        Assert.That(tool.Name, Is.EqualTo("find_video_moment"));
        Assert.That((await tool.InvokeAsync(new AIFunctionArguments { ["request"] = "the dog jumps in" }))?.ToString(), Does.Contain("found"));
        Assert.That(asked, Is.EqualTo("the dog jumps in"));
        var call = new FunctionCallContent("1", "find_video_moment", new Dictionary<string, object> { ["request"] = "the dog jumps in" });
        Assert.That(AgentTools.Describe(call), Is.EqualTo("Searching the video for \u201Cthe dog jumps in\u201D"));

        var moments = new List<ChatVideoMoment>
        {
            new ChatVideoMoment { StartMs = 64000, EndMs = 68000, Score = 0.75f },
            new ChatVideoMoment { StartMs = 2000, EndMs = 4000, Score = 0.7f }
        };
        string reply = ChatVideos.Reply("the dog jumps in", moments, 1);
        Assert.That(reply, Does.StartWith("Found 2 moments for \u201Cthe dog jumps in\u201D, best first: 01:04-01:08, 00:02-00:04."));
        Assert.That(reply, Does.Contain("you can't see the video"));
        Assert.That(ChatVideos.Reply("fireworks", moments.Take(1).ToList(), 0.4), Does.Contain("1 moment").And.Contain("only the first 40% of the video"));
        Assert.That(ChatVideos.Reply("fireworks", new ChatVideoMoment[0], 1), Is.EqualTo("No moment of the video matches \u201Cfireworks\u201D."));
        Assert.That(ChatVideos.Context("party.mp4", 151000, 1), Is.EqualTo("The user attached the video \u201Cparty.mp4\u201D (02:31); find_video_moment finds when something happens in it."));
        Assert.That(ChatVideos.Context("party.mp4", 151000, 0.25), Does.Contain("still being watched (25% done)"));

        // Only moments close to the best are shown, at most 4.
        Assert.That(ChatVideos.CountShown(new[] { 0.75f, 0.68f, 0.66f }), Is.EqualTo(1));
        Assert.That(ChatVideos.CountShown(new[] { 0.75f, 0.73f, 0.70f }), Is.EqualTo(2));
        Assert.That(ChatVideos.CountShown(new[] { 0.75f, 0.75f, 0.75f, 0.75f, 0.75f }), Is.EqualTo(4));
        Assert.That(ChatVideos.CountShown(new float[0]), Is.EqualTo(0));
    }

    [Test]
    public void TheMomentsAreSavedWithTheReply()
    {
        var saved = new ChatVideoMoments
        {
            VideoName = "party.mp4",
            VideoFile = "0a1b.mp4",
            Request = "cake",
            Moments = { new ChatVideoMoment { StartMs = 2000, EndMs = 6000, Score = 0.7f, Thumbnail = new byte[] { 0xFF, 0xD8, 1 } } }
        };
        ChatVideoMoments loaded = ChatVideoMoments.FromJson(saved.ToJson());
        Assert.That(loaded.VideoName, Is.EqualTo("party.mp4"));
        Assert.That(loaded.VideoFile, Is.EqualTo("0a1b.mp4"));
        Assert.That(loaded.Moments.Single().ToString(), Is.EqualTo("00:02-00:06"));
        Assert.That(loaded.Moments.Single().Thumbnail, Is.EqualTo(new byte[] { 0xFF, 0xD8, 1 }));
        Assert.That(ChatVideoMoments.FromJson(System.Text.Encoding.UTF8.GetBytes("not json")), Is.Null);
        Assert.That(ChatVideoMoments.FromJson(null), Is.Null);
    }
}
