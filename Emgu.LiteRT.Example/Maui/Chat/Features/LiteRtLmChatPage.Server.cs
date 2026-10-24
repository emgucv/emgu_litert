using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Maui.Demo.Lite;

// A model on a server of the user's own - LM Studio, Ollama, llama.cpp's server, vLLM... - through its
// OpenAI-compatible API (Microsoft.Extensions.AI.OpenAI's IChatClient), in place of a model on the device. It chats
// through the same agent as Gemma 4, with the same tools (the server's model must support tool calling), and the same
// saved chats and compaction - with the token counts estimated, since there is no tokenizer on the device.
public partial class LiteRtLmChatPage
{
    private const string ServerKeyStorageName = "litert_lm_chat_server_key";

    // Without a context length from the server, the conversation is kept within this (LM Studio's default).
    private const int DefaultServerContextTokens = 4096;

    // Not one of Models (nothing to download or store); selected as index Models.Length.
    private static readonly ModelOption ServerOption = new ModelOption
    {
        Name = "Your server",
        Choice = ModelChoice.Server,
        // Thinking comes back as reasoning_content (LM Studio, Ollama, ...), which Microsoft.Extensions.AI.OpenAI turns
        // into TextReasoningContent; the Think chip is sent as reasoning_effort ("none" turns it off in LM Studio).
        CanThink = true,
        CanUseTools = true,
        CanSummarize = true,
        AcceptsImages = true,
        Detail = "A model on your own server with an OpenAI-compatible API, such as LM Studio or Ollama."
    };

    private static int ServerIndex => Models.Length;

    private ModelOption OptionAt(int index) => index == ServerIndex ? ServerOption : Models[index];

    private static int ModelIndexOf(string choice)
    {
        if (choice == ModelChoice.Server.ToString())
            return ServerIndex;
        return Array.FindIndex(Models, m => m.Choice.ToString() == choice);
    }

    private static readonly HttpClient ServerHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    private string _serverUrl = string.Empty;
    private string _serverModel = string.Empty;
    private string _serverKey = string.Empty;
    // The server, model and key the connection was checked for (ModelReady), or null.
    private string _serverConnected;
    // The server refused reasoning_effort (some OpenAI-compatible servers reject what they don't know): not sent to it again.
    private bool _serverRejectsReasoning;
    private string _serverRejectsReasoningUrl;
    private int _serverContextTokens = DefaultServerContextTokens;
    private bool _serverContextKnown;

    private Entry _serverUrlEntry;
    private Entry _serverKeyEntry;
    private Button _serverModelButton;
    private Label _serverStatus;

    private string ServerConnectionKey => _serverUrl + "\n" + _serverModel + "\n" + _serverKey;

    private bool ServerSelected => _selectedModel == ServerIndex;

    private string ServerModelLabel => string.IsNullOrEmpty(_serverModel) ? ServerOption.Name : _serverModel;

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

    private View BuildServerSection()
    {
        _serverUrl = AppSettings.Chat.ServerUrl;
        _serverModel = AppSettings.Chat.ServerModel;
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
        _ = LoadServerKeyAsync();
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                Theme.SectionTitle("Your own server"),
                new Label
                {
                    Text = "Chat with a model on a computer of yours instead of this device: any server with an OpenAI-compatible " +
                        "API, such as LM Studio (turn on \"Serve on Local Network\") or Ollama. Then pick \"Your server\" in the " +
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
        ReconnectServerIfSelected();
    }

    private void ReconnectServerIfSelected()
    {
        _serverConnected = null;
        if (ServerSelected)
        {
            _modelChipLabel.Text = ServerModelLabel;
            RefreshModelList();
            if (_pageStarted && !_busy)
                _ = PrepareModelAsync(false);
        }
        else
            RefreshModelList();
    }

    private async void OnServerModelTapped(object sender, EventArgs e)
    {
        if (!CanChangeModel)
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
        string picked = await DisplayActionSheetAsync("Model on your server", "Cancel", null, names);
        if (picked == null || !names.Contains(picked) || picked == _serverModel || !CanChangeModel)
            return;
        SetServerModel(picked);
        // A chat belongs to a model: another model starts a new chat.
        if (ServerSelected && _pageStarted)
            StartNewChat();
        ReconnectServerIfSelected();
    }

    private void SetServerModel(string id)
    {
        _serverModel = id ?? string.Empty;
        AppSettings.Chat.ServerModel = _serverModel;
        if (_serverModelButton != null)
            _serverModelButton.Text = "Model: " + (string.IsNullOrEmpty(_serverModel) ? "choose after connecting" : _serverModel);
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

    // LoadModelAsync for the server: check that it answers and has the model, and learn what the model takes.
    private async Task<bool> ConnectServerAsync(ModelOption option, CancellationToken token)
    {
        string url = _serverUrl;
        if (string.IsNullOrEmpty(url))
        {
            ShowServerSetupNeeded(option);
            ClearLoading(option.Choice);
            return false;
        }
        ShowLoadCard(option, "Connecting to " + url + "...", null);
        try
        {
            List<ServerModelInfo> models = await FetchServerModelsAsync(url, _serverKey, token);
            ChooseServerModel(models);
            ApplyServerModelInfo(models.First(m => m.Id == _serverModel));
        }
        catch (Exception ex)
        {
            ClearLoading(option.Choice);
            if (token.IsCancellationRequested || ex is OperationCanceledException && token.IsCancellationRequested)
                return false;
            UiStallWatchdog.Mark("Server connection failed: " + ex.Message);
            ShowLoadCard(option, "Could not reach your server at " + url + ": " + ex.Message +
                "\nCheck that the server is running and accepts connections from the network (LM Studio: \"Serve on Local Network\"), " +
                "and its address in Settings > Your own server.", null);
            _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
            SetLoadButtons("Retry", () => _ = PrepareModelAsync(true), null);
            return false;
        }
        if (token.IsCancellationRequested)
            return false;
        _serverConnected = ServerConnectionKey;
        if (_serverRejectsReasoningUrl != url)
            _serverRejectsReasoning = false;
        _loaded = option.Choice;
        _loadedOnGpu = false;
        _loadedContextTokens = 0;
        _modelChipLabel.Text = ServerModelLabel;
        RefreshModelList();
        // A conversation in progress (another model before, or the server reconnected) is given to the server, through a
        // new client for it.
        ResetAgent();
        _rereadNext = ChatMessages.Any();
        ClearLoading(option.Choice);
        HideLoadCard();
        UpdateMemoryLabel();
        UiStallWatchdog.Mark("Server ready: " + _serverModel + ", context " + _serverContextTokens);
        return true;
    }

    private void ShowServerSetupNeeded(ModelOption option)
    {
        ShowLoadCard(option, "Add your server's address in Settings > Your own server, then try again.", null);
        _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
        _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
        SetLoadButtons("Open settings", OnSettingsTapped, null);
    }

    private void ApplyServerModelInfo(ServerModelInfo model)
    {
        _serverContextKnown = model.ContextTokens.HasValue;
        _serverContextTokens = model.ContextTokens ?? DefaultServerContextTokens;
        ServerOption.AcceptsImages = model.Vision ?? true;
        if (ServerSelected)
        {
            foreach (PendingAttachment attachment in _pending.ToList())
                if (attachment.Image != null && !ServerOption.AcceptsImages)
                    RemovePending(attachment);
        }
    }

    // A server's 400 that names the reasoning option: remember it for this server, and let the message be sent again.
    private bool ServerRejectedReasoning(System.ClientModel.ClientResultException ex)
    {
        if (!ServerSelected || _serverRejectsReasoning || ex.Status != 400
            || ex.Message.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        _serverRejectsReasoning = true;
        _serverRejectsReasoningUrl = _serverUrl;
        return true;
    }

    // LM Studio loads a model on its first request, with the context length it is set to: learn it then.
    private async Task RefreshServerContextAsync()
    {
        if (!ServerSelected || _serverContextKnown || string.IsNullOrEmpty(_serverUrl))
            return;
        try
        {
            List<ServerModelInfo> models = await FetchServerModelsAsync(_serverUrl, _serverKey, CancellationToken.None);
            ServerModelInfo model = models.FirstOrDefault(m => m.Id == _serverModel);
            if (model?.ContextTokens != null && ServerSelected)
            {
                ApplyServerModelInfo(model);
                UpdateMemoryLabel();
            }
        }
        catch (Exception) { }
    }

    private IChatClient CreateServerChatClient()
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

    // Whether the app is in front: a server request that failed while it wasn't is sent again once it is.
    private TaskCompletionSource<bool> _foreground = CompletedForeground();

    private static TaskCompletionSource<bool> CompletedForeground()
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(true);
        return source;
    }

    private void ServerAppStopped(object sender, EventArgs e)
    {
        if (_foreground.Task.IsCompleted)
            _foreground = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void ServerAppResumed(object sender, EventArgs e) => _foreground.TrySetResult(true);

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

    // Without the model's tokenizer: about 3.5 characters a token for English, fewer for other scripts - rounded up,
    // so the conversation is compacted a little early rather than too late.
    private static int ApproximateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        int ascii = text.Count(c => c < 128);
        return (int)Math.Ceiling(ascii / 3.5 + (text.Length - ascii) * 1.0);
    }

    // Summarize with the server's model, without thinking. The limit still leaves room for it, for a server that thinks
    // anyway: LM Studio runs Gemma 4 with thinking on unless told otherwise, and it used up SummaryMaxTokens before any
    // summary came out (an empty reply). The instructions keep the summary itself short.
    private async Task<string> SummarizeOnServerAsync(string prompt)
    {
        using IChatClient client = CreateServerChatClient();
        var options = new ChatOptions
        {
            MaxOutputTokens = SummaryMaxTokens + 1600,
            Reasoning = _serverRejectsReasoning ? null : new ReasoningOptions { Effort = ReasoningEffort.None }
        };
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

    // The model list's entry for the server.
    private View BuildServerModelRow(bool inSheet)
    {
        bool selected = ServerSelected;
        string detail = string.IsNullOrEmpty(_serverUrl)
            ? "Not set up: add its address in Settings > Your own server."
            : (string.IsNullOrEmpty(_serverModel) ? "" : _serverModel + " · ") + _serverUrl;
        var row = new Grid
        {
            Padding = new Thickness(10, 8),
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        row.Add(new VerticalStackLayout
        {
            Children =
            {
                new Label { Text = ServerOption.Name, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText },
                new Label { Text = detail, FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText },
                new Label { Text = ServerOption.Detail, FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText }
            }
        }, 0, 0);
        if (selected)
            row.Add(Theme.MakeIcon(Theme.GlyphCheck, Theme.Accent, 22), 1, 0);
        var cell = new Border
        {
            BackgroundColor = selected ? Theme.TileBackground : Colors.Transparent,
            Stroke = Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Content = row
        };
        cell.OnTap(() =>
        {
            if (inSheet)
                _sheet.Dismiss(ServerIndex + ":cpu");
            else if (CanChangeModel)
                SelectModel(ServerIndex, false);
        });
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = "Server", FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText, Padding = new Thickness(10, 8) },
                cell
            }
        };
    }
}
