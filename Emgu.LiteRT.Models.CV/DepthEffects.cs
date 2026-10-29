// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace Emgu.LiteRT.Models;

/// <summary>How strongly <see cref="DepthEffects.BlurBackground"/> blurs.</summary>
public enum BackgroundBlurStrength
{
    /// <summary>A soft blur: the background stays recognizable.</summary>
    Light,
    /// <summary>A phone portrait mode's blur.</summary>
    Medium,
    /// <summary>A heavy blur.</summary>
    Strong
}

/// <summary>Photo effects from a depth map (<see cref="DepthEstimator.Estimate"/>).</summary>
public static class DepthEffects
{
    // The split (Otsu's threshold) is the top of the background's depths: from it to this much nearer, the photo goes
    // from blurred to sharp - a soft edge, so hair and the depth map's soft outlines don't leave a halo.
    private const float EdgeWidth = 0.06f;

    /// <summary>
    /// Portrait mode for any photo: what is nearer than the split between subject and background stays sharp, and what
    /// is farther is blurred, more the farther it is (two blur levels mixed by depth). The split is Otsu's threshold of
    /// the depth's histogram (the subject and the background are its two peaks).
    /// </summary>
    /// <param name="bgr">The 8-bit BGR photo</param>
    /// <param name="depth">Its depth: float, the photo's size, 0 farthest to 1 nearest</param>
    /// <param name="strength">How strongly to blur</param>
    /// <returns>The photo with its background blurred</returns>
    public static Mat BlurBackground(Mat bgr, Mat depth, BackgroundBlurStrength strength = BackgroundBlurStrength.Medium)
    {
        if (bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR image.", nameof(bgr));
        if (depth.Size != bgr.Size || depth.Depth != DepthType.Cv32F || depth.NumberOfChannels != 1)
            throw new ArgumentException("Expected a float depth map the image's size.", nameof(depth));
        float split = SubjectSplit(depth);
        // The blur's size relative to the photo, so a small and a large photo look alike.
        double sigma = Math.Min(bgr.Width, bgr.Height) * (strength == BackgroundBlurStrength.Light ? 0.006 : strength == BackgroundBlurStrength.Strong ? 0.022 : 0.012);
        using var sharpWeight = Weights(depth, d => SmoothStep(split, split + EdgeWidth, d));
        using var backgroundOnly = new Mat();
        CvInvoke.Subtract(new ScalarArray(1.0), sharpWeight, backgroundOnly);
        using var near = BlurWithout(bgr, backgroundOnly, sigma * 0.5);
        using var far = BlurWithout(bgr, backgroundOnly, sigma);
        // The background: the slightly blurred photo just behind the subject, the fully blurred one at the back.
        using var farWeight = Weights(depth, d => Clamp01((split - d) / Math.Max(0.05f, split)));
        using var background = Blend(far, near, farWeight);
        return Blend(bgr, background, sharpWeight);
    }

    // The photo blurred from the pixels the weight keeps only (a normalized convolution): the subject's colours don't
    // bleed into the background around it as a glow.
    private static Mat BlurWithout(Mat bgr, Mat weight, double sigma)
    {
        using var image = new Mat();
        bgr.ConvertTo(image, DepthType.Cv32F);
        using var weight3 = new Mat();
        CvInvoke.Merge(new Emgu.CV.Util.VectorOfMat(weight, weight, weight), weight3);
        using var weighted = new Mat();
        CvInvoke.Multiply(image, weight3, weighted);
        using var blurredImage = new Mat();
        CvInvoke.GaussianBlur(weighted, blurredImage, System.Drawing.Size.Empty, sigma);
        using var blurredWeight = new Mat();
        CvInvoke.GaussianBlur(weight3, blurredWeight, System.Drawing.Size.Empty, sigma);
        // Where no background is near (inside the subject) the result isn't shown, so any small weight will do.
        CvInvoke.Max(blurredWeight, new ScalarArray(new MCvScalar(1e-3, 1e-3, 1e-3)), blurredWeight);
        using var divided = new Mat();
        CvInvoke.Divide(blurredImage, blurredWeight, divided);
        var result = new Mat();
        divided.ConvertTo(result, DepthType.Cv8U);
        return result;
    }

    /// <summary>The depth (0..1) that separates the subject (nearer) from the background: Otsu's threshold, the top of the background's depths.</summary>
    /// <param name="depth">Float depth, 0 farthest to 1 nearest</param>
    public static float SubjectSplit(Mat depth)
    {
        using var depth8 = new Mat();
        depth.ConvertTo(depth8, DepthType.Cv8U, 255);
        using var unused = new Mat();
        return (float)(CvInvoke.Threshold(depth8, unused, 0, 255, ThresholdType.Binary | ThresholdType.Otsu) / 255.0);
    }

    // a * weight + b * (1 - weight), per pixel.
    private static Mat Blend(Mat a, Mat b, Mat weight)
    {
        using var inverse = new Mat();
        CvInvoke.Subtract(new ScalarArray(1.0), weight, inverse);
        var result = new Mat();
        CvInvoke.BlendLinear(a, b, weight, inverse, result);
        return result;
    }

    private static Mat Weights(Mat depth, Func<float, float> weight)
    {
        var values = new float[depth.Rows * depth.Cols];
        depth.CopyTo(values);
        for (int i = 0; i < values.Length; i++)
            values[i] = weight(values[i]);
        var result = new Mat(depth.Rows, depth.Cols, DepthType.Cv32F, 1);
        result.SetTo(values);
        return result;
    }

    private static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;

    private static float SmoothStep(float from, float to, float x)
    {
        float t = Clamp01((x - from) / (to - from));
        return t * t * (3 - 2 * t);
    }
}
