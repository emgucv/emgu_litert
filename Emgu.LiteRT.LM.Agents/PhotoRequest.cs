//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Turns a photo request in the user's words ("beach photos from Spain last summer", "selfies", "photos taken with my
    /// Pixel in 2024") into a PhotoQuery, with fixed rules: dates relative to now, kinds by keyword, places and cameras by
    /// the ones the app knows are in its photos (and a capitalised name after "in", "from" or "at"), and the rest as the
    /// description of what is in the photos. The find_photos tool takes the request as one text because Gemma 4 E2B filled
    /// separate fields unreliably (a beach as the place, an invented camera) but passed the request on faithfully.
    /// </summary>
    public static class PhotoRequest
    {
        private static readonly String[] Months =
            { "january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december" };
        private static readonly String[] ShortMonths =
            { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

        // Words that say "photo" or ask for one, and connecting words: dropped from the description.
        private static readonly HashSet<String> Filler = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "photo", "photos", "picture", "pictures", "pic", "pics", "image", "images", "shot", "shots", "snap", "snaps",
            "show", "find", "search", "look", "looking", "get", "give", "see", "display", "me", "my", "mine", "i", "we", "our",
            "us", "you", "the", "a", "an", "some", "any", "all", "of", "from", "in", "at", "on", "during", "for", "with",
            "taken", "took", "take", "made", "by", "that", "which", "where", "when", "have", "has", "had", "do", "did", "there",
            "is", "are", "was", "were", "please", "can", "could", "would", "to", "and", "or", "near", "around", "while"
        };

        // Travel words: dropped when a place is found ("my trip to Japan").
        private static readonly HashSet<String> TravelWords = new HashSet<String>(StringComparer.OrdinalIgnoreCase)
        {
            "trip", "trips", "vacation", "vacations", "holiday", "holidays", "visit", "visiting", "travel", "travels", "journey"
        };

        private static readonly (Regex Pattern, String Kind)[] Kinds =
        {
            (new Regex(@"\b(selfies?|front camera)\b", RegexOptions.IgnoreCase), "selfie"),
            (new Regex(@"\b(screen ?shots?|screen captures?|screen grabs?)\b", RegexOptions.IgnoreCase), "screenshot"),
            (new Regex(@"\b(panoramas?|panoramic|panos?)\b", RegexOptions.IgnoreCase), "panorama"),
            (new Regex(@"\b(favou?rites?|favou?rited|starred)\b", RegexOptions.IgnoreCase), "favorite"),
            (new Regex(@"\bportrait mode\b", RegexOptions.IgnoreCase), "portrait")
        };

        // Camera and phone brands, recognised even if no indexed photo names them.
        private static readonly String[] CameraBrands =
        {
            "iphone", "ipad", "pixel", "galaxy", "samsung", "huawei", "xiaomi", "oneplus", "oppo", "vivo", "motorola", "sony",
            "xperia", "canon", "nikon", "fujifilm", "fuji", "olympus", "panasonic", "lumix", "leica", "gopro", "dji", "ricoh", "pentax"
        };

        /// <summary>
        /// Parse a request.
        /// </summary>
        /// <param name="request">The request, e.g. "beach photos from Spain last summer"</param>
        /// <param name="now">Today, for relative dates</param>
        /// <param name="places">The places of the app's photos ("Town, Region, Country"), for recognising place names; or null</param>
        /// <param name="cameras">The cameras of the app's photos ("Google Pixel 8"), for recognising them; or null</param>
        public static PhotoQuery Parse(String request, DateTime now, IEnumerable<String> places = null, IEnumerable<String> cameras = null)
        {
            PhotoQuery query = new PhotoQuery();
            String text = " " + Regex.Replace(request ?? String.Empty, @"\s+", " ").Trim() + " ";

            // Kinds.
            foreach (var kind in Kinds)
            {
                if (query.Kind == null && kind.Pattern.IsMatch(text))
                {
                    query.Kind = kind.Kind;
                    text = kind.Pattern.Replace(text, " ");
                }
            }

            // Dates.
            text = ParseDates(text, now, query);

            // Cameras: one of the app's, by its name or a word of it ("Pixel", "Pixel 8"), or a known brand.
            List<String> cameraNames = new List<String>();
            foreach (String camera in cameras ?? Enumerable.Empty<String>())
            {
                if (String.IsNullOrWhiteSpace(camera))
                    continue;
                cameraNames.Add(camera);
                String[] words = camera.Split(' ');
                for (int i = 0; i < words.Length; i++)
                    for (int j = i; j < words.Length; j++)
                        cameraNames.Add(String.Join(" ", words, i, j - i + 1));
            }
            cameraNames.AddRange(CameraBrands);
            String cameraMatch = LongestPhrase(text, cameraNames.Where(c => c.Length >= 3 && !Regex.IsMatch(c, @"^\d+$")));
            if (cameraMatch != null)
            {
                query.Camera = cameraMatch;
                text = RemovePhrase(text, cameraMatch);
            }

            // Places: one of the app's (town, region or country), or a capitalised name after "in", "from" or "at".
            List<String> placeNames = new List<String>(PlaceAliases.Keys);
            foreach (String place in places ?? Enumerable.Empty<String>())
                if (!String.IsNullOrWhiteSpace(place))
                    placeNames.AddRange(place.Split(',').Select(p => p.Trim()).Where(p => p.Length > 1));
            String placeMatch = LongestPhrase(text, placeNames);
            if (placeMatch == null)
            {
                Match named = Regex.Match(text, @"\b(?:in|from|at|to|around|near)\s+((?:\p{Lu}[\p{L}'\-]*)(?:\s+\p{Lu}[\p{L}'\-]*)*)");
                if (named.Success)
                    placeMatch = named.Groups[1].Value;
            }
            if (placeMatch != null)
            {
                query.Place = PlaceAliases.TryGetValue(placeMatch, out String alias) ? alias : placeMatch;
                text = RemovePhrase(text, placeMatch);
                foreach (String travel in TravelWords)
                    text = Regex.Replace(text, @"\b" + travel + @"\b", " ", RegexOptions.IgnoreCase);
            }

            // The rest describes what is in the photos.
            List<String> words2 = Regex.Matches(text, @"[\p{L}\p{N}'\-]+").Cast<Match>().Select(m => m.Value)
                .Where(w => !Filler.Contains(w)).ToList();
            query.Description = words2.Count > 0 ? String.Join(" ", words2) : null;
            return query;
        }

        // Common ways of naming countries that the place list spells otherwise.
        private static readonly Dictionary<String, String> PlaceAliases = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase)
        {
            { "USA", "United States" }, { "U.S.", "United States" }, { "U.S.A.", "United States" }, { "the US", "United States" },
            { "the States", "United States" }, { "America", "United States" }, { "UK", "United Kingdom" }, { "U.K.", "United Kingdom" },
            { "Britain", "United Kingdom" }, { "Great Britain", "United Kingdom" }, { "Holland", "Netherlands" },
            { "Czech Republic", "Czechia" }, { "UAE", "United Arab Emirates" }
        };

        // The longest of the phrases that occurs in the text as whole words (ignoring case and accents), as written in the
        // phrase list; or null.
        private static String LongestPhrase(String text, IEnumerable<String> phrases)
        {
            String folded = " " + Fold(text) + " ";
            String best = null;
            foreach (String phrase in phrases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                String f = Fold(phrase);
                if (f.Length == 0 || (best != null && f.Length <= Fold(best).Length))
                    continue;
                if (folded.Contains(" " + f + " "))
                    best = phrase;
            }
            return best;
        }

        private static String RemovePhrase(String text, String phrase)
        {
            // The phrase as words, matching accented or unaccented spelling in the text.
            String[] words = Fold(phrase).Split(' ');
            List<Match> tokens = Regex.Matches(text, @"[\p{L}\p{N}'\-\.]+").Cast<Match>().ToList();
            for (int i = 0; i + words.Length <= tokens.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < words.Length && match; j++)
                    match = Fold(tokens[i + j].Value) == words[j];
                if (match)
                {
                    int start = tokens[i].Index;
                    int end = tokens[i + words.Length - 1].Index + tokens[i + words.Length - 1].Length;
                    return text.Substring(0, start) + " " + text.Substring(end);
                }
            }
            return text;
        }

        // Lower case, accents removed, anything but letters and digits a single space.
        private static String Fold(String text)
        {
            StringBuilder sb = new StringBuilder();
            bool space = false;
            foreach (char c in (text ?? String.Empty).Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (Char.IsLetterOrDigit(c))
                {
                    if (space && sb.Length > 0)
                        sb.Append(' ');
                    space = false;
                    sb.Append(Char.ToLowerInvariant(c));
                }
                else
                {
                    space = true;
                }
            }
            return sb.ToString();
        }

        // ---------- Dates ----------

        /// <summary>Extract date bounds, returning the remaining text. Bounds are inclusive, in local time.</summary>
        /// <param name="text">The request text.</param>
        /// <param name="now">The local date used for relative dates.</param>
        /// <param name="query">Receives the date bounds.</param>
        public static String ParseDates(String text, DateTime now, PhotoQuery query)
        {
            DateTime today = now.Date;
            Match m;

            // "in the last 3 weeks", "past 2 months", "last 10 days"
            m = Regex.Match(text, @"\b(?:in the |within the |over the )?(?:last|past) (\d+) (day|week|month|year)s?\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int n = Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                query.From = Subtract(today, n, m.Groups[2].Value);
                query.To = today.AddDays(1).AddTicks(-1);
                return Cut(text, m);
            }
            // "3 weeks ago", "a month ago", "2 years ago"
            m = Regex.Match(text, @"\b(\d+|a|an|one|two|three) (day|week|month|year)s? ago\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int n = Number(m.Groups[1].Value);
                String unit = m.Groups[2].Value.ToLowerInvariant();
                DateTime at = Subtract(today, n, unit);
                // That day, week, month or year, with a little slack.
                (query.From, query.To) = unit == "day" ? (at, at.AddDays(1).AddTicks(-1))
                    : unit == "week" ? (at.AddDays(-3), at.AddDays(4).AddTicks(-1))
                    : unit == "month" ? (new DateTime(at.Year, at.Month, 1), new DateTime(at.Year, at.Month, 1).AddMonths(1).AddTicks(-1))
                    : (new DateTime(at.Year, 1, 1), new DateTime(at.Year + 1, 1, 1).AddTicks(-1));
                return Cut(text, m);
            }
            // "today", "yesterday", "this/last week|weekend|month|year"
            m = Regex.Match(text, @"\b(today|tonight|this morning|yesterday|(this|last) (week|weekend|month|year))\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                String w = m.Groups[1].Value.ToLowerInvariant();
                DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                DateTime saturday = monday.AddDays(5);
                if (w == "today" || w == "tonight" || w == "this morning") (query.From, query.To) = (today, today.AddDays(1).AddTicks(-1));
                else if (w == "yesterday") (query.From, query.To) = (today.AddDays(-1), today.AddTicks(-1));
                else if (w == "this week") (query.From, query.To) = (monday, today.AddDays(1).AddTicks(-1));
                else if (w == "last week") (query.From, query.To) = (monday.AddDays(-7), monday.AddTicks(-1));
                else if (w == "this weekend") (query.From, query.To) = (saturday, saturday.AddDays(2).AddTicks(-1));
                else if (w == "last weekend")
                {
                    // The weekend before this one on a weekend, else the most recent Saturday and Sunday.
                    DateTime lastSaturday = today >= saturday ? saturday.AddDays(-7) : monday.AddDays(-2);
                    (query.From, query.To) = (lastSaturday, lastSaturday.AddDays(2).AddTicks(-1));
                }
                else if (w == "this month") (query.From, query.To) = (new DateTime(today.Year, today.Month, 1), today.AddDays(1).AddTicks(-1));
                else if (w == "last month") (query.From, query.To) = (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddTicks(-1));
                else if (w == "this year") (query.From, query.To) = (new DateTime(today.Year, 1, 1), today.AddDays(1).AddTicks(-1));
                else if (w == "last year") (query.From, query.To) = (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year, 1, 1).AddTicks(-1));
                return Cut(text, m);
            }
            // Seasons (northern hemisphere): "last summer", "this winter", "summer 2024", "the summer of 2023", "in spring"
            m = Regex.Match(text, @"\b(?:(this|last|past) )?(spring|summer|autumn|fall|winter)(?:(?: of)? (\d{4}))?\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                String season = m.Groups[2].Value.ToLowerInvariant();
                int startMonth = season == "spring" ? 3 : season == "summer" ? 6 : season == "winter" ? 12 : 9;
                int year;
                if (m.Groups[3].Success)
                    year = Int32.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                else
                {
                    // The most recent such season that has begun ("last" skips one that is still going on).
                    year = today.Year;
                    DateTime start = new DateTime(year, startMonth, 1);
                    if (start > today)
                        year--;
                    bool current = new DateTime(year, startMonth, 1).AddMonths(3) > today;
                    if (current && m.Groups[1].Value.Equals("last", StringComparison.OrdinalIgnoreCase))
                        year--;
                }
                // Winter 2024 runs from December 2024.
                DateTime from = new DateTime(year, startMonth, 1);
                (query.From, query.To) = (from, from.AddMonths(3).AddTicks(-1));
                return Cut(text, m);
            }
            // Months: "in March", "March 2024", "last March", "in Aug"
            m = Regex.Match(text, @"\b(?:(last|this) )?(" + String.Join("|", Months) + "|" + String.Join("|", ShortMonths.Where(s => s != "may").Select(s => s + @"\.?")) + @")(?: (\d{4}))?\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                String name = m.Groups[2].Value.TrimEnd('.').ToLowerInvariant();
                int month = Array.IndexOf(Months, name) + 1;
                if (month == 0)
                    month = Array.IndexOf(ShortMonths, name) + 1;
                int year = m.Groups[3].Success ? Int32.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                    : (month > today.Month || (month == today.Month && m.Groups[1].Value.Equals("last", StringComparison.OrdinalIgnoreCase)) ? today.Year - 1 : today.Year);
                DateTime from = new DateTime(year, month, 1);
                (query.From, query.To) = (from, from.AddMonths(1).AddTicks(-1));
                return Cut(text, m);
            }
            // "since 2023", "after 2022", "before 2020", "until 2019"
            m = Regex.Match(text, @"\b(since|after|before|until|till) (\d{4})\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int year = Int32.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                String word = m.Groups[1].Value.ToLowerInvariant();
                if (word == "since") query.From = new DateTime(year, 1, 1);
                else if (word == "after") query.From = new DateTime(year + 1, 1, 1);
                else if (word == "before") query.To = new DateTime(year, 1, 1).AddTicks(-1);
                else query.To = new DateTime(year + 1, 1, 1).AddTicks(-1);
                return Cut(text, m);
            }
            // A year: "in 2024", "2023"
            m = Regex.Match(text, @"\b(?:in )?((?:19|20)\d{2})\b", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int year = Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                if (year <= today.Year + 1)
                {
                    (query.From, query.To) = (new DateTime(year, 1, 1), new DateTime(year + 1, 1, 1).AddTicks(-1));
                    return Cut(text, m);
                }
            }
            return text;
        }

        private static int Number(String word)
        {
            switch (word.ToLowerInvariant())
            {
                case "a": case "an": case "one": return 1;
                case "two": return 2;
                case "three": return 3;
                default: return Int32.Parse(word, CultureInfo.InvariantCulture);
            }
        }

        private static DateTime Subtract(DateTime date, int n, String unit)
        {
            switch (unit.ToLowerInvariant())
            {
                case "day": return date.AddDays(-n);
                case "week": return date.AddDays(-7 * n);
                case "month": return date.AddMonths(-n);
                default: return date.AddYears(-n);
            }
        }

        private static String Cut(String text, Match m)
        {
            return text.Substring(0, m.Index) + " " + text.Substring(m.Index + m.Length);
        }
    }
}
