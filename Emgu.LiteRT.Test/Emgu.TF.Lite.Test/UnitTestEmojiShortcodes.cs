// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// EmojiShortcodes: ":smile:" and the like (as copied from Slack) become the emoji; what isn't one stays as written. No model.
[TestFixture]
public class UnitTestEmojiShortcodes
{
    [Test]
    public void ReplacesKnownShortcodes()
    {
        Assert.That(EmojiShortcodes.Replace(":slightly_smiling_face:"), Is.EqualTo("\U0001F642"));
        Assert.That(EmojiShortcodes.Replace("Thanks :+1: and :thumbsup:, :tada:!"), Is.EqualTo("Thanks \U0001F44D and \U0001F44D, \U0001F389!"));
        Assert.That(EmojiShortcodes.Replace(":SMILE:"), Is.EqualTo("\U0001F604"), "names are not case sensitive");
        Assert.That(EmojiShortcodes.Replace(":smile::tada:"), Is.EqualTo("\U0001F604\U0001F389"));
        Assert.That(EmojiShortcodes.Replace(":flag-us:"), Is.EqualTo("\U0001F1FA\U0001F1F8"));
    }

    [Test]
    public void LeavesWhatIsNotAnEmoji()
    {
        foreach (string text in new[]
        {
            "", "no colon at all", "a : b", "a:b", "https://example.com:8080/x", "10:30:45", ":partyparrot:", "::", ":not a name:", ":smi", "ratio 3:2:1"
        })
            Assert.That(EmojiShortcodes.Replace(text), Is.EqualTo(text), text);
        Assert.That(EmojiShortcodes.Replace(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void AnUnknownNameDoesNotHideTheNextShortcode()
    {
        Assert.That(EmojiShortcodes.Replace(":foo:smile:"), Is.EqualTo(":foo\U0001F604"));
        Assert.That(EmojiShortcodes.Replace("at 10:30:smile: ok"), Is.EqualTo("at 10:30\U0001F604 ok"));
    }

    [Test]
    public void LeavesCodeAlone()
    {
        string code = "Use `:smile:` or\n```\nx = \":tada:\"\n```\nbut :smile: here";
        Assert.That(EmojiShortcodes.Replace(code), Is.EqualTo("Use `:smile:` or\n```\nx = \":tada:\"\n```\nbut \U0001F604 here"));
    }

    [Test]
    public void ATextStillBeingWrittenGrowsToTheEmoji()
    {
        string full = "Hi :smile: there";
        for (int length = 0; length <= full.Length; length++)
        {
            string shown = EmojiShortcodes.Replace(full.Substring(0, length));
            if (length < "Hi :smile:".Length)
                Assert.That(shown, Is.EqualTo(full.Substring(0, length)), "unfinished: " + length);
            else
                Assert.That(shown, Does.StartWith("Hi \U0001F604"));
        }
        Assert.That(EmojiShortcodes.Replace(full), Is.EqualTo("Hi \U0001F604 there"));
    }
}
