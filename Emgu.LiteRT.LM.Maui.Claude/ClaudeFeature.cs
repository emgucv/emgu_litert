// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Anthropic.Models.Models;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.Claude;

/// <summary>
/// Anthropic's Claude models through the Claude API (Anthropic's C# SDK and its IChatClient), in place of a model on the
/// device: the remote "Claude" option of the model list (<see cref="Option"/>, which the app adds to the page's models).
/// It chats through the same agent as the models on the device, with the same tools, saved chats and compaction - the
/// token counts estimated, since there is no tokenizer on the device. The settings' "Claude" section takes an API key from
/// the Claude Console (kept in SecureStorage) and one of the models the key can use ("claude_model"); the model list gives
/// each model's context window, longest reply, whether it takes images and which kinds of thinking it accepts. The Think
/// chip asks for Claude's thinking, shown as a local model's; a model that can't stop thinking gets the lowest effort for
/// "no thinking" instead of a request it would refuse. Each request asks the API to cache the conversation so far, so a
/// long chat costs less per message. Claude doesn't take audio: voice messages are written down by the device's speech
/// recognizer first.
/// </summary>
public sealed class ClaudeFeature : ChatFeature, IRemoteChatModel
{
    /// <summary>The id of the option made when none is given.</summary>
    public const string DefaultId = "Claude";

    /// <summary>The settings key of the model chosen.</summary>
    public const string ModelSettingKey = "claude_model";

    /// <summary>The settings key of the chosen model's name ("Claude Sonnet 5.5"), shown before the model list is fetched.</summary>
    public const string ModelNameSettingKey = "claude_model_name";

    /// <summary>The model chosen until the user picks one, when the key can use it.</summary>
    public const string DefaultModel = "claude-sonnet-5-5";

    private const string KeyStorageName = "litert_lm_chat_claude_key";
    // Until the model list says (the smallest window of the current models).
    private const int DefaultContextTokens = 200000;
    // A reply's limit: room for thinking and a long answer, within what every current model allows.
    private const int ReplyMaxTokens = 16000;
    private const int SummaryMaxTokens = 1200;

    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private string _modelName = string.Empty;
    private ModelInfo _modelInfo;
    private List<ModelInfo> _models;

    private Entry _keyEntry;
    private Button _modelButton;
    private Label _status;

    /// <summary>Create the feature, for a remote option of the app's or one of its own.</summary>
    /// <param name="option">The remote option of the model list this feature connects (its <see cref="ChatModelOption.Remote"/> is set); null for a new "Claude" option with the <see cref="DefaultId"/></param>
    public ClaudeFeature(ChatModelOption option = null)
    {
        if (option != null && !option.IsRemote)
            throw new ArgumentException("Claude's option must be a remote one (no model on the device)", nameof(option));
        Option = option ?? new ChatModelOption(DefaultId, "Claude")
        {
            CanUseTools = true,
            CanSummarize = true,
            AcceptsImages = true,
            ContextWindow = DefaultContextTokens
        };
        Option.Remote = this;
        UpdateOptionDetail();
    }

    /// <inheritdoc/>
    public override string Name => "claude";

    /// <inheritdoc/>
    public override string Title => "Claude";

    /// <inheritdoc/>
    public override bool PersistEnabled => false;

    /// <summary>The remote option of the model list this feature connects: add it to the page's models.</summary>
    public ChatModelOption Option { get; }

    /// <summary>True while Claude is the model chosen.</summary>
    public bool IsSelected => Host?.Model == Option;

    /// <summary>The model chosen (its id), or empty until one is.</summary>
    public string Model => _model;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        _model = host.Settings.Get(ModelSettingKey, string.Empty) ?? string.Empty;
        _modelName = host.Settings.Get(ModelNameSettingKey, string.Empty) ?? string.Empty;
        UpdateOptionDetail();
        _ = LoadKeyAsync();
    }

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        _keyEntry = new Entry { Text = _apiKey, Placeholder = "API key (from the Claude Console)", IsPassword = true, FontSize = 14 };
        _keyEntry.Completed += (s, e) => _ = ApplyKeyAsync();
        var connect = new Button { Text = "Connect" };
        connect.Clicked += (s, e) => _ = ApplyKeyAsync(true);
        _modelButton = new Button { Text = ModelButtonText() };
        _modelButton.Clicked += OnModelTapped;
        _status = new Label();
        ChatResources.ApplyStyle(_status, "ChatSectionNoteStyle");
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                ChatPage.SectionTitle("Claude"),
                ChatPage.Note("Chat with Anthropic's Claude instead of a model on this device, with an API key from the Claude " +
                    "Console (platform.claude.com > API keys); what you use is billed to that account. Then pick \"" +
                    Option.DisplayName + "\" in the model list. Your messages, pictures and the tools' results go to Anthropic."),
                _keyEntry,
                connect,
                _modelButton,
                _status
            }
        };
    }

    private string ModelButtonText() => "Model: " + (string.IsNullOrEmpty(_model) ? "choose after connecting" : ModelName(_model));

    // The model's name: from the model list, else the one saved when it was chosen, else its id.
    private string ModelName(string id) => _models?.FirstOrDefault(m => m.ID == id)?.DisplayName
        ?? (id == _model && !string.IsNullOrEmpty(_modelName) ? _modelName : id);

    private async Task LoadKeyAsync()
    {
        try { _apiKey = await SecureStorage.Default.GetAsync(KeyStorageName) ?? string.Empty; }
        catch (Exception) { _apiKey = string.Empty; }
        if (_keyEntry != null)
            _keyEntry.Text = _apiKey;
        UpdateOptionDetail();
    }

    // Save the key; if it changed (or `force`), list the models it can use and reconnect the chat.
    private async Task ApplyKeyAsync(bool force = false)
    {
        string key = _keyEntry.Text?.Trim() ?? string.Empty;
        if (!force && key == _apiKey)
            return;
        if (key != _apiKey)
        {
            _apiKey = key;
            _models = null;
            try
            {
                if (key.Length > 0) await SecureStorage.Default.SetAsync(KeyStorageName, key);
                else SecureStorage.Default.Remove(KeyStorageName);
            }
            catch (Exception) { }
        }
        UpdateOptionDetail();
        if (key.Length == 0)
        {
            _status.Text = "Enter your API key.";
            return;
        }
        _status.Text = "Connecting...";
        try
        {
            List<ModelInfo> models = await ListModelsAsync(CancellationToken.None);
            ChooseModel(models);
            _status.Text = string.Format("Connected: {0} model{1}.", models.Count, models.Count == 1 ? "" : "s");
        }
        catch (Exception ex)
        {
            _status.Text = "Could not connect: " + Problem(ex);
            return;
        }
        Reconnect();
    }

    // The settings changed: the next message connects again (a conversation in progress is given to the model then).
    private void Reconnect()
    {
        UpdateOptionDetail();
        Host.ResetRemote();
        Host.RefreshModelList();
    }

    private void UpdateOptionDetail()
    {
        Option.Detail = string.IsNullOrEmpty(_apiKey)
            ? "Not set up: add your API key in Settings > Claude."
            : (string.IsNullOrEmpty(_model) ? "" : ModelName(_model) + " · ") + "Anthropic's Claude through the Claude API.";
    }

    private async void OnModelTapped(object sender, EventArgs e)
    {
        if (Host.IsBusy)
            return;
        if (string.IsNullOrEmpty(_apiKey))
        {
            _status.Text = "Enter your API key first.";
            return;
        }
        List<ModelInfo> models;
        try { models = await ListModelsAsync(CancellationToken.None); }
        catch (Exception ex)
        {
            _status.Text = "Could not connect: " + Problem(ex);
            return;
        }
        // The page's sheet, a row per model: the system's action sheet stacked a long list of plain buttons on Mac Catalyst.
        string picked = await Host.ChooseAsync("Claude model", models.Select(m => new ChatChoice(m.ID, m.DisplayName, ModelDetail(m))), _model);
        ModelInfo chosen = models.FirstOrDefault(m => m.ID == picked);
        if (chosen == null || chosen.ID == _model || Host.IsBusy)
            return;
        SetModel(chosen.ID);
        // A chat belongs to a model: another model starts a new chat.
        if (IsSelected)
            Host.StartNewChat();
        Reconnect();
    }

    // "1M context · images · released 2026-08" (what the model list says of it).
    private static string ModelDetail(ModelInfo model)
    {
        var parts = new List<string>();
        if (model.MaxInputTokens is long input && input > 0)
            parts.Add((input >= 1000000 ? (input / 1000000.0).ToString("0.#") + "M" : (input / 1000) + "K") + " context");
        if (model.Capabilities?.ImageInput?.Supported == true)
            parts.Add("images");
        if (model.CreatedAt.Year > 2000)
            parts.Add("released " + model.CreatedAt.ToString("yyyy-MM"));
        parts.Add(model.ID);
        return string.Join(" · ", parts);
    }

    private void SaveModelName()
    {
        string name = _models?.FirstOrDefault(m => m.ID == _model)?.DisplayName;
        if (name == null || name == _modelName) return;
        _modelName = name;
        Host?.Settings.Set(ModelNameSettingKey, name);
    }

    private void SetModel(string id)
    {
        _model = id ?? string.Empty;
        Host.Settings.Set(ModelSettingKey, _model);
        SaveModelName();
        if (_modelButton != null)
            _modelButton.Text = ModelButtonText();
        UpdateOptionDetail();
    }

    // Keep the chosen model if the key can use it, else the default, else the newest.
    private void ChooseModel(List<ModelInfo> models)
    {
        if (models.Count == 0)
            throw new InvalidOperationException("this key can't use any model");
        if (!models.Any(m => m.ID == _model))
            SetModel((models.FirstOrDefault(m => m.ID == DefaultModel) ?? models[0]).ID);
    }

    private AnthropicClient NewClient() => new AnthropicClient { ApiKey = _apiKey };

    // The models the key can use, newest first, without retired ones.
    private async Task<List<ModelInfo>> ListModelsAsync(CancellationToken token)
    {
        if (_models != null)
            return _models;
        var models = new List<ModelInfo>();
        AnthropicClient client = NewClient();
        ModelListPage page = await client.Models.List(new ModelListParams { Limit = 100 }, token);
        await foreach (ModelInfo model in page.Paginate().WithCancellation(token))
            models.Add(model);
        _models = models.Where(m => m.RetiresAt == null || m.RetiresAt > DateTimeOffset.UtcNow)
            .ToList();
        // The model's name, now that the list says it ("Claude Sonnet 5.5" rather than its id).
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SaveModelName();
            if (_modelButton != null) _modelButton.Text = ModelButtonText();
            UpdateOptionDetail();
        });
        return _models;
    }

    // An API error in words for the user.
    private static string Problem(Exception ex) => ex switch
    {
        AnthropicUnauthorizedException => "the API key was not accepted. Check it in Settings > Claude.",
        AnthropicForbiddenException => "this API key isn't allowed to do that (" + ex.Message + ").",
        AnthropicRateLimitException => "too many requests or the account's credit is used up; try again later.",
        AnthropicIOException => "no connection to Anthropic (" + ex.Message + ").",
        _ => ex.Message
    };

    // ---------- IRemoteChatModel ----------

    /// <summary>
    /// Check that the key works and can use the model, and learn what the model takes: its context window, whether it takes
    /// images and how it thinks (the page's connection before the first message).
    /// </summary>
    /// <param name="cancellationToken">Cancels it</param>
    /// <returns>Null when Claude is ready, else why it can't be reached</returns>
    public async Task<string> ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return "Add your Claude API key in Settings > Claude, then try again.";
        Host?.SetStatus("Connecting to Claude...");
        try
        {
            List<ModelInfo> models = await ListModelsAsync(cancellationToken);
            ChooseModel(models);
            ApplyModelInfo(models.First(m => m.ID == _model));
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
                return "The connection was cancelled.";
            System.Diagnostics.Debug.WriteLine("Claude connection failed: " + ex);
            return "Could not reach Claude: " + Problem(ex);
        }
        return cancellationToken.IsCancellationRequested ? "The connection was cancelled." : null;
    }

    private void ApplyModelInfo(ModelInfo model)
    {
        _modelInfo = model;
        Option.ContextWindow = model.MaxInputTokens is long input && input > 0 ? (int)Math.Min(input, int.MaxValue) : DefaultContextTokens;
        Option.AcceptsImages = model.Capabilities?.ImageInput?.Supported ?? true;
        Option.CanThink = model.Capabilities?.Thinking?.Supported ?? true;
        if (IsSelected)
            Host.ModelCapabilitiesChanged();
    }

    private bool Adaptive => _modelInfo?.Capabilities?.Thinking?.Types?.Adaptive?.Supported ?? true;

    private bool CanStopThinking => _modelInfo?.Capabilities?.Thinking?.Types?.Disabled?.Supported ?? false;

    private int ReplyLimit => _modelInfo?.MaxTokens is long most && most > 0 ? (int)Math.Min(ReplyMaxTokens, most) : ReplyMaxTokens;

    /// <summary>The client for the chosen model (the page adds function invocation).</summary>
    public IChatClient CreateChatClient() =>
        new ClaudeRequests(NewClient().AsIChatClient(_model, ReplyLimit, Adaptive ? AnthropicThinkingMode.Adaptive : AnthropicThinkingMode.Extended), this);

    /// <inheritdoc/>
    public void AdjustOptions(ChatOptions options) => Adjust(options);

    /// <inheritdoc/>
    public bool ShouldResend(Exception error, ChatOptions options) => false;

    // What every request to Claude carries: thinking it accepts, its text shown, and the conversation cached.
    private void Adjust(ChatOptions options)
    {
        if (options == null)
            return;
        // No sampling options: current models refuse a temperature or top_p other than their own.
        options.Temperature = null;
        options.TopP = null;
        if (options.Reasoning != null)
        {
            // "No thinking" from a model that can't stop: as little as it does.
            if (options.Reasoning.Effort == ReasoningEffort.None && !CanStopThinking)
                options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low };
            else if (options.Reasoning.Effort != ReasoningEffort.None)
                options.Reasoning = new ReasoningOptions { Effort = options.Reasoning.Effort, Output = ReasoningOutput.Summary };
        }
        // Everything up to the newest message cached: the next message reads it at a tenth of the price.
        Func<IChatClient, object> raw = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            object made = raw?.Invoke(client);
            if (made is MessageCreateParams parameters)
                return parameters with { CacheControl = new CacheControlEphemeral() };
            return made ?? new MessageCreateParams { CacheControl = new CacheControlEphemeral(), MaxTokens = ReplyLimit, Messages = [], Model = _model };
        };
    }

    /// <summary>The conversation's summary options: little thinking, room for a summary.</summary>
    public ChatOptions SummaryOptions => new ChatOptions
    {
        MaxOutputTokens = SummaryMaxTokens + 2000,
        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
    };

    /// <summary>One question to the chosen model in a request of its own, with little thinking; its answer.</summary>
    /// <param name="prompt">The question</param>
    /// <param name="maxTokens">The longest answer; 0 for the summaries' size</param>
    /// <param name="cancellationToken">Cancels it</param>
    public async Task<string> AskOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        using IChatClient client = CreateChatClient();
        ChatOptions options = SummaryOptions;
        if (maxTokens > 0)
            options.MaxOutputTokens = maxTokens + 2000;
        ChatResponse response = await client.GetResponseAsync(prompt, options, cancellationToken);
        string text = (response.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            throw new Exception("The answer is empty");
        return text;
    }

    // Every request through Adjust, whoever composed its options (the page, a feature's own request, a summary).
    private sealed class ClaudeRequests : DelegatingChatClient
    {
        private readonly ClaudeFeature _feature;

        public ClaudeRequests(IChatClient inner, ClaudeFeature feature) : base(inner) => _feature = feature;

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null,
            CancellationToken cancellationToken = default) => base.GetResponseAsync(messages, Prepared(options), cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions options = null, CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(messages, Prepared(options), cancellationToken);

        private ChatOptions Prepared(ChatOptions options)
        {
            ChatOptions copy = options?.Clone() ?? new ChatOptions();
            _feature.Adjust(copy);
            return copy;
        }
    }
}
