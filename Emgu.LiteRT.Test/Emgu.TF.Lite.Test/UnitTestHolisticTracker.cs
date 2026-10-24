//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

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

// HolisticTracker's geometry (MediaPipe's calculators, checked by hand) and, with the models, the whole landmarker against MediaPipe's own
// results on the demo's synthetic sample person and four copies (TestData/holistic_reference.json, made by
// Emgu.LiteRT.Models/tools/make_holistic_reference.py).
public class UnitTestHolisticTracker
{
    [Test]
    public void TransformsARegionAsMediaPipeDoes()
    {
        // Upright: the shift is along the image's axes, square on the longer side in pixels, then scaled.
        var upright = new HandRect { CenterX = 0.5f, CenterY = 0.5f, Width = 0.2f, Height = 0.1f };
        HandRect r = HolisticTracker.Transform(upright, 200, 100, 2, 0, -0.1f, true);
        Assert.That(r.CenterX, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(r.CenterY, Is.EqualTo(0.49f).Within(1e-6));
        // 0.2 x 200 = 40 px, 0.1 x 100 = 10 px: a 40 px square, then x 2.
        Assert.That(r.Width, Is.EqualTo(0.4f).Within(1e-6));
        Assert.That(r.Height, Is.EqualTo(0.8f).Within(1e-6));
        // Turned by 90 degrees, "up" along the region points along the image's +x.
        var turned = new HandRect { CenterX = 0.5f, CenterY = 0.5f, Width = 0.1f, Height = 0.1f, Rotation = (float)(Math.PI / 2) };
        r = HolisticTracker.Transform(turned, 100, 100, 1, 0, -0.1f, false);
        Assert.That(r.CenterX, Is.EqualTo(0.51f).Within(1e-6));
        Assert.That(r.CenterY, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(r.Rotation, Is.EqualTo(turned.Rotation));
    }

    [Test]
    public void FindsAHandsRegionFromThePose()
    {
        var pose = Enumerable.Range(0, 33).Select(_ => new[] { 0.5f, 0.5f, 0, 1, 1 }).ToArray();
        // The left wrist below the hand, the index and pinky above it: an upright hand, rotation 0.
        pose[15] = new[] { 0.5f, 0.6f, 0, 0.9f, 1 };
        pose[17] = new[] { 0.45f, 0.5f, 0, 0.9f, 1 };
        pose[19] = new[] { 0.525f, 0.5f, 0, 0.9f, 1 };
        HandRect rect = HolisticTracker.HandRectFromPose(pose, true, 100, 100);
        // Middle = (2 index + pinky) / 3 = (0.5, 0.5); 10 px from the wrist, so 20 px, x 2.7 = 54 px, shifted 0.1 of 20 px up.
        Assert.That(rect.Rotation, Is.EqualTo(0).Within(1e-6));
        Assert.That(rect.CenterX, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(rect.CenterY, Is.EqualTo(0.48f).Within(1e-6));
        Assert.That(rect.Width, Is.EqualTo(0.54f).Within(1e-5));
        Assert.That(rect.Height, Is.EqualTo(0.54f).Within(1e-5));
        // A hand pointing right is turned by -90 degrees, so the wrist is below it in the crop.
        pose[16] = new[] { 0.4f, 0.5f, 0, 0.9f, 1 };
        pose[18] = new[] { 0.5f, 0.45f, 0, 0.9f, 1 };
        pose[20] = new[] { 0.5f, 0.525f, 0, 0.9f, 1 };
        rect = HolisticTracker.HandRectFromPose(pose, false, 100, 100);
        Assert.That(rect.Rotation, Is.EqualTo(Math.PI / 2).Within(1e-6));
        Assert.That(rect.CenterX, Is.EqualTo(0.52f).Within(1e-6), "shifted along the hand, away from the wrist");
        // An unseen wrist has no region.
        pose[15][3] = 0.1f;
        Assert.That(HolisticTracker.HandRectFromPose(pose, true, 100, 100), Is.Null);
    }

    [Test]
    public void FindsAHandsRegionFromTheRecropModelsPoints()
    {
        // The second point is below the first: upright, twice the distance across, shifted up by 0.1 of that.
        HandRect rect = HolisticTracker.HandRectFromRecrop(new[] { 0.5f, 0.5f }, new[] { 0.5f, 0.6f }, 100, 100);
        Assert.That(rect.Rotation, Is.EqualTo(0).Within(1e-6));
        Assert.That(rect.Width, Is.EqualTo(0.2f).Within(1e-6));
        Assert.That(rect.CenterY, Is.EqualTo(0.48f).Within(1e-6));
    }

    [Test]
    public void FindsTheFacesRegionFromThePose()
    {
        var pose = Enumerable.Range(0, 33).Select(_ => new[] { 0.5f, 0.9f, 0, 1, 1 }).ToArray();
        for (int i = 0; i <= 10; i++) pose[i] = new[] { 0.5f, 0.3f, 0, 1, 1 };
        pose[2] = new[] { 0.45f, 0.3f, 0, 1, 1 };   // right eye, on the image's left
        pose[5] = new[] { 0.55f, 0.3f, 0, 1, 1 };   // left eye
        pose[9] = new[] { 0.5f, 0.35f, 0, 1, 1 };
        HandRect rect = HolisticTracker.FaceRectFromPose(pose, 100, 100);
        Assert.That(rect.Rotation, Is.EqualTo(Math.PI).Within(1e-6).Or.EqualTo(-Math.PI).Within(1e-6),
            "MediaPipe turns the line from landmark 5 to landmark 2 to 0 degrees");
        Assert.That(rect.CenterX, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(rect.CenterY, Is.EqualTo(0.325f).Within(1e-6));
        Assert.That(rect.Width, Is.EqualTo(0.3f).Within(1e-5), "10 px wide, square, x 3");
    }

    [Test]
    public void RefinesPoseLandmarksWithTheHeatmap()
    {
        var raw = new float[39 * 5];
        var heatmap = Enumerable.Repeat(-30f, 64 * 64 * 39).ToArray();
        // Landmark 0 at (100, 100) of 256 px: cell (25, 25); the heatmap's peak is at cell (27, 25).
        raw[0] = 100; raw[1] = 100;
        heatmap[(25 * 64 + 27) * 39] = 10;
        // Landmark 1 has no confident cell near it: unchanged.
        raw[5] = 50; raw[6] = 60;
        float[] refined = PoseTracker.RefineWithHeatmap(raw, heatmap);
        Assert.That(refined[0], Is.EqualTo(27f / 64 * 256).Within(0.01));
        Assert.That(refined[1], Is.EqualTo(25f / 64 * 256).Within(0.01));
        Assert.That(refined[5], Is.EqualTo(50));
        Assert.That(refined[6], Is.EqualTo(60));
    }

    // The test image's copies (the same OpenCV calls as make_holistic_reference.py).
    internal static Emgu.CV.Mat HolisticVariant(Emgu.CV.Mat image, string name)
    {
        if (name != "big") return UnitTestFaceTracker.FaceVariant(image, name);
        var result = new Emgu.CV.Mat();
        Emgu.CV.CvInvoke.Resize(image, result, new System.Drawing.Size(image.Width * 2, image.Height * 2), 0, 0, Emgu.CV.CvEnum.Inter.Linear);
        return result;
    }
}

[TestFixture, Category("Native")]
public class UnitTestHolisticTrackerNative
{
    internal static async Task<string[]> ModelsAsync()
    {
        string subfolder = Path.Combine("Emgu.LiteRT.Tests", "Holistic");
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), subfolder);
        var bundle = new DownloadableFile(HolisticModelFiles.Url, subfolder, HolisticModelFiles.Sha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "The holistic landmarker bundle's download or checksum failed.");
        return HolisticModelFiles.Extract(bundle.LocalFile, folder);
    }

    // Mean distance in pixels between our landmarks and MediaPipe's, and the largest among those MediaPipe sees (visibility 0.5 or more, when
    // it gives one): points it guesses outside the picture, such as the feet of a waist-up photo, move a few pixels with any change in the crop.
    private static (double mean, double max) Distance(float[][] ours, JsonElement theirs, int width, int height)
    {
        double sum = 0, max = 0;
        int i = 0;
        foreach (JsonElement p in theirs.EnumerateArray())
        {
            double d = Math.Sqrt(Math.Pow((ours[i][0] - p[0].GetDouble()) * width, 2) + Math.Pow((ours[i][1] - p[1].GetDouble()) * height, 2));
            sum += d;
            if (p.GetArrayLength() < 4 || p[3].GetDouble() >= 0.5) max = Math.Max(max, d);
            i++;
        }
        Assert.That(i, Is.EqualTo(ours.Length));
        return (sum / i, max);
    }

    // Mean distance in metres between world landmarks, or between their positions relative to the first one.
    private static double WorldDistance(float[][] ours, JsonElement theirs, bool relative)
    {
        double[][] t = theirs.EnumerateArray().Select(e => new[] { e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble() }).ToArray();
        Assert.That(t.Length, Is.EqualTo(ours.Length));
        double sum = 0;
        for (int i = 0; i < t.Length; i++)
            sum += Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(ours[i][k] - (relative ? ours[0][k] : 0) - t[i][k] + (relative ? t[0][k] : 0), 2)));
        return sum / t.Length;
    }

    // The GPU case runs the hand and face models on the GPU (the pose models always run on the CPU: they don't compile for it).
    [TestCase(Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase(Emgu.LiteRT.HwAccelerators.Gpu, Explicit = true)]
    public async Task LandmarksMatchMediaPipeOnTheSamplePersonAndCopiesOfIt(Emgu.LiteRT.HwAccelerators accelerators)
    {
        string root = AppContext.BaseDirectory;
        string[] models = await ModelsAsync();
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "holistic_reference.json")));
        using var tracker = new HolisticTracker(models, 2,
            accelerators == Emgu.LiteRT.HwAccelerators.Gpu ? Emgu.LiteRT.HwAccelerators.Gpu | Emgu.LiteRT.HwAccelerators.Cpu : accelerators,
            Emgu.LiteRT.GpuPrecision.Fp32);
        if (accelerators == Emgu.LiteRT.HwAccelerators.Gpu)
            TestContext.Out.WriteLine($"Hand and face models fully on the GPU: {tracker.IsFullyAccelerated}");
        using var source = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "holistic_person.png"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        var failures = new List<string>();
        foreach (var image in reference.RootElement.GetProperty("images").EnumerateObject())
        {
            using var bgr = UnitTestHolisticTracker.HolisticVariant(source, image.Name);
            HolisticResult result = Emgu.LiteRT.Models.HolisticImageInput.Track(tracker, bgr);
            Assert.That(result, Is.Not.Null, image.Name);
            // Errors in pixels of a 256 px picture, whatever the copy's size.
            double perPixel = 256.0 / Math.Max(bgr.Width, bgr.Height);
            void Check(string part, float[][] ours, string key, double meanLimit, double maxLimit)
            {
                JsonElement theirs = image.Value.GetProperty(key);
                if (theirs.GetArrayLength() == 0)
                {
                    if (ours != null) failures.Add($"{image.Name} {part}: found, MediaPipe found none");
                    return;
                }
                if (ours == null) { failures.Add($"{image.Name} {part}: not found"); return; }
                (double mean, double max) = Distance(ours, theirs, bgr.Width, bgr.Height);
                mean *= perPixel; max *= perPixel;
                TestContext.Out.WriteLine($"{accelerators} {image.Name} {part}: within {mean:0.00} px on average, {max:0.00} at most");
                if (mean >= meanLimit || max >= maxLimit) failures.Add($"{image.Name} {part}: {mean:0.00} / {max:0.00} px");
            }
            Check("pose", result.Pose.Landmarks, "pose", 1.5, 3);
            Check("left hand", result.LeftHand?.Landmarks, "leftHand", 0.75, 2);
            Check("right hand", result.RightHand?.Landmarks, "rightHand", 0.75, 2);
            Check("face", result.Face?.Landmarks, "face", 0.5, 1.5);
            // World landmarks in metres: the shape relative to the first point (the nose, the wrist), and where they are - a hand is moved so its
            // wrist is the pose's wrist, so it carries that point's error too.
            void CheckWorld(string part, float[][] ours, string key, double positionLimit)
            {
                if (ours == null) return;
                JsonElement theirs = image.Value.GetProperty(key);
                double position = WorldDistance(ours, theirs, false), shape = WorldDistance(ours, theirs, true);
                TestContext.Out.WriteLine($"{accelerators} {image.Name} {part} world: shape within {shape * 1000:0.0} mm, position {position * 1000:0.0} mm on average");
                if (shape >= 0.01 || position >= positionLimit) failures.Add($"{image.Name} {part} world: {shape * 1000:0.0} / {position * 1000:0.0} mm");
            }
            CheckWorld("pose", result.Pose.WorldLandmarks, "poseWorld", 0.015);
            CheckWorld("left hand", result.LeftHand?.WorldLandmarks, "leftHandWorld", 0.025);
            CheckWorld("right hand", result.RightHand?.WorldLandmarks, "rightHandWorld", 0.025);
            if (result.Face != null)
            {
                float[] wanted = image.Value.GetProperty("blendshapes").EnumerateArray().Select(e => e.GetSingle()).ToArray();
                double worst = Enumerable.Range(0, FaceTracker.BlendshapeCount).Max(k => Math.Abs(result.Face.Blendshapes[k] - wanted[k]));
                TestContext.Out.WriteLine($"{accelerators} {image.Name} expressions within {worst:0.000}");
                if (worst >= 0.1) failures.Add($"{image.Name} expressions: {worst:0.000}");
            }
            Assert.That(result.LeftHand?.Handedness ?? "Left", Is.EqualTo("Left"));
            Assert.That(result.RightHand?.Handedness ?? "Right", Is.EqualTo("Right"));
        }
        Assert.That(failures, Is.Empty);
        // How long a frame takes (the sample picture, after a first run).
        using (var original = UnitTestHolisticTracker.HolisticVariant(source, "original"))
        {
            Emgu.LiteRT.Models.HolisticImageInput.Track(tracker, original);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Emgu.LiteRT.Models.HolisticImageInput.Track(tracker, original);
            TestContext.Out.WriteLine($"{accelerators}: {watch.Elapsed.TotalMilliseconds / 10:0.0} ms a frame (2 threads)");
        }
        // An image without a person has none.
        using var shuttle = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "space_shuttle.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(Emgu.LiteRT.Models.HolisticImageInput.Track(tracker, shuttle), Is.Null);
    }

    // The detector downloads, unpacks and runs the models. It only tries the GPU where CocoDetector.CanTryGpu (Android, Mac Catalyst), so this
    // build checks the CPU path; on the phones the GPU wasn't faster and the CPU was chosen (HOLISTIC.md).
    [Test]
    public async Task TheDetectorFindsTheBodyHandsAndFace()
    {
        string root = AppContext.BaseDirectory;
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Emgu.LiteRT.Tests", "HolisticDetector");
        using var detector = new HolisticDetector(cache,
            name => Task.FromResult(Emgu.CV.CvInvoke.Imread(Path.Combine(root, name), Emgu.CV.CvEnum.ImreadModes.ColorBgr)));
        await detector.PrepareAsync(true, 2, text => TestContext.Out.WriteLine(text), CancellationToken.None);
        TestContext.Out.WriteLine($"Backend {detector.Backend}; {detector.GpuFailure}");
        if (CocoDetector.CanTryGpu)
            Assert.That(detector.Backend == "GPU" || detector.GpuFailure != null, Is.True, "The GPU is used or the reason it isn't is given.");
        else
            Assert.That((detector.Backend, detector.GpuFailure), Is.EqualTo(("CPU", (string)null)));
        using var source = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "holistic_person.png"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        HolisticResult result = detector.Detect(source);
        Assert.That(result?.LeftHand != null && result.RightHand != null && result.Face != null, Is.True);
        Assert.That(detector.LastTimings.Pose + detector.LastTimings.Hands + detector.LastTimings.Face, Is.GreaterThan(0));
    }

    [Test]
    public async Task UnpacksTheSevenModels()
    {
        string[] models = await ModelsAsync();
        Assert.That(models.Select(Path.GetFileName), Is.EqualTo(HolisticModelFiles.ModelNames));
        Assert.That(models, Is.All.Matches<string>(File.Exists));
    }
}
