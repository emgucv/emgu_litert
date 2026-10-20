// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// YAMNet audio event classification on LiteRT (TODO item 41): WAV reading and resampling without a model, and the model against
// Python's results (yamnet_reference.json, made by running the same file through the TFLite interpreter).
[TestFixture]
public class UnitTestYamNet
{
    private static byte[] Wave(int rate, int channels, int bits, params short[] samples)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        int dataLength = samples.Length * bits / 8;
        w.Write(0x46464952); w.Write(36 + dataLength); w.Write(0x45564157);
        w.Write(0x20746D66); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate);
        w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write((short)bits);
        w.Write(0x61746164); w.Write(dataLength);
        foreach (short s in samples)
            if (bits == 16) w.Write(s); else w.Write((byte)(s + 128));
        return stream.ToArray();
    }

    [Test]
    public void WaveFilesAreReadAsMonoFloats()
    {
        float[] mono = YamNet.ReadWave(Wave(16000, 1, 16, 0, 16384, -32768), out int rate);
        Assert.That(rate, Is.EqualTo(16000));
        Assert.That(mono, Is.EqualTo(new[] { 0f, 0.5f, -1f }));
        // Two channels are averaged.
        float[] stereo = YamNet.ReadWave(Wave(44100, 2, 16, 16384, -16384, 8192, 8192), out rate);
        Assert.That(rate, Is.EqualTo(44100));
        Assert.That(stereo, Is.EqualTo(new[] { 0f, 0.25f }));
        Assert.That(YamNet.ReadWave(Wave(8000, 1, 8, 0, 64, -128), out _), Is.EqualTo(new[] { 0f, 0.5f, -1f }));
        Assert.Throws<InvalidDataException>(() => YamNet.ReadWave(new byte[100], out _));
    }

    [Test]
    public void SamplesAreResampledTo16Khz()
    {
        var ramp = Enumerable.Range(0, 24).Select(i => (float)i).ToArray();
        Assert.That(YamNet.Resample(ramp, 16000), Is.SameAs(ramp));
        float[] down = YamNet.Resample(ramp, 24000);     // every 1.5th sample, interpolated
        Assert.That(down.Length, Is.EqualTo(16));
        Assert.That(down[0], Is.EqualTo(0f));
        Assert.That(down[1], Is.EqualTo(1.5f).Within(1e-5));
        Assert.That(down[15], Is.EqualTo(22.5f).Within(1e-5));
        float[] up = YamNet.Resample(new[] { 0f, 1f }, 8000);
        Assert.That(up, Is.EqualTo(new[] { 0f, 0.5f, 1f, 1f }).Within(1e-6));
    }
}

[TestFixture, Category("Native")]
public class UnitTestYamNetNative
{
    private static async Task<string> ModelAsync()
    {
        var file = new DownloadableFile(YamNet.ModelUrl, Path.Combine("Emgu.LiteRT.Tests", "YamNet"), YamNet.ModelSha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(file);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "YAMNet's download or checksum failed.");
        return file.LocalFile;
    }

    // The signals the reference script made: silence, a 440 Hz sine, white noise from a fixed generator, and the speech sample.
    private static float[] Signal(string name)
    {
        const int n = YamNet.WindowSamples;
        switch (name)
        {
            case "silence": return new float[n];
            case "sine440": return Enumerable.Range(0, n).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 16000))).ToArray();
            case "noise":
                uint x = 12345;
                return Enumerable.Range(0, n).Select(_ => { x = unchecked(x * 1664525 + 1013904223); return (float)((x / 4294967296.0 * 2 - 1) * 0.3); }).ToArray();
            default: return Speech().Take(n).ToArray();
        }
    }

    private static float[] Speech()
    {
        var samples = YamNet.ReadWave(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "have_a_wonderful_day.wav")), out int rate);
        return YamNet.Resample(samples, rate);
    }

    [Test]
    public async Task ScoresMatchThePythonInterpreter()
    {
        string path = await ModelAsync();
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yamnet_reference.json")));
        using var model = new YamNet(path);
        Assert.That(model.Labels.Count, Is.EqualTo(521));
        Assert.That(model.Labels[0], Is.EqualTo("Speech"));
        foreach (var signal in reference.RootElement.GetProperty("signals").EnumerateObject())
        {
            float[] scores = model.Classify(Signal(signal.Name));
            Assert.That(scores.Length, Is.EqualTo(521));
            var top = model.Top(scores, 1)[0];
            var expected = signal.Value[0];
            Assert.That(top.Label, Is.EqualTo(expected.GetProperty("label").GetString()), signal.Name);
            Assert.That(top.Score, Is.EqualTo(expected.GetProperty("score").GetSingle()).Within(0.02), signal.Name);
            foreach (var e in signal.Value.EnumerateArray())
                Assert.That(scores[e.GetProperty("index").GetInt32()], Is.EqualTo(e.GetProperty("score").GetSingle()).Within(0.03), signal.Name + " " + e.GetProperty("label").GetString());
        }
    }

    [Test]
    public async Task SpeechIsFoundInEveryWindowOfTheSample()
    {
        string path = await ModelAsync();
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "yamnet_reference.json")));
        using var model = new YamNet(path);
        float[] speech = Speech();
        foreach (var window in reference.RootElement.GetProperty("speech_windows").EnumerateArray())
        {
            var top = model.Top(model.Classify(speech, window.GetProperty("start").GetInt32()), 1)[0];
            Assert.That(top.Label, Is.EqualTo(window.GetProperty("label").GetString()));
            Assert.That(top.Score, Is.EqualTo(window.GetProperty("score").GetSingle()).Within(0.03));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Classify(speech, speech.Length - 100));
        model.Dispose();
        Assert.Throws<ObjectDisposedException>(() => model.Classify(speech));
    }
}
