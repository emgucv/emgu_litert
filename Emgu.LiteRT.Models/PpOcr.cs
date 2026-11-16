// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// PaddleOCR's PP-OCRv6 Small text detector (DB, Apache-2.0) as the LiteRT community converted it
    /// (litert-community/PP-OCRv6-Small-LiteRT, float32 only: its GPU and NPU results didn't match the reference): input
    /// [1,3,640,640] float32, planar B, G, R, each (value / 255 - mean) / std with the means 0.485, 0.456, 0.406 and the
    /// standard deviations 0.229, 0.224, 0.225; output [1,1,640,640] text probabilities. The image around it (resize, the
    /// DB post-processing into quadrilaterals) is <c>PpOcrDetector</c> in Emgu.LiteRT.Models.CV. Calls are serialized by the host.
    /// </summary>
    public sealed class PpOcrDetModel : IDisposable
    {
        /// <summary>The fixed square input size.</summary>
        public const int InputSize = 640;
        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private bool _disposed;

        /// <summary>Compile the detector graph on the CPU. Compilation should run off the UI thread.</summary>
        /// <param name="modelPath">The local ppocrv6_small_det_640_fp32.tflite</param>
        /// <param name="numThreads">CPU threads</param>
        public PpOcrDetModel(string modelPath, int numThreads = 2)
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
                    !input.Dimensions.SequenceEqual(new[] { 1, 3, InputSize, InputSize }) ||
                    output.ElementType != ElementType.Float32 || !output.Dimensions.SequenceEqual(new[] { 1, 1, InputSize, InputSize }))
                    throw new ArgumentException("Expected the PP-OCRv6 Small detector: float32 [1,3,640,640] in, [1,1,640,640] out.", nameof(modelPath));
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

        /// <summary>The input buffer for the normalized planar BGR pixels.</summary>
        public TensorBuffer InputBuffer
        {
            get { CheckActive(); return _inputs[0]; }
        }

        /// <summary>Run the graph on what is in <see cref="InputBuffer"/>.</summary>
        /// <returns>The 640 x 640 text probability map, row by row</returns>
        public float[] Run()
        {
            CheckActive();
            _compiled.Run(_inputs, _outputs);
            return _outputs[0].Read<float>();
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PpOcrDetModel));
        }

        /// <summary>Release the graph.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _compiled?.Dispose();
            _model?.Dispose();
            _environment?.Dispose();
        }
    }

    /// <summary>
    /// PaddleOCR's PP-OCRv6 Small text recognizer for one exported line width (320, 640 or 960): input [1,3,48,W] float32,
    /// planar B, G, R, (value - 127.5) / 127.5, a line scaled to 48 high and padded on the right with zeros; output
    /// [1,W/8,18710] probabilities over the dictionary (<see cref="LoadCharacters"/>), read by greedy CTC
    /// (<see cref="Decode"/>). CPU float32 only. Calls are serialized by the host.
    /// </summary>
    public sealed class PpOcrRecModel : IDisposable
    {
        /// <summary>The height every line is scaled to.</summary>
        public const int InputHeight = 48;
        /// <summary>The size of the dictionary, blank (index 0) included.</summary>
        public const int CharacterCount = 18710;
        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private bool _disposed;

        /// <summary>The line width this graph takes.</summary>
        public int Width { get; }

        /// <summary>The time steps it returns.</summary>
        public int Steps => Width / 8;

        /// <summary>Compile a recognizer graph on the CPU. Compilation should run off the UI thread.</summary>
        /// <param name="modelPath">The local ppocrv6_small_rec_320_fp32.tflite (or the 640 or 960 one)</param>
        /// <param name="width">The width the file was exported for</param>
        /// <param name="numThreads">CPU threads</param>
        public PpOcrRecModel(string modelPath, int width = 320, int numThreads = 2)
        {
            if (width != 320 && width != 640 && width != 960) throw new ArgumentOutOfRangeException(nameof(width));
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            Width = width;
            try
            {
                _environment = new Environment();
                _model = new Model(_environment, modelPath);
                var signature = _model.GetSignature(0);
                var input = signature.GetInputTensorType(0);
                var output = signature.GetOutputTensorType(0);
                if (signature.InputCount != 1 || signature.OutputCount != 1 || input.ElementType != ElementType.Float32 ||
                    !input.Dimensions.SequenceEqual(new[] { 1, 3, InputHeight, width }) ||
                    output.ElementType != ElementType.Float32 || !output.Dimensions.SequenceEqual(new[] { 1, width / 8, CharacterCount }))
                    throw new ArgumentException("Expected the PP-OCRv6 Small recognizer for width " + width + ": float32 [1,3,48," + width + "] in, [1," + width / 8 + ",18710] out.", nameof(modelPath));
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

        /// <summary>The input buffer for the normalized planar BGR line.</summary>
        public TensorBuffer InputBuffer
        {
            get { if (_disposed) throw new ObjectDisposedException(nameof(PpOcrRecModel)); return _inputs[0]; }
        }

        /// <summary>Run the graph on what is in <see cref="InputBuffer"/>.</summary>
        /// <returns>The probabilities, [steps x 18710], step by step</returns>
        public float[] Run()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PpOcrRecModel));
            _compiled.Run(_inputs, _outputs);
            return _outputs[0].Read<float>();
        }

        /// <summary>Read the dictionary (characters.json: a list of 18,710 strings, "blank" first).</summary>
        /// <param name="path">The file</param>
        public static string[] LoadCharacters(string path)
        {
            string[] characters = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path, Encoding.UTF8));
            if (characters == null || characters.Length != CharacterCount)
                throw new InvalidDataException("Expected a dictionary of " + CharacterCount + " entries.");
            return characters;
        }

        /// <summary>
        /// Greedy CTC as PaddleOCR reads it: each time step's most likely entry, repeats collapsed, the blank (0) removed.
        /// The probabilities are already softmax, so none is applied.
        /// </summary>
        /// <param name="probabilities">[steps x characters.Length], step by step</param>
        /// <param name="steps">The number of time steps</param>
        /// <param name="characters">The dictionary</param>
        /// <param name="score">The mean probability of the entries kept (0 for an empty text)</param>
        public static string Decode(float[] probabilities, int steps, IReadOnlyList<string> characters, out float score)
        {
            if (probabilities == null || characters == null || steps < 1 || probabilities.Length != steps * characters.Count)
                throw new ArgumentException("Expected steps x characters probabilities.");
            var text = new StringBuilder();
            int previous = -1, kept = 0;
            float total = 0;
            for (int step = 0; step < steps; step++)
            {
                int best = 0;
                float top = float.MinValue;
                for (int c = 0; c < characters.Count; c++)
                {
                    float p = probabilities[step * characters.Count + c];
                    if (p > top) { top = p; best = c; }
                }
                if (best != previous && best != 0)
                {
                    text.Append(characters[best]);
                    total += top;
                    kept++;
                }
                previous = best;
            }
            score = kept == 0 ? 0 : total / kept;
            return text.ToString();
        }

        /// <summary>Release the graph.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _compiled?.Dispose();
            _model?.Dispose();
            _environment?.Dispose();
        }
    }
}
