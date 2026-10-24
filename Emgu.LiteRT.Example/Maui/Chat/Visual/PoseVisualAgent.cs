// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// MediaPipe's pose landmarks as a visual agent (TODO item 52): each analysed frame goes through the pose detector and landmark model (the lite
// variant, 5.8 MB, downloaded on first use, on the CPU: 26 ms a frame on a Galaxy S10), and the person's postures - arms raised, hand raised, standing,
// sitting, lying down, and waving from the last two seconds - are reported as observations of those classes. A monitor for "waving" is then an ordinary
// object monitor, with the same hold time, actions and episodes as one for a person. All model work is serialized off the UI thread by
// VisualMonitorRuntime, which is also what makes one wave detector per agent enough.
internal sealed class PoseVisualAgent : IVisualAgent
{
    private readonly PoseDetector _detector = DetectorFactory.Pose();
    private readonly PoseGestures.WaveDetector _wave = new PoseGestures.WaveDetector();

    public VisualAgentDescriptor Descriptor => VisualHostCatalog.PoseDescriptor(!_detector.Imported, _detector.Backend);

    public async Task PrepareAsync(Action<string> progress, CancellationToken cancellationToken)
    {
        await _detector.PrepareAsync(PoseModelFiles.Lite, false, Math.Min(2, CpuThreadSettings.Count), progress, cancellationToken).ConfigureAwait(false);
        _wave.Reset();
    }

    public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_detector.Imported) throw new InvalidOperationException("Prepare the pose models first.");
        using var input = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(input.DataPointer, y * input.Step), frame.Width * 3);
        var poses = _detector.Detect(input);
        cancellationToken.ThrowIfCancellationRequested();
        var observations = VisualHostCatalog.PoseObservations(poses.Count > 0 ? poses[0] : null, frame.Width, frame.Height, _wave, frame.Timestamp).ToArray();
        return Task.FromResult(new VisualAnalysis(Descriptor.Id, frame, observations));
    }

    public void Dispose() => _detector.Dispose();
}
