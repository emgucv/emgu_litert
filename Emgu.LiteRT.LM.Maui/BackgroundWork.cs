//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Application = Android.App.Application;
#elif IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// Long work the user started - a research of several minutes - kept going while the app is in the background. On Android a
/// foreground service (<c>WorkForegroundService</c>) with a notification showing the progress and a Stop button, and a partial
/// wake lock: without it Android freezes the app soon after it leaves the screen, and may close it (a Pixel Fold did, the
/// research lost). On Mac Catalyst a user-initiated activity, so App Nap doesn't slow it while hidden; on iOS the short time iOS
/// gives an app after it leaves the screen. Elsewhere nothing is needed.
/// </summary>
public sealed class BackgroundWork : IDisposable
{
    private static readonly List<BackgroundWork> _running = new();
    private static readonly object _lock = new();

    private readonly Action _stop;
    private bool _disposed;
#if IOS || MACCATALYST
    private NSObject _activity;
    private nint _task = -1;
#endif

    private BackgroundWork(string title, string text, Action stop)
    {
        Title = title;
        Text = text;
        _stop = stop;
    }

    /// <summary>What the work is ("Researching"), the notification's title</summary>
    public string Title { get; }

    /// <summary>Its progress in words ("2 of 4: ..."), the notification's text</summary>
    public string Text { get; private set; }

    /// <summary>Raised (on any thread) when the running work or its progress changes.</summary>
    internal static event Action Changed;

    internal static BackgroundWork[] Snapshot()
    {
        lock (_lock)
            return _running.ToArray();
    }

    /// <summary>Begins work that goes on in the background. Call it while the app is on the screen (the user started it).</summary>
    /// <param name="title">What the work is, e.g. "Researching"</param>
    /// <param name="text">Its progress so far</param>
    /// <param name="stop">What the notification's Stop button does, or null for no button</param>
    /// <returns>The work, to <see cref="Update"/> and to dispose of when it ends</returns>
    public static async Task<BackgroundWork> BeginAsync(string title, string text = null, Action stop = null)
    {
        var work = new BackgroundWork(title, text, stop);
        lock (_lock)
            _running.Add(work);
        try
        {
#if ANDROID
            await WorkForegroundService.EnsureStartedAsync();
#elif IOS || MACCATALYST
            await MainThread.InvokeOnMainThreadAsync(work.BeginPlatform);
#else
            await Task.CompletedTask;
#endif
        }
        catch (Exception ex)
        {
            // The work still runs while the app is on the screen.
            System.Diagnostics.Debug.WriteLine("Background work: " + ex.Message);
        }
        Changed?.Invoke();
        return work;
    }

    /// <summary>Shows new progress.</summary>
    /// <param name="text">The progress in words</param>
    public void Update(string text)
    {
        if (_disposed || text == Text)
            return;
        Text = text;
        Changed?.Invoke();
    }

    internal void Stop() => _stop?.Invoke();

    internal bool CanStop => _stop != null;

    /// <summary>Ends the work. When the app isn't on the screen, <paramref name="done"/> is posted as a notification.</summary>
    /// <param name="done">What to tell the user, e.g. "The research is done", or null for nothing</param>
    public void Finish(string done)
    {
        if (_disposed)
            return;
#if ANDROID
        if (done != null)
            WorkForegroundService.NotifyDoneIfAway(Title, done);
#endif
        Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_lock)
            _running.Remove(this);
#if IOS || MACCATALYST
        MainThread.BeginInvokeOnMainThread(EndPlatform);
#endif
        Changed?.Invoke();
    }

#if IOS || MACCATALYST
    private void BeginPlatform()
    {
        if (_disposed)
            return;
#if MACCATALYST
        _activity = NSProcessInfo.ProcessInfo.BeginActivity(NSActivityOptions.UserInitiated, Title);
#else
        _task = UIApplication.SharedApplication.BeginBackgroundTask(Title, EndPlatform);
#endif
    }

    private void EndPlatform()
    {
        if (_activity != null)
        {
            NSProcessInfo.ProcessInfo.EndActivity(_activity);
            _activity = null;
        }
        if (_task != -1)
        {
            UIApplication.SharedApplication.EndBackgroundTask(_task);
            _task = -1;
        }
    }
#endif
}

#if ANDROID
/// <summary>
/// The Android foreground service of <see cref="BackgroundWork"/>: a notification with the work's progress and a Stop button, and a
/// partial wake lock. It starts with the first work and stops with the last; an app doesn't start it itself. This library declares
/// it, and the permissions it needs (those of <see cref="DownloadForegroundService"/>), in the app's manifest.
/// </summary>
[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class WorkForegroundService : Service
{
    private const string ChannelId = "background_work";
    private const string DoneChannelId = "background_work_done";
    private const string StopAction = "com.emgu.litert.STOP_WORK";
    // Not 1402, Emgu.LiteRT.LM.Maui.CV's monitoring service's: a meeting's work starts while that one still runs, and its
    // notification going would take this one's with it.
    private const int NotificationId = 1404;
    private const int DoneNotificationId = 1403;
    private static WorkForegroundService _instance;
    private static TaskCompletionSource<bool> _starting;
    private static bool _notificationPermissionRequested;
    private PowerManager.WakeLock _wakeLock;
    private bool _stopping;
    private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static async Task EnsureStartedAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_notificationPermissionRequested) return;
                _notificationPermissionRequested = true;
                // Asked only when not yet granted: work begun from a notification (a meeting's Stop) has no screen to ask on.
                if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() == PermissionStatus.Granted) return;
                // Denial doesn't prevent a foreground service; Android still lists it in Active apps.
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            });
        }
        // Started while the app is on the screen: Android doesn't let an app in the background start one.
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
                    ContextCompat.StartForegroundService(Application.Context, new Intent(Application.Context, typeof(WorkForegroundService)));
                    return _starting.Task;
                }
                catch (Exception)
                {
                    _starting = null;
                    throw;
                }
            });
            if (await ready.WaitAsync(TimeSpan.FromSeconds(10))) return;
        }
    }

    // A notification that the work is done, when the app isn't on the screen (tapping it opens the app).
    internal static void NotifyDoneIfAway(string title, string text)
    {
        var state = new ActivityManager.RunningAppProcessInfo();
        ActivityManager.GetMyMemoryState(state);
        if (state.Importance == Importance.Foreground)
            return;
        Context context = Application.Context;
        var notifications = (NotificationManager)context.GetSystemService(NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            notifications.CreateNotificationChannel(new NotificationChannel(DoneChannelId, "Finished work", NotificationImportance.Default));
        notifications.Notify(DoneNotificationId, new NotificationCompat.Builder(context, DoneChannelId)
            .SetSmallIcon(Android.Resource.Drawable.StatSysDownloadDone)
            .SetContentTitle(title).SetContentText(text)
            .SetContentIntent(OpenApp(context)).SetAutoCancel(true).Build());
    }

    private static PendingIntentFlags Flags => PendingIntentFlags.UpdateCurrent
        | (OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0);

    private static PendingIntent OpenApp(Context context) =>
        PendingIntent.GetActivity(context, 0, context.PackageManager.GetLaunchIntentForPackage(context.PackageName), Flags);

    /// <inheritdoc/>
    public override void OnCreate()
    {
        base.OnCreate();
        _instance = this;
        var notifications = (NotificationManager)GetSystemService(NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Work in the background", NotificationImportance.Low));
        BackgroundWork.Changed += OnWorkChanged;
        var power = (PowerManager)GetSystemService(PowerService);
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "EmguLiteRT:BackgroundWork");
        _wakeLock.Acquire();
    }

    /// <inheritdoc/>
    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        // Always enter foreground, including when the work ended before this callback was delivered.
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, BuildNotification(), Android.Content.PM.ForegroundService.TypeDataSync);
            else
                StartForeground(NotificationId, BuildNotification());
            _starting?.TrySetResult(true);
            _starting = null;
            if (intent?.Action == StopAction)
                foreach (BackgroundWork work in BackgroundWork.Snapshot())
                    work.Stop();
            Refresh();
        }
        catch (Exception ex)
        {
            _starting?.TrySetException(ex);
            _starting = null;
            StopWorkService();
        }
        return StartCommandResult.NotSticky;
    }

    private void OnWorkChanged() => MainThread.BeginInvokeOnMainThread(Refresh);

    private void Refresh()
    {
        if (_stopping) return;
        if (BackgroundWork.Snapshot().Length == 0) { StopWorkService(); return; }
        ((NotificationManager)GetSystemService(NotificationService)).Notify(NotificationId, BuildNotification());
    }

    private Notification BuildNotification()
    {
        BackgroundWork[] running = BackgroundWork.Snapshot();
        BackgroundWork work = running.LastOrDefault();
        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.StatNotifySync)
            .SetContentTitle(work?.Title ?? "Working")
            .SetContentText(work?.Text ?? string.Empty)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(work?.Text ?? string.Empty))
            .SetProgress(0, 0, true).SetOngoing(true).SetOnlyAlertOnce(true)
            .SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)
            .SetContentIntent(OpenApp(this));
        if (running.Any(w => w.CanStop))
            builder.AddAction(Android.Resource.Drawable.IcMediaPause, "Stop",
                PendingIntent.GetService(this, 1, new Intent(this, typeof(WorkForegroundService)).SetAction(StopAction), Flags));
        return builder.Build();
    }

    private void StopWorkService()
    {
        if (_stopping) return;
        _stopping = true;
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            StopForeground(StopForegroundFlags.Remove);
        else
            StopForeground(true);
        StopSelf();
    }

    /// <inheritdoc/>
    public override void OnTimeout(int startId, Android.Content.PM.ForegroundService foregroundServiceType)
    {
        // Android's daily limit for data-sync services (6 hours on Android 15): the work is stopped.
        foreach (BackgroundWork work in BackgroundWork.Snapshot())
            work.Stop();
        StopWorkService();
    }

    /// <inheritdoc/>
    public override void OnDestroy()
    {
        BackgroundWork.Changed -= OnWorkChanged;
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        if (ReferenceEquals(_instance, this)) _instance = null;
        _starting?.TrySetException(new InvalidOperationException("The background service stopped."));
        _starting = null;
        _closed.TrySetResult(false);
        base.OnDestroy();
    }

    /// <inheritdoc/>
    public override IBinder OnBind(Intent intent) => null;
}
#endif
