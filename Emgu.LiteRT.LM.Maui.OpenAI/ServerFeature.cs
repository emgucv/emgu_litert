// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Emgu.LiteRT.LM.Maui.OpenAI;

/// <summary>
/// A model on a server of the user's own - LM Studio, Ollama, llama.cpp's server, vLLM... - through its OpenAI-compatible
/// API (Microsoft.Extensions.AI.OpenAI's client), in place of a model on the device: the remote "Your own server"
/// option of the model list (<see cref="Option"/>, which the app adds to the page's models). It chats through the same
/// agent as the models on the device, with the same tools (the server's model must support tool calling), and the same
/// saved chats and compaction - with the token counts estimated, since there is no tokenizer on the device. The
/// settings' "Your own server" takes the address (kept in the page's settings as "server_url"), the API key (in
/// SecureStorage) and one of the server's models ("server_model"); the page reaches the server through the option's
/// <see cref="ChatModelOption.Remote"/>, which this feature is: it connects before the first message, makes the client,
/// drops the reasoning option a server rejected, and writes the summaries. A request that failed while the app was in
/// the background is sent again once it is in front.
/// </summary>
public sealed class ServerFeature : ChatFeature, IRemoteChatModel
{
    /// <summary>The id of the option made when none is given.</summary>
    public const string DefaultId = "Server";

    /// <summary>The settings key of the server's address.</summary>
    public const string UrlSettingKey = "server_url";

    /// <summary>The settings key of the model chosen on the server.</summary>
    public const string ModelSettingKey = "server_model";

    private const string ServerKeyStorageName = "litert_lm_chat_server_key";

    // Without a context length from the server, the conversation is kept within this (LM Studio's default).
    private const int DefaultServerContextTokens = 4096;
    private const int SummaryMaxTokens = 400;
    // Room for a server that thinks anyway (LM Studio runs Gemma 4 with thinking on unless told otherwise, and it used
    // up SummaryMaxTokens before any summary came out).
    private const int ThinkingRoomTokens = 1600;

    private static readonly HttpClient ServerHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    private string _serverUrl = string.Empty;
    private string _serverModel = string.Empty;
    private string _serverKey = string.Empty;
    // The server refused reasoning_effort (some OpenAI-compatible servers reject what they don't know): not sent to it again.
    private bool _serverRejectsReasoning;
    private string _serverRejectsReasoningUrl;
    private int _serverContextTokens = DefaultServerContextTokens;
    private bool _serverContextKnown;

    private Entry _serverUrlEntry;
    private Entry _serverKeyEntry;
    private Button _serverModelButton;
    private Label _serverStatus;

    // Whether the app is in front: a server request that failed while it wasn't is sent again once it is.
    private TaskCompletionSource<bool> _foreground = CompletedForeground();

    /// <summary>Create the feature, for a remote option of the app's or one of its own.</summary>
    /// <param name="option">The remote option of the model list this feature connects (its <see cref="ChatModelOption.Remote"/> is set); null for a new "Your own server" option with the <see cref="DefaultId"/></param>
    public ServerFeature(ChatModelOption option = null)
    {
        if (option != null && !option.IsRemote)
            throw new ArgumentException("The server's option must be a remote one (no model on the device)", nameof(option));
        Option = option ?? new ChatModelOption(DefaultId, "Your own server") { CanUseTools = true, CanSummarize = true };
        Option.Remote = this;
        UpdateOptionDetail();
    }

    /// <inheritdoc/>
    public override string Name => "server";

    /// <inheritdoc/>
    public override string Title => "Your own server";

    /// <inheritdoc/>
    public override bool PersistEnabled => false;

    /// <summary>The remote option of the model list this feature connects: add it to the page's models.</summary>
    public ChatModelOption Option { get; }

    /// <summary>True while the server is the model chosen.</summary>
    public bool IsSelected => Host?.Model == Option;

    /// <summary>The server refused reasoning_effort: messages go without it.</summary>
    public bool RejectsReasoning => _serverRejectsReasoning;

    /// <summary>The context length of the server's model (a default until the server says: <see cref="ContextKnown"/>).</summary>
    public int ContextTokens => _serverContextTokens;

    /// <summary>True once the server said its model's context length.</summary>
    public bool ContextKnown => _serverContextKnown;

    /// <summary>The model chosen on the server, or the option's name until one is.</summary>
    public string ModelLabel => string.IsNullOrEmpty(_serverModel) ? Option.DisplayName : _serverModel;

    /// <summary>The server's address as set up (empty until the user adds one).</summary>
    public string ServerUrl => _serverUrl;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        _serverUrl = host.Settings.Get(UrlSettingKey, string.Empty) ?? string.Empty;
        _serverModel = host.Settings.Get(ModelSettingKey, string.Empty) ?? string.Empty;
        UpdateOptionDetail();
        _ = LoadServerKeyAsync();
    }

    /// <inheritdoc/>
    public override void OnAppStopped()
    {
        if (_foreground.Task.IsCompleted)
            _foreground = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <inheritdoc/>
    public override void OnAppResumed() => _foreground.TrySetResult(true);

    /// <inheritdoc/>
    public override void OnReplyCompleted(ChatExchangeEventArgs e)
    {
        // LM Studio loads a model on its first request, with the context length it is set to: learn it then.
        _ = RefreshServerContextAsync();
    }

    /// <summary>The server's address as typed, made into the API's base address: "host:1234" gets "http://" and "/v1".</summary>
    /// <param name="text">The address as typed</param>
    public static string NormalizeServerUrl(string text)
    {
        string url = (text ?? string.Empty).Trim().TrimEnd('/');
        if (url.Length == 0)
            return url;
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "http://" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0))
            url += "/v1";
        return url;
    }

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        _serverUrlEntry = new Entry
        {
            Text = _serverUrl,
            Placeholder = "Server address, e.g. http://192.168.1.20:1234/v1",
            Keyboard = Keyboard.Url,
            FontSize = 14
        };
        _serverKeyEntry = new Entry
        {
            Text = _serverKey,
            Placeholder = "API key (if the server needs one)",
            IsPassword = true,
            FontSize = 14
        };
        _serverUrlEntry.Completed += (s, e) => _ = ApplyServerSettingsAsync();
        _serverKeyEntry.Completed += (s, e) => _ = ApplyServerSettingsAsync();
        var connect = new Button { Text = "Connect" };
        connect.Clicked += (s, e) => _ = ApplyServerSettingsAsync(true);
        _serverModelButton = new Button { Text = ModelButtonText() };
        _serverModelButton.Clicked += OnServerModelTapped;
        _serverStatus = new Label();
        ChatResources.ApplyStyle(_serverStatus, "ChatSectionNoteStyle");
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                ChatPage.SectionTitle("Your own server"),
                ChatPage.Note("Chat with a model on a computer of yours instead of this device: any server with an OpenAI-compatible " +
                    "API, such as LM Studio (turn on \"Serve on Local Network\") or Ollama. Then pick \"" + Option.DisplayName + "\" in the " +
                    "model list. Your messages, pictures and the tools' results go to that server. Tools need a model that " +
                    "supports tool calling."),
                _serverUrlEntry,
                _serverKeyEntry,
                connect,
                _serverModelButton,
                _serverStatus
            }
        };
    }

    private string ModelButtonText() => "Model: " + (string.IsNullOrEmpty(_serverModel) ? "choose after connecting" : _serverModel);

    private async Task LoadServerKeyAsync()
    {
        try { _serverKey = await SecureStorage.Default.GetAsync(ServerKeyStorageName) ?? string.Empty; }
        catch (Exception) { _serverKey = string.Empty; }
        if (_serverKeyEntry != null)
            _serverKeyEntry.Text = _serverKey;
    }

    // Save the address and key; if they changed (or `force`), check the server and reconnect the chat to it.
    private async Task ApplyServerSettingsAsync(bool force = false)
    {
        string url = NormalizeServerUrl(_serverUrlEntry.Text);
        string key = _serverKeyEntry.Text?.Trim() ?? string.Empty;
        if (!force && url == _serverUrl && key == _serverKey)
            return;
        _serverUrlEntry.Text = url;
        _serverUrl = url;
        Host.Settings.Set(UrlSettingKey, url);
        if (key != _serverKey)
        {
            _serverKey = key;
            try
            {
                if (key.Length > 0) await SecureStorage.Default.SetAsync(ServerKeyStorageName, key);
                else SecureStorage.Default.Remove(ServerKeyStorageName);
            }
            catch (Exception) { }
        }
        UpdateOptionDetail();
        if (url.Length == 0)
        {
            _serverStatus.Text = "Enter the server's address.";
            return;
        }
        _serverStatus.Text = "Connecting...";
        try
        {
            List<ServerModelInfo> models = await FetchServerModelsAsync(url, key, CancellationToken.None);
            ChooseServerModel(models);
            _serverStatus.Text = string.Format("Connected: {0} model{1}.", models.Count, models.Count == 1 ? "" : "s");
        }
        catch (Exception ex)
        {
            _serverStatus.Text = "Could not connect: " + ex.Message;
            return;
        }
        Reconnect();
    }

    // The settings changed: the next message connects again (a conversation in progress is given to the server then).
    private void Reconnect()
    {
        UpdateOptionDetail();
        Host.ResetRemote();
        Host.RefreshModelList();
    }

    // The model list's row for the server says what it is set up with.
    private void UpdateOptionDetail()
    {
        Option.Detail = string.IsNullOrEmpty(_serverUrl)
            ? "Not set up: add its address in Settings > Your own server."
            : (string.IsNullOrEmpty(_serverModel) ? "" : _serverModel + " · ") + _serverUrl +
              ". A model on your own server with an OpenAI-compatible API, such as LM Studio or Ollama.";
    }

    private async void OnServerModelTapped(object sender, EventArgs e)
    {
        if (Host.IsBusy)
            return;
        if (string.IsNullOrEmpty(_serverUrl))
        {
            _serverStatus.Text = "Enter the server's address first.";
            return;
        }
        List<ServerModelInfo> models;
        try { models = await FetchServerModelsAsync(_serverUrl, _serverKey, CancellationToken.None); }
        catch (Exception ex)
        {
            _serverStatus.Text = "Could not connect: " + ex.Message;
            return;
        }
        string[] names = models.Where(m => !m.IsEmbedding).Select(m => m.Id).ToArray();
        if (names.Length == 0)
        {
            _serverStatus.Text = "The server has no chat models.";
            return;
        }
        string picked = await Host.Page.DisplayActionSheetAsync("Model on your server", "Cancel", null, names);
        if (picked == null || !names.Contains(picked) || picked == _serverModel || Host.IsBusy)
            return;
        SetServerModel(picked);
        // A chat belongs to a model: another model starts a new chat.
        if (IsSelected)
            Host.StartNewChat();
        Reconnect();
    }

    private void SetServerModel(string id)
    {
        _serverModel = id ?? string.Empty;
        Host.Settings.Set(ModelSettingKey, _serverModel);
        if (_serverModelButton != null)
            _serverModelButton.Text = ModelButtonText();
        UpdateOptionDetail();
    }

    // Keep the chosen model if the server has it, else take the first chat model (a loaded one, if the server says).
    private void ChooseServerModel(List<ServerModelInfo> models)
    {
        List<ServerModelInfo> chat = models.Where(m => !m.IsEmbedding).ToList();
        if (chat.Count == 0)
            throw new InvalidOperationException("The server has no chat models.");
        if (!chat.Any(m => m.Id == _serverModel))
            SetServerModel((chat.FirstOrDefault(m => m.Loaded) ?? chat[0]).Id);
    }

    private sealed class ServerModelInfo
    {
        public string Id;
        public bool IsEmbedding;
        public bool Loaded;
        // From LM Studio's own API; null when the server doesn't say.
        public bool? Vision;
        public int? ContextTokens;
    }

    private static HttpRequestMessage ServerRequest(string url, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }

    // The server's models (GET /models of the OpenAI API), with what LM Studio's own API (/api/v0/models) adds: the
    // kind of model (embeddings, vision) and the context length a loaded model has.
    private static async Task<List<ServerModelInfo>> FetchServerModelsAsync(string url, string key, CancellationToken token)
    {
        var models = new List<ServerModelInfo>();
        using (HttpRequestMessage request = ServerRequest(url + "/models", key))
        using (HttpResponseMessage response = await ServerHttp.SendAsync(request, token).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(string.Format("the server answered {0} {1}", (int)response.StatusCode, response.ReasonPhrase));
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            if (json.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                foreach (JsonElement model in data.EnumerateArray())
                    if (model.TryGetProperty("id", out JsonElement id) && id.GetString() is string name)
                        models.Add(new ServerModelInfo { Id = name, IsEmbedding = name.Contains("embed", StringComparison.OrdinalIgnoreCase) });
        }
        if (models.Count == 0)
            throw new InvalidOperationException("the server lists no models");

        string root = url.EndsWith("/v1", StringComparison.Ordinal) ? url.Substring(0, url.Length - 3) : url;
        try
        {
            using HttpRequestMessage request = ServerRequest(root + "/api/v0/models", key);
            using HttpResponseMessage response = await ServerHttp.SendAsync(request, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                if (json.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement entry in data.EnumerateArray())
                    {
                        string id = entry.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
                        ServerModelInfo model = models.FirstOrDefault(m => m.Id == id);
                        if (model == null)
                            continue;
                        string type = entry.TryGetProperty("type", out JsonElement t) ? t.GetString() : null;
                        model.IsEmbedding = type == "embeddings";
                        if (type == "vlm") model.Vision = true;
                        else if (type == "llm") model.Vision = false;
                        model.Loaded = entry.TryGetProperty("state", out JsonElement state) && state.GetString() == "loaded";
                        if (entry.TryGetProperty("loaded_context_length", out JsonElement context) && context.TryGetInt32(out int tokens) && tokens > 0)
                            model.ContextTokens = tokens;
                    }
            }
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // Not LM Studio: the OpenAI API's list is all there is.
        }
        return models;
    }

    // ---------- IRemoteChatModel ----------

    /// <summary>
    /// Check that the server answers and has the model, and learn what the model takes (the page's connection before
    /// the first message).
    /// </summary>
    /// <param name="cancellationToken">Cancels it</param>
    /// <returns>Null when the server is ready, else why it can't be reached</returns>
    public async Task<string> ConnectAsync(CancellationToken cancellationToken)
    {
        string url = _serverUrl;
        if (string.IsNullOrEmpty(url))
            return "Add your server's address in Settings > Your own server, then try again.";
        Host?.SetStatus("Connecting to " + url + "...");
        try
        {
            List<ServerModelInfo> models = await FetchServerModelsAsync(url, _serverKey, cancellationToken);
            ChooseServerModel(models);
            ApplyServerModelInfo(models.First(m => m.Id == _serverModel));
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
                return "The connection was cancelled.";
            System.Diagnostics.Debug.WriteLine("Server connection failed: " + ex.Message);
            return "Could not reach your server at " + url + ": " + ex.Message +
                " Check that the server is running and accepts connections from the network (LM Studio: \"Serve on Local Network\"), " +
                "and its address in Settings > Your own server.";
        }
        if (cancellationToken.IsCancellationRequested)
            return "The connection was cancelled.";
        if (_serverRejectsReasoningUrl != url)
            _serverRejectsReasoning = false;
        System.Diagnostics.Debug.WriteLine("Server ready: " + _serverModel + ", context " + _serverContextTokens);
        return null;
    }

    private void ApplyServerModelInfo(ServerModelInfo model)
    {
        _serverContextKnown = model.ContextTokens.HasValue;
        _serverContextTokens = model.ContextTokens ?? DefaultServerContextTokens;
        Option.ContextWindow = _serverContextTokens;
        Option.AcceptsImages = model.Vision ?? true;
        // The page follows: what can be attached, the pending images a text-only model doesn't take, the context window.
        if (IsSelected)
            Host.ModelCapabilitiesChanged();
    }

    /// <summary>
    /// A server's 400 that names the reasoning option: remember it for this server (messages go without it from now on).
    /// </summary>
    /// <param name="ex">What the request threw</param>
    /// <returns>True when it was that, so the message can be sent again without the option</returns>
    public bool ServerRejectedReasoning(ClientResultException ex)
    {
        if (ex == null || !IsSelected || _serverRejectsReasoning || ex.Status != 400
            || ex.Message.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        _serverRejectsReasoning = true;
        _serverRejectsReasoningUrl = _serverUrl;
        return true;
    }

    /// <summary>Drops the reasoning option when the server rejected it.</summary>
    /// <param name="options">The message's options</param>
    public void AdjustOptions(ChatOptions options)
    {
        if (options != null && _serverRejectsReasoning)
            options.Reasoning = null;
    }

    /// <summary>
    /// The server rejected the reasoning option: the message goes again without it (<see cref="ServerRejectedReasoning"/>).
    /// </summary>
    /// <param name="error">What the request threw</param>
    /// <param name="options">The message's options; the reasoning option is taken out when it returns true</param>
    public bool ShouldResend(Exception error, ChatOptions options)
    {
        if (!(error is ClientResultException ex) || !ServerRejectedReasoning(ex))
            return false;
        System.Diagnostics.Debug.WriteLine("Server rejected reasoning_effort: " + ex.Message);
        if (options != null)
            options.Reasoning = null;
        return true;
    }

    private async Task RefreshServerContextAsync()
    {
        if (!IsSelected || _serverContextKnown || string.IsNullOrEmpty(_serverUrl))
            return;
        try
        {
            List<ServerModelInfo> models = await FetchServerModelsAsync(_serverUrl, _serverKey, CancellationToken.None);
            ServerModelInfo model = models.FirstOrDefault(m => m.Id == _serverModel);
            if (model?.ContextTokens != null && IsSelected)
                ApplyServerModelInfo(model);
        }
        catch (Exception) { }
    }

    /// <summary>The client for the server's model (the page adds function invocation).</summary>
    public IChatClient CreateChatClient()
    {
        var client = new OpenAIClient(new ApiKeyCredential(string.IsNullOrEmpty(_serverKey) ? "none" : _serverKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(_serverUrl),
                NetworkTimeout = TimeSpan.FromMinutes(10),
                Transport = new HttpClientPipelineTransport(new HttpClient(new ServerRetryHandler(WaitForForegroundAsync)
                {
                    InnerHandler = new HttpClientHandler()
                }))
            });
        return client.GetChatClient(_serverModel).AsIChatClient();
    }

    private static TaskCompletionSource<bool> CompletedForeground()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }

    private Task WaitForForegroundAsync(CancellationToken token) => _foreground.Task.WaitAsync(token);

    // Sends a request that failed without a response once more, after the app is back in front. On a Galaxy S10
    // (Android 12) the connection to the server ended ("unexpected end of stream") the moment another app came to the
    // front - Google Maps, opened by open_directions while the reply to its result was being requested - so the reply
    // was lost. A chat request changes nothing on the server, so sending it again is safe.
    private sealed class ServerRetryHandler : DelegatingHandler
    {
        private readonly Func<CancellationToken, Task> _waitForForeground;

        public ServerRetryHandler(Func<CancellationToken, Task> waitForForeground)
        {
            _waitForForeground = waitForForeground;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine("Server request failed, sending it again: " + ex.Message);
                await _waitForForeground(cancellationToken);
                return await base.SendAsync(Copy(request, body), cancellationToken);
            }
        }

        // A request can only be sent once.
        private static HttpRequestMessage Copy(HttpRequestMessage request, byte[] body)
        {
            HttpRequestMessage copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
            foreach (var header in request.Headers)
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (body != null)
            {
                copy.Content = new ByteArrayContent(body);
                foreach (var header in request.Content.Headers)
                    copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return copy;
        }
    }

    /// <summary>
    /// The conversation's summary options for the server: without thinking, and with room for a server that thinks anyway.
    /// </summary>
    public ChatOptions SummaryOptions => new ChatOptions
    {
        MaxOutputTokens = SummaryMaxTokens + ThinkingRoomTokens,
        Reasoning = _serverRejectsReasoning ? null : new ReasoningOptions { Effort = ReasoningEffort.None }
    };

    /// <summary>One question to the server's model in a request of its own, without thinking; its answer.</summary>
    /// <param name="prompt">The question</param>
    /// <param name="maxTokens">The longest answer (the request allows room for a server that thinks anyway); 0 for the summaries' size</param>
    /// <param name="cancellationToken">Cancels it</param>
    public async Task<string> AskOnceAsync(string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        using IChatClient client = CreateChatClient();
        ChatOptions options = SummaryOptions;
        if (maxTokens > 0)
            options.MaxOutputTokens = maxTokens + ThinkingRoomTokens;
        ChatResponse response;
        try { response = await client.GetResponseAsync(prompt, options, cancellationToken); }
        catch (ClientResultException ex) when (ServerRejectedReasoning(ex))
        {
            options.Reasoning = null;
            response = await client.GetResponseAsync(prompt, options, cancellationToken);
        }
        string text = new Emgu.LiteRT.LM.Models.ChatReply(response.Text ?? string.Empty).Text.Trim();
        if (text.Length == 0)
            throw new Exception("The answer is empty");
        return text;
    }
}
