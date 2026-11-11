// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.LM.Server;
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

    // A Mac paired through "Find a Mac" (its Bonjour name), so it is found again when its address changes.
    private const string PairedMacSettingKey = "server_paired_mac";

    // Whether "Find a Mac" has looked before on iOS: the first search brings up the Local Network prompt and finds nothing.
    private const string SearchedBeforeSettingKey = "server_searched_before";

    // The paired Mac's search model, as its model list last gave it ("id|name"), offered in Settings > Embedding model.
    private const string SearchModelSettingKey = "server_search_model";

    /// <summary>The key of the paired Mac's search model in Settings > Embedding model.</summary>
    public const string SearchOptionKey = "server";

    // The paired Mac's image model, as its model list last gave it ("id|name|size|steps").
    private const string ImageModelSettingKey = "server_image_model";

    /// <summary>The key an app gives the paired Mac's image model among its image models (e.g. Emgu.LiteRT.LM.Maui.Images' ImageModelOption).</summary>
    public const string ImageOptionKey = "server";

    // Without a context length from the server, the conversation is kept within this (LM Studio's default).
    private const int DefaultServerContextTokens = 4096;
    private const int SummaryMaxTokens = 400;
    // Room for a server that thinks anyway (LM Studio runs Gemma 4 with thinking on unless told otherwise, and it used
    // up SummaryMaxTokens before any summary came out).
    private const int ThinkingRoomTokens = 1600;

    // A server that isn't answering (a Mac asleep, stopped, or at another address) fails within seconds.
    private static readonly HttpClient ServerHttp = new HttpClient(ServerReachability.CreateHandler()) { Timeout = TimeSpan.FromSeconds(15) };

    private string _serverUrl = string.Empty;
    private string _serverModel = string.Empty;
    private string _serverKey = string.Empty;
    private string _pairedMac = string.Empty;
    private readonly string _defaultName;
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
        _defaultName = Option.DisplayName;
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
        _pairedMac = host.Settings.Get(PairedMacSettingKey, string.Empty) ?? string.Empty;
        UpdateOptionDetail();
        UpdateSearchOption();
        _ = LoadServerKeyAsync();
        ServerModelsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>An image model on the paired Mac: its id, name, picture size ("512 x 512") and steps.</summary>
    public sealed record ServerImageModelInfo(string Id, string Name, string SizeLabel, int Steps);

    /// <summary>
    /// The paired Mac's image model, as its model list last gave it (an app offers it beside its own image models, with
    /// <see cref="CreateImageGenerator"/>); null when the server is no Emgu LiteRT Mac or draws nothing.
    /// </summary>
    public ServerImageModelInfo PairedImageModel
    {
        get
        {
            string[] saved = (Host?.Settings.Get(ImageModelSettingKey, string.Empty) ?? string.Empty).Split('|');
            if (saved.Length < 4 || saved[0].Length == 0 || string.IsNullOrEmpty(_serverUrl))
                return null;
            return new ServerImageModelInfo(saved[0], saved[1], saved[2].Replace("x", " x "),
                int.TryParse(saved[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int steps) ? steps : 0);
        }
    }

    /// <summary>The server by name: the paired Mac's, else its address.</summary>
    public string ServerDisplayName => string.IsNullOrEmpty(_pairedMac) ? "your server" : _pairedMac;

    /// <summary>Raised (on the UI thread) when what the server offers besides its chat model may have changed: <see cref="PairedImageModel"/>.</summary>
    public event EventHandler ServerModelsChanged;

    /// <summary>A client that draws with the server's image model, reading the server's address and token for each picture.</summary>
    /// <param name="model">The model's id (<see cref="ServerImageModelInfo.Id"/>)</param>
    public ServerImageGenerator CreateImageGenerator(string model) => new ServerImageGenerator(() => _serverUrl, () => _serverKey, model);

    // Settings > Embedding model offers the paired Mac's search model (texts embedded there, so past-chat and document search
    // need no model on the phone) while the server is a Mac that serves one.
    private void UpdateSearchOption()
    {
        SearchModelFeature search = Host?.Services.Get<SearchModelFeature>();
        if (search == null)
            return;
        string[] saved = (Host.Settings.Get(SearchModelSettingKey, string.Empty) ?? string.Empty).Split('|', 2);
        string id = saved[0], name = saved.Length > 1 && saved[1].Length > 0 ? saved[1] : saved[0];
        // "EmbeddingGemma 2 Text Vision 440M · CPU": the Mac's backend means nothing here.
        int backend = name.LastIndexOf(" · ", StringComparison.Ordinal);
        if (backend > 0)
            name = name.Substring(0, backend);
        if (id.Length == 0 || string.IsNullOrEmpty(_serverUrl))
        {
            search.RemoteOption = null;
            return;
        }
        string where = string.IsNullOrEmpty(_pairedMac) ? "your server" : _pairedMac;
        // The Mac first: the list cuts a long name off at its end.
        string label = where + " · " + name;
        if (search.RemoteOption?.IndexKey == id && search.RemoteOption.Name == label)
            return;
        search.RemoteOption = new SearchModelOption(SearchOptionKey, label,
            new ServerEmbeddingGenerator(() => _serverUrl, () => _serverKey, id), id,
            "Runs on " + where + ": past-chat and document search without the model on this device, while it can be reached. " +
            "Text only - photo and video search are off with it.");
    }

    // What the server's model list says of its search and image models: an Emgu LiteRT Mac's, or none.
    private void RememberServerModels(List<ServerModelInfo> models)
    {
        ServerModelInfo search = models.FirstOrDefault(m => m.IsEmguMac && m.IsEmbedding);
        string value = search == null ? string.Empty : search.Id + "|" + (search.DisplayName ?? search.Id);
        ServerModelInfo images = models.FirstOrDefault(m => m.IsEmguMac && m.IsImages);
        string image = images == null ? string.Empty : string.Join("|", images.Id, (images.DisplayName ?? images.Id).Replace("|", " "),
            images.ImageSize ?? string.Empty, images.ImageSteps.ToString(CultureInfo.InvariantCulture));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Host == null)
                return;
            Host.Settings.Set(SearchModelSettingKey, value);
            UpdateSearchOption();
            if (Host.Settings.Get(ImageModelSettingKey, string.Empty) != image)
            {
                Host.Settings.Set(ImageModelSettingKey, image);
                ServerModelsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <inheritdoc/>
    public override void OnAppStopped()
    {
        if (_foreground.Task.IsCompleted)
            _foreground = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <inheritdoc/>
    public override void OnAppResumed()
    {
        _foreground.TrySetResult(true);
        _ = RefreshPairedMacModelsAsync();
    }

    /// <inheritdoc/>
    public override void OnReplyCompleted(ChatExchangeEventArgs e)
    {
        // LM Studio loads a model on its first request, with the context length it is set to: learn it then.
        _ = RefreshServerContextAsync();
        if (IsSelected)
            _ = RefreshPairedMacModelsAsync();
    }

    // What the paired Mac serves besides its chat model changes while the phone is connected (image generation turned on
    // there, another search model): look again when the app comes back and after each reply from it. Quietly: a Mac that
    // isn't answering is reported when the chat connects.
    private async Task RefreshPairedMacModelsAsync()
    {
        if (Host == null || string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(_pairedMac))
            return;
        try { RememberServerModels(await FetchServerModelsAsync(_serverUrl, _serverKey, CancellationToken.None).ConfigureAwait(false)); }
        catch (Exception) { }
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
        var findMac = new Button { Text = "Find a Mac", IsVisible = LocalServerBrowser.IsSupported };
        findMac.Clicked += (s, e) => _ = FindMacAsync();
        SemanticProperties.SetHint(findMac, "Looks for a Mac on this network that serves its chat's model, and pairs with it");
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
                ChatPage.Note("A Mac running Emgu LiteRT can serve its model too (on the Mac: Settings > Serve this model on the network): " +
                    "Find a Mac pairs with it using the code it shows."),
                findMac,
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
            UpdateSearchOption();
            ServerModelsChanged?.Invoke(this, EventArgs.Empty);
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

    // "Find a Mac": the Macs serving their model on this network, the one chosen paired with the code it shows, and the chat
    // set up to use it.
    private async Task FindMacAsync()
    {
        if (Host == null || Host.IsBusy)
            return;
        _serverStatus.Text = "Looking for Macs on this network...";
        IList<FoundServer> found;
        try
        {
            found = await LocalServerBrowser.BrowseAsync(TimeSpan.FromSeconds(4));
            // iOS's first search only asks for the local network: look again while the user answers (about half a minute).
            if (DeviceInfo.Current.Platform == DevicePlatform.iOS && !Host.Settings.Get(SearchedBeforeSettingKey, false))
            {
                Host.Settings.Set(SearchedBeforeSettingKey, true);
                if (found.Count == 0)
                    _serverStatus.Text = "Looking for Macs on this network (allow Local Network if iOS asks)...";
                for (int i = 0; i < 7 && found.Count == 0; i++)
                    found = await LocalServerBrowser.BrowseAsync(TimeSpan.FromSeconds(4));
            }
        }
        catch (Exception ex)
        {
            _serverStatus.Text = "Couldn't look for Macs: " + ex.Message;
            return;
        }
        if (found.Count == 0)
        {
            _serverStatus.Text = "No Mac found. On the Mac: Settings > Serve this model on the network, on the same Wi-Fi as this device" +
                (DeviceInfo.Current.Platform == DevicePlatform.iOS ? " (and allow Local Network for this app in the iOS Settings)." : ".");
            return;
        }
        _serverStatus.Text = string.Empty;
        string picked = await Host.ChooseAsync("Pair with a Mac", found.Select(f => new ChatChoice(f.Name, f.Name,
            string.IsNullOrEmpty(f.Model) ? f.ApiUrl : f.Model + " · " + f.ApiUrl)), null);
        FoundServer mac = found.FirstOrDefault(f => f.Name == picked);
        if (mac == null)
            return;
        string code = await Host.PromptAsync("Pair with " + mac.Name, "On the Mac, tap \"Pair a phone\" and type the 6-digit code it shows.", "Pair", "Cancel", null, 7, Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(code))
            return;
        _serverStatus.Text = "Pairing with " + mac.Name + "...";
        string token;
        try { token = await PairAsync(mac, code.Replace(" ", string.Empty)); }
        catch (Exception ex)
        {
            _serverStatus.Text = "Couldn't pair: " + ex.Message;
            return;
        }
        _pairedMac = mac.Name;
        Host.Settings.Set(PairedMacSettingKey, _pairedMac);
        _serverUrlEntry.Text = mac.ApiUrl;
        _serverKeyEntry.Text = token;
        SetServerModel(string.Empty);
        await ApplyServerSettingsAsync(true);
    }

    private static async Task<string> PairAsync(FoundServer mac, string code)
    {
        string root = mac.ApiUrl.Substring(0, mac.ApiUrl.Length - 3);
        string body = JsonSerializer.Serialize(new Dictionary<string, string> { ["code"] = code, ["device"] = DeviceInfo.Current.Name });
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await ServerHttp.PostAsync(root + "/pair", content).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using JsonDocument json = JsonDocument.Parse(text);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(json.RootElement.TryGetProperty("error", out JsonElement error) && error.TryGetProperty("message", out JsonElement message)
                ? message.GetString() : "the Mac answered " + (int)response.StatusCode);
        return json.RootElement.GetProperty("token").GetString();
    }

    // The paired Mac at a new address (its Wi-Fi reconnected): found by its name, and the address saved. True when it moved.
    private async Task<bool> FindPairedMacAgainAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_pairedMac) || !LocalServerBrowser.IsSupported)
            return false;
        IList<FoundServer> found = await LocalServerBrowser.BrowseAsync(TimeSpan.FromSeconds(3), cancellationToken);
        FoundServer mac = found.FirstOrDefault(f => f.Name == _pairedMac);
        if (mac == null || mac.ApiUrl == _serverUrl)
            return false;
        _serverUrl = mac.ApiUrl;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Host?.Settings.Set(UrlSettingKey, _serverUrl);
            if (_serverUrlEntry != null)
                _serverUrlEntry.Text = _serverUrl;
            UpdateOptionDetail();
        });
        return true;
    }

    // The settings changed: the next message connects again (a conversation in progress is given to the server then).
    private void Reconnect()
    {
        UpdateOptionDetail();
        Host.ResetRemote();
        Host.RefreshModelList();
    }

    // The model list's row for the server says what it is set up with.
    // The server by name: the paired Mac's, else its address.
    private string ServerName => string.IsNullOrEmpty(_pairedMac) ? _serverUrl : _pairedMac;

    // What the user sees when the server can't be reached; the model list says it too until the next connection.
    private string Unreachable(string url)
    {
        string when = DateTime.Now.ToString("t");
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Option.Detail = ServerName + " wasn't answering · " + when + " · " + (string.IsNullOrEmpty(_serverModel) ? "" : _serverModel + " · ") + url;
            Host?.RefreshModelList();
        });
        return string.IsNullOrEmpty(_pairedMac)
            ? "Your server at " + url + " isn't answering: check that it is running and on this network, and its address in Settings > Your own server."
            : _pairedMac + " isn't answering - it may be asleep or on another network. Wake it and send the message again (\"Keep this Mac awake\" in its settings keeps it from sleeping while it serves).";
    }

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
        RememberServerModels(models);
        List<ServerModelInfo> chat = models.Where(m => !m.IsEmbedding && !m.IsImages).ToList();
        if (chat.Count == 0)
        {
            _serverStatus.Text = "The server has no chat models.";
            return;
        }
        // The page's sheet, a row per model with what the server says of it.
        string picked = await Host.ChooseAsync("Model on your server", chat.Select(m => new ChatChoice(m.Id, m.Id, ModelDetail(m))), _serverModel);
        if (picked == null || !chat.Any(m => m.Id == picked) || picked == _serverModel || Host.IsBusy)
            return;
        SetServerModel(picked);
        // A chat belongs to a model: another model starts a new chat.
        if (IsSelected)
            Host.StartNewChat();
        Reconnect();
    }

    // "Loaded · images · 8K context" - what LM Studio's own API says of the model; null when the server says nothing.
    private static string ModelDetail(ServerModelInfo model)
    {
        var parts = new List<string>();
        if (model.Loaded) parts.Add("loaded");
        if (model.Vision == true) parts.Add("images");
        if (model.ContextTokens is int tokens) parts.Add((tokens >= 1000 ? (tokens / 1000) + "K" : tokens.ToString()) + " context");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
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
        RememberServerModels(models);
        List<ServerModelInfo> chat = models.Where(m => !m.IsEmbedding && !m.IsImages).ToList();
        if (chat.Count == 0)
            throw new InvalidOperationException("The server has no chat models.");
        if (!chat.Any(m => m.Id == _serverModel))
            SetServerModel((chat.FirstOrDefault(m => m.Loaded) ?? chat[0]).Id);
    }

    private sealed class ServerModelInfo
    {
        public string Id;
        public bool IsEmbedding;
        // An Emgu LiteRT Mac's image model, its picture size ("512x512") and steps.
        public bool IsImages;
        public string ImageSize;
        public int ImageSteps;
        public bool Loaded;
        // From LM Studio's own API, or an Emgu LiteRT Mac's list; null when the server doesn't say.
        public bool? Vision;
        public int? ContextTokens;
        // An Emgu LiteRT Mac's (Emgu.LiteRT.LM.Server): its model's name and what it takes.
        public bool IsEmguMac;
        public string DisplayName;
        public bool? Audio;
        public bool? Tools;
        public bool? Thinking;
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
                    {
                        var info = new ServerModelInfo { Id = name, IsEmbedding = name.Contains("embed", StringComparison.OrdinalIgnoreCase) };
                        // An Emgu LiteRT Mac says its context window and what its model takes (other servers may too).
                        info.IsEmguMac = model.TryGetProperty("owned_by", out JsonElement owner) && owner.ValueKind == JsonValueKind.String && owner.GetString() == "emgu-litert";
                        if (model.TryGetProperty("display_name", out JsonElement display) && display.ValueKind == JsonValueKind.String)
                            info.DisplayName = display.GetString();
                        if (model.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String && type.GetString() == "embeddings")
                            info.IsEmbedding = true;
                        if (type.ValueKind == JsonValueKind.String && type.GetString() == "images")
                        {
                            info.IsImages = true;
                            info.ImageSize = model.TryGetProperty("size", out JsonElement size) && size.ValueKind == JsonValueKind.String ? size.GetString() : null;
                            info.ImageSteps = model.TryGetProperty("steps", out JsonElement steps) && steps.TryGetInt32(out int count) ? count : 0;
                        }
                        if (model.TryGetProperty("context_length", out JsonElement length) && length.TryGetInt32(out int contextTokens) && contextTokens > 0)
                            info.ContextTokens = contextTokens;
                        if (model.TryGetProperty("capabilities", out JsonElement capabilities) && capabilities.ValueKind == JsonValueKind.Object)
                        {
                            info.Vision = Flag(capabilities, "vision");
                            info.Audio = Flag(capabilities, "audio");
                            info.Tools = Flag(capabilities, "tools");
                            info.Thinking = Flag(capabilities, "thinking");
                        }
                        if (info.IsEmguMac)
                            info.Loaded = true;
                        models.Add(info);
                    }
        }
        if (models.Count == 0)
            throw new InvalidOperationException("the server lists no models");

        string root = url.EndsWith("/v1", StringComparison.Ordinal) ? url.Substring(0, url.Length - 3) : url;
        if (models.All(m => m.IsEmguMac))
            return models;
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

    private static bool? Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean() : null;

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
            List<ServerModelInfo> models = null;
            Exception first = null;
            try { models = await FetchServerModelsAsync(url, _serverKey, cancellationToken); }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { first = ex; }
            if (models == null)
            {
                // The paired Mac moved to another address: once more there.
                if (!await FindPairedMacAgainAsync(cancellationToken))
                    throw first;
                url = _serverUrl;
                models = await FetchServerModelsAsync(url, _serverKey, cancellationToken);
            }
            ChooseServerModel(models);
            ApplyServerModelInfo(models.First(m => m.Id == _serverModel));
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
                return "The connection was cancelled.";
            System.Diagnostics.Debug.WriteLine("Server connection failed: " + ex.Message);
            if (ServerReachability.IsUnreachable(ex))
                return Unreachable(url);
            return "Could not reach your server at " + url + ": " + ex.Message +
                " Check that the server is running and accepts connections from the network (LM Studio: \"Serve on Local Network\"), " +
                "and its address in Settings > Your own server.";
        }
        if (cancellationToken.IsCancellationRequested)
            return "The connection was cancelled.";
        if (_serverRejectsReasoningUrl != url)
            _serverRejectsReasoning = false;
        System.Diagnostics.Debug.WriteLine("Server ready: " + _serverModel + ", context " + _serverContextTokens);
        MainThread.BeginInvokeOnMainThread(UpdateOptionDetail);
        return null;
    }

    private void ApplyServerModelInfo(ServerModelInfo model)
    {
        _serverContextKnown = model.ContextTokens.HasValue;
        _serverContextTokens = model.ContextTokens ?? DefaultServerContextTokens;
        Option.ContextWindow = _serverContextTokens;
        Option.AcceptsImages = model.Vision ?? true;
        if (model.Audio is bool audio)
            Option.AcceptsAudio = audio;
        if (model.Tools is bool tools)
            Option.CanUseTools = tools;
        if (model.Thinking is bool thinking)
            Option.CanThink = thinking;
        // "Gemma 4 E4B on Canming's Mac mini" for a paired Mac; the option's own name otherwise.
        string name = model.IsEmguMac && !string.IsNullOrEmpty(_pairedMac) ? (model.DisplayName ?? model.Id) + " on " + _pairedMac : _defaultName;
        if (Option.DisplayName != name)
        {
            Option.DisplayName = name;
            MainThread.BeginInvokeOnMainThread(() => Host?.RefreshModelList());
        }
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
        if (ServerReachability.IsUnreachable(error))
        {
            // Not sent again: the next message connects again (and looks for a paired Mac at a new address).
            Host?.ResetRemote();
            return false;
        }
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
                RetryPolicy = new ReachableOnlyRetryPolicy(),
                Transport = new HttpClientPipelineTransport(new HttpClient(new ServerRetryHandler(WaitForForegroundAsync, () => _foreground.Task.IsCompleted,
                    () => Unreachable(_serverUrl))
                {
                    InnerHandler = ServerReachability.CreateHandler()
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

    // The OpenAI SDK's retries (3, with back-off), except for a server that can't be reached: on a Galaxy S10 a stopped
    // Mac's refusal came back four times, the error joining four copies of the message.
    private sealed class ReachableOnlyRetryPolicy : ClientRetryPolicy
    {
        protected override bool ShouldRetry(PipelineMessage message, Exception exception) =>
            !ServerReachability.IsUnreachable(exception) && base.ShouldRetry(message, exception);

        protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception exception) =>
            ServerReachability.IsUnreachable(exception) ? new ValueTask<bool>(false) : base.ShouldRetryAsync(message, exception);
    }

    // Sends a request that failed without a response once more, after the app is back in front. On a Galaxy S10
    // (Android 12) the connection to the server ended ("unexpected end of stream") the moment another app came to the
    // front - Google Maps, opened by open_directions while the reply to its result was being requested - so the reply
    // was lost. A chat request changes nothing on the server, so sending it again is safe. A server that can't be reached
    // while the app is in front isn't tried again: the user is told at once, in words naming it.
    private sealed class ServerRetryHandler : DelegatingHandler
    {
        private readonly Func<CancellationToken, Task> _waitForForeground;
        private readonly Func<bool> _inForeground;
        private readonly Func<string> _unreachable;

        public ServerRetryHandler(Func<CancellationToken, Task> waitForForeground, Func<bool> inForeground, Func<string> unreachable)
        {
            _waitForForeground = waitForForeground;
            _inForeground = inForeground;
            _unreachable = unreachable;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex) when (ServerReachability.IsUnreachable(ex) && _inForeground() && !cancellationToken.IsCancellationRequested)
            {
                // Still unreachable, for ShouldResend; the message is what the page shows ("Error: ...").
                throw new HttpRequestException(_unreachable(), ex);
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
