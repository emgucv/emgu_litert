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
        public static AIFunction StockQuote(string apiKey, HttpClient http = null)
        {
            string key = CheckTwelveDataKey(apiKey);
            HttpClient client = http ?? DefaultHttpClient;
            return AIFunctionFactory.Create(
                ([Description("The stock's ticker symbol (e.g. AAPL, MSFT, SHOP:TSX) or the company's name")] string symbol, CancellationToken cancellationToken) =>
                    StockQuoteAsync(client, key, symbol, cancellationToken),
                "get_stock_quote",
                "Gets the latest price of a stock or ETF, with its change since the previous close, the day's range and the 52-week range. Use it for questions about a stock price or how a stock is doing today.");
        }

        /// <summary>
        /// get_stock_history: how a stock or ETF did over a past period, from Twelve Data - the change over the period, its
        /// high and low, and the closes along the way. Takes a ticker or a company name (looked up).
        /// </summary>
        /// <param name="apiKey">The Twelve Data API key</param>
        /// <param name="http">The HttpClient to use, or null for DefaultHttpClient</param>
        public static AIFunction StockHistory(string apiKey, HttpClient http = null)
        {
            string key = CheckTwelveDataKey(apiKey);
            HttpClient client = http ?? DefaultHttpClient;
            return AIFunctionFactory.Create(
                ([Description("The stock's ticker symbol (e.g. AAPL, MSFT) or the company's name")] string symbol,
                 [Description("The period: \"1 week\", \"1 month\", \"3 months\", \"6 months\", \"1 year\", \"5 years\", \"10 years\" or \"year to date\"")] string period,
                 CancellationToken cancellationToken) => StockHistoryAsync(client, key, symbol, period, DateTime.Today, cancellationToken),
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

        private static async Task<string> StockQuoteAsync(HttpClient http, string key, string symbol, CancellationToken cancellationToken)
        {
            (string resolved, string error) = await ResolveStockSymbolAsync(http, key, symbol, cancellationToken).ConfigureAwait(false);
            if (resolved == null)
                return error;
            using (JsonDocument quote = await GetTwelveDataAsync(http, key, "quote?symbol=" + Uri.EscapeDataString(resolved), cancellationToken).ConfigureAwait(false))
            {
                string failed = TwelveDataError(quote.RootElement, resolved);
                return failed ?? FormatStockQuote(quote.RootElement);
            }
        }

        private static async Task<string> StockHistoryAsync(HttpClient http, string key, string symbol, string period, DateTime today, CancellationToken cancellationToken)
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
                return failed ?? FormatStockHistory(series.RootElement, range.Name);
            }
        }

        // A ticker as given, or the best listing for a company name (a US listing first, as most questions mean those).
        private static async Task<(string Symbol, string Error)> ResolveStockSymbolAsync(HttpClient http, string key, string symbol, CancellationToken cancellationToken)
        {
            string text = (symbol ?? string.Empty).Trim();
            if (text.Length == 0)
                return (null, "No stock was named.");
            if (TickerPattern.IsMatch(text) && (text == text.ToUpperInvariant() || text.Length <= 4))
                return (text.ToUpperInvariant(), null);
            using (JsonDocument found = await GetTwelveDataAsync(http, key, "symbol_search?outputsize=10&symbol=" + Uri.EscapeDataString(text), cancellationToken).ConfigureAwait(false))
            {
                string failed = TwelveDataError(found.RootElement, text);
                if (failed != null)
                    return (null, failed);
                string best = BestStockListing(found.RootElement, text);
                return best != null ? (best, null) : (null, "No stock found for \"" + text + "\". Ask the user for its ticker symbol.");
            }
        }

        /// <summary>The best listing in a Twelve Data symbol_search response: a ticker matching the query, then a US stock or ETF, then the first.</summary>
        /// <param name="response">The response's root element</param>
        /// <param name="query">What was searched for</param>
        /// <returns>The symbol, or null when there was none</returns>
        public static string BestStockListing(JsonElement response, string query)
        {
            if (!response.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                return null;
            var listings = data.EnumerateArray().Select(d => (
                Symbol: Text(d, "symbol"), Type: Text(d, "instrument_type") ?? string.Empty, Country: Text(d, "country") ?? string.Empty))
                .Where(l => !string.IsNullOrEmpty(l.Symbol)).ToList();
            if (listings.Count == 0)
                return null;
            var exact = listings.FirstOrDefault(l => string.Equals(l.Symbol, query?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact.Symbol != null)
                return exact.Symbol;
            var us = listings.FirstOrDefault(l => l.Country == "United States" && (l.Type == "Common Stock" || l.Type == "ETF"));
            return us.Symbol ?? listings[0].Symbol;
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
        public static string FormatStockHistory(JsonElement series, string periodName)
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
            text.Append(" Closes: ").Append(string.Join("; ", picks.Select(b => b.Date + " " + Money(b.Close)))).Append(". (Twelve Data.)");
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
