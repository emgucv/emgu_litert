// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Chat's ChatModelCatalog: the models a chat offers per platform, as ChatModelOptions. No model is loaded.
[TestFixture]
public class UnitTestChatModelCatalog
{
    [Test]
    public void EveryPlatformHasTheSmallModelsAndGemma4()
    {
        foreach (ChatPlatform platform in Enum.GetValues(typeof(ChatPlatform)))
        {
            IList<ChatModelOption> models = ChatModelCatalog.For(platform);
            Assert.That(models.Take(5).Select(m => m.Id), Is.EqualTo(new[] { "Qwen3", "Qwen35_0_8B_VL", "Qwen35_4B", "Gemma4E2B", "Gemma4E4B" }), platform.ToString());
            Assert.That(models.All(m => !m.IsRemote), Is.True, "the server isn't in the catalog");
        }
    }

    [Test]
    public void Gemma4_12BIsOnlyOfferedOnDesktops()
    {
        Assert.That(ChatModelCatalog.For(ChatPlatform.Android).Any(m => m.Id == "Gemma4_12B"), Is.False);
        Assert.That(ChatModelCatalog.For(ChatPlatform.iOS).Any(m => m.Id == "Gemma4_12B"), Is.False);
        ChatModelOption mac = ChatModelCatalog.For(ChatPlatform.MacCatalyst).Single(m => m.Id == "Gemma4_12B");
        ChatModelOption windows = ChatModelCatalog.For(ChatPlatform.Windows).Single(m => m.Id == "Gemma4_12B");
        Assert.That(mac.Detail, Does.Contain("Mac"));
        Assert.That(windows.Detail, Does.Contain("CPU only on Windows"));
        Assert.That(mac.Backends, Is.EqualTo(new[] { "cpu", "gpu" }));
        Assert.That(windows.Backends, Is.EqualTo(new[] { "cpu" }));
    }

    [Test]
    public void TheFlagsMatchWhatTheModelsCanDo()
    {
        foreach (ChatModelOption option in ChatModelCatalog.For(ChatPlatform.MacCatalyst))
        {
            Assert.That(option.ModelFile, Is.Not.Null, option.DisplayName);
            bool gemma = option.Id.StartsWith("Gemma4", StringComparison.Ordinal);
            Assert.That(option.CanUseTools, Is.EqualTo(gemma), option.DisplayName + " tools");
            Assert.That(option.MaxContextWindow > 0, Is.EqualTo(gemma), option.DisplayName + " context window choice");
            Assert.That(option.Create().GetType().Name, Is.EqualTo(option.Id), option.DisplayName + " instance");
        }
    }

    [Test]
    public void TheGpuIsOfferedWhereItWasVerified()
    {
        IList<ChatModelOption> android = ChatModelCatalog.For(ChatPlatform.Android);
        IList<ChatModelOption> mac = ChatModelCatalog.For(ChatPlatform.MacCatalyst);
        ChatModelOption gemma = android.Single(o => o.Id == "Gemma4E2B");
        Assert.That(gemma.DisplayName, Is.EqualTo("Gemma 4 E2B"));
        Assert.That(gemma.Backends, Is.EqualTo(new[] { "cpu", "gpu" }));
        Assert.That(android.Single(o => o.Id == "Gemma4E4B").Backends, Is.EqualTo(new[] { "cpu", "gpu" }), "E4B on Android's GPU too");
        Assert.That(android.Single(o => o.Id == "Qwen3").Backends, Is.EqualTo(new[] { "cpu" }));
        Assert.That(mac.Single(o => o.Id == "Gemma4E4B").Backends, Is.EqualTo(new[] { "cpu", "gpu" }));
        Assert.That(ChatModelCatalog.For(ChatPlatform.iOS).All(o => o.Backends.SequenceEqual(new[] { "cpu" })), Is.True);
        Assert.That(gemma.CanUseTools && gemma.AcceptsImages && gemma.AcceptsAudio && gemma.CanSummarize, Is.True);
        Assert.That(gemma.ContextWindow, Is.EqualTo(8192));
        Assert.That(gemma.MaxContextWindow, Is.EqualTo(32000));
        Assert.That(gemma.DownloadSize, Is.EqualTo(2600L << 20));
        Assert.That(android.Single(o => o.Id == "Qwen35_0_8B_VL").CanThink, Is.False);
        Assert.That(ChatModelCatalog.For(ChatPlatform.Android), Is.Not.SameAs(android), "each call makes new options");
    }
}
