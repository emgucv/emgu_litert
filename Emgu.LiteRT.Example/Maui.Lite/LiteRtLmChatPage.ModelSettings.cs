#if WINDOWS || IOS || ANDROID || MACCATALYST
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    private const string EmbeddingModelPreference = "litert_lm_chat_embedding_model";
    private readonly string[] _embeddingNames = {
        "EmbeddingGemma 2 Text Vision 440M · CPU", "EmbeddingGemma 2 Multimodal 740M · CPU",
#if WINDOWS_WEBGPU
        "EmbeddingGemma 2 Text Vision 440M · GPU", "EmbeddingGemma 2 Multimodal 740M · GPU",
#endif
    };
    private int _selectedEmbedding = ReadEmbeddingSelection();
    private bool _selectedEmbeddingGpu = ReadEmbeddingGpuSelection();
    private const string EmbeddingGpuPreference = "litert_lm_chat_embedding_windows_gpu";
    private Button _embeddingModelButton;
    private Label _embeddingModelDetail;
    private bool EmbeddingSupportsImages => true;
    // Keep persisted IDs 1 and 2; legacy text-only ID 0 now selects Text Vision.
    private string SelectedEmbeddingName => _embeddingNames[_selectedEmbedding - 1 + (_selectedEmbeddingGpu ? 2 : 0)];
    private string SelectedEmbeddingBackend => _selectedEmbeddingGpu ? "gpu" : "cpu";

    private static bool ReadEmbeddingGpuSelection()
    {
#if WINDOWS_WEBGPU
        try { return Preferences.Default.Get(EmbeddingGpuPreference, false); } catch (Exception) { }
#endif
        return false;
    }

    private static int ReadEmbeddingSelection()
    {
        try { return Preferences.Default.Get(EmbeddingModelPreference, 1) == 2 ? 2 : 1; }
        catch (Exception) { return 1; }
    }

    private EmbeddingModel CreateEmbeddingModel()
    {
        EmbeddingModel model = _selectedEmbedding == 2 ? new EmbeddingGemma2Multimodal() : new EmbeddingGemma2TextVision();
#if WINDOWS_WEBGPU
        model.EnableWindowsWebGpu = _selectedEmbeddingGpu;
        model.VisionBackend = SelectedEmbeddingBackend;
        model.AudioBackend = SelectedEmbeddingBackend;
#endif
        return model;
    }

    private string SelectedEmbeddingFile
    {
        get { using var model = CreateEmbeddingModel(); return System.IO.Path.GetFileName(model.DefaultModelFile.Url); }
    }

    private View BuildEmbeddingModelSection()
    {
        _embeddingModelButton = new Button { Text = SelectedEmbeddingName, HorizontalOptions = LayoutOptions.Fill };
        _embeddingModelButton.Clicked += OnEmbeddingModelTapped;
        _embeddingModelDetail = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
        UpdateEmbeddingModelDetail();
        return new VerticalStackLayout { Spacing = 6, Children = { Theme.SectionTitle("Embedding model"), _embeddingModelButton, _embeddingModelDetail } };
    }

    private void UpdateEmbeddingModelDetail()
    {
        _embeddingModelButton.Text = SelectedEmbeddingName;
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
    }

    private async void OnEmbeddingModelTapped(object sender, EventArgs e)
    {
        if (!CanChangeModel) return;
        // Reorder only the displayed choices; keep the existing model/backend mapping and saved selections.
        string[] entries = _embeddingNames.Skip(2).Concat(_embeddingNames.Take(2)).ToArray();
        string selected = await DisplayActionSheetAsync("Embedding model", "Cancel", null, entries);
        int entry = Array.IndexOf(_embeddingNames, selected);
        if (entry < 0 || !CanChangeModel) return;
        int index = entry % 2 + 1;
        bool gpu = entry >= 2;
        if (index == _selectedEmbedding && gpu == _selectedEmbeddingGpu) return;
        _reloadingCpuThreads = true;
        _cpuReloadTask = ChangeEmbeddingModelAsync(index, gpu);
        await _cpuReloadTask;
    }

    private async Task ChangeEmbeddingModelAsync(int index, bool gpu)
    {
        _settingsView.IsEnabled = false;
        try
        {
            _photoIndexing?.Cancel();
            try { await (_photoIndexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
            if (_document?.Ready != null) await _document.Ready;
            SetBusy(true);
            bool reload = _embedderLoad != null;
            await _embedderGate.WaitAsync();
            try { await ReleaseEmbedderAsync(); }
            finally { _embedderGate.Release(); }
            bool modelChanged = _selectedEmbedding != index;
            _selectedEmbedding = index;
            _selectedEmbeddingGpu = gpu;
            Preferences.Default.Set(EmbeddingModelPreference, index);
            Preferences.Default.Set(EmbeddingGpuPreference, gpu);
            UpdateEmbeddingModelDetail();
            if (modelChanged) await _photoIndex.ClearAsync();
            if (!EmbeddingSupportsImages && _photoSwitch.IsToggled)
                _photoSwitch.IsToggled = false;
            _photoSwitch.IsEnabled = EmbeddingSupportsImages;
            string documentId = _document?.Id;
            RemoveDocument();
            if (documentId != null) await RestoreDocumentAsync(documentId);
            else if (reload) await EnsureEmbedderAsync(null, CancellationToken.None);
            SetStatus(_embedder != null ? "Embedding model ready: " + SelectedEmbeddingName : "Embedding model selected. It will load when search needs it.");
        }
        catch (Exception ex) { SetStatus("Could not switch embedding model: " + ex.Message); }
        finally
        {
            _reloadingCpuThreads = false;
            _settingsView.IsEnabled = true;
            SetBusy(false);
            if (_pageVisible) StartPhotoIndexing();
        }
    }

    private View BuildImageModelSelector()
    {
        var button = new Button { Text = "Z-Image-Turbo · CPU", HorizontalOptions = LayoutOptions.Fill };
        button.Clicked += async (_, _) =>
        {
            if (!CanChangeModel) return;
            await DisplayActionSheetAsync("Image-generation model", "Cancel", null, "Z-Image-Turbo · CPU");
        };
        return new VerticalStackLayout { Spacing = 6, Children = { Theme.SectionTitle("Image-generation model"), button,
            new Label { Text = "Z-Image-Turbo is the only available image model. 10.6 GB download, 256 × 256 images. GPU execution is not supported by this selector.", FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText } } };
    }
}
#endif
