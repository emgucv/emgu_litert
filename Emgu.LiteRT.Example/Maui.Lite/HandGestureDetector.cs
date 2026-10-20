// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Emgu.CV;

namespace Maui.Demo.Lite;

// MediaPipe's hand landmarks and gestures (HandTracker) for the demo: downloads Google's gesture_recognizer.task (8.4 MB, SHA-256
// checked, not copied to Emgu's server), unpacks the four models, and uses the GPU for the two vision models when they compile fully
// for it and give the same gesture and landmarks as the CPU on a bundled photo, else the CPU. (No speed check: the CPU/GPU comparison
// the other detectors use alternates the two and, with models this small, made the CPU look 2-3 times slower than it is alone.) See HAND_GESTURES.md.
internal sealed class HandGestureDetector : IDisposable
{
    private HandTracker _tracker;
    public bool Imported => _tracker != null;
    public string Backend { get; private set; }
    public string GpuFailure { get; private set; }

    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string folder = Path.Combine(FileSystem.CacheDirectory, "mediapipe-hands");
        string file = Path.Combine(FileSystem.CacheDirectory, "gesture_recognizer.task");
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
        if (preferGpu && CocoDetector.CanTryGpu)
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
    private static async Task ValidateGpuAsync(HandTracker gpu, string[] models, int numThreads, CancellationToken token)
    {
        if (!gpu.IsFullyAccelerated)
            throw new InvalidOperationException("The hand models don't compile fully for the GPU.");
        using var cpu = await Task.Run(() => new HandTracker(models[0], models[1], models[2], models[3], numThreads), token).ConfigureAwait(false);
        using var image = ImageUtil.Decode(await ImageUtil.ReadAppFileAsync("hand_thumb_up.jpg").ConfigureAwait(false));
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

    public List<Hand> Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the hand models first.");
        return HandImageInput.Track(_tracker, bgr);
    }

    public void Dispose() { _tracker?.Dispose(); _tracker = null; Backend = null; }
}
