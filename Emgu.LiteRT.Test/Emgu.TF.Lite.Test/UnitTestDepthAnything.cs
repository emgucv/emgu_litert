// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Depth Anything V2 Small (downloads 27.7 MB on the first run) and the background blur.
[TestFixture, Category("Native")]
public class UnitTestDepthAnything
{
    private static string CacheDirectory => Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "DepthAnythingV2");

    private static Mat Load(string name) => CvInvoke.Imread(Path.Combine(AppContext.BaseDirectory, name), ImreadModes.ColorBgr);

    private static float At(Mat depth, double x, double y)
    {
        var values = new float[depth.Rows * depth.Cols];
        depth.CopyTo(values);
        return values[(int)(y * depth.Rows) * depth.Cols + (int)(x * depth.Cols)];
    }

    [Test]
    public async Task PeopleAreNearerThanWhatIsBehindThem()
    {
        using var estimator = new DepthEstimator(CacheDirectory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await estimator.PrepareAsync(false, 4, null, timeout.Token);
        Assert.That(estimator.Backend, Is.EqualTo("CPU"));
        using (Mat surfers = Load("surfers.jpg"))
        using (Mat depth = estimator.Estimate(surfers))
        {
            Assert.That(depth.Size, Is.EqualTo(surfers.Size));
            // The middle surfer's board against the sea behind.
            Assert.That(At(depth, 0.34, 0.6), Is.GreaterThan(At(depth, 0.5, 0.15) + 0.3));
            float split = DepthEffects.SubjectSplit(depth);
            Assert.That(split, Is.InRange(0.2f, 0.8f));
            Assert.That(At(depth, 0.34, 0.6), Is.GreaterThan(split));
            Assert.That(At(depth, 0.5, 0.15), Is.LessThan(split));
        }
        using (Mat pose = Load("pose_standing.jpg"))
        using (Mat depth = estimator.Estimate(pose))
            Assert.That(At(depth, 0.5, 0.55), Is.GreaterThan(At(depth, 0.15, 0.2) + 0.3), "the person against the sky");
        estimator.Dispose();
        Assert.Throws<InvalidOperationException>(() => estimator.Estimate(new Mat(10, 10, DepthType.Cv8U, 3)));
    }

    // The GPU at FP16, as DepthEstimator uses it, matches the CPU (it is checked the same way on a device).
    [Test, Explicit("Needs the GPU accelerator")]
    public async Task GpuMatchesTheCpu()
    {
        using var estimator = new DepthEstimator(CacheDirectory);
        await estimator.PrepareAsync(false, 4, null, CancellationToken.None);
        using var cpu = new DepthAnythingV2(estimator.ModelPath, 4);
        using var gpu = new DepthAnythingV2(estimator.ModelPath, 4, Emgu.LiteRT.HwAccelerators.Gpu | Emgu.LiteRT.HwAccelerators.Cpu, Emgu.LiteRT.GpuPrecision.Fp16);
        Assert.That(gpu.IsFullyAccelerated, Is.True);
        using Mat surfers = Load("surfers.jpg");
        DepthAnythingV2ImageInput.Write(cpu, surfers);
        DepthAnythingV2ImageInput.Write(gpu, surfers);
        Assert.That(DepthEstimator.Correlation(cpu.Run(), gpu.Run()), Is.GreaterThan(DepthEstimator.MinGpuCorrelation));
    }

    // Left half near, right half far: the left stays as it was, the right is blurred.
    [Test]
    public void TheBackgroundIsBlurredAndTheSubjectKept()
    {
        using var image = new Mat(200, 200, DepthType.Cv8U, 3);
        var random = new Random(1);
        var pixels = new byte[200 * 200 * 3];
        random.NextBytes(pixels);
        image.SetTo(pixels);
        using var depth = new Mat(200, 200, DepthType.Cv32F, 1);
        depth.SetTo(new MCvScalar(0.1));
        using (var left = new Mat(depth, new Rectangle(0, 0, 100, 200)))
            left.SetTo(new MCvScalar(0.9));
        using Mat blurred = DepthEffects.BlurBackground(image, depth);
        Assert.That(MeanDifference(image, blurred, new Rectangle(0, 0, 90, 200)), Is.LessThan(1));
        Assert.That(MeanDifference(image, blurred, new Rectangle(110, 0, 90, 200)), Is.GreaterThan(30));
        using Mat light = DepthEffects.BlurBackground(image, depth, BackgroundBlurStrength.Light);
        using Mat strong = DepthEffects.BlurBackground(image, depth, BackgroundBlurStrength.Strong);
        Assert.That(MeanDifference(image, strong, new Rectangle(110, 0, 90, 200)),
            Is.GreaterThan(MeanDifference(image, light, new Rectangle(110, 0, 90, 200))));
        Assert.Throws<ArgumentException>(() => DepthEffects.BlurBackground(image, new Mat(10, 10, DepthType.Cv32F, 1)));
    }

    private static double MeanDifference(Mat a, Mat b, Rectangle region)
    {
        using var ra = new Mat(a, region);
        using var rb = new Mat(b, region);
        using var diff = new Mat();
        CvInvoke.AbsDiff(ra, rb, diff);
        MCvScalar mean = CvInvoke.Mean(diff);
        return (mean.V0 + mean.V1 + mean.V2) / 3;
    }
}
