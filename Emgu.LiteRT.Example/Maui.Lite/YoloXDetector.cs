// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Util;
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// Initial experiment qualifies CPU only. V3's GPU qualification/fallback remains independent.
internal sealed class YoloXDetector : ICocoDetector
{
    internal const string ModelUrl = "https://emgu-public.s3.amazonaws.com/yolox/yolox_nano_float32.tflite";
    internal const string Asset = "yolox_nano_float32.tflite";
    internal const string AssetSha256 = "4BE8F0E9E203E8150738DB6B3E8486D49B3C3D16C46EBA6ED3CCB70BB215FE00";
    private YoloX _model;
    public bool Imported => _model != null;
    public string Backend => Imported ? "CPU" : null;
    public string GpuFailure => null;

    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(FileSystem.CacheDirectory, Asset);
        var weights = new DownloadableFile(ModelUrl, "YoloX", AssetSha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading YOLOX-Nano: {percent:0}%" : "Downloading YOLOX-Nano…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("YOLOX-Nano download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        progress?.Invoke("Preparing YOLOX-Nano on CPU…");
        var candidate = await Task.Run(() => new YoloX(file, numThreads), token).ConfigureAwait(false);
        if (token.IsCancellationRequested) { candidate.Dispose(); token.ThrowIfCancellationRequested(); }
        _model = candidate;
        progress?.Invoke("YOLOX-Nano ready · LiteRT CPU");
    }

    public CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare YOLOX-Nano first.");
        YoloXImageInput.Write(_model, bgr);
        return _model.Run(bgr.Width, bgr.Height).Select(r => new CocoSsdMobilenet.RecognitionResult
            { Class = r.Class, Label = r.Label, Score = r.Score, Rectangle = r.Rectangle }).ToArray();
    }
    public void Dispose() { _model?.Dispose(); _model = null; }
}
