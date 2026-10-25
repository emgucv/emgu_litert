// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Audio;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Agents' sound input without a microphone: BoxResampler (what Emgu.LiteRT.LM.Maui's MicrophoneSoundSource averages
// 44.1/48 kHz input down with) and ClipSoundSource.
[TestFixture]
public class UnitTestSoundInput
{
    [Test]
    public void ResamplesToSixteenKilohertzInAnyPieces()
    {
        // One second at 48 kHz: a constant stays constant, and the count is 16,000 however the input is cut.
        var input = Enumerable.Repeat(0.5f, 48000).ToArray();
        float[] whole = new BoxResampler(48000).Process(input, input.Length);
        Assert.That(whole.Length, Is.EqualTo(16000));
        Assert.That(whole, Is.All.EqualTo(0.5f).Within(1e-5f));

        var resampler = new BoxResampler(44100);
        var pieces = new List<float>();
        var piece = new float[441];
        for (int i = 0; i < 100; i++)
        {
            for (int j = 0; j < piece.Length; j++) piece[j] = (float)Math.Sin(2 * Math.PI * 440 * (i * piece.Length + j) / 44100.0);
            pieces.AddRange(resampler.Process(piece, piece.Length));
        }
        Assert.That(pieces.Count, Is.InRange(15999, 16000));
        // Each output averages three samples at 48 kHz; at the Nyquist rate of the input, they cancel out.
        float[] alternating = new BoxResampler(48000).Process(new float[] { 1, -1, 1, -1, 1, -1 }, 6);
        Assert.That(alternating.Length, Is.EqualTo(2));
        Assert.That(alternating.Select(Math.Abs), Is.All.LessThanOrEqualTo(1 / 3f + 1e-5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoxResampler(0));
    }

    [Test]
    public async Task ClipPlaysInOrderAtItsOwnSpeed()
    {
        var clip = Enumerable.Range(0, 4000).Select(i => i / 4000f).ToArray();     // 250 ms
        var heard = new List<float>();
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var source = new ClipSoundSource(clip);
        source.Samples += samples => { lock (heard) heard.AddRange(samples); };
        source.Finished += () => finished.TrySetResult(true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(await source.StartAsync(), Is.True);
        Assert.That(await Task.WhenAny(finished.Task, Task.Delay(5000)), Is.SameAs(finished.Task));
        Assert.That(clock.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(200));
        await source.StopAsync();
        Assert.That(heard, Is.EqualTo(clip));
    }
}
