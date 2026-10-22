// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Util;
using Emgu.CV;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Tries the GPU first (Metal on Mac Catalyst, OpenCL on Android) with explicit FP32 - the default precision failed the
// upstream parity check on both - and keeps it only if it matches the CPU's detections on a bundled image and is
// faster; otherwise the CPU detector is used (GpuFailure says why). See Emgu.LiteRT.Models/YOLOX_EVALUATION.md.
/// <summary>YOLOX-Nano object detector (80 COCO classes). Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class YoloXDetector : ICocoDetector
{
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the weights are downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name (e.g. "surfers.jpg"), used to check a GPU's
    /// results against the CPU's before trusting it. Without it the GPU is never used.</param>
    public YoloXDetector(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    internal const string ModelUrl = "https://emgu-public.s3.amazonaws.com/yolox/yolox_nano_float32.tflite";
    internal const string Asset = "yolox_nano_float32.tflite";
    internal const string AssetSha256 = "4BE8F0E9E203E8150738DB6B3E8486D49B3C3D16C46EBA6ED3CCB70BB215FE00";
    private YoloX _model;
    /// <inheritdoc/>
    public bool Imported => _model != null;
    /// <inheritdoc/>
    public string Backend { get; private set; }
    /// <inheritdoc/>
    public string GpuFailure { get; private set; }

    /// <inheritdoc/>
    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(_cacheDirectory, Asset);
        var weights = new DownloadableFile(ModelUrl, "YoloX", AssetSha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading YOLOX-Nano: {percent:0}%" : "Downloading YOLOX-Nano…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("YOLOX-Nano download failed or did not match its SHA-256 checksum. Check the connection and retry.");

        GpuFailure = null;
        if (preferGpu && CocoDetector.CanTryGpu && _validationImage != null)
        {
            try
            {
                await CompileAsync(file, HwAccelerators.Gpu | HwAccelerators.Cpu, numThreads, progress, token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Dispose();
                GpuFailure = ex.Message;
                progress?.Invoke("YOLOX GPU unavailable or failed validation; preparing CPU detector…");
            }
        }
        await CompileAsync(file, HwAccelerators.Cpu, numThreads, progress, token).ConfigureAwait(false);
    }

    private async Task CompileAsync(string file, HwAccelerators accelerator, int numThreads, Action<string> progress, CancellationToken token)
    {
        bool gpu = (accelerator & HwAccelerators.Gpu) != 0;
        progress?.Invoke($"Preparing YOLOX-Nano on {(gpu ? "GPU" : "CPU")}…");
        var candidate = await Task.Run(() => new YoloX(file, numThreads, accelerator, gpu ? GpuPrecision.Fp32 : null), token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (gpu)
                await ValidateGpuAsync(candidate, file, numThreads, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Backend = gpu ? (candidate.IsFullyAccelerated ? "GPU" : "GPU + CPU") : "CPU";
            _model = candidate;
            candidate = null;
            progress?.Invoke($"YOLOX-Nano ready · LiteRT {Backend}");
        }
        finally { candidate?.Dispose(); }
    }

    // Compile can succeed and the results still differ (default precision did): compare a bundled known image with the
    // CPU's, then check that the GPU is actually faster before using it for real alerts.
    private async Task ValidateGpuAsync(YoloX gpu, string file, int numThreads, Action<string> progress, CancellationToken token)
    {
        using var cpu = await Task.Run(() => new YoloX(file, numThreads), token).ConfigureAwait(false);
        using var image = await _validationImage("surfers.jpg").ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image " + "surfers.jpg" + " is not available.");
        await Task.Run(() =>
        {
            var expected = Detect(cpu, image);
            var unmatched = Detect(gpu, image).ToList();
            // As in the upstream parity test: count, class, score within 0.01 and box within 0.005 (normalized).
            bool matches = expected.Length > 0 && expected.Length == unmatched.Count;
            foreach (var detection in expected)
            {
                int index = matches ? unmatched.FindIndex(r => r.Class == detection.Class
                    && Math.Abs(r.Score - detection.Score) <= 0.01
                    && Enumerable.Range(0, 4).All(k => Math.Abs(r.Rectangle[k] - detection.Rectangle[k]) <= 0.005)) : -1;
                if (index < 0) { matches = false; break; }
                unmatched.RemoveAt(index);
            }
            if (!matches) throw new InvalidOperationException("GPU accuracy check failed for YOLOX-Nano.");

            progress?.Invoke("Checking YOLOX GPU speed against CPU…");
            using var benchmarkImage = new Mat();
            CvInvoke.Resize(image, benchmarkImage, new System.Drawing.Size(640, 480));
            var timing = CocoBackendBenchmark.Compare(() => Detect(cpu, benchmarkImage), () => Detect(gpu, benchmarkImage), token);
            if (!timing.UseGpu)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"GPU speed check failed for YOLOX-Nano: CPU {timing.CpuMilliseconds:F2} ms, GPU {timing.GpuMilliseconds:F2} ms. Keeping CPU."));
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    /// <inheritdoc/>
    public CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare YOLOX-Nano first.");
        return Detect(_model, bgr).Select(r => new CocoSsdMobilenet.RecognitionResult
            { Class = r.Class, Label = r.Label, Score = r.Score, Rectangle = r.Rectangle }).ToArray();
    }

    private static YoloX.Detection[] Detect(YoloX model, Mat bgr)
    {
        YoloXImageInput.Write(model, bgr);
        return model.Run(bgr.Width, bgr.Height);
    }

    /// <inheritdoc/>
    public void Dispose() { _model?.Dispose(); _model = null; Backend = null; }
}
