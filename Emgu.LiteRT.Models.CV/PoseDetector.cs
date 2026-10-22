// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Emgu.CV;

namespace Emgu.LiteRT.Models;

// MediaPipe's pose landmarker (PoseTracker) for the demo: downloads Google's pose_landmarker_{lite,full,heavy}.task (5.8 / 9.4 / 30.7 MB, SHA-256
// checked, not copied to Emgu's server), unpacks the two models, and uses the GPU only if asked and only if both compile fully for it and give the
// CPU's landmarks on a bundled photo. See POSE.md.
/// <summary>MediaPipe pose landmarks (<see cref="PoseTracker"/>) for Emgu CV images. Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class PoseDetector : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the weights are downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name (e.g. "surfers.jpg"), used to check a GPU's
    /// results against the CPU's before trusting it. Without it the GPU is never used.</param>
    public PoseDetector(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    private PoseTracker _tracker;
    /// <summary>True once <see cref="PrepareAsync"/> has completed.</summary>
    public bool Imported => _tracker != null;
    /// <summary>The backend in use ("CPU" or "GPU"), or null before <see cref="PrepareAsync"/> has completed.</summary>
    public string Backend { get; private set; }
    /// <summary>Why the GPU was not used, or null if it was used or never tried.</summary>
    public string GpuFailure { get; private set; }

    /// <summary>Downloads the models if needed and compiles them, trying the GPU first when <paramref name="preferGpu"/> is set and the platform supports it; the CPU otherwise or on failure.</summary>
    public async Task PrepareAsync(PoseModelFiles.Variant variant, bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string folder = Path.Combine(_cacheDirectory, "mediapipe-pose-" + variant.Name);
        string file = Path.Combine(_cacheDirectory, "pose_landmarker_" + variant.Name + ".task");
        var bundle = new DownloadableFile(variant.Url, "PoseLandmarker", variant.Sha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading MediaPipe's pose model ({variant.Name}): {percent:0}%" : "Downloading MediaPipe's pose model…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The pose model's download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        string[] models = await Task.Run(() => PoseModelFiles.Extract(file, folder), token).ConfigureAwait(false);

        GpuFailure = null;
        if (preferGpu && CocoDetector.CanTryGpu && _validationImage != null)
        {
            try
            {
                progress?.Invoke("Preparing the pose models on the GPU…");
                PoseTracker gpu = await Task.Run(() => new PoseTracker(models[0], models[1], numThreads, HwAccelerators.Gpu | HwAccelerators.Cpu, GpuPrecision.Fp32), token).ConfigureAwait(false);
                try
                {
                    await ValidateGpuAsync(gpu, models, numThreads, token).ConfigureAwait(false);
                    _tracker = gpu;
                    Backend = "GPU";
                    progress?.Invoke("Pose models ready · LiteRT GPU");
                    return;
                }
                catch { gpu.Dispose(); throw; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                GpuFailure = ex.Message;
                progress?.Invoke("The GPU isn't usable for the pose models; preparing the CPU…");
            }
        }
        progress?.Invoke("Preparing the pose models on the CPU…");
        _tracker = await Task.Run(() => new PoseTracker(models[0], models[1], numThreads), token).ConfigureAwait(false);
        Backend = "CPU";
        progress?.Invoke("Pose models ready · LiteRT CPU");
    }

    // Fully accelerated, and the landmarks within 3% of the image of the CPU's on the bundled photo.
    private async Task ValidateGpuAsync(PoseTracker gpu, string[] models, int numThreads, CancellationToken token)
    {
        if (!gpu.IsFullyAccelerated)
            throw new InvalidOperationException("The pose models don't compile fully for the GPU.");
        using var cpu = await Task.Run(() => new PoseTracker(models[0], models[1], numThreads), token).ConfigureAwait(false);
        using var image = await _validationImage("pose_standing.jpg").ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image " + "pose_standing.jpg" + " is not available.");
        await Task.Run(() =>
        {
            var expected = PoseImageInput.Track(cpu, image);
            var actual = PoseImageInput.Track(gpu, image);
            bool same = expected.Count == 1 && actual.Count == 1;
            for (int i = 0; same && i < PoseTracker.LandmarkCount; i++)
                same = Math.Abs(expected[0].Landmarks[i][0] - actual[0].Landmarks[i][0]) <= 0.03
                    && Math.Abs(expected[0].Landmarks[i][1] - actual[0].Landmarks[i][1]) <= 0.03;
            if (!same) throw new InvalidOperationException("The GPU's pose differs from the CPU's.");
        }, token).ConfigureAwait(false);
    }

    /// <summary>Detects people's poses in an 8-bit BGR image.</summary>
    public List<Pose> Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the pose models first.");
        return PoseImageInput.Track(_tracker, bgr);
    }

    /// <summary>Releases the compiled models.</summary>
    public void Dispose() { _tracker?.Dispose(); _tracker = null; Backend = null; }
}
