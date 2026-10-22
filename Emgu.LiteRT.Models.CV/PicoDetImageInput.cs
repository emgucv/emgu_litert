// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Shared with native regression tests so the resize, color order and normalization use the production path.
internal static class PicoDetImageInput
{
    internal static void Write(PicoDet model, Mat bgr)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        int size = PicoDet.InputSize;
        IntPtr pixels = model.InputBuffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            using var resized = new Mat();
            CvInvoke.Resize(bgr, resized, new System.Drawing.Size(size, size), 0, 0, Inter.Cubic); // No letterbox.
            using var rgb = new Mat();
            CvInvoke.CvtColor(resized, rgb, ColorConversion.Bgr2Rgb);
            rgb.ConvertTo(input, DepthType.Cv32F, 1.0 / 255);
            // (x - mean) / std per channel (RGB order), after the scaling above.
            using var mean = new Mat(size, size, DepthType.Cv32F, 3);
            mean.SetTo(new MCvScalar(0.485, 0.456, 0.406));
            using var scale = new Mat(size, size, DepthType.Cv32F, 3);
            scale.SetTo(new MCvScalar(1 / 0.229, 1 / 0.224, 1 / 0.225));
            CvInvoke.Subtract(input, mean, input);
            CvInvoke.Multiply(input, scale, input);
        }
        finally { model.InputBuffer.Unlock(); }
    }
}
