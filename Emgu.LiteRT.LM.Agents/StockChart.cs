//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// A stock's prices to plot, from the stock tools (<see cref="AgentTools.StockQuote"/>: the day's prices every 5 minutes;
    /// <see cref="AgentTools.StockHistory"/>: a period's closes) - for an app to draw above the reply. Kept as JSON
    /// (<see cref="ToJson"/>, <see cref="FromJson"/>) so a saved chat can draw it again.
    /// </summary>
    public sealed class StockChart
    {
        /// <summary>The ticker, e.g. "AAPL".</summary>
        public string Symbol { get; set; }

        /// <summary>The company's name, or null.</summary>
        public string Name { get; set; }

        /// <summary>The prices' currency, e.g. "USD", or null.</summary>
        public string Currency { get; set; }

        /// <summary>What the chart covers, in words: "27 October 2026" for a day, "the past year"...</summary>
        public string Period { get; set; }

        /// <summary>True for one day's prices (labelled by time), false for a period's closes (labelled by date).</summary>
        public bool Intraday { get; set; }

        /// <summary>The close before the period (the previous day's for a day), drawn as a line to compare with; or null.</summary>
        public double? PreviousClose { get; set; }

        /// <summary>The prices, oldest first: the time or date as given ("2026-10-27 15:55:00", "2026-10-26") and the price.</summary>
        public List<(string Time, double Price)> Points { get; } = new List<(string, double)>();

        /// <summary>The change from <see cref="PreviousClose"/> (or the first price) to the last, or null without prices.</summary>
        public double? Change
        {
            get
            {
                if (Points.Count == 0)
                    return null;
                double start = PreviousClose ?? Points[0].Price;
                return Points[Points.Count - 1].Price - start;
            }
        }

        /// <summary>The chart as JSON (UTF-8).</summary>
        public byte[] ToJson()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("symbol", Symbol);
                    writer.WriteString("name", Name);
                    writer.WriteString("currency", Currency);
                    writer.WriteString("period", Period);
                    writer.WriteBoolean("intraday", Intraday);
                    if (PreviousClose.HasValue)
                        writer.WriteNumber("previous_close", PreviousClose.Value);
                    writer.WriteStartArray("points");
                    foreach (var point in Points)
                    {
                        writer.WriteStartArray();
                        writer.WriteStringValue(point.Time);
                        writer.WriteNumberValue(point.Price);
                        writer.WriteEndArray();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return stream.ToArray();
            }
        }

        /// <summary>A chart from <see cref="ToJson"/>, or null if the data isn't one.</summary>
        /// <param name="json">The JSON (UTF-8)</param>
        public static StockChart FromJson(byte[] json)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    var chart = new StockChart
                    {
                        Symbol = Get(root, "symbol"),
                        Name = Get(root, "name"),
                        Currency = Get(root, "currency"),
                        Period = Get(root, "period"),
                        Intraday = root.TryGetProperty("intraday", out JsonElement intraday) && intraday.ValueKind == JsonValueKind.True,
                        PreviousClose = root.TryGetProperty("previous_close", out JsonElement previous) && previous.ValueKind == JsonValueKind.Number ? previous.GetDouble() : (double?)null
                    };
                    if (root.TryGetProperty("points", out JsonElement points) && points.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement point in points.EnumerateArray())
                            if (point.ValueKind == JsonValueKind.Array && point.GetArrayLength() == 2)
                                chart.Points.Add((point[0].GetString(), point[1].GetDouble()));
                    return chart.Points.Count > 0 ? chart : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A point's time or date as a short label: "15:55" for a day's prices, "26 Oct" or "Oct 2026" otherwise.</summary>
        /// <param name="time">The point's time or date</param>
        /// <param name="longPeriod">True for a period longer than a year (labelled by month)</param>
        public string Label(string time, bool longPeriod = false)
        {
            if (Intraday && time != null && time.Length >= 16)
                return time.Substring(11, 5);
            if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                return date.ToString(longPeriod ? "MMM yyyy" : "d MMM", CultureInfo.InvariantCulture);
            return time ?? string.Empty;
        }

        private static string Get(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
