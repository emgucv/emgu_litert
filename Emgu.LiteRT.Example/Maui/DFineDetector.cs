// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Util;
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// D-FINE-N at 640 px on the CPU. The GPU compilers reject the graph (DFINE_EVALUATION.md), and it's 1.4-2.7x slower than
// YOLOX-Nano, so this is for single photos where accuracy matters more than speed (COCO AP 42.8 vs 25.8), on desktops.
internal sealed class DFineDetector : ICocoDetector
{
    internal const string ModelUrl = "https://emgu-public.s3.amazonaws.com/dfine/dfine_n_coco_640_float32.tflite";
    internal const string Asset = "dfine_n_coco_640_float32.tflite";
    internal const string AssetSha256 = "1F7C2FA937E414D4D728B007C4295ADDB106FD56C113EA188155193D2A6E80BC";
    private DFine _model;
    public bool Imported => _model != null;
    public string Backend => Imported ? "CPU" : null;
    public string GpuFailure => null;

    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(FileSystem.CacheDirectory, Asset);
        var weights = new DownloadableFile(ModelUrl, "DFine", AssetSha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading D-FINE-N: {percent:0}%" : "Downloading D-FINE-N…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("D-FINE-N download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        progress?.Invoke("Preparing D-FINE-N on CPU…");
        _model = await Task.Run(() => new DFine(file, numThreads), token).ConfigureAwait(false);
        progress?.Invoke("D-FINE-N ready · LiteRT CPU");
    }

    public CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare D-FINE-N first.");
        DFineImageInput.Write(_model, bgr);
        return _model.Run().Select(r => new CocoSsdMobilenet.RecognitionResult
            { Class = r.Class, Label = r.Label, Score = r.Score, Rectangle = r.Rectangle }).ToArray();
    }

    public void Dispose() { _model?.Dispose(); _model = null; }
}
