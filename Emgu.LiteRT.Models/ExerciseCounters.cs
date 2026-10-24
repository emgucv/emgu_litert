// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Counts the repetitions of an exercise from a <see cref="Pose"/>'s landmarks, frame by frame (POSE.md): <see cref="JumpingJackCounter"/>,
    /// <see cref="SquatCounter"/>, <see cref="HighKneesCounter"/>. Feed it the frames of one camera in order, from one thread.
    /// </summary>
    public interface IExerciseCounter
    {
        /// <summary>The repetitions counted so far</summary>
        int Count { get; }

        /// <summary>The position read in the last frame, in a word or two ("up", "down", "open"...), or null when it couldn't be read</summary>
        string Phase { get; }

        /// <summary>Start again from 0.</summary>
        void Reset();

        /// <summary>
        /// Read one frame. Returns true when it completes a repetition (<see cref="Count"/> has gone up).
        /// </summary>
        /// <param name="landmarks">The 33 landmarks (x, y as fractions of the image, z, visibility...), or null when nobody is in view: the
        /// repetition under way is then dropped, the count kept</param>
        /// <param name="imageWidth">The image's width in pixels</param>
        /// <param name="imageHeight">The image's height in pixels</param>
        /// <param name="minVisibility">The least visibility of a landmark that is used</param>
        bool Update(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f);
    }

    /// <summary>
    /// Counts squats: standing - the hips well above the knees - then down - the hips near the knees' height - and up again; it counts when
    /// the person stands up. Measured as how high the hip is above the ankle compared with the knee (hip-to-ankle height over knee-to-ankle
    /// height): about 2 standing, 1 with the thighs level. Seen from the front or the side alike, since only heights are compared. Each leg
    /// whose hip, knee and ankle are visible counts, averaged; frames between the two positions change nothing.
    /// </summary>
    public sealed class SquatCounter : IExerciseCounter
    {
        private const int LeftHip = 23, RightHip = 24, LeftKnee = 25, RightKnee = 26, LeftAnkle = 27, RightAnkle = 28;

        /// <summary>The hip-to-ankle over knee-to-ankle height at or above which the person is standing</summary>
        public const double Standing = 1.75;
        /// <summary>The hip-to-ankle over knee-to-ankle height at or below which the person is down in a squat (thighs about 30 degrees from level)</summary>
        public const double Down = 1.45;

        private bool? _down;    // null: not read yet, or lost

        /// <inheritdoc/>
        public int Count { get; private set; }

        /// <inheritdoc/>
        public string Phase { get; private set; }

        /// <inheritdoc/>
        public void Reset()
        {
            Count = 0;
            Phase = null;
            _down = null;
        }

        /// <inheritdoc/>
        public bool Update(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
        {
            double? height = landmarks == null ? null : HipHeight(landmarks, imageWidth, imageHeight, minVisibility);
            if (height == null)
            {
                Phase = null;
                _down = null;
                return false;
            }
            bool? down = height <= Down ? true : height >= Standing ? false : (bool?)null;
            Phase = down == true ? "down" : down == false ? "standing" : "between";
            if (down == null || down == _down)
                return false;
            bool completed = _down == true && down == false;
            _down = down;
            if (completed)
                Count++;
            return completed;
        }

        /// <summary>The hip's height above the ankle over the knee's, averaged over the legs with all three visible; null without one.</summary>
        public static double? HipHeight(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
        {
            if (landmarks.Length != PoseTracker.LandmarkCount) throw new ArgumentException("Expected 33 landmarks.", nameof(landmarks));
            double sum = 0;
            int legs = 0;
            foreach (var (hip, knee, ankle) in new[] { (LeftHip, LeftKnee, LeftAnkle), (RightHip, RightKnee, RightAnkle) })
            {
                if (landmarks[hip][3] < minVisibility || landmarks[knee][3] < minVisibility || landmarks[ankle][3] < minVisibility)
                    continue;
                double shin = (landmarks[ankle][1] - landmarks[knee][1]) * imageHeight;
                if (shin < 2)       // the knee not above the ankle: not standing on this leg
                    continue;
                sum += (landmarks[ankle][1] - landmarks[hip][1]) * imageHeight / shin;
                legs++;
            }
            return legs == 0 ? (double?)null : sum / legs;
        }
    }

    /// <summary>
    /// Counts high knees (running or marching on the spot, knees up): each time a knee comes up to near the hip's height and goes down again,
    /// either leg. A knee is up when it is no more than <see cref="Up"/> torso lengths below its hip, down when at least <see cref="Lowered"/>
    /// below (the torso: from the shoulders' middle to the hips' middle), and up only when it is in front of the hip, no more than
    /// <see cref="MaxSideways"/> torso lengths to the side - legs spread wide (a jumping jack, a lunge) also bring the knees up near the hips'
    /// height. Each leg needs its hip and knee visible, and the torso both shoulders and hips.
    /// </summary>
    public sealed class HighKneesCounter : IExerciseCounter
    {
        private const int LeftShoulder = 11, RightShoulder = 12, LeftHip = 23, RightHip = 24, LeftKnee = 25, RightKnee = 26;

        /// <summary>How far below the hip, in torso lengths, a knee may be and count as up</summary>
        public const double Up = 0.2;
        /// <summary>How far below the hip, in torso lengths, a knee must be to count as down (standing, it is about 0.8)</summary>
        public const double Lowered = 0.5;
        /// <summary>How far to the side of its hip, in torso lengths, a knee may be and count as up</summary>
        public const double MaxSideways = 0.35;

        private readonly bool?[] _up = new bool?[2];

        /// <inheritdoc/>
        public int Count { get; private set; }

        /// <inheritdoc/>
        public string Phase { get; private set; }

        /// <inheritdoc/>
        public void Reset()
        {
            Count = 0;
            Phase = null;
            _up[0] = _up[1] = null;
        }

        /// <inheritdoc/>
        public bool Update(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
        {
            double?[] sideways = new double?[2];
            double?[] drops = landmarks == null ? new double?[2] : KneeDrops(landmarks, imageWidth, imageHeight, minVisibility, out sideways);
            bool completed = false;
            for (int leg = 0; leg < 2; leg++)
            {
                if (drops[leg] == null) { _up[leg] = null; continue; }
                bool? up = drops[leg] <= Up ? (sideways[leg] <= MaxSideways ? true : (bool?)null) : drops[leg] >= Lowered ? false : (bool?)null;
                if (up == null || up == _up[leg]) continue;
                if (_up[leg] == true && up == false) { Count++; completed = true; }
                _up[leg] = up;
            }
            Phase = drops[0] == null && drops[1] == null ? null
                : _up[0] == true && _up[1] == true ? "both knees up"
                : _up[0] == true ? "left knee up" : _up[1] == true ? "right knee up" : "knees down";
            return completed;
        }

        /// <summary>How far each knee (left, right) is below its hip, in torso lengths; null for a leg that can't be read.</summary>
        public static double?[] KneeDrops(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f) =>
            KneeDrops(landmarks, imageWidth, imageHeight, minVisibility, out _);

        /// <summary>How far each knee (left, right) is below its hip, and to its side, in torso lengths; null for a leg that can't be read.</summary>
        public static double?[] KneeDrops(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility, out double?[] sideways)
        {
            sideways = new double?[2];
            if (landmarks.Length != PoseTracker.LandmarkCount) throw new ArgumentException("Expected 33 landmarks.", nameof(landmarks));
            var drops = new double?[2];
            bool Visible(int i) => landmarks[i][3] >= minVisibility;
            if (!Visible(LeftShoulder) || !Visible(RightShoulder) || !Visible(LeftHip) || !Visible(RightHip))
                return drops;
            double MidX(int a, int b) => (landmarks[a][0] + landmarks[b][0]) / 2 * imageWidth;
            double MidY(int a, int b) => (landmarks[a][1] + landmarks[b][1]) / 2 * imageHeight;
            double torso = Math.Sqrt(Math.Pow(MidX(LeftShoulder, RightShoulder) - MidX(LeftHip, RightHip), 2) + Math.Pow(MidY(LeftShoulder, RightShoulder) - MidY(LeftHip, RightHip), 2));
            if (torso < 2)
                return drops;
            foreach (var (leg, hip, knee) in new[] { (0, LeftHip, LeftKnee), (1, RightHip, RightKnee) })
            {
                if (!Visible(knee)) continue;
                drops[leg] = (landmarks[knee][1] - landmarks[hip][1]) * imageHeight / torso;
                sideways[leg] = Math.Abs(landmarks[knee][0] - landmarks[hip][0]) * imageWidth / torso;
            }
            return drops;
        }
    }
}
