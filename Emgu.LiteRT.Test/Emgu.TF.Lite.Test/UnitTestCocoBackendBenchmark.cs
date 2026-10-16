// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Threading;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestCocoBackendBenchmark
{
    [TestCase(10, 8, true)]
    [TestCase(10, 12, false)]
    [TestCase(10, 10, false)]
    [TestCase(0, 0, false)]
    [TestCase(double.NaN, 8, false)]
    [TestCase(10, double.PositiveInfinity, false)]
    public void ChoosesGpuOnlyWhenStrictlyFaster(double cpuMs, double gpuMs, bool useGpu)
    {
        Action cpu = () => { };
        Action gpu = () => { };
        var result = CocoBackendBenchmark.Compare(cpu, gpu, CancellationToken.None,
            run => run == cpu ? cpuMs : gpuMs);
        Assert.That(result.UseGpu, Is.EqualTo(useGpu));
    }

    [Test]
    public void WarmsBothBackendsAlternatesOrderAndUsesMedians()
    {
        int cpuRuns = 0, gpuRuns = 0, cpuSamples = 0, gpuSamples = 0;
        Action cpu = () => cpuRuns++;
        Action gpu = () => gpuRuns++;
        var order = new List<string>();
        var result = CocoBackendBenchmark.Compare(cpu, gpu, CancellationToken.None, run =>
        {
            run();
            if (run == cpu) { order.Add("CPU"); return ++cpuSamples == 1 ? 1000 : 10; }
            order.Add("GPU"); return ++gpuSamples == 1 ? 2000 : 8;
        });
        Assert.Multiple(() =>
        {
            Assert.That(cpuRuns, Is.EqualTo(15));
            Assert.That(gpuRuns, Is.EqualTo(15));
            Assert.That(order.GetRange(0, 4), Is.EqualTo(new[] { "CPU", "GPU", "GPU", "CPU" }));
            Assert.That(result.CpuMilliseconds, Is.EqualTo(10));
            Assert.That(result.GpuMilliseconds, Is.EqualTo(8));
            Assert.That(result.UseGpu, Is.True);
        });
    }

    [Test]
    public void CancellationStopsBeforeStartingNextBackend()
    {
        using var cancellation = new CancellationTokenSource();
        int gpuRuns = 0;
        Action cpu = () => { };
        Action gpu = () => gpuRuns++;
        Assert.Throws<OperationCanceledException>(() => CocoBackendBenchmark.Compare(cpu, gpu, cancellation.Token, run =>
        {
            run(); cancellation.Cancel(); return 10;
        }));
        Assert.That(gpuRuns, Is.EqualTo(3), "GPU must only run the three warmups.");
    }
}
