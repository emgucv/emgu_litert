using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Application = Android.App.Application;

namespace Maui.Demo.Lite;

// Keeps the sound monitors (and the camera that records for them) running when the app is in the background or the screen is off (TODO item 54).
// Android stops the microphone and camera of an app that isn't visible unless a foreground service of the matching type runs: this one shows an
// ongoing notification ("Listening for ..." with a Stop button), holds a partial wake lock so the CPU keeps scoring sounds with the screen off,
// and posts a separate high-priority notification for each alert. The page starts it when a monitor is enabled, while the app is in front
// (Android 14 refuses to start a microphone or camera service from the background), and stops it when none is.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMicrophone | ForegroundService.TypeCamera)]
public sealed class MonitoringForegroundService : Service
{
    private const string ChannelId = "monitoring";
    private const string AlertChannelId = "monitor_alerts";
    private const string StopAction = "com.emgu.litert.STOP_MONITORING";
    private const int NotificationId = 1402;
    private static int _alertId = 1500;
    private static MonitoringForegroundService _instance;
    private static string _title = "Monitoring", _text = "";
    private static bool _wantsCamera;
    private static bool _notificationPermissionRequested;
    private PowerManager.WakeLock _wakeLock;

    // The Stop button: the page pauses its monitors, which stops the service.
    public static Action StopRequested { get; set; }

    // True while the service runs with the camera type, so the camera may be opened with the app in the background.
    public static bool CameraActive { get; private set; }
    public static bool IsRunning => _instance != null;

    // Starts the service or updates its notification. camera: a monitor records photos or clips, so the camera type is needed too.
    // Returns an error text, or null.
    internal static async Task<string> StartAsync(string title, string text, bool camera)
    {
        _title = title; _text = text; _wantsCamera = camera;
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_notificationPermissionRequested) return;
                _notificationPermissionRequested = true;
                // Without it the service still runs (Android lists it under Active apps), but alerts can't be shown as notifications.
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            });
        }
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                ContextCompat.StartForegroundService(Application.Context, new Intent(Application.Context, typeof(MonitoringForegroundService))));
            return null;
        }
        catch (Exception ex)
        {
            return "Android wouldn't keep it running in the background: " + ex.Message;
        }
    }

    internal static void Stop()
    {
        _wantsCamera = false;
        CameraActive = false;
        try { Application.Context.StopService(new Intent(Application.Context, typeof(MonitoringForegroundService))); } catch (Exception) { }
    }

    // A notification for something a monitor noticed, when the user isn't looking at the app.
    internal static void Alert(string title, string text, string details)
    {
        try
        {
            var context = Application.Context;
            var notifications = (NotificationManager)context.GetSystemService(NotificationService);
            notifications.CreateNotificationChannel(new NotificationChannel(AlertChannelId, "Monitor alerts", NotificationImportance.High));
            var notification = new NotificationCompat.Builder(context, AlertChannelId)
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetContentTitle(title).SetContentText(text).SetStyle(new NotificationCompat.BigTextStyle().BigText(text + "\n" + details))
                .SetAutoCancel(true).SetCategory(NotificationCompat.CategoryAlarm).SetPriority(NotificationCompat.PriorityHigh)
                .SetContentIntent(OpenApp(context)).Build();
            notifications.Notify(Interlocked.Increment(ref _alertId), notification);
        }
        catch (Exception) { }
    }

    private static PendingIntent OpenApp(Context context)
    {
        var launch = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
        return PendingIntent.GetActivity(context, 0, launch, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        var notifications = (NotificationManager)GetSystemService(NotificationService);
        notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Monitoring", NotificationImportance.Low));
        var power = (PowerManager)GetSystemService(PowerService);
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "EmguLiteRT:Monitoring");
        _wakeLock.Acquire();
    }

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == StopAction)
        {
            StopRequested?.Invoke();
            return StartCommandResult.NotSticky;
        }
        try
        {
            var type = ForegroundService.TypeMicrophone;
            // The camera type needs the permission when it starts (Android 14), or StartForeground throws.
            bool camera = _wantsCamera && ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.Camera) == Permission.Granted;
            if (camera) type |= ForegroundService.TypeCamera;
            StartForeground(NotificationId, BuildNotification(), type);
            CameraActive = camera;
        }
        catch (Exception)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
        }
        return StartCommandResult.NotSticky;
    }

    private Notification BuildNotification()
    {
        var stop = PendingIntent.GetService(this, 1, new Intent(this, typeof(MonitoringForegroundService)).SetAction(StopAction),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcMenuView)
            .SetContentTitle(_title).SetContentText(_text).SetOngoing(true).SetOnlyAlertOnce(true)
            .SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)
            .SetContentIntent(OpenApp(this)).AddAction(Android.Resource.Drawable.IcMediaPause, "Stop", stop).Build();
    }

    public override void OnDestroy()
    {
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        if (ReferenceEquals(_instance, this)) _instance = null;
        CameraActive = false;
        base.OnDestroy();
    }

    public override IBinder OnBind(Intent intent) => null;
}
