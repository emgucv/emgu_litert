// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>One thing a body is doing in a frame, with how sure that is (the least visibility of the landmarks it was read from).</summary>
    public sealed class PoseReading
    {
        /// <summary>One of <see cref="PoseGestures.Names"/></summary>
        public string Name { get; internal set; }
        /// <summary>0..1</summary>
        public float Score { get; internal set; }
    }

    /// <summary>
    /// Simple postures read from a <see cref="Pose"/>'s 33 landmarks (TODO item 52, POSE.md): arms raised, a hand raised, standing, sitting and
    /// lying down - from one frame - and (<see cref="WaveDetector"/>) waving, which needs several. They are geometry on the landmarks, in
    /// pixels so the image's shape doesn't matter, with the camera upright: "up" is up in the image. A reading needs all the landmarks it
    /// uses to be visible (at least <c>minVisibility</c>), so a person cut off at the waist is never standing or sitting. This is a rule of
    /// thumb for triggers, not a measurement: the model card rules out life-critical use, so nothing here is a fall detector.
    /// </summary>
    public static class PoseGestures
    {
        /// <summary>The names, in the order of the visual agent's classes</summary>
        public static readonly string[] Names = { "arms raised", "hand raised", "waving", "standing", "sitting", "lying down" };

        private const int Nose = 0, LeftShoulder = 11, RightShoulder = 12, LeftWrist = 15, RightWrist = 16,
            LeftHip = 23, RightHip = 24, LeftKnee = 25, RightKnee = 26, LeftAnkle = 27, RightAnkle = 28;

        /// <summary>How far above its shoulder a wrist must be to count as raised, in shoulder widths</summary>
        public const double RaisedAbove = 0.4;

        /// <summary>
        /// The postures of a body in an image of this size. Waving is not here (see <see cref="WaveDetector"/>).
        /// </summary>
        public static List<PoseReading> Classify(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
        {
            if (landmarks == null || landmarks.Length != PoseTracker.LandmarkCount) throw new ArgumentException("Expected 33 landmarks.", nameof(landmarks));
            if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            var result = new List<PoseReading>();
            bool Visible(int i) => landmarks[i][3] >= minVisibility;
            float Vis(params int[] indexes) => indexes.Min(i => landmarks[i][3]);
            double X(int i) => landmarks[i][0] * imageWidth;
            double Y(int i) => landmarks[i][1] * imageHeight;
            double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

            bool shoulders = Visible(LeftShoulder) && Visible(RightShoulder);
            double width = shoulders ? Distance(X(LeftShoulder), Y(LeftShoulder), X(RightShoulder), Y(RightShoulder)) : 0;
            bool torso = shoulders && Visible(LeftHip) && Visible(RightHip);
            double sx = 0, sy = 0, hx = 0, hy = 0, length = 0, lean = 0;
            if (torso)
            {
                sx = (X(LeftShoulder) + X(RightShoulder)) / 2; sy = (Y(LeftShoulder) + Y(RightShoulder)) / 2;
                hx = (X(LeftHip) + X(RightHip)) / 2; hy = (Y(LeftHip) + Y(RightHip)) / 2;
                length = Distance(sx, sy, hx, hy);
                // The angle of the spine from straight up: a body lying across the image is far from it.
                lean = Math.Atan2(Math.Abs(hx - sx), Math.Abs(hy - sy)) * 180 / Math.PI;
            }
            bool lying = torso && length >= 1 && lean > 60;
            // Raised means above the shoulder in an upright body: a lying one doesn't raise a hand.
            if (shoulders && width > 1 && !lying)
            {
                bool left = IsRaised(landmarks, LeftWrist, LeftShoulder, imageHeight, width, minVisibility);
                bool right = IsRaised(landmarks, RightWrist, RightShoulder, imageHeight, width, minVisibility);
                if (left && right)
                    result.Add(new PoseReading { Name = "arms raised", Score = Vis(LeftWrist, RightWrist, LeftShoulder, RightShoulder) });
                else if (left || right)
                    result.Add(new PoseReading { Name = "hand raised", Score = left ? Vis(LeftWrist, LeftShoulder, RightShoulder) : Vis(RightWrist, LeftShoulder, RightShoulder) });
            }

            if (!torso || length < 1) return result;
            float torsoScore = Vis(LeftShoulder, RightShoulder, LeftHip, RightHip);
            if (lean > 60)
            {
                result.Add(new PoseReading { Name = "lying down", Score = torsoScore });
                return result;
            }
            if (lean < 35 && Visible(LeftKnee) && Visible(RightKnee))
            {
                // The thighs along the spine, each leg on its own: standing has both knees a torso's length down the spine from the hips, sitting
                // has both level with the hips (across the spine, under 0.3 of the torso's length: the test photo's lunge reads 0.4-0.5 on its bent leg).
                // A lunge or a step has one of each, so it is neither.
                double ux = (hx - sx) / length, uy = (hy - sy) / length;
                double Drop(int knee, int hip) => ((X(knee) - X(hip)) * ux + (Y(knee) - Y(hip)) * uy) / length;
                double left = Drop(LeftKnee, LeftHip), right = Drop(RightKnee, RightHip);
                float score = Math.Min(torsoScore, Vis(LeftKnee, RightKnee));
                bool ankles = Visible(LeftAnkle) && Visible(RightAnkle);
                if (Math.Min(left, right) > 0.6 && (!ankles || Drop(LeftAnkle, LeftKnee) > 0.2 && Drop(RightAnkle, RightKnee) > 0.2))
                    result.Add(new PoseReading { Name = "standing", Score = score });
                else if (Math.Max(left, right) < 0.3)
                    result.Add(new PoseReading { Name = "sitting", Score = score });
            }
            return result;
        }

        private static bool IsRaised(float[][] landmarks, int wrist, int shoulder, int imageHeight, double shoulderWidth, float minVisibility) =>
            landmarks[wrist][3] >= minVisibility && landmarks[shoulder][3] >= minVisibility &&
            (landmarks[shoulder][1] - landmarks[wrist][1]) * imageHeight > RaisedAbove * shoulderWidth;

        /// <summary>The box (left, top, right, bottom, as fractions of the image) round the landmarks that are visible, clipped to the image.</summary>
        public static float[] BoxOf(float[][] landmarks, float minVisibility = 0.5f)
        {
            float left = 1, top = 1, right = 0, bottom = 0;
            foreach (float[] p in landmarks)
            {
                if (p[3] < minVisibility) continue;
                left = Math.Min(left, p[0]); right = Math.Max(right, p[0]);
                top = Math.Min(top, p[1]); bottom = Math.Max(bottom, p[1]);
            }
            if (right < left) return new float[] { 0, 0, 0, 0 };
            return new[] { Math.Max(0, left), Math.Max(0, top), Math.Min(1, right), Math.Min(1, bottom) };
        }

        /// <summary>
        /// Waving: a wrist above its shoulder that swings from side to side at least twice (three extremes, each at least 0.25 shoulder widths
        /// from the one before) within 2 seconds. Feed it every frame of the same camera; a frame without a person resets it. One detector per
        /// camera, called from one thread.
        /// </summary>
        public sealed class WaveDetector
        {
            private readonly List<(TimeSpan Time, double X)>[] _history = { new List<(TimeSpan, double)>(), new List<(TimeSpan, double)>() };
            private readonly TimeSpan _window = TimeSpan.FromSeconds(2);

            /// <summary>Clear what it has seen.</summary>
            public void Reset() { foreach (var h in _history) h.Clear(); }

            /// <summary>The wave reading for this frame, or null.</summary>
            /// <param name="time">A monotonic time</param>
            /// <param name="landmarks">The 33 landmarks, or null when nobody is in view</param>
            public PoseReading Update(TimeSpan time, float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
            {
                if (landmarks == null) { Reset(); return null; }
                if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
                float Vis(int i) => landmarks[i][3];
                bool shoulders = Vis(LeftShoulder) >= minVisibility && Vis(RightShoulder) >= minVisibility;
                double dx = (landmarks[LeftShoulder][0] - landmarks[RightShoulder][0]) * imageWidth;
                double dy = (landmarks[LeftShoulder][1] - landmarks[RightShoulder][1]) * imageHeight;
                double width = shoulders ? Math.Sqrt(dx * dx + dy * dy) : 0;
                PoseReading best = null;
                int[][] sides = { new[] { LeftWrist, LeftShoulder }, new[] { RightWrist, RightShoulder } };
                for (int side = 0; side < 2; side++)
                {
                    var history = _history[side];
                    int wrist = sides[side][0], shoulder = sides[side][1];
                    bool raised = shoulders && width > 1 && IsRaised(landmarks, wrist, shoulder, imageHeight, width, minVisibility);
                    if (!raised) { history.Clear(); continue; }
                    history.Add((time, landmarks[wrist][0] * imageWidth));
                    history.RemoveAll(h => time - h.Time > _window);
                    if (Swings(history, 0.25 * width) >= 2)
                    {
                        float score = Math.Min(Vis(wrist), Math.Min(Vis(LeftShoulder), Vis(RightShoulder)));
                        if (best == null || score > best.Score) best = new PoseReading { Name = "waving", Score = score };
                    }
                }
                return best;
            }

            // The reversals of a track, ignoring wobbles under the threshold: the first movement sets the direction, then each turn after
            // moving at least that far against it counts.
            private static int Swings(List<(TimeSpan Time, double X)> track, double threshold)
            {
                if (track.Count < 3) return 0;
                int swings = 0, direction = 0;           // 0 not yet moving, 1 right, -1 left
                double low = track[0].X, high = track[0].X, extreme = track[0].X;
                foreach (var (_, x) in track.Skip(1))
                {
                    if (direction == 0)
                    {
                        low = Math.Min(low, x); high = Math.Max(high, x);
                        if (x - low >= threshold) { direction = 1; extreme = x; }
                        else if (high - x >= threshold) { direction = -1; extreme = x; }
                    }
                    else if (direction == 1)
                    {
                        if (x > extreme) extreme = x;
                        else if (extreme - x >= threshold) { swings++; direction = -1; extreme = x; }
                    }
                    else
                    {
                        if (x < extreme) extreme = x;
                        else if (x - extreme >= threshold) { swings++; direction = 1; extreme = x; }
                    }
                }
                return swings;
            }
        }
    }
}
