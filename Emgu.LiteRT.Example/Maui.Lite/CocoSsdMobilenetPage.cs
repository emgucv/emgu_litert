//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.TF.Lite.ImageIO;

namespace Maui.Demo.Lite
{
    public class CocoSsdMobilenetPage : DemoPage
    {
        private ICocoDetector _detector;
        private readonly bool _useYoloX;
        private readonly Picker _backend;

        public CocoSsdMobilenetPage(bool useYoloX = false)
            : base(
                useYoloX ? "YOLOX Nano" : "Coco SSD Mobilenet",
                "Find and label objects in a photo.",
                Theme.GlyphDetect,
                useYoloX
                    ? "Detects COCO objects with the official YOLOX-Nano model on this device. This experimental detector tries the GPU where available (FP32, checked against the CPU for accuracy and speed), with CPU fallback; compare its boxes and timing with SSD MobileNet in the other demo."
                    : "Detects the 80 object classes of the COCO dataset with SSD MobileNet v3 using LiteRT CompiledModel, drawing a box and a confidence score around each object found. GPU is tried where available, with CPU fallback.",
                new[]
                {
                    new Sample("dog416.png", "Dog & bike", Theme.GlyphPets),
                    new Sample("surfers.jpg", "Surfers"),
                    new Sample("space_shuttle.jpg", "Space shuttle"),
                    new Sample("tulips.jpg", "Tulips")
                },
                0,
                "Detect Objects",
                "Detections")
        {
            _useYoloX = useYoloX;
            _backend = new Picker { Title = "Detector backend", FontFamily = Theme.BodyFont, FontSize = 16,
                TextColor = Theme.PrimaryText, TitleColor = Theme.SecondaryText };
            _backend.Items.Add("CPU");
            if (CocoDetector.CanTryGpu) _backend.Items.Add("GPU (CPU fallback)");
            _backend.SelectedIndex = CocoDetector.CanTryGpu ? 1 : 0;
            _backend.SelectedIndexChanged += (_, _) => ReleaseModel();
            AddOptionsCard(Theme.Card(_backend, 14));
        }

        protected override void ReleaseModel()
        {
            _detector?.Dispose();
            _detector = null;
        }

        protected override async Task<DemoResult> RunAsync(Mat input)
        {
            _backend.IsEnabled = false;
            try
            {
                _detector ??= _useYoloX ? new YoloXDetector() : new CocoDetector();
                await _detector.PrepareAsync(_backend.SelectedIndex == 1, CpuThreadSettings.Count,
                    ShowProgress, CancellationToken.None);
                ShowProgress("Detecting objects...");
                return await Task.Run(() =>
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    var result = _detector.Detect(input).Where(r => r.Score >= 0.5f).ToArray();
                    watch.Stop();

                    Mat render = input.Clone();
                    DemoResult demo = new DemoResult { Annotated = render, Summary = $"LiteRT {_detector.Backend} · {watch.ElapsedMilliseconds} ms" +
                        (_detector.GpuFailure != null ? " · GPU unavailable, using CPU" : "") };
                    MCvScalar color = new MCvScalar(247, 123, 61);
                    foreach (var r in result.OrderByDescending(x => x.Score))
                    {
                        demo.Rows.Add(new ResultRow(r.Label, $"{r.Score * 100:0}%", r.Score));
                        if (r.Rectangle == null)
                            continue;
                        float[] rects = NativeImageIO.ScaleLocation(r.Rectangle, render.Width, render.Height);
                        System.Drawing.RectangleF rect = new System.Drawing.RectangleF(
                            rects[0], rects[1], rects[2] - rects[0], rects[3] - rects[1]);
                        CvInvoke.Rectangle(render, System.Drawing.Rectangle.Round(rect), color, 2);
                        CvInvoke.PutText(render, $"{r.Label} {r.Score * 100:0}%", System.Drawing.Point.Round(rect.Location),
                            FontFace.HersheyDuplex, _useYoloX ? 0.45 : 0.8, color, _useYoloX ? 1 : 2);
                    }
                    return demo;
                });
            }
            finally { _backend.IsEnabled = true; }
        }
    }
}
