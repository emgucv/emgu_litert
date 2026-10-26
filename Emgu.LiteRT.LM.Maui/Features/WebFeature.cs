//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents.Skills;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// The web (<see cref="WebSkill"/>: the weather, Wikipedia, currency conversion, reading pages; with a Tavily API key,
    /// web search): a "Web search" chip above the message box (off by default; kept as "web_search"), and a settings
    /// section to enter the key, which is kept in the platform's secure storage (<see cref="KeyStorageName"/>). The
    /// app declares internet access (Android's INTERNET permission).
    /// </summary>
    public sealed class WebFeature : SkillFeature<WebSkill>
    {
        /// <summary>The secure storage name of the Tavily API key: "tavily_api_key".</summary>
        public const string KeyStorageName = "tavily_api_key";

        private const string Key = "web_search";
        private Entry _keyEntry;
        private Label _keyStatus;
        private Button _saveButton;
        private string _savedKey;
        private readonly SemaphoreSlim _saveGate = new SemaphoreSlim(1, 1);

        /// <summary>Create the feature.</summary>
        public WebFeature() : base(new WebSkill())
        {
        }

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            Enabled = host.Settings.Get(Key, false);
            Skill.SettingsLocation = host.SettingsLocation + " > Web search";
            _ = LoadKeyAsync();
        }

        /// <inheritdoc/>
        public override ChatChip BuildComposerChip() => new ChatChip("Web search", () => Enabled, on =>
        {
            Enabled = on;
            Host?.Settings.Set(Key, on);
            // Applies from the next message; the conversation continues.
            if (Host != null && !Host.IsReplying)
                Host.SetStatus(!on ? null : string.IsNullOrEmpty(Skill.TavilyApiKey)
                    ? "Web search requires a Tavily API key in " + Skill.SettingsLocation + ". Wikipedia and other keyless tools are available."
                    : "Web search on: Wikipedia and the web.");
        })
        { IsVisible = () => Host?.Model?.CanUseTools == true };

        /// <inheritdoc/>
        public override View BuildSettingsSection()
        {
            _keyEntry = new Entry { Placeholder = "Tavily API key (required for web search)", IsEnabled = _savedKey != null, IsPassword = true, Text = Skill.TavilyApiKey };
            ChatResources.SetThemeColor(_keyEntry, Entry.TextColorProperty, "ChatPrimaryText");
            ChatResources.SetThemeColor(_keyEntry, Entry.PlaceholderColorProperty, "ChatSecondaryText");
            _keyStatus = ChatPage.Note(_savedKey == null ? "Loading saved API key..." : _savedKey.Length > 0 ? "API key loaded from secure storage." : "No API key saved.");
            _saveButton = new Button { Text = "Save API key", IsEnabled = _savedKey != null };
            ChatResources.ApplyStyle(_saveButton, "ChatStopButtonStyle");
            _saveButton.Clicked += (s, e) => _ = SaveKeyAsync();
            _keyEntry.Completed += (s, e) => _ = SaveKeyAsync();
            _keyEntry.Unfocused += (s, e) => _ = SaveKeyAsync();
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    ChatPage.SectionTitle("Web search"),
                    ChatPage.Note("Searching the web for news and recent events requires a Tavily API key from tavily.com. With \U0001F50E Web search on, Wikipedia, reading links you provide, weather and currency conversion work without a key. Your searches leave the device."),
                    _keyEntry,
                    _saveButton,
                    _keyStatus
                }
            };
        }

        private async Task LoadKeyAsync()
        {
            try
            {
                string key = await SecureStorage.Default.GetAsync(KeyStorageName) ?? string.Empty;
                Skill.TavilyApiKey = key;
                _savedKey = key;
                if (_keyEntry != null)
                    _keyEntry.Text = key;
                if (_keyStatus != null)
                    _keyStatus.Text = key.Length > 0 ? "API key loaded from secure storage." : "No API key saved.";
            }
            catch (Exception)
            {
                _savedKey = string.Empty;
                if (_keyStatus != null)
                    _keyStatus.Text = "Could not load the saved API key from secure storage. Enter it again and select Save API key to retry.";
            }
            if (_keyEntry != null)
                _keyEntry.IsEnabled = true;
            if (_saveButton != null)
                _saveButton.IsEnabled = true;
        }

        private async Task SaveKeyAsync()
        {
            if (_keyEntry == null)
                return;
            await _saveGate.WaitAsync();
            try
            {
                string key = _keyEntry.Text?.Trim() ?? string.Empty;
                if (key == _savedKey)
                    return;
                Skill.TavilyApiKey = key;
                _saveButton.IsEnabled = false;
                _keyStatus.Text = "Saving API key to secure storage...";
                if (key.Length > 0)
                    await SecureStorage.Default.SetAsync(KeyStorageName, key);
                else if (!SecureStorage.Default.Remove(KeyStorageName) && !string.IsNullOrEmpty(_savedKey))
                    throw new InvalidOperationException("Secure storage removal failed.");
                // Read back before reporting success: some platform keychain failures don't throw.
                string stored = await SecureStorage.Default.GetAsync(KeyStorageName) ?? string.Empty;
                if (!string.Equals(stored, key, StringComparison.Ordinal))
                    throw new InvalidOperationException("Secure storage verification failed.");
                _savedKey = key;
                _keyStatus.Text = key.Length > 0 ? "API key saved securely." : "API key removed from secure storage.";
            }
            catch (Exception)
            {
                // Never put the key or the exception in the UI or logs.
                _savedKey = null;
                _keyStatus.Text = Skill.TavilyApiKey?.Length > 0
                    ? "API key could not be saved securely. It is available for this session only. Select Save API key to retry."
                    : "Could not confirm removal from secure storage. A previously saved key may return after restarting. Select Save API key to retry.";
#if MACCATALYST
                _keyStatus.Text += " This Mac build needs code signing and a provisioning profile with Keychain access enabled.";
#endif
            }
            finally
            {
                _saveButton.IsEnabled = true;
                _saveGate.Release();
            }
        }
    }
}
