// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// The demo's settings for Emgu.LiteRT.LM.Maui's ChatPage and its features: the keys the demo has always used
// ("litert_lm_chat_" + the page's short names), with two old keys read as defaults so saved settings carry over - the
// single "litert_lm_chat_use_gpu" before the GPU choice was kept per platform, and "litert_lm_photos_only_charging".
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
        if (key == "photos_only_charging" && !_settings.Contains(key) && defaultValue is bool chargingDefault)
        {
            // The demo kept "Index photos only while charging" under its own key, without the chat prefix.
            try { return (T)(object)Preferences.Default.Get("litert_lm_photos_only_charging", chargingDefault); }
            catch (System.Exception) { return defaultValue; }
        }
        return _settings.Get(key, defaultValue);
    }

    public void Set<T>(string key, T value) => _settings.Set(key, value);

    public bool Contains(string key) => _settings.Contains(key);
}
