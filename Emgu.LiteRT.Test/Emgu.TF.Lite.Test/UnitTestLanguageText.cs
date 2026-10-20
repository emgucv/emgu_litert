// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The language helpers of the thinking translation (TODO item 39).
[TestFixture]
public class UnitTestLanguageText
{
    [Test]
    public void TagsBecomeTwoLetterCodes()
    {
        Assert.That(LanguageText.TwoLetter("es"), Is.EqualTo("es"));
        Assert.That(LanguageText.TwoLetter("zh-Latn"), Is.EqualTo("zh"));
        Assert.That(LanguageText.TwoLetter("zh-Hans"), Is.EqualTo("zh"));
        Assert.That(LanguageText.TwoLetter("PT-br"), Is.EqualTo("pt"));
        Assert.That(LanguageText.TwoLetter("und"), Is.Null);
        Assert.That(LanguageText.TwoLetter(""), Is.Null);
        Assert.That(LanguageText.TwoLetter(null), Is.Null);
    }

    // One token per word.
    private static int Words(string text) => text.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries).Length;

    [Test]
    public void LongTextIsCutAtLineEndsIntoPiecesThatFit()
    {
        string text = string.Join("\n", Enumerable.Range(1, 10).Select(i => "line " + i + " has five words"));
        var pieces = LanguageText.SplitForTranslation(text, 15, Words);
        Assert.That(pieces.Count, Is.EqualTo(4));       // 3 lines of 5 words fit in 15 tokens
        Assert.That(pieces.All(p => Words(p) <= 15), Is.True);
        Assert.That(string.Join("\n", pieces), Is.EqualTo(text));
    }

    [Test]
    public void ShortTextIsOnePieceAndALongLineStandsAlone()
    {
        Assert.That(LanguageText.SplitForTranslation("a short note", 100, Words), Is.EqualTo(new[] { "a short note" }));
        Assert.That(LanguageText.SplitForTranslation("", 100, Words), Is.Empty);
        Assert.That(LanguageText.SplitForTranslation(null, 100, Words), Is.Empty);
        var pieces = LanguageText.SplitForTranslation("one\n" + string.Join(" ", Enumerable.Repeat("long", 50)) + "\ntwo", 10, Words);
        Assert.That(pieces.Count, Is.EqualTo(3));
        Assert.That(Words(pieces[1]), Is.EqualTo(50));
    }
}
