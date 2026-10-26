// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Maui.Demo.Lite;

// A model on a server of the user's own - LM Studio, Ollama, llama.cpp's server, vLLM... - through its
// OpenAI-compatible API (Microsoft.Extensions.AI.OpenAI's IChatClient), in place of a model on the device: the remote
// "Your own server" option of the model list. It chats through the same agent as Gemma 4, with the same tools (the
// server's model must support tool calling), and the same saved chats and compaction - with the token counts
// estimated, since there is no tokenizer on the device. Settings > "Your own server" holds the address, the key and
// the model; the page's ConnectRemoteAsync and CreateChatClient come here.
internal sealed class ServerFeature : DemoFeature
{
    private const string ServerKeyStorageName = "litert_lm_chat_server_key";

    // Without a context length from the server, the conversation is kept within this (LM Studio's default).
    private const int DefaultServerContextTokens = 4096;
    private const int SummaryMaxTokens = 400;

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

    public ServerFeature(ChatModelOption option)
    {
        Option = option ?? throw new ArgumentNullException(nameof(option));
        _serverUrl = AppSettings.Chat.ServerUrl;
        _serverModel = AppSettings.Chat.ServerModel;
        UpdateOptionDetail();
    }

    public override string Name => "server";

    public override string Title => "Your own server";

    public override bool PersistEnabled => false;

    // The remote option of the model list this feature connects.
    public ChatModelOption Option { get; }

    public bool IsSelected => Host?.Model == Option;

    // The server refused reasoning_effort: the message goes without it.
    public bool RejectsReasoning => _serverRejectsReasoning;

    // The context length of the server's model, and whether the server said (else a default).
    public int ContextTokens => _serverContextTokens;

    public bool ContextKnown => _serverContextKnown;

    public string ModelLabel => string.IsNullOrEmpty(_serverModel) ? Option.DisplayName : _serverModel;

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        _ = LoadServerKeyAsync();
    }

    protected override void OnAppStopped()
    {
        if (_foreground.Task.IsCompleted)
            _foreground = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    protected override void OnAppResumed() => _foreground.TrySetResult(true);

    // LM Studio loads a model on its first request, with the context length it is set to: learn it then.
    public override void OnReplyCompleted(ChatExchangeEventArgs e) => _ = RefreshServerContextAsync();

    // The server's address as typed, made into the API's base address: "http://host:1234" gets "/v1".
    private static string NormalizeServerUrl(string text)
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

    public override View BuildSettingsSection()
    {
        _serverUrlEntry = new Entry
        {
            Text = _serverUrl,
            Placeholder = "Server address, e.g. http://192.168.1.20:1234/v1",
            Keyboard = Keyboard.Url,
            FontFamily = Theme.BodyFont,
            FontSize = 14,
            TextColor = Theme.PrimaryText,
            PlaceholderColor = Theme.SecondaryText
        };
        _serverKeyEntry = new Entry
        {
            Text = _serverKey,
            Placeholder = "API key (if the server needs one)",
            IsPassword = true,
            FontFamily = Theme.BodyFont,
            FontSize = 14,
            TextColor = Theme.PrimaryText,
            PlaceholderColor = Theme.SecondaryText
        };
        _serverUrlEntry.Completed += (s, e) => _ = ApplyServerSettingsAsync();
        _serverKeyEntry.Completed += (s, e) => _ = ApplyServerSettingsAsync();
        var connect = new Button { Text = "Connect" };
        connect.Clicked += (s, e) => _ = ApplyServerSettingsAsync(true);
        _serverModelButton = new Button { Text = "Model: " + (string.IsNullOrEmpty(_serverModel) ? "choose after connecting" : _serverModel) };
        _serverModelButton.Clicked += OnServerModelTapped;
        _serverStatus = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Theme.SectionTitle("Your own server"),
                new Label
                {
                    Text = "Chat with a model on a computer of yours instead of this device: any server with an OpenAI-compatible " +
                        "API, such as LM Studio (turn on \"Serve on Local Network\") or Ollama. Then pick \"Your own server\" in the " +
                        "model list. Your messages, pictures and the tools' results go to that server. Tools need a model that " +
                        "supports tool calling.",
                    FontFamily = Theme.BodyFont,
                    FontSize = 12,
                    TextColor = Theme.SecondaryText
                },
                _serverUrlEntry,
                _serverKeyEntry,
                connect,
                _serverModelButton,
                _serverStatus
            }
        };
    }

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
        AppSettings.Chat.ServerUrl = url;
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
        Demo.ResetRemote();
        Demo.RefreshModelList();
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
            Demo.StartNewChat();
        Reconnect();
    }

    private void SetServerModel(string id)
    {
        _serverModel = id ?? string.Empty;
        AppSettings.Chat.ServerModel = _serverModel;
        if (_serverModelButton != null)
            _serverModelButton.Text = "Model: " + (string.IsNullOrEmpty(_serverModel) ? "choose after connecting" : _serverModel);
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

    // The page's ConnectRemoteAsync: check that the server answers and has the model, and learn what the model
    // takes. False (with the status saying why) when it can't be reached.
    public async Task<bool> ConnectAsync(CancellationToken token)
    {
        string url = _serverUrl;
        if (string.IsNullOrEmpty(url))
        {
            Host.SetStatus("Add your server's address in Settings > Your own server, then try again.");
            return false;
        }
        Host.SetStatus("Connecting to " + url + "...");
        try
        {
            List<ServerModelInfo> models = await FetchServerModelsAsync(url, _serverKey, token);
            ChooseServerModel(models);
            ApplyServerModelInfo(models.First(m => m.Id == _serverModel));
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
                return false;
            UiStallWatchdog.Mark("Server connection failed: " + ex.Message);
            Host.SetStatus("Could not reach your server at " + url + ": " + ex.Message +
                " Check that the server is running and accepts connections from the network (LM Studio: \"Serve on Local Network\"), " +
                "and its address in Settings > Your own server.");
            return false;
        }
        if (token.IsCancellationRequested)
            return false;
        if (_serverRejectsReasoningUrl != url)
            _serverRejectsReasoning = false;
        UiStallWatchdog.Mark("Server ready: " + _serverModel + ", context " + _serverContextTokens);
        return true;
    }

    private void ApplyServerModelInfo(ServerModelInfo model)
    {
        _serverContextKnown = model.ContextTokens.HasValue;
        _serverContextTokens = model.ContextTokens ?? DefaultServerContextTokens;
        Option.ContextWindow = _serverContextTokens;
        Option.AcceptsImages = model.Vision ?? true;
        if (IsSelected)
        {
            if (Host.Conversation != null)
                Host.Conversation.ContextWindow = _serverContextTokens;
            if (!Option.AcceptsImages)
                Demo.DropPendingImages();
        }
    }

    // A server's 400 that names the reasoning option: remember it for this server, and let the message be sent again.
    public bool ServerRejectedReasoning(ClientResultException ex)
    {
        if (!IsSelected || _serverRejectsReasoning || ex.Status != 400
            || ex.Message.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        _serverRejectsReasoning = true;
        _serverRejectsReasoningUrl = _serverUrl;
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

    // The page's CreateChatClient for the server.
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

    // The conversation's summary options for the server: without thinking, and with room for a server that thinks
    // anyway (LM Studio runs Gemma 4 with thinking on unless told otherwise, and it used up SummaryMaxTokens before
    // any summary came out).
    public ChatOptions SummaryOptions => new ChatOptions
    {
        MaxOutputTokens = SummaryMaxTokens + 1600,
        Reasoning = _serverRejectsReasoning ? null : new ReasoningOptions { Effort = ReasoningEffort.None }
    };

    // Summarize with the server's model, without thinking (the page's AskModelOnceAsync for the server).
    public async Task<string> AskOnceAsync(string prompt)
    {
        using IChatClient client = CreateChatClient();
        ChatOptions options = SummaryOptions;
        ChatResponse response;
        try { response = await client.GetResponseAsync(prompt, options); }
        catch (ClientResultException ex) when (ServerRejectedReasoning(ex))
        {
            options.Reasoning = null;
            response = await client.GetResponseAsync(prompt, options);
        }
        string text = new Emgu.LiteRT.LM.Models.ChatReply(response.Text ?? string.Empty).Text.Trim();
        if (text.Length == 0)
            throw new Exception("The summary is empty");
        return text;
    }
}
