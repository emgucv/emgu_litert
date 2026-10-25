// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents.Vision;

/// <summary>
/// MediaPipe's pose landmarks (<see cref="PoseDetector"/>) as a visual agent: each analysed frame goes through the pose detector and landmark model, and the
/// person's postures (<see cref="PoseGestures"/>: arms raised, hand raised, standing, sitting, lying down, and waving from the last two seconds) are
/// observations of those classes, so a monitor for "waving" is an ordinary object monitor. <see cref="VisualMonitorRuntime"/> serializes the calls off
/// the UI thread, which is also what makes one wave detector per agent enough. The agent owns the detector and disposes it.
/// </summary>
public sealed class PoseVisualAgent : IVisualAgent
{
    private readonly PoseDetector _detector;
    private readonly PoseModelFiles.Variant _variant;
    private readonly Func<int> _numThreads;
    private readonly bool _preferGpu;
    private readonly PoseGestures.WaveDetector _wave = new PoseGestures.WaveDetector();

    /// <param name="detector">The detector, not yet prepared</param>
    /// <param name="variant">The landmark model (<see cref="PoseModelFiles.Lite"/> when null: 26 ms a frame on a Galaxy S10's CPU)</param>
    /// <param name="numThreads">The CPU threads to prepare it with, asked each time it is prepared (2 when null)</param>
    /// <param name="preferGpu">Try the GPU first</param>
    public PoseVisualAgent(PoseDetector detector, PoseModelFiles.Variant variant = null, Func<int> numThreads = null, bool preferGpu = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _variant = variant ?? PoseModelFiles.Lite;
        _numThreads = numThreads ?? (() => 2);
        _preferGpu = preferGpu;
    }

    /// <summary>The "body-pose" agent's descriptor, with the backend in its name once prepared.</summary>
    public static VisualAgentDescriptor Describe(bool requiresPreparation = true, string backend = null) => new VisualAgentDescriptor(
        "body-pose", "Body pose · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "body_pose" }, VisualClasses.PoseClasses, requiresPreparation: requiresPreparation);

    /// <summary>
    /// The postures of the person in a frame (and waving, from the wave detector's recent frames) as observations of their classes, each with the box of the
    /// visible landmarks. <paramref name="pose"/> null (nobody in view) resets the wave detector.
    /// </summary>
    public static IReadOnlyList<VisualObservation> Observations(Pose pose, int width, int height, PoseGestures.WaveDetector wave, TimeSpan time) =>
        Observations(pose?.Landmarks, width, height, wave, time);

    /// <summary>The same from the 33 landmarks.</summary>
    public static IReadOnlyList<VisualObservation> Observations(float[][] landmarks, int width, int height, PoseGestures.WaveDetector wave, TimeSpan time)
    {
        // Not an iterator: the wave detector must see every frame, whether or not the caller reads the result.
        var readings = new List<(string, float)>();
        if (landmarks != null) readings.AddRange(PoseGestures.Classify(landmarks, width, height).Select(r => (r.Name, r.Score)));
        PoseReading waving = wave?.Update(time, landmarks, width, height);
        if (waving != null) readings.Add((waving.Name, waving.Score));
        return landmarks == null ? new List<VisualObservation>() : VisualFrames.Observations(readings, PoseGestures.BoxOf(landmarks));
    }

    /// <inheritdoc/>
    public VisualAgentDescriptor Descriptor => Describe(!_detector.Imported, _detector.Backend);

    /// <inheritdoc/>
    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        await _detector.PrepareAsync(_variant, _preferGpu, _numThreads(), progress, cancellationToken).ConfigureAwait(false);
        _wave.Reset();
    }

    /// <inheritdoc/>
    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the pose models first.");
        using Mat input = VisualFrames.ToMat(frame);
        var poses = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, Observations(poses.Count > 0 ? poses[0] : null, frame.Width, frame.Height, _wave, frame.Timestamp)));
    }

    /// <inheritdoc/>
    public void Dispose() => _detector.Dispose();
}
