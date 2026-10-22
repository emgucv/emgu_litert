// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;

namespace Maui.Demo.Lite;

// Shared by the photo demo and live visual agent. Callers serialize prepare/detect/dispose off the UI thread.
internal sealed class CocoDetector : ICocoDetector
{
    private CocoSsdMobilenetV3 _model;
    private readonly DownloadableFile _weights = new(
        "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v3_small_2020_01_14/model.tflite",
        "CocoSsdMobilenetV3", "0F52F7A4884DD6426D38F4AFC06DA75105EFE77F7C83E470254C267FE34CC43C");
    private readonly DownloadableFile _labels = new(
        "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v3_small_2020_01_14/labelmap.txt",
        "CocoSsdMobilenetV3", "C7E79C855F73CBBA9F33D649D60E1676EB0A974021A41696D1AC0D4B7F7E0211");

    public bool Imported => _model?.Imported == true;
    public string Backend { get; private set; }
    public string GpuFailure { get; private set; }
    public static bool CanTryGpu =>
#if ANDROID || MACCATALYST
        true;
#else
        false;
#endif

    public async Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        var downloads = new FileDownloadManager();
        downloads.AddFile(_weights); downloads.AddFile(_labels);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading COCO detector: {percent:0}%" : "Downloading COCO detector…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded) throw new IOException("COCO detector download failed. Check the connection and retry.");

        GpuFailure = null;
        if (preferGpu && CanTryGpu)
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
                progress?.Invoke("COCO GPU unavailable or failed validation; preparing CPU detector…");
            }
        }
        await CompileAsync(HwAccelerators.Cpu, numThreads, progress, token).ConfigureAwait(false);
    }

    private async Task CompileAsync(HwAccelerators accelerator, int numThreads, Action<string> progress, CancellationToken token)
    {
        progress?.Invoke($"Compiling COCO detector for {accelerator}…");
        var candidate = new CocoSsdMobilenetV3 { NumThreads = numThreads };
        try
        {
            await candidate.Init(_weights, _labels, accelerator).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            // Exercise buffer transfers and execution as well as compilation before accepting a GPU backend.
            await Task.Run(() =>
            {
                using var blank = new Mat(320, 320, DepthType.Cv8U, 3);
                blank.SetTo(new Emgu.CV.Structure.MCvScalar(0));
                Detect(candidate, blank);
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if ((accelerator & HwAccelerators.Gpu) != 0)
                await ValidateGpuAsync(candidate, numThreads, progress, token).ConfigureAwait(false);
            Backend = (accelerator & HwAccelerators.Gpu) != 0
                ? (candidate.CompiledModel.IsFullyAccelerated ? "GPU" : "GPU + CPU") : "CPU";
            _model = candidate;
            candidate = null;
            progress?.Invoke($"COCO detector ready · LiteRT {Backend}");
        }
        finally { candidate?.Dispose(); }
    }

    private async Task ValidateGpuAsync(CocoSsdMobilenetV3 gpu, int numThreads, Action<string> progress, CancellationToken token)
    {
        // This uint8 model can compile successfully yet give incorrect GPU results on some runtimes/devices.
        // Compare a bundled known image with CPU execution before using the GPU for real alerts.
        using var cpu = new CocoSsdMobilenetV3 { NumThreads = numThreads };
        await cpu.Init(_weights, _labels, HwAccelerators.Cpu).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using var image = ImageUtil.Decode(await ImageUtil.ReadAppFileAsync("surfers.jpg").ConfigureAwait(false));
        await Task.Run(() =>
        {
            var expected = Detect(cpu, image).Where(r => r.Score >= 0.5).ToArray();
            var actual = Detect(gpu, image).Where(r => r.Score >= 0.5).ToArray();
            // Match by class and box rather than ranking: near-equal detections can exchange positions.
            var unmatched = actual.ToList();
            bool matches = expected.Length > 0 && expected.Length == actual.Length;
            foreach (var detection in expected)
            {
                int index = unmatched.FindIndex(r => r.Class == detection.Class
                    && Math.Abs(r.Score - detection.Score) <= 0.05
                    && Enumerable.Range(0, 4).All(k => Math.Abs(r.Rectangle[k] - detection.Rectangle[k]) <= 0.05));
                if (index < 0) { matches = false; break; }
                unmatched.RemoveAt(index);
            }
            if (!matches) throw new InvalidOperationException("GPU accuracy check failed for the COCO detector.");

            progress?.Invoke("Checking COCO GPU speed against CPU…");
            using var benchmarkImage = new Mat();
            CvInvoke.Resize(image, benchmarkImage, new System.Drawing.Size(640, 480));
            var timing = CocoBackendBenchmark.Compare(() => Detect(cpu, benchmarkImage), () => Detect(gpu, benchmarkImage), token);
            if (!timing.UseGpu)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"GPU speed check failed for the COCO detector: CPU {timing.CpuMilliseconds:F2} ms, GPU {timing.GpuMilliseconds:F2} ms. Keeping CPU."));
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    public CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the COCO detector first.");
        return Detect(_model, bgr);
    }

    private static CocoSsdMobilenet.RecognitionResult[] Detect(CocoSsdMobilenetV3 model, Mat bgr)
    {
        var shape = model.InputTensorType.Dimensions;
        var size = new System.Drawing.Size(shape[2], shape[1]);
        var buffer = model.InputBuffers[0];
        var data = buffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var target = new Mat(size, DepthType.Cv8U, 3, data, size.Width * 3);
            using var resized = new Mat();
            CvInvoke.Resize(bgr, resized, size);
            CvInvoke.CvtColor(resized, target, ColorConversion.Bgr2Rgb);
        }
        finally { buffer.Unlock(); }
        model.CompiledModel.Run(model.InputBuffers, model.OutputBuffers);
        return model.GetResults(0);
    }

    public void Dispose() { _model?.Dispose(); _model = null; Backend = null; }
}
