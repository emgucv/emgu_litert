// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Emgu.CV;

namespace Emgu.LiteRT.Models;

// MediaPipe's face landmarker (FaceTracker) for the demo: downloads Google's face_landmarker.task (3.8 MB, SHA-256 checked, not copied to
// Emgu's server), unpacks the three models, and uses the GPU for the detector and the landmark model only if asked and only if both compile
// fully for it and give the CPU's landmarks on a bundled photo (the blendshape model stays on the CPU). See Emgu.LiteRT.Models/FACE.md.
/// <summary>MediaPipe face landmarks and expressions (<see cref="FaceTracker"/>) for Emgu CV images. Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class FaceDetector : IDisposable
{
    private const string ValidationImage = "face_portrait.png";
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the weights are downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name ("face_portrait.png"), used to check a GPU's
    /// results against the CPU's before trusting it. Without it the GPU is never used.</param>
    public FaceDetector(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    private FaceTracker _tracker;
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
        string folder = Path.Combine(_cacheDirectory, "mediapipe-face");
        string file = Path.Combine(_cacheDirectory, "face_landmarker.task");
        var bundle = new DownloadableFile(FaceModelFiles.Url, "FaceLandmarker", FaceModelFiles.Sha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading MediaPipe's face model: {percent:0}%" : "Downloading MediaPipe's face model…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The face model's download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        string[] models = await Task.Run(() => FaceModelFiles.Extract(file, folder), token).ConfigureAwait(false);

        GpuFailure = null;
        if (preferGpu && CocoDetector.CanTryGpu && _validationImage != null)
        {
            try
            {
                progress?.Invoke("Preparing the face models on the GPU…");
                FaceTracker gpu = await Task.Run(() => new FaceTracker(models[0], models[1], models[2], numThreads, HwAccelerators.Gpu | HwAccelerators.Cpu, GpuPrecision.Fp32), token).ConfigureAwait(false);
                try
                {
                    await ValidateGpuAsync(gpu, models, numThreads, token).ConfigureAwait(false);
                    _tracker = gpu;
                    Backend = "GPU";
                    progress?.Invoke("Face models ready · LiteRT GPU");
                    return;
                }
                catch { gpu.Dispose(); throw; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                GpuFailure = ex.Message;
                progress?.Invoke("The GPU isn't usable for the face models; preparing the CPU…");
            }
        }
        progress?.Invoke("Preparing the face models on the CPU…");
        _tracker = await Task.Run(() => new FaceTracker(models[0], models[1], models[2], numThreads), token).ConfigureAwait(false);
        Backend = "CPU";
        progress?.Invoke("Face models ready · LiteRT CPU");
    }

    // Fully accelerated, and the landmarks within 1% of the image of the CPU's on the bundled photo.
    private async Task ValidateGpuAsync(FaceTracker gpu, string[] models, int numThreads, CancellationToken token)
    {
        if (!gpu.IsFullyAccelerated)
            throw new InvalidOperationException("The face models don't compile fully for the GPU.");
        using var cpu = await Task.Run(() => new FaceTracker(models[0], models[1], null, numThreads), token).ConfigureAwait(false);
        using var image = await _validationImage(ValidationImage).ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image " + ValidationImage + " is not available.");
        await Task.Run(() =>
        {
            var expected = FaceImageInput.Track(cpu, image);
            var actual = FaceImageInput.Track(gpu, image);
            bool same = expected.Count == 1 && actual.Count == 1;
            for (int i = 0; same && i < FaceTracker.LandmarkCount; i++)
                same = Math.Abs(expected[0].Landmarks[i][0] - actual[0].Landmarks[i][0]) <= 0.01
                    && Math.Abs(expected[0].Landmarks[i][1] - actual[0].Landmarks[i][1]) <= 0.01;
            if (!same) throw new InvalidOperationException("The GPU's face landmarks differ from the CPU's.");
        }, token).ConfigureAwait(false);
    }

    /// <summary>Detects faces in an 8-bit BGR image (at most <paramref name="maxFaces"/>), each with its landmarks and expressions.</summary>
    public List<Face> Detect(Mat bgr, int maxFaces = 1)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the face models first.");
        return FaceImageInput.Track(_tracker, bgr, maxFaces);
    }

    /// <summary>Releases the compiled models.</summary>
    public void Dispose() { _tracker?.Dispose(); _tracker = null; Backend = null; }
}
