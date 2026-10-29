// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Diagnostics;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models;

// Tries the GPU first (Metal on Mac Catalyst, OpenCL on Android) at FP16 - as close to the CPU as FP32 (correlation
// > 0.9998) and about twice as fast - and keeps it only if its depth on a bundled image matches the CPU's and it is
// faster; otherwise the CPU is used (GpuFailure says why). See Emgu.LiteRT.Models/DEPTH_ANYTHING_EVALUATION.md.
/// <summary>
/// Depth from one photo with Depth Anything V2 Small (<see cref="DepthAnythingV2"/>): relative depth, nearer larger.
/// The 27.7 MB model is downloaded on first use. Callers serialize <c>PrepareAsync</c>, <c>Estimate</c> and
/// <c>Dispose</c> off the UI thread.
/// </summary>
public sealed class DepthEstimator : IDisposable
{
    /// <summary>The correlation with the CPU's depth a GPU must reach on the validation image.</summary>
    public const double MinGpuCorrelation = 0.999;

    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;
    private DepthAnythingV2 _model;

    /// <summary>Creates the estimator.</summary>
    /// <param name="cacheDirectory">Where the model is downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name ("surfers.jpg"), used to check a GPU's depth
    /// against the CPU's before trusting it. Without it the GPU is never used.</param>
    public DepthEstimator(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    /// <summary>Whether the model is ready.</summary>
    public bool Imported => _model != null;

    /// <summary>"GPU", "GPU + CPU" or "CPU" once ready.</summary>
    public string Backend { get; private set; }

    /// <summary>Why the GPU isn't used, when it was tried and failed.</summary>
    public string GpuFailure { get; private set; }

    /// <summary>The model file's path in the cache folder.</summary>
    public string ModelPath => Path.Combine(_cacheDirectory, "depth_anything_v2_small_wi8_afp32.tflite");

    /// <summary>Whether the model is already downloaded (it may still need its checksum checked).</summary>
    public bool IsDownloaded => File.Exists(ModelPath);

    /// <summary>Download the model if needed and compile it (the GPU when it passes the checks).</summary>
    /// <param name="preferGpu">Try the GPU first</param>
    /// <param name="numThreads">CPU threads</param>
    /// <param name="progress">Status lines, or null</param>
    /// <param name="token">Cancels it</param>
    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        DownloadableFile weights = DepthAnythingV2.ModelFile;
        weights.LocalFile = ModelPath;
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading the depth model: {percent:0}%" : "Downloading the depth model…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The depth model's download failed or did not match its SHA-256 checksum. Check the connection and retry.");

        GpuFailure = null;
        if (preferGpu && CocoDetector.CanTryGpu && _validationImage != null)
        {
            try
            {
                await CompileAsync(HwAccelerators.Gpu | HwAccelerators.Cpu, numThreads, progress, token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Dispose();
                GpuFailure = ex.Message;
                progress?.Invoke("The depth model's GPU is unavailable or failed its check; preparing the CPU…");
            }
        }
        await CompileAsync(HwAccelerators.Cpu, numThreads, progress, token).ConfigureAwait(false);
    }

    private async Task CompileAsync(HwAccelerators accelerator, int numThreads, Action<string> progress, CancellationToken token)
    {
        bool gpu = (accelerator & HwAccelerators.Gpu) != 0;
        progress?.Invoke($"Preparing the depth model on the {(gpu ? "GPU" : "CPU")}…");
        var candidate = await Task.Run(() => new DepthAnythingV2(ModelPath, numThreads, accelerator, gpu ? GpuPrecision.Fp16 : null), token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (gpu)
                await ValidateGpuAsync(candidate, numThreads, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Backend = gpu ? (candidate.IsFullyAccelerated ? "GPU" : "GPU + CPU") : "CPU";
            _model = candidate;
            candidate = null;
            progress?.Invoke($"Depth model ready · LiteRT {Backend}");
        }
        finally { candidate?.Dispose(); }
    }

    // The GPU's depth must match the CPU's on a known image, and the GPU be faster. A CPU run takes 0.4-2.7 s, so this
    // times one CPU run against the faster of two GPU runs rather than a long benchmark.
    private async Task ValidateGpuAsync(DepthAnythingV2 gpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        progress?.Invoke("Checking the depth model's GPU against the CPU…");
        using var cpu = await Task.Run(() => new DepthAnythingV2(ModelPath, numThreads), token).ConfigureAwait(false);
        using var image = await _validationImage("surfers.jpg").ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image surfers.jpg is not available.");
        await Task.Run(() =>
        {
            Run(gpu, image); // the first run sets the GPU up
            var watch = Stopwatch.StartNew();
            float[] expected = Run(cpu, image);
            double cpuMs = watch.Elapsed.TotalMilliseconds;
            token.ThrowIfCancellationRequested();
            double gpuMs = double.MaxValue;
            float[] actual = null;
            for (int i = 0; i < 2; i++)
            {
                watch.Restart();
                actual = Run(gpu, image);
                gpuMs = Math.Min(gpuMs, watch.Elapsed.TotalMilliseconds);
            }
            double correlation = Correlation(expected, actual);
            if (!(correlation >= MinGpuCorrelation))
                throw new InvalidOperationException(FormattableString.Invariant($"GPU accuracy check failed for the depth model (correlation {correlation:F4})."));
            if (!(gpuMs < cpuMs))
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"GPU speed check failed for the depth model: CPU {cpuMs:F0} ms, GPU {gpuMs:F0} ms. Keeping CPU."));
        }, token).ConfigureAwait(false);
    }

    /// <summary>
    /// The depth of a BGR image: a float Mat the image's size, 0 at the farthest point to 1 at the nearest (relative
    /// to the image, not distances).
    /// </summary>
    /// <param name="bgr">An 8-bit BGR image</param>
    public Mat Estimate(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the depth model first.");
        return ToDepthMat(Run(_model, bgr), bgr.Size);
    }

    private static float[] Run(DepthAnythingV2 model, Mat bgr)
    {
        DepthAnythingV2ImageInput.Write(model, bgr);
        return model.Run();
    }

    // The model's depth normalized to 0..1 and resized to the image.
    internal static Mat ToDepthMat(float[] depth, System.Drawing.Size size)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float d in depth)
        {
            if (d < min) min = d;
            if (d > max) max = d;
        }
        float range = Math.Max(1e-6f, max - min);
        var normalized = new float[depth.Length];
        for (int i = 0; i < depth.Length; i++)
            normalized[i] = (depth[i] - min) / range;
        using var small = new Mat(DepthAnythingV2.InputHeight, DepthAnythingV2.InputWidth, DepthType.Cv32F, 1);
        small.SetTo(normalized);
        var result = new Mat();
        CvInvoke.Resize(small, result, size, 0, 0, Inter.Linear);
        return result;
    }

    internal static double Correlation(float[] a, float[] b)
    {
        double ma = 0, mb = 0;
        for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
        ma /= a.Length; mb /= b.Length;
        double sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double x = a[i] - ma, y = b[i] - mb;
            sab += x * y; saa += x * x; sbb += y * y;
        }
        return sab / Math.Sqrt(saa * sbb);
    }

    /// <inheritdoc/>
    public void Dispose() { _model?.Dispose(); _model = null; Backend = null; }
}
