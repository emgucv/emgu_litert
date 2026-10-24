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
// input as float32 RGB scaled to -1..1, and the rotated region of a body warped into the landmark model's 256 x 256 input, 0..1, its edges
// repeated (MediaPipe's default BORDER_REPLICATE); the landmarks are refined with the model's heatmap, as MediaPipe's pose landmarker does.
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
        // As MediaPipe's OpenCV converter does: the region's corners go to the input's corners (0 and size), with no half-pixel adjustment.
        System.Drawing.PointF[] source = { new System.Drawing.PointF(c[0], c[1]), new System.Drawing.PointF(c[2], c[3]), new System.Drawing.PointF(c[4], c[5]) };
        System.Drawing.PointF[] target = { new System.Drawing.PointF(0, 0), new System.Drawing.PointF(size, 0), new System.Drawing.PointF(size, size) };
        using var transform = CvInvoke.GetAffineTransform(source, target);
        using var warped = new Mat();
        CvInvoke.WarpAffine(bgr, warped, transform, new System.Drawing.Size(size, size), Inter.Linear, Warp.Default, BorderType.Replicate);
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
            Pose pose = tracker.Read(detection, rect, refineWithHeatmap: true);
            if (pose != null)
                poses.Add(pose);
        }
        return poses;
    }
}
