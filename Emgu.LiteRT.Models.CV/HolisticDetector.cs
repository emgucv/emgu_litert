// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Emgu.CV;

namespace Emgu.LiteRT.Models;

// MediaPipe's holistic landmarker (HolisticTracker) for Emgu CV images: downloads Google's holistic_landmarker.task (13.7 MB, SHA-256 checked, not
// copied to Emgu's server) and unpacks the seven models. The pose models and the blendshape model always run on the CPU (the pose models don't
// compile for the GPU); the hand and face models use the GPU only if asked, if they compile fully for it, give the CPU's landmarks on a bundled
// picture and are faster there. See Emgu.LiteRT.Models/HOLISTIC.md.
/// <summary>MediaPipe holistic landmarks (<see cref="HolisticTracker"/>) for Emgu CV images. Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.</summary>
public sealed class HolisticDetector : IDisposable
{
    private const string ValidationImage = "holistic_person.png";
    private readonly string _cacheDirectory;
    private readonly Func<string, Task<Mat>> _validationImage;
    private HolisticTracker _tracker;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the models are downloaded to (default: a folder in the temp path).</param>
    /// <param name="validationImage">Returns a bundled image by file name ("holistic_person.png"), used to check a GPU's results and speed
    /// against the CPU's before trusting it. Without it the GPU is never used.</param>
    public HolisticDetector(string cacheDirectory = null, Func<string, Task<Mat>> validationImage = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
        _validationImage = validationImage;
    }

    /// <summary>True once <see cref="PrepareAsync"/> has completed.</summary>
    public bool Imported => _tracker != null;
    /// <summary>Where the hand and face models run ("CPU" or "GPU"), or null before <see cref="PrepareAsync"/> has completed.</summary>
    public string Backend { get; private set; }
    /// <summary>Why the GPU was not used, or null if it was used or never tried.</summary>
    public string GpuFailure { get; private set; }

    /// <summary>Downloads the models if needed and compiles them, trying the GPU for the hand and face models when <paramref name="preferGpu"/>
    /// is set and the platform supports it; the CPU otherwise, on failure, or when the GPU isn't faster.</summary>
    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
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

        GpuFailure = null;
        if (preferGpu && CocoDetector.CanTryGpu && _validationImage != null)
        {
            HolisticTracker gpu = null, cpu = null;
            try
            {
                progress?.Invoke("Preparing the holistic models on the GPU…");
                gpu = await Task.Run(() => new HolisticTracker(models, numThreads, HwAccelerators.Gpu | HwAccelerators.Cpu, GpuPrecision.Fp32), token).ConfigureAwait(false);
                if (!gpu.IsFullyAccelerated)
                    throw new InvalidOperationException("The hand and face models don't compile fully for the GPU.");
                cpu = await Task.Run(() => new HolisticTracker(models, numThreads), token).ConfigureAwait(false);
                bool gpuFaster = await CompareAsync(gpu, cpu, token).ConfigureAwait(false);
                _tracker = gpuFaster ? gpu : cpu;
                (gpuFaster ? cpu : gpu).Dispose();
                gpu = cpu = null;
                Backend = gpuFaster ? "GPU" : "CPU";
                progress?.Invoke("Holistic models ready · LiteRT " + Backend);
                return;
            }
            catch (OperationCanceledException) { gpu?.Dispose(); cpu?.Dispose(); throw; }
            catch (Exception ex)
            {
                gpu?.Dispose(); cpu?.Dispose();
                GpuFailure = ex.Message;
                progress?.Invoke("The GPU isn't usable for the holistic models; preparing the CPU…");
            }
        }
        progress?.Invoke("Preparing the holistic models on the CPU…");
        _tracker = await Task.Run(() => new HolisticTracker(models, numThreads), token).ConfigureAwait(false);
        Backend = "CPU";
        progress?.Invoke("Holistic models ready · LiteRT CPU");
    }

    // The GPU's hand and face landmarks within 1% of the image of the CPU's on the bundled picture (else it throws); true if the GPU is faster
    // (the median of five frames each after a first one), else GpuFailure says why it isn't used.
    private async Task<bool> CompareAsync(HolisticTracker gpu, HolisticTracker cpu, CancellationToken token)
    {
        using var image = await _validationImage(ValidationImage).ConfigureAwait(false) ?? throw new InvalidOperationException("The validation image " + ValidationImage + " is not available.");
        return await Task.Run(() =>
        {
            HolisticResult expected = HolisticImageInput.Track(cpu, image), actual = HolisticImageInput.Track(gpu, image);
            bool Same(float[][] a, float[][] b) => a == null ? b == null
                : b != null && a.Length == b.Length && Enumerable.Range(0, a.Length).All(i => Math.Abs(a[i][0] - b[i][0]) <= 0.01 && Math.Abs(a[i][1] - b[i][1]) <= 0.01);
            if (expected == null || actual == null || !Same(expected.LeftHand?.Landmarks, actual.LeftHand?.Landmarks) ||
                !Same(expected.RightHand?.Landmarks, actual.RightHand?.Landmarks) || !Same(expected.Face?.Landmarks, actual.Face?.Landmarks))
                throw new InvalidOperationException("The GPU's hand or face landmarks differ from the CPU's.");
            double Time(HolisticTracker tracker)
            {
                var times = new List<double>();
                for (int i = 0; i < 5; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    HolisticImageInput.Track(tracker, image);
                    times.Add(watch.Elapsed.TotalMilliseconds);
                }
                times.Sort();
                return times[2];
            }
            double gpuTime = Time(gpu), cpuTime = Time(cpu);
            if (gpuTime < cpuTime) return true;
            GpuFailure = $"The GPU ({gpuTime:0} ms a frame) isn't faster than the CPU ({cpuTime:0} ms).";
            return false;
        }, token).ConfigureAwait(false);
    }

    /// <summary>The person in an 8-bit BGR image, with their hands and face; null if no body is found.</summary>
    public HolisticResult Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the holistic models first.");
        HolisticResult result = HolisticImageInput.Track(_tracker, bgr, out HolisticTimings timings);
        LastTimings = timings;
        return result;
    }

    /// <summary>How long each part of the last <see cref="Detect"/> took.</summary>
    public HolisticTimings LastTimings { get; private set; }

    /// <summary>Releases the compiled models.</summary>
    public void Dispose() { _tracker?.Dispose(); _tracker = null; Backend = null; }
}
