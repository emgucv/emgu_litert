// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// MediaPipe's holistic landmarker as a visual agent: each analysed frame goes through the body, both hands and the face in one pass (13.7 MB, downloaded on
// first use, on the CPU: 50-72 ms a frame on a Galaxy S10), and what they show together - a posture combined with an expression ("hand raised and
// smiling"), covering the eyes, a hand over the mouth or on the face (HolisticGestures) - is reported as observations of those classes, so "take a photo
// when I wave and smile" is an ordinary object monitor. Registered after the body-pose and face-expression agents, which keep the plain postures and
// expressions. It doesn't identify anyone. VisualMonitorRuntime serializes the calls off the UI thread.
internal sealed class HolisticVisualAgent : IVisualAgent
{
    private readonly HolisticDetector _detector = DetectorFactory.Holistic();
    private readonly PoseGestures.WaveDetector _wave = new PoseGestures.WaveDetector();

    public VisualAgentDescriptor Descriptor => VisualHostCatalog.HolisticDescriptor(!_detector.Imported, _detector.Backend);

    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        await _detector.PrepareAsync(false, Math.Min(2, CpuThreadSettings.Count), progress, cancellationToken).ConfigureAwait(false);
        _wave.Reset();
    }

    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the holistic models first.");
        using var input = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(input.DataPointer, y * input.Step), frame.Width * 3);
        HolisticResult result = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        var observations = VisualHostCatalog.HolisticObservations(result, frame.Width, frame.Height, _wave, frame.Timestamp).ToArray();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, observations));
    }

    public void Dispose() => _detector.Dispose();
}
