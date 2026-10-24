//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.TF.Lite;
using Emgu.TF.Lite.Models;

namespace Maui.Demo.Lite;

public class InceptionPage : DemoPage
{
    private Inception _inception;

    public InceptionPage()
        : base(
            "Inception",
            "Recognize flowers in a photo.",
            Theme.GlyphSparkle,
            "Classifies a photo with an Inception model trained on flower species, running on the TensorFlow Lite interpreter, and lists the most likely species with their probabilities.",
            new[]
            {
                new Sample("tulips.jpg", "Tulips"),
                new Sample("space_shuttle.jpg", "Space shuttle"),
                new Sample("dog416.png", "Dog & bike", Theme.GlyphPets),
                new Sample("surfers.jpg", "Surfers")
            },
            0,
            "Classify Image",
            "Top predictions")
    {
    }

    protected override void ReleaseModel()
    {
        _inception?.Dispose();
        _inception = null;
    }

    protected override bool SupportsLiveCamera => true;

    // A classifier: the list is the result; live frames get the top labels drawn on them.
    protected override bool DrawsOnPhoto => false;

    protected override string AnalyzingText => "Classifying...";

    protected override async Task PrepareAsync()
    {
        if (_inception == null)
        {
            _inception = new Inception { NumThreads = CpuThreadSettings.Count };
            _inception.OnDownloadProgressChanged += OnDownloadProgress;
        }
        ShowProgress("Preparing the Inception model...\n(the first run downloads it)");
        await _inception.Init();
        if (!_inception.Imported)
            throw new Exception("Failed to initialize the Inception model.");
    }

    protected override DemoResult Analyze(Mat input, Mat render)
    {
        _inception.WriteInput(input);
        Stopwatch watch = Stopwatch.StartNew();
        Inception.RecognitionResult[] result = _inception.Invoke();
        watch.Stop();

        DemoResult demo = new DemoResult { Summary = $"Recognized in {watch.ElapsedMilliseconds} ms" };
        foreach (var r in result.Take(5))
            demo.Rows.Add(new ResultRow(r.Label, $"{r.Probability * 100:0.#}%", r.Probability));
        MobilenetPage.DrawLabels(render, result.Take(3).Select(r => (r.Label, r.Probability)));
        return demo;
    }
}
