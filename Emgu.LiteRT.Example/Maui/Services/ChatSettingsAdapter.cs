// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The demo's settings for Emgu.LiteRT.LM.Maui's ChatPage: the keys the demo has always used ("litert_lm_chat_" + the
// page's short names, the same ones ChatSettings reads for the features), and the GPU default ChatSettings has - the
// legacy "litert_lm_chat_use_gpu" key, then the GPU everywhere but Windows.
internal sealed class ChatSettingsAdapter : IChatPageSettings
{
    private const string Prefix = "litert_lm_chat_";
    private readonly PreferencesChatPageSettings _settings = new PreferencesChatPageSettings(Prefix);

    public T Get<T>(string key, T defaultValue)
    {
        if (key.StartsWith("use_gpu_", System.StringComparison.Ordinal) && !_settings.Contains(key) && defaultValue is bool gpuDefault)
        {
            // Before it was kept per platform, "Use the GPU" was one setting, which is still read as the default.
            bool legacy = _settings.Get("use_gpu", gpuDefault);
            return (T)(object)legacy;
        }
        return _settings.Get(key, defaultValue);
    }

    public void Set<T>(string key, T value) => _settings.Set(key, value);

    public bool Contains(string key) => _settings.Contains(key);
}
