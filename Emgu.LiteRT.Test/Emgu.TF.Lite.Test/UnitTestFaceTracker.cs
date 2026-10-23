// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// MediaPipe's face landmarker on LiteRT (FaceTracker, Emgu.LiteRT.Models/FACE.md): the decoding without a model, and the whole pipeline
// against MediaPipe's own results on the demo's synthetic sample face and five copies (TestData/face_reference.json, made by
// Emgu.LiteRT.Models/tools/make_face_reference.py).
[TestFixture]
public class UnitTestFaceTracker
{
    [Test]
    public void HasTheDetectorsAnchorsAndTheBlendshapeTables()
    {
        float[][] anchors = FaceTracker.Anchors();
        Assert.That(anchors.Length, Is.EqualTo(896));
        // 16 x 16 cells of 2 anchors, then 8 x 8 of 6.
        Assert.That(anchors[0], Is.EqualTo(new[] { 0.5f / 16, 0.5f / 16 }));
        Assert.That(anchors[511], Is.EqualTo(new[] { 15.5f / 16, 15.5f / 16 }));
        Assert.That(anchors[512], Is.EqualTo(new[] { 0.5f / 8, 0.5f / 8 }));
        Assert.That(anchors[895], Is.EqualTo(new[] { 7.5f / 8, 7.5f / 8 }));

        Assert.That(FaceTracker.BlendshapeNames.Count, Is.EqualTo(FaceTracker.BlendshapeCount));
        Assert.That(FaceTracker.BlendshapeNames.Distinct().Count(), Is.EqualTo(52));
        Assert.That(FaceTracker.IndexOfBlendshape("mouthSmileLeft"), Is.EqualTo(44));
        Assert.That(FaceTracker.BlendshapeLandmarks.Count, Is.EqualTo(146));
        Assert.That(FaceTracker.BlendshapeLandmarks, Is.Ordered.And.Unique);
        Assert.That(FaceTracker.BlendshapeLandmarks.Last(), Is.EqualTo(477));
        Assert.That(FaceTracker.Contours.SelectMany(p => p), Is.All.InRange(0, FaceTracker.LandmarkCount - 1));
    }

    [Test]
    public void DecodesADetectionThroughTheLetterbox()
    {
        // One anchor fires, with an offset; the image is twice as wide as tall, so the detector's input has bands above and below.
        var regressors = new float[896 * 16];
        var logits = Enumerable.Repeat(-10f, 896).ToArray();
        int anchor = 16 * 8 * 2 + 8 * 2;     // the cell (8, 8) of the 16 x 16 grid: centre (0.53125, 0.53125)
        logits[anchor] = 4;
        regressors[anchor * 16] = 0;         // centre offset
        regressors[anchor * 16 + 1] = 0;
        regressors[anchor * 16 + 2] = 32;    // 32 x 32 pixels of the 128 input
        regressors[anchor * 16 + 3] = 32;
        regressors[anchor * 16 + 4] = -8;    // the first eye 8 px left of the centre, the second 8 px right: level
        regressors[anchor * 16 + 6] = 8;
        FaceDetection face = FaceTracker.Decode(regressors, logits, 200, 100).Single();
        Assert.That(face.Score, Is.EqualTo(1 / (1 + Math.Exp(-4))).Within(1e-5));
        // The input is 128 x 64 of image in the middle 32 rows: y' = (y - 0.25) / 0.5.
        Assert.That(face.Box[0], Is.EqualTo(0.53125f - 0.125f).Within(1e-5));
        Assert.That(face.Box[2], Is.EqualTo(0.53125f + 0.125f).Within(1e-5));
        Assert.That(face.Box[1], Is.EqualTo((0.53125f - 0.125f - 0.25f) / 0.5f).Within(1e-5));
        Assert.That(face.Box[3], Is.EqualTo((0.53125f + 0.125f - 0.25f) / 0.5f).Within(1e-5));
        HandRect rect = FaceTracker.RectOf(face, 200, 100);
        Assert.That(rect.Rotation, Is.EqualTo(0).Within(1e-6));
        Assert.That(rect.Width, Is.EqualTo(0.25f * 1.5f).Within(1e-5));
        Assert.That(rect.Height, Is.EqualTo(0.5f * 1.5f).Within(1e-5));
        Assert.That(FaceTracker.Decode(regressors, Enumerable.Repeat(-10f, 896).ToArray(), 200, 100), Is.Empty);
    }

    [Test]
    public void GivesADynamicBatchASizeOfOne()
    {
        // Shape vectors as a flatbuffer holds them: a length, then the values.
        int[] words = { 4, -1, 256, 256, 3, /* not a batch: */ 3, -1, 0, 5, /* a batch: */ 2, -1, 128, /* a plain shape: */ 4, 1, 1, 1, 1434 };
        byte[] model = words.SelectMany(BitConverter.GetBytes).ToArray();
        byte[] patched = FaceModelFiles.WithStaticBatch(model, out int count);
        int[] result = Enumerable.Range(0, words.Length).Select(i => BitConverter.ToInt32(patched, 4 * i)).ToArray();
        Assert.That(count, Is.EqualTo(2));
        Assert.That(result, Is.EqualTo(new[] { 4, 1, 256, 256, 3, 3, -1, 0, 5, 2, 1, 128, 4, 1, 1, 1, 1434 }));
        Assert.That(model, Is.EqualTo(words.SelectMany(BitConverter.GetBytes).ToArray()), "the input is not changed");
    }

    // The test image's copies (the same OpenCV calls as make_face_reference.py).
    internal static Emgu.CV.Mat FaceVariant(Emgu.CV.Mat image, string name)
    {
        var result = new Emgu.CV.Mat();
        int w = image.Width, h = image.Height;
        var grey = new Emgu.CV.Structure.MCvScalar(128, 128, 128);
        switch (name)
        {
            case "original": image.CopyTo(result); break;
            case "flip": Emgu.CV.CvInvoke.Flip(image, result, Emgu.CV.CvEnum.FlipType.Horizontal); break;
            case "rot90": Emgu.CV.CvInvoke.Rotate(image, result, Emgu.CV.CvEnum.RotateFlags.Rotate90Clockwise); break;
            case "crop": using (var region = new Emgu.CV.Mat(image, new System.Drawing.Rectangle(w / 8, h / 8, w - w / 8 - w / 8, h - h / 8 - h / 8))) region.CopyTo(result); break;
            case "far":
                using (var padded = new Emgu.CV.Mat())
                {
                    Emgu.CV.CvInvoke.CopyMakeBorder(image, padded, h / 2, h / 2, w / 2, w / 2, Emgu.CV.CvEnum.BorderType.Constant, grey);
                    Emgu.CV.CvInvoke.Resize(padded, result, new System.Drawing.Size(w, h), 0, 0, Emgu.CV.CvEnum.Inter.Linear);
                }
                break;
            case "tilt":
                using (var matrix = new Emgu.CV.Mat())
                {
                    Emgu.CV.CvInvoke.GetRotationMatrix2D(new System.Drawing.PointF(w / 2f, h / 2f), 20, 1.0, matrix);
                    Emgu.CV.CvInvoke.WarpAffine(image, result, matrix, image.Size, Emgu.CV.CvEnum.Inter.Linear, Emgu.CV.CvEnum.Warp.Default,
                        Emgu.CV.CvEnum.BorderType.Constant, grey);
                }
                break;
            default: throw new ArgumentException(name);
        }
        return result;
    }
}

[TestFixture, Category("Native")]
public class UnitTestFaceTrackerNative
{
    internal static async Task<string[]> ModelsAsync()
    {
        string subfolder = Path.Combine("Emgu.LiteRT.Tests", "Face");
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), subfolder);
        var bundle = new DownloadableFile(FaceModelFiles.Url, subfolder, FaceModelFiles.Sha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "The face landmarker bundle's download or checksum failed.");
        return FaceModelFiles.Extract(bundle.LocalFile, folder);
    }

    [TestCase(Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase(Emgu.LiteRT.HwAccelerators.Gpu, Explicit = true)]
    public async Task LandmarksAndExpressionsMatchMediaPipeOnItsTestImageAndCopiesOfIt(Emgu.LiteRT.HwAccelerators accelerators)
    {
        string root = AppContext.BaseDirectory;
        string[] models = await ModelsAsync();
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "face_reference.json")));
        using var tracker = new FaceTracker(models[0], models[1], models[2], 2,
            accelerators == Emgu.LiteRT.HwAccelerators.Gpu ? Emgu.LiteRT.HwAccelerators.Gpu | Emgu.LiteRT.HwAccelerators.Cpu : accelerators,
            Emgu.LiteRT.GpuPrecision.Fp32);
        if (accelerators == Emgu.LiteRT.HwAccelerators.Gpu)
            Assert.That(tracker.IsFullyAccelerated, Is.True, "The detector and the landmark model must compile fully for the GPU.");
        using var source = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "face_portrait.png"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        foreach (var image in reference.RootElement.GetProperty("images").EnumerateObject())
        {
            using var bgr = UnitTestFaceTracker.FaceVariant(source, image.Name);
            List<Face> faces = Emgu.LiteRT.Models.FaceImageInput.Track(tracker, bgr, 2);
            var expected = image.Value.GetProperty("faces").EnumerateArray().ToList();
            if (expected.Count == 0)
            {
                Assert.That(faces, Is.Empty, image.Name);
                continue;
            }
            Assert.That(faces.Count, Is.EqualTo(expected.Count), image.Name);
            foreach (Face face in faces)
            {
                // The nearest of MediaPipe's faces.
                (double mean, double max, JsonElement match) best = (double.MaxValue, 0, default);
                foreach (JsonElement candidate in expected)
                {
                    double sum = 0, max = 0;
                    int i = 0;
                    foreach (JsonElement p in candidate.GetProperty("landmarks").EnumerateArray())
                    {
                        double d = Math.Sqrt(Math.Pow((face.Landmarks[i][0] - p[0].GetDouble()) * bgr.Width, 2) + Math.Pow((face.Landmarks[i][1] - p[1].GetDouble()) * bgr.Height, 2));
                        sum += d; max = Math.Max(max, d); i++;
                    }
                    if (sum / i < best.mean) best = (sum / i, max, candidate);
                }
                // The crop is resampled slightly differently from MediaPipe's: within a pixel or so on the 256 px image.
                float[] wanted = best.match.GetProperty("blendshapes").EnumerateArray().Select(e => e.GetSingle()).ToArray();
                TestContext.Out.WriteLine($"{accelerators} {image.Name}: landmarks within {best.mean:0.00} px on average, {best.max:0.00} at most; " +
                    $"expressions within {Enumerable.Range(0, FaceTracker.BlendshapeCount).Max(k => Math.Abs(face.Blendshapes[k] - wanted[k])):0.000}");
                Assert.That(best.mean, Is.LessThan(1), image.Name + " mean landmark error in pixels");
                Assert.That(best.max, Is.LessThan(4), image.Name + " largest landmark error in pixels");
                Assert.That(face.Presence, Is.GreaterThan(0.5f));
                for (int k = 0; k < FaceTracker.BlendshapeCount; k++)
                    Assert.That(face.Blendshapes[k], Is.EqualTo(wanted[k]).Within(0.1), image.Name + " " + FaceTracker.BlendshapeNames[k]);
            }
        }
        // A smiling face: the portrait's top expressions are the smiles.
        using (var original = UnitTestFaceTracker.FaceVariant(source, "original"))
        {
            Face face = Emgu.LiteRT.Models.FaceImageInput.Track(tracker, original).Single();
            Assert.That(face.Blendshape("mouthSmileLeft"), Is.GreaterThan(0.8f));
            Assert.That(face.Blendshape("eyeBlinkLeft"), Is.LessThan(0.3f));
        }
        // An image without a face has none.
        using var shuttle = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "space_shuttle.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(Emgu.LiteRT.Models.FaceImageInput.Track(tracker, shuttle), Is.Empty);
        tracker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tracker.Detect(100, 100));
    }

    [Test]
    public async Task UnpacksTheLandmarkModelWithAStaticBatch()
    {
        string[] models = await ModelsAsync();
        Assert.That(models.Select(Path.GetFileName), Is.EqualTo(FaceModelFiles.ModelNames));
        // Already patched: nothing left to patch.
        FaceModelFiles.WithStaticBatch(File.ReadAllBytes(models[1]), out int left);
        Assert.That(left, Is.Zero);
    }
}
