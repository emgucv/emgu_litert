// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Cuts and scales images for FaceTracker (shared with the native regression test): the image letterboxed into the detector's 128 x 128
// input as float32 RGB scaled to -1..1 with the padding 0 (MediaPipe's BORDER_ZERO, in the tensor's range), and the rotated region of a
// face warped into the landmark model's 256 x 256 input, 0..1, its edges repeated (MediaPipe's default BORDER_REPLICATE).
internal static class FaceImageInput
{
    internal static void FillDetector(FaceTracker tracker, Mat bgr)
    {
        const int size = FaceTracker.DetectorSize;
        FaceTracker.Letterbox(bgr.Width, bgr.Height, out int width, out int height, out int left, out int top);
        using var resized = new Mat();
        CvInvoke.Resize(bgr, resized, new System.Drawing.Size(width, height), 0, 0, Inter.Linear);
        using var rgb = new Mat();
        CvInvoke.CvtColor(resized, rgb, ColorConversion.Bgr2Rgb);
        IntPtr pixels = tracker.DetectorInput.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            input.SetTo(new MCvScalar(0, 0, 0));
            using var region = new Mat(input, new System.Drawing.Rectangle(left, top, width, height));
            rgb.ConvertTo(region, DepthType.Cv32F, 2.0 / 255, -1);
        }
        finally { tracker.DetectorInput.Unlock(); }
    }

    internal static void FillLandmarks(FaceTracker tracker, Mat bgr, HandRect rect)
    {
        const int size = FaceTracker.LandmarkSize;
        float[] c = rect.SourceCorners(bgr.Width, bgr.Height);
        // As MediaPipe's OpenCV converter does: the region's corners go to the input's corners (0 and size), with no half-pixel adjustment.
        System.Drawing.PointF[] source = { new System.Drawing.PointF(c[0], c[1]), new System.Drawing.PointF(c[2], c[3]), new System.Drawing.PointF(c[4], c[5]) };
        System.Drawing.PointF[] target = { new System.Drawing.PointF(0, 0), new System.Drawing.PointF(size, 0), new System.Drawing.PointF(size, size) };
        using var transform = CvInvoke.GetAffineTransform(source, target);
        using var warped = new Mat();
        CvInvoke.WarpAffine(bgr, warped, transform, new System.Drawing.Size(size, size), Inter.Linear, Warp.Default, BorderType.Replicate);
        IntPtr pixels = tracker.LandmarkInput.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            using var rgb = new Mat();
            CvInvoke.CvtColor(warped, rgb, ColorConversion.Bgr2Rgb);
            rgb.ConvertTo(input, DepthType.Cv32F, 1.0 / 255, 0);
        }
        finally { tracker.LandmarkInput.Unlock(); }
    }

    /// <summary>Find the faces in a BGR image (one by default), best first, each with its 478 landmarks and, if the tracker has them, 52 expression scores.</summary>
    internal static List<Face> Track(FaceTracker tracker, Mat bgr, int maxFaces = 1, float threshold = 0.5f)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        FillDetector(tracker, bgr);
        var faces = new List<Face>();
        foreach (FaceDetection detection in tracker.Detect(bgr.Width, bgr.Height, threshold, maxFaces))
        {
            HandRect rect = FaceTracker.RectOf(detection, bgr.Width, bgr.Height);
            FillLandmarks(tracker, bgr, rect);
            Face face = tracker.Read(detection, rect, bgr.Width, bgr.Height);
            if (face != null)
                faces.Add(face);
        }
        return faces;
    }
}
