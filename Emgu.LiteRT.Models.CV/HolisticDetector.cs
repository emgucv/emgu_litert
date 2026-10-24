// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Emgu.CV;

namespace Emgu.LiteRT.Models;

// MediaPipe's holistic landmarker (HolisticTracker) for Emgu CV images: downloads Google's holistic_landmarker.task (13.7 MB, SHA-256 checked, not
// copied to Emgu's server), unpacks the seven models and compiles them on the CPU. See Emgu.LiteRT.Models/HOLISTIC.md.
/// <summary>MediaPipe holistic landmarks (<see cref="HolisticTracker"/>) for Emgu CV images. Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class HolisticDetector : IDisposable
{
    private readonly string _cacheDirectory;
    private HolisticTracker _tracker;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the models are downloaded to (default: a folder in the temp path).</param>
    public HolisticDetector(string cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
    }

    /// <summary>True once <see cref="PrepareAsync"/> has completed.</summary>
    public bool Imported => _tracker != null;

    /// <summary>Downloads the models if needed and compiles them on the CPU.</summary>
    public async Task PrepareAsync(int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string folder = Path.Combine(_cacheDirectory, "mediapipe-holistic");
        string file = Path.Combine(_cacheDirectory, "holistic_landmarker.task");
        var bundle = new DownloadableFile(HolisticModelFiles.Url, "HolisticLandmarker", HolisticModelFiles.Sha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading MediaPipe's holistic models: {percent:0}%" : "Downloading MediaPipe's holistic models…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The holistic models' download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        string[] models = await Task.Run(() => HolisticModelFiles.Extract(file, folder), token).ConfigureAwait(false);
        progress?.Invoke("Preparing the holistic models on the CPU…");
        _tracker = await Task.Run(() => new HolisticTracker(models, numThreads), token).ConfigureAwait(false);
        progress?.Invoke("Holistic models ready · LiteRT CPU");
    }

    /// <summary>The person in an 8-bit BGR image, with their hands and face; null if no body is found.</summary>
    public HolisticResult Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the holistic models first.");
        return HolisticImageInput.Track(_tracker, bgr);
    }

    /// <summary>Releases the compiled models.</summary>
    public void Dispose() { _tracker?.Dispose(); _tracker = null; }
}
