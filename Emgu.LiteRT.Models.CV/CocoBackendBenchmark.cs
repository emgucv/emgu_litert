// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Diagnostics;
using System.Threading;

namespace Emgu.LiteRT.Models;

internal readonly record struct CocoBackendTiming(double CpuMilliseconds, double GpuMilliseconds)
{
    // Ties and invalid measurements keep the CPU backend.
    public bool UseGpu => IsFinite(CpuMilliseconds) && IsFinite(GpuMilliseconds)
        && CpuMilliseconds > 0 && GpuMilliseconds > 0 && GpuMilliseconds < CpuMilliseconds;

    // double.IsFinite is .NET Core 3.0+; this builds for netstandard2.0 too.
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

internal static class CocoBackendBenchmark
{
    internal static CocoBackendTiming Compare(Action cpu, Action gpu, CancellationToken token,
        Func<Action, double> measure = null)
    {
        measure ??= MeasureMilliseconds;
        for (int i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested(); cpu();
            token.ThrowIfCancellationRequested(); gpu();
        }

        var cpuTimes = new double[12];
        var gpuTimes = new double[12];
        for (int i = 0; i < cpuTimes.Length; i++)
        {
            // Alternate ordering to reduce first-run/clock/thermal bias between each pair.
            if (i % 2 == 0)
            {
                token.ThrowIfCancellationRequested(); cpuTimes[i] = measure(cpu);
                token.ThrowIfCancellationRequested(); gpuTimes[i] = measure(gpu);
            }
            else
            {
                token.ThrowIfCancellationRequested(); gpuTimes[i] = measure(gpu);
                token.ThrowIfCancellationRequested(); cpuTimes[i] = measure(cpu);
            }
        }
        token.ThrowIfCancellationRequested();
        Array.Sort(cpuTimes);
        Array.Sort(gpuTimes);
        return new CocoBackendTiming((cpuTimes[5] + cpuTimes[6]) / 2, (gpuTimes[5] + gpuTimes[6]) / 2);
    }

    private static double MeasureMilliseconds(Action run)
    {
        long start = Stopwatch.GetTimestamp();
        run();
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }
}
