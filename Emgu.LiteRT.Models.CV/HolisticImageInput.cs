// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Cuts and scales images for HolisticTracker (shared with the native regression test) and runs its steps in order: the pose as PoseImageInput
// does, then each hand (its region from the pose warped into the re-crop model's 256 x 256 input, the corrected region into the landmark model's
// 224 x 224, both 0..1 with the edges repeated - MediaPipe's BORDER_REPLICATE), then the face (its region from the pose into the detector's
// 128 x 128 input, -1..1, black outside the image - BORDER_ZERO; then the face's region into the landmark model's 192 x 192, 0..1, edges repeated).
internal static class HolisticImageInput
{
    // A rotated region of the image warped into a size x size float32 RGB input, value * scale + shift.
    internal static void WarpInto(Mat bgr, HandRect rect, int size, BorderType border, TensorBuffer buffer, double scale, double shift)
    {
        float[] c = rect.SourceCorners(bgr.Width, bgr.Height);
        // As MediaPipe's OpenCV converter does: the region's corners go to the input's corners (0 and size), with no half-pixel adjustment.
        PointF[] source = { new PointF(c[0], c[1]), new PointF(c[2], c[3]), new PointF(c[4], c[5]) };
        PointF[] target = { new PointF(0, 0), new PointF(size, 0), new PointF(size, size) };
        using var transform = CvInvoke.GetAffineTransform(source, target);
        using var warped = new Mat();
        // A constant border is black (MediaPipe's OpenCV converter warps the 8-bit image, then scales: black is the tensor range's minimum).
        CvInvoke.WarpAffine(bgr, warped, transform, new Size(size, size), Inter.Linear, Warp.Default, border, new MCvScalar(0, 0, 0));
        IntPtr pixels = buffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            using var rgb = new Mat();
            CvInvoke.CvtColor(warped, rgb, ColorConversion.Bgr2Rgb);
            rgb.ConvertTo(input, DepthType.Cv32F, scale, shift);
        }
        finally { buffer.Unlock(); }
    }

    /// <summary>The person in a BGR image with their hands and face, or null if no body is found.</summary>
    internal static HolisticResult Track(HolisticTracker tracker, Mat bgr) => Track(tracker, bgr, out _);

    /// <summary>The same, with how long each part took.</summary>
    internal static HolisticResult Track(HolisticTracker tracker, Mat bgr, out HolisticTimings timings)
    {
        timings = new HolisticTimings();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        int width = bgr.Width, height = bgr.Height;
        PoseImageInput.FillDetector(tracker.Pose, bgr);
        PoseDetection detection = tracker.DetectPose(width, height);
        if (detection == null) return null;
        HandRect bodyRect = PoseTracker.RectOf(detection, width, height);
        PoseImageInput.FillLandmarks(tracker.Pose, bgr, bodyRect);
        Pose pose = tracker.ReadPose(detection, bodyRect);
        timings.Pose = watch.Elapsed.TotalMilliseconds;
        if (pose == null) return null;
        watch.Restart();
        Hand leftHand = null, rightHand = null;
        foreach (bool left in new[] { true, false })
        {
            HandRect crop = HolisticTracker.HandRectFromPose(pose.Landmarks, left, width, height);
            if (crop == null) continue;
            WarpInto(bgr, crop, HolisticTracker.RefinementSize, BorderType.Replicate, tracker.RefinementInput, 1.0 / 255, 0);
            HandRect handRect = tracker.RefineHand(crop, width, height);
            WarpInto(bgr, handRect, HolisticTracker.HandLandmarkSize, BorderType.Replicate, tracker.HandLandmarkInput, 1.0 / 255, 0);
            Hand hand = tracker.ReadHand(handRect, left, pose);
            if (left) leftHand = hand; else rightHand = hand;
        }
        timings.Hands = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        HandRect faceCrop = HolisticTracker.FaceRectFromPose(pose.Landmarks, width, height);
        WarpInto(bgr, faceCrop, HolisticTracker.FaceDetectorSize, BorderType.Constant, tracker.FaceDetectorInput, 2.0 / 255, -1);
        FaceDetection faceDetection = tracker.DetectFace(faceCrop);
        Face face = null;
        if (faceDetection != null)
        {
            HandRect faceRect = HolisticTracker.FaceRectFromDetection(faceDetection, width, height);
            WarpInto(bgr, faceRect, HolisticTracker.FaceLandmarkSize, BorderType.Replicate, tracker.FaceLandmarkInput, 1.0 / 255, 0);
            face = tracker.ReadFace(faceDetection, faceRect, width, height);
        }
        timings.Face = watch.Elapsed.TotalMilliseconds;
        return new HolisticResult(pose, leftHand, rightHand, face);
    }
}

/// <summary>How long each part of a holistic frame took, in milliseconds (the image warps included).</summary>
public sealed class HolisticTimings
{
    /// <summary>The pose detector and landmark model</summary>
    public double Pose { get; internal set; }
    /// <summary>Both hands: the re-crop and landmark models</summary>
    public double Hands { get; internal set; }
    /// <summary>The face detector, landmark model and blendshapes</summary>
    public double Face { get; internal set; }
}
