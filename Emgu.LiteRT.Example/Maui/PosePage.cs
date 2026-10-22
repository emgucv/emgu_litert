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
    // The body in a photo: 33 landmarks drawn as a skeleton, with MediaPipe's pose landmarker (BlazePose). The first step towards pose
    // monitors for the camera agent (POSE.md).
    public class PosePage : DemoPage
    {
        private PoseDetector _detector;
        private readonly Picker _backend, _variant;

        public PosePage()
            : base(
                "Body pose",
                "Find a person and draw their skeleton.",
                Theme.GlyphDetect,
                "Finds one person with MediaPipe's pose detector and reads 33 body landmarks - face, shoulders, elbows, wrists, hips, knees, ankles, feet - through LiteRT. The models (Apache-2.0) are downloaded from Google on first use: lite 5.8 MB, full 9.4 MB, heavy 30.7 MB. It tracks one person whose head is in view and who is within about 4 m, and Google lists surveillance and identification as out of scope: this reads where the joints are, not who someone is.",
                new[] { new Sample("pose_standing.jpg", "Standing", Theme.GlyphPets) },
                0,
                "Find Pose",
                "Pose")
        {
            _variant = new Picker { Title = "Model", FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
            _variant.Items.Add("Lite (5.8 MB)");
            _variant.Items.Add("Full (9.4 MB)");
            _variant.Items.Add("Heavy (30.7 MB)");
            _variant.SelectedIndex = 0;
            _variant.SelectedIndexChanged += (_, _) => ReleaseModel();
            _backend = new Picker { Title = "Backend", FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
            _backend.Items.Add("CPU");
            if (CocoDetector.CanTryGpu) _backend.Items.Add("GPU (CPU fallback)");
            _backend.SelectedIndex = 0;
            _backend.SelectedIndexChanged += (_, _) => ReleaseModel();
            AddOptionsCard(Theme.Card(new VerticalStackLayout { Spacing = 6, Children = { _variant, _backend } }, 14));
        }

        protected override void ReleaseModel()
        {
            _detector?.Dispose();
            _detector = null;
        }

        private PoseModelFiles.Variant Variant => _variant.SelectedIndex == 2 ? PoseModelFiles.Heavy : _variant.SelectedIndex == 1 ? PoseModelFiles.Full : PoseModelFiles.Lite;

        protected override async Task<DemoResult> RunAsync(Mat input)
        {
            _backend.IsEnabled = false; _variant.IsEnabled = false;
            try
            {
                _detector ??= DetectorFactory.Pose();
                await _detector.PrepareAsync(Variant, _backend.SelectedIndex == 1, CpuThreadSettings.Count, ShowProgress, CancellationToken.None);
                ShowProgress("Finding the pose...");
                return await Task.Run(() =>
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    var poses = _detector.Detect(input);
                    watch.Stop();
                    Mat render = input.Clone();
                    DemoResult demo = new DemoResult
                    {
                        Annotated = render,
                        Summary = $"LiteRT {_detector.Backend} · {Variant.Name} · {watch.ElapsedMilliseconds} ms" + (_detector.GpuFailure != null ? " · GPU unavailable, using CPU" : "")
                    };
                    MCvScalar bone = new MCvScalar(247, 123, 61), joint = new MCvScalar(255, 255, 255), faint = new MCvScalar(160, 160, 160);
                    foreach (Pose pose in poses)
                    {
                        float[] L(int i) => pose.Landmarks[i];
                        System.Drawing.Point P(int i) => new System.Drawing.Point((int)(L(i)[0] * render.Width), (int)(L(i)[1] * render.Height));
                        // A landmark the model thinks is hidden is drawn grey.
                        foreach (int[] b in PoseTracker.Bones)
                            CvInvoke.Line(render, P(b[0]), P(b[1]), L(b[0])[3] < 0.5f || L(b[1])[3] < 0.5f ? faint : bone, 2);
                        for (int i = 0; i < PoseTracker.LandmarkCount; i++)
                            CvInvoke.Circle(render, P(i), 3, L(i)[3] < 0.5f ? faint : joint, -1);
                        int visible = pose.Landmarks.Count(p => p[3] >= 0.5f);
                        demo.Rows.Add(new ResultRow("Person", $"{visible} of 33 landmarks visible", pose.Presence));
                    }
                    if (poses.Count == 0)
                        demo.Rows.Add(new ResultRow("No person found", ""));
                    return demo;
                });
            }
            finally { _backend.IsEnabled = true; _variant.IsEnabled = true; }
        }
    }
}
