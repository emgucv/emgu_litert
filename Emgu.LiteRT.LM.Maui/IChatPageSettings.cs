//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Where a <see cref="ChatPage"/> and its features keep their settings (the Think toggle, the model, each feature's
    /// switches): <see cref="PreferencesChatPageSettings"/> over MAUI's Preferences by default. Keys are short names
    /// ("think", "memory", "feature_web"); a store may prefix them.
    /// </summary>
    public interface IChatPageSettings
    {
        /// <summary>A value, or the default if it isn't set (or can't be read).</summary>
        /// <typeparam name="T">bool, int, long, double, float, string or DateTime</typeparam>
        /// <param name="key">The key</param>
        /// <param name="defaultValue">The default</param>
        T Get<T>(string key, T defaultValue);

        /// <summary>Set a value (a failing store forgets it, without throwing).</summary>
        /// <typeparam name="T">bool, int, long, double, float, string or DateTime</typeparam>
        /// <param name="key">The key</param>
        /// <param name="value">The value</param>
        void Set<T>(string key, T value);

        /// <summary>Whether a value is set.</summary>
        /// <param name="key">The key</param>
        bool Contains(string key);
    }

    /// <summary>
    /// <see cref="IChatPageSettings"/> over MAUI's Preferences, with a prefix before each key: "litert_lm_chat_" by
    /// default, the keys the Emgu LiteRT MAUI demo has always used, so its users' settings carry over.
    /// </summary>
    public sealed class PreferencesChatPageSettings : IChatPageSettings
    {
        private readonly string _prefix;

        /// <summary>Create the store.</summary>
        /// <param name="prefix">What goes before each key</param>
        public PreferencesChatPageSettings(string prefix = "litert_lm_chat_")
        {
            _prefix = prefix ?? string.Empty;
        }

        /// <inheritdoc/>
        public T Get<T>(string key, T defaultValue)
        {
            try { return Preferences.Default.Get(_prefix + key, defaultValue); }
            catch (Exception) { return defaultValue; }
        }

        /// <inheritdoc/>
        public void Set<T>(string key, T value)
        {
            try { Preferences.Default.Set(_prefix + key, value); }
            catch (Exception) { }
        }

        /// <inheritdoc/>
        public bool Contains(string key)
        {
            try { return Preferences.Default.ContainsKey(_prefix + key); }
            catch (Exception) { return false; }
        }
    }

    /// <summary>An <see cref="IChatPageSettings"/> in memory (nothing is kept between runs), e.g. for previews.</summary>
    public sealed class InMemoryChatPageSettings : IChatPageSettings
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>();

        /// <inheritdoc/>
        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out object value) && value is T typed ? typed : defaultValue;

        /// <inheritdoc/>
        public void Set<T>(string key, T value) => _values[key] = value;

        /// <inheritdoc/>
        public bool Contains(string key) => _values.ContainsKey(key);
    }
}
