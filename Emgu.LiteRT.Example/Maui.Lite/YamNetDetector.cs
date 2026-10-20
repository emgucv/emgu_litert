// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;

namespace Maui.Demo.Lite;

// YAMNet (521 sound classes) for the demo: downloads Google's MediaPipe copy (4.1 MB, SHA-256 checked, not copied to Emgu's server) and
// runs it on the CPU - a model this small gains nothing from the GPU. See YAMNET.md.
internal sealed class YamNetDetector : IDisposable
{
    private YamNet _model;
    public bool Imported => _model != null;

    public async Task PrepareAsync(int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(FileSystem.CacheDirectory, "yamnet.tflite");
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

    /// <summary>All 521 scores for a window of 15,600 samples.</summary>
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

    public void Dispose() { _model?.Dispose(); _model = null; }
}
