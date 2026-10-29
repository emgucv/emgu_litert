// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Noise removal: a macOS voice (skipped elsewhere) with room noise at 15 dB SNR, cleaned by DTLN (CPU) and CMGAN (GPU,
// skipped without one); each must bring it nearer the clean speech. Downloads the models (4.2 MB, 4 MB).
[TestFixture, Category("Native")]
public class UnitTestSpeechDenoisers
{
    private static string Cache => Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "SpeechDenoisers");

    // The voice and the voice with noise, 16 kHz.
    private static (float[] Clean, float[] Noisy) Speech()
    {
        if (!File.Exists("/usr/bin/say"))
            Assert.Ignore("Needs macOS's say command for the voice.");
        string dir = Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "Denoise");
        Directory.CreateDirectory(dir);
        string aiff = Path.Combine(dir, "voice.aiff"), wav = Path.Combine(dir, "voice.wav");
        Run("/usr/bin/say", "-v", "Samantha", "-o", aiff, "The beta went well. We had about two hundred testers and the crash rate was under one percent.");
        Run("/usr/bin/afconvert", "-f", "WAVE", "-d", "LEI16@16000", "-c", "1", aiff, wav);
        byte[] bytes = File.ReadAllBytes(wav);
        int data = 12;
        while (!(bytes[data] == 'd' && bytes[data + 1] == 'a' && bytes[data + 2] == 't' && bytes[data + 3] == 'a'))
            data += 8 + BitConverter.ToInt32(bytes, data + 4);
        int count = BitConverter.ToInt32(bytes, data + 4) / 2;
        var clean = new float[count];
        for (int k = 0; k < count; k++) clean[k] = BitConverter.ToInt16(bytes, data + 8 + 2 * k) / 32768f;
        // Room noise: low-passed white noise, scaled to 15 dB under the speech.
        var random = new Random(3);
        var noise = new float[count];
        double last = 0;
        for (int k = 0; k < count; k++) noise[k] = (float)(last = 0.7 * last + 0.3 * (random.NextDouble() - 0.5));
        double speech = Math.Sqrt(clean.Average(x => (double)x * x)), room = Math.Sqrt(noise.Average(x => (double)x * x));
        double gain = speech / room / Math.Pow(10, 15 / 20.0);
        return (clean, clean.Select((x, k) => (float)(x + noise[k] * gain)).ToArray());
    }

    private static void Run(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (string a in arguments) start.ArgumentList.Add(a);
        using Process process = Process.Start(start);
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.EqualTo(0), file);
    }

    // Scale-invariant signal-to-noise ratio of an estimate against the reference, in dB.
    private static double SiSnr(float[] estimate, float[] reference)
    {
        int n = Math.Min(estimate.Length, reference.Length);
        double em = estimate.Take(n).Average(), rm = reference.Take(n).Average(), dot = 0, rr = 0;
        for (int i = 0; i < n; i++) { dot += (estimate[i] - em) * (reference[i] - rm); rr += (reference[i] - rm) * (reference[i] - rm); }
        double a = dot / rr, target = 0, error = 0;
        for (int i = 0; i < n; i++)
        {
            double t = a * (reference[i] - rm), e = estimate[i] - em - t;
            target += t * t; error += e * e;
        }
        return 10 * Math.Log10(target / error);
    }

    [Test]
    public async Task DtlnRemovesNoise()
    {
        (float[] clean, float[] noisy) = Speech();
        (string mask, string block) = await DtlnDenoiser.DownloadAsync(Cache);
        using var dtln = new DtlnDenoiser(mask, block);
        var watch = Stopwatch.StartNew();
        float[] cleaned = dtln.Denoise(noisy);
        double seconds = watch.Elapsed.TotalSeconds;
        Assert.That(cleaned.Length, Is.EqualTo(noisy.Length));
        double before = SiSnr(noisy, clean), after = SiSnr(cleaned, clean);
        TestContext.WriteLine($"DTLN: SI-SNR {before:F1} -> {after:F1} dB, {noisy.Length / 16000.0 / seconds:F1}x real time");
        Assert.That(after, Is.GreaterThan(before + 3));

        // As the audio arrives, in uneven pieces: the same samples, Latency behind.
        dtln.Reset();
        var streamed = new System.Collections.Generic.List<float>();
        var random = new Random(1);
        for (int at = 0; at < noisy.Length;)
        {
            int take = Math.Min(random.Next(1, 700), noisy.Length - at);
            streamed.AddRange(dtln.Process(noisy.Skip(at).Take(take).ToArray()));
            at += take;
        }
        Assert.That(streamed.Count % DtlnDenoiser.ShiftSamples, Is.EqualTo(0));
        for (int i = DtlnDenoiser.Latency; i < streamed.Count; i += 997)
            Assert.That(streamed[i], Is.EqualTo(cleaned[i - DtlnDenoiser.Latency]).Within(1e-5));
    }

    [Test]
    public async Task CmganRemovesNoise()
    {
        (float[] clean, float[] noisy) = Speech();
        string model = await CmganDenoiser.DownloadAsync(Cache);
        CmganDenoiser cmgan;
        try { cmgan = new CmganDenoiser(model); }
        catch (Exception ex) { Assert.Ignore("CMGAN needs a GPU accelerator: " + ex.Message); return; }
        using (cmgan)
        {
            var watch = Stopwatch.StartNew();
            float[] cleaned = cmgan.Denoise(noisy);
            double seconds = watch.Elapsed.TotalSeconds;
            Assert.That(cleaned.Length, Is.EqualTo(noisy.Length));
            double before = SiSnr(noisy, clean), after = SiSnr(cleaned, clean);
            TestContext.WriteLine($"CMGAN: SI-SNR {before:F1} -> {after:F1} dB, {noisy.Length / 16000.0 / seconds:F1}x real time, whole graph on the GPU {cmgan.IsFullyAccelerated}");
            Assert.That(after, Is.GreaterThan(before + 3));
            Assert.That(cmgan.DenoiseChunk(new float[CmganDenoiser.ChunkSamples]).All(x => x == 0), Is.True, "silence stays silent");
        }
    }
}
