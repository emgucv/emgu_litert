// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Cuts and scales images for PoseTracker (shared with the native regression test): the image letterboxed into the detector's 224 x 224
// input as float32 RGB scaled to -1..1, and the rotated region of a body warped into the landmark model's 256 x 256 input, 0..1.
internal static class PoseImageInput
{
    internal static void FillDetector(PoseTracker tracker, Mat bgr)
    {
        PoseTracker.Letterbox(bgr.Width, bgr.Height, out int width, out int height, out int left, out int top);
        using var resized = new Mat();
        CvInvoke.Resize(bgr, resized, new System.Drawing.Size(width, height), 0, 0, Inter.Linear);
        using var canvas = new Mat(PoseTracker.DetectorSize, PoseTracker.DetectorSize, DepthType.Cv8U, 3);
        canvas.SetTo(new MCvScalar(0, 0, 0));
        using (var region = new Mat(canvas, new System.Drawing.Rectangle(left, top, width, height)))
            resized.CopyTo(region);
        Write(tracker.DetectorInput, canvas, PoseTracker.DetectorSize, 2.0 / 255, -1);
    }

    internal static void FillLandmarks(PoseTracker tracker, Mat bgr, HandRect rect)
    {
        const int size = PoseTracker.LandmarkSize;
        float[] c = rect.SourceCorners(bgr.Width, bgr.Height);
        // OpenCV's pixel centres are at integer coordinates, while the corners are pixel edges: half a pixel on each side.
        System.Drawing.PointF[] source = { new System.Drawing.PointF(c[0] - 0.5f, c[1] - 0.5f), new System.Drawing.PointF(c[2] - 0.5f, c[3] - 0.5f), new System.Drawing.PointF(c[4] - 0.5f, c[5] - 0.5f) };
        System.Drawing.PointF[] target = { new System.Drawing.PointF(-0.5f, -0.5f), new System.Drawing.PointF(size - 0.5f, -0.5f), new System.Drawing.PointF(size - 0.5f, size - 0.5f) };
        using var transform = CvInvoke.GetAffineTransform(source, target);
        using var warped = new Mat();
        CvInvoke.WarpAffine(bgr, warped, transform, new System.Drawing.Size(size, size), Inter.Linear, Warp.Default, BorderType.Constant);
        Write(tracker.LandmarkInput, warped, size, 1.0 / 255, 0);
    }

    // BGR bytes into the buffer as float32 RGB * scale + shift.
    private static void Write(TensorBuffer buffer, Mat bgr, int size, double scale, double shift)
    {
        IntPtr pixels = buffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            using var rgb = new Mat();
            CvInvoke.CvtColor(bgr, rgb, ColorConversion.Bgr2Rgb);
            rgb.ConvertTo(input, DepthType.Cv32F, scale, shift);
        }
        finally { buffer.Unlock(); }
    }

    /// <summary>Find the people in a BGR image (one by default), best first, each with its 33 landmarks.</summary>
    internal static List<Pose> Track(PoseTracker tracker, Mat bgr, int maxPoses = 1, float threshold = 0.5f)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        FillDetector(tracker, bgr);
        var poses = new List<Pose>();
        foreach (PoseDetection detection in tracker.Detect(bgr.Width, bgr.Height, threshold, maxPoses))
        {
            HandRect rect = PoseTracker.RectOf(detection, bgr.Width, bgr.Height);
            FillLandmarks(tracker, bgr, rect);
            Pose pose = tracker.Read(detection, rect);
            if (pose != null)
                poses.Add(pose);
        }
        return poses;
    }
}
