//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>
/// What the device says about heavy work right now (Emgu.LiteRT.LM.Maui's <c>DeviceWorkload.ReadAsync</c> reads it).
/// </summary>
/// <param name="BatteryLevel">The charge, 0..1; negative when unknown</param>
/// <param name="OnExternalPower">True when charging or plugged in</param>
/// <param name="TooHot">True when the device is hot enough to throttle (Android's thermal status severe or worse, Apple's serious or critical)</param>
/// <param name="PhoneOrTablet">True on a phone or tablet, where the battery counts; false on a desktop</param>
public readonly record struct WorkloadConditions(double BatteryLevel, bool OnExternalPower, bool TooHot,
    bool PhoneOrTablet = true);

/// <summary>
/// When to pause heavy on-device work (a model's background indexing, a long generation): while the device is hot, while the
/// battery is low (at 20% or less, until it is back at 25% or on power), and, if asked, until a charger is connected. It has no
/// device code, so the thresholds, recovery and cancellation can be tested without a device or a model. One instance remembers
/// the low-battery state between readings; it is thread-safe.
/// </summary>
public sealed class WorkloadPolicy
{
    private bool _lowBattery;
    private readonly object _gate = new();

    /// <summary>
    /// Why the work should wait now, or null to go on.
    /// </summary>
    /// <param name="conditions">The device's reading</param>
    /// <param name="onlyWhileCharging">True to wait for a charger on a phone or tablet</param>
    /// <returns>A short reason to show ("device is hot; ..."), or null</returns>
    public string PauseReason(WorkloadConditions conditions, bool onlyWhileCharging)
    {
        lock (_gate)
        {
            if (!conditions.PhoneOrTablet || conditions.OnExternalPower) _lowBattery = false;
            else if (conditions.BatteryLevel >= 0 && conditions.BatteryLevel <= 0.20) _lowBattery = true;
            else if (conditions.BatteryLevel >= 0.25) _lowBattery = false;
            if (conditions.TooHot) return "device is hot; waiting for it to cool down";
            if (_lowBattery) return "battery is low; connect power or charge to 25%";
            if (onlyWhileCharging && conditions.PhoneOrTablet && !conditions.OnExternalPower)
                return "waiting for a charger";
            return null;
        }
    }

    /// <summary>
    /// Return when the work may go on, reading the device every second until then.
    /// </summary>
    /// <param name="read">Reads the device (e.g. <c>DeviceWorkload.ReadAsync</c>)</param>
    /// <param name="onlyWhileCharging">Read at each check: true to wait for a charger</param>
    /// <param name="status">Told each new reason to wait, and null when the wait is over (only if it waited); may be null</param>
    /// <param name="cancel">Stops waiting with an <see cref="OperationCanceledException"/></param>
    /// <param name="delay">The pause between readings; null for one second (tests pass their own)</param>
    public async Task WaitAsync(Func<Task<WorkloadConditions>> read, Func<bool> onlyWhileCharging,
        Action<string> status, CancellationToken cancel, Func<CancellationToken, Task> delay = null)
    {
        string previous = null;
        try
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                string reason = PauseReason(await read().ConfigureAwait(false), onlyWhileCharging());
                cancel.ThrowIfCancellationRequested();
                if (reason == null) return;
                if (reason != previous) { previous = reason; status?.Invoke(reason); }
                await (delay?.Invoke(cancel) ?? Task.Delay(1000, cancel)).ConfigureAwait(false);
            }
        }
        finally
        {
            if (previous != null) status?.Invoke(null);
        }
    }
}
