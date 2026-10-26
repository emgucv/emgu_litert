//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// MediaPipe's holistic landmarker (HolisticTracker, Emgu.LiteRT.Models/HOLISTIC.md): one person's body, both hands and face in one pass,
// drawn over the photo or the live camera, with the face's strongest expressions. The live camera tracks from one frame to the next as MediaPipe's video
// mode does (the pose detector skipped, the body smoothed); a photo is read on its own. The GPU backend runs the hand and face models on the GPU when it
// matches the CPU and is faster; the pose models always run on the CPU (they don't compile for the GPU).
public class HolisticPage : DemoPage
{
    private static readonly MCvScalar HandBone = new MCvScalar(80, 200, 120), FaceLine = new MCvScalar(230, 180, 90), Joint = new MCvScalar(255, 255, 255);
    private HolisticDetector _detector;
    private readonly Picker _backend;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public HolisticPage()
        : base(
            "Holistic",
            "Find a person's body, hands and face at once.",
            Theme.GlyphDetect,
            "Finds one person with MediaPipe's holistic landmarker and reads, in one pass, 33 body landmarks, 21 landmarks for each hand and 478 face landmarks with 52 expression scores, through LiteRT. The seven models (13.7 MB, Apache-2.0) are downloaded from Google on first use. It tracks one person whose head is in view, and Google lists surveillance and identification as out of scope: this reads where the body, hands and face are, not who someone is.",
            new[] { new Sample("holistic_person.png", "Person", Theme.GlyphPets) },
            0,
            "Find Body, Hands and Face",
            "Holistic")
    {
        _backend = new Picker { Title = "Backend", FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
        _backend.Items.Add("CPU");
        if (CocoDetector.CanTryGpu) _backend.Items.Add("GPU for hands and face (if faster)");
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

    protected override string AnalyzingText => "Finding the body, hands and face...";

    protected override void SetOptionsEnabled(bool enabled) => _backend.IsEnabled = enabled;

    protected override async Task PrepareAsync()
    {
        _detector ??= DetectorFactory.Holistic();
        await _detector.PrepareAsync(_backend.SelectedIndex == 1, CpuThreadSettings.Count, ShowProgress, CancellationToken.None);
    }

    protected override DemoResult Analyze(Mat input, Mat render) => Show(input, render, false);

    // Live frames go through the video mode, timed by the page's clock (a pause of more than a second starts tracking again).
    protected override DemoResult AnalyzeFrame(Mat input, Mat render) => Show(input, render, true);

    private DemoResult Show(Mat input, Mat render, bool live)
    {
        Stopwatch watch = Stopwatch.StartNew();
        HolisticResult result = live ? _detector.Detect(input, _clock.Elapsed) : _detector.Detect(input);
        watch.Stop();
        HolisticTimings t = _detector.LastTimings;
        DemoResult demo = new DemoResult
        {
            Summary = $"LiteRT {(_detector.Backend == "GPU" ? "CPU + GPU" : "CPU")} · {watch.ElapsedMilliseconds} ms" +
                (live && !t.PoseDetected ? " · tracking" : "") + (_detector.GpuFailure != null ? " · GPU not used" : "")
        };
        if (result == null)
        {
            demo.Rows.Add(new ResultRow("No person found", ""));
            return demo;
        }
        int thickness = Math.Max(1, render.Width / 400);
        if (result.Face != null)
        {
            float[][] f = result.Face.Landmarks;
            System.Drawing.Point F(int i) => new System.Drawing.Point((int)(f[i][0] * render.Width), (int)(f[i][1] * render.Height));
            foreach (int[] c in FaceTracker.Contours)
                CvInvoke.Line(render, F(c[0]), F(c[1]), FaceLine, thickness, LineType.AntiAlias);
        }
        // The pose's head (0-10) and fingers (17-22: pinky, index, thumb) where the face and hand models found them.
        PoseDrawing.Draw(render, result.Pose.Landmarks, i =>
            (i <= 10 && result.Face != null) ||
            (i >= 17 && i <= 22 && (i % 2 == 1 ? result.LeftHand != null : result.RightHand != null)));
        foreach (Hand hand in new[] { result.LeftHand, result.RightHand })
        {
            if (hand == null) continue;
            System.Drawing.Point H(int i) => new System.Drawing.Point((int)(hand.Landmarks[i][0] * render.Width), (int)(hand.Landmarks[i][1] * render.Height));
            foreach (int[] b in HandGesturePage.Bones)
                CvInvoke.Line(render, H(b[0]), H(b[1]), HandBone, 2, LineType.AntiAlias);
            for (int i = 0; i < HandTracker.LandmarkCount; i++)
                CvInvoke.Circle(render, H(i), 2, Joint, -1, LineType.AntiAlias);
        }
        demo.Rows.Add(new ResultRow("Time", $"body {t.Pose:0} · hands {t.Hands:0} · face {t.Face:0} ms"));
        int visible = result.Pose.Landmarks.Count(p => p[3] >= 0.5f);
        demo.Rows.Add(new ResultRow("Body", $"{visible} of 33 landmarks visible", result.Pose.Presence));
        demo.Rows.Add(new ResultRow("Left hand", result.LeftHand != null ? "found" : "not found", result.LeftHand?.Presence ?? -1));
        demo.Rows.Add(new ResultRow("Right hand", result.RightHand != null ? "found" : "not found", result.RightHand?.Presence ?? -1));
        if (result.Face == null)
            demo.Rows.Add(new ResultRow("Face", "not found"));
        else
            // The strongest expressions (without "_neutral").
            foreach (int k in Enumerable.Range(1, FaceTracker.BlendshapeCount - 1).OrderByDescending(k => result.Face.Blendshapes[k]).Take(3))
                demo.Rows.Add(new ResultRow("Face: " + FacePage.Readable(FaceTracker.BlendshapeNames[k]),
                    $"{result.Face.Blendshapes[k] * 100:0}%", result.Face.Blendshapes[k]));
        return demo;
    }
}
