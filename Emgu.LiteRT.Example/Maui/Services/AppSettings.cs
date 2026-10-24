// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
namespace Maui.Demo.Lite;

// The app's settings, kept in MAUI's Preferences.
internal static class AppSettings
{
    // The chat page's settings.
    public static ChatSettings Chat { get; } = new ChatSettings(new PreferencesStore(), DeviceInfo.Platform.ToString(),
        DeviceInfo.Platform != DevicePlatform.WinUI);

    private sealed class PreferencesStore : ISettingsStore
    {
        public bool ContainsKey(string key) => Preferences.Default.ContainsKey(key);

        public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);

        public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);
    }
}
