// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Linq;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The voice languages of Settings (VoiceLanguages.cs).
[TestFixture]
public class UnitTestVoiceLanguages
{
    [Test]
    public void TheChosenLanguageWinsOverTheDevicesAndTagsAreMadePlain()
    {
        Assert.That(VoiceLanguages.Resolve("zh-CN", "en-US"), Is.EqualTo("zh-CN"));
        Assert.That(VoiceLanguages.Resolve("", "en_GB"), Is.EqualTo("en-GB"));
        Assert.That(VoiceLanguages.Resolve(null, "zh-Hans-CN"), Is.EqualTo("zh-CN"));
        Assert.That(VoiceLanguages.Resolve(null, "zh-Hant"), Is.EqualTo("zh-TW"));
        Assert.That(VoiceLanguages.Resolve(null, "es-419"), Is.EqualTo("es-419"));
        Assert.That(VoiceLanguages.Resolve(null, "fr"), Is.EqualTo("fr"));
        Assert.That(VoiceLanguages.Resolve("  ", "DE-de"), Is.EqualTo("de-DE"));
        Assert.That(VoiceLanguages.Resolve(null, null), Is.EqualTo("en-US"));
        // How Android's recognizers write Mandarin.
        Assert.That(VoiceLanguages.Resolve("cmn-Hans-CN", null), Is.EqualTo("zh-CN"));
        Assert.That(VoiceLanguages.Resolve("cmn-Hant-TW", null), Is.EqualTo("zh-TW"));
        Assert.That(VoiceLanguages.Resolve("zh-Hant-TW", null), Is.EqualTo("zh-TW"));
        Assert.That(VoiceLanguages.Resolve("en-US-u-mu-celsius", null), Is.EqualTo("en-US"));
    }

    [Test]
    public void ARecognizersListIsMatchedByTagOrByLanguage()
    {
        var listed = new[] { "en-US", "zh-CN", "es_ES", "yue-HK", "cmn-Hant-TW" };
        Assert.That(VoiceLanguages.IsListed(listed, "zh-CN"), Is.True);
        Assert.That(VoiceLanguages.IsListed(listed, "ZH-cn"), Is.True);
        Assert.That(VoiceLanguages.IsListed(listed, "es-ES"), Is.True);      // underscore in the list
        Assert.That(VoiceLanguages.IsListed(listed, "zh"), Is.True);         // any region of the language
        Assert.That(VoiceLanguages.IsListed(listed, "zh-TW"), Is.True);      // listed as cmn-Hant-TW
        Assert.That(VoiceLanguages.IsListed(listed, "zh-HK"), Is.False);     // another region
        Assert.That(VoiceLanguages.IsListed(listed, "ja-JP"), Is.False);
        Assert.That(VoiceLanguages.IsListed(null, "zh-CN"), Is.False);
        Assert.That(VoiceLanguages.IsListed(listed, ""), Is.False);
    }

    [Test]
    public void TheListHasUniqueTagsWithNames()
    {
        Assert.That(VoiceLanguages.All.Select(l => l.Tag).Distinct().Count(), Is.EqualTo(VoiceLanguages.All.Count));
        Assert.That(VoiceLanguages.All.All(l => l.Name.Length > 0 && l.Tag.Contains('-')), Is.True);
        Assert.That(VoiceLanguages.NameOf("zh-cn"), Is.EqualTo("中文 (普通话)"));
        Assert.That(VoiceLanguages.NameOf("xx-XX"), Is.EqualTo("xx-XX"));
    }
}
