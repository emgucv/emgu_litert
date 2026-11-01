// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Kokoro82M's helpers and checks before anything is loaded.
[TestFixture]
public class UnitTestTextToSpeechHelpers
{
    [Test]
    public void TellsAVoicesLanguageAndName()
    {
        Assert.That(Kokoro82M.VoiceLanguage("af_heart"), Is.EqualTo("en-US"));
        Assert.That(Kokoro82M.VoiceLanguage("bm_george"), Is.EqualTo("en-GB"));
        Assert.That(Kokoro82M.VoiceLanguage("zf_xiaobei"), Is.EqualTo("zh-CN"));
        Assert.That(Kokoro82M.VoiceLanguage("pf_dora"), Is.EqualTo("pt-BR"));
        Assert.That(Kokoro82M.VoiceLanguage("x"), Is.Null);
        Assert.That(Kokoro82M.DescribeVoice("af_heart"), Is.EqualTo("Heart (female)"));
        Assert.That(Kokoro82M.DescribeVoice("jm_kumo"), Is.EqualTo("Kumo (male)"));
    }

    [Test]
    public void TheTwoFilesAreDifferentDownloadsWithTheirOwnCaches()
    {
        using var gpu = new Kokoro82M();
        using var cpu = new Kokoro82M(gpuFile: false);
        Assert.That(gpu.ModelFile.LocalFile, Is.Not.EqualTo(cpu.ModelFile.LocalFile));
        Assert.That(gpu.CacheFolderName, Is.Not.EqualTo(cpu.CacheFolderName));
        Assert.That(gpu.ModelName, Is.EqualTo("kokoro"));
        Assert.That(gpu.DownloadSize, Is.GreaterThan(290_000_000L));
    }

    [Test]
    public void RefusesBeforeLoading()
    {
        using var model = new Kokoro82M();
        Assert.ThrowsAsync<ArgumentException>(() => model.SynthesizeAsync(" "));
        Assert.ThrowsAsync<InvalidOperationException>(() => model.SynthesizeAsync("Hello."));
        Assert.ThrowsAsync<InvalidOperationException>(() => model.SetVoiceAsync("en-US", "af_heart"));
    }
}

// Kokoro 82M through libomniextern (downloads its 295 MB .litertlm on the first run). Ignored by default, like the other
// model tests: dotnet test -p:TestModels=true. Skipped where libomniextern isn't built.
[TestFixture, Category("Native")]
public class UnitTestTextToSpeech
{
    private static Kokoro82M _model;

    // The GPU file where a GPU plugin is next to libLiteRt (it is noisier on the CPU), else the CPU file.
    private static async Task<Kokoro82M> Model()
    {
        if (_model != null)
            return _model;
        bool gpu = UnitTest1.StageGpuPlugin();
        var model = new Kokoro82M(gpuFile: gpu);
        try
        {
            var watch = Stopwatch.StartNew();
            await model.Init(gpu ? "gpu" : "cpu", 0, "en-US", "af_heart");
            TestContext.Progress.WriteLine($"Kokoro loaded on the {model.Backend} in {watch.Elapsed.TotalSeconds:F1} s");
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

    private static double Rms(float[] samples) => Math.Sqrt(samples.Average(s => (double)s * s));

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task SpeaksEachLanguage()
    {
        Kokoro82M model = await Model();
        var sentences = new (string Language, string Text)[]
        {
            ("en-US", "The city library opens a new reading room next week."),
            ("en-GB", "The city library opens a new reading room next week."),
            ("es", "La biblioteca abre una nueva sala de lectura la próxima semana."),
            ("fr", "La bibliothèque ouvre une nouvelle salle de lecture la semaine prochaine."),
            ("it", "La biblioteca apre una nuova sala di lettura la prossima settimana."),
            ("pt-BR", "A biblioteca abre uma nova sala de leitura na próxima semana."),
            ("hi", "पुस्तकालय अगले सप्ताह एक नया वाचनालय खोलेगा।"),
            ("zh", "图书馆下周将开放一个新的阅览室。"),
            ("ja", "図書館は来週新しい閲覧室を開きます。"),
        };
        foreach (var (language, text) in sentences)
        {
            await model.SetVoiceAsync(language, null);
            var watch = Stopwatch.StartNew();
            TextToSpeechAudio audio = await model.SynthesizeAsync(text);
            double rms = Rms(audio.Samples);
            TestContext.Progress.WriteLine($"{language}: {audio.Duration.TotalSeconds:F2} s of audio in {watch.Elapsed.TotalSeconds:F2} s, RMS {rms:F3}");
            Assert.That(audio.SampleRate, Is.EqualTo(24000));
            Assert.That(audio.Duration.TotalSeconds, Is.InRange(1.5, 10.0), language);
            Assert.That(rms, Is.GreaterThan(0.01), language + " is silent");
            Assert.That(audio.Samples.All(s => s >= -1.001f && s <= 1.001f), language);
        }
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task ListsItsVoicesAndLanguages()
    {
        Kokoro82M model = await Model();
        string[] languages = model.Languages;
        TestContext.Progress.WriteLine("Languages: " + string.Join(", ", languages));
        string[] all = model.GetVoices();
        TestContext.Progress.WriteLine($"{all.Length} voices: " + string.Join(", ", all));
        Assert.That(all.Length, Is.GreaterThanOrEqualTo(50));
        string[] english = model.GetVoices("en-US");
        Assert.That(english, Does.Contain("af_heart"));
        Assert.That(english.All(v => Kokoro82M.VoiceLanguage(v) == "en-US"), string.Join(", ", english));
        Assert.That(model.GetDefaultVoice("en-US"), Is.EqualTo("af_heart"));
        Assert.That(Kokoro82M.VoiceLanguage(model.GetDefaultVoice("fr")), Is.EqualTo("fr"));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task RefusesAVoiceOfAnotherLanguage()
    {
        Kokoro82M model = await Model();
        var error = Assert.ThrowsAsync<LiteRtLmException>(() => model.SetVoiceAsync("en-US", "bf_emma"));
        Assert.That(error.Status, Is.EqualTo(LiteRtLmStatusCode.InvalidArgument));
        TestContext.Progress.WriteLine(error.ErrorMessage);
        // The engine before it is still there.
        TextToSpeechAudio audio = await model.SynthesizeAsync("Still here.");
        Assert.That(audio.Samples.Length, Is.GreaterThan(0));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task SentencesTogetherTakeLessThanOneByOne()
    {
        // Every acoustic run costs about the same, and the engine cuts only at line breaks: three short sentences in one
        // text are one run, not three.
        Kokoro82M model = await Model();
        await model.SetVoiceAsync("en-US", "af_heart");
        string[] sentences = { "It has forty desks.", "The room is quiet.", "Children are welcome." };
        await model.SynthesizeAsync("Warm up.");
        var watch = Stopwatch.StartNew();
        TextToSpeechAudio together = await model.SynthesizeAsync(string.Join(" ", sentences));
        double togetherSeconds = watch.Elapsed.TotalSeconds;
        watch.Restart();
        double audioOneByOne = 0;
        foreach (string s in sentences)
            audioOneByOne += (await model.SynthesizeAsync(s)).Duration.TotalSeconds;
        double oneByOneSeconds = watch.Elapsed.TotalSeconds;
        TestContext.Progress.WriteLine($"Together: {together.Duration.TotalSeconds:F2} s of audio in {togetherSeconds:F2} s; one by one: {audioOneByOne:F2} s in {oneByOneSeconds:F2} s");
        Assert.That(togetherSeconds, Is.LessThan(oneByOneSeconds * 0.8));
        Assert.That(together.Duration.TotalSeconds, Is.GreaterThan(audioOneByOne * 0.7));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task ParakeetHearsWhatKokoroSays()
    {
        Kokoro82M model = await Model();
        await model.SetVoiceAsync("en-US", "af_heart");
        TextToSpeechAudio audio = await model.SynthesizeAsync("Have a wonderful day. The library opens at nine tomorrow morning.");
        using var parakeet = new ParakeetTdt06BV3();
        try
        {
            await parakeet.Init();
        }
        catch (DllNotFoundException e)
        {
            Assert.Ignore("libomniextern isn't available here: " + e.Message);
        }
        string text = await parakeet.TranscribeAsync(audio.Samples, audio.SampleRate);
        TestContext.Progress.WriteLine($"Parakeet heard: \"{text}\"");
        string words = new string(text.ToLowerInvariant().Where(c => char.IsLetter(c) || char.IsWhiteSpace(c)).ToArray());
        Assert.That(words, Does.Contain("wonderful day"));
        Assert.That(words, Does.Contain("library"));
        Assert.That(words, Does.Contain("tomorrow morning"));
    }
}
