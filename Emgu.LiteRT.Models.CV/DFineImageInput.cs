// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Shared with native regression tests so the resize, color order and scale use the production path.
internal static class DFineImageInput
{
    internal static void Write(DFine model, Mat bgr)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        int size = model.InputSize;
        IntPtr pixels = model.InputBuffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(size, size, DepthType.Cv32F, 3, pixels, size * 3 * sizeof(float));
            using var resized = new Mat();
            CvInvoke.Resize(bgr, resized, new System.Drawing.Size(size, size), 0, 0, Inter.Linear); // No letterbox.
            using var rgb = new Mat();
            CvInvoke.CvtColor(resized, rgb, ColorConversion.Bgr2Rgb);
            rgb.ConvertTo(input, DepthType.Cv32F, 1.0 / 255);
        }
        finally { model.InputBuffer.Unlock(); }
    }
}
