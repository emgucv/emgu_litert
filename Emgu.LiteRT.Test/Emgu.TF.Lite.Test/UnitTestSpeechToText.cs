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

    [Test]
    public void JoinsChineseAndJapanesePiecesWithoutSpaces()
    {
        // Qwen3-ASR's windows in Chinese and Japanese: no space at a seam next to Han, kana or their punctuation;
        // Korean and Latin text still get one.
        var text = new StringBuilder();
        foreach (string piece in new[] { "大家下午好，今天我们来讨论", "社区花园的新计划：", "市政府同意" })
            SpeechToTextModel.Append(text, piece);
        Assert.That(text.ToString(), Is.EqualTo("大家下午好，今天我们来讨论社区花园的新计划：市政府同意"));
        text.Clear();
        foreach (string piece in new[] { "皆さんこんにちは。", "今日は新しい", "コミュニティガーデン" })
            SpeechToTextModel.Append(text, piece);
        Assert.That(text.ToString(), Is.EqualTo("皆さんこんにちは。今日は新しいコミュニティガーデン"));
        text.Clear();
        foreach (string piece in new[] { "내일 아침에", "토마토에 물" })
            SpeechToTextModel.Append(text, piece);
        Assert.That(text.ToString(), Is.EqualTo("내일 아침에 토마토에 물"));
        text.Clear();
        foreach (string piece in new[] { "我们用", "LiteRT", "来测试" })
            SpeechToTextModel.Append(text, piece);
        Assert.That(text.ToString(), Is.EqualTo("我们用LiteRT来测试"));
    }
}

// Qwen3-ASR-0.6B through libomniextern (downloads its 960 MB .litertlm on the first run). Ignored by default, like the
// other model tests: dotnet test -p:TestModels=true. The Chinese and Japanese speech is made with macOS's say command
// (skipped elsewhere, or without the voice).
[TestFixture, Category("Native")]
public class UnitTestQwen3Asr
{
    private static Qwen3Asr06B _model;

    private static async Task<Qwen3Asr06B> Model()
    {
        if (_model != null)
            return _model;
        var model = new Qwen3Asr06B();
        try
        {
            var watch = Stopwatch.StartNew();
            await model.Init();
            TestContext.Progress.WriteLine($"Qwen3-ASR loaded in {watch.Elapsed.TotalSeconds:F1} s");
        }
        catch (DllNotFoundException e)
        {
            model.Dispose();
            Assert.Ignore("libomniextern isn't available here: " + e.Message);
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

    // Speech from a macOS voice, as 16 kHz samples.
    private static float[] Say(string voice, string text)
    {
        if (!File.Exists("/usr/bin/say"))
            Assert.Ignore("Needs macOS's say command for the voices.");
        string dir = Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "Qwen3Asr");
        Directory.CreateDirectory(dir);
        string wav = Path.Combine(dir, voice + "_" + (uint)text.GetHashCode() + ".wav");
        var start = new ProcessStartInfo("/usr/bin/say") { UseShellExecute = false, RedirectStandardError = true };
        foreach (string a in new[] { "-v", voice, "-o", wav, "--data-format=LEI16@16000", text })
            start.ArgumentList.Add(a);
        using (Process process = Process.Start(start))
        {
            process.WaitForExit();
            if (process.ExitCode != 0 || !File.Exists(wav))
                Assert.Ignore("The macOS voice " + voice + " isn't installed.");
        }
        byte[] bytes = File.ReadAllBytes(wav);
        int data = 12;
        while (Encoding.ASCII.GetString(bytes, data, 4) != "data")
            data += 8 + BitConverter.ToInt32(bytes, data + 4);
        int count = BitConverter.ToInt32(bytes, data + 4) / 2;
        var samples = new float[count];
        for (int k = 0; k < count; k++)
            samples[k] = BitConverter.ToInt16(bytes, data + 8 + 2 * k) / 32768f;
        return samples;
    }

    private static async Task<string> Transcribe(float[] samples, int rate = 16000)
    {
        Qwen3Asr06B model = await Model();
        var watch = Stopwatch.StartNew();
        string text = await model.TranscribeAsync(samples, rate);
        TestContext.Progress.WriteLine($"\"{text}\" in {watch.ElapsedMilliseconds} ms ({samples.Length / (double)rate:F1} s of audio)");
        return text;
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task TranscribesEnglish()
    {
        byte[] wav = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "have_a_wonderful_day.wav"));
        int rate = BitConverter.ToInt32(wav, 24);
        int data = 12;
        while (Encoding.ASCII.GetString(wav, data, 4) != "data")
            data += 8 + BitConverter.ToInt32(wav, data + 4);
        int count = Math.Min(BitConverter.ToInt32(wav, data + 4), wav.Length - data - 8) / 2;
        float[] samples = Enumerable.Range(0, count).Select(k => BitConverter.ToInt16(wav, data + 8 + 2 * k) / 32768f).ToArray();
        // 24 kHz in the file: resampled by TranscribeAsync.
        string text = await Transcribe(samples, rate);
        Assert.That(text.ToLowerInvariant(), Does.Contain("wonderful day"));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task TranscribesChineseAndJapanese()
    {
        string chinese = await Transcribe(Say("Tingting", "请提醒我明天早上给西红柿浇水。"));
        Assert.That(chinese, Does.Contain("明天早上").And.Contain("西红柿"));
        string japanese = await Transcribe(Say("Kyoko", "明日の朝、トマトに水をやるのを思い出させてください。"));
        Assert.That(japanese, Does.Contain("明日の朝").And.Contain("トマト"));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task TranscribesChineseAcrossWindows()
    {
        // About 24 s, five windows: the pieces join without spaces, and the phrases at both ends are there.
        const string script = "大家下午好。今天我们来讨论社区花园的新计划。市政府同意把图书馆后面的空地借给我们使用三年。"
            + "我们需要志愿者来搭建十二个种植箱，安装一个水龙头，并为大门画一块招牌。第一次劳动日定在下周六上午九点，午饭由榆树街的面包店提供。";
        string text = await Transcribe(Say("Tingting", script));
        Assert.That(text, Does.Not.Contain(" "));
        Assert.That(text, Does.StartWith("大家下午好").And.Contain("社区花园").And.Contain("面包店"));
        // No phrase repeated at a seam: about as many characters as the script (punctuation may differ).
        Assert.That(text.Length, Is.InRange(script.Length - 8, script.Length + 8));
    }

    // On the GPU (Metal on a Mac; OpenCL on Android), a model of its own.
#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task TranscribesOnTheGpu()
    {
        float[] samples = Say("Kyoko", "明日の朝、トマトに水をやるのを思い出させてください。");
        using (var model = new Qwen3Asr06B())
        {
            var watch = Stopwatch.StartNew();
            try { await model.Init("gpu"); }
            catch (DllNotFoundException e) { Assert.Ignore("libomniextern isn't available here: " + e.Message); }
            TestContext.Progress.WriteLine($"Qwen3-ASR on the GPU loaded in {watch.Elapsed.TotalSeconds:F1} s");
            watch.Restart();
            string text = await model.TranscribeAsync(samples, 16000);
            TestContext.Progress.WriteLine($"\"{text}\" in {watch.ElapsedMilliseconds} ms");
            Assert.That(text, Does.Contain("明日の朝").And.Contain("トマト"));
        }
    }
}

// Parakeet-TDT-0.6B-v3 through libomniextern (downloads its 615 MB .litertlm on the first run). Ignored by default,
// like the other model tests: dotnet test -p:TestModels=true. Skipped where libomniextern isn't built.
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
            Assert.Ignore("libomniextern isn't available here: " + e.Message);
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
        using (SpeechToTextSession session = model.CreateSession())
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
