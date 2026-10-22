using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Application = Android.App.Application;

namespace Maui.Demo.Lite;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DownloadForegroundService : Service
{
    private const string ChannelId = "model_downloads";
    private const string PauseAction = "com.emgu.litert.PAUSE_DOWNLOADS";
    private const int NotificationId = 1401;
    private static DownloadForegroundService _instance;
    private static TaskCompletionSource<bool> _starting;
    private PowerManager.WakeLock _wakeLock;
    private bool _stopping;
    private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static bool _notificationPermissionRequested;

    internal static async Task EnsureStartedAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_notificationPermissionRequested) return;
                _notificationPermissionRequested = true;
                // Denial doesn't prevent a foreground service; Android still lists it in Active apps.
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            });
        }
        // Start while the user's download action is in the foreground, before dispatching network work.
        while (true)
        {
            Task<bool> ready = await MainThread.InvokeOnMainThreadAsync<Task<bool>>(() =>
            {
                if (_instance?._stopping == true) return _instance._closed.Task;
                if (_instance != null) { _instance.Refresh(); return Task.FromResult(true); }
                if (_starting != null) return _starting.Task;
                _starting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    ContextCompat.StartForegroundService(Application.Context, new Intent(Application.Context, typeof(DownloadForegroundService)));
                    return _starting.Task;
                }
                catch (Exception ex)
                {
                    _starting = null;
                    throw new InvalidOperationException("Android could not start the download. Keep the app open and choose Continue to retry.", ex);
                }
            });
            if (await ready.WaitAsync(TimeSpan.FromSeconds(10))) return;
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        var notifications = (NotificationManager)GetSystemService(NotificationService);
        notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Model downloads", NotificationImportance.Low));
        BackgroundDownloads.Activities.Changed += OnDownloadsChanged;
        var power = (PowerManager)GetSystemService(PowerService);
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "EmguLiteRT:ModelDownload");
        _wakeLock.Acquire();
    }

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        // Always enter foreground, including when a download finished before this callback was delivered.
        try
        {
            StartForeground(NotificationId, BuildNotification(), ForegroundService.TypeDataSync);
            _starting?.TrySetResult(true);
            _starting = null;
            if (intent?.Action == PauseAction) BackgroundDownloads.Activities.PauseAll();
            Refresh();
        }
        catch (Exception ex)
        {
            _starting?.TrySetException(ex);
            _starting = null;
            BackgroundDownloads.Activities.PauseAll();
            StopDownloadService();
        }
        return StartCommandResult.NotSticky;
    }

    private void OnDownloadsChanged() => MainThread.BeginInvokeOnMainThread(Refresh);

    private void Refresh()
    {
        if (_stopping) return;
        if (BackgroundDownloads.Activities.Snapshot().Length == 0) { StopDownloadService(); return; }
        ((NotificationManager)GetSystemService(NotificationService)).Notify(NotificationId, BuildNotification());
    }

    private Notification BuildNotification()
    {
        var downloads = BackgroundDownloads.Activities.Snapshot();
        long received = downloads.Sum(d => d.Received);
        bool known = downloads.Length > 0 && downloads.All(d => d.Total.HasValue);
        long total = known ? downloads.Sum(d => d.Total.Value) : 0;
        int percent = total > 0 ? (int)Math.Clamp(received * 100.0 / total, 0, 100) : 0;
        var launch = PackageManager.GetLaunchIntentForPackage(PackageName);
        var open = PendingIntent.GetActivity(this, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var pause = PendingIntent.GetService(this, 1, new Intent(this, typeof(DownloadForegroundService)).SetAction(PauseAction),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownload)
            .SetContentTitle(downloads.Length == 1 ? "Downloading " + downloads[0].Name : "Downloading models")
            .SetContentText(known ? $"{received / 1e6:0.0} of {total / 1e6:0.0} MB" : "Preparing download...")
            .SetProgress(100, percent, !known).SetOngoing(true).SetOnlyAlertOnce(true)
            .SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)
            .SetContentIntent(open).AddAction(Android.Resource.Drawable.IcMediaPause, "Pause", pause).Build();
    }

    private void StopDownloadService()
    {
        if (_stopping) return;
        _stopping = true;
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnTimeout(int startId, ForegroundService foregroundServiceType)
    {
        BackgroundDownloads.Activities.PauseAll();
        StopDownloadService();
    }

    public override void OnDestroy()
    {
        BackgroundDownloads.Activities.Changed -= OnDownloadsChanged;
        // Unexpected service shutdown must also stop its network work and retain resumable bytes.
        if (!_stopping) BackgroundDownloads.Activities.PauseAll();
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        if (ReferenceEquals(_instance, this)) _instance = null;
        _starting?.TrySetException(new InvalidOperationException("The download service stopped. Return to the app to continue."));
        _starting = null;
        _closed.TrySetResult(false);
        base.OnDestroy();
    }

    public override IBinder OnBind(Intent intent) => null;
}
