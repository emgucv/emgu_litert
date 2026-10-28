//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Skills;
using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>
    /// The web (<see cref="WebSkill"/>: the weather, Wikipedia, currency conversion, reading pages; with a Tavily API key,
    /// web search; with a Twelve Data API key, stock prices): a "Web search" chip above the message box (off by default;
    /// kept as "web_search"), and a settings section to enter the keys, which are kept in the platform's secure storage
    /// (<see cref="KeyStorageName"/>, <see cref="StockKeyStorageName"/>). The app declares internet access (Android's
    /// INTERNET permission).
    /// </summary>
    public sealed class WebFeature : SkillFeature<WebSkill>
    {
        /// <summary>The secure storage name of the Tavily API key: "tavily_api_key".</summary>
        public const string KeyStorageName = "tavily_api_key";

        /// <summary>The secure storage name of the Twelve Data API key (stock prices): "twelvedata_api_key".</summary>
        public const string StockKeyStorageName = "twelvedata_api_key";

        /// <summary>The kind of the saved file a stock chart is kept as with its reply (its <see cref="StockChart"/> as JSON).</summary>
        public const string StockChartKind = "stock_chart";

        private const string Key = "web_search";
        private readonly ApiKeyRow _searchKey;
        private readonly ApiKeyRow _stockKey;

        /// <summary>Create the feature.</summary>
        public WebFeature() : base(new WebSkill())
        {
            _searchKey = new ApiKeyRow(KeyStorageName, "Tavily API key (required for web search)", key =>
            {
                Skill.TavilyApiKey = key;
                MainThread.BeginInvokeOnMainThread(() => Host?.RefreshChips());
            });
            _stockKey = new ApiKeyRow(StockKeyStorageName, "Twelve Data API key (for stock prices)", key => Skill.TwelveDataApiKey = key);
        }

        /// <inheritdoc/>
        public override bool PersistEnabled => false;

        /// <inheritdoc/>
        public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

        /// <summary>Whether a Tavily API key is set (general web search is available).</summary>
        public bool HasSearchKey => !string.IsNullOrWhiteSpace(Skill.TavilyApiKey);

        /// <summary>Turn web search on, as the chip does (e.g. for research mode, which needs it).</summary>
        public void TurnOn()
        {
            if (Enabled)
                return;
            Enabled = true;
            Host?.Settings.Set(Key, true);
            Host?.RefreshChips();
        }

        // The charts shown above the reply (by their data), so the same one isn't shown twice.
        private object _chartsReply;
        private readonly HashSet<string> _chartsShown = new HashSet<string>();

        /// <inheritdoc/>
        public override void OnAttached(IChatPageHost host)
        {
            base.OnAttached(host);
            Enabled = host.Settings.Get(Key, false);
            Skill.SettingsLocation = host.SettingsLocation + " > Web search";
            // A stock tool's prices: a chart above the reply, kept with it in the saved chat - once per reply: Gemma 4 E2B
            // fetched AMD's week twice in one research part on the Fold, and the reply showed two identical charts.
            Skill.StockChartReady = chart => MainThread.BeginInvokeOnMainThread(() =>
            {
                byte[] json = chart.ToJson();
                object reply = Host?.Conversation?.Messages.LastOrDefault();
                string key = Convert.ToBase64String(json);
                if (!ReferenceEquals(reply, _chartsReply))
                {
                    _chartsReply = reply;
                    _chartsShown.Clear();
                }
                if (_chartsShown.Add(key))
                    Host?.AddCardToReply(StockChartView.Create(chart), new ChatSavedFile(StockChartKind, "application/json", json));
            });
            _ = LoadKeysAsync();
        }

        /// <inheritdoc/>
        public override View RestoreSavedFile(ChatSavedFile file)
        {
            if (file?.Kind != StockChartKind)
                return null;
            StockChart chart = StockChart.FromJson(file.Data);
            return chart != null ? StockChartView.Create(chart) : null;
        }

        // The keys from secure storage; the chips follow (research needs the search key).
        private async Task LoadKeysAsync()
        {
            await Task.WhenAll(_searchKey.LoadAsync(), _stockKey.LoadAsync());
            MainThread.BeginInvokeOnMainThread(() => Host?.RefreshChips());
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
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    ChatPage.SectionTitle("Web search"),
                    ChatPage.Note("Searching the web for news and recent events requires a Tavily API key from tavily.com. With \U0001F50E Web search on, Wikipedia, reading links you provide, weather and currency conversion work without a key. Your searches leave the device."),
                    _searchKey.Build(),
                    ChatPage.Note("Stock prices and their history (\"how did Apple do this year?\") need a Twelve Data API key, free at twelvedata.com for 800 requests a day. Prices on the free plan may be delayed."),
                    _stockKey.Build()
                }
            };
        }

        // An API key's entry, Save button and status line, kept in the platform's secure storage.
        private sealed class ApiKeyRow
        {
            private readonly string _storageName;
            private readonly string _placeholder;
            private readonly Action<string> _apply;
            private readonly SemaphoreSlim _saveGate = new SemaphoreSlim(1, 1);
            private Entry _entry;
            private Label _status;
            private Button _save;
            private string _saved;
            private string _current;

            public ApiKeyRow(string storageName, string placeholder, Action<string> apply)
            {
                _storageName = storageName;
                _placeholder = placeholder;
                _apply = apply;
            }

            public View Build()
            {
                // No SemanticProperties.Description on this entry: on Android it went into the field's accessibility text
                // together with the key itself, unmasked (checked on a Pixel Fold); the placeholder already names it.
                _entry = new Entry { Placeholder = _placeholder, IsEnabled = _saved != null, IsPassword = true, Text = _current };
                ChatResources.SetThemeColor(_entry, Entry.TextColorProperty, "ChatPrimaryText");
                ChatResources.SetThemeColor(_entry, Entry.PlaceholderColorProperty, "ChatSecondaryText");
                _status = ChatPage.Note(_saved == null ? "Loading saved API key..." : _saved.Length > 0 ? "API key loaded from secure storage." : "No API key saved.");
                _save = new Button { Text = "Save API key", IsEnabled = _saved != null };
                ChatResources.ApplyStyle(_save, "ChatStopButtonStyle");
                _save.Clicked += (s, e) => _ = SaveAsync();
                _entry.Completed += (s, e) => _ = SaveAsync();
                _entry.Unfocused += (s, e) => _ = SaveAsync();
                return new VerticalStackLayout { Spacing = 6, Children = { _entry, _save, _status } };
            }

            public async Task LoadAsync()
            {
                try
                {
                    string key = await SecureStorage.Default.GetAsync(_storageName) ?? string.Empty;
                    _current = key;
                    _apply(key);
                    _saved = key;
                    if (_entry != null)
                        _entry.Text = key;
                    if (_status != null)
                        _status.Text = key.Length > 0 ? "API key loaded from secure storage." : "No API key saved.";
                }
                catch (Exception)
                {
                    _saved = string.Empty;
                    if (_status != null)
                        _status.Text = "Could not load the saved API key from secure storage. Enter it again and select Save API key to retry.";
                }
                if (_entry != null)
                    _entry.IsEnabled = true;
                if (_save != null)
                    _save.IsEnabled = true;
            }

            private async Task SaveAsync()
            {
                if (_entry == null)
                    return;
                await _saveGate.WaitAsync();
                try
                {
                    string key = _entry.Text?.Trim() ?? string.Empty;
                    if (key == _saved)
                        return;
                    _current = key;
                    _apply(key);
                    _save.IsEnabled = false;
                    _status.Text = "Saving API key to secure storage...";
                    if (key.Length > 0)
                        await SecureStorage.Default.SetAsync(_storageName, key);
                    else if (!SecureStorage.Default.Remove(_storageName) && !string.IsNullOrEmpty(_saved))
                        throw new InvalidOperationException("Secure storage removal failed.");
                    // Read back before reporting success: some platform keychain failures don't throw.
                    string stored = await SecureStorage.Default.GetAsync(_storageName) ?? string.Empty;
                    if (!string.Equals(stored, key, StringComparison.Ordinal))
                        throw new InvalidOperationException("Secure storage verification failed.");
                    _saved = key;
                    _status.Text = key.Length > 0 ? "API key saved securely." : "API key removed from secure storage.";
                }
                catch (Exception)
                {
                    // Never put the key or the exception in the UI or logs.
                    _saved = null;
                    _status.Text = _current?.Length > 0
                        ? "API key could not be saved securely. It is available for this session only. Select Save API key to retry."
                        : "Could not confirm removal from secure storage. A previously saved key may return after restarting. Select Save API key to retry.";
#if MACCATALYST
                    _status.Text += " This Mac build needs code signing and a provisioning profile with Keychain access enabled.";
#endif
                }
                finally
                {
                    _save.IsEnabled = true;
                    _saveGate.Release();
                }
            }
        }
    }
}
