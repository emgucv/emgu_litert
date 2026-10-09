//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.       
//----------------------------------------------------------------------------

using System.Diagnostics;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.TF.Lite;
using Emgu.TF.Lite.Models;

namespace Maui.Demo.Lite
{
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

        protected override async Task<DemoResult> RunAsync(Mat input)
        {
            if (_inception == null)
            {
                _inception = new Inception();
                _inception.OnDownloadProgressChanged += OnDownloadProgress;
            }
            ShowProgress("Preparing the Inception model...\n(the first run downloads it)");
            await _inception.Init();
            if (!_inception.Imported)
                throw new Exception("Failed to initialize the Inception model.");

            ShowProgress("Classifying...");
            Inception model = _inception;
            (Inception.RecognitionResult[] result, long ms) = await Task.Run(() =>
            {
                Tensor t = model.InputTensor;
                System.Drawing.Size s = new System.Drawing.Size(299, 299);
                using (Mat resizedMat = new Mat(s, DepthType.Cv8U, 3))
                using (Mat tensorMat = new Mat(s, DepthType.Cv32F, 3, t.DataPointer, 3 * s.Width * Marshal.SizeOf<float>()))
                {
                    CvInvoke.Resize(input, resizedMat, s);
                    CvInvoke.CvtColor(resizedMat, resizedMat, ColorConversion.Bgr2Rgb);
                    resizedMat.ConvertTo(tensorMat, DepthType.Cv32F, 1.0 / 255.0, -0.0);
                }
                Stopwatch watch = Stopwatch.StartNew();
                var r = model.Invoke();
                watch.Stop();
                return (r, watch.ElapsedMilliseconds);
            });

            DemoResult demo = new DemoResult { Summary = $"Recognized in {ms} ms" };
            foreach (var r in result.Take(5))
                demo.Rows.Add(new ResultRow(r.Label, $"{r.Probability * 100:0.#}%", r.Probability));
            return demo;
        }
    }
}
