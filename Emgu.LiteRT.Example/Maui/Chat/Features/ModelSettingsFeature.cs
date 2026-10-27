// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Maui.Documents;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite;

// Settings > "Embedding model": the search model document, photo and past-chat search share (EmbeddingGemma 2 Text
// Vision or Multimodal; on Windows also on the GPU), kept loaded by the SearchEmbedder this feature owns; switching it
// reloads the model and rebuilds the indexes. Also the image-generation model's selector (one choice today).
internal sealed class ModelSettingsFeature : DemoFeature, ISearchModelChoice
{
    private readonly string[] _embeddingNames = {
        "EmbeddingGemma 2 Text Vision 440M · CPU", "EmbeddingGemma 2 Multimodal 740M · CPU",
#if WINDOWS_WEBGPU
        "EmbeddingGemma 2 Text Vision 440M · GPU", "EmbeddingGemma 2 Multimodal 740M · GPU",
#endif
    };
    // The page's settings keys ("litert_lm_chat_" + these, the demo's).
    private const string EmbeddingModelKey = "embedding_model";
    private const string EmbeddingGpuKey = "embedding_windows_gpu";

    // 1 Text Vision, 2 Multimodal (read from the settings when attached; the old text-only 0 reads as 1).
    private int _selectedEmbedding = 1;
    private bool _selectedEmbeddingGpu;
    private Button _embeddingModelButton;
    private Label _embeddingModelDetail;

    public ModelSettingsFeature()
    {
        Embedder = new SearchEmbedder(this);
    }

    public override string Name => "search_model";

    public override string Title => "Embedding model";

    public override bool PersistEnabled => false;

    // The search model the searches share: loaded once (downloaded the first time), one embedding at a time.
    public SearchEmbedder Embedder { get; }

    // Whether the search model embeds images too (photo search and a document's pictures need it).
    public bool SupportsImages => true;

    // Keep persisted IDs 1 and 2; legacy text-only ID 0 now selects Text Vision.
    public string SearchModelName => _embeddingNames[_selectedEmbedding - 1 + (_selectedEmbeddingGpu ? 2 : 0)];

    private string SearchModelBackend => _selectedEmbeddingGpu ? "gpu" : "cpu";

    // The file the chosen model is (indexes record it, so another model rebuilds them).
    public string SearchModelFile
    {
        get { using var model = CreateEmbeddingModel(); return System.IO.Path.GetFileName(model.DefaultModelFile.Url); }
    }

    // The user deleted the search model in Settings > Storage: it isn't downloaded again until they ask.
    public bool SearchDownloadRequired => StorageFeature.IsSearchModelDeleted(Host?.Settings, SearchModelFile);

    private DocumentsFeature Documents => Host.Services.Get<DocumentsFeature>();

    private StorageFeature Storage => Host.Services.Get<StorageFeature>();

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        _selectedEmbedding = host.Settings.Get(EmbeddingModelKey, 1) == 2 ? 2 : 1;
#if WINDOWS_WEBGPU
        _selectedEmbeddingGpu = host.Settings.Get(EmbeddingGpuKey, false);
#endif
    }

    // ---------- ISearchModelChoice ----------

    EmbeddingModel ISearchModelChoice.CreateModel() => CreateEmbeddingModel();

    string ISearchModelChoice.ModelName => SearchModelName;

    string ISearchModelChoice.Backend => SearchModelBackend;

    bool ISearchModelChoice.UsesGpu => _selectedEmbeddingGpu;

    bool ISearchModelChoice.DownloadRequired => SearchDownloadRequired;

    async Task<bool> ISearchModelChoice.SwitchToCpuAsync(string error)
    {
        bool useCpu = await MainThread.InvokeOnMainThreadAsync(() => Host.AskAsync(
            "GPU embedding model could not load", error + "\n\nRun this model on CPU instead?", "Run on CPU", "Cancel"));
        if (!useCpu) return false;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _selectedEmbeddingGpu = false;
            Host.Settings.Set(EmbeddingGpuKey, false);
            UpdateEmbeddingModelDetail();
        });
        return true;
    }

    public EmbeddingModel CreateEmbeddingModel()
    {
        EmbeddingModel model = _selectedEmbedding == 2 ? new EmbeddingGemma2Multimodal() : new EmbeddingGemma2TextVision();
#if WINDOWS_WEBGPU
        model.EnableWindowsWebGpu = _selectedEmbeddingGpu;
        model.VisionBackend = SearchModelBackend;
        model.AudioBackend = SearchModelBackend;
#endif
        return model;
    }

    public override View BuildSettingsSection()
    {
        _embeddingModelButton = new Button { Text = SearchModelName, HorizontalOptions = LayoutOptions.Fill };
        _embeddingModelButton.Clicked += OnEmbeddingModelTapped;
        _embeddingModelDetail = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
        UpdateEmbeddingModelDetail();
        return new VerticalStackLayout { Spacing = 6, Children = { Theme.SectionTitle("Embedding model"), _embeddingModelButton, _embeddingModelDetail } };
    }

    private void UpdateEmbeddingModelDetail()
    {
        if (_embeddingModelButton == null) return;
        _embeddingModelButton.Text = SearchModelName;
        _embeddingModelDetail.Text = _selectedEmbedding switch
        {
            2 => "485 MB download. Text, images and audio embeddings. Document and photo search currently use text and images.",
            _ => "388 MB download. Text and image embeddings for document and photo search."
        };
        _embeddingModelDetail.Text += " Switching reloads the search model. Changing models rebuilds cached embeddings. Models download on first use.";
        if (_selectedEmbeddingGpu)
            _embeddingModelDetail.Text += (_selectedEmbedding == 2
                ? " Text, vision and audio embeddings use Windows GPU."
                : " Text and vision embeddings use Windows GPU.") + " A compatible Direct3D 12 GPU is required.";
        Storage?.RefreshSearchState();
    }

    private async void OnEmbeddingModelTapped(object sender, EventArgs e)
    {
        if (Host.IsBusy) return;
        // Reorder only the displayed choices; keep the existing model/backend mapping and saved selections.
        string[] entries = _embeddingNames.Skip(2).Concat(_embeddingNames.Take(2)).ToArray();
        string selected = await Host.Page.DisplayActionSheetAsync("Embedding model", "Cancel", null, entries);
        int entry = Array.IndexOf(_embeddingNames, selected);
        if (entry < 0 || Host.IsBusy) return;
        int index = entry % 2 + 1;
        bool gpu = entry >= 2;
        if (index == _selectedEmbedding && gpu == _selectedEmbeddingGpu) return;
        await Host.RunReloadAsync(() => ChangeEmbeddingModelAsync(index, gpu));
    }

    private async Task ChangeEmbeddingModelAsync(int index, bool gpu)
    {
        PhotoSearchFeature photos = Host.Services.Get<PhotoSearchFeature>();
        PastChatsFeature chats = Host.Services.Get<PastChatsFeature>();
        DocumentsFeature documents = Documents;
        try
        {
            photos.CancelIndexing();
            await chats.StopIndexingAsync();
            await photos.WaitForIndexingAsync();
            if (documents?.DocumentReady != null) await documents.DocumentReady;
            Host.SetBusy(true);
            bool reload = Embedder.IsLoaded;
            await Embedder.ReleaseWithGateAsync();
            bool modelChanged = _selectedEmbedding != index;
            _selectedEmbedding = index;
            _selectedEmbeddingGpu = gpu;
            Host.Settings.Set(EmbeddingModelKey, index);
            Host.Settings.Set(EmbeddingGpuKey, gpu);
            UpdateEmbeddingModelDetail();
            if (modelChanged) await photos.ClearIndexAsync();
            if (!SupportsImages && photos.On)
                photos.SwitchOff();
            photos.SetSwitchEnabled(SupportsImages && !SearchDownloadRequired);
            string documentId = documents?.DocumentId;
            documents?.RemoveDocument();
            if (documentId != null && documents != null) await documents.RestoreDocumentAsync(documentId);
            else if (reload) await Embedder.EnsureAsync(null, CancellationToken.None);
            Host.SetStatus(Embedder.Model != null ? "Embedding model ready: " + SearchModelName : "Embedding model selected. It will load when search needs it.");
        }
        catch (Exception ex) { Host.SetStatus("Could not switch embedding model: " + ex.Message); }
        finally
        {
            Host.SetBusy(false);
            if (PageVisible) photos.StartIndexing();
            if (PageVisible) chats.StartIndexing();
        }
    }

    // The image-generation model's selector, for the image feature's settings section.
    public View BuildImageModelSelector()
    {
        var button = new Button { Text = "Z-Image-Turbo · CPU", HorizontalOptions = LayoutOptions.Fill };
        button.Clicked += async (_, _) =>
        {
            if (Host.IsBusy) return;
            await Host.Page.DisplayActionSheetAsync("Image-generation model", "Cancel", null, "Z-Image-Turbo · CPU");
        };
        return new VerticalStackLayout { Spacing = 6, Children = { Theme.SectionTitle("Image-generation model"), button,
            new Label { Text = "Z-Image-Turbo is the only available image model. 10.6 GB download, 256 × 256 images. GPU execution is not supported by this selector.", FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText } } };
    }
}
