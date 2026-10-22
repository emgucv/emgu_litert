// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace Emgu.TF.Lite.Models;

/// <summary>
/// Emgu CV <see cref="Mat"/> input for the Interpreter-based <see cref="Mobilenet"/> and <see cref="Inception"/>
/// (Emgu.TF.Lite.Models), which themselves read image files only. The model must be initialized first.
/// </summary>
public static class InterpreterMatInput
{
    /// <summary>Resizes an 8-bit, 3-channel BGR image to 224 x 224 and writes it into the model's input tensor, scaled to [-1, 1] as the model expects.</summary>
    public static void WriteInput(this Mobilenet model, Mat bgr)
    {
        Write(model.InputTensor, bgr, 224, false, 1.0 / 128.0, -1.0 / 128.0);
    }

    /// <summary>Writes <paramref name="bgr"/> into the input tensor (see <see cref="WriteInput(Mobilenet, Mat)"/>) and runs the model.</summary>
    public static Mobilenet.RecognitionResult[] Recognize(this Mobilenet model, Mat bgr)
    {
        model.WriteInput(bgr);
        return model.Invoke();
    }

    /// <summary>Resizes an 8-bit, 3-channel BGR image to 299 x 299 and writes it into the model's input tensor as RGB, scaled to [0, 1].</summary>
    public static void WriteInput(this Inception model, Mat bgr)
    {
        Write(model.InputTensor, bgr, 299, true, 1.0 / 255.0, 0.0);
    }

    /// <summary>Writes <paramref name="bgr"/> into the input tensor (see <see cref="WriteInput(Inception, Mat)"/>) and runs the model.</summary>
    public static Inception.RecognitionResult[] Recognize(this Inception model, Mat bgr)
    {
        model.WriteInput(bgr);
        return model.Invoke();
    }

    private static void Write(Tensor input, Mat bgr, int side, bool toRgb, double scale, double shift)
    {
        if (bgr == null || bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR image.", nameof(bgr));
        System.Drawing.Size size = new System.Drawing.Size(side, side);
        using (Mat resized = new Mat(size, DepthType.Cv8U, 3))
        using (Mat tensor = new Mat(size, DepthType.Cv32F, 3, input.DataPointer, 3 * side * Marshal.SizeOf<float>()))
        {
            CvInvoke.Resize(bgr, resized, size);
            if (toRgb)
                CvInvoke.CvtColor(resized, resized, ColorConversion.Bgr2Rgb);
            resized.ConvertTo(tensor, DepthType.Cv32F, scale, shift);
        }
    }
}
