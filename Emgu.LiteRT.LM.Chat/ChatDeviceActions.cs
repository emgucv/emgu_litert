//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// The device actions tool group of a chat (timers, alarms, calendar events, reminders, directions; the tools are
    /// Emgu.LiteRT.LM.Agents' <c>AgentTools.DeviceActions</c>): its name, what it does for the stub tool, and the words
    /// of a message that need it. A keyword also matches longer words it starts ("remind" - "reminder").
    /// </summary>
    public static class ChatDeviceActions
    {
        /// <summary>The group's name.</summary>
        public const string GroupName = "clock_calendar_maps";

        /// <summary>What the group does, for the stub tool.</summary>
        public const string GroupSummary = "set a timer or an alarm, add a calendar event or a reminder, or get directions";

        /// <summary>The words of a message that need the group.</summary>
        public static readonly string[] GroupKeywords =
        {
            "timer", "alarm", "wake me", "wake up", "remind", "calendar", "schedule", "appointment", "meeting", "event",
            "book a", "put it in", "put lunch", "put dinner", "direction", "how do i get", "how to get", "get to",
            "walk to", "drive to", "take me to", "navigate", "route", "map", "minutes for", "countdown"
        };
    }
}
