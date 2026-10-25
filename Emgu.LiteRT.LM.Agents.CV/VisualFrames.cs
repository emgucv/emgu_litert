// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace Emgu.LiteRT.LM.Agents.Vision;

// What the agents share: a frame as an Emgu CV image, and observations' boxes.
internal static class VisualFrames
{
    // The frame's BGR pixels copied into a new 8-bit, 3-channel Mat, row by row (the frame's stride may be wider than its rows); no native pointer
    // escapes the agent.
    internal static Mat ToMat(VisualFrame frame)
    {
        var mat = new Mat(frame.Height, frame.Width, DepthType.Cv8U, 3);
        if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)) pixels = new ArraySegment<byte>(frame.Pixels.ToArray());
        for (int y = 0; y < frame.Height; y++)
            Marshal.Copy(pixels.Array, pixels.Offset + y * frame.Stride, IntPtr.Add(mat.DataPointer, y * mat.Step), frame.Width * 3);
        return mat;
    }

    internal static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    // A box from left, top, right and bottom as fractions of the image, cut to it.
    internal static VisualBox Box(double left, double top, double right, double bottom)
    {
        left = Clamp(left, 0, 1); top = Clamp(top, 0, 1);
        return new VisualBox(left, top, Math.Max(0, Clamp(right, 0, 1) - left), Math.Max(0, Clamp(bottom, 0, 1) - top));
    }

    // Each reading as an observation of its class, with the same box.
    internal static IReadOnlyList<VisualObservation> Observations(IEnumerable<(string Name, float Score)> readings, float[] box)
    {
        var observations = new List<VisualObservation>();
        foreach (var (name, score) in readings)
            observations.Add(new VisualObservation("object", name, Clamp(score, 0, 1), new VisualBox(box[0], box[1], Math.Max(0, box[2] - box[0]), Math.Max(0, box[3] - box[1]))));
        return observations;
    }
}
