// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.LiteRT.Models;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Vision;

namespace Maui.Demo.Lite;

// MediaPipe's hand gestures as a visual agent (TODO item 47): each analysed frame goes through the palm detector, the landmark model and the gesture
// classifier, and every hand with a recognised gesture is reported as an observation of that gesture's class ("thumb up", "victory"...). A monitor for
// "thumb up" is then an ordinary object monitor, with the same hold time, actions and episodes as one for a person. All native/model work is
// serialized off the UI thread by VisualMonitorRuntime; the models (8 MB) are downloaded on first use, and it runs on the CPU, which was as fast as the GPU
// for these small models on the phones (Emgu.LiteRT.Models/HAND_GESTURES.md).
internal sealed class HandVisualAgent : IVisualAgent
{
    private readonly HandGestureDetector _detector = DetectorFactory.Hand();

    public VisualAgentDescriptor Descriptor => VisualHostCatalog.HandDetector(!_detector.Imported, _detector.Backend);

    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        await _detector.PrepareAsync(false, Math.Min(2, CpuThreadSettings.Count), progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the hand models first.");
        using var input = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        // Honor arbitrary frame strides; no native pointers escape the adapter.
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(input.DataPointer, y * input.Step), frame.Width * 3);
        var hands = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, VisualHostCatalog.GestureObservations(hands).ToArray()));
    }

    public void Dispose() => _detector.Dispose();
}
