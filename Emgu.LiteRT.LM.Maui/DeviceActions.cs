//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// What a device action did: the text for the model (AgentTools.DeviceActionDone / DeviceActionFailed) and a short
/// one for the confirmation card.
/// </summary>
public sealed class DeviceActionOutcome
{
    /// <summary>True when it was done.</summary>
    public bool Done { get; private set; }

    /// <summary>What the model is told.</summary>
    public string ForModel { get; private set; }

    /// <summary>What the card says.</summary>
    public string ForUser { get; private set; }

    /// <summary>An action done.</summary>
    /// <param name="forModel">What was done, for the model ("the timer is running in the clock app")</param>
    /// <param name="forUser">What the card says ("Timer started")</param>
    public static DeviceActionOutcome Success(string forModel, string forUser) =>
        new DeviceActionOutcome { Done = true, ForModel = AgentTools.DeviceActionDone(forModel), ForUser = forUser };

    /// <summary>An action that couldn't be done.</summary>
    /// <param name="reason">Why, in words for the model and the user</param>
    public static DeviceActionOutcome Failure(string reason) =>
        new DeviceActionOutcome { ForModel = AgentTools.DeviceActionFailed(reason), ForUser = "Couldn't: " + reason };
}

/// <summary>
/// Does the chat agent's device actions (Emgu.LiteRT.LM.Agents' DeviceActions) once the user has confirmed them:
/// Android - timers and alarms through the clock app's AlarmClock intents, events through the calendar app's insert
/// intent (it opens filled in; the user saves there), directions through Google Maps' URL; iOS / Mac Catalyst -
/// events and reminders saved with EventKit (write-only calendar access, full reminders access), directions in
/// Apple Maps; Windows - directions in Bing Maps. iOS has no public timer or alarm API, Android no standard
/// reminders app. <see cref="Features.DeviceActionsFeature"/> offers the tools and asks the user first.
/// </summary>
public static class DeviceActions
{
    /// <summary>The actions this platform can do.</summary>
    public static DeviceActionKinds Supported
    {
        get
        {
#if ANDROID
            return DeviceActionKinds.Timer | DeviceActionKinds.Alarm | DeviceActionKinds.CalendarEvent | DeviceActionKinds.Directions;
#elif IOS || MACCATALYST
            return DeviceActionKinds.CalendarEvent | DeviceActionKinds.Reminder | DeviceActionKinds.Directions;
#else
            return DeviceActionKinds.Directions;
#endif
        }
    }

    /// <summary>Do a confirmed action (on the UI thread); never throws.</summary>
    /// <param name="request">The action, as the tool read it</param>
    public static async Task<DeviceActionOutcome> PerformAsync(DeviceActionRequest request)
    {
        try
        {
            switch (request)
            {
                case DirectionsRequest directions:
                    return await OpenDirectionsAsync(directions);
#if ANDROID
                case TimerRequest timer:
                    return SetTimer(timer);
                case AlarmRequest alarm:
                    return SetAlarm(alarm);
                case CalendarEventRequest calendarEvent:
                    return AddCalendarEvent(calendarEvent);
#elif IOS || MACCATALYST
                case CalendarEventRequest calendarEvent:
                    return await AddCalendarEventAsync(calendarEvent);
                case ReminderRequest reminder:
                    return await AddReminderAsync(reminder);
#endif
                default:
                    return DeviceActionOutcome.Failure("this device can't do that");
            }
        }
        catch (Exception ex)
        {
            return DeviceActionOutcome.Failure(ex.Message.TrimEnd('.'));
        }
    }

    private static async Task<DeviceActionOutcome> OpenDirectionsAsync(DirectionsRequest request)
    {
        string destination = Uri.EscapeDataString(request.Destination);
#if IOS || MACCATALYST
        string flag = request.Mode == TravelMode.Walking ? "w" : request.Mode == TravelMode.Transit ? "r"
            : request.Mode == TravelMode.Driving ? "d" : null;
        string url = "https://maps.apple.com/?daddr=" + destination + (flag == null ? "" : "&dirflg=" + flag);
#elif WINDOWS
        string mode = request.Mode == TravelMode.Walking ? "w" : request.Mode == TravelMode.Transit ? "t" : "d";
        string url = "bingmaps:?rtp=~adr." + destination + "&mode=" + mode;
#else
        string mode = request.Mode == TravelMode.Walking ? "walking" : request.Mode == TravelMode.Transit ? "transit"
            : request.Mode == TravelMode.Cycling ? "bicycling" : "driving";
        string url = "https://www.google.com/maps/dir/?api=1&destination=" + destination + "&travelmode=" + mode;
#endif
        if (!await Launcher.Default.OpenAsync(new Uri(url)))
            return DeviceActionOutcome.Failure("no maps app could be opened");
        return DeviceActionOutcome.Success("the maps app is open with " + request.Summary, "Opened maps");
    }

#if ANDROID
    private static bool Start(Android.Content.Intent intent)
    {
        Android.App.Activity activity = Platform.CurrentActivity;
        if (activity == null || intent.ResolveActivity(activity.PackageManager) == null)
            return false;
        activity.StartActivity(intent);
        return true;
    }

    private static DeviceActionOutcome SetTimer(TimerRequest request)
    {
        var intent = new Android.Content.Intent(Android.Provider.AlarmClock.ActionSetTimer);
        intent.PutExtra(Android.Provider.AlarmClock.ExtraLength, (int)Math.Round(request.Duration.TotalSeconds));
        intent.PutExtra(Android.Provider.AlarmClock.ExtraSkipUi, true);
        if (!string.IsNullOrEmpty(request.Label))
            intent.PutExtra(Android.Provider.AlarmClock.ExtraMessage, request.Label);
        if (!Start(intent))
            return DeviceActionOutcome.Failure("there is no clock app that takes timers");
        return DeviceActionOutcome.Success(request.Summary + " is running in the clock app", "Timer started");
    }

    private static DeviceActionOutcome SetAlarm(AlarmRequest request)
    {
        // The intent takes an hour and minute: the alarm rings the next time it comes.
        if (request.Time - DateTime.Now > TimeSpan.FromHours(24))
            return DeviceActionOutcome.Failure("the clock app can only set an alarm for the next 24 hours; a calendar event can remind the user further ahead");
        var intent = new Android.Content.Intent(Android.Provider.AlarmClock.ActionSetAlarm);
        intent.PutExtra(Android.Provider.AlarmClock.ExtraHour, request.Time.Hour);
        intent.PutExtra(Android.Provider.AlarmClock.ExtraMinutes, request.Time.Minute);
        intent.PutExtra(Android.Provider.AlarmClock.ExtraSkipUi, true);
        if (!string.IsNullOrEmpty(request.Label))
            intent.PutExtra(Android.Provider.AlarmClock.ExtraMessage, request.Label);
        if (!Start(intent))
            return DeviceActionOutcome.Failure("there is no clock app that takes alarms");
        return DeviceActionOutcome.Success(request.Summary + " is set in the clock app", "Alarm set");
    }

    // The calendar app opens with the event filled in, and the user saves it there (writing it directly would need
    // calendar permissions and a choice of calendar).
    private static DeviceActionOutcome AddCalendarEvent(CalendarEventRequest request)
    {
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionInsert);
        intent.SetData(Android.Provider.CalendarContract.Events.ContentUri);
        intent.PutExtra("beginTime", new DateTimeOffset(request.Start).ToUnixTimeMilliseconds());   // CalendarContract.EXTRA_EVENT_BEGIN_TIME
        intent.PutExtra("endTime", new DateTimeOffset(request.End).ToUnixTimeMilliseconds());       // EXTRA_EVENT_END_TIME
        intent.PutExtra("allDay", request.AllDay);                                                  // EXTRA_EVENT_ALL_DAY
        intent.PutExtra("title", request.EventTitle);                                               // Events.TITLE
        if (!string.IsNullOrEmpty(request.Location))
            intent.PutExtra("eventLocation", request.Location);                                     // Events.EVENT_LOCATION
        if (!Start(intent))
            return DeviceActionOutcome.Failure("there is no calendar app");
        // Not "added": the model said so when told only that the calendar app was open.
        return DeviceActionOutcome.Success("the calendar app is open with " + request.Summary
            + " filled in, but it isn't saved yet: ask the user to check it and tap Save there", "Opened the calendar");
    }
#endif

#if IOS || MACCATALYST
    private static EventKit.EKEventStore _store;
    private static EventKit.EKEventStore Store => _store ??= new EventKit.EKEventStore();

    private static Foundation.NSDate ToNSDate(DateTime local) => (Foundation.NSDate)DateTime.SpecifyKind(local, DateTimeKind.Local);

    private static async Task<DeviceActionOutcome> AddCalendarEventAsync(CalendarEventRequest request)
    {
        bool granted;
        if (OperatingSystem.IsIOSVersionAtLeast(17) || OperatingSystem.IsMacCatalystVersionAtLeast(17))
            granted = (await Store.RequestWriteOnlyAccessToEventsAsync()).Item1;
        else
            granted = (await Store.RequestAccessAsync(EventKit.EKEntityType.Event)).Item1;
        if (!granted)
            return DeviceActionOutcome.Failure("the user hasn't allowed this app to add calendar events (Settings > Privacy > Calendars)");
        EventKit.EKEvent calendarEvent = EventKit.EKEvent.FromStore(Store);
        calendarEvent.Title = request.EventTitle;
        calendarEvent.StartDate = ToNSDate(request.Start);
        calendarEvent.EndDate = ToNSDate(request.AllDay ? request.End.AddSeconds(-1) : request.End);
        calendarEvent.AllDay = request.AllDay;
        if (!string.IsNullOrEmpty(request.Location))
            calendarEvent.Location = request.Location;
        calendarEvent.Calendar = Store.DefaultCalendarForNewEvents;
        if (calendarEvent.Calendar == null)
            return DeviceActionOutcome.Failure("there is no calendar to add it to");
        if (!Store.SaveEvent(calendarEvent, EventKit.EKSpan.ThisEvent, true, out Foundation.NSError error))
            return DeviceActionOutcome.Failure(error?.LocalizedDescription ?? "the calendar didn't save it");
        return DeviceActionOutcome.Success(request.Summary + " is in the user's calendar", "Added to the calendar");
    }

    private static async Task<DeviceActionOutcome> AddReminderAsync(ReminderRequest request)
    {
        bool granted;
        if (OperatingSystem.IsIOSVersionAtLeast(17) || OperatingSystem.IsMacCatalystVersionAtLeast(17))
            granted = (await Store.RequestFullAccessToRemindersAsync()).Item1;
        else
            granted = (await Store.RequestAccessAsync(EventKit.EKEntityType.Reminder)).Item1;
        if (!granted)
            return DeviceActionOutcome.Failure("the user hasn't allowed this app to use Reminders (Settings > Privacy > Reminders)");
        EventKit.EKReminder reminder = EventKit.EKReminder.Create(Store);
        reminder.Title = request.ReminderTitle;
        reminder.Calendar = Store.DefaultCalendarForNewReminders;
        if (reminder.Calendar == null)
            return DeviceActionOutcome.Failure("there is no reminders list to add it to");
        if (request.Due != null)
        {
            DateTime due = request.Due.Value;
            var components = new Foundation.NSDateComponents { Year = due.Year, Month = due.Month, Day = due.Day };
            if (request.DueHasTime)
            {
                components.Hour = due.Hour;
                components.Minute = due.Minute;
                reminder.AddAlarm(EventKit.EKAlarm.FromDate(ToNSDate(due)));
            }
            reminder.DueDateComponents = components;
        }
        if (!Store.SaveReminder(reminder, true, out Foundation.NSError error))
            return DeviceActionOutcome.Failure(error?.LocalizedDescription ?? "Reminders didn't save it");
        return DeviceActionOutcome.Success(request.Summary + " is in the user's Reminders", "Reminder added");
    }
#endif
}
