#if WINDOWS || IOS || ANDROID || MACCATALYST
using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    private const string EmbeddingModelPreference = "litert_lm_chat_embedding_model";
    private readonly string[] _embeddingNames = { "EmbeddingGemma 2 Text Vision 440M · CPU", "EmbeddingGemma 2 Multimodal 740M · CPU" };
    private int _selectedEmbedding = ReadEmbeddingSelection();
    private Button _embeddingModelButton;
    private Label _embeddingModelDetail;
    private bool EmbeddingSupportsImages => true;
    // Keep persisted IDs 1 and 2; legacy text-only ID 0 now selects Text Vision.
    private string SelectedEmbeddingName => _embeddingNames[_selectedEmbedding - 1];

    private static int ReadEmbeddingSelection()
    {
        try { return Preferences.Default.Get(EmbeddingModelPreference, 1) == 2 ? 2 : 1; }
        catch (Exception) { return 1; }
    }

    private EmbeddingModel CreateEmbeddingModel() => _selectedEmbedding switch
    {
        2 => new EmbeddingGemma2Multimodal(),
        _ => new EmbeddingGemma2TextVision()
    };

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
        _embeddingModelDetail.Text += " Switching reloads the search model and rebuilds cached embeddings. Models download on first use. CPU only in this selector.";
    }

    private async void OnEmbeddingModelTapped(object sender, EventArgs e)
    {
        if (!CanChangeModel) return;
        string selected = await DisplayActionSheetAsync("Embedding model", "Cancel", null, _embeddingNames);
        int index = Array.IndexOf(_embeddingNames, selected) + 1;
        if (index == 0 || index == _selectedEmbedding || !CanChangeModel) return;
        _reloadingCpuThreads = true;
        _cpuReloadTask = ChangeEmbeddingModelAsync(index);
        await _cpuReloadTask;
    }

    private async Task ChangeEmbeddingModelAsync(int index)
    {
        _settingsView.IsEnabled = false;
        try
        {
            _photoIndexing?.Cancel();
            try { await (_photoIndexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
            if (_document?.Ready != null) await _document.Ready;
            SetBusy(true);
            await _embedderGate.WaitAsync();
            try { await ReleaseEmbedderAsync(); }
            finally { _embedderGate.Release(); }
            _selectedEmbedding = index;
            Preferences.Default.Set(EmbeddingModelPreference, index);
            UpdateEmbeddingModelDetail();
            await _photoIndex.ClearAsync();
            if (!EmbeddingSupportsImages && _photoSwitch.IsToggled)
                _photoSwitch.IsToggled = false;
            _photoSwitch.IsEnabled = EmbeddingSupportsImages;
            string documentId = _document?.Id;
            RemoveDocument();
            if (documentId != null) await RestoreDocumentAsync(documentId);
            SetStatus("Embedding model selected. It will load when search needs it.");
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
