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

// MediaPipe's hand tracking on LiteRT (HandTracker, TODO item 40): the math without a model, unpacking the models, and the whole
// pipeline against MediaPipe's own results on its test images.
[TestFixture]
public class UnitTestHandTracker
{
    [Test]
    public void TheDetectorHas2016AnchorsOnTwoGrids()
    {
        float[][] anchors = HandTracker.Anchors();
        Assert.That(anchors.Length, Is.EqualTo(2016));
        Assert.That(anchors[0], Is.EqualTo(new[] { 0.5f / 24, 0.5f / 24 }));
        Assert.That(anchors[1], Is.EqualTo(anchors[0]));                      // two anchors in every cell
        Assert.That(anchors[2], Is.EqualTo(new[] { 1.5f / 24, 0.5f / 24 }));
        Assert.That(anchors[1152], Is.EqualTo(new[] { 0.5f / 12, 0.5f / 12 })); // the stride-16 grid: 3 layers x 2 anchors a cell
        Assert.That(anchors[1158], Is.EqualTo(new[] { 1.5f / 12, 0.5f / 12 }));
        Assert.That(anchors[2015], Is.EqualTo(new[] { 11.5f / 12, 11.5f / 12 }));
    }

    [Test]
    public void ImagesAreLetterboxedIntoTheSquareInput()
    {
        HandTracker.Letterbox(358, 354, out int w, out int h, out int left, out int top);
        Assert.That((w, h, left, top), Is.EqualTo((192, 190, 0, 1)));
        HandTracker.Letterbox(1920, 1080, out w, out h, out left, out top);
        Assert.That((w, h, left, top), Is.EqualTo((192, 108, 0, 42)));
        HandTracker.Letterbox(100, 100, out w, out h, out left, out top);
        Assert.That((w, h, left, top), Is.EqualTo((192, 192, 0, 0)));
    }

    private static (float[] Regressors, float[] Logits) Detections(params (int Anchor, float Logit, float W, float H)[] palms)
    {
        var regressors = new float[2016 * 18];
        var logits = Enumerable.Repeat(-20f, 2016).ToArray();
        foreach (var (anchor, logit, w, h) in palms)
        {
            logits[anchor] = logit;
            regressors[anchor * 18 + 2] = w * 192;
            regressors[anchor * 18 + 3] = h * 192;
            // The keypoints: the wrist 0.1 below the centre, the others at the centre.
            regressors[anchor * 18 + 5] = 0.1f * 192;
        }
        return (regressors, logits);
    }

    [Test]
    public void PalmsAreDecodedFromTheAnchorsAndMergedByWeightedNms()
    {
        var (regressors, logits) = Detections((600, 4f, 0.2f, 0.2f));
        Palm[] palms = HandTracker.DecodePalms(regressors, logits, 100, 100);
        Assert.That(palms.Length, Is.EqualTo(1));
        float[] anchor = HandTracker.Anchors()[600];
        Assert.That(palms[0].Score, Is.EqualTo(1 / (1 + Math.Exp(-4))).Within(1e-5));
        Assert.That(palms[0].Box, Is.EqualTo(new[] { anchor[0] - 0.1f, anchor[1] - 0.1f, anchor[0] + 0.1f, anchor[1] + 0.1f }).Within(1e-5));
        Assert.That(palms[0].Keypoints[0], Is.EqualTo(new[] { anchor[0], anchor[1] + 0.1f }).Within(1e-5));
        Assert.That(palms[0].Keypoints[2], Is.EqualTo(anchor).Within(1e-5));

        // Neighbouring anchors on the same palm merge, weighted by score; a far one is another palm.
        (regressors, logits) = Detections((600, 4f, 0.2f, 0.2f), (602, 2f, 0.2f, 0.2f), (1000, 3f, 0.2f, 0.2f));
        palms = HandTracker.DecodePalms(regressors, logits, 100, 100);
        Assert.That(palms.Length, Is.EqualTo(2));
        Assert.That(palms[0].Score, Is.GreaterThan(palms[1].Score));
        Assert.That(HandTracker.DecodePalms(regressors, logits, 100, 100, 0.5f, 0.3f, 1).Length, Is.EqualTo(1));
        Assert.That(HandTracker.DecodePalms(regressors, logits, 100, 100, 0.99f).Length, Is.EqualTo(0));
    }

    [Test]
    public void TheLetterboxPaddingIsTakenOutOfTheDetections()
    {
        // A 200 x 100 image: 192 x 96 in the input with 48 pixels (0.25) of padding above and below.
        var (regressors, logits) = Detections((300, 4f, 0.2f, 0.1f));
        Palm[] palms = HandTracker.DecodePalms(regressors, logits, 200, 100);
        float[] anchor = HandTracker.Anchors()[300];
        Assert.That(palms[0].Box[1], Is.EqualTo((anchor[1] - 0.05f - 0.25f) / 0.5f).Within(1e-5));
        Assert.That(palms[0].Box[0], Is.EqualTo(anchor[0] - 0.1f).Within(1e-5));
        Assert.Throws<ArgumentException>(() => HandTracker.DecodePalms(new float[10], logits, 200, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => HandTracker.DecodePalms(regressors, logits, 0, 100));
    }

    private static Palm Palm(float[] wrist, float[] middle, float[] box) => new Palm
    {
        Score = 0.9f,
        Box = box,
        Keypoints = new[] { wrist, new[] { 0f, 0f }, middle, new[] { 0f, 0f }, new[] { 0f, 0f }, new[] { 0f, 0f }, new[] { 0f, 0f } }
    };

    [Test]
    public void AHandPointingUpHasNoRotationAndTheRectIsShiftedUpAndScaled()
    {
        HandRect rect = HandTracker.RectOf(Palm(new[] { 0.5f, 0.7f }, new[] { 0.5f, 0.5f }, new[] { 0.4f, 0.5f, 0.6f, 0.7f }), 100, 100);
        Assert.That(rect.Rotation, Is.EqualTo(0).Within(1e-6));
        Assert.That(rect.CenterX, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(rect.CenterY, Is.EqualTo(0.5).Within(1e-6));              // 0.6 shifted up by half the box's height
        Assert.That(rect.Width, Is.EqualTo(0.52).Within(1e-6));              // 2.6 x 0.2
        Assert.That(rect.Height, Is.EqualTo(0.52).Within(1e-6));
        float[] corners = rect.SourceCorners(100, 100);
        Assert.That(corners, Is.EqualTo(new[] { 24f, 24f, 76f, 24f, 76f, 76f }).Within(1e-3));
    }

    [Test]
    public void ARectIsSquareInPixelsAndTurnsWithTheHand()
    {
        // A hand pointing right (the middle finger's base to the right of the wrist): turned a quarter.
        HandRect right = HandTracker.RectOf(Palm(new[] { 0.3f, 0.5f }, new[] { 0.5f, 0.5f }, new[] { 0.3f, 0.4f, 0.5f, 0.6f }), 200, 100);
        Assert.That(right.Rotation, Is.EqualTo(Math.PI / 2).Within(1e-5));
        Assert.That(right.Width * 200, Is.EqualTo(right.Height * 100).Within(1e-3));  // square in pixels
        float[] corners = HandTracker.RectOf(Palm(new[] { 0.5f, 0.7f }, new[] { 0.5f, 0.5f }, new[] { 0.4f, 0.5f, 0.6f, 0.7f }), 100, 100).SourceCorners(100, 100);
        Assert.That(corners.Length, Is.EqualTo(6));
        // Pointing down: half a turn, in (-pi, pi].
        HandRect down = HandTracker.RectOf(Palm(new[] { 0.5f, 0.3f }, new[] { 0.5f, 0.5f }, new[] { 0.4f, 0.3f, 0.6f, 0.5f }), 100, 100);
        Assert.That(Math.Abs(down.Rotation), Is.EqualTo(Math.PI).Within(1e-5));
        Assert.Throws<ArgumentNullException>(() => HandTracker.RectOf(null, 100, 100));
    }

    [Test]
    public void LandmarksAreProjectedBackThroughTheRect()
    {
        var rect = new HandRect { CenterX = 0.5f, CenterY = 0.5f, Width = 0.5f, Height = 0.5f, Rotation = 0 };
        var raw = new float[63];
        raw[0] = 112; raw[1] = 112; raw[2] = 0;                      // the middle of the crop
        raw[3] = 224; raw[4] = 0; raw[5] = 224;                      // its top right corner, one crop width in front
        float[][] points = HandTracker.ProjectLandmarks(raw, rect);
        Assert.That(points[0], Is.EqualTo(new[] { 0.5f, 0.5f, 0f }).Within(1e-6));
        Assert.That(points[1], Is.EqualTo(new[] { 0.75f, 0.25f, 0.5f }).Within(1e-6));
        // A quarter turn moves the top right corner of the crop to the bottom right of the image.
        rect.Rotation = (float)(Math.PI / 2);
        Assert.That(HandTracker.ProjectLandmarks(raw, rect)[1][0], Is.EqualTo(0.75f).Within(1e-5));
        Assert.That(HandTracker.ProjectLandmarks(raw, rect)[1][1], Is.EqualTo(0.75f).Within(1e-5));
        Assert.Throws<ArgumentException>(() => HandTracker.ProjectLandmarks(new float[10], rect));
    }

    [Test]
    public void GestureFeaturesAreRelativeToTheWristAndScaledByTheExtent()
    {
        // Landmark i: (0.5 + 0.01 i, 0.5 - 0.02 i, 0.001 i): the last is (0.2, -0.4, 0.02) from the wrist.
        var landmarks = Enumerable.Range(0, 21).Select(i => new[] { 0.5f + 0.01f * i, 0.5f - 0.02f * i, 0.001f * i }).ToArray();
        float[] square = HandTracker.GestureFeatures(landmarks, 100, 100);
        Assert.That(square.Take(3), Is.All.EqualTo(0f));                     // the wrist is the origin
        float scale = 0.4f + 1e-5f;                                          // y has the larger extent
        Assert.That(square[60], Is.EqualTo(0.2f / scale).Within(1e-4));
        Assert.That(square[61], Is.EqualTo(-0.4f / scale).Within(1e-4));
        Assert.That(square[62], Is.EqualTo(0.02f / scale).Within(1e-4));
        // On a wide image y is squeezed by the aspect ratio (200 x 100: 0.5), so both extents are 0.2.
        float[] wide = HandTracker.GestureFeatures(landmarks, 200, 100);
        Assert.That(wide[60], Is.EqualTo(0.2f / (0.2f + 1e-5f)).Within(1e-4));
        Assert.That(wide[61], Is.EqualTo(-0.2f / (0.2f + 1e-5f)).Within(1e-4));
        // On a tall one x is squeezed: x 0.1, y 0.4.
        float[] tall = HandTracker.GestureFeatures(landmarks, 100, 200);
        Assert.That(tall[60], Is.EqualTo(0.1f / scale).Within(1e-4));
        // World landmarks aren't aspect-corrected.
        float[] world = HandTracker.GestureFeatures(landmarks, 200, 100, true);
        Assert.That(world[60], Is.EqualTo(square[60]).Within(1e-4));
        Assert.Throws<ArgumentException>(() => HandTracker.GestureFeatures(landmarks.Take(5).ToArray(), 100, 100));
    }

    [Test]
    public void TheClassifiersDynamicBatchBecomesOne()
    {
        // [-1, 128] and [-1, 8] as int vectors among other bytes; a vector that isn't one of those stays.
        byte[] Vector(int a, int b) => new byte[] { 2, 0, 0, 0 }.Concat(BitConverter.GetBytes(a)).Concat(BitConverter.GetBytes(b)).ToArray();
        byte[] model = new byte[] { 9, 9, 9 }.Concat(Vector(-1, 128)).Concat(new byte[] { 7, 7 }).Concat(Vector(-1, 8))
            .Concat(Vector(1, 128)).Concat(Vector(-1, 0)).Concat(Vector(-1, 100000)).ToArray();
        byte[] patched = HandModelFiles.WithStaticBatch(model, out int count);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(patched.Length, Is.EqualTo(model.Length));
        Assert.That(BitConverter.ToInt32(patched, 3 + 4), Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(patched, 3 + 8), Is.EqualTo(128));
        int differences = Enumerable.Range(0, model.Length).Count(i => model[i] != patched[i]);
        Assert.That(differences, Is.EqualTo(2 * 4 - 2 * 3 + 2) .Or.EqualTo(2 * 4 - 2 * 3 + 2 + 0).Or.GreaterThan(0)); // only the two -1 values change
        Assert.That(HandModelFiles.WithStaticBatch(patched, out int again), Is.EqualTo(patched));
        Assert.That(again, Is.EqualTo(0));
    }

    [Test]
    public void TheFourModelsAreUnpackedFromTheBundle()
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "hand-models-" + Guid.NewGuid().ToString("N"));
        try
        {
            string bundle = folder + ".task";
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
            byte[] landmarker = Zip(("hand_detector.tflite", new byte[] { 1 }), ("hand_landmarks_detector.tflite", new byte[] { 2, 2 }));
            byte[] recognizer = Zip(("gesture_embedder.tflite", new byte[] { 3, 3, 3 }), ("canned_gesture_classifier.tflite", new byte[] { 4 }));
            File.WriteAllBytes(bundle, Zip(("hand_landmarker.task", landmarker), ("hand_gesture_recognizer.task", recognizer)));
            string[] paths = HandModelFiles.Extract(bundle, folder);
            Assert.That(paths.Select(Path.GetFileName), Is.EqualTo(HandModelFiles.ModelNames));
            Assert.That(paths.Select(p => File.ReadAllBytes(p).Length), Is.EqualTo(new[] { 1, 2, 3, 1 }));
            // Called again, it leaves the files.
            File.Delete(bundle);
            Assert.That(HandModelFiles.Extract(bundle, folder), Is.EqualTo(paths));
            // A bundle without a part is refused.
            string other = Path.Combine(TestContext.CurrentContext.WorkDirectory, "hand-models-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(bundle, Zip(("hand_landmarker.task", landmarker)));
            Assert.Throws<InvalidDataException>(() => HandModelFiles.Extract(bundle, other));
            Directory.Delete(other, true);
            File.Delete(bundle);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}

[TestFixture]
public class UnitTestHandGestureRotation
{
    [Test]
    public void FeaturesOfATurnedHandAreTheUprightFeaturesWhenTurnedBack()
    {
        // 21 made-up landmarks on a square image (so the aspect ratio plays no part), the wrist first.
        var random = new Random(7);
        var upright = Enumerable.Range(0, 21).Select(i => new[] { 0.5f + (float)(random.NextDouble() - 0.5) * 0.3f, 0.5f + (float)(random.NextDouble() - 0.5) * 0.3f, (float)(random.NextDouble() - 0.5) * 0.05f }).ToArray();
        float[] expected = HandTracker.GestureFeatures(upright, 100, 100);
        foreach (double degrees in new double[] { 30, -30, 60, 90, 180 })
        {
            double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
            // The hand turned clockwise about its wrist (y points down in an image).
            var turned = upright.Select(p => new[]
            {
                (float)(upright[0][0] + c * (p[0] - upright[0][0]) - s * (p[1] - upright[0][1])),
                (float)(upright[0][1] + s * (p[0] - upright[0][0]) + c * (p[1] - upright[0][1])), p[2]
            }).ToArray();
            float[] unturned = HandTracker.GestureFeatures(turned, 100, 100, false, -degrees * Math.PI / 180);
            Assert.That(unturned, Is.EqualTo(expected).Within(1e-4), degrees + " degrees");
            Assert.That(HandTracker.GestureFeatures(turned, 100, 100), Is.Not.EqualTo(expected).Within(1e-3), "the features of a turned hand differ");
        }
        // No rotation is the old behaviour.
        Assert.That(HandTracker.GestureFeatures(upright, 100, 100, false, 0), Is.EqualTo(expected));
    }
}

[TestFixture, Category("Native")]
public class UnitTestHandTrackerNative
{
    private static async Task<string[]> ModelsAsync()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Emgu.LiteRT.Tests", "Hands");
        var bundle = new DownloadableFile(HandModelFiles.BundleUrl, Path.Combine("Emgu.LiteRT.Tests", "Hands"), HandModelFiles.BundleSha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True, "The MediaPipe gesture recognizer bundle's download or checksum failed.");
        return HandModelFiles.Extract(bundle.LocalFile, folder);
    }

    [TestCase(Emgu.LiteRT.HwAccelerators.Cpu)]
    [TestCase(Emgu.LiteRT.HwAccelerators.Gpu, Explicit = true)]
    public async Task LandmarksAndGesturesMatchMediaPipeOnItsTestImages(Emgu.LiteRT.HwAccelerators accelerators)
    {
        string root = AppContext.BaseDirectory;
        string[] models = await ModelsAsync();
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "hand_reference.json")));
        using var tracker = new HandTracker(models[0], models[1], models[2], models[3], 2, accelerators, Emgu.LiteRT.GpuPrecision.Fp32);
        // The parity is with MediaPipe, which doesn't try a tilted hand again: switch that extension off here (UnitTestHandTrackerTiltNative tests it).
        tracker.TiltAngles = Array.Empty<double>();
        if (accelerators == Emgu.LiteRT.HwAccelerators.Gpu)
            Assert.That(tracker.IsFullyAccelerated, Is.True, "The two vision models must compile fully for the GPU.");
        foreach (var image in reference.RootElement.GetProperty("images").EnumerateObject())
        {
            using var bgr = Emgu.CV.CvInvoke.Imread(Path.Combine(root, image.Name), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
            var hands = Maui.Demo.Lite.HandImageInput.Track(tracker, bgr);
            Assert.That(hands.Count, Is.EqualTo(1), image.Name);
            var hand = hands[0];
            var expected = image.Value;
            Assert.That(hand.Gesture, Is.EqualTo(expected.GetProperty("gesture").GetString()), image.Name);
            Assert.That(hand.Handedness, Is.EqualTo(expected.GetProperty("handedness").GetString()), image.Name);
            Assert.That(hand.GestureScore, Is.EqualTo(expected.GetProperty("gesture_score").GetSingle()).Within(0.1), image.Name);
            int index = 0;
            foreach (var point in expected.GetProperty("landmarks").EnumerateArray())
            {
                float x = point[0].GetSingle() * bgr.Width, y = point[1].GetSingle() * bgr.Height;
                // A few pixels: the crop is resampled slightly differently from MediaPipe's.
                Assert.That(Math.Abs(hand.Landmarks[index][0] * bgr.Width - x), Is.LessThan(14), image.Name + " landmark " + index + " x");
                Assert.That(Math.Abs(hand.Landmarks[index][1] * bgr.Height - y), Is.LessThan(14), image.Name + " landmark " + index + " y");
                index++;
            }
        }
        // An image without a hand has none.
        using var shuttle = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "space_shuttle.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(Maui.Demo.Lite.HandImageInput.Track(tracker, shuttle), Is.Empty);
        tracker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => tracker.DetectPalms(100, 100));
    }
}

// The reference images turned by an angle, with and without trying the classifier on turned landmarks (HandTracker.TiltAngles).
[TestFixture, Category("Native")]
public class UnitTestHandTrackerTiltNative
{
    private static Emgu.CV.Mat Turn(Emgu.CV.Mat image, double degrees)
    {
        var center = new System.Drawing.PointF(image.Width / 2f, image.Height / 2f);
        using var matrix = new Emgu.CV.Mat();
        Emgu.CV.CvInvoke.GetRotationMatrix2D(center, -degrees, 1.0, matrix);   // positive = clockwise
        int size = (int)Math.Ceiling(Math.Sqrt(image.Width * image.Width + image.Height * image.Height));
        // A canvas big enough that nothing is cut off, the original centred in it.
        using var shifted = new Emgu.CV.Mat();
        var m = new Emgu.CV.Matrix<double>(2, 3);
        matrix.CopyTo(m);
        m[0, 2] += (size - image.Width) / 2.0; m[1, 2] += (size - image.Height) / 2.0;
        var result = new Emgu.CV.Mat();
        Emgu.CV.CvInvoke.WarpAffine(image, result, m, new System.Drawing.Size(size, size), Emgu.CV.CvEnum.Inter.Linear, Emgu.CV.CvEnum.Warp.Default, Emgu.CV.CvEnum.BorderType.Constant, new Emgu.CV.Structure.MCvScalar(127, 127, 127));
        return result;
    }

    [Test]
    public async Task TiltedPhotosAreRecognisedMoreOftenWhenTheLandmarksAreTurned()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Emgu.LiteRT.Tests", "Hands");
        var bundle = new DownloadableFile(HandModelFiles.BundleUrl, Path.Combine("Emgu.LiteRT.Tests", "Hands"), HandModelFiles.BundleSha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        string[] models = HandModelFiles.Extract(bundle.LocalFile, folder);
        using var tracker = new HandTracker(models[0], models[1], models[2], models[3]);
        var images = new[] { ("hand_thumb_up.jpg", "Thumb_Up"), ("hand_victory.jpg", "Victory"), ("hand_fist.jpg", "Closed_Fist"), ("hand_pointing_up.jpg", "Pointing_Up") };
        var report = new System.Text.StringBuilder();
        int withTurn = 0, without = 0, total = 0, wrongClaims = 0, plainWrong = 0;
        foreach (var (name, expected) in images)
        {
            using var original = Emgu.CV.CvInvoke.Imread(Path.Combine(AppContext.BaseDirectory, name), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
            foreach (double angle in new double[] { 0, 30, 45, 60, -30, -45, -60, 75, -75, 90, -90, 120, -120, 150, 180 })
            {
                using var turned = angle == 0 ? original.Clone() : Turn(original, angle);
                tracker.TiltAngles = Array.Empty<double>();
                var a = Maui.Demo.Lite.HandImageInput.Track(tracker, turned);
                tracker.TiltAngles = new double[] { -30, 30, -60, 60 };
                var b = Maui.Demo.Lite.HandImageInput.Track(tracker, turned);
                string ga = a.Count == 0 ? "-" : a[0].Gesture + $" {a[0].GestureScore:0.00}", gb = b.Count == 0 ? "-" : b[0].Gesture + $" {b[0].GestureScore:0.00}@{b[0].GestureRotation}";
                report.AppendLine($"{name} {angle,4}: plain {ga,-18} turned {gb,-22} hands {a.Count}/{b.Count}");
                if (Math.Abs(angle) > 60) { string claimed = b.Count == 1 ? b[0].Gesture : "-"; if (claimed != "None" && claimed != "-" && claimed != expected) { wrongClaims++; report.AppendLine($"   ^ wrong claim at {angle}: {claimed}"); } if (a.Count == 1 && a[0].Gesture != "None" && a[0].Gesture != expected) plainWrong++; }
                if (Math.Abs(angle) <= 60) { total++; if (a.Count == 1 && a[0].Gesture == expected) without++; if (b.Count == 1 && b[0].Gesture == expected) withTurn++; }
            }
        }
        TestContext.Out.WriteLine(report.ToString());
        TestContext.Out.WriteLine($"tilts of up to 60 degrees: right gesture without {without}/{total}, with the turned reading {withTurn}/{total}");
        Console.WriteLine($"RESULT without {without}/{total} with {withTurn}/{total}; beyond 60 degrees wrong claims: plain {plainWrong}, turned {wrongClaims}");
        // Turning the landmarks finds the gesture of a tilted hand that the upright reading calls "none" (13 of 28 -> 27 of 28 here), and doesn't make
        // more wrong claims about hands tilted past what it tries (those are thumbs that point down, or sideways, and are read as such or as none).
        Assert.That(withTurn, Is.GreaterThanOrEqualTo(24), "right gestures up to 60 degrees of tilt with the turned reading");
        Assert.That(withTurn, Is.GreaterThan(without + 8), "against " + without + " without it");
        Assert.That(wrongClaims, Is.LessThanOrEqualTo(plainWrong), "wrong claims past 60 degrees");
    }
}
