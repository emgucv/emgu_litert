// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Maui.Demo.Lite;

// All native/model work is serialized off the UI thread by VisualMonitorRuntime.
internal sealed class CocoVisualAgent : IVisualAgent
{
    private readonly ICocoDetector _detector;
    private readonly bool _useYoloX;
    internal CocoVisualAgent(bool useYoloX = false)
    {
        _useYoloX = useYoloX;
        _detector = useYoloX ? new YoloXDetector() : new CocoDetector();
    }
    internal static readonly string[] Classes = VisualHostCatalog.Classes;
    public VisualAgentDescriptor Descriptor => _useYoloX
        ? VisualHostCatalog.YoloXDetector(!_detector.Imported, Backend)
        : VisualHostCatalog.Detector(!_detector.Imported, Backend);
    private string Backend => _detector.Imported
        ? _detector.Backend + (_detector.GpuFailure == null ? "" : " (GPU validation failed or unavailable)") : null;

    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        await _detector.PrepareAsync(CocoDetector.CanTryGpu, Math.Min(2, CpuThreadSettings.Count), progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the detector first.");
        using var input = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        // Honor arbitrary frame strides; no native pointers escape the adapter.
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(input.DataPointer, y * input.Step), frame.Width * 3);
        var results = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        var observations = results.Select(r =>
        {
            // CompiledModel results are x0,y0,x1,y1.
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
    public void Dispose() => _detector.Dispose();
}
#endif
