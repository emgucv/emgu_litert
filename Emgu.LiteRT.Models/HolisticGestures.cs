// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>One thing a person is doing in a frame, read from a <see cref="HolisticResult"/>, with a score from 0 to 1 (0.5 at the threshold).</summary>
    public sealed class HolisticReading
    {
        /// <summary>A posture (<see cref="PoseGestures.Names"/>), an expression (<see cref="FaceExpressions.Names"/>), an interaction
        /// (<see cref="HolisticGestures.InteractionNames"/>) or a combination of a posture and an expression ("hand raised and smiling")</summary>
        public string Name { get; internal set; }
        /// <summary>0..1, 0.5 at the threshold</summary>
        public float Score { get; internal set; }
    }

    /// <summary>
    /// What the body, hands and face of a <see cref="HolisticResult"/> show together (HOLISTIC.md): the postures of <see cref="PoseGestures"/> and the
    /// expressions of <see cref="FaceExpressions"/>, each posture combined with each expression ("hand raised and smiling", the lower of the two
    /// scores), and three things only the hands and face together can show - covering the eyes, a hand over the mouth, a hand on the face. Those are
    /// geometry in pixels, measured in eye distances: both eyes or the mouth (the pose's landmarks, which stay when a hand hides the face) near a bone of
    /// a hand, or a hand's landmarks inside the outline of the pose's head. Rules of thumb for fun triggers (peekaboo), from synthetic
    /// pictures: like the face and pose models' cards, nothing here identifies anyone or judges attention or tiredness.
    /// </summary>
    public static class HolisticGestures
    {
        /// <summary>What only the hands and face together show</summary>
        public static readonly string[] InteractionNames = { "covering eyes", "hand over mouth", "hand on face" };

        /// <summary>Every posture combined with every expression, posture first ("hand raised and smiling")</summary>
        public static readonly string[] CombinationNames =
            PoseGestures.Names.SelectMany(p => FaceExpressions.Names.Select(e => Combine(p, e))).ToArray();

        /// <summary>The name of a posture combined with an expression</summary>
        public static string Combine(string posture, string expression) => posture + " and " + expression;

        /// <summary>How near a bone of a hand (the segments between its landmarks: fingers and the palm's edges) both eyes must be, in eye distances
        /// (the pose's eyes 2 and 5): the synthetic covered eyes were within 0.13, an eye beside a hand 0.42 or more</summary>
        public const double EyesCoveredDistance = 0.25;
        /// <summary>How near a bone of a hand the middle of the mouth must be, in eye distances: covered mouths were within 0.13, a hand cradling the
        /// chin (whose outline does span the mouth) 0.26 in MediaPipe's landmarks - but 0.13 in a JPEG copy through this tracker, so such a hand, its
        /// fingers by the mouth, can read as covering it</summary>
        public const double MouthCoveredDistance = 0.18;
        /// <summary>The distance either side of those two thresholds over which a score goes from 0.5 to 0 or 1, in eye distances</summary>
        public const double DistanceSpan = 0.15;
        /// <summary>How far inside the outline of the pose's head landmarks (0-10) a hand's deepest landmark must be, in eye distances: hands over
        /// the face were 0.16-0.50 inside, hands raised beside the face 0.20 or more outside</summary>
        public const double HandOnFaceDepth = 0.05;
        /// <summary>The depth either side of that threshold over which the score goes from 0.5 to 0 or 1, in eye distances</summary>
        public const double DepthSpan = 0.4;

        // The hand's bones: the thumb, the four fingers (each from the palm), and the palm's edge.
        private static readonly int[][] Bones =
        {
            new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 0, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 8 },
            new[] { 5, 9 }, new[] { 9, 10 }, new[] { 10, 11 }, new[] { 11, 12 }, new[] { 9, 13 }, new[] { 13, 14 }, new[] { 14, 15 }, new[] { 15, 16 },
            new[] { 13, 17 }, new[] { 17, 18 }, new[] { 18, 19 }, new[] { 19, 20 }, new[] { 0, 17 }
        };

        private const int LeftEye = 2, RightEye = 5, MouthLeft = 9, MouthRight = 10;

        /// <summary>
        /// Everything the person in <paramref name="result"/> shows, in an image of this size: postures (with waving from <paramref name="wave"/>,
        /// when given), expressions, interactions and the combinations. A reading is returned when its score is at least <paramref name="minScore"/>.
        /// </summary>
        public static List<HolisticReading> Classify(HolisticResult result, int imageWidth, int imageHeight, PoseGestures.WaveDetector wave = null,
            TimeSpan time = default, float minScore = 0.25f)
        {
            if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            var readings = new List<HolisticReading>();
            PoseReading waving = wave?.Update(time, result?.Pose?.Landmarks, imageWidth, imageHeight);
            if (result?.Pose == null) return readings;
            var postures = PoseGestures.Classify(result.Pose.Landmarks, imageWidth, imageHeight).Select(p => (p.Name, p.Score)).ToList();
            if (waving != null) postures.Add((waving.Name, waving.Score));
            var expressions = result.Face == null ? new List<(string Name, float Score)>()
                : FaceExpressions.Classify(result.Face, 0).Select(e => (e.Name, e.Score)).ToList();
            foreach (var (name, score) in postures.Concat(expressions))
                readings.Add(new HolisticReading { Name = name, Score = score });
            foreach (var (name, score) in Interactions(result, imageWidth, imageHeight))
                readings.Add(new HolisticReading { Name = name, Score = score });
            foreach (var p in postures)
                foreach (var e in expressions)
                    readings.Add(new HolisticReading { Name = Combine(p.Name, e.Name), Score = Math.Min(p.Score, e.Score) });
            readings.RemoveAll(r => r.Score < minScore);
            return readings;
        }

        /// <summary>The scores of <see cref="InteractionNames"/> (0 when a part they need wasn't found).</summary>
        public static IEnumerable<(string Name, float Score)> Interactions(HolisticResult result, int imageWidth, int imageHeight)
        {
            float[][] pose = result?.Pose?.Landmarks;
            if (pose == null) yield break;
            double[] P(float[] p) => new[] { p[0] * (double)imageWidth, p[1] * (double)imageHeight };
            double[] leftEye = P(pose[LeftEye]), rightEye = P(pose[RightEye]);
            double eyes = Math.Sqrt(Math.Pow(leftEye[0] - rightEye[0], 2) + Math.Pow(leftEye[1] - rightEye[1], 2));
            var hands = new[] { result.LeftHand, result.RightHand }.Where(h => h != null).Select(h => h.Landmarks.Select(P).ToArray()).ToList();
            if (eyes < 1 || hands.Count == 0)
            {
                foreach (string name in InteractionNames) yield return (name, 0f);
                yield break;
            }
            // The distance to the nearest bone of either hand, not to the hand's outline: a hand cradling the chin has the mouth inside its outline,
            // between the thumb and the fingers, but not under them.
            double Near(double[] point) => hands.Min(h => Bones.Min(b => Segment(point, h[b[0]], h[b[1]]))) / eyes;
            double[] mouth = { (P(pose[MouthLeft])[0] + P(pose[MouthRight])[0]) / 2, (P(pose[MouthLeft])[1] + P(pose[MouthRight])[1]) / 2 };
            // The face's outline is the pose's head (nose, eyes, ears, mouth): the face model often finds no face when hands hide it (none of the synthetic
            // pictures of covered eyes had one), while the pose keeps its head landmarks.
            List<double[]> face = Hull(pose.Take(11).Select(P));
            double onFace = hands.Max(h => h.Max(p => Depth(face, p))) / eyes;
            yield return ("covering eyes", Score(DistanceSpan, EyesCoveredDistance - Math.Max(Near(leftEye), Near(rightEye))));
            yield return ("hand over mouth", Score(DistanceSpan, MouthCoveredDistance - Near(mouth)));
            yield return ("hand on face", Score(DepthSpan, onFace - HandOnFaceDepth));
        }

        // 0.5 at the threshold (margin 0), rising to 1 a span beyond it and falling to 0 a span short of it.
        private static float Score(double span, double margin) => (float)Math.Max(0, Math.Min(1, 0.5 + margin / (2 * span)));

        private static double Segment(double[] p, double[] a, double[] b)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], length = dx * dx + dy * dy;
            double t = length > 0 ? Math.Max(0, Math.Min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / length)) : 0;
            return Math.Sqrt(Math.Pow(p[0] - a[0] - t * dx, 2) + Math.Pow(p[1] - a[1] - t * dy, 2));
        }

        // The convex hull of points (Andrew's monotone chain), counter-clockwise in image coordinates' sense of "turn".
        internal static List<double[]> Hull(IEnumerable<double[]> points)
        {
            var p = points.OrderBy(q => q[0]).ThenBy(q => q[1]).ToList();
            if (p.Count < 3) return p;
            double Cross(double[] o, double[] a, double[] b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
            var hull = new List<double[]>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (double[] q in pass == 0 ? p : Enumerable.Reverse(p))
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(q);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        // The signed distance from a point to a convex polygon's outline: positive inside, negative outside (as OpenCV's pointPolygonTest).
        internal static double Depth(List<double[]> hull, double[] point)
        {
            if (hull.Count == 0) return double.NegativeInfinity;
            double nearest = double.MaxValue;
            bool inside = hull.Count >= 3;
            for (int i = 0; i < hull.Count; i++)
            {
                double[] a = hull[i], b = hull[(i + 1) % hull.Count];
                double dx = b[0] - a[0], dy = b[1] - a[1], length = dx * dx + dy * dy;
                double t = length > 0 ? Math.Max(0, Math.Min(1, ((point[0] - a[0]) * dx + (point[1] - a[1]) * dy) / length)) : 0;
                nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(point[0] - a[0] - t * dx, 2) + Math.Pow(point[1] - a[1] - t * dy, 2)));
                // Inside a counter-clockwise hull every edge has the point on its left.
                if (dx * (point[1] - a[1]) - dy * (point[0] - a[0]) < 0) inside = false;
            }
            return inside ? nearest : -nearest;
        }
    }
}
