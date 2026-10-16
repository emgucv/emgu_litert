// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Util;
using Emgu.TF.Lite.Models;

namespace Maui.Demo.Lite;

// All native/model work is serialized off the UI thread by VisualMonitorRuntime.
internal sealed class CocoVisualAgent : IVisualAgent
{
    private CocoSsdMobilenetV3 _model;
    private readonly DownloadableFile _weights = new(
        "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v3_small_2020_01_14/model.tflite",
        "CocoSsdMobilenetV3", "0F52F7A4884DD6426D38F4AFC06DA75105EFE77F7C83E470254C267FE34CC43C");
    private readonly DownloadableFile _labels = new(
        "https://github.com/emgucv/models/raw/master/coco_ssd_mobilenet_v3_small_2020_01_14/labelmap.txt",
        "CocoSsdMobilenetV3", "C7E79C855F73CBBA9F33D649D60E1676EB0A974021A41696D1AC0D4B7F7E0211");
    internal static readonly string[] Classes = ("person,bicycle,car,motorcycle,airplane,bus,train,truck,boat,traffic light," +
        "fire hydrant,stop sign,parking meter,bench,bird,cat,dog,horse,sheep,cow,elephant,bear,zebra,giraffe,backpack," +
        "umbrella,handbag,tie,suitcase,frisbee,skis,snowboard,sports ball,kite,baseball bat,baseball glove,skateboard," +
        "surfboard,tennis racket,bottle,wine glass,cup,fork,knife,spoon,bowl,banana,apple,sandwich,orange,broccoli," +
        "carrot,hot dog,pizza,donut,cake,chair,couch,potted plant,bed,dining table,toilet,tv,laptop,mouse,remote," +
        "keyboard,cell phone,microwave,oven,toaster,sink,refrigerator,book,clock,vase,scissors,teddy bear,hair drier,toothbrush").Split(',');
    public VisualAgentDescriptor Descriptor => new("coco-ssd-mobilenet-v3", "COCO SSD MobileNet V3",
        new[] { "object_detection" }, Classes, requiresPreparation: _model?.Imported != true);

    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        if (_model?.Imported == true) return;
        progress?.Invoke("Preparing COCO detector (first use may download the model)");
        // Download explicitly to support cancellation; Init then reuses the verified files.
        var downloads = new FileDownloadManager();
        downloads.AddFile(_weights); downloads.AddFile(_labels);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading COCO detector: {percent:0}%" : "Downloading COCO detector…");
        await downloads.Download(1, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded) throw new IOException("COCO detector download failed. Check the connection and resume this monitor to retry.");
        _model?.Dispose();
        _model = new CocoSsdMobilenetV3 { NumThreads = Math.Min(2, CpuThreadSettings.Count) };
        await _model.Init(_weights, _labels).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_model.Imported) throw new InvalidOperationException("COCO detector could not be loaded.");
    }
    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_model?.Imported != true) throw new InvalidOperationException("Prepare the detector first.");
        using var input = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        // Honor arbitrary frame strides; no native pointers escape the adapter.
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(input.DataPointer, y * input.Step), frame.Width * 3);
        var tensor = _model.InputTensor;
        var size = new System.Drawing.Size(tensor.Dims[2], tensor.Dims[1]);
        using (var target = new Mat(size, DepthType.Cv8U, 3, tensor.DataPointer, size.Width * 3))
        using (var rgb = new Mat())
        {
            CvInvoke.CvtColor(input, rgb, ColorConversion.Bgr2Rgb);
            CvInvoke.Resize(rgb, target, size);
        }
        _model.Interpreter.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        var observations = _model.GetResults(0).Select(r =>
        {
            // Wrapper results are x0,y0,x1,y1, despite its historical Rectangle field comment.
            var b = r.Rectangle;
            double x = Math.Clamp(b[0], 0, 1), y = Math.Clamp(b[1], 0, 1);
            return new VisualObservation("object", CanonicalLabel(r.Label), r.Score,
                new VisualBox(x, y, Math.Max(0, Math.Clamp(b[2], 0, 1) - x), Math.Max(0, Math.Clamp(b[3], 0, 1) - y)));
        }).Where(o => Classes.Contains(o.Label) && o.Confidence >= 0 && o.Confidence <= 1).ToArray();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, observations));
    }
    private static string CanonicalLabel(string label) => label.Trim().ToLowerInvariant() switch
    {
        "motorbike" => "motorcycle", "aeroplane" => "airplane", "sofa" => "couch",
        "pottedplant" => "potted plant", "diningtable" => "dining table", "tvmonitor" => "tv", var value => value
    };
    public void Dispose() { _model?.Dispose(); _model = null; }
}
#endif
