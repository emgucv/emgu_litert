// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace Emgu.LiteRT.Models;

// Shared with the native tests, so they use the production path: RGB stretched to 686 x 518 (bicubic, as the export's
// processor), scaled to 0..1 and normalized per channel, written straight into the NCHW input planes.
internal static class DepthAnythingV2ImageInput
{
    private const int W = DepthAnythingV2.InputWidth, H = DepthAnythingV2.InputHeight;

    internal static void Write(DepthAnythingV2 model, Mat bgr)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR image.", nameof(bgr));
        using var resized = new Mat();
        CvInvoke.Resize(bgr, resized, new System.Drawing.Size(W, H), 0, 0, Inter.Cubic);
        using var channels = new VectorOfMat();
        CvInvoke.Split(resized, channels);
        IntPtr pixels = model.InputBuffer.Lock(TensorBufferLockMode.Write);
        try
        {
            for (int c = 0; c < 3; c++)
            {
                // Plane c is R, G, B: the BGR image's channel 2 - c. (x / 255 - mean) / std in one conversion.
                using var plane = new Mat(H, W, DepthType.Cv32F, 1, pixels + c * W * H * sizeof(float), W * sizeof(float));
                float std = DepthAnythingV2.Std[c];
                channels[2 - c].ConvertTo(plane, DepthType.Cv32F, 1.0 / (255.0 * std), -DepthAnythingV2.Mean[c] / std);
            }
        }
        finally { model.InputBuffer.Unlock(); }
    }
}
