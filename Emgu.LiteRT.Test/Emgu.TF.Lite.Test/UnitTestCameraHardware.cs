// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using NUnit.Framework;
using LiteRtModels = Emgu.LiteRT.Models;

namespace Emgu.TF.Lite.Test;

// Needs a real camera, so it only runs when EMGU_CAMERA_TEST=1: the opening and reading the demo's LiveCamera does
// (VideoCapture 0, 640x480, frames in a loop), then the COCO detector on a captured frame.
[TestFixture, NonParallelizable]
public class UnitTestCameraHardware
{
    [Test, Category("Hardware")]
    public async Task LiveCameraDeliversFramesAndTheDetectorRunsOnThem()
    {
        if (Environment.GetEnvironmentVariable("EMGU_CAMERA_TEST") != "1")
            Assert.Ignore("Set EMGU_CAMERA_TEST=1 to run with the machine's camera.");

        string jpeg = Path.Combine(TestContext.CurrentContext.WorkDirectory, "camera-frame.jpg");
        using (var capture = new VideoCapture(0, VideoCapture.API.Any))
        {
            Assert.That(capture.IsOpened, Is.True, "No camera could be opened.");
            capture.Set(CapProp.FrameWidth, 640);
            capture.Set(CapProp.FrameHeight, 480);
            using var frame = new Mat();
            // The first frames of a webcam are often dark while its exposure settles.
            for (int i = 0; i < 20; i++) capture.Read(frame);
            int frames = 0;
            double brightness = 0;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                Assert.That(capture.Read(frame) && !frame.IsEmpty, Is.True, "The camera stopped delivering frames.");
                frames++;
                brightness = CvInvoke.Mean(frame).V0;
            }
            double fps = frames / watch.Elapsed.TotalSeconds;
            TestContext.Progress.WriteLine($"Camera {frame.Width}x{frame.Height}, {frames} frames in 5 s = {fps:F1} FPS, mean blue {brightness:F0}");
            Assert.That(fps, Is.GreaterThan(5), "Too few frames per second.");
            Assert.That(frame.NumberOfChannels, Is.EqualTo(3));
            CvInvoke.Imwrite(jpeg, frame, new System.Collections.Generic.KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 85));
        }
        Assert.That(new FileInfo(jpeg).Length, Is.GreaterThan(2000), "The captured JPEG is nearly empty.");

        using var detector = new LiteRtModels.CocoSsdMobilenetV3 { NumThreads = 2 };
        await detector.Init();
        var results = detector.Recognize(jpeg, 0.5f);
        TestContext.Progress.WriteLine("Detections: " + (results.Length == 0 ? "none"
            : string.Join(", ", results.Select(r => $"{r.Label} {r.Score:F2}"))));
    }
}
