//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT.Util;
using Emgu.TF.Lite;
using Emgu.TF.Lite.ImageIO;
using Emgu.TF.Lite.Models;

namespace Maui.Demo.Lite
{
    public class CocoSsdMobilenetPage : DemoPage
    {
        private CocoSsdMobilenetV3 _mobilenet;

        public CocoSsdMobilenetPage()
            : base(
                "Coco SSD Mobilenet",
                "Find and label objects in a photo.",
                Theme.GlyphDetect,
                "Detects the 80 object classes of the COCO dataset with an SSD MobileNet v3 model running on the TensorFlow Lite interpreter, drawing a box and a confidence score around each object found.",
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
        }

        protected override void ReleaseModel()
        {
            _mobilenet?.Dispose();
            _mobilenet = null;
        }

        protected override async Task<DemoResult> RunAsync(Mat input)
        {
            if (_mobilenet == null)
            {
                _mobilenet = new CocoSsdMobilenetV3();
                _mobilenet.OnDownloadProgressChanged += OnDownloadProgress;
            }
            ShowProgress("Preparing the Coco SSD model...\n(the first run downloads it)");
            await _mobilenet.Init();
            if (!_mobilenet.Imported)
                throw new Exception("Failed to initialize the Coco SSD Mobilenet model.");

            ShowProgress("Detecting objects...");
            CocoSsdMobilenetV3 model = _mobilenet;
            return await Task.Run(() =>
            {
                Tensor t = model.InputTensor;
                System.Drawing.Size s = new System.Drawing.Size(t.Dims[2], t.Dims[1]);
                using (Mat tensorMat = new Mat(s, DepthType.Cv8U, 3, t.DataPointer, 3 * s.Width * Marshal.SizeOf<byte>()))
                {
                    CvInvoke.Resize(input, tensorMat, s);
                }

                Stopwatch watch = Stopwatch.StartNew();
                model.Interpreter.Invoke();
                var result = model.GetResults(0.5f);
                watch.Stop();

                Mat render = input.Clone();
                DemoResult demo = new DemoResult { Annotated = render, Summary = $"Detected in {watch.ElapsedMilliseconds} ms" };
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
                        FontFace.HersheyDuplex, 0.8, color, 2);
                }
                return demo;
            });
        }
    }
}
