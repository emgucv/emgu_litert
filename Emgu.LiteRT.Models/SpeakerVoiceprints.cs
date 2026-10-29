// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Voice fingerprints of recorded speech, for telling speakers apart (e.g. Emgu.LiteRT.LM.Agents' MeetingTranscript and
    /// SpeakerClustering): <see cref="SpeakerEmbedder"/>'s model, downloaded on first use (13.4 MB), on the GPU when its
    /// fingerprints match the CPU's and it is faster, and a fingerprint for speech of any length - the average of up to six
    /// 5 s stretches of a longer piece. Callers serialize <c>PrepareAsync</c>, <c>Voiceprint</c> and <c>Dispose</c>.
    /// </summary>
    public sealed class SpeakerVoiceprints : IDisposable
    {
        /// <summary>The shortest speech that gets a fingerprint, in seconds.</summary>
        public const double MinSeconds = 0.8;
        /// <summary>The most 5 s stretches averaged for one fingerprint.</summary>
        public const int MaxStretches = 6;
        /// <summary>The cosine similarity with the CPU's fingerprint a GPU must reach.</summary>
        public const double MinGpuSimilarity = 0.999;

        private readonly string _cacheDirectory;
        private SpeakerEmbedder _model;

        /// <summary>Create it.</summary>
        /// <param name="cacheDirectory">Where the model is downloaded to (default: a folder in the temp path)</param>
        public SpeakerVoiceprints(string cacheDirectory = null)
        {
            _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Models");
        }

        /// <summary>The model file's path.</summary>
        public string ModelPath => Path.Combine(_cacheDirectory, "wespeaker_emb_fp16.tflite");

        /// <summary>Whether the model is ready.</summary>
        public bool Imported => _model != null;

        /// <summary>"GPU", "GPU + CPU" or "CPU" once ready.</summary>
        public string Backend { get; private set; }

        /// <summary>Why the GPU isn't used, when it was tried and failed.</summary>
        public string GpuFailure { get; private set; }

        /// <summary>Download the model if needed and compile it (the GPU when it passes its checks).</summary>
        /// <param name="preferGpu">Try the GPU first</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="progress">Status lines, or null</param>
        /// <param name="token">Cancels it</param>
        public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
        {
            if (Imported) return;
            DownloadableFile weights = SpeakerEmbedder.ModelFile;
            weights.LocalFile = ModelPath;
            var downloads = new FileDownloadManager();
            downloads.AddFile(weights);
            downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
                ? $"Downloading the voice model: {percent:0}%" : "Downloading the voice model…");
            await downloads.Download(1, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!downloads.AllFilesDownloaded)
                throw new IOException("The voice model's download failed or did not match its SHA-256 checksum. Check the connection and retry.");
            GpuFailure = null;
            if (preferGpu)
            {
                try
                {
                    _model = await Task.Run(() => CompileGpu(numThreads), token).ConfigureAwait(false);
                    Backend = _model.IsFullyAccelerated ? "GPU" : "GPU + CPU";
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    GpuFailure = ex.Message;
                    progress?.Invoke("The voice model's GPU is unavailable or failed its check; using the CPU…");
                }
            }
            _model = await Task.Run(() => new SpeakerEmbedder(ModelPath, numThreads), token).ConfigureAwait(false);
            Backend = "CPU";
        }

        // The GPU at FP16, kept if its fingerprint of a test sound matches the CPU's and it is faster.
        private SpeakerEmbedder CompileGpu(int numThreads)
        {
            var gpu = new SpeakerEmbedder(ModelPath, numThreads, HwAccelerators.Gpu | HwAccelerators.Cpu, GpuPrecision.Fp16);
            try
            {
                using (var cpu = new SpeakerEmbedder(ModelPath, numThreads))
                {
                    float[] sound = TestSound();
                    gpu.Embed(sound, 0, sound.Length); // the first run sets the GPU up
                    var watch = Stopwatch.StartNew();
                    float[] expected = cpu.Embed(sound, 0, sound.Length);
                    double cpuMs = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    float[] actual = gpu.Embed(sound, 0, sound.Length);
                    double gpuMs = watch.Elapsed.TotalMilliseconds;
                    double similarity = 0;
                    for (int i = 0; i < expected.Length; i++) similarity += expected[i] * actual[i];
                    if (!(similarity >= MinGpuSimilarity))
                        throw new InvalidOperationException(FormattableString.Invariant($"GPU accuracy check failed for the voice model (similarity {similarity:F4})."));
                    if (!(gpuMs < cpuMs))
                        throw new InvalidOperationException(FormattableString.Invariant($"GPU speed check failed for the voice model: CPU {cpuMs:F0} ms, GPU {gpuMs:F0} ms."));
                }
                return gpu;
            }
            catch
            {
                gpu.Dispose();
                throw;
            }
        }

        // Two seconds of a voice-like sound: a gliding pitch with harmonics and a slow wobble.
        internal static float[] TestSound()
        {
            var sound = new float[2 * SpeakerEmbedder.SampleRate];
            double phase = 0;
            for (int i = 0; i < sound.Length; i++)
            {
                double t = (double)i / SpeakerEmbedder.SampleRate;
                double pitch = 120 + 60 * Math.Sin(2 * Math.PI * 0.7 * t);
                phase += 2 * Math.PI * pitch / SpeakerEmbedder.SampleRate;
                double v = 0;
                for (int h = 1; h <= 8; h++) v += Math.Sin(h * phase) / h;
                sound[i] = (float)(0.2 * v * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 3 * t)));
            }
            return sound;
        }

        /// <summary>
        /// The fingerprint of 16 kHz mono speech in -1..1 (length 1), from its speech only - its 20 ms frames at least a
        /// tenth as loud as its loud parts and above -50 dBFS, so the silence or room noise around the words doesn't count -
        /// or null for less than <see cref="MinSeconds"/> of speech. More than 5 s of it: the average of up to
        /// <see cref="MaxStretches"/> 5 s stretches spread over it.
        /// </summary>
        /// <param name="samples">The recording</param>
        public float[] Voiceprint(float[] samples)
        {
            if (!Imported) throw new InvalidOperationException("Prepare the voice model first.");
            if (samples == null) return null;
            samples = Speech(samples);
            if (samples.Length < MinSeconds * SpeakerEmbedder.SampleRate) return null;
            int stretch = SpeakerEmbedder.InputSamples;
            if (samples.Length <= stretch)
                return _model.Embed(samples, 0, samples.Length);
            int count = Math.Min(MaxStretches, (samples.Length + stretch - 1) / stretch);
            var sum = new float[SpeakerEmbedder.Dimension];
            for (int k = 0; k < count; k++)
            {
                int start = (int)((long)(samples.Length - stretch) * k / Math.Max(1, count - 1));
                float[] e = _model.Embed(samples, start, stretch);
                for (int i = 0; i < sum.Length; i++) sum[i] += e[i];
            }
            return SpeakerEmbedder.Normalize(sum);
        }

        // The frames with speech in them, joined. (A piece that started before anyone spoke gave a fingerprint of the room,
        // which made a speaker of its own.)
        internal static float[] Speech(float[] samples)
        {
            const int frame = SpeakerEmbedder.SampleRate / 50;
            int frames = samples.Length / frame;
            if (frames == 0) return new float[0];
            var rms = new double[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int i = f * frame; i < (f + 1) * frame; i++)
                    sum += samples[i] * samples[i];
                rms[f] = Math.Sqrt(sum / frame);
            }
            double[] sorted = (double[])rms.Clone();
            Array.Sort(sorted);
            double floor = Math.Max(0.00316, 0.1 * sorted[(int)(0.95 * (frames - 1))]);
            var speech = new System.Collections.Generic.List<float>(samples.Length);
            for (int f = 0; f < frames; f++)
                if (rms[f] >= floor)
                    for (int i = f * frame; i < (f + 1) * frame; i++)
                        speech.Add(samples[i]);
            return speech.ToArray();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _model?.Dispose();
            _model = null;
            Backend = null;
        }
    }
}
