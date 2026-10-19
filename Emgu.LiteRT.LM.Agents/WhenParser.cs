//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Reads durations ("10 minutes", "1h30m", "half an hour") and dates and times ("tomorrow at 3pm", "Friday",
    /// "October 21 at 9:30", "in 2 hours", "2026-10-21T15:00") written in English, relative to a given time. The device
    /// action tools take times in the user's words and parse them here, rather than asking the model for exact dates,
    /// which small models get wrong; the confirmation the app shows then says what was understood.
    /// </summary>
    public static class WhenParser
    {
        private static readonly Dictionary<string, double> NumberWords = new Dictionary<string, double>
        {
            { "a", 1 }, { "an", 1 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 },
            { "six", 6 }, { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 },
            { "twelve", 12 }, { "fifteen", 15 }, { "twenty", 20 }, { "thirty", 30 }, { "forty", 40 },
            { "fortyfive", 45 }, { "fifty", 50 }, { "sixty", 60 }, { "ninety", 90 }, { "couple", 2 }, { "few", 3 }
        };

        private static readonly string[] MonthNames =
        {
            "january", "february", "march", "april", "may", "june", "july", "august", "september", "october",
            "november", "december"
        };

        private static readonly string[] DayNames =
        {
            "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"
        };

        // Words that carry no meaning here.
        private static readonly HashSet<string> Filler = new HashSet<string>
        {
            "at", "on", "the", "of", "for", "by", "around", "about", "and", "from", "now", "o'clock", "oclock", "starting",
            "please", "approximately", "roughly", "just", "exactly", "sharp", "in"
        };

        /// <summary>
        /// Reads a duration such as "10 minutes", "1 hour 30 minutes", "1h30m", "90 s", "1.5 hours", "half an hour",
        /// "5:00" (minutes:seconds), "1:30:00" or "PT10M". A number alone is minutes.
        /// </summary>
        /// <param name="text">The duration</param>
        /// <param name="duration">The duration read, if successful</param>
        /// <returns>True if the whole text was read as a positive duration</returns>
        public static bool TryParseDuration(string text, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string s = text.Trim().ToLowerInvariant();

            Match iso = Regex.Match(s, @"^pt?(?:(\d+(?:\.\d+)?)h)?(?:(\d+(?:\.\d+)?)m)?(?:(\d+(?:\.\d+)?)s)?$");
            if (iso.Success && s.Length > 2)
            {
                duration = TimeSpan.FromHours(Number(iso.Groups[1].Value)) + TimeSpan.FromMinutes(Number(iso.Groups[2].Value))
                    + TimeSpan.FromSeconds(Number(iso.Groups[3].Value));
                return duration > TimeSpan.Zero;
            }
            Match clock = Regex.Match(s, @"^(\d+):(\d{1,2})(?::(\d{1,2}))?$");
            if (clock.Success)
            {
                duration = clock.Groups[3].Success
                    ? new TimeSpan(int.Parse(clock.Groups[1].Value), int.Parse(clock.Groups[2].Value), int.Parse(clock.Groups[3].Value))
                    : new TimeSpan(0, int.Parse(clock.Groups[1].Value), int.Parse(clock.Groups[2].Value));
                return duration > TimeSpan.Zero;
            }

            s = Regex.Replace(s, @"\b(?:an hour and a half|one and a half hours?)\b", "90 minutes");
            s = Regex.Replace(s, @"\ba minute and a half\b", "90 seconds");
            s = Regex.Replace(s, @"\b(?:a )?half (?:an )?hour\b", "30 minutes");
            s = Regex.Replace(s, @"\b(?:a )?quarter (?:of )?(?:an )?hour\b", "15 minutes");
            // "two and a half hours"
            s = Regex.Replace(s, @"(\S+) and a half (hours?|minutes?)", m =>
                TryNumber(m.Groups[1].Value, out double whole) ? (whole + 0.5).ToString(CultureInfo.InvariantCulture) + " " + m.Groups[2].Value : m.Value);
            List<string> words = Words(s);
            if (words.Count == 1 && double.TryParse(words[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double bare))
            {
                duration = TimeSpan.FromMinutes(bare);
                return bare > 0;
            }
            double? pending = null;
            TimeSpan? lastUnit = null;
            bool any = false;
            for (int i = 0; i < words.Count; i++)
            {
                string word = words[i];
                if (TryNumber(word, out double number))
                {
                    // "a 3 minute timer"
                    if ((word == "a" || word == "an") && i + 1 < words.Count && TryNumber(words[i + 1], out _))
                        continue;
                    if (pending != null)
                        return false;
                    pending = number;
                    continue;
                }
                TimeSpan? unit = DurationUnit(word);
                if (unit != null)
                {
                    if (pending == null)
                        return false;
                    duration += TimeSpan.FromTicks((long)(unit.Value.Ticks * pending.Value));
                    pending = null;
                    lastUnit = unit;
                    any = true;
                    continue;
                }
                // "2 hours and a half"
                if (word == "half" && lastUnit != null && pending == 1)
                {
                    duration += TimeSpan.FromTicks(lastUnit.Value.Ticks / 2);
                    pending = null;
                    continue;
                }
                if (Filler.Contains(word) || word == "timer" || word == "countdown")
                    continue;
                return false;
            }
            return any && pending == null && duration > TimeSpan.Zero;
        }

        /// <summary>
        /// Reads a date and/or time such as "tomorrow at 3pm", "today 15:30", "tonight", "Friday morning", "next
        /// Monday at 9", "October 21", "21 Oct 2027 at noon", "in 2 hours", "in 3 days", "7:30" or "2026-10-21T15:00".
        /// A time without a date is the next time it comes (today, or tomorrow if it has passed); an hour without am/pm
        /// likewise the next one (at 14:00, "3" is 15:00), or, on another day, the afternoon for 1-6 and the morning for
        /// 7-12. A date without a year is the next one; a weekday is the next one ("this Friday" can be today).
        /// </summary>
        /// <param name="text">The date and/or time</param>
        /// <param name="now">The current local time</param>
        /// <param name="value">The local date and time read, if successful (midnight when there was no time)</param>
        /// <param name="hasTime">Whether a time of day was given</param>
        /// <returns>True if the text was read; false if it had words that aren't a date or time</returns>
        public static bool TryParseDateTime(string text, DateTime now, out DateTime value, out bool hasTime)
        {
            value = DateTime.MinValue;
            hasTime = false;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            string trimmed = text.Trim();
            string[] isoFormats = { "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" };
            if (DateTime.TryParseExact(trimmed, isoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                hasTime = true;
                return true;
            }
            if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
                return true;
            if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}-\d{2}T") &&
                DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset offset))
            {
                value = offset.LocalDateTime;
                hasTime = true;
                return true;
            }

            string s = " " + trimmed.ToLowerInvariant().Replace("a.m.", "am").Replace("p.m.", "pm").Replace(",", " ") + " ";
            s = Regex.Replace(s, @"(\d)(st|nd|rd|th)\b", "$1");
            s = Regex.Replace(s, @"\s+", " ");

            // "in 2 hours", "in 1 hour 30 minutes", "in 3 days": from now.
            Match relative = Regex.Match(s, @"^ (?:in|after) (.+?) (?:from now )?$");
            if (relative.Success)
            {
                string rest = relative.Groups[1].Value;
                Match days = Regex.Match(rest, @"^(\S+) (days?|weeks?|months?)$");
                if (days.Success && TryNumber(days.Groups[1].Value, out double count))
                {
                    value = days.Groups[2].Value.StartsWith("month", StringComparison.Ordinal)
                        ? now.Date.AddMonths((int)count)
                        : now.Date.AddDays(count * (days.Groups[2].Value.StartsWith("week", StringComparison.Ordinal) ? 7 : 1));
                    return true;
                }
                if (TryParseDuration(rest, out TimeSpan fromNow))
                {
                    value = now + fromNow;
                    value = value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
                    hasTime = true;
                    return true;
                }
                // Not a length of time ("in the morning"): read below.
            }

            // Mark dates and times, then read the words in order.
            string months = string.Join("|", MonthNames.Select(m => m.Substring(0, 3) + "[a-z]*"));
            s = Regex.Replace(s, @" (\d{1,2})(?: of)? (" + months + @")(?: (\d{4}))?(?= )",
                m => " @d" + (MonthIndex(m.Groups[2].Value) + 1) + "-" + m.Groups[1].Value + "-" + (m.Groups[3].Success ? m.Groups[3].Value : "0"));
            s = Regex.Replace(s, @" (" + months + @") (\d{1,2})(?: (\d{4}))?(?= )(?! ?(?::|am|pm))",
                m => " @d" + (MonthIndex(m.Groups[1].Value) + 1) + "-" + m.Groups[2].Value + "-" + (m.Groups[3].Success ? m.Groups[3].Value : "0"));
            s = Regex.Replace(s, @" (\d{1,2})(?::(\d{2}))? ?(am|pm)(?= )",
                m => " @t" + m.Groups[1].Value + "-" + (m.Groups[2].Success ? m.Groups[2].Value : "0") + "-" + m.Groups[3].Value);
            s = Regex.Replace(s, @" (\d{1,2}):(\d{2})(?= )", m => " @t" + m.Groups[1].Value + "-" + m.Groups[2].Value + "-24");
            s = s.Replace(" day after tomorrow ", " dayaftertomorrow ").Replace(" next week ", " nextweek ");

            DateTime? date = null;
            int hour = -1, minute = 0;
            string clock = null; // "am", "pm", "24" (written with minutes), or null (an hour alone)
            string period = null;
            bool thisDay = false;
            foreach (string word in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                DateTime? wordDate = null;
                if (word.StartsWith("@d", StringComparison.Ordinal))
                {
                    string[] parts = word.Substring(2).Split('-');
                    int month = int.Parse(parts[0]), day = int.Parse(parts[1]), year = int.Parse(parts[2]);
                    int targetYear = year == 0 ? now.Year : year;
                    if (day < 1 || day > DateTime.DaysInMonth(targetYear, month))
                        return false;
                    DateTime candidate = new DateTime(targetYear, month, day);
                    wordDate = year == 0 && candidate < now.Date ? candidate.AddYears(1) : candidate;
                }
                else if (word.StartsWith("@t", StringComparison.Ordinal))
                {
                    if (hour >= 0)
                        return false;
                    string[] parts = word.Substring(2).Split('-');
                    hour = int.Parse(parts[0]);
                    minute = int.Parse(parts[1]);
                    clock = parts[2];
                    if (minute > 59 || clock == "24" && hour > 23 || clock != "24" && (hour < 1 || hour > 12))
                        return false;
                    continue;
                }
                else if (word == "today") wordDate = now.Date;
                else if (word == "tonight") { wordDate = now.Date; period = "night"; }
                else if (word == "tomorrow") wordDate = now.Date.AddDays(1);
                else if (word == "dayaftertomorrow") wordDate = now.Date.AddDays(2);
                else if (word == "nextweek") wordDate = now.Date.AddDays(7);
                else if (word == "weekend") wordDate = NextWeekday(now.Date, DayOfWeek.Saturday, true);
                else if (DayIndex(word) >= 0) wordDate = NextWeekday(now.Date, (DayOfWeek)DayIndex(word), thisDay);
                else if (word == "this") { thisDay = true; continue; }
                else if (word == "next") continue;
                else if (word == "noon" || word == "midday")
                {
                    if (hour >= 0) return false;
                    hour = 12; minute = 0; clock = "24";
                    continue;
                }
                else if (word == "midnight")
                {
                    if (hour >= 0) return false;
                    hour = 0; minute = 0; clock = "24";
                    if (date == null) date = now.Date.AddDays(1);
                    continue;
                }
                else if (word == "morning" || word == "afternoon" || word == "evening" || word == "night")
                {
                    period = word;
                    continue;
                }
                else if (Regex.IsMatch(word, @"^\d{1,2}h?$"))
                {
                    // An hour alone ("at 3", "15h").
                    if (hour >= 0) return false;
                    hour = int.Parse(word.TrimEnd('h'));
                    minute = 0;
                    clock = hour > 12 || hour == 0 || word.EndsWith("h", StringComparison.Ordinal) ? "24" : null;
                    if (hour > 23) return false;
                    continue;
                }
                else if (Filler.Contains(word))
                    continue;
                else
                    return false;
                if (date != null)
                    return false;
                date = wordDate;
            }

            if (date == null && hour < 0 && period == null)
                return false;
            if (hour < 0 && period != null)
            {
                // "tomorrow morning": a typical time for that part of the day.
                hour = period == "morning" ? 9 : period == "afternoon" ? 15 : period == "evening" ? 19 : 20;
                clock = "24";
            }
            if (hour < 0)
            {
                value = date.Value;
                return true;
            }

            if (clock == "am")
                hour %= 12;
            else if (clock == "pm")
                hour = hour % 12 + 12;
            else if (clock == null)
            {
                // An hour without am/pm.
                if (period == "afternoon" || period == "evening" || period == "night")
                    hour = hour % 12 + 12;
                else if (period == "morning")
                    hour %= 12;
                else if (date == null || date.Value == now.Date)
                {
                    DateTime morning = now.Date.AddHours(hour % 12).AddMinutes(minute);
                    if (morning > now) hour %= 12;
                    else if (morning.AddHours(12) > now) hour = hour % 12 + 12;
                    else hour %= 12; // tomorrow morning (no date) - or earlier today, with "today"
                }
                else
                    hour = hour % 12 >= 1 && hour % 12 <= 6 ? hour % 12 + 12 : hour == 12 ? 12 : hour % 12;
            }
            value = (date ?? now.Date).AddHours(hour).AddMinutes(minute);
            if (date == null && value <= now)
                value = value.AddDays(1);
            hasTime = true;
            return true;
        }

        /// <summary>
        /// A date (and time) in English for the model, e.g. "Monday 19 October 2026 at 15:00".
        /// </summary>
        /// <param name="value">The date and time</param>
        /// <param name="hasTime">Whether to include the time</param>
        /// <returns>The text</returns>
        public static string Describe(DateTime value, bool hasTime)
        {
            string date = value.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture);
            return hasTime ? date + " at " + value.ToString("HH:mm", CultureInfo.InvariantCulture) : date;
        }

        /// <summary>
        /// A duration in English, e.g. "1 hour 30 minutes".
        /// </summary>
        /// <param name="duration">The duration</param>
        /// <returns>The text</returns>
        public static string Describe(TimeSpan duration)
        {
            List<string> parts = new List<string>();
            int hours = (int)duration.TotalHours;
            if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
            if (duration.Minutes > 0) parts.Add(duration.Minutes + (duration.Minutes == 1 ? " minute" : " minutes"));
            if (duration.Seconds > 0) parts.Add(duration.Seconds + (duration.Seconds == 1 ? " second" : " seconds"));
            return parts.Count == 0 ? "0 seconds" : string.Join(" ", parts);
        }

        private static DateTime NextWeekday(DateTime today, DayOfWeek day, bool includeToday)
        {
            int days = ((int)day - (int)today.DayOfWeek + 7) % 7;
            if (days == 0 && !includeToday)
                days = 7;
            return today.AddDays(days);
        }

        private static int DayIndex(string word)
        {
            for (int i = 0; i < DayNames.Length; i++)
                if (word == DayNames[i] || word.Length >= 3 && DayNames[i].StartsWith(word, StringComparison.Ordinal)
                    && (word.Length == 3 || word == "tues" || word == "thur" || word == "thurs"))
                    return i;
            return -1;
        }

        private static int MonthIndex(string word)
        {
            for (int i = 0; i < MonthNames.Length; i++)
                if (word == MonthNames[i] || word.Length >= 3 && MonthNames[i].StartsWith(word, StringComparison.Ordinal)
                    && (word.Length == 3 || word == "sept"))
                    return i;
            return -1;
        }

        private static TimeSpan? DurationUnit(string word)
        {
            switch (word)
            {
                case "h": case "hr": case "hrs": case "hour": case "hours": return TimeSpan.FromHours(1);
                case "m": case "min": case "mins": case "minute": case "minutes": return TimeSpan.FromMinutes(1);
                case "s": case "sec": case "secs": case "second": case "seconds": return TimeSpan.FromSeconds(1);
                default: return null;
            }
        }

        private static bool TryNumber(string word, out double number)
        {
            if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number >= 0;
            return NumberWords.TryGetValue(word, out number);
        }

        private static double Number(string text)
        {
            return text.Length == 0 ? 0 : double.Parse(text, CultureInfo.InvariantCulture);
        }

        // Lower-case words; numbers and letters written together ("1h30m") are split, "-" and "." between words too.
        private static List<string> Words(string s)
        {
            s = Regex.Replace(s, @"(\d)([a-z])", "$1 $2");
            s = Regex.Replace(s, @"([a-z])(\d)", "$1 $2");
            List<string> words = new List<string>();
            foreach (string word in Regex.Split(s.Replace("forty-five", "fortyfive").Replace("forty five", "fortyfive"), @"[\s\-/]+"))
            {
                string w = word.Trim('.', '!', '?', ';', '"', '\'');
                if (w.Length > 0)
                    words.Add(w);
            }
            return words;
        }
    }
}
