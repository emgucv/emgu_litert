// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Official YOLOX-Nano raw-output graph, converted to float32 NHWC LiteRT (416 square).
    /// Pixels are BGR in 0..255, with top-left letterboxing and padding 114. No RGB swap or normalization.
    /// Calls are serialized by the host. See the demo's YOLOX_EVALUATION.md for model provenance.
    /// </summary>
    public sealed class YoloX : IDisposable
    {
        /// <summary>The fixed input size of the official Nano checkpoint.</summary>
        public const int InputSize = 416;
        /// <summary>Number of prediction rows over the stride 8, 16 and 32 grids.</summary>
        public const int PredictionCount = 3549;
        /// <summary>Values per prediction: xywh, objectness, 80 class probabilities.</summary>
        public const int PredictionWidth = 85;
        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private readonly bool _channelFirstOutput;
        private bool _disposed;

        /// <summary>COCO's contiguous 80-class order used by YOLOX, without a background entry.</summary>
        public static IReadOnlyList<string> Labels { get; } = Array.AsReadOnly((
            "person,bicycle,car,motorcycle,airplane,bus,train,truck,boat,traffic light,fire hydrant,stop sign," +
            "parking meter,bench,bird,cat,dog,horse,sheep,cow,elephant,bear,zebra,giraffe,backpack,umbrella," +
            "handbag,tie,suitcase,frisbee,skis,snowboard,sports ball,kite,baseball bat,baseball glove,skateboard," +
            "surfboard,tennis racket,bottle,wine glass,cup,fork,knife,spoon,bowl,banana,apple,sandwich,orange," +
            "broccoli,carrot,hot dog,pizza,donut,cake,chair,couch,potted plant,bed,dining table,toilet,tv,laptop," +
            "mouse,remote,keyboard,cell phone,microwave,oven,toaster,sink,refrigerator,book,clock,vase,scissors," +
            "teddy bear,hair drier,toothbrush").Split(','));

        /// <summary>Compile a local converted YOLOX-Nano graph. Compilation should run off the UI thread.</summary>
        public YoloX(string modelPath, int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu,
            GpuPrecision? gpuPrecision = null)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _model = new Model(_environment, modelPath);
                var signature = _model.GetSignature(0);
                var input = signature.GetInputTensorType(0);
                var output = signature.GetOutputTensorType(0);
                _channelFirstOutput = output.Dimensions.SequenceEqual(new[] { 1, PredictionWidth, PredictionCount });
                if (signature.InputCount != 1 || signature.OutputCount != 1 || input.ElementType != ElementType.Float32 ||
                    !input.Dimensions.SequenceEqual(new[] { 1, InputSize, InputSize, 3 }) ||
                    output.ElementType != ElementType.Float32 ||
                    (!_channelFirstOutput && !output.Dimensions.SequenceEqual(new[] { 1, PredictionCount, PredictionWidth })))
                    throw new ArgumentException("Expected YOLOX-Nano float32 NHWC input and raw [1,3549,85] output.", nameof(modelPath));
                using (var options = new Options(accelerators))
                {
                    options.AddCpuOptions(new CpuOptions { NumThreads = numThreads });
                    if ((accelerators & HwAccelerators.Gpu) != 0 && gpuPrecision.HasValue)
                        options.AddGpuOptions(new GpuOptions { Precision = gpuPrecision.Value });
                    _compiled = new CompiledModel(_environment, _model, options);
                }
                _inputs = _compiled.CreateInputBuffers();
                _outputs = _compiled.CreateOutputBuffers();
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Whether LiteRT reports the complete graph accelerated by the requested backend.</summary>
        public bool IsFullyAccelerated
        {
            get { CheckActive(); return _compiled.IsFullyAccelerated; }
        }

        /// <summary>The input buffer for letterboxed float32 BGR pixels.</summary>
        public TensorBuffer InputBuffer
        {
            get { CheckActive(); return _inputs[0]; }
        }

        /// <summary>Run and decode predictions into the original image's normalized coordinates.</summary>
        public Detection[] Run(int imageWidth, int imageHeight, float scoreThreshold = 0.25f)
        {
            CheckActive();
            _compiled.Run(_inputs, _outputs);
            var raw = _outputs[0].Read<float>();
            if (_channelFirstOutput)
            {
                var rows = new float[raw.Length];
                for (int row = 0; row < PredictionCount; row++)
                    for (int c = 0; c < PredictionWidth; c++) rows[row * PredictionWidth + c] = raw[c * PredictionCount + row];
                raw = rows;
            }
            return Decode(raw, imageWidth, imageHeight, scoreThreshold);
        }

        /// <summary>A detected COCO object, with normalized xyxy coordinates.</summary>
        public sealed class Detection
        {
            /// <summary>Contiguous COCO class index, 0..79.</summary>
            public int Class { get; internal set; }
            /// <summary>COCO label.</summary>
            public string Label => Labels[Class];
            /// <summary>Objectness multiplied by the best class probability.</summary>
            public float Score { get; internal set; }
            /// <summary>Normalized x0,y0,x1,y1 in the original image, clipped after NMS.</summary>
            public float[] Rectangle { get; internal set; }
            internal double[] Pixels;
        }

        /// <summary>
        /// Decode raw YOLOX output. Uses the official ONNX demo's best-class, class-agnostic NMS with inclusive
        /// pixel areas (+1), on un-clipped original-image coordinates. Clips only the returned boxes.
        /// </summary>
        public static Detection[] Decode(float[] predictions, int width, int height, float scoreThreshold = 0.25f,
            float nmsThreshold = 0.45f, int maxDetections = 100)
        {
            if (predictions == null || predictions.Length != PredictionCount * PredictionWidth)
                throw new ArgumentException("Expected raw YOLOX-Nano predictions.", nameof(predictions));
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (!Probability(scoreThreshold) || !Probability(nmsThreshold) || maxDetections < 1)
                throw new ArgumentOutOfRangeException(nameof(scoreThreshold));
            double ratio = Math.Min((double)InputSize / width, (double)InputSize / height);
            var candidates = new List<Detection>();
            int row = 0;
            foreach (int stride in new[] { 8, 16, 32 })
            for (int y = 0; y < InputSize / stride; y++)
            for (int x = 0; x < InputSize / stride; x++, row++)
            {
                int offset = row * PredictionWidth;
                float obj = predictions[offset + 4];
                if (!Probability(obj) || obj <= scoreThreshold) continue;
                int label = -1; float best = -1;
                for (int c = 0; c < 80; c++)
                {
                    float value = predictions[offset + 5 + c];
                    if (Probability(value) && value > best) { best = value; label = c; }
                }
                float score = obj * best;
                if (label < 0 || score <= scoreThreshold) continue;
                double cx = (predictions[offset] + x) * stride / ratio;
                double cy = (predictions[offset + 1] + y) * stride / ratio;
                double w = Math.Exp(predictions[offset + 2]) * stride / ratio;
                double h = Math.Exp(predictions[offset + 3]) * stride / ratio;
                if (!Finite(cx) || !Finite(cy) || !Finite(w) || !Finite(h) || w <= 0 || h <= 0) continue;
                candidates.Add(new Detection { Class = label, Score = score,
                    Pixels = new[] { cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2 } });
            }
            var kept = new List<Detection>();
            foreach (var candidate in candidates.OrderByDescending(c => c.Score))
            {
                if (kept.Any(k => Overlap(k.Pixels, candidate.Pixels) > nmsThreshold)) continue;
                var b = candidate.Pixels;
                float x0 = Clip(b[0] / width), y0 = Clip(b[1] / height);
                float x1 = Clip(b[2] / width), y1 = Clip(b[3] / height);
                if (x1 <= x0 || y1 <= y0) continue; // Entirely in padding/outside the visible frame.
                candidate.Rectangle = new[] { x0, y0, x1, y1 };
                kept.Add(candidate);
                if (kept.Count == maxDetections) break;
            }
            return kept.ToArray();
        }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(YoloX));
        }
        private static bool Probability(float n) => Finite(n) && n >= 0 && n <= 1;
        private static float Clip(double n) => (float)Math.Max(0, Math.Min(1, n));
        private static double Overlap(double[] a, double[] b)
        {
            double intersection = Math.Max(0, Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]) + 1) *
                Math.Max(0, Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]) + 1);
            double areaA = (a[2] - a[0] + 1) * (a[3] - a[1] + 1);
            double areaB = (b[2] - b[0] + 1) * (b[3] - b[1] + 1);
            return intersection / (areaA + areaB - intersection);
        }

        /// <summary>Release buffers, compiled model, graph and environment in ownership order.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_inputs != null) foreach (var b in _inputs) b?.Dispose();
            if (_outputs != null) foreach (var b in _outputs) b?.Dispose();
            _compiled?.Dispose(); _model?.Dispose(); _environment?.Dispose();
        }
    }
}
