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

// MediaPipe's face landmarker (FaceTracker, Emgu.LiteRT.Models/FACE.md): up to two faces, their outlines drawn from the 478 landmarks, and
// the strongest of the 52 expression scores. On a photo or the live camera.
public class FacePage : DemoPage
{
    private FaceDetector _detector;
    private readonly Picker _backend;

    public FacePage()
        : base(
            "Face",
            "Find faces and read their expressions.",
            Theme.GlyphDetect,
            "Finds up to two faces with MediaPipe's face detector, reads 478 face landmarks (the eyes, brows, lips, the face's outline and the irises) and scores 52 expressions - smiles, blinks, an open jaw, raised brows - with MediaPipe's face landmarker, all through LiteRT. The models (3.8 MB, Apache-2.0) are downloaded from Google on first use. They are made for faces near the camera that face it. They read expressions only: they don't recognise or identify anyone, and their model cards rule out surveillance and life-critical uses.",
            new[] { new Sample("face_portrait.png", "Portrait", Theme.GlyphPets) },
            0,
            "Find Faces",
            "Expressions")
    {
        _backend = new Picker { Title = "Backend", FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
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

    protected override string AnalyzingText => "Finding faces...";

    protected override void SetOptionsEnabled(bool enabled) => _backend.IsEnabled = enabled;

    protected override async Task PrepareAsync()
    {
        _detector ??= DetectorFactory.Face();
        await _detector.PrepareAsync(_backend.SelectedIndex == 1, CpuThreadSettings.Count, ShowProgress, CancellationToken.None);
    }

    protected override DemoResult Analyze(Mat input, Mat render)
    {
        Stopwatch watch = Stopwatch.StartNew();
        var faces = _detector.Detect(input, 2);
        watch.Stop();
        DemoResult demo = new DemoResult
        {
            Summary = $"LiteRT {_detector.Backend} · {watch.ElapsedMilliseconds} ms" + (_detector.GpuFailure != null ? " · GPU unavailable, using CPU" : "")
        };
        MCvScalar line = new MCvScalar(247, 123, 61), iris = new MCvScalar(255, 255, 255);
        int thickness = Math.Max(1, render.Width / 400);
        for (int f = 0; f < faces.Count; f++)
        {
            Face face = faces[f];
            System.Drawing.Point P(int i) => new System.Drawing.Point((int)(face.Landmarks[i][0] * render.Width), (int)(face.Landmarks[i][1] * render.Height));
            foreach (int[] c in FaceTracker.Contours)
                CvInvoke.Line(render, P(c[0]), P(c[1]), line, thickness, LineType.AntiAlias);
            // The irises' centres.
            CvInvoke.Circle(render, P(468), thickness + 2, iris, -1, LineType.AntiAlias);
            CvInvoke.Circle(render, P(473), thickness + 2, iris, -1, LineType.AntiAlias);
            // The strongest expressions (without "_neutral"), the first face's in the list.
            var top = Enumerable.Range(1, FaceTracker.BlendshapeCount - 1)
                .OrderByDescending(k => face.Blendshapes[k]).Take(faces.Count == 1 ? 5 : 3).ToList();
            foreach (int k in top)
                demo.Rows.Add(new ResultRow((faces.Count > 1 ? $"Face {f + 1}: " : "") + Readable(FaceTracker.BlendshapeNames[k]),
                    $"{face.Blendshapes[k] * 100:0}%", face.Blendshapes[k]));
        }
        if (faces.Count == 0)
            demo.Rows.Add(new ResultRow("No face found", ""));
        return demo;
    }

    // "mouthSmileLeft" -> "mouth smile left".
    internal static string Readable(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && text.Length > 0) text.Append(' ');
            text.Append(char.ToLowerInvariant(c));
        }
        return text.ToString();
    }
}
