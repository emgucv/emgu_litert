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

namespace Maui.Demo.Lite
{
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

        protected override async Task<DemoResult> RunAsync(Mat input)
        {
            if (_mobilenet == null)
            {
                _mobilenet = new Mobilenet();
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

            ShowProgress("Classifying...");
            Mobilenet model = _mobilenet;
            (Mobilenet.RecognitionResult[] result, long ms) = await Task.Run(() =>
            {
                Tensor t = model.InputTensor;
                System.Drawing.Size s = new System.Drawing.Size(224, 224);
                using (Mat resizedMat = new Mat(s, DepthType.Cv8U, 3))
                using (Mat tensorMat = new Mat(s, DepthType.Cv32F, 3, t.DataPointer, 3 * s.Width * Marshal.SizeOf<float>()))
                {
                    CvInvoke.Resize(input, resizedMat, s);
                    resizedMat.ConvertTo(tensorMat, DepthType.Cv32F, 1.0 / 128.0, -1.0 / 128.0);
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
