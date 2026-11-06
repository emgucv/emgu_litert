//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
#if ANDROID
using System.Collections.Concurrent;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Application = Android.App.Application;

// Exact alarms, so a task runs on time and its alarm may start the foreground service; granted on Android 12 and 13, asked for
// on 14 and later ("Alarms & reminders").
[assembly: UsesPermission("android.permission.SCHEDULE_EXACT_ALARM")]
[assembly: UsesPermission("android.permission.FOREGROUND_SERVICE_SPECIAL_USE")]
#endif

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// Starts scheduled tasks' runs (<see cref="ScheduledTaskRunner"/>) on time while the app is closed. On Android an alarm
/// clock (<c>SetAlarmClock</c>: exact, fires in Doze, and isn't held by Samsung's process freezer) starts a foreground service (<c>TaskRunService</c>,
/// type specialUse) - an exact alarm is one of the exemptions from Android 12's rule that an app in the background can't
/// start one - which runs the task with a partial wake lock, posts the answer and stops; the alarms are armed again after a
/// restart, a change of time or an update (<c>ScheduledTaskRestoreReceiver</c>). Without exact alarms (Android 14 and later
/// until the user allows "Alarms &amp; reminders"), an inexact alarm posts "Task due" with a Run button instead, since the
/// service can't start from it. Elsewhere nothing yet (<see cref="IsSupported"/>): iOS gives no run at a chosen time.
/// </summary>
public static class ScheduledTaskAlarms
{
    /// <summary>Whether tasks run on their own on this platform.</summary>
    public static bool IsSupported =>
#if ANDROID
        true;
#else
        false;
#endif

    /// <summary>Whether the alarms are exact, so the runs start on time with the app closed (Android 14+: "Alarms &amp; reminders").</summary>
    public static bool CanRunOnTime
    {
        get
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(31))
                return true;
            var alarms = (AlarmManager)Application.Context.GetSystemService(Context.AlarmService);
            return alarms.CanScheduleExactAlarms();
#else
            return false;
#endif
        }
    }

    /// <summary>Open the system's "Alarms &amp; reminders" page for this app (Android 12+), where the user allows exact alarms.</summary>
    public static void OpenExactAlarmSettings()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            return;
        Context context = Application.Context;
        var intent = new Intent(Android.Provider.Settings.ActionRequestScheduleExactAlarm, Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
#endif
    }

    /// <summary>Arm a task's next run (or cancel it when the task is paused or gone).</summary>
    /// <param name="task">The task</param>
    /// <param name="after">The next run after this time; null for now</param>
    public static void Arm(ScheduledTask task, DateTime? after = null)
    {
        if (task == null)
            return;
        DateTime? next = task.NextRun(after ?? DateTime.Now);
        if (next == null)
            Cancel(task.Id);
        else
            SetAlarm(task.Id, next.Value, 0);
    }

    /// <summary>Cancel a task's alarms and its "due" notification.</summary>
    /// <param name="taskId">The task's id</param>
    public static void Cancel(string taskId)
    {
#if ANDROID
        if (string.IsNullOrEmpty(taskId))
            return;
        Context context = Application.Context;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService);
        foreach (int code in new[] { Code(taskId, Slot.Alarm), Code(taskId, Slot.Retry) })
            alarms.Cancel(PendingIntent.GetBroadcast(context, code, AlarmIntent(context, taskId, 0), Flags));
        NotificationManagerCompat.From(context).Cancel(NotificationTag, Code(taskId, Slot.Due));
#endif
    }

    /// <summary>Arm every task's next run again (the app's start, a restart, a change of time, exact alarms allowed).</summary>
    public static async Task RearmAllAsync()
    {
        IAgentTaskStore store = ScheduledTaskRunner.Options?.Tasks;
        if (store == null || !IsSupported)
            return;
        // Read while a private chat is open too: GetTaskAsync is.
        IList<ScheduledTask> tasks = await store.GetTasksAsync().ConfigureAwait(false);
        foreach (ScheduledTask task in tasks)
            Arm(task);
    }

    /// <summary>Run a task now, as its alarm would (Settings' "Run now"); call it while the app is on the screen.</summary>
    /// <param name="taskId">The task's id</param>
    public static void RunNow(string taskId)
    {
#if ANDROID
        TaskRunService.Start(Application.Context, taskId, 0, armNext: false);
#endif
    }

    // A run to try again later (the chat was replying, or private): the alarm's attempt counts up.
    internal static void Retry(string taskId, TimeSpan delay, int attempt) => SetAlarm(taskId, DateTime.Now + delay, attempt);

    private static void SetAlarm(string taskId, DateTime at, int attempt)
    {
#if ANDROID
        if (string.IsNullOrEmpty(taskId))
            return;
        Context context = Application.Context;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService);
        PendingIntent pending = PendingIntent.GetBroadcast(context, Code(taskId, attempt > 0 ? Slot.Retry : Slot.Alarm),
            AlarmIntent(context, taskId, attempt), Flags);
        long when = new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        // An alarm clock rather than SetExactAndAllowWhileIdle: Samsung's process freezer held an exact alarm for a frozen
        // (cached) process until the app was opened - a Galaxy S10's 9:02 run came at 9:07 (2026-11-05); an alarm clock must
        // ring. It shows Android's alarm icon, with the run as the next alarm; tapping that opens the app.
        if (CanRunOnTime)
        {
            PendingIntent show = PendingIntent.GetActivity(context, Code(taskId, Slot.Show),
                context.PackageManager.GetLaunchIntentForPackage(context.PackageName), Flags);
            alarms.SetAlarmClock(new AlarmManager.AlarmClockInfo(when, show), pending);
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(23))
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, when, pending);
        else
            alarms.SetExact(AlarmType.RtcWakeup, when, pending);
#endif
    }

#if ANDROID
    internal const string TaskExtra = "task";
    internal const string AttemptExtra = "attempt";
    internal const string NotificationTag = "scheduled_task";

    internal enum Slot { Alarm, Retry, Due, Result, Run, Show }

    internal static PendingIntentFlags Flags => PendingIntentFlags.UpdateCurrent
        | (OperatingSystem.IsAndroidVersionAtLeast(23) ? PendingIntentFlags.Immutable : 0);

    private static Intent AlarmIntent(Context context, string taskId, int attempt) =>
        new Intent(context, typeof(ScheduledTaskReceiver)).SetAction(ScheduledTaskReceiver.RunAction)
            .PutExtra(TaskExtra, taskId).PutExtra(AttemptExtra, attempt);

    // A stable request code (and notification id) for each of a task's slots (FNV-1a of its id): string.GetHashCode differs between runs.
    internal static int Code(string taskId, Slot slot)
    {
        uint hash = 2166136261;
        foreach (char c in taskId ?? string.Empty)
            hash = (hash ^ c) * 16777619;
        return 7000 + (int)(hash & 0xFFFFF) * 8 + (int)slot;
    }

    // "Task due" with a Run button, when the alarm couldn't start the service (an inexact alarm): a tap on a notification may.
    internal static void NotifyDue(Context context, string taskId, string description)
    {
        CreateChannels(context);
        PendingIntent run = TaskRunService.PendingStart(context, taskId, Code(taskId, Slot.Run));
        Notification notification = new NotificationCompat.Builder(context, TaskRunService.ResultChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcPopupReminder)
            .SetContentTitle("Scheduled task due")
            .SetContentText(description ?? "Tap Run to run it now.")
            .SetCategory(NotificationCompat.CategoryReminder)
            .SetContentIntent(run).SetAutoCancel(true)
            .AddAction(Android.Resource.Drawable.IcMediaPlay, "Run", run)
            .Build();
        NotificationManagerCompat.From(context).Notify(NotificationTag, Code(taskId, Slot.Due), notification);
    }

    internal static void CreateChannels(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService);
        notifications.CreateNotificationChannel(new NotificationChannel(TaskRunService.RunningChannelId, "Running scheduled tasks", NotificationImportance.Low));
        notifications.CreateNotificationChannel(new NotificationChannel(TaskRunService.ResultChannelId, "Scheduled tasks", NotificationImportance.High));
    }
#endif
}

#if ANDROID
/// <summary>
/// A scheduled task's alarm: starts <see cref="TaskRunService"/> for it, and arms its next run. Not open to other apps.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class ScheduledTaskReceiver : BroadcastReceiver
{
    internal const string RunAction = "com.emgu.litert.TASK_RUN";

    /// <inheritdoc/>
    public override void OnReceive(Context context, Intent intent)
    {
        string taskId = intent?.GetStringExtra(ScheduledTaskAlarms.TaskExtra);
        if (string.IsNullOrEmpty(taskId))
            return;
        int attempt = intent.GetIntExtra(ScheduledTaskAlarms.AttemptExtra, 0);
        bool started = true;
        // Straight away, while the exact alarm's exemption lasts.
        try { TaskRunService.Start(context, taskId, attempt, armNext: attempt == 0); }
        catch (Exception) { started = false; }
        if (started)
            return;
        PendingResult pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                ScheduledTask task = ScheduledTaskRunner.Options?.Tasks == null ? null
                    : await ScheduledTaskRunner.Options.Tasks.GetTaskAsync(taskId).ConfigureAwait(false);
                if (task == null || task.Paused)
                    return;
                ScheduledTaskAlarms.NotifyDue(context, taskId, task.Description);
                if (attempt == 0)
                    ScheduledTaskAlarms.Arm(task, DateTime.Now.AddMinutes(1));
            }
            catch (Exception) { }
            finally { pending.Finish(); }
        });
    }
}

/// <summary>
/// Arms the scheduled tasks' alarms again after a restart, a change of time or time zone, an update (Android clears alarms
/// then), or when exact alarms are allowed. Open to the system's broadcasts only for that.
/// </summary>
[BroadcastReceiver(Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionTimezoneChanged, Intent.ActionTimeChanged, Intent.ActionMyPackageReplaced,
    "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED" })]
public sealed class ScheduledTaskRestoreReceiver : BroadcastReceiver
{
    /// <inheritdoc/>
    public override void OnReceive(Context context, Intent intent)
    {
        PendingResult pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try { await ScheduledTaskAlarms.RearmAllAsync().ConfigureAwait(false); }
            catch (Exception) { }
            finally { pending.Finish(); }
        });
    }
}

/// <summary>
/// Runs scheduled tasks (<see cref="ScheduledTaskRunner"/>) as a foreground service of type specialUse, started by their
/// alarms with the app closed: an ongoing notification "Running: ..." and a partial wake lock while a task runs; then the
/// answer as a notification (tapping it opens its saved chat, <see cref="ChatLaunch"/>), or a later try when the chat was
/// busy, and the service stops. Runs that come together go one after the other.
/// </summary>
[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeSpecialUse)]
[Property("android.app.PROPERTY_SPECIAL_USE_FGS_SUBTYPE",
    Value = "Runs an on-device AI task the user scheduled (e.g. a morning weather summary) at its time and shows the answer.")]
public sealed class TaskRunService : Service
{
    internal const string RunningChannelId = "scheduled_task_running";
    internal const string ResultChannelId = "scheduled_tasks";
    private const string ArmNextExtra = "arm_next";
    // Not 1401-1404: the downloads', the monitors' and the background work's.
    private const int NotificationId = 1405;
    private const int MaxAttempts = 4;

    private readonly ConcurrentQueue<(string TaskId, int Attempt, bool ArmNext)> _queue = new();
    private readonly CancellationTokenSource _stop = new();
    private PowerManager.WakeLock _wakeLock;
    private bool _running;
    private string _current;

    internal static void Start(Context context, string taskId, int attempt, bool armNext) =>
        ContextCompat.StartForegroundService(context, StartIntent(context, taskId, attempt, armNext));

    // From a notification's tap or button: the user's interaction lets it start the service.
    internal static PendingIntent PendingStart(Context context, string taskId, int code)
    {
        Intent intent = StartIntent(context, taskId, 0, armNext: false);
        return OperatingSystem.IsAndroidVersionAtLeast(26)
            ? PendingIntent.GetForegroundService(context, code, intent, ScheduledTaskAlarms.Flags)
            : PendingIntent.GetService(context, code, intent, ScheduledTaskAlarms.Flags);
    }

    private static Intent StartIntent(Context context, string taskId, int attempt, bool armNext) =>
        new Intent(context, typeof(TaskRunService)).PutExtra(ScheduledTaskAlarms.TaskExtra, taskId)
            .PutExtra(ScheduledTaskAlarms.AttemptExtra, attempt).PutExtra(ArmNextExtra, armNext);

    /// <inheritdoc/>
    public override IBinder OnBind(Intent intent) => null;

    /// <inheritdoc/>
    public override void OnCreate()
    {
        base.OnCreate();
        ScheduledTaskAlarms.CreateChannels(this);
        var power = (PowerManager)GetSystemService(PowerService);
        _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "EmguLiteRT:ScheduledTask");
        _wakeLock.Acquire(TimeSpan.FromMinutes(30).Ticks / TimeSpan.TicksPerMillisecond);
    }

    /// <inheritdoc/>
    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        // Into the foreground first, within the seconds Android allows.
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, BuildNotification("Starting a scheduled task"), Android.Content.PM.ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, BuildNotification("Starting a scheduled task"));
        string taskId = intent?.GetStringExtra(ScheduledTaskAlarms.TaskExtra);
        if (!string.IsNullOrEmpty(taskId))
        {
            NotificationManagerCompat.From(this).Cancel(ScheduledTaskAlarms.NotificationTag, ScheduledTaskAlarms.Code(taskId, ScheduledTaskAlarms.Slot.Due));
            _queue.Enqueue((taskId, intent.GetIntExtra(ScheduledTaskAlarms.AttemptExtra, 0), intent.GetBooleanExtra(ArmNextExtra, false)));
        }
        if (!_running)
        {
            _running = true;
            _ = Task.Run(RunQueueAsync);
        }
        return StartCommandResult.NotSticky;
    }

    private async Task RunQueueAsync()
    {
        try
        {
            while (_queue.TryDequeue(out var item))
            {
                ScheduledTask task = null;
                try
                {
                    task = ScheduledTaskRunner.Options?.Tasks == null ? null
                        : await ScheduledTaskRunner.Options.Tasks.GetTaskAsync(item.TaskId).ConfigureAwait(false);
                    // The next run is armed before this one, so a failing run doesn't stop the schedule.
                    if (item.ArmNext && task != null)
                        ScheduledTaskAlarms.Arm(task, DateTime.Now.AddMinutes(1));
                    if (task == null)
                        continue;
                    _current = task.Description;
                    UpdateNotification("Running: " + task.Description);
                    ScheduledTaskOutcome outcome = await ScheduledTaskRunner.RunAsync(item.TaskId, _stop.Token).ConfigureAwait(false);
                    if (outcome.RetryIn is TimeSpan delay)
                    {
                        if (item.Attempt + 1 < MaxAttempts)
                            ScheduledTaskAlarms.Retry(item.TaskId, delay, item.Attempt + 1);
                        else
                            await ScheduledTaskRunner.RecordSkipAsync(item.TaskId, "the chat was busy").ConfigureAwait(false);
                    }
                    else if (outcome.Notify)
                        NotifyResult(outcome);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("Scheduled task " + item.TaskId + " failed: " + ex.Message);
                }
            }
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _running = false;
                // A run asked for while the last one finished.
                if (!_queue.IsEmpty)
                {
                    _running = true;
                    _ = Task.Run(RunQueueAsync);
                    return;
                }
                StopService();
            });
        }
    }

    // The answer: tapping it opens the run's saved chat.
    private void NotifyResult(ScheduledTaskOutcome outcome)
    {
        ScheduledTask task = outcome.Task;
        Intent open = PackageManager.GetLaunchIntentForPackage(PackageName);
        open.AddFlags(ActivityFlags.SingleTop);
        if (outcome.ChatId is int chatId)
            open.PutExtra(ChatLaunch.OpenChatExtra, chatId);
        int code = ScheduledTaskAlarms.Code(task.Id, ScheduledTaskAlarms.Slot.Result);
        Notification notification = new NotificationCompat.Builder(this, ResultChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcPopupReminder)
            .SetContentTitle(task.Description)
            .SetContentText(outcome.NotificationText)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(outcome.NotificationText))
            .SetContentIntent(PendingIntent.GetActivity(this, code, open, ScheduledTaskAlarms.Flags))
            .SetAutoCancel(true)
            .Build();
        NotificationManagerCompat.From(this).Notify(ScheduledTaskAlarms.NotificationTag, code, notification);
    }

    private Notification BuildNotification(string text) =>
        new NotificationCompat.Builder(this, RunningChannelId)
            .SetSmallIcon(Android.Resource.Drawable.StatNotifySync)
            .SetContentTitle("Scheduled task")
            .SetContentText(text)
            .SetProgress(0, 0, true).SetOngoing(true).SetOnlyAlertOnce(true)
            .SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)
            .Build();

    private void UpdateNotification(string text) =>
        ((NotificationManager)GetSystemService(NotificationService)).Notify(NotificationId, BuildNotification(text));

    /// <inheritdoc/>
    public override void OnTimeout(int startId, Android.Content.PM.ForegroundService foregroundServiceType)
    {
        _stop.Cancel();
        StopService();
    }

    private void StopService()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            StopForeground(StopForegroundFlags.Remove);
        else
            StopForeground(true);
        StopSelf();
    }

    /// <inheritdoc/>
    public override void OnDestroy()
    {
        _stop.Cancel();
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        base.OnDestroy();
    }
}
#endif
