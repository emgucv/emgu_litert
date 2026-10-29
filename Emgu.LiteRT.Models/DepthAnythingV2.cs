// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Depth Anything V2 Small (DINOv2 Small backbone with a DPT decoder, ~24.8M parameters, Apache 2.0): a relative-depth
    /// map from one RGB image, larger values nearer (not distances in metres). The LiteRT export of
    /// https://huggingface.co/litert-community/depth-anything-v2-small, with its fixed 686 x 518 input. The input is RGB
    /// stretched to 686 x 518 (bicubic), scaled to 0..1 and normalized with ImageNet's mean and standard deviation, in
    /// NCHW order; Emgu.LiteRT.Models.CV's DepthEstimator writes it from an Emgu CV image. Calls are serialized by the
    /// host. See DEPTH_ANYTHING_EVALUATION.md in this folder for the measurements.
    /// </summary>
    public sealed class DepthAnythingV2 : IDisposable
    {
        /// <summary>The input's fixed width.</summary>
        public const int InputWidth = 686;
        /// <summary>The input's fixed height.</summary>
        public const int InputHeight = 518;
        /// <summary>The per-channel (R, G, B) mean subtracted from pixels scaled to 0..1.</summary>
        public static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        /// <summary>The per-channel (R, G, B) standard deviation the pixels are divided by after the mean.</summary>
        public static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private bool _disposed;

        /// <summary>
        /// The model file with dynamic int8 weights and float32 activations (27.7 MB): as accurate as the float32 file
        /// (99.5 MB) on the GPU and as fast there, a quarter of the download.
        /// </summary>
        public static DownloadableFile ModelFile => new DownloadableFile(
            "https://huggingface.co/litert-community/depth-anything-v2-small/resolve/main/tflite/depth_anything_v2_small_wi8_afp32.tflite",
            "DepthAnythingV2",
            "f74509422e4a9270a354b249a9193abdd4903354be63701262238a7f4b869611");

        /// <summary>Compile the model. Compilation should run off the UI thread (about 1-2 s for a GPU).</summary>
        /// <param name="modelPath">The .tflite file</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="accelerators">Cpu, or Gpu | Cpu</param>
        /// <param name="gpuPrecision">The GPU's precision (Fp16 matches the CPU closely and is about 1.5-2x faster than Fp32)</param>
        public DepthAnythingV2(string modelPath, int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu,
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
                if (signature.InputCount != 1 || signature.OutputCount != 1 || input.ElementType != ElementType.Float32 ||
                    !input.Dimensions.SequenceEqual(new[] { 1, 3, InputHeight, InputWidth }) ||
                    output.ElementType != ElementType.Float32 || !output.Dimensions.SequenceEqual(new[] { 1, InputHeight, InputWidth }))
                    throw new ArgumentException("Expected Depth Anything V2 with a float32 NCHW [1,3,518,686] input and a [1,518,686] output.", nameof(modelPath));
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

        /// <summary>The input buffer: normalized float32 RGB planes (R, then G, then B), each 686 x 518.</summary>
        public TensorBuffer InputBuffer
        {
            get { CheckActive(); return _inputs[0]; }
        }

        /// <summary>Run the model on the input buffer; the relative depth, 686 x 518 row by row, larger nearer.</summary>
        public float[] Run()
        {
            CheckActive();
            _compiled.Run(_inputs, _outputs);
            return _outputs[0].Read<float>();
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DepthAnythingV2));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_inputs != null) foreach (var b in _inputs) b?.Dispose();
            if (_outputs != null) foreach (var b in _outputs) b?.Dispose();
            _compiled?.Dispose();
            _model?.Dispose();
            _environment?.Dispose();
        }
    }
}
