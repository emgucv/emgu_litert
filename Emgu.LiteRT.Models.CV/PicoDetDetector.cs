// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.Util;
using Emgu.CV;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Tries the GPU first (Metal on Mac Catalyst, OpenCL on Android) with explicit FP32 and keeps it only if it matches the
// CPU's detections on a bundled image and is faster; otherwise the CPU detector is used (GpuFailure says why). The model's
// HardSigmoid ops are rewritten at conversion time, since the GPU compilers reject the RELU_0_TO_1 they became. See
// PICODET_EVALUATION.md.
/// <summary>PP-PicoDet-S object detector (80 COCO classes). Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class PicoDetDetector : ICocoDetector
{
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the weights are downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name (e.g. "surfers.jpg"), used to check a GPU's
    /// results against the CPU's before trusting it. Without it the GPU is never used.</param>
    public PicoDetDetector(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    internal const string ModelUrl = "https://emgu-public.s3.amazonaws.com/picodet/picodet_s_416_coco_float32.tflite";
    internal const string Asset = "picodet_s_416_coco_float32.tflite";
    internal const string AssetSha256 = "68870E3D997E53FE6A08297B653F9188543D1D8BA06D22D35B25877B5AE6E64E";
    private PicoDet _model;
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
        var weights = new DownloadableFile(ModelUrl, "PicoDet", AssetSha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading PicoDet-S: {percent:0}%" : "Downloading PicoDet-S…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("PicoDet-S download failed or did not match its SHA-256 checksum. Check the connection and retry.");

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
                progress?.Invoke("PicoDet GPU unavailable or failed validation; preparing CPU detector…");
            }
        }
        await CompileAsync(file, HwAccelerators.Cpu, numThreads, progress, token).ConfigureAwait(false);
    }

    private async Task CompileAsync(string file, HwAccelerators accelerator, int numThreads, Action<string> progress, CancellationToken token)
    {
        bool gpu = (accelerator & HwAccelerators.Gpu) != 0;
        progress?.Invoke($"Preparing PicoDet-S on {(gpu ? "GPU" : "CPU")}…");
        var candidate = await Task.Run(() => new PicoDet(file, numThreads, accelerator, gpu ? GpuPrecision.Fp32 : null), token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (gpu)
                await ValidateGpuAsync(candidate, file, numThreads, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Backend = gpu ? (candidate.IsFullyAccelerated ? "GPU" : "GPU + CPU") : "CPU";
            _model = candidate;
            candidate = null;
            progress?.Invoke($"PicoDet-S ready · LiteRT {Backend}");
        }
        finally { candidate?.Dispose(); }
    }

    // Compile can succeed and the results still differ (default precision did): compare a bundled known image with the
    // CPU's, then check that the GPU is actually faster before using it for real alerts.
    private async Task ValidateGpuAsync(PicoDet gpu, string file, int numThreads, Action<string> progress, CancellationToken token)
    {
        using var cpu = await Task.Run(() => new PicoDet(file, numThreads), token).ConfigureAwait(false);
        using var image = await _validationImage("surfers.jpg").ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image " + "surfers.jpg" + " is not available.");
        await Task.Run(() =>
        {
            var expected = Detect(cpu, image);
            var unmatched = Detect(gpu, image).ToList();
            // Count, class, score within 0.01 and box within 0.005 (normalized), as for YOLOX.
            bool matches = expected.Length > 0 && expected.Length == unmatched.Count;
            foreach (var detection in expected)
            {
                int index = matches ? unmatched.FindIndex(r => r.Class == detection.Class
                    && Math.Abs(r.Score - detection.Score) <= 0.01
                    && Enumerable.Range(0, 4).All(k => Math.Abs(r.Rectangle[k] - detection.Rectangle[k]) <= 0.005)) : -1;
                if (index < 0) { matches = false; break; }
                unmatched.RemoveAt(index);
            }
            if (!matches) throw new InvalidOperationException("GPU accuracy check failed for PicoDet-S.");

            progress?.Invoke("Checking PicoDet GPU speed against CPU…");
            using var benchmarkImage = new Mat();
            CvInvoke.Resize(image, benchmarkImage, new System.Drawing.Size(640, 480));
            var timing = CocoBackendBenchmark.Compare(() => Detect(cpu, benchmarkImage), () => Detect(gpu, benchmarkImage), token);
            if (!timing.UseGpu)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"GPU speed check failed for PicoDet-S: CPU {timing.CpuMilliseconds:F2} ms, GPU {timing.GpuMilliseconds:F2} ms. Keeping CPU."));
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    /// <inheritdoc/>
    public CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare PicoDet-S first.");
        return Detect(_model, bgr).Select(r => new CocoSsdMobilenet.RecognitionResult
            { Class = r.Class, Label = r.Label, Score = r.Score, Rectangle = r.Rectangle }).ToArray();
    }

    private static PicoDet.Detection[] Detect(PicoDet model, Mat bgr)
    {
        PicoDetImageInput.Write(model, bgr);
        return model.Run(0.3f); // The page and agents filter further.
    }

    /// <inheritdoc/>
    public void Dispose() { _model?.Dispose(); _model = null; Backend = null; }
}
