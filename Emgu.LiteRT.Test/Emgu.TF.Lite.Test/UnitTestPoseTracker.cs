// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// MediaPipe's pose landmarker on LiteRT (PoseTracker, TODO item 51): the math without a model, unpacking the models, and the whole
// pipeline against MediaPipe's own results on its test image and transformed copies of it.
[TestFixture]
public class UnitTestPoseTracker
{
    [Test]
    public void TheDetectorHas2254AnchorsOnThreeGrids()
    {
        float[][] anchors = PoseTracker.Anchors();
        Assert.That(anchors.Length, Is.EqualTo(2254));
        Assert.That(anchors[0], Is.EqualTo(new[] { 0.5f / 28, 0.5f / 28 }));
        Assert.That(anchors[1], Is.EqualTo(anchors[0]));                       // two anchors in every cell
        Assert.That(anchors[1568], Is.EqualTo(new[] { 0.5f / 14, 0.5f / 14 })); // the stride-16 grid starts after 28 x 28 x 2
        Assert.That(anchors[1568 + 392], Is.EqualTo(new[] { 0.5f / 7, 0.5f / 7 })); // the stride-32 grid: 3 layers x 2 anchors a cell
        Assert.That(anchors[2253], Is.EqualTo(new[] { 6.5f / 7, 6.5f / 7 }));
    }

    [Test]
    public void ImagesAreLetterboxedIntoTheSquareInput()
    {
        PoseTracker.Letterbox(1000, 667, out int w, out int h, out int left, out int top);
        Assert.That((w, h, left, top), Is.EqualTo((224, 149, 0, 37)));
        PoseTracker.Letterbox(480, 640, out w, out h, out left, out top);
        Assert.That((w, h, left, top), Is.EqualTo((168, 224, 28, 0)));
    }

    private static (float[] Regressors, float[] Logits) Detections(params (int Anchor, float Logit, float W, float H)[] people)
    {
        var regressors = new float[2254 * 12];
        var logits = Enumerable.Repeat(-20f, 2254).ToArray();
        foreach (var (anchor, logit, w, h) in people)
        {
            logits[anchor] = logit;
            regressors[anchor * 12 + 2] = w * 224;
            regressors[anchor * 12 + 3] = h * 224;
            regressors[anchor * 12 + 5] = 0.1f * 224;       // keypoint 0 (the hips) 0.1 below the anchor
            regressors[anchor * 12 + 7] = -0.3f * 224;      // keypoint 1 (the body's circle) 0.3 above it
        }
        return (regressors, logits);
    }

    [Test]
    public void PeopleAreDecodedFromTheAnchorsAndMergedByWeightedNms()
    {
        var (regressors, logits) = Detections((300, 4f, 0.3f, 0.6f));
        PoseDetection[] found = PoseTracker.Decode(regressors, logits, 224, 224);
        Assert.That(found.Length, Is.EqualTo(1));
        float[] anchor = PoseTracker.Anchors()[300];
        Assert.That(found[0].Score, Is.EqualTo(1 / (1 + Math.Exp(-4))).Within(1e-5));
        Assert.That(found[0].Box, Is.EqualTo(new[] { anchor[0] - 0.15f, anchor[1] - 0.3f, anchor[0] + 0.15f, anchor[1] + 0.3f }).Within(1e-5));
        Assert.That(found[0].Keypoints[0], Is.EqualTo(new[] { anchor[0], anchor[1] + 0.1f }).Within(1e-5));
        Assert.That(found[0].Keypoints[1], Is.EqualTo(new[] { anchor[0], anchor[1] - 0.3f }).Within(1e-5));

        // Neighbouring anchors on the same person merge, weighted by score; the best first, one by default.
        (regressors, logits) = Detections((300, 4f, 0.3f, 0.6f), (302, 2f, 0.3f, 0.6f), (1900, 3f, 0.3f, 0.6f));
        Assert.That(PoseTracker.Decode(regressors, logits, 224, 224, 0.5f, 0.3f, 2).Length, Is.EqualTo(2));
        Assert.That(PoseTracker.Decode(regressors, logits, 224, 224).Length, Is.EqualTo(1));
        Assert.That(PoseTracker.Decode(regressors, logits, 224, 224, 0.99f).Length, Is.EqualTo(0));
        Assert.Throws<ArgumentException>(() => PoseTracker.Decode(new float[10], logits, 224, 224));
        Assert.Throws<ArgumentOutOfRangeException>(() => PoseTracker.Decode(regressors, logits, 0, 224));
    }

    [Test]
    public void TheLetterboxPaddingIsTakenOutOfTheDetections()
    {
        // A 200 x 100 image: 224 x 112 in the input with 56 pixels (0.25) of padding above and below.
        var (regressors, logits) = Detections((300, 4f, 0.3f, 0.2f));
        PoseDetection[] found = PoseTracker.Decode(regressors, logits, 200, 100);
        float[] anchor = PoseTracker.Anchors()[300];
        Assert.That(found[0].Box[1], Is.EqualTo((anchor[1] - 0.1f - 0.25f) / 0.5f).Within(1e-5));
        Assert.That(found[0].Box[0], Is.EqualTo(anchor[0] - 0.15f).Within(1e-5));
    }

    [Test]
    public void TheRegionIsCenteredOnTheHipsAndTurnsWithTheBody()
    {
        // The hips at (0.5, 0.6), the circle point 0.3 above: upright, and 0.3 x 2 x 1.25 = 0.75 across.
        HandRect up = PoseTracker.RectOf(new[] { 0.5f, 0.6f }, new[] { 0.5f, 0.3f }, 100, 100);
        Assert.That(up.Rotation, Is.EqualTo(0).Within(1e-6));
        Assert.That((up.CenterX, up.CenterY), Is.EqualTo((0.5f, 0.6f)));
        Assert.That(up.Width, Is.EqualTo(0.75).Within(1e-5));
        Assert.That(up.Height, Is.EqualTo(0.75).Within(1e-5));
        // A body lying with its head to the right: a quarter turn; the region is square in pixels whatever the image's shape.
        HandRect right = PoseTracker.RectOf(new[] { 0.3f, 0.5f }, new[] { 0.6f, 0.5f }, 200, 100);
        Assert.That(right.Rotation, Is.EqualTo(Math.PI / 2).Within(1e-5));
        Assert.That(right.Width * 200, Is.EqualTo(right.Height * 100).Within(1e-3));
        // Upside down: half a turn.
        HandRect down = PoseTracker.RectOf(new[] { 0.5f, 0.3f }, new[] { 0.5f, 0.6f }, 100, 100);
        Assert.That(Math.Abs(down.Rotation), Is.EqualTo(Math.PI).Within(1e-5));
        Assert.Throws<ArgumentNullException>(() => PoseTracker.RectOf((PoseDetection)null, 100, 100));
    }

    [Test]
    public void LandmarksAreProjectedBackThroughTheRegion()
    {
        var rect = new HandRect { CenterX = 0.5f, CenterY = 0.5f, Width = 0.5f, Height = 0.5f, Rotation = 0 };
        var raw = new float[39 * 5];
        raw[0] = 128; raw[1] = 128; raw[2] = 0; raw[3] = 0; raw[4] = 20;           // the middle of the crop, visibility 0.5, presence ~1
        raw[5] = 256; raw[6] = 0; raw[7] = 256; raw[8] = -20; raw[9] = 0;          // its top right corner, one crop width in front
        raw[33 * 5] = 128; raw[33 * 5 + 1] = 64;                                   // the first auxiliary landmark
        float[][] points = PoseTracker.ProjectLandmarks(raw, rect, out float[][] auxiliary);
        Assert.That(points.Length, Is.EqualTo(33));
        Assert.That(auxiliary.Length, Is.EqualTo(6));
        Assert.That(points[0].Take(3), Is.EqualTo(new[] { 0.5f, 0.5f, 0f }).Within(1e-6));
        Assert.That(points[0][3], Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(points[0][4], Is.EqualTo(1f).Within(1e-6));
        Assert.That(points[1].Take(3), Is.EqualTo(new[] { 0.75f, 0.25f, 0.5f }).Within(1e-6));
        Assert.That(points[1][3], Is.LessThan(1e-6));
        Assert.That(auxiliary[0].Take(2), Is.EqualTo(new[] { 0.5f, 0.375f }).Within(1e-6));
        rect.Rotation = (float)(Math.PI / 2);                                       // a quarter turn: the top right corner goes to the bottom right
        float[][] turned = PoseTracker.ProjectLandmarks(raw, rect, out _);
        Assert.That(turned[1][0], Is.EqualTo(0.75f).Within(1e-5));
        Assert.That(turned[1][1], Is.EqualTo(0.75f).Within(1e-5));
        Assert.Throws<ArgumentException>(() => PoseTracker.ProjectLandmarks(new float[10], rect, out _));
    }

    [Test]
    public void TheTwoModelsAreUnpackedFromTheBundle()
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pose-models-" + Guid.NewGuid().ToString("N"));
        string bundle = folder + ".task";
        try
        {
            byte[] Zip(params (string Name, byte[] Data)[] entries)
            {
                using var memory = new MemoryStream();
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                    foreach (var (name, data) in entries)
                    {
                        using var stream = zip.CreateEntry(name).Open();
                        stream.Write(data, 0, data.Length);
                    }
                return memory.ToArray();
            }
            File.WriteAllBytes(bundle, Zip(("pose_detector.tflite", new byte[] { 1 }), ("pose_landmarks_detector.tflite", new byte[] { 2, 2 })));
            string[] paths = PoseModelFiles.Extract(bundle, folder);
            Assert.That(paths.Select(Path.GetFileName), Is.EqualTo(PoseModelFiles.ModelNames));
            Assert.That(paths.Select(p => File.ReadAllBytes(p).Length), Is.EqualTo(new[] { 1, 2 }));
            File.Delete(bundle);
            Assert.That(PoseModelFiles.Extract(bundle, folder), Is.EqualTo(paths));            // called again, it leaves the files
            string other = Path.Combine(TestContext.CurrentContext.WorkDirectory, "pose-models-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(bundle, Zip(("pose_detector.tflite", new byte[] { 1 })));
            Assert.Throws<InvalidDataException>(() => PoseModelFiles.Extract(bundle, other));    // a bundle without a part is refused
            Directory.Delete(other, true);
        }
        finally
        {
            File.Delete(bundle);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        Assert.That(new[] { PoseModelFiles.Lite, PoseModelFiles.Full, PoseModelFiles.Heavy }.Select(v => v.Url),
            Is.All.StartWith("https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_"));
    }
}

[TestFixture, Category("Native")]
public class UnitTestPoseTrackerNative
{
    private static async Task<string[]> ModelsAsync(PoseModelFiles.Variant variant)
    {
        string subfolder = Path.Combine("Emgu.LiteRT.Tests", "Pose-" + variant.Name);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), subfolder);
        var bundle = new DownloadableFile(variant.Url, subfolder, variant.Sha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "The pose landmarker bundle's download or checksum failed.");
        return PoseModelFiles.Extract(bundle.LocalFile, folder);
    }

    // The test image and the copies the reference was made from (the same OpenCV calls as the Python script's).
    private static Emgu.CV.Mat Variant(Emgu.CV.Mat image, string name)
    {
        var result = new Emgu.CV.Mat();
        switch (name)
        {
            case "original": image.CopyTo(result); break;
            case "flip": Emgu.CV.CvInvoke.Flip(image, result, Emgu.CV.CvEnum.FlipType.Horizontal); break;
            case "rot90": Emgu.CV.CvInvoke.Rotate(image, result, Emgu.CV.CvEnum.RotateFlags.Rotate90Clockwise); break;
            case "crop": using (var region = new Emgu.CV.Mat(image, new System.Drawing.Rectangle(150, 0, 700, image.Height))) region.CopyTo(result); break;
            case "far":
                using (var padded = new Emgu.CV.Mat())
                {
                    Emgu.CV.CvInvoke.CopyMakeBorder(image, padded, image.Height / 2, image.Height / 2, image.Width / 2, image.Width / 2,
                        Emgu.CV.CvEnum.BorderType.Constant, new Emgu.CV.Structure.MCvScalar(128, 128, 128));
                    Emgu.CV.CvInvoke.Resize(padded, result, new System.Drawing.Size(1000, 667), 0, 0, Emgu.CV.CvEnum.Inter.Linear);
                }
                break;
            case "tilt":
                using (var matrix = new Emgu.CV.Mat())
                {
                    Emgu.CV.CvInvoke.GetRotationMatrix2D(new System.Drawing.PointF(image.Width / 2f, image.Height / 2f), 20, 1.0, matrix);
                    Emgu.CV.CvInvoke.WarpAffine(image, result, matrix, image.Size, Emgu.CV.CvEnum.Inter.Linear, Emgu.CV.CvEnum.Warp.Default,
                        Emgu.CV.CvEnum.BorderType.Constant, new Emgu.CV.Structure.MCvScalar(128, 128, 128));
                }
                break;
            default: throw new ArgumentException(name);
        }
        return result;
    }

    [TestCase("lite", Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase("full", Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase("lite", Emgu.LiteRT.HwAccelerators.Gpu, Explicit = true)]
    public async Task LandmarksMatchMediaPipeOnItsTestImageAndCopiesOfIt(string variantName, Emgu.LiteRT.HwAccelerators accelerators)
    {
        string root = AppContext.BaseDirectory;
        string[] models = await ModelsAsync(variantName == "lite" ? PoseModelFiles.Lite : PoseModelFiles.Full);
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pose_reference.json")));
        using var tracker = new PoseTracker(models[0], models[1], 2, accelerators, Emgu.LiteRT.GpuPrecision.Fp32);
        if (accelerators == Emgu.LiteRT.HwAccelerators.Gpu)
            Assert.That(tracker.IsFullyAccelerated, Is.True, "Both models must compile fully for the GPU.");
        using var source = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "pose_standing.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        foreach (var image in reference.RootElement.GetProperty("images").EnumerateObject())
        {
            using var bgr = Variant(source, image.Name);
            var poses = Maui.Demo.Lite.PoseImageInput.Track(tracker, bgr);
            Assert.That(poses.Count, Is.EqualTo(1), image.Name);
            var expected = image.Value.GetProperty(variantName);
            int index = 0;
            double sum = 0;
            foreach (var point in expected.GetProperty("landmarks").EnumerateArray())
            {
                double dx = poses[0].Landmarks[index][0] * bgr.Width - point[0].GetSingle() * bgr.Width;
                double dy = poses[0].Landmarks[index][1] * bgr.Height - point[1].GetSingle() * bgr.Height;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                sum += distance;
                // A few pixels on a 1000 px image: the crop is resampled slightly differently from MediaPipe's.
                Assert.That(distance, Is.LessThan(30), image.Name + " landmark " + PoseTracker.LandmarkNames[index]);
                if (point[3].GetSingle() > 0.9f)
                    Assert.That(poses[0].Landmarks[index][3], Is.GreaterThan(0.7f), image.Name + " visibility of " + PoseTracker.LandmarkNames[index]);
                index++;
            }
            Assert.That(sum / 33, Is.LessThan(12), image.Name + " mean error in pixels");
            int w = 0;
            foreach (var point in expected.GetProperty("world").EnumerateArray())
            {
                for (int k = 0; k < 3; k++)
                    Assert.That(poses[0].WorldLandmarks[w][k], Is.EqualTo(point[k].GetSingle()).Within(0.12), image.Name + " world " + w);
                w++;
            }
            Assert.That(poses[0].Presence, Is.GreaterThan(0.5f));
        }
        // An image without a person has none.
        using var shuttle = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "space_shuttle.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(Maui.Demo.Lite.PoseImageInput.Track(tracker, shuttle), Is.Empty);
        tracker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tracker.Detect(100, 100));
    }
}
