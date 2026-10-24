// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents.Audio;

namespace Maui.Demo.Lite;

// Monitoring in the background (TODO item 54): on Android a foreground service (MonitoringForegroundService) keeps the microphone - and the
// camera, for monitors that photograph or record - usable while the app isn't in front or the screen is off, shows what is being watched, and
// posts a notification for each alert. The page tells it when monitors start and stop; the Stop button in its notification pauses them all.
public partial class LiteRtLmChatPage
{
    private string _monitoringKey;

#if ANDROID
    // The camera may be opened and used without the page being visible: the service runs with the camera type.
    private bool BackgroundCapture => MonitoringForegroundService.CameraActive;
    private bool CameraUsable => (_pageVisible && !_cameraSuspended) || BackgroundCapture;
    private bool AppInBackground => _cameraSuspended || !_pageVisible;

    private void StartMonitoringStopButton() => MonitoringForegroundService.StopRequested = () => MainThread.BeginInvokeOnMainThread(async () =>
    {
        Meetings.Stop("the user pressed Stop in the notification");
        if (_soundRuntime == null) return;
        foreach (var monitor in _soundRuntime.Snapshot()) _soundRuntime.SetEnabled(monitor.Id, false);
        await UpdateSoundListeningAsync();
    });

    private void EndMonitoringStopButton() => MonitoringForegroundService.StopRequested = null;

    // Starts, updates or stops the service to match the enabled sound monitors and a meeting being recorded (which has the microphone:
    // the monitors are paused meanwhile).
    private async Task SyncMonitoringServiceAsync()
    {
        var monitors = _soundRuntime?.Snapshot().Where(m => m.Enabled).ToList() ?? new List<SoundMonitorInfo>();
        string meeting = Meetings.ActiveTitle;
        if (meeting == null && (_soundDisposed || monitors.Count == 0))
        {
            if (_monitoringKey == null) return;
            _monitoringKey = null;
            await MainThread.InvokeOnMainThreadAsync(MonitoringForegroundService.Stop);
            return;
        }
        bool camera = meeting == null && monitors.Any(m => m.ClipSeconds > 0 || m.TakePhoto);
        int heard = monitors.Sum(m => m.Heard);
        string title = meeting != null ? "Recording a meeting" : monitors.Count == 1 ? "Listening for a sound" : "Listening for " + monitors.Count + " sounds";
        string text = meeting != null ? (meeting.Length > 0 ? meeting + " · " : "") + "writing down what is said"
            : string.Join(", ", monitors.Select(m => m.Sound.Replace('_', ' '))) + (heard > 0 ? " · heard " + heard + (heard == 1 ? " time" : " times") : "");
        string key = title + "|" + text + "|" + camera;
        if (key == _monitoringKey && (!camera || MonitoringForegroundService.CameraActive)) return;
        string error = await MonitoringForegroundService.StartAsync(title, text, camera);
        _monitoringKey = error == null ? key : null;
        if (error != null) _soundStatus = "Listening, but only while the app is open. " + error;
    }

    // When the user isn't looking at the app, a sound that was heard also becomes a notification.
    private void NotifySoundAlert(SoundEvent occurrence, SoundMonitorInfo monitor)
    {
        if (!AppInBackground) return;
        MonitoringForegroundService.Alert("Heard " + (occurrence.Label ?? monitor.Sound).ToLowerInvariant(),
            occurrence.At.ToString("HH:mm:ss") + " · " + Math.Round(occurrence.Score * 100) + "%", monitor.Description);
    }
#else
    private bool BackgroundCapture => false;
    private bool CameraUsable => _pageVisible && !_cameraSuspended;
    private void StartMonitoringStopButton() { }
    private void EndMonitoringStopButton() { }
    private Task SyncMonitoringServiceAsync() => Task.CompletedTask;
    private void NotifySoundAlert(SoundEvent occurrence, SoundMonitorInfo monitor) { }
#endif
}
