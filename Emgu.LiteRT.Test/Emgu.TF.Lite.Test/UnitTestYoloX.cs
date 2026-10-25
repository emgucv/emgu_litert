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
using Emgu.LiteRT.LM.Agents.Vision;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestYoloX
{
    [Test]
    public void ClassesMatchTheVisualHost()
    {
        Assert.That(YoloX.Labels, Is.EqualTo(VisualClasses.ObjectClasses));
        Assert.That(CocoVisualAgent.Describe(yoloX: true).Id,
            Is.Not.EqualTo(CocoVisualAgent.Describe().Id));
    }
    private static void Prediction(float[] data, int row, float x, float y, float w, float h, int label, float score)
    {
        int p = row * 85;
        data[p] = x; data[p + 1] = y; data[p + 2] = (float)Math.Log(w); data[p + 3] = (float)Math.Log(h);
        data[p + 4] = .8f; data[p + 5 + label] = score;
    }

    [Test]
    public void RestoresTopLeftLetterboxAndCombinesObjectnessWithClassScore()
    {
        var raw = new float[3549 * 85];
        Prediction(raw, 0, 10, 10, 4, 4, 0, .9f);
        var result = YoloX.Decode(raw, 832, 416);
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Label, Is.EqualTo("person"));
        Assert.That(result[0].Score, Is.EqualTo(.72).Within(1e-6));
        Assert.That(result[0].Rectangle, Is.EqualTo(new[] { 128f / 832, 128f / 416, 192f / 832, 192f / 416 }).Within(1e-6));
    }

    [Test]
    public void DecodesTheStride16And32Grids()
    {
        foreach (var item in new[] { (Row: 2704, Stride: 16), (Row: 3380, Stride: 32) })
        {
            var raw = new float[3549 * 85];
            Prediction(raw, item.Row, 2, 3, 1, 1, 79, .9f);
            var detection = YoloX.Decode(raw, 416, 416)[0];
            Assert.That(detection.Label, Is.EqualTo("toothbrush"));
            Assert.That(detection.Rectangle[0], Is.EqualTo(1.5 * item.Stride / 416).Within(1e-6));
            Assert.That(detection.Rectangle[1], Is.EqualTo(2.5 * item.Stride / 416).Within(1e-6));
        }
    }

    [Test]
    public void SuppressesOverlappingDifferentClassesLikeOfficialDemo()
    {
        var raw = new float[3549 * 85];
        Prediction(raw, 0, 10, 10, 4, 4, 0, .9f);
        Prediction(raw, 1, 9, 10, 4, 4, 1, .8f); // Same box, different class; grid x=1.
        Prediction(raw, 2, 25, 25, 2, 2, 2, .7f);
        var result = YoloX.Decode(raw, 416, 416);
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Class, Is.EqualTo(0));
        Assert.That(result[1].Class, Is.EqualTo(2));
        Assert.That(YoloX.Decode(raw, 416, 416, maxDetections: 1).Length, Is.EqualTo(1));
    }

    [Test]
    public void FiltersNonFinitePredictionsAndInvisiblePadding()
    {
        var raw = new float[3549 * 85];
        Prediction(raw, 0, float.NaN, 10, 4, 4, 0, .9f);
        Prediction(raw, 1, 10, 50, 1, 1, 0, .9f); // Below the content in a wide image.
        Prediction(raw, 2, 10, 10, 1, 1, 0, .9f);
        raw[2 * 85 + 2] = float.PositiveInfinity;
        Assert.That(YoloX.Decode(raw, 832, 416), Is.Empty);
    }

    [Test]
    public void RejectsWrongTensorShapeAndInvalidArguments()
    {
        var raw = new float[3549 * 85];
        Assert.Throws<ArgumentException>(() => YoloX.Decode(new float[85], 416, 416));
        Assert.Throws<ArgumentOutOfRangeException>(() => YoloX.Decode(raw, 0, 416));
        Assert.Throws<ArgumentOutOfRangeException>(() => YoloX.Decode(raw, 416, 416, float.NaN));
    }
}

[TestFixture, Category("Native")]
public class UnitTestYoloXNative
{
    [TestCase(Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase(Emgu.LiteRT.HwAccelerators.Gpu, Explicit = true)]
    public async Task CompiledDetectorMatchesOfficialOnnxOnBundledImages(Emgu.LiteRT.HwAccelerators accelerators)
    {
        string root = AppContext.BaseDirectory;
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "yolox_reference.json")));
        var weights = new DownloadableFile(
            "https://emgu-public.s3.amazonaws.com/yolox/yolox_nano_float32.tflite",
            Path.Combine("Emgu.LiteRT.Tests", "YoloX"),
            reference.RootElement.GetProperty("tflite_sha256").GetString());
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "YOLOX test checkpoint download/checksum failed.");
        string path = weights.LocalFile;
        using (var stream = File.OpenRead(path))
            Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant(),
                Is.EqualTo(reference.RootElement.GetProperty("tflite_sha256").GetString()));
        using var model = new YoloX(path, 2, accelerators, Emgu.LiteRT.GpuPrecision.Fp32);
        if (accelerators == Emgu.LiteRT.HwAccelerators.Gpu)
            Assert.That(model.IsFullyAccelerated, Is.True, "GPU-only graph must be fully accelerated.");
        foreach (var sample in reference.RootElement.GetProperty("samples").EnumerateArray())
        {
            string name = sample.GetProperty("image").GetString();
            using var image = Emgu.CV.CvInvoke.Imread(Path.Combine(root, name), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
            Emgu.LiteRT.Models.YoloXImageInput.Write(model, image);
            var actual = model.Run(image.Width, image.Height).ToList();
            var expected = sample.GetProperty("detections_xyxy_score_class").EnumerateArray().ToArray();
            Assert.That(actual.Count, Is.EqualTo(expected.Length), name);
            foreach (var row in expected)
            {
                var e = row.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var match = actual.FirstOrDefault(d => d.Class == (int)e[5] && Math.Abs(d.Score - e[4]) < .01 &&
                    Enumerable.Range(0, 4).All(k => Math.Abs(d.Rectangle[k] - Math.Clamp(e[k] /
                        (k % 2 == 0 ? image.Width : image.Height), 0, 1)) < .005));
                Assert.That(match, Is.Not.Null, name + " class " + e[5]);
                actual.Remove(match);
            }
        }
        model.Dispose();
        Assert.Throws<ObjectDisposedException>(() => model.Run(416, 416));
        Assert.Throws<ObjectDisposedException>(() => { var unused = model.InputBuffer; });
        Assert.Throws<ObjectDisposedException>(() => { var unused = model.IsFullyAccelerated; });
    }
}
