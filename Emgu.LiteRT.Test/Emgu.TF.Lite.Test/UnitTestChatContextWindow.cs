// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Chat's ChatContextWindow: the window a chat page loads a model with, from the user's choice, the model's
// own size and the device (the MAUI demo's rules, moved into the library on 2026-10-26).
[TestFixture]
public class UnitTestChatContextWindow
{
    [Test]
    public void ChoosesTheWindow()
    {
        // A fixed window (no maximum): always the model's default.
        Assert.That(ChatContextWindow.Choose(16384, 8192, 0, false, true), Is.Zero);
        // Automatic: the model's size on a desktop; on a phone only for a model with tools.
        Assert.That(ChatContextWindow.Choose(0, 8192, 32000, false, false), Is.EqualTo(8192));
        Assert.That(ChatContextWindow.Choose(0, 8192, 32000, true, true), Is.EqualTo(8192));
        Assert.That(ChatContextWindow.Choose(0, 8192, 32000, true, false), Is.Zero);
        // A choice, capped at the model's maximum.
        Assert.That(ChatContextWindow.Choose(16384, 8192, 32000, true, false), Is.EqualTo(16384));
        Assert.That(ChatContextWindow.Choose(64000, 8192, 32000, false, true), Is.EqualTo(32000));
    }

    [Test]
    public void NamesAndNotes()
    {
        Assert.That(ChatContextWindow.Name(0), Is.EqualTo("Automatic"));
        Assert.That(ChatContextWindow.Name(4096), Is.EqualTo("4k"));
        Assert.That(ChatContextWindow.Name(32000), Is.EqualTo("32k"));
        Assert.That(ChatContextWindow.Choices, Is.EqualTo(new[] { 0, 4096, 8192, 16384, 32000 }));
        Assert.That(ChatContextWindow.Note(8192, true), Does.Not.Contain("minutes"));
        Assert.That(ChatContextWindow.Note(16384, true), Does.Contain("On a phone"));
        Assert.That(ChatContextWindow.Note(32000, false), Does.Contain("Apple Silicon"));
    }
}
