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
public class UnitTestDFine
{
    private static void Query(float[] logits, float[] boxes, int query, int label, float logit, float cx, float cy, float w, float h)
    {
        logits[query * DFine.ClassCount + label] = logit;
        boxes[query * 4] = cx; boxes[query * 4 + 1] = cy; boxes[query * 4 + 2] = w; boxes[query * 4 + 3] = h;
    }

    private static (float[] Logits, float[] Boxes) Empty() =>
        (Enumerable.Repeat(-10f, DFine.QueryCount * DFine.ClassCount).ToArray(), new float[DFine.QueryCount * 4]);

    [Test]
    public void DecodesSigmoidScoresAndCenterBoxesBestFirst()
    {
        var (logits, boxes) = Empty();
        Query(logits, boxes, 5, 16, 0.0f, 0.5f, 0.5f, 0.2f, 0.4f);   // sigmoid(0) = 0.5
        Query(logits, boxes, 7, 0, 2.0f, 0.25f, 0.5f, 0.1f, 0.2f);   // 0.8808
        Query(logits, boxes, 9, 1, -2.0f, 0.5f, 0.5f, 0.1f, 0.1f);   // 0.1192, below the threshold
        var result = DFine.Decode(logits, boxes);
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Label, Is.EqualTo("person"));
        Assert.That(result[0].Score, Is.EqualTo(0.8808).Within(1e-4));
        Assert.That(result[0].Rectangle, Is.EqualTo(new[] { 0.2f, 0.4f, 0.3f, 0.6f }).Within(1e-6));
        Assert.That(result[1].Label, Is.EqualTo("dog"));
        Assert.That(result[1].Score, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(result[1].Rectangle, Is.EqualTo(new[] { 0.4f, 0.3f, 0.6f, 0.7f }).Within(1e-6));
    }

    [Test]
    public void KeepsEveryClassOverTheThresholdAndDoesNoNms()
    {
        var (logits, boxes) = Empty();
        Query(logits, boxes, 0, 2, 3f, 0.5f, 0.5f, 0.4f, 0.4f);
        logits[0 * DFine.ClassCount + 7] = 1f;                         // the same query as a truck too
        Query(logits, boxes, 1, 2, 2f, 0.5f, 0.5f, 0.4f, 0.4f);       // an identical box: no suppression
        var result = DFine.Decode(logits, boxes);
        Assert.That(result.Select(r => r.Label), Is.EqualTo(new[] { "car", "car", "truck" }));
        Assert.That(DFine.Decode(logits, boxes, 0.5f, 2).Length, Is.EqualTo(2));
    }

    [Test]
    public void ClipsBoxesAndDropsInvalidOnes()
    {
        var (logits, boxes) = Empty();
        Query(logits, boxes, 0, 0, 3f, 0.95f, 0.5f, 0.2f, 0.2f);      // runs past the right edge
        Query(logits, boxes, 1, 0, 3f, float.NaN, 0.5f, 0.2f, 0.2f);
        Query(logits, boxes, 2, 0, 3f, 0.5f, 0.5f, 0f, 0.2f);
        Query(logits, boxes, 3, 0, 3f, 1.5f, 0.5f, 0.2f, 0.2f);       // entirely outside
        var result = DFine.Decode(logits, boxes);
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Rectangle, Is.EqualTo(new[] { 0.85f, 0.4f, 1f, 0.6f }).Within(1e-6));
        logits[0] = float.NaN;
        Assert.DoesNotThrow(() => DFine.Decode(logits, boxes));
    }

    [Test]
    public void RejectsWrongSizesAndThresholds()
    {
        var (logits, boxes) = Empty();
        Assert.Throws<ArgumentException>(() => DFine.Decode(new float[10], boxes));
        Assert.Throws<ArgumentException>(() => DFine.Decode(logits, new float[10]));
        Assert.Throws<ArgumentOutOfRangeException>(() => DFine.Decode(logits, boxes, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => DFine.Decode(logits, boxes, 0.5f, 0));
    }
}

[TestFixture, Category("Native")]
public class UnitTestDFineNative
{
    [Test]
    public async Task CompiledDetectorMatchesOnnxRuntimeOnBundledImages()
    {
        string root = AppContext.BaseDirectory;
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "dfine_reference.json")));
        string sha = reference.RootElement.GetProperty("tflite_sha256").GetString();
        var weights = new DownloadableFile(
            "https://emgu-public.s3.amazonaws.com/dfine/dfine_n_coco_640_float32.tflite",
            Path.Combine("Emgu.LiteRT.Tests", "DFine"), sha);
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "D-FINE test model download/checksum failed.");
        using var model = new DFine(weights.LocalFile, 2);
        Assert.That(model.InputSize, Is.EqualTo(640));
        foreach (var sample in reference.RootElement.GetProperty("samples").EnumerateArray())
        {
            string name = sample.GetProperty("image").GetString();
            using var image = Emgu.CV.CvInvoke.Imread(Path.Combine(root, name), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
            Emgu.LiteRT.Models.DFineImageInput.Write(model, image);
            var actual = model.Run().ToList();
            var expected = sample.GetProperty("detections_xyxy_score_class").EnumerateArray().ToArray();
            Assert.That(actual.Count, Is.EqualTo(expected.Length), name);
            foreach (var row in expected)
            {
                var e = row.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                // Boxes within 2 pixels of the original image, scores within 0.01 (as check_dfine_conversion.py).
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
    }
}
