// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestVoiceEngines
{
    [Test]
    public void MapsATextsLanguageToKokoros()
    {
        Assert.That(VoiceEngines.KokoroLanguage("en", "CA"), Is.EqualTo("en-US"));
        Assert.That(VoiceEngines.KokoroLanguage("en", "GB"), Is.EqualTo("en-GB"));
        Assert.That(VoiceEngines.KokoroLanguage("en-AU", "US"), Is.EqualTo("en-GB"));
        Assert.That(VoiceEngines.KokoroLanguage("zh", null), Is.EqualTo("zh-CN"));
        Assert.That(VoiceEngines.KokoroLanguage("pt", "PT"), Is.EqualTo("pt-BR"));
        Assert.That(VoiceEngines.KokoroLanguage("ja", null), Is.EqualTo("ja"));
        Assert.That(VoiceEngines.KokoroLanguage("de", "DE"), Is.Null);
        Assert.That(VoiceEngines.KokoroLanguage("ko", null), Is.Null);
        // No language detected: the device's.
        Assert.That(VoiceEngines.KokoroLanguage(null, "FR", "fr"), Is.EqualTo("fr"));
        Assert.That(VoiceEngines.KokoroLanguage(null, null, null), Is.Null);
        Assert.That(VoiceEngines.KokoroLanguages.Count, Is.EqualTo(9));
    }

    [Test]
    public void CutsTheFirstSentenceAloneThenPacksTheRest()
    {
        string text = "Hello. The city library opens a new reading room next week. It has forty desks. "
            + "And a quiet corner for children! Is it open on Sundays? Yes.";
        Assert.That(VoiceEngines.Sentences(text).Count, Is.EqualTo(6));
        var pieces = VoiceEngines.SpeechPieces(text);
        Assert.That(pieces[0], Is.EqualTo("Hello."));
        Assert.That(string.Join(" ", pieces), Is.EqualTo(text));
        Assert.That(pieces.Count, Is.LessThan(6));
        Assert.That(pieces.Skip(1).All(p => p.Length <= VoiceEngines.PieceWeight), string.Join(" | ", pieces));
        // Chinese sentences end without spaces; each character counts twice.
        var chinese = VoiceEngines.SpeechPieces("你好。图书馆下周将开放一个新的阅览室。里面有四十张桌子。");
        Assert.That(chinese[0], Is.EqualTo("你好。"));
        Assert.That(VoiceEngines.SpeechPieces("   "), Is.Empty);
        // A sentence longer than a piece is a piece of its own.
        string longSentence = new string('a', 200) + ".";
        Assert.That(VoiceEngines.SpeechPieces("Hi. " + longSentence + " Bye."), Is.EqualTo(new[] { "Hi.", longSentence, "Bye." }));
    }

    [Test]
    public void KokoroSpeaksOnlyWhenChosenHereAndInItsLanguages()
    {
        Assert.That(VoiceEngines.Choose(VoiceEngine.Kokoro, true, "fr"), Is.EqualTo(VoiceEngine.Kokoro));
        Assert.That(VoiceEngines.Choose(VoiceEngine.Kokoro, true, null), Is.EqualTo(VoiceEngine.System));
        Assert.That(VoiceEngines.Choose(VoiceEngine.Kokoro, false, "fr"), Is.EqualTo(VoiceEngine.System));
        Assert.That(VoiceEngines.Choose(VoiceEngine.System, true, "fr"), Is.EqualTo(VoiceEngine.System));
        Assert.That(VoiceEngines.Parse("kokoro"), Is.EqualTo(VoiceEngine.Kokoro));
        Assert.That(VoiceEngines.Parse(null), Is.EqualTo(VoiceEngine.System));
        Assert.That(VoiceEngines.ToSetting(VoiceEngine.Kokoro), Is.EqualTo("kokoro"));
        Assert.That(VoiceEngines.VoiceSettingKey("en-US"), Is.EqualTo("kokoro_voice_en-US"));
    }
}
