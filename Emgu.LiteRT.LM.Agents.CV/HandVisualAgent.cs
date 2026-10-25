// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents.Vision;

/// <summary>
/// MediaPipe's hand gestures (<see cref="HandGestureDetector"/>) as a visual agent: each analysed frame goes through the palm detector, the landmark model
/// and the gesture classifier, and every hand with a recognised gesture is an observation of that gesture's class (<see cref="VisualClasses.Gestures"/>:
/// "thumb up", "victory"...), so a monitor for "thumb up" is an ordinary object monitor. On the CPU by default, which was as fast as the GPU for these
/// small models on phones. <see cref="VisualMonitorRuntime"/> serializes the calls off the UI thread. The agent owns the detector and disposes it.
/// </summary>
public sealed class HandVisualAgent : IVisualAgent
{
    private readonly HandGestureDetector _detector;
    private readonly Func<int> _numThreads;
    private readonly bool _preferGpu;

    /// <param name="detector">The detector, not yet prepared</param>
    /// <param name="numThreads">The CPU threads to prepare it with, asked each time it is prepared (2 when null)</param>
    /// <param name="preferGpu">Try the GPU first</param>
    public HandVisualAgent(HandGestureDetector detector, Func<int> numThreads = null, bool preferGpu = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _numThreads = numThreads ?? (() => 2);
        _preferGpu = preferGpu;
    }

    /// <summary>The "hand-gesture" agent's descriptor, with the backend in its name once prepared.</summary>
    public static VisualAgentDescriptor Describe(bool requiresPreparation = true, string backend = null) => new VisualAgentDescriptor(
        "hand-gesture", "Hand gestures · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "hand_gestures" }, VisualClasses.Gestures, requiresPreparation: requiresPreparation);

    /// <summary>A recognised gesture of each hand as an observation of that class, with the hand's box (from its landmarks); "none" is not reported.</summary>
    public static IReadOnlyList<VisualObservation> Observations(IReadOnlyList<Hand> hands)
    {
        var observations = new List<VisualObservation>();
        foreach (Hand hand in hands)
        {
            string gesture = VisualClasses.CanonicalGesture(hand.Gesture);
            if (gesture == null || hand.GestureScore < 0 || hand.GestureScore > 1) continue;
            double left = 1, top = 1, right = 0, bottom = 0;
            foreach (float[] point in hand.Landmarks)
            {
                left = Math.Min(left, point[0]); right = Math.Max(right, point[0]);
                top = Math.Min(top, point[1]); bottom = Math.Max(bottom, point[1]);
            }
            observations.Add(new VisualObservation("object", gesture, hand.GestureScore, VisualFrames.Box(left, top, right, bottom)));
        }
        return observations;
    }

    /// <inheritdoc/>
    public VisualAgentDescriptor Descriptor => Describe(!_detector.Imported, _detector.Backend);

    /// <inheritdoc/>
    public Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken) =>
        _detector.PrepareAsync(_preferGpu, _numThreads(), progress, cancellationToken);

    /// <inheritdoc/>
    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the hand models first.");
        using Mat input = VisualFrames.ToMat(frame);
        var hands = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, Observations(hands)));
    }

    /// <inheritdoc/>
    public void Dispose() => _detector.Dispose();
}
