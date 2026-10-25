// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents.Vision;

/// <summary>
/// MediaPipe's holistic landmarker (<see cref="HolisticDetector"/>) as a visual agent: each analysed frame goes through the body, both hands and the face in
/// one pass, and what they show together (<see cref="HolisticGestures"/>: a posture combined with an expression, "hand raised and smiling", covering the
/// eyes, a hand over the mouth or on the face) are observations of those classes, so "take a photo when I wave and smile" is an ordinary object monitor.
/// Frames go through the video mode (tracked and smoothed from one to the next, by their timestamps). Register it after the body-pose and
/// face-expression agents, which keep the plain postures and expressions. It doesn't identify anyone. <see cref="VisualMonitorRuntime"/> serializes the
/// calls off the UI thread. The agent owns the detector and disposes it.
/// </summary>
public sealed class HolisticVisualAgent : IVisualAgent
{
    private readonly HolisticDetector _detector;
    private readonly Func<int> _numThreads;
    private readonly bool _preferGpu;
    private readonly PoseGestures.WaveDetector _wave = new PoseGestures.WaveDetector();

    /// <param name="detector">The detector, not yet prepared</param>
    /// <param name="numThreads">The CPU threads to prepare it with, asked each time it is prepared (2 when null)</param>
    /// <param name="preferGpu">Try the GPU first for the hand and face models</param>
    public HolisticVisualAgent(HolisticDetector detector, Func<int> numThreads = null, bool preferGpu = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _numThreads = numThreads ?? (() => 2);
        _preferGpu = preferGpu;
    }

    /// <summary>The "holistic" agent's descriptor, with the backend in its name once prepared.</summary>
    public static VisualAgentDescriptor Describe(bool requiresPreparation = true, string backend = null) => new VisualAgentDescriptor(
        "holistic", "Body, hands and face · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "holistic" }, VisualClasses.HolisticClasses, requiresPreparation: requiresPreparation);

    /// <summary>What the person in a frame shows (<see cref="HolisticGestures.Classify"/>) as observations of their classes, each with the box of the
    /// body's visible landmarks.</summary>
    public static IReadOnlyList<VisualObservation> Observations(HolisticResult result, int width, int height, PoseGestures.WaveDetector wave, TimeSpan time)
    {
        // Every frame goes through Classify, so the wave detector sees them all.
        var readings = HolisticGestures.Classify(result, width, height, wave, time);
        return result?.Pose == null ? new List<VisualObservation>()
            : VisualFrames.Observations(readings.Select(r => (r.Name, r.Score)), PoseGestures.BoxOf(result.Pose.Landmarks));
    }

    /// <inheritdoc/>
    public VisualAgentDescriptor Descriptor => Describe(!_detector.Imported, _detector.Backend);

    /// <inheritdoc/>
    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        await _detector.PrepareAsync(_preferGpu, _numThreads(), progress, cancellationToken).ConfigureAwait(false);
        _wave.Reset();
        _detector.ResetTracking();
    }

    /// <inheritdoc/>
    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the holistic models first.");
        using Mat input = VisualFrames.ToMat(frame);
        HolisticResult result = _detector.Detect(input, frame.Timestamp);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, Observations(result, frame.Width, frame.Height, _wave, frame.Timestamp)));
    }

    /// <inheritdoc/>
    public void Dispose() => _detector.Dispose();
}
