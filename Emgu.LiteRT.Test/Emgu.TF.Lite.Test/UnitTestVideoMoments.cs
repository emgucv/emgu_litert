// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// VideoMomentIndex without a model: windows and their frame times, the sound split per frame, the token budget, merging
// the best windows into moments, and saving an index.
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
}
