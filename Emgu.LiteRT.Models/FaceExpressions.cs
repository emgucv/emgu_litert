// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;

namespace Emgu.LiteRT.Models
{
    /// <summary>One expression a face shows in a frame, with how strongly.</summary>
    public sealed class FaceExpressionReading
    {
        /// <summary>One of <see cref="FaceExpressions.Names"/></summary>
        public string Name { get; internal set; }
        /// <summary>
        /// 0..1, scaled so that 0.5 is the expression's threshold: at least 0.5 means it is shown, and a monitor's default confidence of 0.5
        /// fires on it. Below the threshold it falls linearly to 0, above it rises linearly to 1.
        /// </summary>
        public float Score { get; internal set; }
        /// <summary>The value read from the blendshapes before scaling (e.g. the mean of the two smile scores)</summary>
        public float Strength { get; internal set; }
    }

    /// <summary>
    /// Simple expressions read from a <see cref="Face"/>'s 52 blendshape scores (FACE.md): smiling, eyes closed, winking, mouth open and
    /// eyebrows raised. The thresholds were set on synthetic faces drawn by <see cref="ZImageTurbo"/> for each expression (a neutral face, a
    /// broad smile, closed eyes, a surprised open mouth - whose brows went up - and a wink; three seeds each) - for example a broad smile scores
    /// 0.95 and a neutral face 0, closed eyes at least 0.76 and a smile's squint at most 0.35. They are rules of thumb for triggers ("take a photo when I
    /// smile"), for one face looking at the camera. The model cards rule out surveillance, identification and life-critical use: nothing here
    /// tells who a face is, and "eyes closed" is not a drowsiness or attention measure.
    /// </summary>
    public static class FaceExpressions
    {
        /// <summary>The names, in the order of the visual agent's classes</summary>
        public static readonly string[] Names = { "smiling", "eyes closed", "winking", "mouth open", "eyebrows raised" };

        /// <summary>The mean of the two smile scores at which a face counts as smiling</summary>
        public const float SmileThreshold = 0.5f;
        /// <summary>The smaller of the two blink scores at which both eyes count as closed</summary>
        public const float EyesClosedThreshold = 0.6f;
        /// <summary>The difference between the two blink scores at which a face counts as winking (with the closed eye at least <see cref="WinkClosed"/>)</summary>
        public const float WinkThreshold = 0.25f;
        /// <summary>The blink score the winking eye needs: a wink drawn by Z-Image scored only 0.52-0.54, as a squint takes part of it</summary>
        public const float WinkClosed = 0.45f;
        /// <summary>The jaw's opening score at which the mouth counts as open</summary>
        public const float MouthOpenThreshold = 0.4f;
        /// <summary>The brows' score (the inner brows, or the mean of the outer ones, whichever is higher) at which they count as raised</summary>
        public const float BrowsRaisedThreshold = 0.45f;

        /// <summary>Readings scoring less than this are left out of <see cref="Classify(float[], float)"/>'s result</summary>
        public const float DefaultMinScore = 0.25f;

        /// <summary>The expressions of a face, or none if it has no blendshapes.</summary>
        public static List<FaceExpressionReading> Classify(Face face, float minScore = DefaultMinScore)
        {
            if (face == null) throw new ArgumentNullException(nameof(face));
            return face.Blendshapes == null ? new List<FaceExpressionReading>() : Classify(face.Blendshapes, minScore);
        }

        /// <summary>
        /// The expressions scoring at least <paramref name="minScore"/> (see <see cref="FaceExpressionReading.Score"/>), in the order of
        /// <see cref="Names"/>, from 52 blendshape scores in the order of <see cref="FaceTracker.BlendshapeNames"/>.
        /// </summary>
        public static List<FaceExpressionReading> Classify(float[] blendshapes, float minScore = DefaultMinScore)
        {
            if (blendshapes == null || blendshapes.Length != FaceTracker.BlendshapeCount)
                throw new ArgumentException("Expected 52 blendshape scores.", nameof(blendshapes));
            float B(string name) => blendshapes[FaceTracker.IndexOfBlendshape(name)];

            float smile = (B("mouthSmileLeft") + B("mouthSmileRight")) / 2;
            float blinkLeft = B("eyeBlinkLeft"), blinkRight = B("eyeBlinkRight");
            float closed = Math.Min(blinkLeft, blinkRight);
            // A wink is one eye closed and the other open; the side doesn't matter (the camera may be mirrored).
            float wink = Math.Max(blinkLeft, blinkRight) >= WinkClosed ? Math.Abs(blinkLeft - blinkRight) : 0;
            float jaw = B("jawOpen");
            float brows = Math.Max(B("browInnerUp"), (B("browOuterUpLeft") + B("browOuterUpRight")) / 2);

            var result = new List<FaceExpressionReading>();
            void Add(string name, float strength, float threshold)
            {
                float score = Scale(strength, threshold);
                if (score >= minScore) result.Add(new FaceExpressionReading { Name = name, Score = score, Strength = strength });
            }
            Add("smiling", smile, SmileThreshold);
            Add("eyes closed", closed, EyesClosedThreshold);
            Add("winking", wink, WinkThreshold);
            Add("mouth open", jaw, MouthOpenThreshold);
            Add("eyebrows raised", brows, BrowsRaisedThreshold);
            return result;
        }

        /// <summary>A strength mapped to 0..1 so that the threshold is 0.5: linear from 0 to the threshold, and from it to 1.</summary>
        public static float Scale(float strength, float threshold)
        {
            if (threshold <= 0 || threshold >= 1) throw new ArgumentOutOfRangeException(nameof(threshold));
            float v = Math.Max(0f, Math.Min(1f, strength));
            return v < threshold ? 0.5f * v / threshold : 0.5f + 0.5f * (v - threshold) / (1 - threshold);
        }

        /// <summary>The box of a face's landmarks as (left, top, right, bottom) fractions of the image, clipped to it.</summary>
        public static float[] BoxOf(Face face)
        {
            if (face?.Landmarks == null || face.Landmarks.Length == 0) return new float[] { 0, 0, 0, 0 };
            float left = 1, top = 1, right = 0, bottom = 0;
            foreach (float[] p in face.Landmarks)
            {
                left = Math.Min(left, p[0]); right = Math.Max(right, p[0]);
                top = Math.Min(top, p[1]); bottom = Math.Max(bottom, p[1]);
            }
            if (right < left) return new float[] { 0, 0, 0, 0 };
            return new[] { Math.Max(0, left), Math.Max(0, top), Math.Min(1, right), Math.Min(1, bottom) };
        }
    }
}
