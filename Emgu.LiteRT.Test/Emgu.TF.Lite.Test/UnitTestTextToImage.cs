// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// TextToImageModel's checks before anything is loaded.
[TestFixture]
public class UnitTestTextToImageArguments
{
    [Test]
    public void RefusesBadArgumentsBeforeLoading()
    {
        using var model = new BonsaiImage4B();
        Assert.ThrowsAsync<ArgumentException>(() => model.GenerateAsync(" ", 1));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => model.GenerateAsync("a fox", 0));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => model.GenerateAsync("a fox", 1, -1));
        Assert.ThrowsAsync<InvalidOperationException>(() => model.GenerateAsync("a fox", 1));
    }

    [Test]
    public void DescribesTheModels()
    {
        using var bonsai = new BonsaiImage4B();
        using var bonsai512 = new BonsaiImage4B512();
        using var klein = new Flux2Klein4B();
        Assert.That(bonsai.ModelName, Is.EqualTo("bonsai-flux2"));
        Assert.That(klein.ModelName, Is.EqualTo("flux2-klein"));
        Assert.That((bonsai.ImageSize, bonsai512.ImageSize, klein.ImageSize), Is.EqualTo((256, 512, 256)));
        // The 512 file has the same registered name as the 256 one: its caches go in a folder of their own.
        Assert.That(bonsai512.ModelName, Is.EqualTo(bonsai.ModelName));
        Assert.That(bonsai512.CacheFolderName, Is.Not.EqualTo(bonsai.CacheFolderName));
        Assert.That(bonsai512.ModelFile.LocalFile, Is.Not.EqualTo(bonsai.ModelFile.LocalFile));
        foreach (TextToImageModel model in new TextToImageModel[] { bonsai, bonsai512, klein })
        {
            Assert.That(model.ImageSize % 16, Is.EqualTo(0));
            Assert.That(model.DefaultSteps, Is.EqualTo(4));
            Assert.That(model.ModelFile.Url, Does.EndWith(".litertlm"));
            Assert.That(model.DownloadSize, Is.GreaterThan(4_000_000_000L));
        }
    }
}

// Bonsai Image 4B and FLUX.2-klein 4B through libomniextern (each downloads a 4.3-4.7 GB .litertlm on the first
// run). Ignored by default, like the other model tests: dotnet test -p:TestModels=true. Skipped where libomniextern
// isn't built.
[TestFixture, Category("Native")]
public class UnitTestTextToImage
{
    private const string Prompt = "a red fox sitting in fresh snow at sunrise";

    private static async Task<T> Load<T>(string backend = "cpu") where T : TextToImageModel, new()
    {
        var model = new T();
        try
        {
            var watch = Stopwatch.StartNew();
            await model.Init(backend);
            TestContext.Progress.WriteLine($"{model.DisplayName} loaded on the {backend} in {watch.Elapsed.TotalSeconds:F1} s");
        }
        catch (DllNotFoundException e)
        {
            model.Dispose();
            Assert.Ignore("libomniextern isn't available here: " + e.Message);
        }
        return model;
    }

    // A picture of the right size that isn't flat, saved next to the tests as a PNG to look at.
    private static void CheckPicture(TextToImageModel model, TextToImageResult picture, string name)
    {
        Assert.That(picture.Width, Is.EqualTo(model.ImageSize));
        Assert.That(picture.Height, Is.EqualTo(model.ImageSize));
        Assert.That(picture.Channels, Is.EqualTo(3));
        Assert.That(picture.Rgb.Length, Is.EqualTo(picture.Width * picture.Height * 3));
        double mean = picture.Rgb.Average(b => (double)b);
        double variance = picture.Rgb.Average(b => (b - mean) * (b - mean));
        TestContext.Progress.WriteLine($"{name}: mean {mean:F1}, standard deviation {Math.Sqrt(variance):F1}");
        Assert.That(Math.Sqrt(variance), Is.GreaterThan(10), "a flat picture");
        string file = Path.Combine(AppContext.BaseDirectory, name + ".png");
        File.WriteAllBytes(file, Emgu.LiteRT.Models.PngEncoder.Encode(picture.Rgb, picture.Width, picture.Height));
        TestContext.Progress.WriteLine("Saved " + file);
    }

    private static async Task DrawsTheSamePictureForTheSameSeed<T>() where T : TextToImageModel, new()
    {
        using T model = await Load<T>();
        var watch = Stopwatch.StartNew();
        TextToImageResult first = await model.GenerateAsync(Prompt, 42);
        TestContext.Progress.WriteLine($"First picture in {watch.Elapsed.TotalSeconds:F1} s");
        CheckPicture(model, first, $"{model.ModelName}_{model.ImageSize}_fox");
        watch.Restart();
        TextToImageResult again = await model.GenerateAsync(Prompt, 42);
        TestContext.Progress.WriteLine($"Again in {watch.Elapsed.TotalSeconds:F1} s");
        Assert.That(again.Rgb, Is.EqualTo(first.Rgb), "the same seed gave another picture");
        TextToImageResult other = await model.GenerateAsync(Prompt, 7);
        Assert.That(other.Rgb, Is.Not.EqualTo(first.Rgb), "another seed gave the same picture");
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public Task BonsaiDrawsTheSamePictureForTheSameSeed() => DrawsTheSamePictureForTheSameSeed<BonsaiImage4B>();

// Each model on the GPU (the demo's "· GPU" choices): skipped without a LiteRT GPU accelerator plugin next to
// libLiteRt, like the other GPU tests (on Windows: EMGU_LITERT_WEBGPU_DIR names the bundle bazel_build_windows_gpu.ps1 made).
// Draws the CPU's picture for the same seed too, and compares them: the GPU stages run in FP32 and the pictures may differ in details.
#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public Task BonsaiDrawsOnTheGpu() => DrawsOnTheGpu<BonsaiImage4B>();

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public Task Flux2KleinDrawsOnTheGpu() => DrawsOnTheGpu<Flux2Klein4B>();

    private static async Task DrawsOnTheGpu<T>() where T : TextToImageModel, new()
    {
        if (!UnitTest1.StageGpuPlugin())
            Assert.Ignore("No LiteRT GPU accelerator plugin next to libLiteRt (on Windows: set EMGU_LITERT_WEBGPU_DIR)");
        TextToImageResult gpuPicture;
        using (T gpu = await Load<T>("gpu"))
        {
            var watch = Stopwatch.StartNew();
            gpuPicture = await gpu.GenerateAsync(Prompt, 42);
            TestContext.Progress.WriteLine($"First GPU picture in {watch.Elapsed.TotalSeconds:F1} s");
            CheckPicture(gpu, gpuPicture, $"{gpu.ModelName}_gpu_{gpu.ImageSize}_fox");
            watch.Restart();
            TextToImageResult again = await gpu.GenerateAsync(Prompt, 42);
            TestContext.Progress.WriteLine($"Second GPU picture in {watch.Elapsed.TotalSeconds:F1} s");
            Assert.That(again.Rgb, Is.EqualTo(gpuPicture.Rgb), "the same seed gave another picture on the GPU");
        }
        using (T cpu = await Load<T>("cpu"))
        {
            var watch = Stopwatch.StartNew();
            TextToImageResult cpuPicture = await cpu.GenerateAsync(Prompt, 42);
            TestContext.Progress.WriteLine($"CPU picture in {watch.Elapsed.TotalSeconds:F1} s");
            double squared = 0;
            for (int i = 0; i < cpuPicture.Rgb.Length; i++)
            {
                double d = cpuPicture.Rgb[i] - gpuPicture.Rgb[i];
                squared += d * d;
            }
            double psnr = 10 * Math.Log10(255.0 * 255.0 / Math.Max(squared / cpuPicture.Rgb.Length, 1e-9));
            TestContext.Progress.WriteLine($"GPU against CPU, same seed: PSNR {psnr:F1} dB");
            Assert.That(psnr, Is.GreaterThan(15), "the GPU's picture is nothing like the CPU's");
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
    public Task Flux2KleinDrawsTheSamePictureForTheSameSeed() => DrawsTheSamePictureForTheSameSeed<Flux2Klein4B>();

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public Task Bonsai512DrawsTheSamePictureForTheSameSeed() => DrawsTheSamePictureForTheSameSeed<BonsaiImage4B512>();

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public async Task ACancelledPictureLeavesTheModelUsable()
    {
        using BonsaiImage4B model = await Load<BonsaiImage4B>();
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            Assert.CatchAsync<OperationCanceledException>(() => model.GenerateAsync(Prompt, 3, 0, cancel.Token));
        // The next picture waits for the abandoned one, then is drawn.
        TextToImageResult picture = await model.GenerateAsync(Prompt, 3, 1);
        Assert.That(picture.Width, Is.EqualTo(model.ImageSize));
    }

#if !TEST_MODELS
#if VS_TEST
    [Ignore()]
#else
    [Ignore("Ignore from test run by default.")]
#endif
#endif
    [Test]
    public void AnEngineRefusesASizeThatIsntAMultipleOf16()
    {
        string file = BonsaiImage4B.DefaultModelFile.LocalFile;
        if (!File.Exists(file))
            Assert.Ignore("The Bonsai model isn't downloaded.");
        try
        {
            var error = Assert.Throws<LiteRtLmException>(() => new TextToImageEngine(BonsaiImage4B.RegisteredName, file, "cpu", 0, 500));
            Assert.That(error.Status, Is.EqualTo(LiteRtLmStatusCode.InvalidArgument));
            Assert.That(error.ErrorMessage, Does.Contain("16"));
        }
        catch (DllNotFoundException e)
        {
            Assert.Ignore("libomniextern isn't available here: " + e.Message);
        }
    }
}
