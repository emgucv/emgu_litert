// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// Cuts and scales images for HandTracker (shared with the native regression test): the image letterboxed into the palm detector's
// 192 x 192 input, and the rotated region of a hand warped into the landmark model's 224 x 224 input - float32 RGB, 0..1.
internal static class HandImageInput
{
    internal static void FillDetector(HandTracker tracker, Mat bgr)
    {
        HandTracker.Letterbox(bgr.Width, bgr.Height, out int width, out int height, out int left, out int top);
        using var resized = new Mat();
        CvInvoke.Resize(bgr, resized, new System.Drawing.Size(width, height), 0, 0, Inter.Linear);
        using var canvas = new Mat(HandTracker.DetectorSize, HandTracker.DetectorSize, DepthType.Cv8U, 3);
        canvas.SetTo(new MCvScalar(0, 0, 0));
        using (var region = new Mat(canvas, new System.Drawing.Rectangle(left, top, width, height)))
            resized.CopyTo(region);
        Write(tracker.DetectorInput, canvas, HandTracker.DetectorSize);
    }

    internal static void FillLandmarks(HandTracker tracker, Mat bgr, HandRect rect)
    {
        const int size = HandTracker.LandmarkSize;
        float[] c = rect.SourceCorners(bgr.Width, bgr.Height);
        // OpenCV's pixel centres are at integer coordinates, while the corners are pixel edges: half a pixel on each side.
        System.Drawing.PointF[] source = { new System.Drawing.PointF(c[0] - 0.5f, c[1] - 0.5f), new System.Drawing.PointF(c[2] - 0.5f, c[3] - 0.5f), new System.Drawing.PointF(c[4] - 0.5f, c[5] - 0.5f) };
        System.Drawing.PointF[] target = { new System.Drawing.PointF(-0.5f, -0.5f), new System.Drawing.PointF(size - 0.5f, -0.5f), new System.Drawing.PointF(size - 0.5f, size - 0.5f) };
        using var transform = CvInvoke.GetAffineTransform(source, target);
        using var warped = new Mat();
        CvInvoke.WarpAffine(bgr, warped, transform, new System.Drawing.Size(size, size), Inter.Linear, Warp.Default, BorderType.Constant);
        Write(tracker.LandmarkInput, warped, size);
    }

    // BGR bytes into the buffer as float32 RGB / 255.
    private static void Write(TensorBuffer buffer, Mat bgr, int size)
    {
        IntPtr pixels = buffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            using var rgb = new Mat();
            CvInvoke.CvtColor(bgr, rgb, ColorConversion.Bgr2Rgb);
            rgb.ConvertTo(input, DepthType.Cv32F, 1.0 / 255);
        }
        finally { buffer.Unlock(); }
    }

    /// <summary>Find the hands in a BGR image, best palm first, each with its landmarks and gesture.</summary>
    // The palm detector's score a candidate needs. MediaPipe's 0.5 never found a closed fist in front of a real camera (TODO item 47); the landmark
    // model's presence score (0.5) still rejects candidates that aren't hands.
    internal const float PalmThreshold = 0.35f;

    internal static List<Hand> Track(HandTracker tracker, Mat bgr, int maxHands = 2, float threshold = PalmThreshold)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        FillDetector(tracker, bgr);
        var hands = new List<Hand>();
        foreach (Palm palm in tracker.DetectPalms(bgr.Width, bgr.Height, threshold, maxHands))
        {
            HandRect rect = HandTracker.RectOf(palm, bgr.Width, bgr.Height);
            FillLandmarks(tracker, bgr, rect);
            Hand hand = tracker.ReadHand(palm, rect, bgr.Width, bgr.Height);
            if (hand != null)
                hands.Add(hand);
        }
        return hands;
    }
}
