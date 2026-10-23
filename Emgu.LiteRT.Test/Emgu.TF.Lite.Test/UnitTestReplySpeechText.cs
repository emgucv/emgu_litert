// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// ReplySpeechText: a reply's text made ready to read aloud, and its language from the writing system. No model.
[TestFixture]
public class UnitTestReplySpeechText
{
    [Test]
    public void RemovesMarkupCodeLinksAndEmoji()
    {
        string text = "## Result\n**Bold** and *italic* with `code` 😀\n- first item\n- second item\n" +
            "See [the docs](https://example.com) or https://example.com/x.\n```python\nprint(1)\n```\nDone!";
        Assert.That(ReplySpeechText.CleanForSpeech(text),
            Is.EqualTo("Result. Bold and italic with code. first item. second item. See the docs or. Done!"));
    }

    [Test]
    public void ReadsMathAsText()
    {
        Assert.That(ReplySpeechText.CleanForSpeech("The area is $\\pi r^2$:\n$$a^2 + b^2 = c^2$$"),
            Is.EqualTo("The area is π r²: a² + b² = c²."));
        Assert.That(ReplySpeechText.CleanForSpeech(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void DetectsTheWritingSystem()
    {
        Assert.That(ReplySpeechText.DetectScriptLanguage("今天天气很好，我们去公园吧。"), Is.EqualTo("zh"));
        Assert.That(ReplySpeechText.DetectScriptLanguage("今日はいい天気ですね。"), Is.EqualTo("ja"));
        Assert.That(ReplySpeechText.DetectScriptLanguage("안녕하세요, 반갑습니다."), Is.EqualTo("ko"));
        Assert.That(ReplySpeechText.DetectScriptLanguage("Привет, как дела?"), Is.EqualTo("ru"));
        Assert.That(ReplySpeechText.DetectScriptLanguage("Hello, how are you?"), Is.Null);
        // A few CJK words in an English sentence: still English.
        Assert.That(ReplySpeechText.DetectScriptLanguage("The word for water in Chinese is 水, pronounced shui."), Is.Null);
    }
}
