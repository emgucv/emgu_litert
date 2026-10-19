// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestPicoDet
{
    private static readonly int[] Cells = { 2704, 676, 169, 49 };

    // Level scores of 0, and distributions whose softmax is exactly one bin (a large logit there).
    private static (float[][] Scores, float[][] Distributions) Empty()
    {
        var scores = Cells.Select(n => new float[n * PicoDet.ClassCount]).ToArray();
        var distributions = Cells.Select(n => new float[n * 4 * PicoDet.Bins]).ToArray();
        return (scores, distributions);
    }

    private static void Cell(float[][] scores, float[][] distributions, int level, int cell, int label, float score, int[] bins)
    {
        scores[level][cell * PicoDet.ClassCount + label] = score;
        for (int side = 0; side < 4; side++)
        {
            for (int b = 0; b < PicoDet.Bins; b++) distributions[level][cell * 32 + side * 8 + b] = b == bins[side] ? 40f : 0f;
        }
    }

    [Test]
    public void DecodesDistancesFromTheCellCenterAndScalesToTheImage()
    {
        var (scores, distributions) = Empty();
        // Stride 8, cell (x 10, y 20) of the 52-cell grid: centre (84, 164); distances 2, 3, 4, 5 strides.
        Cell(scores, distributions, 0, 20 * 52 + 10, 0, 0.9f, new[] { 2, 3, 4, 5 });
        var result = PicoDet.Decode(scores, distributions);
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Label, Is.EqualTo("person"));
        Assert.That(result[0].Score, Is.EqualTo(0.9f).Within(1e-6));
        Assert.That(result[0].Rectangle, Is.EqualTo(new[] { (84 - 16) / 416f, (164 - 24) / 416f, (84 + 32) / 416f, (164 + 40) / 416f }).Within(1e-5));
    }

    [Test]
    public void UsesTheStrideAndGridOfEachLevel()
    {
        var (scores, distributions) = Empty();
        Cell(scores, distributions, 1, 3 * 26 + 5, 16, 0.8f, new[] { 1, 1, 1, 1 });   // stride 16: centre (88, 56)
        Cell(scores, distributions, 3, 2 * 7 + 6, 2, 0.7f, new[] { 1, 1, 1, 1 });     // stride 64 on a 7 x 7 grid: (416, 160)
        var result = PicoDet.Decode(scores, distributions);
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Label, Is.EqualTo("dog"));
        Assert.That(result[0].Rectangle, Is.EqualTo(new[] { 72 / 416f, 40 / 416f, 104 / 416f, 72 / 416f }).Within(1e-5));
        Assert.That(result[1].Label, Is.EqualTo("car"));
        // Centre x = 6.5 * 64 = 416, so the box runs past the right edge and is clipped.
        Assert.That(result[1].Rectangle, Is.EqualTo(new[] { 352 / 416f, 96 / 416f, 1f, 224 / 416f }).Within(1e-5));
    }

    [Test]
    public void SuppressesOverlapsPerClassOnly()
    {
        var (scores, distributions) = Empty();
        var bins = new[] { 3, 3, 3, 3 };
        Cell(scores, distributions, 0, 10 * 52 + 10, 0, 0.9f, bins);
        Cell(scores, distributions, 0, 10 * 52 + 11, 0, 0.8f, bins);   // the same class, a neighbouring cell: suppressed
        Cell(scores, distributions, 0, 10 * 52 + 12, 1, 0.7f, bins);   // another class over the same area: kept
        var result = PicoDet.Decode(scores, distributions);
        Assert.That(result.Select(r => r.Label), Is.EqualTo(new[] { "person", "bicycle" }));
        Assert.That(PicoDet.Decode(scores, distributions, 0.5f, 0.6f, 1).Length, Is.EqualTo(1));
    }

    [Test]
    public void KeepsOnlyScoresAboveTheThresholdAndDropsInvalidBoxes()
    {
        var (scores, distributions) = Empty();
        Cell(scores, distributions, 0, 0, 0, 0.4f, new[] { 1, 1, 1, 1 });      // below 0.5
        Cell(scores, distributions, 0, 100, 0, 0.6f, new[] { 1, 1, 1, 1 });
        Cell(scores, distributions, 0, 200, 0, float.NaN, new[] { 1, 1, 1, 1 });
        Cell(scores, distributions, 0, 300, 0, 0.9f, new[] { 0, 0, 0, 0 });    // zero distances: empty after +0
        Assert.That(PicoDet.Decode(scores, distributions).Length, Is.EqualTo(1));
        Assert.That(PicoDet.Decode(scores, distributions, 0.3f).Length, Is.EqualTo(2));
        distributions[0][100 * 32] = float.NaN;
        Assert.DoesNotThrow(() => PicoDet.Decode(scores, distributions));
    }

    [Test]
    public void RejectsWrongSizesAndThresholds()
    {
        var (scores, distributions) = Empty();
        Assert.Throws<ArgumentException>(() => PicoDet.Decode(scores.Take(3).ToArray(), distributions));
        Assert.Throws<ArgumentException>(() => PicoDet.Decode(scores, distributions.Select(d => d.Take(10).ToArray()).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => PicoDet.Decode(scores, distributions, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PicoDet.Decode(scores, distributions, 0.5f, 0.6f, 0));
    }
}

[TestFixture, Category("Native")]
public class UnitTestPicoDetNative
{
    [TestCase(Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase(Emgu.LiteRT.HwAccelerators.Gpu, Explicit = true)]
    public async Task CompiledDetectorMatchesOnnxRuntimeOnBundledImages(Emgu.LiteRT.HwAccelerators accelerators)
    {
        string root = AppContext.BaseDirectory;
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "picodet_reference.json")));
        string sha = reference.RootElement.GetProperty("tflite_sha256").GetString();
        // PICODET_MODEL: a local converted model, for before it is hosted or when offline.
        string path = System.Environment.GetEnvironmentVariable("PICODET_MODEL");
        if (string.IsNullOrEmpty(path))
        {
            var weights = new DownloadableFile(
                "https://emgu-public.s3.amazonaws.com/picodet/picodet_s_416_coco_float32.tflite",
                Path.Combine("Emgu.LiteRT.Tests", "PicoDet"), sha);
            var downloads = new FileDownloadManager();
            downloads.AddFile(weights);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await downloads.Download(1, timeout.Token);
            Assert.That(downloads.AllFilesDownloaded, Is.True, "PicoDet test model download/checksum failed.");
            path = weights.LocalFile;
        }
        using (var stream = File.OpenRead(path))
            Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant(), Is.EqualTo(sha));
        using var model = new PicoDet(path, 2, accelerators, Emgu.LiteRT.GpuPrecision.Fp32);
        if (accelerators == Emgu.LiteRT.HwAccelerators.Gpu)
            Assert.That(model.IsFullyAccelerated, Is.True, "GPU-only graph must be fully accelerated.");
        foreach (var sample in reference.RootElement.GetProperty("samples").EnumerateArray())
        {
            string name = sample.GetProperty("image").GetString();
            using var image = Emgu.CV.CvInvoke.Imread(Path.Combine(root, name), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
            Maui.Demo.Lite.PicoDetImageInput.Write(model, image);
            var actual = model.Run().ToList();
            var expected = sample.GetProperty("detections_xyxy_score_class").EnumerateArray().ToArray();
            Assert.That(actual.Count, Is.EqualTo(expected.Length), name);
            foreach (var row in expected)
            {
                var e = row.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                // Boxes within 2 pixels of the original image, scores within 0.01 (as check_picodet_conversion.py).
                var match = actual.FirstOrDefault(d => d.Class == (int)e[5] && Math.Abs(d.Score - e[4]) < .01 &&
                    Enumerable.Range(0, 4).All(k => Math.Abs(d.Rectangle[k] * (k % 2 == 0 ? image.Width : image.Height) -
                        Math.Clamp(e[k], 0, k % 2 == 0 ? image.Width : image.Height)) <= 2));
                Assert.That(match, Is.Not.Null, name + " class " + e[5]);
                actual.Remove(match);
            }
        }
        model.Dispose();
        Assert.Throws<ObjectDisposedException>(() => model.Run());
        Assert.Throws<ObjectDisposedException>(() => { var unused = model.InputBuffer; });
        Assert.Throws<ObjectDisposedException>(() => { var unused = model.IsFullyAccelerated; });
    }
}
