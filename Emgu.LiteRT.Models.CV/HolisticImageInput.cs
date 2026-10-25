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
// With a HolisticTracking it runs as MediaPipe's video mode (holistic_pose_tracking.cc, holistic_hand_tracking.cc, holistic_face_tracking.cc).
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
    internal static HolisticResult Track(HolisticTracker tracker, Mat bgr, out HolisticTimings timings) =>
        Track(tracker, bgr, null, default, out timings);

    /// <summary>
    /// The person in a BGR image with their hands and face, or null if no body is found; with <paramref name="tracking"/>, as a frame of a video at
    /// <paramref name="time"/> (later than the last frame's): the regions come from the last frame where MediaPipe's video mode takes them from
    /// there, and the body is smoothed.
    /// </summary>
    internal static HolisticResult Track(HolisticTracker tracker, Mat bgr, HolisticTracking tracking, TimeSpan time, out HolisticTimings timings)
    {
        timings = new HolisticTimings();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        int width = bgr.Width, height = bgr.Height;
        // The body: from the last frame's region while it is tracked (the detector is skipped), else from the detector.
        HandRect previousBody = tracking?.Body;
        if (tracking != null) tracking.Body = null;
        PoseDetection detection = null;
        HandRect bodyRect = previousBody;
        if (bodyRect == null)
        {
            PoseImageInput.FillDetector(tracker.Pose, bgr);
            detection = tracker.DetectPose(width, height);
            if (detection == null) return Lost(tracking, timings, watch);
            bodyRect = PoseTracker.RectOf(detection, width, height);
        }
        timings.PoseDetected = detection != null;
        PoseImageInput.FillLandmarks(tracker.Pose, bgr, bodyRect);
        Pose pose = tracker.ReadPose(detection, bodyRect);
        if (pose == null) return Lost(tracking, timings, watch);
        if (tracking != null) pose = tracking.Smooth(pose, time, width, height);
        timings.Pose = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        Hand leftHand = null, rightHand = null;
        foreach (bool left in new[] { true, false })
        {
            float[][] previous = tracking == null ? null : left ? tracking.LeftHand : tracking.RightHand;
            Hand hand = null;
            HandRect crop = HolisticTracker.HandRectFromPose(pose.Landmarks, left, width, height);
            if (crop != null)
            {
                WarpInto(bgr, crop, HolisticTracker.RefinementSize, BorderType.Replicate, tracker.RefinementInput, 1.0 / 255, 0);
                HandRect handRect = tracker.RefineHand(crop, width, height);
                if (previous != null)
                {
                    HandRect last = HolisticTracker.HandRectFromLandmarks(previous, width, height);
                    if (HolisticTracker.KeepTracking(last, previous, handRect, width, height, HolisticTracker.HandTrackingRotation,
                            HolisticTracker.HandTrackingTranslation, HolisticTracker.HandTrackingScale, HolisticTracker.HandTrackingMargin))
                    {
                        handRect = last;
                        timings.HandsTracked++;
                    }
                }
                WarpInto(bgr, handRect, HolisticTracker.HandLandmarkSize, BorderType.Replicate, tracker.HandLandmarkInput, 1.0 / 255, 0);
                hand = tracker.ReadHand(handRect, left, pose);
            }
            if (left) leftHand = hand; else rightHand = hand;
            if (tracking != null)
            {
                if (left) tracking.LeftHand = hand?.Landmarks; else tracking.RightHand = hand?.Landmarks;
            }
        }
        timings.Hands = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        float[][] previousFace = tracking?.Face;
        if (tracking != null) tracking.Face = null;
        HandRect faceCrop = HolisticTracker.FaceRectFromPose(pose.Landmarks, width, height);
        WarpInto(bgr, faceCrop, HolisticTracker.FaceDetectorSize, BorderType.Constant, tracker.FaceDetectorInput, 2.0 / 255, -1);
        FaceDetection faceDetection = tracker.DetectFace(faceCrop);
        Face face = null;
        if (faceDetection != null)
        {
            HandRect faceRect = HolisticTracker.FaceRectFromDetection(faceDetection, width, height);
            if (previousFace != null)
            {
                HandRect last = HolisticTracker.FaceRectFromLandmarks(previousFace, width, height);
                if (HolisticTracker.KeepTracking(last, previousFace, faceRect, width, height, HolisticTracker.FaceTrackingRotation,
                        HolisticTracker.FaceTrackingTranslation, HolisticTracker.FaceTrackingScale, HolisticTracker.FaceTrackingMargin))
                {
                    faceRect = last;
                    timings.FaceTracked = true;
                }
            }
            WarpInto(bgr, faceRect, HolisticTracker.FaceLandmarkSize, BorderType.Replicate, tracker.FaceLandmarkInput, 1.0 / 255, 0);
            face = tracker.ReadFace(faceDetection, faceRect, width, height);
            if (tracking != null) tracking.Face = face?.Landmarks;
        }
        timings.Face = watch.Elapsed.TotalMilliseconds;
        return new HolisticResult(pose, leftHand, rightHand, face);
    }

    // No body this frame: nothing carries to the next (MediaPipe's loopbacks hold only the previous frame's results).
    private static HolisticResult Lost(HolisticTracking tracking, HolisticTimings timings, System.Diagnostics.Stopwatch watch)
    {
        if (tracking != null) tracking.LeftHand = tracking.RightHand = tracking.Face = null;
        timings.Pose = watch.Elapsed.TotalMilliseconds;
        return null;
    }
}

/// <summary>
/// What MediaPipe's holistic video mode carries from one frame to the next: the body's region (from its smoothed auxiliary landmarks), each hand's and
/// the face's last landmarks, and the body's smoothing filters (one-euro filters with MediaPipe's settings, and the visibility's low-pass filter).
/// </summary>
internal sealed class HolisticTracking
{
    // holistic_pose_tracking.cc's settings.
    private readonly LandmarkSmoother _auxiliary = new LandmarkSmoother(0.01, 10, 1);
    private readonly LandmarkSmoother _landmarks = new LandmarkSmoother(0.05, 80, 1, visibilityAlpha: 0.1f);
    private readonly LandmarkSmoother _world = new LandmarkSmoother(0.1, 40, 1, visibilityAlpha: 0.1f, scaleByObject: false);

    /// <summary>The time of the last frame, or null before the first</summary>
    internal TimeSpan? LastTime { get; set; }
    /// <summary>The body's region for the next frame, or null to run the detector</summary>
    internal HandRect Body { get; set; }
    internal float[][] LeftHand { get; set; }
    internal float[][] RightHand { get; set; }
    internal float[][] Face { get; set; }

    /// <summary>Start again: the next frame runs the detector and passes through the filters.</summary>
    internal void Reset()
    {
        LastTime = null;
        Body = null;
        LeftHand = RightHand = Face = null;
        _auxiliary.Reset();
        _landmarks.Reset();
        _world.Reset();
    }

    // The body smoothed, and the next frame's region from its smoothed auxiliary landmarks; the smoothing divides speeds by the body's size from the
    // raw auxiliary landmarks.
    internal Pose Smooth(Pose pose, TimeSpan time, int width, int height)
    {
        double scale = HolisticTracker.PoseScale(pose.AuxiliaryLandmarks, width, height);
        float[][] auxiliary = _auxiliary.Apply(pose.AuxiliaryLandmarks, time, width, height, scale);
        Body = PoseTracker.RectOf(auxiliary[0], auxiliary[1], width, height);
        return HolisticTracker.WithLandmarks(pose, _landmarks.Apply(pose.Landmarks, time, width, height, scale),
            pose.WorldLandmarks == null ? null : _world.Apply(pose.WorldLandmarks, time), auxiliary);
    }
}

/// <summary>How long each part of a holistic frame took, in milliseconds (the image warps included), and what tracking kept.</summary>
public sealed class HolisticTimings
{
    /// <summary>The pose detector (when it ran) and landmark model</summary>
    public double Pose { get; internal set; }
    /// <summary>Both hands: the re-crop and landmark models</summary>
    public double Hands { get; internal set; }
    /// <summary>The face detector, landmark model and blendshapes</summary>
    public double Face { get; internal set; }
    /// <summary>Whether the pose detector ran (false when the body was tracked from the last frame)</summary>
    public bool PoseDetected { get; internal set; }
    /// <summary>How many hands were read in their region from the last frame</summary>
    public int HandsTracked { get; internal set; }
    /// <summary>Whether the face was read in its region from the last frame</summary>
    public bool FaceTracked { get; internal set; }
}
