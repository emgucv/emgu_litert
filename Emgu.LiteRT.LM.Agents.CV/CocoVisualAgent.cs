// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents.Vision;

/// <summary>
/// A COCO object detector of Emgu.LiteRT.Models.CV (<see cref="CocoDetector"/>, the COCO SSD MobileNet V3, or <see cref="YoloXDetector"/>) as a visual
/// agent: each analysed frame's objects of the 80 <see cref="VisualClasses.ObjectClasses"/> are its observations, so "tell me when the cat is on the
/// sofa" is an object monitor. <see cref="VisualMonitorRuntime"/> serializes the calls off the UI thread. The agent owns the detector and disposes it.
/// </summary>
public sealed class CocoVisualAgent : IVisualAgent
{
    private readonly ICocoDetector _detector;
    private readonly Func<int> _numThreads;
    private readonly bool _preferGpu;
    private readonly bool _yoloX;

    /// <param name="detector">The detector, not yet prepared: a <see cref="YoloXDetector"/> is the "coco-yolox-nano" agent, any other "coco-ssd-mobilenet-v3"</param>
    /// <param name="numThreads">The CPU threads to prepare it with, asked each time it is prepared (2 when null)</param>
    /// <param name="preferGpu">Try the GPU first (<see cref="CocoDetector.CanTryGpu"/> says where it can)</param>
    public CocoVisualAgent(ICocoDetector detector, Func<int> numThreads = null, bool preferGpu = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _numThreads = numThreads ?? (() => 2);
        _preferGpu = preferGpu;
        _yoloX = detector is YoloXDetector;
    }

    /// <summary>The descriptor of the COCO SSD MobileNet V3 agent, or of the YOLOX one, with the backend in its name once prepared.</summary>
    public static VisualAgentDescriptor Describe(bool yoloX = false, bool requiresPreparation = true, string backend = null) => yoloX
        ? new VisualAgentDescriptor("coco-yolox-nano", "YOLOX Nano (experimental)" + (backend == null ? "" : " · LiteRT " + backend),
            new[] { "object_detection" }, VisualClasses.ObjectClasses, requiresPreparation: requiresPreparation)
        : new VisualAgentDescriptor("coco-ssd-mobilenet-v3", "COCO SSD MobileNet V3" + (backend == null ? "" : " · LiteRT " + backend),
            new[] { "object_detection" }, VisualClasses.ObjectClasses, requiresPreparation: requiresPreparation);

    /// <inheritdoc/>
    public VisualAgentDescriptor Descriptor => Describe(_yoloX, !_detector.Imported, Backend);

    private string Backend => _detector.Imported
        ? _detector.Backend + (_detector.GpuFailure == null ? "" : " (GPU validation failed or unavailable)") : null;

    /// <inheritdoc/>
    public Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken) =>
        _detector.PrepareAsync(_preferGpu, _numThreads(), progress, cancellationToken);

    /// <inheritdoc/>
    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the detector first.");
        using Mat input = VisualFrames.ToMat(frame);
        var results = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        // The rectangles are x0, y0, x1, y1.
        var observations = results
            .Select(r => new VisualObservation("object", VisualClasses.CanonicalObject(r.Label), r.Score,
                VisualFrames.Box(r.Rectangle[0], r.Rectangle[1], r.Rectangle[2], r.Rectangle[3])))
            .Where(o => VisualClasses.ObjectClasses.Contains(o.Label) && o.Confidence >= 0 && o.Confidence <= 1).ToArray();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, observations));
    }

    /// <inheritdoc/>
    public void Dispose() => _detector.Dispose();
}
