// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// A voice's fingerprint: WeSpeaker's ResNet34 speaker embedding (trained on VoxCeleb; the weights are CC-BY-4.0 -
    /// credit WeSpeaker and pyannote where the app lists its models), 256 values, L2-normalized, from up to 5 s of
    /// 16 kHz mono speech. Two stretches of one voice have a high cosine similarity; Emgu.LiteRT.LM.Agents' SpeakerClustering
    /// groups them by voice. The LiteRT export of https://huggingface.co/litert-community/Speaker-Diarization-LiteRT
    /// (pyannote 3.1's recipe), input [1,500,80] Kaldi log-mel features with each bin's mean removed. Calls are
    /// serialized by the host.
    /// </summary>
    public sealed class SpeakerEmbedder : IDisposable
    {
        /// <summary>The sample rate the model takes.</summary>
        public const int SampleRate = 16000;
        /// <summary>The model's input: 500 frames of 10 ms (80,240 samples, 5.015 s). Shorter speech is repeated to fill it.</summary>
        public const int InputSamples = 80240;
        /// <summary>The input's frames.</summary>
        public const int Frames = 500;
        /// <summary>The input's mel bins.</summary>
        public const int MelBins = 80;
        /// <summary>The embedding's length.</summary>
        public const int Dimension = 256;

        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private readonly KaldiFbank _fbank = new KaldiFbank(SampleRate, MelBins);
        private bool _disposed;

        /// <summary>The model file (13.4 MB, FP16 weights).</summary>
        public static DownloadableFile ModelFile => new DownloadableFile(
            "https://huggingface.co/litert-community/Speaker-Diarization-LiteRT/resolve/main/wespeaker_emb_fp16.tflite",
            "SpeakerDiarization",
            "4853c284fdb3e39bd41f692d0bc3fd5068bf78782792d8ef02a3283c9d97554d");

        /// <summary>Compile the model (off the UI thread).</summary>
        /// <param name="modelPath">The .tflite file</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="accelerators">Cpu, or Gpu | Cpu</param>
        /// <param name="gpuPrecision">The GPU's precision</param>
        public SpeakerEmbedder(string modelPath, int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu,
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
                    !input.Dimensions.SequenceEqual(new[] { 1, Frames, MelBins }) ||
                    output.ElementType != ElementType.Float32 || !output.Dimensions.SequenceEqual(new[] { 1, Dimension }))
                    throw new ArgumentException("Expected WeSpeaker with a float32 [1,500,80] input and a [1,256] output.", nameof(modelPath));
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

        /// <summary>
        /// The fingerprint of a stretch of 16 kHz mono speech in -1..1 (at most 5.015 s is used; shorter speech is
        /// repeated to fill the input, as the reference does): 256 values with length 1.
        /// </summary>
        /// <param name="audio">The samples</param>
        /// <param name="offset">The stretch's first sample</param>
        /// <param name="count">Its length (at least 25 ms)</param>
        public float[] Embed(float[] audio, int offset, int count)
        {
            CheckActive();
            _inputs[0].Write(Features(audio, offset, count));
            _compiled.Run(_inputs, _outputs);
            return Normalize(_outputs[0].Read<float>());
        }

        /// <summary>The model's input for a stretch: Kaldi features of 5.015 s (repeated to fill), each bin's mean removed.</summary>
        /// <param name="audio">The samples</param>
        /// <param name="offset">The stretch's first sample</param>
        /// <param name="count">Its length</param>
        public float[] Features(float[] audio, int offset, int count)
        {
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            if (offset < 0 || count < 400 || offset + count > audio.Length) throw new ArgumentOutOfRangeException(nameof(count));
            var filled = new float[InputSamples];
            for (int i = 0; i < InputSamples; i++)
                filled[i] = audio[offset + i % count];
            float[] features = _fbank.Compute(filled, 0, InputSamples);
            for (int m = 0; m < MelBins; m++)
            {
                double mean = 0;
                for (int f = 0; f < Frames; f++) mean += features[f * MelBins + m];
                mean /= Frames;
                for (int f = 0; f < Frames; f++) features[f * MelBins + m] -= (float)mean;
            }
            return features;
        }

        /// <summary>A vector scaled to length 1 (all zeros stays zeros).</summary>
        /// <param name="v">The vector</param>
        public static float[] Normalize(float[] v)
        {
            double sum = 0;
            foreach (float x in v) sum += x * x;
            double length = Math.Sqrt(sum);
            var result = new float[v.Length];
            if (length > 0)
                for (int i = 0; i < v.Length; i++) result[i] = (float)(v[i] / length);
            return result;
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpeakerEmbedder));
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
