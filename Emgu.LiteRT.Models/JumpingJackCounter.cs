// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Counts jumping jacks from a <see cref="Pose"/>'s landmarks, frame by frame (POSE.md). A jumping jack is "closed" - both wrists below the
    /// shoulders, feet together - then "open" - both wrists above the shoulders (as <see cref="PoseGestures"/>' arms raised), feet apart - and
    /// closed again; it counts when it closes. The feet are only checked when both ankles are visible, so a camera that sees the person from the
    /// waist up counts the arms alone. Frames between the two positions change nothing, which keeps a wobble near a threshold from counting
    /// twice. Feed it the frames of one camera in order, from one thread; at least ~8 frames a second, since a quick jumping jack is open for
    /// about a quarter of a second.
    /// </summary>
    public sealed class JumpingJackCounter
    {
        private const int LeftShoulder = 11, RightShoulder = 12, LeftWrist = 15, RightWrist = 16, LeftAnkle = 27, RightAnkle = 28;

        /// <summary>The feet's distance apart, in shoulder widths, at or above which they count as apart</summary>
        public const double FeetApart = 1.2;
        /// <summary>The feet's distance apart, in shoulder widths, at or below which they count as together</summary>
        public const double FeetTogether = 1.0;

        /// <summary>The position the person was last seen in</summary>
        public enum Position
        {
            /// <summary>Not seen yet, or the person was lost</summary>
            Unknown,
            /// <summary>Arms down, feet together</summary>
            Closed,
            /// <summary>Arms up, feet apart</summary>
            Open
        }

        /// <summary>The jumping jacks counted so far</summary>
        public int Count { get; private set; }

        /// <summary>The position the person was last seen in</summary>
        public Position Current { get; private set; }

        /// <summary>Start again from 0.</summary>
        public void Reset()
        {
            Count = 0;
            Current = Position.Unknown;
        }

        /// <summary>
        /// Read one frame. Returns true when it completes a jumping jack (<see cref="Count"/> has gone up).
        /// </summary>
        /// <param name="landmarks">The 33 landmarks (x, y as fractions of the image, z, visibility...), or null when nobody is in view: the jumping
        /// jack under way is then dropped, the count kept</param>
        /// <param name="imageWidth">The image's width in pixels</param>
        /// <param name="imageHeight">The image's height in pixels</param>
        /// <param name="minVisibility">The least visibility of a landmark that is used</param>
        public bool Update(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
        {
            if (landmarks == null)
            {
                Current = Position.Unknown;
                return false;
            }
            if (landmarks.Length != PoseTracker.LandmarkCount) throw new ArgumentException("Expected 33 landmarks.", nameof(landmarks));
            if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            Position position = Read(landmarks, imageWidth, imageHeight, minVisibility);
            if (position == Position.Unknown || position == Current)
                return false;
            bool completed = Current == Position.Open && position == Position.Closed;
            Current = position;
            if (completed)
                Count++;
            return completed;
        }

        /// <summary>The position in one frame: Open, Closed, or Unknown for anything in between or a frame without the shoulders and wrists.</summary>
        public static Position Read(float[][] landmarks, int imageWidth, int imageHeight, float minVisibility = 0.5f)
        {
            bool Visible(int i) => landmarks[i][3] >= minVisibility;
            if (!Visible(LeftShoulder) || !Visible(RightShoulder) || !Visible(LeftWrist) || !Visible(RightWrist))
                return Position.Unknown;
            double X(int i) => landmarks[i][0] * imageWidth;
            double Y(int i) => landmarks[i][1] * imageHeight;
            double width = Math.Sqrt(Math.Pow(X(LeftShoulder) - X(RightShoulder), 2) + Math.Pow(Y(LeftShoulder) - Y(RightShoulder), 2));
            if (width < 1)
                return Position.Unknown;
            // How far each wrist is above its shoulder, in shoulder widths.
            double left = (Y(LeftShoulder) - Y(LeftWrist)) / width, right = (Y(RightShoulder) - Y(RightWrist)) / width;
            bool armsUp = left > PoseGestures.RaisedAbove && right > PoseGestures.RaisedAbove;
            bool armsDown = left < 0 && right < 0;
            double? feet = Visible(LeftAnkle) && Visible(RightAnkle) ? Math.Abs(X(LeftAnkle) - X(RightAnkle)) / width : (double?)null;
            if (armsUp && (feet == null || feet >= FeetApart))
                return Position.Open;
            if (armsDown && (feet == null || feet <= FeetTogether))
                return Position.Closed;
            return Position.Unknown;
        }
    }
}
