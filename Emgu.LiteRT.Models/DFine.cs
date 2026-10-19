// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// D-FINE-N (COCO) converted to float32 NHWC LiteRT with static shapes (see the demo's DFINE_EVALUATION.md, which
    /// also has the conversion recipe). The square input (640 is what it was trained at; 416 and 480 were converted
    /// too) is the image resized without keeping its aspect ratio, as RGB divided by 255. The two outputs are
    /// <c>pred_logits</c> [1,300,80] (before the sigmoid) and <c>pred_boxes</c> [1,300,4] (centre x, centre y, width,
    /// height, normalized); there is no NMS step. Calls are serialized by the host.
    /// </summary>
    public sealed class DFine : IDisposable
    {
        /// <summary>The number of queries (candidate objects) the network returns.</summary>
        public const int QueryCount = 300;
        /// <summary>COCO's 80 contiguous classes.</summary>
        public const int ClassCount = 80;
        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private readonly int _logits, _boxes;
        private bool _disposed;

        /// <summary>The labels of the 80 classes, the same list as <see cref="YoloX.Labels"/>.</summary>
        public static IReadOnlyList<string> Labels => YoloX.Labels;

        /// <summary>The model's square input size in pixels.</summary>
        public int InputSize { get; }

        /// <summary>Compile a local converted D-FINE-N graph. Compilation should run off the UI thread.</summary>
        /// <remarks>
        /// Only the CPU is supported: the GPU compilers (Metal, OpenCL) reject the graph, and falling back to the CPU
        /// for the rest made it slower than the CPU alone on phones.
        /// </remarks>
        public DFine(string modelPath, int numThreads = 2)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _model = new Model(_environment, modelPath);
                var signature = _model.GetSignature(0);
                if (signature.InputCount != 1 || signature.OutputCount != 2)
                    throw new ArgumentException("Expected D-FINE with one input and two outputs.", nameof(modelPath));
                var input = signature.GetInputTensorType(0);
                var d = input.Dimensions;
                if (input.ElementType != ElementType.Float32 || d.Length != 4 || d[0] != 1 || d[3] != 3 || d[1] != d[2] || d[1] < 32)
                    throw new ArgumentException("Expected a float32 NHWC [1,S,S,3] input.", nameof(modelPath));
                InputSize = d[1];
                _logits = _boxes = -1;
                for (int i = 0; i < 2; i++)
                {
                    var t = signature.GetOutputTensorType(i);
                    if (t.ElementType != ElementType.Float32) continue;
                    if (t.Dimensions.SequenceEqual(new[] { 1, QueryCount, ClassCount })) _logits = i;
                    else if (t.Dimensions.SequenceEqual(new[] { 1, QueryCount, 4 })) _boxes = i;
                }
                if (_logits < 0 || _boxes < 0)
                    throw new ArgumentException("Expected float32 outputs [1,300,80] and [1,300,4].", nameof(modelPath));
                using (var options = new Options(HwAccelerators.Cpu))
                {
                    options.AddCpuOptions(new CpuOptions { NumThreads = numThreads });
                    _compiled = new CompiledModel(_environment, _model, options);
                }
                _inputs = _compiled.CreateInputBuffers();
                _outputs = _compiled.CreateOutputBuffers();
            }
            catch { Dispose(); throw; }
        }

        /// <summary>The input buffer for the resized float32 RGB pixels (0..1).</summary>
        public TensorBuffer InputBuffer
        {
            get { CheckActive(); return _inputs[0]; }
        }

        /// <summary>Run and decode the queries into the original image's normalized coordinates.</summary>
        public Detection[] Run(float scoreThreshold = 0.5f)
        {
            CheckActive();
            _compiled.Run(_inputs, _outputs);
            return Decode(_outputs[_logits].Read<float>(), _outputs[_boxes].Read<float>(), scoreThreshold);
        }

        /// <summary>A detected COCO object, with normalized xyxy coordinates.</summary>
        public sealed class Detection
        {
            /// <summary>Contiguous COCO class index, 0..79.</summary>
            public int Class { get; internal set; }
            /// <summary>COCO label.</summary>
            public string Label => Labels[Class];
            /// <summary>The sigmoid of the class's logit.</summary>
            public float Score { get; internal set; }
            /// <summary>Normalized x0,y0,x1,y1 in the original image, clipped to it.</summary>
            public float[] Rectangle { get; internal set; }
        }

        /// <summary>
        /// Decode the raw outputs: the sigmoid of every query's 80 logits, the best scores first, every one at or
        /// above the threshold (a query can appear for more than one class). The boxes are normalized to the image
        /// since the input is resized without letterboxing, so no width or height is needed.
        /// </summary>
        public static Detection[] Decode(float[] logits, float[] boxes, float scoreThreshold = 0.5f, int maxDetections = QueryCount)
        {
            if (logits == null || logits.Length != QueryCount * ClassCount || boxes == null || boxes.Length != QueryCount * 4)
                throw new ArgumentException("Expected raw D-FINE outputs [1,300,80] and [1,300,4].");
            if (!(scoreThreshold >= 0 && scoreThreshold <= 1) || maxDetections < 1)
                throw new ArgumentOutOfRangeException(nameof(scoreThreshold));
            // sigmoid(x) >= t  <=>  x >= logit(t): no exponentials for the ~24,000 entries below it.
            double limit = scoreThreshold <= 0 ? double.NegativeInfinity : scoreThreshold >= 1 ? double.PositiveInfinity
                : Math.Log(scoreThreshold / (1 - scoreThreshold));
            var found = new List<Detection>();
            for (int i = 0; i < logits.Length; i++)
            {
                float x = logits[i];
                if (float.IsNaN(x) || x < limit) continue;
                int query = i / ClassCount;
                float cx = boxes[query * 4], cy = boxes[query * 4 + 1], w = boxes[query * 4 + 2], h = boxes[query * 4 + 3];
                if (float.IsNaN(cx + cy + w + h) || float.IsInfinity(cx + cy + w + h) || w <= 0 || h <= 0) continue;
                float x0 = Clip(cx - w / 2), y0 = Clip(cy - h / 2), x1 = Clip(cx + w / 2), y1 = Clip(cy + h / 2);
                if (x1 <= x0 || y1 <= y0) continue;
                found.Add(new Detection { Class = i % ClassCount, Score = (float)(1 / (1 + Math.Exp(-x))),
                    Rectangle = new[] { x0, y0, x1, y1 } });
            }
            return found.OrderByDescending(d => d.Score).Take(maxDetections).ToArray();
        }

        private static float Clip(float n) => Math.Max(0, Math.Min(1, n));
        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DFine));
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
