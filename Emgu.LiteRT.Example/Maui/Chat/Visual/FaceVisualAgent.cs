// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// MediaPipe's face landmarker as a visual agent: each analysed frame goes through the face detector, landmark and blendshape models (3.7 MB, downloaded on
// first use, on the CPU: 8 ms a frame on a Galaxy S10), and the expressions of the largest face - smiling, eyes closed, winking, mouth open, eyebrows
// raised (FaceExpressions) - are reported as observations of those classes, so "take a photo when I smile" is an ordinary object monitor. Per the model
// cards it reads expressions for fun triggers: it doesn't identify anyone and isn't a drowsiness or attention monitor. VisualMonitorRuntime serializes
// the calls off the UI thread.
internal sealed class FaceVisualAgent : IVisualAgent
{
    private readonly FaceDetector _detector = DetectorFactory.Face();

    public VisualAgentDescriptor Descriptor => VisualHostCatalog.FaceDescriptor(!_detector.Imported, _detector.Backend);

    public Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken) =>
        _detector.PrepareAsync(false, Math.Min(2, CpuThreadSettings.Count), progress, cancellationToken);

    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the face models first.");
        using var input = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(input.DataPointer, y * input.Step), frame.Width * 3);
        var faces = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        var observations = VisualHostCatalog.FaceObservations(faces.Count > 0 ? faces[0] : null).ToArray();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, observations));
    }

    public void Dispose() => _detector.Dispose();
}
