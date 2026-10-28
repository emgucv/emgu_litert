// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// search_web's choice of recency (AgentTools.NewsTimeRange): news questions search Tavily's news over a recent time
// range, everything else is an ordinary search. No network.
[TestFixture]
public class UnitTestWebSearch
{
    [TestCase("James Webb Space Telescope news", "month")]
    [TestCase("latest NASA Artemis update", "month")]
    [TestCase("recent earthquakes in Japan", "month")]
    [TestCase("stock market today", "day")]
    [TestCase("what happened yesterday in Ottawa", "day")]
    [TestCase("breaking news Toronto", "day")]
    [TestCase("tech headlines this week", "week")]
    public void NewsQuestionsSearchRecentNews(string query, string range)
    {
        Assert.That(AgentTools.NewsTimeRange(query), Is.EqualTo(range));
    }

    [TestCase("iPhone 16e vs iPhone 17e specs")]
    [TestCase("compare Pixel 10 and Galaxy S26")]
    [TestCase("MacBook Air M5 review")]
    public void ComparisonsPreferThePastYear(string query)
    {
        Assert.That(AgentTools.NewsTimeRange(query), Is.Null);
        Assert.That(AgentTools.ComparisonTimeRange(query), Is.EqualTo("year"));
    }

    [TestCase("latest iPhone 17e review", "month")]
    public void NewsWordsWinOverComparisonWords(string query, string newsRange)
    {
        Assert.That(AgentTools.NewsTimeRange(query), Is.EqualTo(newsRange));
        Assert.That(AgentTools.ComparisonTimeRange(query), Is.Null, "a news search already limits the time");
    }

    [TestCase("how tall is the Eiffel Tower")]
    [TestCase("history of the Roman Empire")]
    [TestCase("best way to cook rice")]
    [TestCase("newspaper layout ideas")]
    [TestCase("")]
    [TestCase(null)]
    public void OtherQuestionsAreOrdinarySearches(string query)
    {
        Assert.That(AgentTools.NewsTimeRange(query), Is.Null);
        Assert.That(AgentTools.ComparisonTimeRange(query), Is.Null);
    }
}
