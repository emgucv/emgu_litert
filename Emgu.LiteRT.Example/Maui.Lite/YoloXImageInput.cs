// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// Shared with native regression tests so color, padding, layout and stride use the production path.
internal static class YoloXImageInput
{
    internal static void Write(YoloX model, Mat bgr)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR frame.", nameof(bgr));
        double ratio = Math.Min((double)YoloX.InputSize / bgr.Width, (double)YoloX.InputSize / bgr.Height);
        int width = Math.Max(1, (int)(bgr.Width * ratio)), height = Math.Max(1, (int)(bgr.Height * ratio));
        IntPtr pixels = model.InputBuffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var input = new Mat(YoloX.InputSize, YoloX.InputSize, DepthType.Cv32F, 3, pixels, YoloX.InputSize * 3 * sizeof(float));
            input.SetTo(new MCvScalar(114, 114, 114));
            using var resized = new Mat();
            CvInvoke.Resize(bgr, resized, new System.Drawing.Size(width, height), 0, 0, Inter.Linear);
            using var region = new Mat(input, new System.Drawing.Rectangle(0, 0, width, height));
            resized.ConvertTo(region, DepthType.Cv32F); // BGR 0..255, matching upstream preproc; no color swap.
        }
        finally { model.InputBuffer.Unlock(); }
    }
}
