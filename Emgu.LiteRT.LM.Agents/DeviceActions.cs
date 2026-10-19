//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// The device actions an app can offer (AgentTools.DeviceActions).
    /// </summary>
    [Flags]
    public enum DeviceActionKinds
    {
        /// <summary>None</summary>
        None = 0,
        /// <summary>set_timer: a countdown in the clock app</summary>
        Timer = 1,
        /// <summary>set_alarm: an alarm in the clock app</summary>
        Alarm = 2,
        /// <summary>add_calendar_event</summary>
        CalendarEvent = 4,
        /// <summary>add_reminder</summary>
        Reminder = 8,
        /// <summary>open_directions: the maps app with directions</summary>
        Directions = 16,
        /// <summary>All of them</summary>
        All = Timer | Alarm | CalendarEvent | Reminder | Directions
    }

    /// <summary>
    /// How to travel, for directions.
    /// </summary>
    public enum TravelMode
    {
        /// <summary>By car</summary>
        Driving,
        /// <summary>On foot</summary>
        Walking,
        /// <summary>By public transport</summary>
        Transit,
        /// <summary>By bicycle</summary>
        Cycling
    }

    /// <summary>
    /// Something a device action tool asks the app to do, already read and checked: the app shows it to the user
    /// (Title, Details, ConfirmText), does it if they confirm, and returns AgentTools.DeviceActionDone,
    /// DeviceActionCancelled or DeviceActionFailed to the model.
    /// </summary>
    public abstract class DeviceActionRequest
    {
        /// <summary>The tool's name, e.g. "set_timer"</summary>
        public abstract string ToolName { get; }

        /// <summary>What will be done, for a confirmation's heading, e.g. "Set a timer"</summary>
        public abstract string Title { get; }

        /// <summary>The confirming button's text, e.g. "Start timer"</summary>
        public abstract string ConfirmText { get; }

        /// <summary>The details to show, one per line, in the current culture</summary>
        public abstract IList<string> Details { get; }

        /// <summary>A short English description for the model, e.g. "a 10-minute timer called \"Pasta\""</summary>
        public abstract string Summary { get; }

        /// <summary>A date and time for people, in the current culture: "Monday, 19 October 2026, 15:00".</summary>
        /// <param name="value">The date and time</param>
        /// <param name="hasTime">Whether to show the time</param>
        /// <returns>The text</returns>
        protected static string ForPeople(DateTime value, bool hasTime)
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
            string date = value.ToString("D", culture);
            return hasTime ? date + ", " + value.ToString("t", culture) : date;
        }
    }

    /// <summary>set_timer: a countdown.</summary>
    public sealed class TimerRequest : DeviceActionRequest
    {
        /// <summary>How long</summary>
        public TimeSpan Duration { get; set; }
        /// <summary>The timer's name, or null</summary>
        public string Label { get; set; }

        /// <inheritdoc/>
        public override string ToolName => "set_timer";
        /// <inheritdoc/>
        public override string Title => "Set a timer";
        /// <inheritdoc/>
        public override string ConfirmText => "Start timer";
        /// <inheritdoc/>
        public override IList<string> Details
        {
            get
            {
                List<string> details = new List<string> { WhenParser.Describe(Duration) };
                if (!string.IsNullOrEmpty(Label)) details.Add(Label);
                return details;
            }
        }
        /// <inheritdoc/>
        public override string Summary =>
            "a timer for " + WhenParser.Describe(Duration) + (string.IsNullOrEmpty(Label) ? "" : " called \"" + Label + "\"");
    }

    /// <summary>set_alarm: an alarm at a time.</summary>
    public sealed class AlarmRequest : DeviceActionRequest
    {
        /// <summary>When it rings (local time)</summary>
        public DateTime Time { get; set; }
        /// <summary>The alarm's name, or null</summary>
        public string Label { get; set; }

        /// <inheritdoc/>
        public override string ToolName => "set_alarm";
        /// <inheritdoc/>
        public override string Title => "Set an alarm";
        /// <inheritdoc/>
        public override string ConfirmText => "Set alarm";
        /// <inheritdoc/>
        public override IList<string> Details
        {
            get
            {
                List<string> details = new List<string> { ForPeople(Time, true) };
                if (!string.IsNullOrEmpty(Label)) details.Add(Label);
                return details;
            }
        }
        /// <inheritdoc/>
        public override string Summary =>
            "an alarm for " + WhenParser.Describe(Time, true) + (string.IsNullOrEmpty(Label) ? "" : " called \"" + Label + "\"");
    }

    /// <summary>add_calendar_event: an event in the user's calendar.</summary>
    public sealed class CalendarEventRequest : DeviceActionRequest
    {
        /// <summary>The event's title</summary>
        public string EventTitle { get; set; }
        /// <summary>When it starts (local time; midnight for an all-day event)</summary>
        public DateTime Start { get; set; }
        /// <summary>When it ends (for an all-day event, midnight after its last day)</summary>
        public DateTime End { get; set; }
        /// <summary>Whether it lasts all day</summary>
        public bool AllDay { get; set; }
        /// <summary>Where, or null</summary>
        public string Location { get; set; }

        /// <inheritdoc/>
        public override string ToolName => "add_calendar_event";
        /// <inheritdoc/>
        public override string Title => "Add to your calendar";
        /// <inheritdoc/>
        public override string ConfirmText => "Add event";
        /// <inheritdoc/>
        public override IList<string> Details
        {
            get
            {
                List<string> details = new List<string> { EventTitle };
                if (AllDay)
                    details.Add(ForPeople(Start, false) + " (all day)");
                else
                    details.Add(ForPeople(Start, true) + " - " + End.ToString(End.Date == Start.Date ? "t" : "g", CultureInfo.CurrentCulture));
                if (!string.IsNullOrEmpty(Location)) details.Add(Location);
                return details;
            }
        }
        /// <inheritdoc/>
        public override string Summary =>
            "\"" + EventTitle + "\" on " + (AllDay ? WhenParser.Describe(Start, false) + " (all day)"
                : WhenParser.Describe(Start, true) + " for " + WhenParser.Describe(End - Start))
            + (string.IsNullOrEmpty(Location) ? "" : " at " + Location);
    }

    /// <summary>add_reminder: a reminder, optionally due at a date or time.</summary>
    public sealed class ReminderRequest : DeviceActionRequest
    {
        /// <summary>What to be reminded of</summary>
        public string ReminderTitle { get; set; }
        /// <summary>When it is due (local time), or null</summary>
        public DateTime? Due { get; set; }
        /// <summary>Whether Due has a time of day (else it is a date)</summary>
        public bool DueHasTime { get; set; }

        /// <inheritdoc/>
        public override string ToolName => "add_reminder";
        /// <inheritdoc/>
        public override string Title => "Add a reminder";
        /// <inheritdoc/>
        public override string ConfirmText => "Add reminder";
        /// <inheritdoc/>
        public override IList<string> Details
        {
            get
            {
                List<string> details = new List<string> { ReminderTitle };
                if (Due != null) details.Add(ForPeople(Due.Value, DueHasTime));
                return details;
            }
        }
        /// <inheritdoc/>
        public override string Summary =>
            "a reminder \"" + ReminderTitle + "\"" + (Due == null ? "" : " for " + WhenParser.Describe(Due.Value, DueHasTime));
    }

    /// <summary>open_directions: the maps app with directions to a place.</summary>
    public sealed class DirectionsRequest : DeviceActionRequest
    {
        /// <summary>Where to (an address or place name)</summary>
        public string Destination { get; set; }
        /// <summary>How to travel</summary>
        public TravelMode Mode { get; set; }

        /// <inheritdoc/>
        public override string ToolName => "open_directions";
        /// <inheritdoc/>
        public override string Title => "Get directions";
        /// <inheritdoc/>
        public override string ConfirmText => "Open maps";
        /// <inheritdoc/>
        public override IList<string> Details => new List<string> { Destination, ModeText };
        /// <inheritdoc/>
        public override string Summary => "directions " + ModeText.ToLowerInvariant() + " to " + Destination;

        private string ModeText =>
            Mode == TravelMode.Walking ? "On foot" : Mode == TravelMode.Transit ? "By public transport"
                : Mode == TravelMode.Cycling ? "By bike" : "By car";
    }

    public static partial class AgentTools
    {
        /// <summary>
        /// Tools that do something on the device - set_timer, set_alarm, add_calendar_event, add_reminder,
        /// open_directions - through the app, which should ask the user first: each tool reads its arguments (times in
        /// the user's words, parsed by WhenParser, since small models get exact dates wrong), then passes the request
        /// to <paramref name="perform"/>, which should show it to the user (DeviceActionRequest.Title, Details and
        /// ConfirmText), do it if they confirm, and return DeviceActionDone, DeviceActionCancelled or
        /// DeviceActionFailed. A time it can't read goes back to the model with what it expects.
        /// </summary>
        /// <param name="kinds">The actions the app can do</param>
        /// <param name="perform">Confirms and does an action, returning the result for the model</param>
        /// <param name="now">The current local time; DateTime.Now if null (for tests)</param>
        /// <returns>The tools</returns>
        public static IList<AIFunction> DeviceActions(DeviceActionKinds kinds,
            Func<DeviceActionRequest, CancellationToken, Task<string>> perform, Func<DateTime> now = null)
        {
            if (perform == null)
                throw new ArgumentNullException(nameof(perform));
            Func<DateTime> clock = now ?? (() => DateTime.Now);
            List<AIFunction> tools = new List<AIFunction>();
            if ((kinds & DeviceActionKinds.Timer) != 0)
                tools.Add(AIFunctionFactory.Create(
                    ([Description("How long, e.g. \"10 minutes\", \"1 hour 30 minutes\"")] string duration,
                     [Description("A name for the timer, or empty")] string label = null,
                     CancellationToken cancellationToken = default) =>
                    {
                        if (!WhenParser.TryParseDuration(duration, out TimeSpan length))
                            return Task.FromResult("I couldn't read the length \"" + duration + "\". Call set_timer again with a duration such as \"10 minutes\".");
                        if (length > TimeSpan.FromHours(24))
                            return Task.FromResult("A timer can be at most 24 hours long; suggest an alarm or a reminder instead.");
                        return perform(new TimerRequest { Duration = length, Label = Clean(label) }, cancellationToken);
                    },
                    "set_timer",
                    "Starts a countdown timer in the device's clock app. Call it whenever the user asks for a timer: you can't start one any other way. The user confirms it on a card first."));
            if ((kinds & DeviceActionKinds.Alarm) != 0)
                tools.Add(AIFunctionFactory.Create(
                    ([Description("When, in the user's words, e.g. \"7:30 am\", \"tomorrow at 6\"")] string time,
                     [Description("A name for the alarm, or empty")] string label = null,
                     CancellationToken cancellationToken = default) =>
                    {
                        DateTime current = clock();
                        if (!WhenParser.TryParseDateTime(time, current, out DateTime at, out bool hasTime) || !hasTime)
                            return Task.FromResult("I couldn't read the time \"" + time + "\". Call set_alarm again with a time such as \"7:30 am\" or \"tomorrow at 6 am\".");
                        if (at <= current)
                            return Task.FromResult("That time has passed (it is now " + WhenParser.Describe(current, true) + "). Ask the user when the alarm should ring.");
                        return perform(new AlarmRequest { Time = at, Label = Clean(label) }, cancellationToken);
                    },
                    "set_alarm",
                    "Sets an alarm in the device's clock app. Call it whenever the user asks to be woken up or for an alarm: you can't set one any other way. The user confirms it on a card first."));
            if ((kinds & DeviceActionKinds.CalendarEvent) != 0)
                tools.Add(AIFunctionFactory.Create(
                    ([Description("The event's title")] string title,
                     [Description("When it starts, in the user's words, e.g. \"Friday at 3pm\", \"October 21\" (all day)")] string when,
                     [Description("How long, e.g. \"1 hour\", or empty")] string duration = null,
                     [Description("Where, or empty")] string location = null,
                     CancellationToken cancellationToken = default) =>
                    {
                        if (string.IsNullOrWhiteSpace(title))
                            return Task.FromResult("The event needs a title. Call add_calendar_event again with one.");
                        if (!WhenParser.TryParseDateTime(when, clock(), out DateTime start, out bool hasTime))
                            return Task.FromResult("I couldn't read the date \"" + when + "\". Call add_calendar_event again with a date and time such as \"Friday at 3pm\" or \"2026-10-21 15:00\".");
                        TimeSpan length = hasTime ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);
                        if (!string.IsNullOrWhiteSpace(duration) && !WhenParser.TryParseDuration(duration, out length))
                            return Task.FromResult("I couldn't read the length \"" + duration + "\". Call add_calendar_event again with a duration such as \"1 hour\", or none.");
                        if (!hasTime)
                            length = TimeSpan.FromDays(Math.Max(1, Math.Round(length.TotalDays)));
                        return perform(new CalendarEventRequest
                        {
                            EventTitle = title.Trim(),
                            Start = start,
                            End = start + length,
                            AllDay = !hasTime,
                            Location = Clean(location)
                        }, cancellationToken);
                    },
                    "add_calendar_event",
                    "Adds an event to the user's calendar. Call it whenever the user asks to put something in their calendar or schedule it: you can't add one any other way. The user confirms it on a card first."));
            if ((kinds & DeviceActionKinds.Reminder) != 0)
                tools.Add(AIFunctionFactory.Create(
                    ([Description("What to remind the user of")] string title,
                     [Description("When, in the user's words, e.g. \"tomorrow at 9am\", or empty")] string when = null,
                     CancellationToken cancellationToken = default) =>
                    {
                        if (string.IsNullOrWhiteSpace(title))
                            return Task.FromResult("The reminder needs a text. Call add_reminder again with one.");
                        DateTime? due = null;
                        bool hasTime = false;
                        if (!string.IsNullOrWhiteSpace(when))
                        {
                            if (!WhenParser.TryParseDateTime(when, clock(), out DateTime at, out hasTime))
                                return Task.FromResult("I couldn't read the time \"" + when + "\". Call add_reminder again with a time such as \"tomorrow at 9am\", or none.");
                            due = at;
                        }
                        return perform(new ReminderRequest { ReminderTitle = title.Trim(), Due = due, DueHasTime = hasTime }, cancellationToken);
                    },
                    "add_reminder",
                    "Adds a reminder to the user's reminders list. Call it whenever the user asks to be reminded of something: you can't add one any other way. The user confirms it on a card first."));
            if ((kinds & DeviceActionKinds.Directions) != 0)
                tools.Add(AIFunctionFactory.Create(
                    ([Description("Where to: an address or a place name")] string destination,
                     [Description("driving, walking, transit or cycling")] string mode = null,
                     CancellationToken cancellationToken = default) =>
                    {
                        if (string.IsNullOrWhiteSpace(destination))
                            return Task.FromResult("Directions need a destination. Call open_directions again with one.");
                        return perform(new DirectionsRequest { Destination = destination.Trim(), Mode = ParseMode(mode) }, cancellationToken);
                    },
                    "open_directions",
                    "Opens the maps app with directions to a place, from where the user is. Call it whenever the user asks how to get somewhere or for directions: you can't open maps any other way. The user confirms it on a card first."));
            return tools;
        }

        /// <summary>
        /// The result of a device action the user confirmed and the app did.
        /// </summary>
        /// <param name="whatHappened">What was done, e.g. "a timer for 10 minutes is running in the clock app"</param>
        /// <returns>The tool result for the model</returns>
        public static string DeviceActionDone(string whatHappened)
        {
            return "Done: " + whatHappened + ". Tell the user in one short sentence.";
        }

        /// <summary>
        /// Instructions for the agent that offers the device action tools: without them, models answered "I've set an
        /// alarm" without calling set_alarm (Gemma 4 26B-A4B with thinking off, about half the time).
        /// </summary>
        /// <param name="kinds">The actions offered</param>
        /// <returns>The instructions, or null if none are offered</returns>
        public static string DeviceActionInstructions(DeviceActionKinds kinds)
        {
            List<string> tools = new List<string>();
            List<string> things = new List<string>();
            if ((kinds & DeviceActionKinds.Timer) != 0) { tools.Add("set_timer"); things.Add("a timer"); }
            if ((kinds & DeviceActionKinds.Alarm) != 0) { tools.Add("set_alarm"); things.Add("an alarm"); }
            if ((kinds & DeviceActionKinds.CalendarEvent) != 0) { tools.Add("add_calendar_event"); things.Add("a calendar event"); }
            if ((kinds & DeviceActionKinds.Reminder) != 0) { tools.Add("add_reminder"); things.Add("a reminder"); }
            if ((kinds & DeviceActionKinds.Directions) != 0) { tools.Add("open_directions"); things.Add("directions"); }
            if (tools.Count == 0)
                return null;
            string text = "You can act on this device with " + string.Join(", ", tools) + ". When the user asks for "
                + string.Join(", ", things) + ", call the tool right away: you can't do these things any other way, so never "
                + "say one is done, set or added unless the tool said so. The user confirms each on a card before anything happens.";
            if ((kinds & DeviceActionKinds.Directions) != 0)
                text += " For directions, the maps app knows where the user is: just pass the destination.";
            return text;
        }

        /// <summary>
        /// The result of a device action the user cancelled. Blunt on purpose: given "The user cancelled it: nothing was
        /// done. Say so...", Gemma 4 E2B answered "I have set an alarm for 7:00 am tomorrow" (and the same for a timer and
        /// a calendar event); with this text it said they were cancelled.
        /// </summary>
        public const string DeviceActionCancelled =
            "CANCELLED - NOT done. The user tapped Cancel, so nothing was set or added. Tell the user, in one short sentence, " +
            "that it was cancelled. Don't say it was done, and don't try again unless the user asks.";

        /// <summary>
        /// The result of a device action that couldn't be done.
        /// </summary>
        /// <param name="reason">Why, e.g. "there is no clock app"</param>
        /// <returns>The tool result for the model</returns>
        public static string DeviceActionFailed(string reason)
        {
            return "It couldn't be done: " + reason + ". Tell the user.";
        }

        private static string Clean(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static TravelMode ParseMode(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            if (m.StartsWith("walk", StringComparison.Ordinal) || m == "on foot" || m == "foot") return TravelMode.Walking;
            if (m.StartsWith("transit", StringComparison.Ordinal) || m.Contains("public") || m == "bus" || m == "train"
                || m == "subway" || m == "metro") return TravelMode.Transit;
            if (m.StartsWith("cycl", StringComparison.Ordinal) || m.StartsWith("bik", StringComparison.Ordinal)
                || m == "bicycle" || m == "bicycling") return TravelMode.Cycling;
            return TravelMode.Driving;
        }
    }
}
