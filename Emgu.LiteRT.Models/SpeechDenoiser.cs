// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Removes background noise from 16 kHz mono speech: <see cref="CmganDenoiser"/> (CMGAN, 2 s chunks on the GPU) or
    /// <see cref="DtlnDenoiser"/> (DTLN, streaming in 8 ms steps on the CPU). Both lifted noisy speech from 15 to 21 dB
    /// SI-SNR, but neither helped a speech model write a recording down: Gemma 4 E2B's errors on noisy synthetic speech
    /// stayed within a word, and on a phone's distant, quiet recording they grew from 12% to 56% (DTLN) and 289% (CMGAN,
    /// invented text) - see MODELS.md. Meant for audio people listen to, not before speech recognition. Not thread-safe.
    /// </summary>
    public abstract class SpeechDenoiser : IDisposable
    {
        /// <summary>The sample rate of the audio it takes and gives.</summary>
        public const int SampleRate = 16000;

        /// <summary>The audio without its background noise, as long as the input.</summary>
        /// <param name="samples">16 kHz mono samples, -1..1</param>
        public abstract float[] Denoise(float[] samples);

        /// <inheritdoc/>
        public abstract void Dispose();

        /// <summary>Download model files if needed, checking their SHA-256.</summary>
        /// <param name="files">The files and the names to save them as</param>
        /// <param name="cacheDirectory">Where to (default: a folder in the temp path)</param>
        /// <param name="what">What they are, for the progress lines</param>
        /// <param name="progress">Status lines, or null</param>
        /// <param name="token">Cancels it</param>
        /// <returns>The files' paths, in order</returns>
        protected static async Task<string[]> DownloadAsync(IReadOnlyList<(DownloadableFile File, string Name)> files, string cacheDirectory,
            string what, Action<string> progress, CancellationToken token)
        {
            cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Models");
            var downloads = new FileDownloadManager();
            var paths = new string[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                paths[i] = Path.Combine(cacheDirectory, files[i].Name);
                files[i].File.LocalFile = paths[i];
                downloads.AddFile(files[i].File);
            }
            downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
                ? $"Downloading the {what}: {percent:0}%" : $"Downloading the {what}…");
            await downloads.Download(1, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!downloads.AllFilesDownloaded)
                throw new IOException($"The {what}'s download failed or did not match its SHA-256 checksum. Check the connection and retry.");
            return paths;
        }
    }
}
