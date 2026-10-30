// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// SpeechToTextModel's helpers, without a model.
[TestFixture]
public class UnitTestSpeechToTextHelpers
{
    [Test]
    public void ResamplesLinearly()
    {
        float[] ramp = Enumerable.Range(0, 48).Select(i => i / 48f).ToArray();
        float[] down = SpeechToTextModel.Resample(ramp, 48000, 16000);
        Assert.That(down.Length, Is.EqualTo(16));
        Assert.That(down[1], Is.EqualTo(3 / 48f).Within(1e-6));
        float[] up = SpeechToTextModel.Resample(new[] { 0f, 1f }, 8000, 16000);
        Assert.That(up, Is.EqualTo(new[] { 0f, 0.5f, 1f, 1f }).Within(1e-6));
        Assert.That(SpeechToTextModel.Resample(ramp, 16000, 16000), Is.SameAs(ramp));
    }

    [Test]
    public void JoinsConfirmedPiecesWithOneSpace()
    {
        var text = new StringBuilder();
        SpeechToTextModel.Append(text, " Hello,");
        SpeechToTextModel.Append(text, "");
        SpeechToTextModel.Append(text, "world. ");
        SpeechToTextModel.Append(text, null);
        Assert.That(text.ToString(), Is.EqualTo("Hello, world."));
    }

    [Test]
    public void LowercasesAWindowsFirstWordInsideASentence()
    {
        // As the Levenshtein merger confirms Parakeet's windows: each starts with a capital.
        var text = new StringBuilder();
        foreach (string piece in new[] { "The service expects rain on Thursday morning,", "Clearing by the afternoon.",
            "On Friday, the", "City Library opens,", "With 40 desks", "I'm told", "Paris is next.", "So come." })
            SpeechToTextModel.Append(text, piece);
        Assert.That(text.ToString(), Is.EqualTo(
            "The service expects rain on Thursday morning, clearing by the afternoon. On Friday, the City Library opens, " +
            "with 40 desks I'm told paris is next. So come."));
    }
}

// Parakeet-TDT-0.6B-v3 through libasrextern (downloads 614 MB and its tokenizer on the first run). Ignored by default,
// like the other model tests: dotnet test -p:TestModels=true. Skipped where libasrextern isn't built.
[TestFixture, Category("Native")]
public class UnitTestSpeechToText
{
    private static ParakeetTdt06BV3 _model;

    private static async Task<ParakeetTdt06BV3> Model()
    {
        if (_model != null)
            return _model;
        var model = new ParakeetTdt06BV3();
        try
        {
            var watch = Stopwatch.StartNew();
            await model.Init();
            TestContext.Progress.WriteLine($"Parakeet loaded in {watch.Elapsed.TotalSeconds:F1} s");
        }
        catch (DllNotFoundException e)
        {
            model.Dispose();
            Assert.Ignore("libasrextern isn't available here: " + e.Message);
        }
        _model = model;
        return model;
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _model?.Dispose();
        _model = null;
    }

    // The 16-bit samples of a mono WAV file, as floats, and its sample rate.
    private static float[] ReadWav(string file, out int sampleRate)
    {
        byte[] wav = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, file));
        sampleRate = BitConverter.ToInt32(wav, 24);
        int position = 12;
        while (position + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, position, 4);
            int size = BitConverter.ToInt32(wav, position + 4);
            if (id == "data")
            {
                int count = Math.Min(size, wav.Length - position - 8) / 2;
                var samples = new float[count];
                for (int i = 0; i < count; i++)
                    samples[i] = BitConverter.ToInt16(wav, position + 8 + i * 2) / 32768f;
                return samples;
            }
            position += 8 + size + (size & 1);
        }
        throw new Exception("No data in " + file);
    }

    private static float[] At16k(string file)
    {
        float[] samples = ReadWav(file, out int rate);
        return SpeechToTextModel.Resample(samples, rate, 16000);
    }

    private static string Words(string text) =>
        new string(text.ToLowerInvariant().Where(c => char.IsLetter(c) || char.IsWhiteSpace(c)).ToArray());

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task TranscribesAShortClip()
    {
        ParakeetTdt06BV3 model = await Model();
        // 24 kHz in the file: resampled by TranscribeAsync.
        float[] samples = ReadWav("have_a_wonderful_day.wav", out int rate);
        var watch = Stopwatch.StartNew();
        string text = await model.TranscribeAsync(samples, rate);
        TestContext.Progress.WriteLine($"\"{text}\" in {watch.ElapsedMilliseconds} ms ({samples.Length / (double)rate:F1} s of audio)");
        Assert.That(Words(text), Does.Contain("wonderful day"));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task TranscribesAcrossWindows()
    {
        ParakeetTdt06BV3 model = await Model();
        // Longer than one 5 s window: the sample, a second of silence, then "have a wonderful day" - the windows'
        // overlapping texts must be merged without repeating words at the seams.
        float[] sample = At16k("audio_sample.wav");
        float[] day = At16k("have_a_wonderful_day.wav");
        float[] audio = sample.Concat(new float[16000]).Concat(day).ToArray();
        var partials = new List<string>();
        var watch = Stopwatch.StartNew();
        string text = await model.TranscribeAsync(audio, 16000, new Progress<string>(partials.Add));
        double seconds = audio.Length / 16000.0;
        TestContext.Progress.WriteLine($"{seconds:F1} s of audio in {watch.ElapsedMilliseconds} ms (real-time factor {watch.Elapsed.TotalSeconds / seconds:F3}): \"{text}\"");
        string sampleText = await model.TranscribeAsync(sample, 16000);
        TestContext.Progress.WriteLine($"The sample alone: \"{sampleText}\"");
        Assert.That(Words(sampleText).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.GreaterThan(3));
        Assert.That(Words(text), Does.EndWith("wonderful day"));
        // No word of the sample's transcript repeated at a seam: the joined transcript isn't much longer than the
        // two transcripts together.
        int words = Words(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int expected = Words(sampleText).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length + 4;
        Assert.That(words, Is.InRange(expected - 2, expected + 2));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task ASessionTakesAudioAsItComes()
    {
        ParakeetTdt06BV3 model = await Model();
        float[] audio = At16k("audio_sample.wav");
        string whole = await model.TranscribeAsync(audio, 16000);
        // Pushed in 100 ms pieces, processed whenever a window is ready, as from a microphone.
        var text = new StringBuilder();
        using (AsrSession session = model.CreateSession())
        {
            for (int offset = 0; offset < audio.Length; offset += 1600)
            {
                session.PushAudio(audio, offset, Math.Min(1600, audio.Length - offset));
                SpeechToTextModel.Append(text, SpeechToTextModel.ReadAll(session));
            }
            session.EndAudio();
            SpeechToTextModel.Append(text, SpeechToTextModel.ReadAll(session));
            // Reset: the same session recognizes new audio.
            session.Reset();
            session.PushAudio(At16k("have_a_wonderful_day.wav"));
            session.EndAudio();
            Assert.That(Words(SpeechToTextModel.ReadAll(session)), Does.Contain("wonderful day"));
        }
        Assert.That(text.ToString(), Is.EqualTo(whole));
    }
}
