//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite
{
    // Hands in a photo: 21 landmarks each, drawn as a skeleton, and MediaPipe's gesture (closed fist, open palm, pointing up, thumb
    // up or down, victory, "I love you"). The first step towards a hand-gesture visual agent (Emgu.LiteRT.Models/HAND_GESTURES.md).
    public class HandGesturePage : DemoPage
    {
        private HandGestureDetector _detector;
        private readonly Picker _backend;

        // The bones: the thumb, the four fingers (each from the palm), and the palm's edge.
        private static readonly int[][] Bones =
        {
            new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 },
            new[] { 0, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 8 },
            new[] { 5, 9 }, new[] { 9, 10 }, new[] { 10, 11 }, new[] { 11, 12 },
            new[] { 9, 13 }, new[] { 13, 14 }, new[] { 14, 15 }, new[] { 15, 16 },
            new[] { 13, 17 }, new[] { 17, 18 }, new[] { 18, 19 }, new[] { 19, 20 }, new[] { 0, 17 }
        };

        public HandGesturePage()
            : base(
                "Hand gestures",
                "Find hands and name the gesture.",
                Theme.GlyphDetect,
                "Finds hands with MediaPipe's palm detector, reads their 21 landmarks and names the gesture (closed fist, open palm, pointing up, thumb up, thumb down, victory, I love you) with MediaPipe's gesture recognizer, all through LiteRT. The models (8 MB, Apache-2.0) are downloaded from Google on first use. A hand held sideways isn't recognised as a gesture, as in MediaPipe.",
                new[]
                {
                    new Sample("hand_thumb_up.jpg", "Thumb up", Theme.GlyphPets),
                    new Sample("hand_victory.jpg", "Victory"),
                    new Sample("hand_fist.jpg", "Fist"),
                    new Sample("hand_pointing_up.jpg", "Pointing up"),
                    new Sample("hand_pointing_up_rotated.jpg", "Sideways")
                },
                0,
                "Find Hands",
                "Hands")
        {
            _backend = new Picker { Title = "Backend", FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
            // The CPU by default: for two models this small it was as fast or faster than the GPU in a whole frame on the
            // Galaxy S10 and the Pixel Fold (Emgu.LiteRT.Models/HAND_GESTURES.md); the GPU is a choice.
            _backend.Items.Add("CPU");
            if (CocoDetector.CanTryGpu) _backend.Items.Add("GPU (CPU fallback)");
            _backend.SelectedIndex = 0;
            _backend.SelectedIndexChanged += (_, _) => ReleaseModel();
            AddOptionsCard(Theme.Card(_backend, 14));
        }

        protected override void ReleaseModel()
        {
            _detector?.Dispose();
            _detector = null;
        }

        protected override bool SupportsLiveCamera => true;

        protected override string AnalyzingText => "Finding hands...";

        protected override void SetOptionsEnabled(bool enabled) => _backend.IsEnabled = enabled;

        protected override async Task PrepareAsync()
        {
            _detector ??= DetectorFactory.Hand();
            await _detector.PrepareAsync(_backend.SelectedIndex == 1, CpuThreadSettings.Count, ShowProgress, CancellationToken.None);
        }

        protected override DemoResult Analyze(Mat input, Mat render)
        {
            Stopwatch watch = Stopwatch.StartNew();
            var hands = _detector.Detect(input);
            watch.Stop();
            DemoResult demo = new DemoResult
            {
                Summary = $"LiteRT {_detector.Backend} · {watch.ElapsedMilliseconds} ms" + (_detector.GpuFailure != null ? " · GPU unavailable, using CPU" : "")
            };
            MCvScalar bone = new MCvScalar(247, 123, 61), joint = new MCvScalar(255, 255, 255);
            foreach (Hand hand in hands)
            {
                System.Drawing.Point P(int i) => new System.Drawing.Point((int)(hand.Landmarks[i][0] * render.Width), (int)(hand.Landmarks[i][1] * render.Height));
                foreach (int[] b in Bones)
                    CvInvoke.Line(render, P(b[0]), P(b[1]), bone, 2);
                for (int i = 0; i < HandTracker.LandmarkCount; i++)
                    CvInvoke.Circle(render, P(i), 3, joint, -1);
                string name = hand.Gesture == "None" ? "no gesture" : hand.Gesture.Replace('_', ' ');
                // The label with a dark outline, so it can be read over any photo.
                var at = new System.Drawing.Point(Math.Max(4, P(0).X - 60), Math.Min(render.Height - 8, P(0).Y + 24));
                string label = $"{name} {hand.GestureScore * 100:0}%";
                CvInvoke.PutText(render, label, at, FontFace.HersheyDuplex, 0.6, new MCvScalar(0, 0, 0), 3);
                CvInvoke.PutText(render, label, at, FontFace.HersheyDuplex, 0.6, new MCvScalar(255, 255, 255), 1);
                demo.Rows.Add(new ResultRow(name, $"{hand.GestureScore * 100:0}% · {hand.Handedness.ToLowerInvariant()} hand", hand.GestureScore));
            }
            if (hands.Count == 0)
                demo.Rows.Add(new ResultRow("No hand found", ""));
            return demo;
        }
    }
}
