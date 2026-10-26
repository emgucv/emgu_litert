// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The MAUI demo's model list (Maui.Demo.Lite.Core's ChatModelCatalog), per platform.
[TestFixture]
public class UnitTestChatModelCatalog
{
    [Test]
    public void EveryPlatformHasTheSmallModelsAndGemma4()
    {
        foreach (ChatPlatform platform in Enum.GetValues(typeof(ChatPlatform)))
        {
            var choices = ChatModelCatalog.ModelsFor(platform).Select(m => m.Choice).ToList();
            Assert.That(choices.Take(5), Is.EqualTo(new[] { ModelChoice.Qwen3, ModelChoice.Qwen35_0_8B_VL, ModelChoice.Qwen35_4B, ModelChoice.Gemma4E2B, ModelChoice.Gemma4E4B }), platform.ToString());
            Assert.That(choices, Does.Not.Contain(ModelChoice.Server), "the server isn't in the list");
        }
    }

    [Test]
    public void Gemma4_12BIsOnlyOfferedOnDesktops()
    {
        Assert.That(ChatModelCatalog.ModelsFor(ChatPlatform.Android).Any(m => m.Choice == ModelChoice.Gemma4_12B), Is.False);
        Assert.That(ChatModelCatalog.ModelsFor(ChatPlatform.iOS).Any(m => m.Choice == ModelChoice.Gemma4_12B), Is.False);
        ModelOption mac = ChatModelCatalog.ModelsFor(ChatPlatform.MacCatalyst).Single(m => m.Choice == ModelChoice.Gemma4_12B);
        ModelOption windows = ChatModelCatalog.ModelsFor(ChatPlatform.Windows).Single(m => m.Choice == ModelChoice.Gemma4_12B);
        Assert.That(mac.Detail, Does.Contain("Mac"));
        Assert.That(windows.Detail, Does.Contain("CPU only on Windows"));
        Assert.That(mac.GpuOnMac && !windows.GpuOnWindows, Is.True);
    }

    [Test]
    public void TheFlagsMatchWhatTheModelsCanDo()
    {
        foreach (ModelOption option in ChatModelCatalog.ModelsFor(ChatPlatform.MacCatalyst))
        {
            Assert.That(option.File, Is.Not.Null, option.Name);
            bool gemma = option.Choice.ToString().StartsWith("Gemma4", StringComparison.Ordinal);
            Assert.That(option.CanUseTools, Is.EqualTo(gemma), option.Name + " tools");
            Assert.That(option.MaxContextTokens > 0, Is.EqualTo(gemma), option.Name + " context window choice");
            Assert.That(ChatModelCatalog.Create(option.Choice).GetType().Name, Is.EqualTo(option.Choice.ToString()), option.Name + " instance");
        }
    }

    [Test]
    public void BecomesTheLibrarysModelOptions()
    {
        // For Emgu.LiteRT.LM.Maui's ChatPage: the ModelChoice's name is the id (saved chats and the "model" setting carry
        // over), the GPU per platform a backend, and the user's own server a remote option at the end.
        var android = ChatModelOptions.FromCatalog(ChatModelCatalog.ModelsFor(ChatPlatform.Android), ChatPlatform.Android);
        var mac = ChatModelOptions.FromCatalog(ChatModelCatalog.ModelsFor(ChatPlatform.MacCatalyst), ChatPlatform.MacCatalyst);
        Assert.That(android.Select(o => o.Id), Is.EqualTo(new[] { "Qwen3", "Qwen35_0_8B_VL", "Qwen35_4B", "Gemma4E2B", "Gemma4E4B", "Server" }));
        Assert.That(mac.Select(o => o.Id), Does.Contain("Gemma4_12B"));
        var gemma = android.Single(o => o.Id == "Gemma4E2B");
        Assert.That(gemma.DisplayName, Is.EqualTo("Gemma 4 E2B"));
        Assert.That(gemma.Backends, Is.EqualTo(new[] { "cpu", "gpu" }));
        Assert.That(android.Single(o => o.Id == "Gemma4E4B").Backends, Is.EqualTo(new[] { "cpu" }), "E4B's GPU wasn't verified on Android");
        Assert.That(mac.Single(o => o.Id == "Gemma4E4B").Backends, Is.EqualTo(new[] { "cpu", "gpu" }));
        Assert.That(gemma.CanUseTools && gemma.AcceptsImages && gemma.AcceptsAudio && gemma.CanSummarize, Is.True);
        Assert.That(gemma.ContextWindow, Is.EqualTo(8192));
        Assert.That(gemma.MaxContextWindow, Is.EqualTo(32000));
        Assert.That(gemma.DownloadSize, Is.EqualTo(2600L << 20));
        Assert.That(gemma.ModelFile, Is.Not.Null);
        Assert.That(gemma.IsRemote, Is.False);
        Assert.That(gemma.Create().GetType().Name, Is.EqualTo("Gemma4E2B"));
        var qwen = android.Single(o => o.Id == "Qwen35_0_8B_VL");
        Assert.That(qwen.CanThink, Is.False);
        var server = android.Last();
        Assert.That(server.IsRemote, Is.True);
        Assert.That(server.Id, Is.EqualTo(ChatModelOptions.ServerId));
    }
}
