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
    /// <see cref="YamNet"/> with its download: <see cref="PrepareAsync"/> downloads Google's MediaPipe copy of the model (4.1 MB,
    /// checked against <see cref="YamNet.ModelSha256"/>) to a cache folder and loads it on the CPU - a model this small gains
    /// nothing from the GPU. Callers serialize <c>PrepareAsync</c>, the classify calls and <c>Dispose</c>.
    /// </summary>
    public sealed class YamNetClassifier : IDisposable
    {
        private readonly string _cacheDirectory;
        private YamNet _model;

        /// <summary>Creates the classifier.</summary>
        /// <param name="cacheDirectory">Where the model is downloaded to (default: a folder in the temp path).</param>
        public YamNetClassifier(string cacheDirectory = null)
        {
            _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Models");
        }

        /// <summary>True once <see cref="PrepareAsync"/> has loaded the model.</summary>
        public bool Imported => _model != null;

        /// <summary>Downloads the model if needed and loads it on a background thread.</summary>
        /// <param name="numThreads">CPU threads for the model.</param>
        /// <param name="progress">Receives status text, e.g. the download percentage.</param>
        /// <param name="token">Cancels the download or the load.</param>
        public async Task PrepareAsync(int numThreads = 2, Action<string> progress = null, CancellationToken token = default)
        {
            if (Imported) return;
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_cacheDirectory);
            string file = Path.Combine(_cacheDirectory, "yamnet.tflite");
            var weights = new DownloadableFile(YamNet.ModelUrl, "YamNet", YamNet.ModelSha256) { LocalFile = file };
            var downloads = new FileDownloadManager();
            downloads.AddFile(weights);
            downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
                ? $"Downloading YAMNet: {percent:0}%" : "Downloading YAMNet…");
            await downloads.Download(1, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!downloads.AllFilesDownloaded)
                throw new IOException("The YAMNet download failed or did not match its SHA-256 checksum. Check the connection and retry.");
            progress?.Invoke("Preparing YAMNet…");
            _model = await Task.Run(() => new YamNet(file, numThreads), token).ConfigureAwait(false);
            progress?.Invoke("YAMNet ready · LiteRT CPU");
        }

        /// <summary>The 521 class names, once prepared.</summary>
        public IReadOnlyList<string> Labels => _model?.Labels;

        /// <summary>All 521 scores for a window of 15,600 samples (16 kHz mono).</summary>
        public float[] Scores(float[] window)
        {
            if (!Imported) throw new InvalidOperationException("Prepare YAMNet first.");
            return _model.Classify(window);
        }

        /// <summary>The sounds in 15,600 samples (16 kHz mono) starting at <paramref name="offset"/>, best first.</summary>
        public IReadOnlyList<SoundScore> Classify(float[] samples, int offset, int count = 5)
        {
            if (!Imported) throw new InvalidOperationException("Prepare YAMNet first.");
            return _model.Top(_model.Classify(samples, offset), count);
        }

        /// <summary>Releases the model.</summary>
        public void Dispose() { _model?.Dispose(); _model = null; }
    }
}
