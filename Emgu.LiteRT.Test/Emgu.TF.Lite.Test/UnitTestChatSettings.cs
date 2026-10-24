// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The MAUI demo's chat settings (Maui.Demo.Lite.Core's ChatSettings): defaults, the keys saved settings already use, and a store that fails.
[TestFixture]
public class UnitTestChatSettings
{
    [Test]
    public void DefaultsAreTheAppsDefaults()
    {
        var settings = new ChatSettings(new InMemorySettingsStore(), "Android", gpuByDefault: true);
        Assert.That(settings.Think, Is.True);
        Assert.That(settings.WebSearch, Is.False);
        Assert.That(settings.Memory, Is.True);
        Assert.That(settings.CountAloud, Is.True);
        Assert.That(settings.CameraSound, Is.True);
        Assert.That(settings.ModelsExpanded, Is.True);
        Assert.That(settings.CpuModelsExpanded, Is.Null);
        Assert.That(settings.LastModel, Is.Null);
        Assert.That(settings.ContextTokens, Is.EqualTo(0));
        Assert.That(settings.EmbeddingModel, Is.EqualTo(1));
        Assert.That(settings.VoiceLanguage, Is.Empty);
        Assert.That(settings.McpServersJson, Is.EqualTo("[]"));
        Assert.That(settings.UseGpu, Is.True);
        Assert.That(new ChatSettings(new InMemorySettingsStore(), "WinUI", gpuByDefault: false).UseGpu, Is.False);
    }

    [Test]
    public void ValuesAreKeptUnderTheKeysTheAppAlwaysUsed()
    {
        var store = new InMemorySettingsStore();
        var settings = new ChatSettings(store, "Android", true);
        settings.Think = false;
        settings.ContextTokens = 16384;
        settings.LastModel = "Gemma4E2B";
        settings.GpuModelsExpanded = false;
        settings.SetSearchModelDeleted("model.litertlm", true);
        Assert.That(store.Get("litert_lm_chat_think", true), Is.False);
        Assert.That(store.Get("litert_lm_chat_context_tokens", 0), Is.EqualTo(16384));
        Assert.That(store.Get<string>("litert_lm_chat_model", null), Is.EqualTo("Gemma4E2B"));
        Assert.That(settings.GpuModelsExpanded, Is.False);
        Assert.That(settings.SearchModelDeleted("model.litertlm"), Is.True);
        Assert.That(settings.SearchModelDeleted("other.litertlm"), Is.False);
    }

    [Test]
    public void TheGpuChoiceIsPerPlatformWithTheOldSettingAsDefault()
    {
        var store = new InMemorySettingsStore();
        store.Set("litert_lm_chat_use_gpu", false);          // set before the choice was kept per platform
        var android = new ChatSettings(store, "Android", true);
        Assert.That(android.UseGpu, Is.False);
        android.UseGpu = true;
        Assert.That(store.Get("litert_lm_chat_use_gpu_Android", false), Is.True);
        Assert.That(new ChatSettings(store, "MacCatalyst", true).UseGpu, Is.False, "another platform still reads the old setting");
    }

    [Test]
    public void TheOldTextOnlyEmbeddingModelReadsAsTextVision()
    {
        var store = new InMemorySettingsStore();
        store.Set("litert_lm_chat_embedding_model", 0);
        Assert.That(new ChatSettings(store, "Android", true).EmbeddingModel, Is.EqualTo(1));
        store.Set("litert_lm_chat_embedding_model", 2);
        Assert.That(new ChatSettings(store, "Android", true).EmbeddingModel, Is.EqualTo(2));
    }

    [Test]
    public void AFailingStoreGivesTheDefaultsAndDoesntThrow()
    {
        var settings = new ChatSettings(new FailingStore(), "Android", true);
        Assert.That(settings.Think, Is.True);
        Assert.That(settings.CpuModelsExpanded, Is.Null);
        Assert.DoesNotThrow(() => settings.Think = false);
        Assert.DoesNotThrow(() => settings.SetSearchModelDeleted("x", true));
    }

    private sealed class FailingStore : ISettingsStore
    {
        public bool ContainsKey(string key) => throw new InvalidOperationException("No preferences here");
        public T Get<T>(string key, T defaultValue) => throw new InvalidOperationException("No preferences here");
        public void Set<T>(string key, T value) => throw new InvalidOperationException("No preferences here");
    }
}
