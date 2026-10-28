//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    // Stock prices from Twelve Data (https://twelvedata.com): a quote and a period's history, with the user's API key (the
    // free Basic plan: 800 credits a day, 8 a minute; each call here is 1 credit, a company name looked up 1 more).
    public static partial class AgentTools
    {
        private const string TwelveDataUrl = "https://api.twelvedata.com/";
        // A ticker as written ("AAPL", "BRK.B", "SHOP:TSX"); anything else (a company name) is looked up first.
        private static readonly Regex TickerPattern = new Regex(@"^[A-Za-z0-9]{1,6}([.\-][A-Za-z0-9]{1,3})?(:[A-Za-z]{2,6})?$", RegexOptions.CultureInvariant);
        // The most closes listed in a history (the rest are summarized by the change, the high and the low).
        private const int MaxListedCloses = 12;

        /// <summary>
        /// get_stock_quote: a stock's or ETF's latest price from Twelve Data - the change from the previous close, the day's
        /// open, high and low, and the 52-week range. Takes a ticker or a company name (looked up).
        /// </summary>
        /// <param name="apiKey">The Twelve Data API key (twelvedata.com; free for 800 requests a day)</param>
        /// <param name="http">The HttpClient to use, or null for DefaultHttpClient</param>
        /// <param name="chart">Given the day's prices every 5 minutes, to plot (one more request); or null for none</param>
        public static AIFunction StockQuote(string apiKey, HttpClient http = null, Action<StockChart> chart = null)
        {
            string key = CheckTwelveDataKey(apiKey);
            HttpClient client = http ?? DefaultHttpClient;
            return AIFunctionFactory.Create(
                ([Description("The stock's ticker symbol - give the ticker when you know it (e.g. INTC for Intel, MSFT for Microsoft, SHOP:TSX) - or else the company's name")] string symbol, CancellationToken cancellationToken) =>
                    StockQuoteAsync(client, key, symbol, chart, cancellationToken),
                "get_stock_quote",
                "Gets the latest price of a stock or ETF, with its change since the previous close, the day's range and the 52-week range. Use it for questions about a stock price or how a stock is doing today.");
        }

        /// <summary>
        /// get_stock_history: how a stock or ETF did over a past period, from Twelve Data - the change over the period, its
        /// high and low, and the closes along the way. Takes a ticker or a company name (looked up).
        /// </summary>
        /// <param name="apiKey">The Twelve Data API key</param>
        /// <param name="http">The HttpClient to use, or null for DefaultHttpClient</param>
        /// <param name="chart">Given the period's closes, to plot; or null for none</param>
        public static AIFunction StockHistory(string apiKey, HttpClient http = null, Action<StockChart> chart = null)
        {
            string key = CheckTwelveDataKey(apiKey);
            HttpClient client = http ?? DefaultHttpClient;
            return AIFunctionFactory.Create(
                ([Description("The stock's ticker symbol - give the ticker when you know it (e.g. INTC for Intel, MSFT for Microsoft) - or else the company's name")] string symbol,
                 [Description("The period: \"1 week\", \"1 month\", \"3 months\", \"6 months\", \"1 year\", \"5 years\", \"10 years\" or \"year to date\"")] string period,
                 CancellationToken cancellationToken) => StockHistoryAsync(client, key, symbol, period, DateTime.Today, chart, cancellationToken),
                "get_stock_history",
                "Gets how a stock or ETF did over a past period: its change in price and percent, its high and low, and its closing prices along the way. Use it for questions like \"how did Apple do this year?\".");
        }

        private static string CheckTwelveDataKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("A Twelve Data API key is needed", nameof(apiKey));
            return apiKey.Trim();
        }

        /// <summary>
        /// The Twelve Data request for a period (the bar interval, how many bars, and a start date for "year to date"):
        /// daily bars up to 3 months, weekly up to 2 years, monthly beyond; 1 year by default.
        /// </summary>
        /// <param name="period">"1 week", "3 months", "1 year", "year to date"...</param>
        /// <param name="today">Today, for "year to date"</param>
        /// <returns>The interval ("1day", "1week" or "1month"), the number of bars, the start date (or null), and the period in words</returns>
        public static (string Interval, int Bars, DateTime? Start, string Name) StockHistoryRange(string period, DateTime today)
        {
            string text = (period ?? string.Empty).Trim().ToLowerInvariant();
            if (Regex.IsMatch(text, @"\b(ytd|year to date|this year|since january)\b"))
                return ("1day", 260, new DateTime(today.Year, 1, 1), "this year so far");
            Match match = Regex.Match(text, @"(\d+(?:\.\d+)?)?\s*(day|week|month|year)s?\b");
            double count = match.Success && match.Groups[1].Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
            string unit = match.Success ? match.Groups[2].Value : "year";
            double days = unit == "day" ? count : unit == "week" ? count * 7 : unit == "month" ? count * 30.5 : count * 365.25;
            days = Math.Max(1, Math.Min(days, 365.25 * 30));
            string name = "the past " + (count == 1 ? unit : count.ToString(CultureInfo.InvariantCulture) + " " + unit + "s");
            // Trading days are about 5/7 of the days; one more bar gives the starting close.
            if (days <= 95)
                return ("1day", (int)Math.Ceiling(days * 5 / 7) + 1, null, name);
            if (days <= 731)
                return ("1week", (int)Math.Ceiling(days / 7) + 1, null, name);
            return ("1month", (int)Math.Ceiling(days / 30.44) + 1, null, name);
        }

        private static async Task<string> StockQuoteAsync(HttpClient http, string key, string symbol, Action<StockChart> chart, CancellationToken cancellationToken)
        {
            (string resolved, string error) = await ResolveStockSymbolAsync(http, key, symbol, cancellationToken).ConfigureAwait(false);
            if (resolved == null)
                return error;
            using (JsonDocument quote = await GetTwelveDataAsync(http, key, "quote?symbol=" + Uri.EscapeDataString(resolved), cancellationToken).ConfigureAwait(false))
            {
                string failed = TwelveDataError(quote.RootElement, resolved);
                if (failed != null)
                    return failed;
                if (chart != null)
                {
                    // The day's prices to plot; the answer stands without them.
                    try
                    {
                        using (JsonDocument day = await GetTwelveDataAsync(http, key, "time_series?symbol=" + Uri.EscapeDataString(resolved)
                            + "&interval=5min&outputsize=100", cancellationToken).ConfigureAwait(false))
                        {
                            StockChart plotted = TwelveDataError(day.RootElement, resolved) == null ? IntradayChart(day.RootElement, quote.RootElement) : null;
                            if (plotted != null)
                                chart(plotted);
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                    }
                }
                return FormatStockQuote(quote.RootElement);
            }
        }

        /// <summary>
        /// The latest day's prices of a Twelve Data 5-minute time series, to plot, with the quote's name, currency and
        /// previous close (the line the day is compared with); null without prices.
        /// </summary>
        /// <param name="series">The /time_series response's root element (5-minute bars, newest first)</param>
        /// <param name="quote">The /quote response's root element</param>
        public static StockChart IntradayChart(JsonElement series, JsonElement quote)
        {
            if (!series.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                return null;
            var bars = values.EnumerateArray().Select(v => (Time: Text(v, "datetime"), Close: Number(v, "close")))
                .Where(b => b.Time != null && b.Time.Length >= 10 && b.Close.HasValue).ToList();
            if (bars.Count == 0)
                return null;
            // Only the newest bar's day (the series reaches back into the day before).
            string day = bars[0].Time.Substring(0, 10);
            var chart = new StockChart
            {
                Symbol = Text(quote, "symbol"),
                Name = Text(quote, "name"),
                Currency = Text(quote, "currency"),
                Intraday = true,
                PreviousClose = Number(quote, "previous_close"),
                Period = DateTime.TryParse(day, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                    ? date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) : day
            };
            foreach (var bar in bars.Where(b => b.Time.StartsWith(day, StringComparison.Ordinal)).Reverse())
                chart.Points.Add((bar.Time, bar.Close.Value));
            return chart.Points.Count > 0 ? chart : null;
        }

        /// <summary>A Twelve Data time series' closes, oldest first, to plot; null without prices.</summary>
        /// <param name="series">The /time_series response's root element (newest first)</param>
        /// <param name="periodName">The period in words ("the past year")</param>
        public static StockChart HistoryChart(JsonElement series, string periodName)
        {
            if (!series.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                return null;
            JsonElement meta = series.TryGetProperty("meta", out JsonElement m) ? m : default;
            var chart = new StockChart
            {
                Symbol = meta.ValueKind == JsonValueKind.Object ? Text(meta, "symbol") : null,
                Currency = meta.ValueKind == JsonValueKind.Object ? Text(meta, "currency") : null,
                Period = periodName
            };
            foreach (JsonElement value in values.EnumerateArray().Reverse())
            {
                string time = Text(value, "datetime");
                double? close = Number(value, "close");
                if (time != null && close.HasValue)
                    chart.Points.Add((time, close.Value));
            }
            return chart.Points.Count > 1 ? chart : null;
        }

        private static async Task<string> StockHistoryAsync(HttpClient http, string key, string symbol, string period, DateTime today, Action<StockChart> chart, CancellationToken cancellationToken)
        {
            (string resolved, string error) = await ResolveStockSymbolAsync(http, key, symbol, cancellationToken).ConfigureAwait(false);
            if (resolved == null)
                return error;
            var range = StockHistoryRange(period, today);
            string query = "time_series?symbol=" + Uri.EscapeDataString(resolved) + "&interval=" + range.Interval
                + "&outputsize=" + range.Bars.ToString(CultureInfo.InvariantCulture)
                + (range.Start.HasValue ? "&start_date=" + range.Start.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty);
            using (JsonDocument series = await GetTwelveDataAsync(http, key, query, cancellationToken).ConfigureAwait(false))
            {
                string failed = TwelveDataError(series.RootElement, resolved);
                if (failed != null)
                    return failed;
                StockChart plotted = chart != null ? HistoryChart(series.RootElement, range.Name) : null;
                if (plotted != null)
                    chart(plotted);
                return FormatStockHistory(series.RootElement, range.Name, today);
            }
        }

        // A ticker as given, or the best listing for a company name. Twelve Data's search by a bare name often misses the
        // company's main listing ("Intel" finds Intel's Thai receipts and an OTC "AI Maverick Intel", not INTC; "Intel Corp"
        // finds INTC), so a name without a US listing is searched again with " Inc" and " Corp" before a listing elsewhere is
        // taken - and a listing whose name doesn't start with the name asked for never is.
        private static async Task<(string Symbol, string Error)> ResolveStockSymbolAsync(HttpClient http, string key, string symbol, CancellationToken cancellationToken)
        {
            string text = (symbol ?? string.Empty).Trim();
            if (text.Length == 0)
                return (null, "No stock was named.");
            if (TickerPattern.IsMatch(text) && text == text.ToUpperInvariant())
                return (text, null);
            string elsewhere = null;
            foreach (string query in new[] { text, text + " Inc", text + " Corp" })
            {
                using (JsonDocument found = await GetTwelveDataAsync(http, key, "symbol_search?outputsize=10&symbol=" + Uri.EscapeDataString(query), cancellationToken).ConfigureAwait(false))
                {
                    string failed = TwelveDataError(found.RootElement, text);
                    if (failed != null)
                        return (null, failed);
                    string listed = BestStockListing(found.RootElement, text, listedOnly: true);
                    if (listed != null)
                        return (listed, null);
                    elsewhere = elsewhere ?? BestStockListing(found.RootElement, text);
                }
            }
            return elsewhere != null ? (elsewhere, null)
                : (null, "No stock found for \"" + text + "\". Call the tool again with its ticker symbol if you know it, or ask the user for it.");
        }

        /// <summary>
        /// The best listing in a Twelve Data symbol_search response. Of the listings whose name starts with the query (a
        /// company's name), a stock or ETF on a US exchange (NASDAQ, NYSE...) comes first - unless the query is written as a
        /// ticker (upper case, or with digits), when the listing with that ticker does. Unless <paramref name="listedOnly"/>,
        /// then: one over the counter in the US, a stock elsewhere, any of them, and last a listing with the query as its
        /// ticker in another case.
        /// </summary>
        /// <param name="response">The response's root element</param>
        /// <param name="query">What was asked for: a ticker or a company's name</param>
        /// <param name="listedOnly">True for only a ticker as written or a listing on a US exchange</param>
        /// <returns>The symbol, or null when there was none</returns>
        public static string BestStockListing(JsonElement response, string query, bool listedOnly = false)
        {
            if (!response.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return null;
            string asked = (query ?? string.Empty).Trim();
            var listings = data.EnumerateArray().Select(d => (
                Symbol: Text(d, "symbol"), Name: Text(d, "instrument_name") ?? string.Empty, Exchange: Text(d, "exchange") ?? string.Empty,
                Type: Text(d, "instrument_type") ?? string.Empty, Country: Text(d, "country") ?? string.Empty))
                .Where(l => !string.IsNullOrEmpty(l.Symbol)).ToList();
            var exact = listings.FirstOrDefault(l => string.Equals(l.Symbol, asked, StringComparison.OrdinalIgnoreCase));
            // "AAPL" or "0r2v" is a ticker; "Ford" is a name, though FORD is Forward Industries' ticker.
            if (exact.Symbol != null && (asked == asked.ToUpperInvariant() || asked.Any(char.IsDigit)))
                return exact.Symbol;
            var named = listings.Where(l => NameStartsWith(l.Name, asked) && l.Type != "Warrant" && l.Type != "Mutual Fund").ToList();
            var us = named.Where(l => l.Country == "United States").ToList();
            var listed = us.FirstOrDefault(l => l.Exchange != "OTC" && (l.Type == "Common Stock" || l.Type == "ETF"));
            if (listed.Symbol != null)
                return listed.Symbol;
            if (listedOnly)
                return null;
            if (us.Count > 0)
                return us[0].Symbol;
            var stock = named.FirstOrDefault(l => l.Type == "Common Stock");
            return stock.Symbol ?? (named.Count > 0 ? named[0].Symbol : exact.Symbol);
        }

        // Whether a listing's name starts with the name asked for, as whole words: "Intel Corporation" and "INTEL" for "Intel",
        // "Amazon.com Inc." for "Amazon"; not "Intellect Design Arena" nor "AI Maverick Intel".
        private static bool NameStartsWith(string name, string asked)
        {
            if (asked.Length == 0 || !name.StartsWith(asked, StringComparison.OrdinalIgnoreCase))
                return false;
            return name.Length == asked.Length || !char.IsLetterOrDigit(name[asked.Length]);
        }

        /// <summary>A Twelve Data quote, in words for the model.</summary>
        /// <param name="quote">The /quote response's root element</param>
        public static string FormatStockQuote(JsonElement quote)
        {
            string currency = Text(quote, "currency");
            string price = Money(Number(quote, "close"));
            var text = new StringBuilder();
            text.Append(Text(quote, "name") ?? Text(quote, "symbol")).Append(" (").Append(Text(quote, "symbol"));
            if (Text(quote, "exchange") != null)
                text.Append(", ").Append(Text(quote, "exchange"));
            text.Append("): ").Append(price).Append(currency != null ? " " + currency : string.Empty);
            bool open = quote.TryGetProperty("is_market_open", out JsonElement isOpen) && isOpen.ValueKind == JsonValueKind.True;
            text.Append(open ? ", the market is open (" : ", the market is closed (the close of ").Append(Text(quote, "datetime")).Append(")");
            double? change = Number(quote, "change"), percent = Number(quote, "percent_change"), previous = Number(quote, "previous_close");
            if (change.HasValue && percent.HasValue)
                text.Append(". Change ").Append(Signed(change.Value)).Append(" (").Append(Signed(percent.Value)).Append("%)")
                    .Append(previous.HasValue ? " from the previous close of " + Money(previous) : string.Empty);
            text.Append(". The day: open ").Append(Money(Number(quote, "open"))).Append(", high ").Append(Money(Number(quote, "high")))
                .Append(", low ").Append(Money(Number(quote, "low")));
            if (quote.TryGetProperty("fifty_two_week", out JsonElement year) && year.ValueKind == JsonValueKind.Object)
                text.Append(". 52 weeks: low ").Append(Money(Number(year, "low"))).Append(", high ").Append(Money(Number(year, "high")));
            text.Append(". (Twelve Data; prices may be delayed.)");
            return text.ToString();
        }

        /// <summary>A Twelve Data time series, in words for the model: the change over the period, its high and low, and some of the closes.</summary>
        /// <param name="series">The /time_series response's root element</param>
        /// <param name="periodName">The period in words ("the past year")</param>
        /// <param name="today">Today, to tell daily bars' today's price so far from the last full day's close; or null</param>
        public static string FormatStockHistory(JsonElement series, string periodName, DateTime? today = null)
        {
            if (!series.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
                return "No prices found for " + periodName + ".";
            JsonElement meta = series.TryGetProperty("meta", out JsonElement m) ? m : default;
            string symbol = meta.ValueKind == JsonValueKind.Object ? Text(meta, "symbol") : null;
            string currency = meta.ValueKind == JsonValueKind.Object ? Text(meta, "currency") : null;
            string interval = meta.ValueKind == JsonValueKind.Object ? Text(meta, "interval") : null;
            // Twelve Data lists the newest first.
            var bars = values.EnumerateArray().Select(v => (Date: Text(v, "datetime"), Close: Number(v, "close"), High: Number(v, "high"), Low: Number(v, "low")))
                .Where(b => b.Date != null && b.Close.HasValue).Reverse().ToList();
            if (bars.Count == 0)
                return "No prices found for " + periodName + ".";
            var first = bars[0];
            var last = bars[bars.Count - 1];
            var high = bars.Where(b => b.High.HasValue).OrderByDescending(b => b.High.Value).FirstOrDefault();
            var low = bars.Where(b => b.Low.HasValue).OrderBy(b => b.Low.Value).FirstOrDefault();
            double change = last.Close.Value - first.Close.Value;
            double percent = first.Close.Value != 0 ? change / first.Close.Value * 100 : 0;
            string unit = currency != null ? " " + currency : string.Empty;
            string bar = interval == "1week" ? "week of " : interval == "1month" ? "month of " : string.Empty;
            var text = new StringBuilder();
            text.Append(symbol ?? "The stock").Append(", ").Append(periodName).Append(": from ").Append(Money(first.Close)).Append(unit)
                .Append(" (").Append(bar).Append(first.Date).Append(") to ").Append(Money(last.Close)).Append(unit)
                .Append(" (").Append(bar).Append(last.Date).Append("), ").Append(Signed(change)).Append(" (").Append(Signed(percent)).Append("%).");
            if (high.Date != null && low.Date != null)
                text.Append(" High ").Append(Money(high.High)).Append(" (").Append(bar).Append(high.Date).Append("), low ")
                    .Append(Money(low.Low)).Append(" (").Append(bar).Append(low.Date).Append(").");
            // Some closes along the way, evenly spread, the first and the last included.
            int listed = Math.Min(MaxListedCloses, bars.Count);
            var picks = Enumerable.Range(0, listed).Select(i => bars[listed == 1 ? 0 : (int)Math.Round(i * (bars.Count - 1) / (double)(listed - 1))]).Distinct();
            text.Append(" Closes: ").Append(string.Join("; ", picks.Select(b => b.Date + " " + Money(b.Close)))).Append(".");
            // Daily bars include today's while the market is open: a model asked for yesterday's close took that one ("to
            // 93.49 (2026-10-28)") on 28 October - so say which is today's and which is the last full day's close.
            if (today.HasValue && bar.Length == 0 && interval != null && interval.EndsWith("day", StringComparison.Ordinal))
            {
                string todayText = today.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string yesterdayText = today.Value.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                Func<string, string> named = date => date.StartsWith(yesterdayText, StringComparison.Ordinal) ? date + ", yesterday" : date;
                int closed = last.Date.StartsWith(todayText, StringComparison.Ordinal) ? bars.Count - 2 : bars.Count - 1;
                if (closed < bars.Count - 1)
                    text.Append(" ").Append(Money(last.Close)).Append(unit).Append(" (").Append(last.Date).Append(") is today's price so far - the day isn't over.");
                if (closed >= 0)
                    text.Append(" The last full day's close: ").Append(Money(bars[closed].Close)).Append(unit).Append(" (").Append(named(bars[closed].Date)).Append(").");
            }
            text.Append(" (Twelve Data.)");
            return text.ToString();
        }

        // Twelve Data's error ({"status":"error","code":...,"message":...}) in words for the model, or null.
        private static string TwelveDataError(JsonElement response, string symbol)
        {
            if (response.ValueKind != JsonValueKind.Object || Text(response, "status") != "error")
                return null;
            int code = response.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
            switch (code)
            {
                case 401:
                case 403:
                    return "The Twelve Data API key was refused (or this needs a paid plan). Tell the user to check the key in the settings.";
                case 404:
                case 400:
                    return "No stock data for \"" + symbol + "\". Ask the user for its ticker symbol, or try the company's name.";
                case 429:
                    return "Twelve Data's request limit was reached (the free plan allows 8 a minute and 800 a day). Try again in a minute.";
                default:
                    return "Twelve Data couldn't answer: " + (Text(response, "message") ?? "error " + code) + ".";
            }
        }

        private static async Task<JsonDocument> GetTwelveDataAsync(HttpClient http, string key, string pathAndQuery, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, TwelveDataUrl + pathAndQuery))
            {
                // The key goes in the header, not the URL (so it isn't logged with the address).
                request.Headers.TryAddWithoutValidation("Authorization", "apikey " + key);
                using (HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try { return JsonDocument.Parse(body); }
                    catch (JsonException)
                    {
                        throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Twelve Data returned {0}", (int)response.StatusCode));
                    }
                }
            }
        }

        private static string Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        // Twelve Data sends numbers as strings ("347.1").
        private static double? Number(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
                return null;
            if (value.ValueKind == JsonValueKind.Number)
                return value.GetDouble();
            return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : (double?)null;
        }

        private static string Money(double? value) => value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "?";

        private static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
