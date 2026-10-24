//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.TF.Lite;
using Emgu.TF.Lite.Models;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

public class MobilenetPage : DemoPage
{
    private Mobilenet _mobilenet;
    private bool _delegateApplied;
    private readonly Picker _picker;

    public MobilenetPage()
        : base(
            "Mobilenet",
            "Recognize what is in a photo.",
            Theme.GlyphImage,
            "Classifies the whole image with MobileNet v2 running on the TensorFlow Lite interpreter, and lists the most likely labels with their probabilities. CPU is always available; NNAPI and GPU appear when the device supports them.",
            new[]
            {
                new Sample("space_shuttle.jpg", "Space shuttle"),
                new Sample("dog416.png", "Dog & bike", Theme.GlyphPets),
                new Sample("surfers.jpg", "Surfers"),
                new Sample("tulips.jpg", "Tulips")
            },
            0,
            "Classify Image",
            "Top predictions")
    {
        _picker = new Picker { Title = "TF Lite backend", FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
        _picker.Items.Add("CPU");
#if __ANDROID__
        if (TfLiteInvoke.DefaultNnApiDelegate != null)
            _picker.Items.Add("NNAPI");
#endif
        if (TfLiteInvoke.DefaultGpuDelegateV2 != null)
            _picker.Items.Add("GPU");
        _picker.SelectedIndex = 0;
        _picker.SelectedIndexChanged += (s, e) => ReleaseModel();
        if (_picker.Items.Count > 1)
        {
            var row = new Grid
            {
                ColumnSpacing = 14,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };
            row.Add(Theme.IconTile(Theme.GlyphSettings, 42, 22, 11), 0, 0);
            row.Add(_picker, 1, 0);
            AddOptionsCard(Theme.Card(row, 14));
        }
    }

    protected override void ReleaseModel()
    {
        _mobilenet?.Dispose();
        _mobilenet = null;
        _delegateApplied = false;
    }

    protected override bool SupportsLiveCamera => true;

    // A classifier: the list is the result; live frames get the top labels drawn on them.
    protected override bool DrawsOnPhoto => false;

    protected override string AnalyzingText => "Classifying...";

    protected override void SetOptionsEnabled(bool enabled) => _picker.IsEnabled = enabled;

    protected override async Task PrepareAsync()
    {
        if (_mobilenet == null)
        {
            _mobilenet = new Mobilenet { NumThreads = CpuThreadSettings.Count };
            _mobilenet.OnDownloadProgressChanged += OnDownloadProgress;
        }
        ShowProgress("Preparing the Mobilenet model...\n(the first run downloads it)");
        await _mobilenet.Init();

        if (_picker.SelectedIndex > 0 && !_delegateApplied)
        {
            string backend = _picker.SelectedItem.ToString();
            if (backend == "NNAPI")
                _mobilenet.Interpreter.ModifyGraphWithDelegate(TfLiteInvoke.DefaultNnApiDelegate);
            else if (backend == "GPU")
                _mobilenet.Interpreter.ModifyGraphWithDelegate(TfLiteInvoke.DefaultGpuDelegateV2);
            _delegateApplied = true;
        }
    }

    protected override DemoResult Analyze(Mat input, Mat render)
    {
        _mobilenet.WriteInput(input);
        Stopwatch watch = Stopwatch.StartNew();
        Mobilenet.RecognitionResult[] result = _mobilenet.Invoke();
        watch.Stop();

        DemoResult demo = new DemoResult { Summary = $"Recognized in {watch.ElapsedMilliseconds} ms" };
        foreach (var r in result.Take(5))
            demo.Rows.Add(new ResultRow(r.Label, $"{r.Probability * 100:0.#}%", r.Probability));
        DrawLabels(render, result.Take(3).Select(r => (r.Label, r.Probability)));
        return demo;
    }

    // The top labels at the top of a live frame, outlined so they can be read over any picture.
    internal static void DrawLabels(Mat render, IEnumerable<(string Label, double Probability)> top)
    {
        int y = 28;
        foreach (var (label, probability) in top)
        {
            string text = $"{label} {probability * 100:0}%";
            var at = new System.Drawing.Point(10, y);
            CvInvoke.PutText(render, text, at, FontFace.HersheyDuplex, 0.7, new Emgu.CV.Structure.MCvScalar(0, 0, 0), 4);
            CvInvoke.PutText(render, text, at, FontFace.HersheyDuplex, 0.7, new Emgu.CV.Structure.MCvScalar(255, 255, 255), 1);
            y += 28;
        }
    }
}
