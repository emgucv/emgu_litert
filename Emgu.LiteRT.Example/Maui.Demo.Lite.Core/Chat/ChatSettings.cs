// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;

namespace Maui.Demo.Lite;

// Where settings are kept: MAUI's Preferences in the app (AppSettings.cs), a dictionary in tests.
internal interface ISettingsStore
{
    bool ContainsKey(string key);
    T Get<T>(string key, T defaultValue);
    void Set<T>(string key, T value);
}

// A store in memory, for tests.
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out object value) && value is T typed ? typed : defaultValue;

    public void Set<T>(string key, T value) => _values[key] = value;
}

// The chat page's settings, typed, with their keys and defaults in one place. Reading or writing never throws: a store that fails (no
// preferences available, e.g. in a preview) gives the default and forgets the value. The keys are the ones the app has always used, so
// saved settings carry over.
internal sealed class ChatSettings
{
    private readonly ISettingsStore _store;
    private readonly string _platform;
    private readonly bool _gpuByDefault;

    // platform: the device platform's name - the GPU choice is kept per platform; gpuByDefault: "Use the GPU" before the user chose.
    public ChatSettings(ISettingsStore store, string platform, bool gpuByDefault)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _platform = platform ?? string.Empty;
        _gpuByDefault = gpuByDefault;
    }

    private T Get<T>(string key, T defaultValue)
    {
        try { return _store.Get(key, defaultValue); }
        catch (Exception) { return defaultValue; }
    }

    private void Set<T>(string key, T value)
    {
        try { _store.Set(key, value); }
        catch (Exception) { }
    }

    private bool? GetOptional(string key)
    {
        try { return _store.ContainsKey(key) ? _store.Get(key, false) : (bool?)null; }
        catch (Exception) { return null; }
    }

    // ---- The message box ----

    // The Think chip (on by default: thinking first gives better answers).
    public bool Think { get => Get("litert_lm_chat_think", true); set => Set("litert_lm_chat_think", value); }

    // The Web search chip.
    public bool WebSearch { get => Get("litert_lm_chat_web_search", false); set => Set("litert_lm_chat_web_search", value); }

    // ---- The model ----

    // The model chosen last (a ModelChoice name), or null.
    public string LastModel { get => Get<string>("litert_lm_chat_model", null); set => Set("litert_lm_chat_model", value); }

    // "Use the GPU", kept per platform; before it was per platform it was one setting, which is still read as the default.
    public bool UseGpu
    {
        get => Get(GpuKey, Get("litert_lm_chat_use_gpu", _gpuByDefault));
        set => Set(GpuKey, value);
    }

    private string GpuKey => "litert_lm_chat_use_gpu_" + _platform;

    // Settings > Context window: 0 for Automatic, else the size in tokens.
    public int ContextTokens { get => Get("litert_lm_chat_context_tokens", 0); set => Set("litert_lm_chat_context_tokens", value); }

    // The model list's sections: all models, and the CPU / GPU groups (null until the user opened or closed them).
    public bool ModelsExpanded { get => Get("litert_lm_chat_models_expanded", true); set => Set("litert_lm_chat_models_expanded", value); }
    public bool? CpuModelsExpanded { get => GetOptional("litert_lm_chat_cpu_models_expanded"); set => Set("litert_lm_chat_cpu_models_expanded", value ?? false); }
    public bool? GpuModelsExpanded { get => GetOptional("litert_lm_chat_gpu_models_expanded"); set => Set("litert_lm_chat_gpu_models_expanded", value ?? false); }

    // ---- The search model ----

    // Settings > Embedding model: 1 Text Vision, 2 Multimodal (the old text-only 0 reads as 1).
    public int EmbeddingModel
    {
        get => Get("litert_lm_chat_embedding_model", 1) == 2 ? 2 : 1;
        set => Set("litert_lm_chat_embedding_model", value);
    }

    // The search model on Windows' GPU (WebGPU builds only).
    public bool EmbeddingGpu { get => Get("litert_lm_chat_embedding_windows_gpu", false); set => Set("litert_lm_chat_embedding_windows_gpu", value); }

    // The search model file the user deleted in Settings > Storage (it isn't downloaded again until they ask).
    public bool SearchModelDeleted(string modelFile) => Get("litert_lm_chat_search_deleted_" + modelFile, false);
    public void SetSearchModelDeleted(string modelFile, bool deleted) => Set("litert_lm_chat_search_deleted_" + modelFile, deleted);

    // ---- Settings sheet ----

    public string VoiceLanguage { get => Get("litert_lm_chat_voice_language", string.Empty); set => Set("litert_lm_chat_voice_language", value ?? string.Empty); }
    public bool ShareLocation { get => Get("litert_lm_chat_share_location", false); set => Set("litert_lm_chat_share_location", value); }
    public bool Memory { get => Get("litert_lm_chat_memory", true); set => Set("litert_lm_chat_memory", value); }
    public bool PhotoSearch { get => Get("litert_lm_chat_photo_search", false); set => Set("litert_lm_chat_photo_search", value); }
    public bool PhotosOnlyWhileCharging { get => Get("litert_lm_photos_only_charging", false); set => Set("litert_lm_photos_only_charging", value); }
    public bool PastChatSearch { get => Get("litert_lm_chat_search_chats", false); set => Set("litert_lm_chat_search_chats", value); }
    public bool ImageGeneration { get => Get("litert_lm_chat_image_generation", false); set => Set("litert_lm_chat_image_generation", value); }
    public bool CameraSound { get => Get("litert_lm_chat_camera_sound", true); set => Set("litert_lm_chat_camera_sound", value); }
    public bool CountAloud { get => Get("litert_lm_chat_count_aloud", true); set => Set("litert_lm_chat_count_aloud", value); }
    public bool DeveloperMode { get => Get("litert_lm_chat_developer_mode", false); set => Set("litert_lm_chat_developer_mode", value); }

    // Your own server: its address and model.
    public string ServerUrl { get => Get("litert_lm_chat_server_url", string.Empty); set => Set("litert_lm_chat_server_url", value ?? string.Empty); }
    public string ServerModel { get => Get("litert_lm_chat_server_model", string.Empty); set => Set("litert_lm_chat_server_model", value ?? string.Empty); }

    // Tool servers (MCP), as JSON.
    public string McpServersJson { get => Get("litert_lm_chat_mcp_servers", "[]"); set => Set("litert_lm_chat_mcp_servers", value ?? "[]"); }
}
