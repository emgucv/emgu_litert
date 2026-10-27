//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// One search model a <see cref="SearchModelFeature"/> offers: its name, a factory, a line about it, and its backend.
/// </summary>
public sealed class SearchModelOption
{
    /// <summary>Create an option.</summary>
    /// <param name="key">Its key, kept in the settings (e.g. "text_vision")</param>
    /// <param name="name">Its name in the list and in status lines</param>
    /// <param name="create">Makes a new instance of the model (not loaded), set up for the backend</param>
    /// <param name="detail">A line about it (its download, what it embeds), or null</param>
    public SearchModelOption(string key, string name, Func<EmbeddingModel> create, string detail = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Name = name ?? key;
        Create = create ?? throw new ArgumentNullException(nameof(create));
        Detail = detail;
    }

    /// <summary>Its key, kept in the settings.</summary>
    public string Key { get; }

    /// <summary>Its name.</summary>
    public string Name { get; }

    /// <summary>Makes a new instance of the model.</summary>
    public Func<EmbeddingModel> Create { get; }

    /// <summary>A line about it, or null.</summary>
    public string Detail { get; }

    /// <summary>"cpu" (the default) or "gpu".</summary>
    public string Backend { get; set; } = "cpu";

    /// <summary>Whether it runs on the GPU.</summary>
    public bool UsesGpu => Backend == "gpu";

    /// <summary>Whether it embeds images (a document's pictures and photo search need it); true by default.</summary>
    public bool SupportsImages { get; set; } = true;

    /// <summary>For a GPU option: the key of the same model on the CPU, offered when the GPU fails to load it; or null.</summary>
    public string CpuFallback { get; set; }

    /// <summary>EmbeddingGemma 2 Text Vision 440M on the CPU: text and image embeddings (388 MB).</summary>
    public static SearchModelOption EmbeddingGemma2TextVision() =>
        new SearchModelOption("text_vision", "EmbeddingGemma 2 Text Vision 440M · CPU", () => new EmbeddingGemma2TextVision(),
            "388 MB download. Text and image embeddings for document and photo search.");

    /// <summary>EmbeddingGemma 2 Multimodal 740M on the CPU: text, image and audio embeddings (485 MB).</summary>
    public static SearchModelOption EmbeddingGemma2Multimodal() =>
        new SearchModelOption("multimodal", "EmbeddingGemma 2 Multimodal 740M · CPU", () => new EmbeddingGemma2Multimodal(),
            "485 MB download. Text, images and audio embeddings. Document and photo search currently use text and images.");
}

/// <summary>
/// Settings > "Embedding model": the search model the page's searches share (past chats, documents, photos), chosen
/// from a list (EmbeddingGemma 2 Text Vision and Multimodal by default; the "search_model" setting keeps the choice's
/// key). It owns the <see cref="SearchEmbedder"/> and is the <see cref="ISearchModelChoice"/>; <see cref="CreateServices"/>
/// makes the <see cref="SearchServices"/> for the search features. Switching stops the <see cref="ISearchModelParticipant"/>s,
/// lets go of the old model, and has them rebuild what the old model made (another model's embeddings don't match); a
/// GPU option that fails to load offers its <see cref="SearchModelOption.CpuFallback"/>. No tools.
/// </summary>
public sealed class SearchModelFeature : ChatFeature, ISearchModelChoice
{
    /// <summary>The settings key of the chosen option's <see cref="SearchModelOption.Key"/>.</summary>
    public const string SettingKey = "search_model";

    private readonly List<SearchModelOption> _options;
    private int _selected;
    private SearchServices _services;
    private Button _button;
    private Label _detail;

    /// <summary>Create the feature.</summary>
    /// <param name="options">The models to choose from, the default first; null for EmbeddingGemma 2 Text Vision and Multimodal on the CPU</param>
    public SearchModelFeature(IEnumerable<SearchModelOption> options = null)
    {
        _options = (options ?? new[] { SearchModelOption.EmbeddingGemma2TextVision(), SearchModelOption.EmbeddingGemma2Multimodal() }).ToList();
        if (_options.Count == 0)
            throw new ArgumentException("No search model to choose from.", nameof(options));
        Embedder = new SearchEmbedder(this);
    }

    /// <inheritdoc/>
    public override string Name => "search_model";

    /// <inheritdoc/>
    public override string Title => "Embedding model";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <summary>The models to choose from.</summary>
    public IReadOnlyList<SearchModelOption> Options => _options;

    /// <summary>The chosen model.</summary>
    public SearchModelOption Selected => _options[_selected];

    /// <summary>The search model the searches share: loaded once (downloaded the first time), one embedding at a time.</summary>
    public SearchEmbedder Embedder { get; }

    /// <summary>The chosen model's file name: the indexes record it, so another model's passages are rebuilt.</summary>
    public string ModelFile
    {
        get
        {
            using EmbeddingModel model = Selected.Create();
            return Path.GetFileName(model.DefaultModelFile.Url);
        }
    }

    /// <summary>
    /// The services for the search features (<see cref="PastChatsFeature"/>, <see cref="PhotoSearchFeature"/>, a
    /// documents feature): this choice, its embedder, the workload and the model's file as the indexes' key.
    /// </summary>
    /// <param name="workload">When background indexing may run; null for <see cref="DeviceIndexingWorkload"/></param>
    public SearchServices CreateServices(IIndexingWorkload workload = null)
    {
        _services = new SearchServices
        {
            Choice = this,
            Embedder = Embedder,
            Workload = workload ?? new DeviceIndexingWorkload(),
            IndexModel = () => ModelFile,
            DownloadRequired = () => DownloadRequired,
            SupportsImages = Selected.SupportsImages
        };
        return _services;
    }

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        string key = host.Settings.Get(SettingKey, string.Empty);
        _selected = Math.Max(0, _options.FindIndex(o => o.Key == key));
        if (_services != null)
            _services.SupportsImages = Selected.SupportsImages;
    }

    // ---------- ISearchModelChoice ----------

    /// <inheritdoc/>
    public EmbeddingModel CreateModel() => Selected.Create();

    /// <inheritdoc/>
    public string ModelName => Selected.Name;

    /// <inheritdoc/>
    public string Backend => Selected.Backend;

    /// <inheritdoc/>
    public bool UsesGpu => Selected.UsesGpu;

    /// <summary>The user deleted the model in Settings > Storage: it isn't downloaded again until they ask.</summary>
    public bool DownloadRequired => StorageFeature.IsSearchModelDeleted(Host?.Settings, ModelFile);

    /// <inheritdoc/>
    public async Task<bool> SwitchToCpuAsync(string error)
    {
        int fallback = Selected.CpuFallback == null ? -1 : _options.FindIndex(o => o.Key == Selected.CpuFallback);
        if (fallback < 0 || Host == null)
            return false;
        bool useCpu = await MainThread.InvokeOnMainThreadAsync(() => Host.AskAsync(
            "GPU embedding model could not load", error + "\n\nRun this model on CPU instead?", "Run on CPU", "Cancel"));
        if (!useCpu)
            return false;
        // The same model on another backend: its embeddings stay valid, nothing is rebuilt.
        await MainThread.InvokeOnMainThreadAsync(() => Select(fallback));
        return true;
    }

    // ---------- The settings section ----------

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        var title = new Label { Text = Title };
        ChatResources.ApplyStyle(title, "ChatSectionTextStyle");
        // A button with the model's whole name, and a list of the names (a picker's wheel cuts long names off on a Mac).
        _button = new Button { HorizontalOptions = LayoutOptions.Fill };
        _button.Clicked += OnChooseClicked;
        SemanticProperties.SetDescription(_button, "Choose the model document, photo and past-chat search use");
        _detail = ChatPage.Note(string.Empty);
        UpdateDetail();
        return new VerticalStackLayout { Spacing = 6, Children = { title, _button, _detail } };
    }

    private void UpdateDetail()
    {
        if (_button != null)
            _button.Text = Selected.Name;
        if (_detail != null)
            _detail.Text = (Selected.Detail == null ? string.Empty : Selected.Detail + " ")
                + "Switching reloads the search model; another model rebuilds the saved embeddings. Models download on first use.";
    }

    private void Select(int index)
    {
        _selected = index;
        Host?.Settings.Set(SettingKey, Selected.Key);
        if (_services != null)
            _services.SupportsImages = Selected.SupportsImages;
        UpdateDetail();
    }

    private async void OnChooseClicked(object sender, EventArgs e)
    {
        if (Host.IsBusy)
        {
            Host.SetStatus("Hang on - still busy.");
            return;
        }
        string chosen = await Host.Page.DisplayActionSheetAsync(Title, "Cancel", null, _options.Select(o => o.Name).ToArray());
        int index = _options.FindIndex(o => o.Name == chosen);
        if (index < 0 || index == _selected || Host.IsBusy)
            return;
        await Host.RunReloadAsync(() => SwitchAsync(index));
        // After the reload, not in it: the searches don't start indexing while a reload is going on.
        RestartSearches();
    }

    // The searches show their state again (a model without images, a deleted one) and go on indexing; the storage
    // feature's notices follow the new model too.
    private void RestartSearches()
    {
        StorageFeature storage = Host.Services.Get<StorageFeature>();
        if (storage != null)
            storage.RefreshSearchState();
        else
            foreach (IStorageParticipant participant in Host.Services.GetAll<IStorageParticipant>())
            {
                try { participant.StorageChanged(null); }
                catch (Exception) { }
            }
    }

    // Stop what uses the model, let it go, switch, have the participants rebuild, and load it again if it was loaded.
    private async Task SwitchAsync(int index)
    {
        IReadOnlyList<ISearchModelParticipant> participants = Host.Services.GetAll<ISearchModelParticipant>();
        try
        {
            await Task.WhenAll(participants.Select(p => p.PrepareSearchModelChangeAsync()));
            Host.SetBusy(true);
            bool reload = Embedder.IsLoaded;
            await Embedder.ReleaseWithGateAsync();
            string oldFile = ModelFile;
            Select(index);
            bool otherModel = ModelFile != oldFile;
            foreach (ISearchModelParticipant participant in participants)
                await participant.SearchModelChangedAsync(otherModel);
            if (reload && !DownloadRequired)
                await Embedder.EnsureAsync(null, CancellationToken.None);
            Host.SetStatus(Embedder.Model != null ? "Embedding model ready: " + Selected.Name : "Embedding model selected. It will load when search needs it.");
        }
        catch (Exception ex)
        {
            Host.SetStatus("Could not switch embedding model: " + ex.Message);
        }
        finally
        {
            Host.SetBusy(false);
        }
    }
}
