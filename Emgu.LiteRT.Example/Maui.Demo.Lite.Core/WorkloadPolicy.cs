using System;
using System.Threading;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

internal readonly record struct WorkloadConditions(double BatteryLevel, bool OnExternalPower, bool TooHot,
    bool PhoneOrTablet = true);

// Kept independent of device APIs so thresholds, recovery and cancellation can be tested without a model.
internal sealed class WorkloadPolicy
{
    private bool _lowBattery;
    private readonly object _gate = new();

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
