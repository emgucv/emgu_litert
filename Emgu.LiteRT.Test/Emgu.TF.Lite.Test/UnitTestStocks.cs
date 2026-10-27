// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Text.Json;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The stock tools' reading of Twelve Data's responses (AgentTools.FormatStockQuote, FormatStockHistory, StockHistoryRange,
// BestStockListing), with responses as Twelve Data returned them for its demo key on 2026-10-27. No network.
[TestFixture]
public class UnitTestStocks
{
    private const string Quote = @"{""symbol"":""AAPL"",""name"":""Apple Inc."",""exchange"":""NASDAQ"",""mic_code"":""XNGS"",""currency"":""USD"",
        ""datetime"":""2026-10-27"",""timestamp"":1793107800,""open"":""343.165"",""high"":""347.77"",""low"":""342.4"",""close"":""347.1"",
        ""volume"":""1127984"",""previous_close"":""343.91000"",""change"":""3.19000"",""percent_change"":""0.92756718"",""is_market_open"":false,
        ""fifty_two_week"":{""low"":""243.42000"",""high"":""351.73001"",""range"":""243.419998 - 351.730011""}}";

    private const string Weekly = @"{""meta"":{""symbol"":""AAPL"",""interval"":""1week"",""currency"":""USD"",""exchange"":""NASDAQ""},
        ""values"":[
          {""datetime"":""2026-10-26"",""open"":""343.92001"",""high"":""347.77"",""low"":""341.029999"",""close"":""347.1""},
          {""datetime"":""2026-10-19"",""open"":""340.78000"",""high"":""351.73001"",""low"":""338.70001"",""close"":""347.73001""},
          {""datetime"":""2026-10-12"",""open"":""338.22000"",""high"":""342.10"",""low"":""330.50"",""close"":""340.00""}],
        ""status"":""ok""}";

    [Test]
    public void AQuoteSaysThePriceTheChangeAndTheRanges()
    {
        using JsonDocument quote = JsonDocument.Parse(Quote);
        string text = AgentTools.FormatStockQuote(quote.RootElement);
        Assert.That(text, Does.StartWith("Apple Inc. (AAPL, NASDAQ): 347.10 USD, the market is closed (the close of 2026-10-27)"));
        Assert.That(text, Does.Contain("Change +3.19 (+0.93%) from the previous close of 343.91"));
        Assert.That(text, Does.Contain("open 343.17, high 347.77, low 342.40"));
        Assert.That(text, Does.Contain("52 weeks: low 243.42, high 351.73"));
    }

    [Test]
    public void AHistoryGivesTheChangeTheHighAndTheLowOldestFirst()
    {
        using JsonDocument series = JsonDocument.Parse(Weekly);
        string text = AgentTools.FormatStockHistory(series.RootElement, "the past 3 weeks");
        // Twelve Data lists the newest first: the change is from the oldest close (340.00) to the newest (347.10).
        Assert.That(text, Does.StartWith("AAPL, the past 3 weeks: from 340.00 USD (week of 2026-10-12) to 347.10 USD (week of 2026-10-26), +7.10 (+2.09%)."));
        Assert.That(text, Does.Contain("High 351.73 (week of 2026-10-19), low 330.50 (week of 2026-10-12)."));
        Assert.That(text, Does.Contain("Closes: 2026-10-12 340.00; 2026-10-19 347.73; 2026-10-26 347.10."));
    }

    [Test]
    public void AnEmptyHistorySaysSo()
    {
        using JsonDocument series = JsonDocument.Parse(@"{""meta"":{""symbol"":""AAPL""},""values"":[],""status"":""ok""}");
        Assert.That(AgentTools.FormatStockHistory(series.RootElement, "the past week"), Is.EqualTo("No prices found for the past week."));
    }

    [TestCase("1 week", "1day", 6)]
    [TestCase("3 months", "1day", 67)]
    [TestCase("1 year", "1week", 54)]
    [TestCase("5 years", "1month", 61)]
    [TestCase("", "1week", 54)]
    public void APeriodChoosesItsBars(string period, string interval, int bars)
    {
        var range = AgentTools.StockHistoryRange(period, new DateTime(2026, 10, 27));
        Assert.That((range.Interval, range.Bars), Is.EqualTo((interval, bars)));
        Assert.That(range.Start, Is.Null);
    }

    [Test]
    public void YearToDateStartsInJanuary()
    {
        var range = AgentTools.StockHistoryRange("year to date", new DateTime(2026, 10, 27));
        Assert.That(range.Start, Is.EqualTo(new DateTime(2026, 1, 1)));
        Assert.That(range.Name, Is.EqualTo("this year so far"));
    }

    [Test]
    public void ACompanyNameFindsItsUsListing()
    {
        using JsonDocument found = JsonDocument.Parse(@"{""data"":[
            {""symbol"":""0R2V"",""instrument_name"":""Apple Inc."",""exchange"":""LSE"",""instrument_type"":""Common Stock"",""country"":""United Kingdom""},
            {""symbol"":""AAPL"",""instrument_name"":""Apple Inc."",""exchange"":""NASDAQ"",""instrument_type"":""Common Stock"",""country"":""United States""}],
            ""status"":""ok""}");
        Assert.That(AgentTools.BestStockListing(found.RootElement, "apple"), Is.EqualTo("AAPL"));
        Assert.That(AgentTools.BestStockListing(found.RootElement, "0r2v"), Is.EqualTo("0R2V"), "a ticker that matches comes first");
        using JsonDocument none = JsonDocument.Parse(@"{""data"":[],""status"":""ok""}");
        Assert.That(AgentTools.BestStockListing(none.RootElement, "nothing"), Is.Null);
    }
}
