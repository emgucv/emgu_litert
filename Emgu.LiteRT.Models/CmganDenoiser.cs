// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// CMGAN (a 1.83M-parameter conformer, Cao et al., TASLP 2024, MIT, trained on VoiceBank-DEMAND) removing background
    /// noise, from Google's LiteRT export litert-community/CMGAN-LiteRT (4.2 MB, FP16 weights). The model takes a 2 s chunk,
    /// RMS-normalized and reflect-padded by 200 samples, computes its STFT itself and returns the cleaned spectrogram,
    /// compressed (magnitude^0.3); this class un-compresses it, takes the inverse STFT (n_fft 400, hop 100, periodic Hamming)
    /// and joins chunks overlapping by half. GPU only: XNNPACK declines the graph. Run it at FP32 - FP16 on an M1's GPU
    /// garbled some chunks (a piece's SI-SNR fell from 15 to -3 dB); at FP32 the mean rose from 15.0 to 21.0 dB.
    /// </summary>
    public sealed class CmganDenoiser : SpeechDenoiser
    {
        /// <summary>The samples in one chunk the model takes (2 s).</summary>
        public const int ChunkSamples = 32000;

        private const int Nfft = 400, Hop = 100, Frames = 321, Bins = 201, Pad = Nfft / 2;

        private readonly Environment _environment;
        private readonly Model _model;
        private readonly CompiledModel _compiled;
        private readonly TensorBuffer[] _inputs, _outputs;
        private readonly float[] _cos = new float[Bins * Nfft], _sin = new float[Bins * Nfft], _window = new float[Nfft];
        private bool _disposed;

        /// <summary>The model file (4.2 MB), at a fixed revision of litert-community/CMGAN-LiteRT.</summary>
        public static DownloadableFile ModelFile => new DownloadableFile(
            "https://huggingface.co/litert-community/CMGAN-LiteRT/resolve/f0275a7644d50253a11744c09073167536670499/cmgan_fp16.tflite",
            "CMGAN",
            "4039920c3922c74f564a06f46fdaca3cca7be81f3b367992424585f20f776b4a");

        /// <summary>Download the model if needed.</summary>
        /// <param name="cacheDirectory">Where to (default: a folder in the temp path)</param>
        /// <param name="progress">Status lines, or null</param>
        /// <param name="token">Cancels it</param>
        /// <returns>The model file's path</returns>
        public static async Task<string> DownloadAsync(string cacheDirectory = null, Action<string> progress = null, CancellationToken token = default)
        {
            string[] paths = await DownloadAsync(new[] { (ModelFile, "cmgan_fp16.tflite") }, cacheDirectory, "noise removal model", progress, token)
                .ConfigureAwait(false);
            return paths[0];
        }

        /// <summary>Compile the model on the GPU (about a second; off the UI thread).</summary>
        /// <param name="modelPath">The .tflite file (<see cref="DownloadAsync"/>)</param>
        /// <param name="precision">The GPU's precision: FP32 (FP16 garbled some chunks on an M1)</param>
        /// <exception cref="Exception">No GPU accelerator, or not this model</exception>
        public CmganDenoiser(string modelPath, GpuPrecision precision = GpuPrecision.Fp32)
        {
            try
            {
                _environment = new Environment();
                _model = new Model(_environment, modelPath);
                Signature signature = _model.GetSignature(0);
                if (signature.InputCount != 1 || signature.OutputCount != 2
                    || !signature.GetInputTensorType(0).Dimensions.SequenceEqual(new[] { 1, ChunkSamples + 2 * Pad })
                    || !signature.GetOutputTensorType(0).Dimensions.SequenceEqual(new[] { 1, 1, Frames, Bins })
                    || !signature.GetOutputTensorType(1).Dimensions.SequenceEqual(new[] { 1, 1, Frames, Bins }))
                    throw new ArgumentException("Expected CMGAN-LiteRT: a [1,32400] input and two [1,1,321,201] outputs.", nameof(modelPath));
                using (var options = new Options(HwAccelerators.Gpu | HwAccelerators.Cpu))
                {
                    options.AddGpuOptions(new GpuOptions { Precision = precision });
                    _compiled = new CompiledModel(_environment, _model, options);
                }
                _inputs = _compiled.CreateInputBuffers();
                _outputs = _compiled.CreateOutputBuffers();
            }
            catch { Dispose(); throw; }
            for (int k = 0; k < Bins; k++)
                for (int n = 0; n < Nfft; n++)
                {
                    _cos[k * Nfft + n] = (float)Math.Cos(2 * Math.PI * k * n / Nfft);
                    _sin[k * Nfft + n] = (float)Math.Sin(2 * Math.PI * k * n / Nfft);
                }
            for (int n = 0; n < Nfft; n++) _window[n] = (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * n / Nfft));
        }

        /// <summary>Whether LiteRT reports the whole graph on the GPU.</summary>
        public bool IsFullyAccelerated
        {
            get { CheckActive(); return _compiled.IsFullyAccelerated; }
        }

        /// <summary>The audio without its background noise: 2 s chunks overlapping by half, cross-faded.</summary>
        /// <param name="samples">16 kHz mono samples, -1..1</param>
        public override float[] Denoise(float[] samples)
        {
            CheckActive();
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            int hop = ChunkSamples / 2;
            var sum = new double[samples.Length + ChunkSamples];
            var weight = new double[samples.Length + ChunkSamples];
            var chunk = new float[ChunkSamples];
            for (int at = 0; at < samples.Length; at += hop)
            {
                Array.Clear(chunk, 0, chunk.Length);
                Array.Copy(samples, at, chunk, 0, Math.Min(ChunkSamples, samples.Length - at));
                float[] clean = DenoiseChunk(chunk);
                for (int n = 0; n < ChunkSamples; n++)
                {
                    // Squared sine: overlapping halves add up to one; a little everywhere so the ends aren't divided by 0.
                    double s = Math.Sin(Math.PI * (n + 0.5) / ChunkSamples), w = s * s + 1e-3;
                    sum[at + n] += clean[n] * w;
                    weight[at + n] += w;
                }
                if (at + ChunkSamples >= samples.Length) break;
            }
            var result = new float[samples.Length];
            for (int n = 0; n < result.Length; n++) result[n] = (float)(sum[n] / weight[n]);
            return result;
        }

        /// <summary>One 2 s chunk without its background noise.</summary>
        /// <param name="chunk"><see cref="ChunkSamples"/> samples (fewer are padded with silence)</param>
        /// <returns><see cref="ChunkSamples"/> samples</returns>
        public float[] DenoiseChunk(float[] chunk)
        {
            CheckActive();
            if (chunk == null) throw new ArgumentNullException(nameof(chunk));
            int length = Math.Min(chunk.Length, ChunkSamples);
            double energy = 0;
            for (int n = 0; n < length; n++) energy += chunk[n] * chunk[n];
            if (energy < 1e-10) return new float[ChunkSamples];
            // RMS-normalized, then reflect-padded by half an FFT on both sides (torch.stft's center=True).
            double c = Math.Sqrt(ChunkSamples / energy);
            var padded = new float[ChunkSamples + 2 * Pad];
            for (int n = 0; n < length; n++) padded[n + Pad] = (float)(chunk[n] * c);
            for (int n = 1; n <= Pad; n++)
            {
                padded[Pad - n] = padded[Pad + n];
                padded[Pad + ChunkSamples - 1 + n] = padded[Pad + ChunkSamples - 1 - n];
            }
            _inputs[0].Write(padded);
            _compiled.Run(_inputs, _outputs);
            float[] real = _outputs[0].Read<float>(), imaginary = _outputs[1].Read<float>();

            // Un-compress (|X|^0.3 back to |X|: scale by |X'|^(7/3)), inverse real DFT of each frame, windowed overlap-add.
            var sum = new double[ChunkSamples + 2 * Pad];
            var norm = new double[ChunkSamples + 2 * Pad];
            var frame = new double[Nfft];
            for (int t = 0; t < Frames; t++)
            {
                Array.Clear(frame, 0, Nfft);
                for (int k = 0; k < Bins; k++)
                {
                    double re = real[t * Bins + k], im = imaginary[t * Bins + k];
                    double scale = Math.Pow(Math.Max(re * re + im * im, 1e-12), 7.0 / 6.0);
                    re *= scale;
                    im *= scale;
                    if (k == 0 || k == Bins - 1) im = 0;
                    else { re *= 2; im *= 2; }
                    int row = k * Nfft;
                    for (int n = 0; n < Nfft; n++)
                        frame[n] += re * _cos[row + n] - im * _sin[row + n];
                }
                for (int n = 0; n < Nfft; n++)
                {
                    int at = t * Hop + n;
                    if (at >= sum.Length) break;
                    sum[at] += frame[n] / Nfft * _window[n];
                    norm[at] += _window[n] * _window[n];
                }
            }
            var result = new float[ChunkSamples];
            for (int n = 0; n < ChunkSamples; n++)
                result[n] = (float)(sum[n + Pad] / Math.Max(norm[n + Pad], 1e-8) / c);
            return result;
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CmganDenoiser));
        }

        /// <inheritdoc/>
        public override void Dispose()
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
