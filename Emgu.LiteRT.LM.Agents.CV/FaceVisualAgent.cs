// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents.Vision;

/// <summary>
/// MediaPipe's face landmarker (<see cref="FaceDetector"/>) as a visual agent: each analysed frame goes through the face detector, landmark and blendshape
/// models, and the expressions of the largest face (<see cref="FaceExpressions"/>: smiling, eyes closed, winking, mouth open, eyebrows raised) are
/// observations of those classes, so "take a photo when I smile" is an ordinary object monitor. Per the model cards it reads expressions for fun
/// triggers: it doesn't identify anyone and isn't a drowsiness or attention monitor. <see cref="VisualMonitorRuntime"/> serializes the calls off the UI
/// thread. The agent owns the detector and disposes it.
/// </summary>
public sealed class FaceVisualAgent : IVisualAgent
{
    private readonly FaceDetector _detector;
    private readonly Func<int> _numThreads;
    private readonly bool _preferGpu;

    /// <param name="detector">The detector, not yet prepared</param>
    /// <param name="numThreads">The CPU threads to prepare it with, asked each time it is prepared (2 when null)</param>
    /// <param name="preferGpu">Try the GPU first</param>
    public FaceVisualAgent(FaceDetector detector, Func<int> numThreads = null, bool preferGpu = false)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _numThreads = numThreads ?? (() => 2);
        _preferGpu = preferGpu;
    }

    /// <summary>The "face-expression" agent's descriptor, with the backend in its name once prepared.</summary>
    public static VisualAgentDescriptor Describe(bool requiresPreparation = true, string backend = null) => new VisualAgentDescriptor(
        "face-expression", "Face expressions · MediaPipe" + (backend == null ? "" : " · LiteRT " + backend),
        new[] { "object_detection", "face_expression" }, VisualClasses.FaceClasses, requiresPreparation: requiresPreparation);

    /// <summary>The expressions of a face as observations of their classes, each with the box of the face's landmarks; none without a face. Their scores
    /// are scaled so each expression's threshold is 0.5, the confidence the planner passes by default.</summary>
    public static IReadOnlyList<VisualObservation> Observations(Face face) => face == null ? new List<VisualObservation>()
        : VisualFrames.Observations(FaceExpressions.Classify(face).Select(r => (r.Name, r.Score)), FaceExpressions.BoxOf(face));

    /// <inheritdoc/>
    public VisualAgentDescriptor Descriptor => Describe(!_detector.Imported, _detector.Backend);

    /// <inheritdoc/>
    public Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken) =>
        _detector.PrepareAsync(_preferGpu, _numThreads(), progress, cancellationToken);

    /// <inheritdoc/>
    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the face models first.");
        using Mat input = VisualFrames.ToMat(frame);
        var faces = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, Observations(faces.Count > 0 ? faces[0] : null)));
    }

    /// <inheritdoc/>
    public void Dispose() => _detector.Dispose();
}
