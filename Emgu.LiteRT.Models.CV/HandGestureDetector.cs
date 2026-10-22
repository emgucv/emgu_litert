// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Emgu.CV;

namespace Emgu.LiteRT.Models;

// MediaPipe's hand landmarks and gestures (HandTracker) for the demo: downloads Google's gesture_recognizer.task (8.4 MB, SHA-256
// checked, not copied to Emgu's server), unpacks the four models, and uses the GPU for the two vision models when they compile fully
// for it and give the same gesture and landmarks as the CPU on a bundled photo, else the CPU. (No speed check: the CPU/GPU comparison
// the other detectors use alternates the two and, with models this small, made the CPU look 2-3 times slower than it is alone.) See Emgu.LiteRT.Models/HAND_GESTURES.md.
/// <summary>MediaPipe hand landmarks and gestures (<see cref="HandTracker"/>) for Emgu CV images. Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class HandGestureDetector : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the weights are downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name (e.g. "surfers.jpg"), used to check a GPU's
    /// results against the CPU's before trusting it. Without it the GPU is never used.</param>
    public HandGestureDetector(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    private HandTracker _tracker;
    /// <summary>True once <see cref="PrepareAsync"/> has completed.</summary>
    public bool Imported => _tracker != null;
    /// <summary>The backend in use ("CPU" or "GPU"), or null before <see cref="PrepareAsync"/> has completed.</summary>
    public string Backend { get; private set; }
    /// <summary>Why the GPU was not used, or null if it was used or never tried.</summary>
    public string GpuFailure { get; private set; }

    /// <summary>Downloads the models if needed and compiles them, trying the GPU first when <paramref name="preferGpu"/> is set and the platform supports it; the CPU otherwise or on failure.</summary>
    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string folder = Path.Combine(_cacheDirectory, "mediapipe-hands");
        string file = Path.Combine(_cacheDirectory, "gesture_recognizer.task");
        var bundle = new DownloadableFile(HandModelFiles.BundleUrl, "HandGestures", HandModelFiles.BundleSha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading MediaPipe's hand models: {percent:0}%" : "Downloading MediaPipe's hand models…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The hand models' download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        string[] models = await Task.Run(() => HandModelFiles.Extract(file, folder), token).ConfigureAwait(false);

        GpuFailure = null;
        if (preferGpu && CocoDetector.CanTryGpu && _validationImage != null)
        {
            try
            {
                progress?.Invoke("Preparing the hand models on the GPU…");
                HandTracker gpu = await Task.Run(() => new HandTracker(models[0], models[1], models[2], models[3], numThreads,
                    HwAccelerators.Gpu | HwAccelerators.Cpu, GpuPrecision.Fp32), token).ConfigureAwait(false);
                try
                {
                    await ValidateGpuAsync(gpu, models, numThreads, token).ConfigureAwait(false);
                    _tracker = gpu;
                    Backend = "GPU";
                    progress?.Invoke("Hand models ready · LiteRT GPU");
                    return;
                }
                catch { gpu.Dispose(); throw; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                GpuFailure = ex.Message;
                progress?.Invoke("The GPU isn't usable for the hand models; preparing the CPU…");
            }
        }
        progress?.Invoke("Preparing the hand models on the CPU…");
        _tracker = await Task.Run(() => new HandTracker(models[0], models[1], models[2], models[3], numThreads), token).ConfigureAwait(false);
        Backend = "CPU";
        progress?.Invoke("Hand models ready · LiteRT CPU");
    }

    // The GPU must be fully accelerated and agree with the CPU on a bundled photo (the same gesture and handedness, landmarks within
    // 2% of the image).
    private async Task ValidateGpuAsync(HandTracker gpu, string[] models, int numThreads, CancellationToken token)
    {
        if (!gpu.IsFullyAccelerated)
            throw new InvalidOperationException("The hand models don't compile fully for the GPU.");
        using var cpu = await Task.Run(() => new HandTracker(models[0], models[1], models[2], models[3], numThreads), token).ConfigureAwait(false);
        using var image = await _validationImage("hand_thumb_up.jpg").ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image " + "hand_thumb_up.jpg" + " is not available.");
        await Task.Run(() =>
        {
            var expected = HandImageInput.Track(cpu, image);
            var actual = HandImageInput.Track(gpu, image);
            bool same = expected.Count == 1 && actual.Count == 1 && expected[0].Gesture == actual[0].Gesture
                && expected[0].Handedness == actual[0].Handedness;
            if (same)
                for (int i = 0; i < HandTracker.LandmarkCount && same; i++)
                    same = Math.Abs(expected[0].Landmarks[i][0] - actual[0].Landmarks[i][0]) <= 0.02
                        && Math.Abs(expected[0].Landmarks[i][1] - actual[0].Landmarks[i][1]) <= 0.02;
            if (!same) throw new InvalidOperationException("The GPU's hands differ from the CPU's.");
        }, token).ConfigureAwait(false);
    }

    /// <summary>Detects people's hands in an 8-bit BGR image.</summary>
    public List<Hand> Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the hand models first.");
        return HandImageInput.Track(_tracker, bgr);
    }

    /// <summary>Releases the compiled models.</summary>
    public void Dispose() { _tracker?.Dispose(); _tracker = null; Backend = null; }
}
