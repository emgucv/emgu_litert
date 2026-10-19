// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Baidu's PP-PicoDet-S (416, COCO, LCNet; PaddleDetection, Apache-2.0) converted to float32 NHWC LiteRT, without
    /// post-processing (see the demo's PICODET_EVALUATION.md, which also has the conversion recipe). The input is the
    /// image resized to 416 x 416 without keeping its aspect ratio (cubic interpolation), as RGB, divided by 255, minus
    /// the ImageNet mean and divided by its standard deviation. For the strides 8, 16, 32 and 64 the network returns the
    /// class scores (already sigmoid) [1,N,80] and the box distributions [1,N,32] - 8 bins for each of the four sides,
    /// where N is 2704, 676, 169 and 49 - which <see cref="Decode"/> turns into boxes. Calls are serialized by the host.
    /// </summary>
    public sealed class PicoDet : IDisposable
    {
        /// <summary>The fixed square input size.</summary>
        public const int InputSize = 416;
        /// <summary>COCO's 80 contiguous classes.</summary>
        public const int ClassCount = 80;
        /// <summary>The bins of each side's box distribution (PicoDet's reg_max + 1).</summary>
        public const int Bins = 8;
        /// <summary>The strides of the four feature levels.</summary>
        public static IReadOnlyList<int> Strides { get; } = Array.AsReadOnly(new[] { 8, 16, 32, 64 });
        private static readonly int[] Cells = { 52 * 52, 26 * 26, 13 * 13, 7 * 7 };
        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private readonly int[] _scoreOutputs = new int[4], _distributionOutputs = new int[4];
        private bool _disposed;

        /// <summary>The labels of the 80 classes, the same list as <see cref="YoloX.Labels"/>.</summary>
        public static IReadOnlyList<string> Labels => YoloX.Labels;

        /// <summary>Compile a local converted PicoDet-S graph. Compilation should run off the UI thread.</summary>
        public PicoDet(string modelPath, int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu,
            GpuPrecision? gpuPrecision = null)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _model = new Model(_environment, modelPath);
                var signature = _model.GetSignature(0);
                var input = signature.GetInputTensorType(0);
                if (signature.InputCount != 1 || signature.OutputCount != 8 || input.ElementType != ElementType.Float32 ||
                    !input.Dimensions.SequenceEqual(new[] { 1, InputSize, InputSize, 3 }))
                    throw new ArgumentException("Expected PicoDet-S with a float32 NHWC [1,416,416,3] input and 8 outputs.", nameof(modelPath));
                for (int level = 0; level < 4; level++) _scoreOutputs[level] = _distributionOutputs[level] = -1;
                for (int i = 0; i < 8; i++)
                {
                    var t = signature.GetOutputTensorType(i);
                    int level = Array.IndexOf(Cells, t.Dimensions.Length == 3 ? t.Dimensions[1] : -1);
                    if (level < 0 || t.ElementType != ElementType.Float32 || t.Dimensions[0] != 1) continue;
                    if (t.Dimensions[2] == ClassCount) _scoreOutputs[level] = i;
                    else if (t.Dimensions[2] == 4 * Bins) _distributionOutputs[level] = i;
                }
                if (_scoreOutputs.Contains(-1) || _distributionOutputs.Contains(-1))
                    throw new ArgumentException("Expected float32 outputs [1,N,80] and [1,N,32] for N = 2704, 676, 169 and 49.", nameof(modelPath));
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

        /// <summary>The input buffer for the resized, normalized float32 RGB pixels.</summary>
        public TensorBuffer InputBuffer
        {
            get { CheckActive(); return _inputs[0]; }
        }

        /// <summary>Run and decode into the original image's normalized coordinates.</summary>
        public Detection[] Run(float scoreThreshold = 0.5f)
        {
            CheckActive();
            _compiled.Run(_inputs, _outputs);
            var scores = _scoreOutputs.Select(i => _outputs[i].Read<float>()).ToArray();
            var distributions = _distributionOutputs.Select(i => _outputs[i].Read<float>()).ToArray();
            return Decode(scores, distributions, scoreThreshold);
        }

        /// <summary>A detected COCO object, with normalized xyxy coordinates.</summary>
        public sealed class Detection
        {
            /// <summary>Contiguous COCO class index, 0..79.</summary>
            public int Class { get; internal set; }
            /// <summary>COCO label.</summary>
            public string Label => Labels[Class];
            /// <summary>The class score, 0..1.</summary>
            public float Score { get; internal set; }
            /// <summary>Normalized x0,y0,x1,y1 in the original image, clipped to it.</summary>
            public float[] Rectangle { get; internal set; }
            internal double[] Pixels;
        }

        /// <summary>
        /// Decode the four levels' scores ([cells x 80]) and box distributions ([cells x 32]) as PicoDet's head does:
        /// a softmax over each side's 8 bins gives its expected distance in strides from the cell centre
        /// ((x + 0.5) * stride), then class-aware NMS with pixel areas (+1) like PaddleDetection's multiclass NMS. The
        /// image was resized without letterboxing, so the boxes are fractions of the 416 input, clipped to the image.
        /// </summary>
        public static Detection[] Decode(float[][] scores, float[][] distributions, float scoreThreshold = 0.5f,
            float nmsThreshold = 0.6f, int maxDetections = 100)
        {
            if (scores == null || distributions == null || scores.Length != 4 || distributions.Length != 4)
                throw new ArgumentException("Expected four levels of scores and distributions.");
            for (int level = 0; level < 4; level++)
                if (scores[level] == null || scores[level].Length != Cells[level] * ClassCount ||
                    distributions[level] == null || distributions[level].Length != Cells[level] * 4 * Bins)
                    throw new ArgumentException("Expected PicoDet-S level sizes [2704,676,169,49] x 80 and x 32.");
            if (!(scoreThreshold >= 0 && scoreThreshold <= 1) || !(nmsThreshold >= 0 && nmsThreshold <= 1) || maxDetections < 1)
                throw new ArgumentOutOfRangeException(nameof(scoreThreshold));
            var candidates = new List<Detection>();
            var box = new double[4];
            for (int level = 0; level < 4; level++)
            {
                int stride = Strides[level], side = (InputSize + stride - 1) / stride;
                for (int cell = 0; cell < Cells[level]; cell++)
                {
                    int best = -1;
                    var cls = scores[level];
                    for (int c = 0; c < ClassCount; c++)
                    {
                        float s = cls[cell * ClassCount + c];
                        if (s > scoreThreshold && !float.IsNaN(s) && (best < 0 || s > cls[cell * ClassCount + best])) best = c;
                    }
                    // A cell may exceed the threshold for several classes; each class is kept separately (class-aware NMS).
                    if (best < 0) continue;
                    if (!Expect(distributions[level], cell * 4 * Bins, stride, box)) continue;
                    double cx = (cell % side + 0.5) * stride, cy = (cell / side + 0.5) * stride;
                    double x0 = cx - box[0], y0 = cy - box[1], x1 = cx + box[2], y1 = cy + box[3];
                    for (int c = 0; c < ClassCount; c++)
                    {
                        float s = cls[cell * ClassCount + c];
                        if (s > scoreThreshold && !float.IsNaN(s))
                            candidates.Add(new Detection { Class = c, Score = s, Pixels = new[] { x0, y0, x1, y1 } });
                    }
                }
            }
            var kept = new List<Detection>();
            foreach (var group in candidates.GroupBy(d => d.Class))
            {
                var classKept = new List<Detection>();
                foreach (var candidate in group.OrderByDescending(d => d.Score))
                    if (!classKept.Any(k => Overlap(k.Pixels, candidate.Pixels) > nmsThreshold)) classKept.Add(candidate);
                kept.AddRange(classKept);
            }
            var result = new List<Detection>();
            foreach (var d in kept.OrderByDescending(d => d.Score))
            {
                float x0 = Clip(d.Pixels[0] / InputSize), y0 = Clip(d.Pixels[1] / InputSize);
                float x1 = Clip(d.Pixels[2] / InputSize), y1 = Clip(d.Pixels[3] / InputSize);
                if (x1 <= x0 || y1 <= y0) continue;
                d.Rectangle = new[] { x0, y0, x1, y1 };
                result.Add(d);
                if (result.Count == maxDetections) break;
            }
            return result.ToArray();
        }

        // The expected distance of each side, in pixels: a softmax over its 8 bins, times the bin index, times the stride.
        private static bool Expect(float[] data, int offset, int stride, double[] distances)
        {
            for (int side = 0; side < 4; side++)
            {
                int o = offset + side * Bins;
                float max = float.NegativeInfinity;
                for (int b = 0; b < Bins; b++) max = Math.Max(max, data[o + b]);
                double sum = 0, weighted = 0;
                for (int b = 0; b < Bins; b++)
                {
                    double e = Math.Exp(data[o + b] - max);
                    sum += e; weighted += e * b;
                }
                double d = weighted / sum * stride;
                if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                distances[side] = d;
            }
            return true;
        }

        private static float Clip(double n) => (float)Math.Max(0, Math.Min(1, n));
        private static double Overlap(double[] a, double[] b)
        {
            double intersection = Math.Max(0, Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]) + 1) *
                Math.Max(0, Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]) + 1);
            double areaA = (a[2] - a[0] + 1) * (a[3] - a[1] + 1);
            double areaB = (b[2] - b[0] + 1) * (b[3] - b[1] + 1);
            return intersection / (areaA + areaB - intersection);
        }
        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PicoDet));
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
