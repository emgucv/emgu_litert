namespace Maui.Demo.Lite;

internal static class DeviceWorkload
{
    // Read on the UI thread: Apple's battery information is backed by UIDevice.
    internal static Task<WorkloadConditions> ReadAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        bool mobile = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
        double level = -1;
        bool powered = false;
        if (mobile)
        {
            try
            {
                level = Battery.Default.ChargeLevel;
                var source = Battery.Default.PowerSource;
                powered = source == BatteryPowerSource.AC || source == BatteryPowerSource.Usb
                    || source == BatteryPowerSource.Wireless;
            }
            catch (FeatureNotSupportedException) { }
        }
        bool hot = false;
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var power = (Android.OS.PowerManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.PowerService);
            hot = (int)power.CurrentThermalStatus >= 3; // THERMAL_STATUS_SEVERE
        }
#elif IOS || MACCATALYST
        hot = (int)Foundation.NSProcessInfo.ProcessInfo.ThermalState >= 2; // Serious / Critical
#endif
        return new WorkloadConditions(level, powered, hot, mobile);
    });
}
