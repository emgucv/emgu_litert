// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Net;
using System.Text.Json;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.LM.Server;
using Microsoft.Extensions.AI;
#if IOS || MACCATALYST
using Foundation;
#endif

namespace Emgu.LiteRT.LM.Maui.OpenAI;

/// <summary>
/// The Mac serves its chat's model to the phones on the same network (TODO item 29): Settings > "Serve this model on the
/// network" starts an OpenAI-compatible server (Emgu.LiteRT.LM.Server's <see cref="ChatServer"/>, port
/// <see cref="ChatServer.DefaultPort"/>) over the page's model client (<see cref="ChatPage.ModelChatClient"/>: the page and
/// the phones take turns on the one model), announced with Bonjour (<see cref="BonjourType"/>) so a phone finds it. A phone
/// pairs with the 6-digit code "Pair a phone" shows, and its tools stay on the phone: the server only returns the model's tool
/// calls. Settings list the paired phones (Remove stops one), and "Keep this Mac awake" holds off idle sleep while serving. Only
/// on Mac Catalyst (<see cref="IsSupported"/>); the switch is kept in the page's settings and turns the server on when the page
/// opens again.
/// </summary>
public sealed class ServeModelsFeature : ChatFeature
{
    /// <summary>The Bonjour service type the server is announced as: "_emgu-litert._tcp".</summary>
    public const string BonjourType = "_emgu-litert._tcp";

    private const string EnabledKey = "serve_models";
    private const string AwakeKey = "serve_keep_awake";
    private const string DevicesKey = "serve_devices";

    private ServerPairing _pairing;
    private ChatServer _server;
    private string _problem;
    private Label _status;
    private VerticalStackLayout _devices;
    private VerticalStackLayout _serving;
    private Label _codeLabel;
    private IDispatcherTimer _codeTimer;
#if IOS || MACCATALYST
    private NSNetService _bonjour;
    private NSObject _activity;
#endif

    /// <summary>Whether the app can serve its model here: Mac Catalyst (a phone has nothing to serve).</summary>
    public static bool IsSupported =>
#if MACCATALYST
        true;
#else
        false;
#endif

    /// <inheritdoc/>
    public override string Name => "serve_models";

    /// <inheritdoc/>
    public override string Title => "Serve this model on the network";

    /// <inheritdoc/>
    public override bool PersistEnabled => false;

    /// <summary>Whether the server is listening.</summary>
    public bool IsServing => _server?.IsRunning == true;

    /// <summary>The server's name, as phones see it: the Mac's name.</summary>
    public static string ServerName
    {
        get
        {
#if MACCATALYST
            // The Mac's name in System Settings > General > Sharing ("Canming's Mac mini"): UIDevice says only "iPad" here,
            // and the host name is "macmini.lan".
            try
            {
                IntPtr cfName = SCDynamicStoreCopyComputerName(IntPtr.Zero, IntPtr.Zero);
                if (cfName != IntPtr.Zero)
                {
                    string name = ObjCRuntime.Runtime.GetNSObject<NSString>(cfName, true)?.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
            catch (Exception) { }
            string host = NSProcessInfo.ProcessInfo.HostName ?? string.Empty;
            int dot = host.IndexOf('.');
            if (dot > 0)
                host = host.Substring(0, dot);
            if (host.Length > 0)
                return host;
#endif
            return Environment.MachineName;
        }
    }

#if MACCATALYST
    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration")]
    private static extern IntPtr SCDynamicStoreCopyComputerName(IntPtr store, IntPtr nameEncoding);
#endif

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        if (!IsSupported)
            return;
        _pairing = new ServerPairing(new SettingsTokenStore(host.Settings));
        _pairing.Changed += (s, e) => MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshDevices();
            // A phone paired with the code shown: the sheet closes.
            if (_codeLabel != null && _pairing.CurrentCode == null)
                Host?.DismissSheet("paired");
        });
        if (host.Settings.Get(EnabledKey, false))
            MainThread.BeginInvokeOnMainThread(() => SetServing(true));
    }

    /// <inheritdoc/>
    public override void OnDetached()
    {
        StopServing();
        base.OnDetached();
    }

    /// <inheritdoc/>
    public override void OnModelChanged(ChatModelOption model)
    {
        UpdateStatus();
#if IOS || MACCATALYST
        if (IsServing)
            Announce();
#endif
    }

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        if (!IsSupported)
            return null;
        _status = ChatPage.Note(string.Empty);
        _devices = new VerticalStackLayout { Spacing = 4 };
        _serving = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                _status,
                ChatPage.Link("Pair a phone", () => _ = PairAsync()),
                _devices,
                ChatPage.SwitchRow("Keep this Mac awake while serving", "Otherwise the Mac may sleep when idle, and the phones can't reach it until it wakes.",
                    Host.Settings.Get(AwakeKey, true), on =>
                    {
                        Host.Settings.Set(AwakeKey, on);
                        if (IsServing)
                            HoldActivity();
                    })
            }
        };
        UpdateStatus();
        RefreshDevices();
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                ChatPage.SwitchRow(Title,
                    "Phones on this network use the chat's model through this Mac - the demo on a phone finds it under Settings > Your own server > Find a Mac. Their tools (location, photos, calendar) stay on the phone; only the messages and replies cross the network. The app must stay open.",
                    IsServing, on => SetServing(on)),
                _serving
            }
        };
    }

    private void SetServing(bool on)
    {
        Host?.Settings.Set(EnabledKey, on);
        if (on)
            StartServing();
        else
            StopServing();
        UpdateStatus();
    }

    private void StartServing()
    {
        if (IsServing || _pairing == null)
            return;
        _problem = null;
        var server = new ChatServer(_pairing, Served, ServedSearch) { Name = ServerName };
        try { server.Start(ChatServer.DefaultPort); }
        catch (Exception ex) when (ex is HttpListenerException || ex is System.Net.Sockets.SocketException || ex is InvalidOperationException)
        {
            server.Dispose();
            _problem = "Couldn't listen on port " + ChatServer.DefaultPort + ": " + ex.Message;
            return;
        }
        server.RequestCompleted += (s, e) => MainThread.BeginInvokeOnMainThread(RefreshDevices);
        _server = server;
        HoldActivity();
#if IOS || MACCATALYST
        Announce();
#endif
    }

    private void StopServing()
    {
        ChatServer server = _server;
        _server = null;
        server?.Dispose();
#if IOS || MACCATALYST
        _bonjour?.Stop();
        _bonjour?.Dispose();
        _bonjour = null;
        if (_activity != null)
        {
            NSProcessInfo.ProcessInfo.EndActivity(_activity);
            _activity = null;
        }
#endif
    }

    // What the server serves for a request: the page's model client and its model, read on the UI thread; null when no
    // model on the Mac is ready (the phone gets 503).
    private ServedChat Served() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var page = Host?.Page as ChatPage;
        IChatClient client = page?.ModelChatClient;
        ChatModelOption option = page?.SelectedModel;
        if (client == null || option == null)
            return null;
        var model = page.LoadedModel;
        var served = new ServedModel(ModelId(option), option.DisplayName)
        {
            ContextLength = model?.MaxNumTokens > 0 ? model.MaxNumTokens : option.ContextWindow,
            Vision = model?.SupportsImages == true,
            Audio = model?.SupportsAudio == true,
            Tools = option.CanUseTools && model?.SupportsToolCalling != false,
            Thinking = option.CanThink
        };
        return new ServedChat(client, served, BeginRequest);
    }).GetAwaiter().GetResult();

    // The page's search model for a phone's POST /v1/embeddings: loaded on the first request, shared with the Mac's own
    // searches (one embedding at a time, searches first); null when the page has none, it was deleted, or it is itself a
    // server's (the phone gets 503). Its id is the model's file name, the key a phone's indexes use: a phone with the same
    // model keeps its embeddings when it switches to the Mac's.
    private ServedEmbeddings ServedSearch()
    {
        SearchModelFeature search = Host?.Services.Get<SearchModelFeature>();
        if (search == null || search.Selected.Remote != null || search.DownloadRequired)
            return null;
        string id = search.ModelFile;
        SearchEmbedder embedder = search.Embedder;
        var generator = new LiteRtLmEmbeddingGenerator(cancel => embedder.EnsureAsync(null, cancel), id, embedder.EnterSearchAsync);
        return new ServedEmbeddings(generator, new ServedModel(id, search.ModelName));
    }

    // "Gemma 4 E4B" -> "gemma-4-e4b".
    private static string ModelId(ChatModelOption option) =>
        string.Join("-", option.DisplayName.ToLowerInvariant().Split(new[] { ' ', '·', '/' }, StringSplitOptions.RemoveEmptyEntries));

    // A phone's request on the page's model: the page is busy meanwhile, and says whose request it is.
    private IDisposable BeginRequest(string device)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Host?.SetBusy(true);
            Host?.SetStatus("Answering " + device + "...");
        });
        return new Done(() => MainThread.BeginInvokeOnMainThread(() =>
        {
            Host?.SetBusy(false);
            Host?.SetStatus(null);
        }));
    }

    private sealed class Done : IDisposable
    {
        private Action _done;

        public Done(Action done) => _done = done;

        public void Dispose() => Interlocked.Exchange(ref _done, null)?.Invoke();
    }

    private void HoldActivity()
    {
#if IOS || MACCATALYST
        if (_activity != null)
            NSProcessInfo.ProcessInfo.EndActivity(_activity);
        // App Nap would slow a hidden app's replies; with "Keep this Mac awake" the Mac doesn't idle-sleep either.
        NSActivityOptions options = NSActivityOptions.UserInitiated
            | (Host?.Settings.Get(AwakeKey, true) == true ? NSActivityOptions.IdleSystemSleepDisabled : 0);
        _activity = NSProcessInfo.ProcessInfo.BeginActivity(options, "Serving the chat's model to phones");
#endif
    }

#if IOS || MACCATALYST
    // Bonjour: the Mac's name, with the model in the TXT record, so a phone lists it and finds it again at a new address.
    private void Announce()
    {
        if (_server == null)
            return;
        _bonjour?.Stop();
        _bonjour?.Dispose();
        string model = (Host?.Page as ChatPage)?.SelectedModel?.DisplayName ?? string.Empty;
        var service = new NSNetService(string.Empty, BonjourType + ".", ServerName, _server.Port);
        var txt = NSDictionary.FromObjectsAndKeys(
            new NSObject[] { NSData.FromString(model), NSData.FromString("1") },
            new NSObject[] { new NSString("model"), new NSString("v") });
        service.SetTxtRecordData(NSNetService.DataFromTxtRecord(txt));
        service.Publish();
        _bonjour = service;
    }
#endif

    private void UpdateStatus()
    {
        if (_status == null)
            return;
        _serving.IsVisible = IsServing || _problem != null;
        if (_problem != null && !IsServing)
        {
            _status.Text = _problem;
            return;
        }
        if (!IsServing)
            return;
        var page = Host?.Page as ChatPage;
        string model = page?.SelectedModel == null ? "no model" : page.SelectedModel.DisplayName + (page.SelectedModel.IsRemote ? " (a remote model can't be served)" : "");
        IList<string> addresses = ChatServer.LocalAddresses();
        _status.Text = "Serving " + model + " as \"" + ServerName + "\" on port " + _server.Port +
            (addresses.Count > 0 ? " - " + string.Join(", ", addresses.Select(a => "http://" + a + ":" + _server.Port + "/v1")) : "") + ".";
    }

    private void RefreshDevices()
    {
        if (_devices == null || _pairing == null)
            return;
        _devices.Children.Clear();
        IReadOnlyList<PairedDevice> devices = _pairing.Devices;
        if (devices.Count == 0)
        {
            _devices.Children.Add(ChatPage.Note("No phone paired yet."));
            return;
        }
        foreach (PairedDevice device in devices)
        {
            PairedDevice d = device;
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
            string last = d.LastUsed is DateTime used ? "last request " + Ago(used) : "paired " + Ago(d.Paired);
            row.Add(ChatPage.Note(d.Name + " - " + last), 0, 0);
            row.Add(ChatPage.Link("Remove", () => _ = RemoveAsync(d)), 1, 0);
            _devices.Children.Add(row);
        }
    }

    private static string Ago(DateTime utc)
    {
        TimeSpan ago = DateTime.UtcNow - utc;
        return ago.TotalMinutes < 1 ? "just now" : ago.TotalHours < 1 ? (int)ago.TotalMinutes + " min ago"
            : ago.TotalDays < 1 ? (int)ago.TotalHours + " h ago" : utc.ToLocalTime().ToString("d MMM");
    }

    private async Task RemoveAsync(PairedDevice device)
    {
        if (Host == null || !await Host.AskAsync("Remove " + device.Name + "?", "It can't use this Mac's model until it pairs again.", "Remove", "Cancel"))
            return;
        _pairing.Remove(device.Id);
        RefreshDevices();
    }

    // The code in a sheet, with the time it has left; it closes when a phone pairs.
    private async Task PairAsync()
    {
        if (Host == null || _pairing == null)
            return;
        string code = _pairing.NewCode();
        _codeLabel = new Label { Text = code.Substring(0, 3) + " " + code.Substring(3), FontSize = 44, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, CharacterSpacing = 4 };
        SemanticProperties.SetDescription(_codeLabel, "Pairing code " + string.Join(" ", code.ToCharArray()));
        Label left = ChatPage.Note(string.Empty);
        left.HorizontalOptions = LayoutOptions.Center;
        void Tick()
        {
            DateTime? expires = _pairing.CodeExpires;
            if (expires == null)
            {
                left.Text = "This code has ended: close and pair again.";
                _codeTimer?.Stop();
                return;
            }
            TimeSpan rest = expires.Value - DateTime.UtcNow;
            left.Text = "Valid for " + (int)rest.TotalMinutes + ":" + rest.Seconds.ToString("00");
        }
        _codeTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_codeTimer != null)
        {
            _codeTimer.Interval = TimeSpan.FromSeconds(1);
            _codeTimer.Tick += (s, e) => Tick();
            _codeTimer.Start();
        }
        Tick();
        var content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                ChatPage.Note("On the phone: Settings > Your own server > Find a Mac, choose \"" + ServerName + "\", and type this code."),
                _codeLabel,
                left
            }
        };
        await Host.ShowSheetAsync("Pair a phone", content, "Close");
        _codeTimer?.Stop();
        _codeTimer = null;
        _codeLabel = null;
        _pairing.CancelCode();
        RefreshDevices();
    }

    // The paired phones' names and token hashes in the page's settings, as JSON.
    private sealed class SettingsTokenStore : IServerTokenStore
    {
        private readonly IChatPageSettings _settings;

        public SettingsTokenStore(IChatPageSettings settings) => _settings = settings;

        private sealed class Row
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Hash { get; set; }
            public DateTime Paired { get; set; }
            public DateTime? LastUsed { get; set; }
        }

        public IList<PairedDevice> Load()
        {
            try
            {
                List<Row> rows = JsonSerializer.Deserialize<List<Row>>(_settings.Get(DevicesKey, "[]")) ?? new List<Row>();
                return rows.Where(r => r.Hash != null).Select(r => new PairedDevice(r.Id, r.Name, r.Hash, r.Paired) { LastUsed = r.LastUsed }).ToList();
            }
            catch (JsonException) { return new List<PairedDevice>(); }
        }

        public void Save(IList<PairedDevice> devices)
        {
            string json = JsonSerializer.Serialize(devices.Select(d => new Row { Id = d.Id, Name = d.Name, Hash = d.TokenHash, Paired = d.Paired, LastUsed = d.LastUsed }).ToList());
            MainThread.BeginInvokeOnMainThread(() => _settings.Set(DevicesKey, json));
        }
    }
}
