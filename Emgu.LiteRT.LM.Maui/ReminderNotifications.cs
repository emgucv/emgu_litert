//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if ANDROID
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Application = Android.App.Application;

[assembly: Android.App.UsesPermission("android.permission.RECEIVE_BOOT_COMPLETED")]
#elif IOS || MACCATALYST
using Foundation;
using UserNotifications;
#endif

namespace Emgu.LiteRT.LM.Maui;

/// <summary>How often a scheduled reminder comes again when the app hasn't scheduled the next one itself.</summary>
public enum ReminderRepeat
{
    /// <summary>Every day</summary>
    Day,
    /// <summary>Every week</summary>
    Week,
    /// <summary>On the last day of every month</summary>
    Month
}

/// <summary>
/// Local notifications at a time of day, e.g. a goal's reminder (<c>GoalsFeature</c>), each under a key the app chooses: one
/// scheduled notification per key, replaced when scheduled again and gone when cancelled. The app schedules the next one
/// it wants and schedules again when that changes (a goal logged as done); meanwhile the reminder comes again by itself,
/// with <c>repeatText</c>, so it keeps coming while the app isn't opened.
/// <para>
/// Android: an inexact alarm (<c>AlarmManager.SetAndAllowWhileIdle</c>, no exact-alarm permission: a few minutes' slack) wakes
/// <c>ReminderReceiver</c>, which posts it on the "Reminders" channel with "Snooze 1 h" (tapping it opens the app) and arms
/// the next; the schedule is kept in shared preferences and armed again after a restart or a change of time zone, at the
/// same time of day. iOS and Mac Catalyst: <c>UNUserNotificationCenter</c> requests for the next one and the six after it.
/// Elsewhere nothing is shown.
/// </para>
/// </summary>
public static class ReminderNotifications
{
    /// <summary>Whether this platform shows them: Android, iOS and Mac Catalyst.</summary>
    public static bool IsSupported =>
#if ANDROID || IOS || MACCATALYST
        true;
#else
        false;
#endif

    /// <summary>Ask for permission to show notifications (Android 13+, iOS, Mac); on the UI thread.</summary>
    /// <returns>Whether they may be shown</returns>
    public static async Task<bool> RequestPermissionAsync()
    {
#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;
        if (await Permissions.CheckStatusAsync<Permissions.PostNotifications>() == PermissionStatus.Granted)
            return true;
        return await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
#elif IOS || MACCATALYST
        var result = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        return result.Item1;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    /// <summary>Schedule a notification, replacing the key's.</summary>
    /// <param name="key">The key, e.g. "goal_3"</param>
    /// <param name="due">When (local time)</param>
    /// <param name="title">Its title</param>
    /// <param name="text">Its text</param>
    /// <param name="repeat">How often it comes again until the app schedules otherwise</param>
    /// <param name="repeatText">The text of the ones after it (the app can't say then what is left), or null for <paramref name="text"/></param>
    public static void Schedule(string key, DateTime due, string title, string text, ReminderRepeat repeat, string repeatText = null)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key));
#if ANDROID
        var entry = new ReminderReceiver.Entry(key, due, title, text, repeat, repeatText ?? text);
        ReminderReceiver.Save(entry);
        ReminderReceiver.Arm(entry);
#elif IOS || MACCATALYST
        Cancel(key);
        EnsureDelegate();
        DateTime at = due;
        for (int i = 0; i < 7; i++)
        {
            var content = new UNMutableNotificationContent { Title = title ?? string.Empty, Body = (i == 0 ? text : repeatText ?? text) ?? string.Empty, Sound = UNNotificationSound.Default };
            var when = new NSDateComponents { Year = at.Year, Month = at.Month, Day = at.Day, Hour = at.Hour, Minute = at.Minute };
            UNNotificationRequest request = UNNotificationRequest.FromIdentifier(key + "#" + i, content, UNCalendarNotificationTrigger.CreateTrigger(when, false));
            UNUserNotificationCenter.Current.AddNotificationRequest(request, error =>
            {
                if (error != null) System.Diagnostics.Debug.WriteLine("Reminder not scheduled: " + error.LocalizedDescription);
            });
            at = Next(at, repeat);
        }
#endif
    }

    /// <summary>Cancel the key's notification, scheduled or shown.</summary>
    /// <param name="key">The key</param>
    public static void Cancel(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
#if ANDROID
        ReminderReceiver.Cancel(key);
#elif IOS || MACCATALYST
        string[] ids = Enumerable.Range(0, 7).Select(i => key + "#" + i).ToArray();
        UNUserNotificationCenter.Current.RemovePendingNotificationRequests(ids);
        UNUserNotificationCenter.Current.RemoveDeliveredNotifications(ids);
#endif
    }

    /// <summary>When a reminder comes again: a day or a week later, or on the next month's last day.</summary>
    /// <param name="due">When it came</param>
    /// <param name="repeat">How often it comes</param>
    public static DateTime Next(DateTime due, ReminderRepeat repeat)
    {
        switch (repeat)
        {
            case ReminderRepeat.Week: return due.AddDays(7);
            case ReminderRepeat.Month:
                DateTime next = new DateTime(due.Year, due.Month, 1).AddMonths(2).AddDays(-1);
                return next.Date + due.TimeOfDay;
            default: return due.AddDays(1);
        }
    }

#if IOS || MACCATALYST
    // Shown while the app is in front too (iOS keeps them silent otherwise), unless the app has its own delegate.
    private static void EnsureDelegate()
    {
        if (UNUserNotificationCenter.Current.Delegate == null)
            UNUserNotificationCenter.Current.Delegate = new ShowInFront();
    }

    private sealed class ShowInFront : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);
    }
#endif
}

#if ANDROID
/// <summary>
/// Arms <see cref="ReminderNotifications"/>' reminders again after a restart, a change of time or time zone, or an update
/// (Android clears alarms then): each at its time of day. Open to the system's broadcasts only for that; showing and
/// snoozing is <see cref="ReminderReceiver"/>'s, which other apps can't reach.
/// </summary>
[BroadcastReceiver(Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionTimezoneChanged, Intent.ActionTimeChanged, Intent.ActionMyPackageReplaced })]
public sealed class ReminderRestoreReceiver : BroadcastReceiver
{
    /// <inheritdoc/>
    public override void OnReceive(Context context, Intent intent)
    {
        try { ReminderReceiver.RestoreAll(); }
        catch (Exception ex) { Android.Util.Log.Warn("EmguReminders", "Reminders not restored: " + ex.Message); }
    }
}

/// <summary>
/// The Android side of <see cref="ReminderNotifications"/>: shows a reminder when its alarm goes off and arms the next,
/// and snoozes one for an hour. This library declares it in the app's manifest; an app doesn't use it itself.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    private const string ShowAction = "com.emgu.litert.REMINDER_SHOW";
    private const string SnoozeAction = "com.emgu.litert.REMINDER_SNOOZE";
    private const string ChannelId = "reminders";
    private const string PreferencesName = "emgu_reminders";

    internal sealed record Entry(string Key, DateTime Due, string Title, string Text, ReminderRepeat Repeat, string RepeatText);

    /// <inheritdoc/>
    public override void OnReceive(Context context, Intent intent)
    {
        try
        {
            switch (intent?.Action)
            {
                case ShowAction:
                    Show(context, intent.GetStringExtra("key"), intent.GetStringExtra("title"), intent.GetStringExtra("text"));
                    // The next one, with the text that holds while the app isn't opened.
                    Entry entry = Load(intent.GetStringExtra("key"));
                    if (entry != null && !intent.GetBooleanExtra("snoozed", false))
                    {
                        DateTime next = entry.Due;
                        while (next <= DateTime.Now) next = ReminderNotifications.Next(next, entry.Repeat);
                        Entry following = entry with { Due = next, Text = entry.RepeatText };
                        Save(following);
                        Arm(following);
                    }
                    break;
                case SnoozeAction:
                    string key = intent.GetStringExtra("key");
                    NotificationManagerCompat.From(context).Cancel(Id(key));
                    SetAlarm(context, key, DateTime.Now.AddHours(1), intent.GetStringExtra("title"), intent.GetStringExtra("text"), true);
                    break;
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("EmguReminders", "Reminder failed: " + ex.Message);
        }
    }

    // Every reminder armed again at its time of day; one missed meanwhile isn't shown late.
    internal static void RestoreAll()
    {
        foreach (Entry saved in LoadAll())
        {
            DateTime due = saved.Due;
            while (due <= DateTime.Now) due = ReminderNotifications.Next(due, saved.Repeat);
            Entry again = due == saved.Due ? saved : saved with { Due = due, Text = saved.RepeatText };
            Save(again);
            Arm(again);
        }
    }

    private static void Show(Context context, string key, string title, string text)
    {
        var notifications = (NotificationManager)context.GetSystemService(Context.NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Reminders", NotificationImportance.Default)
            {
                Description = "Reminders of your goals, at the times you set"
            });
        Intent launch = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
        var snooze = new Intent(context, typeof(ReminderReceiver)).SetAction(SnoozeAction)
            .PutExtra("key", key).PutExtra("title", title).PutExtra("text", text);
        Notification notification = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcPopupReminder)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(text))
            .SetCategory(NotificationCompat.CategoryReminder)
            .SetAutoCancel(true)
            .SetContentIntent(PendingIntent.GetActivity(context, Id(key), launch, Flags))
            .AddAction(Android.Resource.Drawable.IcLockIdleAlarm, "Snooze 1 h", PendingIntent.GetBroadcast(context, Id(key) + 1, snooze, Flags))
            .Build();
        NotificationManagerCompat.From(context).Notify(Id(key), notification);
    }

    internal static void Arm(Entry entry) => SetAlarm(Application.Context, entry.Key, entry.Due, entry.Title, entry.Text, false);

    private static void SetAlarm(Context context, string key, DateTime due, string title, string text, bool snoozed)
    {
        var intent = new Intent(context, typeof(ReminderReceiver)).SetAction(ShowAction)
            .PutExtra("key", key).PutExtra("title", title).PutExtra("text", text).PutExtra("snoozed", snoozed);
        // A snoozed one is an alarm of its own, so the next day's stays armed.
        PendingIntent pending = PendingIntent.GetBroadcast(context, Id(key) + (snoozed ? 2 : 0), intent, Flags);
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService);
        long at = new DateTimeOffset(due).ToUnixTimeMilliseconds();
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
        else
            alarms.Set(AlarmType.RtcWakeup, at, pending);
    }

    internal static void Cancel(string key)
    {
        Context context = Application.Context;
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService);
        foreach (int offset in new[] { 0, 2 })
        {
            var intent = new Intent(context, typeof(ReminderReceiver)).SetAction(ShowAction);
            PendingIntent pending = PendingIntent.GetBroadcast(context, Id(key) + offset, intent, Flags | PendingIntentFlags.NoCreate);
            if (pending != null)
            {
                alarms.Cancel(pending);
                pending.Cancel();
            }
        }
        NotificationManagerCompat.From(context).Cancel(Id(key));
        Preferences(context).Edit().Remove(key).Apply();
    }

    private static PendingIntentFlags Flags => PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;

    // A stable number for the key (string.GetHashCode differs between runs).
    private static int Id(string key)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in key ?? string.Empty) hash = (hash ^ c) * 16777619;
            return 3000 + (hash & 0xFFFFF) * 4;
        }
    }

    private static ISharedPreferences Preferences(Context context) => context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    // An entry as one line: the time of day local (so a new time zone keeps it), the repeat, the texts.
    internal static void Save(Entry entry) =>
        Preferences(Application.Context).Edit().PutString(entry.Key, string.Join("\u001f",
            entry.Due.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), ((int)entry.Repeat).ToString(System.Globalization.CultureInfo.InvariantCulture),
            entry.Title ?? string.Empty, entry.Text ?? string.Empty, entry.RepeatText ?? string.Empty)).Apply();

    private static Entry Load(string key) => Parse(key, Preferences(Application.Context).GetString(key, null));

    private static IEnumerable<Entry> LoadAll() =>
        Preferences(Application.Context).All.Keys.Select(Load).Where(e => e != null).ToList();

    private static Entry Parse(string key, string line)
    {
        string[] parts = line?.Split('\u001f');
        if (parts == null || parts.Length < 5 || !DateTime.TryParseExact(parts[0], "yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out DateTime due))
            return null;
        return new Entry(key, due, parts[2], parts[3], (ReminderRepeat)int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), parts[4]);
    }
}
#endif
