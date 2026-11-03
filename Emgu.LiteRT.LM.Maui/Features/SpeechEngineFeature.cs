//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Settings > "Speech recognition": what writes down voice messages, the voice chat's words and meetings
/// (<see cref="SpeechRecognizer.Engine"/>; the <see cref="SpeechEngines.SettingKey"/> setting) - the device's own recognizer,
/// or a model on the device (<see cref="OmniSpeech"/>): NVIDIA's Parakeet (25 European languages) or Alibaba Qwen's
/// Qwen3-ASR (30 languages, Chinese, Japanese and Korean among them). A language the chosen model lacks goes to the other
/// model when it is downloaded, else to the device's recognizer (<see cref="SpeechEngines.Choose"/>). Choosing a model
/// downloads it (in the background); without a recognizer of the device's (Windows) the models are the only ones. Shown
/// where the models can run. Storage lists them, and deleting the chosen one goes back to the device's recognizer.
/// </summary>
public sealed class SpeechEngineFeature : ChatFeature, IStorageParticipant
{
    private readonly List<(SpeechEngine Value, ToggleChip Chip)> _chips = new();
    private Label _note, _state, _credit;
    private View _gpu;

    /// <summary>The settings key of "Run on the GPU" for the models that can (<see cref="OmniSpeech.UseGpu"/>; off by default).</summary>
    public const string GpuSettingKey = "speech_model_gpu";
    private Button _download;
    private bool _updatingChips;
    private CancellationTokenSource _downloadCancel;

    /// <inheritdoc/>
    public override string Name => "speech_engine";

    /// <inheritdoc/>
    public override string Title => "Speech recognition";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        SpeechRecognizer.Engine = SpeechEngines.Parse(host.Settings.Get(SpeechEngines.SettingKey, string.Empty), SpeechRecognizer.SystemSupported);
        bool gpu = host.Settings.Get(GpuSettingKey, false);
        foreach (OmniSpeech model in OmniSpeech.All)
        {
            model.UseGpu = gpu && model.CanUseGpu;
            model.Changed += OnModelChanged;
        }
    }

    /// <inheritdoc/>
    public override void OnDetached()
    {
        foreach (OmniSpeech model in OmniSpeech.All)
            model.Changed -= OnModelChanged;
        _downloadCancel?.Cancel();
        base.OnDetached();
    }

    private void OnModelChanged() => MainThread.BeginInvokeOnMainThread(UpdateState);

    // The model chosen, or Parakeet when the device's recognizer is (its download and credit are shown then).
    private static OmniSpeech ChosenModel => OmniSpeech.For(SpeechRecognizer.Engine);

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        if (!OmniSpeech.IsAvailable)
            return null;
        var section = new VerticalStackLayout { Spacing = 6, Children = { ChatPage.SectionTitle(Title) } };
        _chips.Clear();
        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        var choices = new List<(SpeechEngine, string)>();
        if (SpeechRecognizer.SystemSupported)
            choices.Add((SpeechEngine.System, "This device's"));
        foreach (OmniSpeech model in OmniSpeech.All)
            choices.Add((model.Engine, ShortName(model) + " (on device, " + model.DownloadMegabytes + " MB)"));
        foreach ((SpeechEngine value, string text) in choices)
        {
            var chip = new ToggleChip(text) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == SpeechRecognizer.Engine };
            SpeechEngine captured = value;
            chip.Toggled += (_, e) => OnChipToggled(captured, e.Value);
            _chips.Add((value, chip));
            chips.Children.Add(chip);
        }
        section.Children.Add(chips);
        _note = ChatPage.Note(string.Empty);
        _state = ChatPage.Note(string.Empty);
        _download = new Button { Text = "Download now", FontSize = 14, HorizontalOptions = LayoutOptions.Start, IsVisible = false };
        _download.Clicked += (_, _) => StartDownload(ChosenModel);
        SemanticProperties.SetDescription(_download, "Download the speech model");
        _credit = ChatPage.Note(string.Empty);
        _credit.TextDecorations = TextDecorations.Underline;
        var open = new TapGestureRecognizer();
        open.Tapped += async (_, _) =>
        {
            try { await Launcher.Default.OpenAsync((ChosenModel ?? OmniSpeech.Parakeet).ModelCardUrl); }
            catch (Exception ex) { Host?.SetStatus("Couldn't open the model card: " + ex.Message); }
        };
        _credit.GestureRecognizers.Add(open);
        _gpu = ChatPage.SwitchRow("Run Qwen3-ASR on the GPU",
            "Faster: on a Galaxy S10 it wrote down a 26 s recording in 17 s instead of 27 s, with the same text. But it takes "
            + "about 1 GB more memory than on the CPU and 13 s to load, so beside a chat model on the GPU the phone may run "
            + "short of memory. It goes back to the CPU when the GPU can't load it.",
            Host?.Settings.Get(GpuSettingKey, false) == true, OnGpuToggled);
        section.Children.Add(_note);
        section.Children.Add(_gpu);
        section.Children.Add(_state);
        section.Children.Add(_download);
        section.Children.Add(_credit);
        UpdateState();
        return section;
    }

    private static string ShortName(OmniSpeech model) => model.Engine == SpeechEngine.Qwen3Asr ? "Qwen3-ASR" : "Parakeet";

    // The chips act as one choice: tapping the chosen one keeps it. Choosing a model downloads it.
    private void OnChipToggled(SpeechEngine value, bool on)
    {
        if (_updatingChips) return;
        if (on) Choose(value);
        UpdateState();
        OmniSpeech model = OmniSpeech.For(value);
        if (on && model != null && !model.IsDownloaded)
            StartDownload(model);
    }

    private void OnGpuToggled(bool on)
    {
        Host?.Settings.Set(GpuSettingKey, on);
        foreach (OmniSpeech model in OmniSpeech.All)
            model.UseGpu = on && model.CanUseGpu;
        UpdateState();
    }

    private void Choose(SpeechEngine engine)
    {
        SpeechRecognizer.Engine = engine;
        Host?.Settings.Set(SpeechEngines.SettingKey, SpeechEngines.ToSetting(engine));
    }

    private async void StartDownload(OmniSpeech model)
    {
        if (model == null || _downloadCancel != null) return;
        _downloadCancel = new CancellationTokenSource();
        try
        {
            await model.EnsureAsync(new Progress<string>(_ => UpdateState()), _downloadCancel.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_state != null) _state.Text = "The download failed: " + ex.Message;
            _downloadCancel = null;
            if (_download != null) _download.IsVisible = true;
            return;
        }
        _downloadCancel = null;
        UpdateState();
    }

    private const string ParakeetLanguages = "Parakeet knows 25 European languages (English, French, German, Spanish, Italian, Portuguese, Dutch, Polish, Russian, Ukrainian...)";
    private const string Qwen3AsrLanguages = "Qwen3-ASR knows 30 languages (Chinese and Cantonese, English, Japanese, Korean, Arabic, Hindi, Thai, Vietnamese, Indonesian, Spanish, French, German...)";

    // The note says what writes recordings down now; the state line, the chosen model's download.
    private void UpdateState()
    {
        if (_note == null) return;
        _updatingChips = true;
        foreach (var (value, chip) in _chips) chip.IsToggled = value == SpeechRecognizer.Engine;
        _updatingChips = false;
        OmniSpeech chosen = ChosenModel;
        string device = SpeechRecognizer.SystemSupported ? "the device's recognizer" : "nothing";
        _note.Text = chosen == null
            ? "The device's speech recognizer writes down voice messages, the voice chat and meetings. A model on the device does it without sending anything away: "
                + ParakeetLanguages + "; " + Qwen3AsrLanguages + ". Each finds which language is spoken."
            : chosen.Engine == SpeechEngine.Parakeet
                ? "Parakeet writes down voice messages, the voice chat and meetings on this device, without sending anything away. " + ParakeetLanguages
                    + " and finds which is spoken; for others (Chinese, Japanese...) Qwen3-ASR is used when it is downloaded, else " + device + ". In a noisy room it makes more mistakes than the device's recognizer."
                : "Qwen3-ASR writes down voice messages, the voice chat and meetings on this device, without sending anything away. " + Qwen3AsrLanguages
                    + " and finds which is spoken; for others Parakeet is used when it is downloaded, else " + device + ". It is slower and larger than Parakeet (about 400 MB in memory) and makes fewer mistakes.";
        OmniSpeech model = chosen ?? OmniSpeech.Parakeet;
        string name = ShortName(model);
        bool downloading = _downloadCancel != null && !model.IsDownloaded;
        _state.Text = model.DownloadPercent is double percent ? string.Format("Downloading {0}: {1:F0}%", name, percent)
            : downloading ? "Downloading " + name + "..."
            : model.IsDownloaded ? (model.IsLoaded ? name + " is downloaded and loaded" + (model.OnGpu ? " on the GPU." : ".") : name + " is downloaded.")
            : chosen != null ? name + " isn't downloaded yet (" + model.DownloadMegabytes + " MB)." + (SpeechRecognizer.SystemSupported ? " Until it is, the device's recognizer writes down recordings." : "")
            : string.Empty;
        _state.IsVisible = _state.Text.Length > 0;
        _download.IsVisible = chosen != null && !model.IsDownloaded && _downloadCancel == null;
        _gpu.IsVisible = chosen != null && chosen.CanUseGpu && OmniSpeech.GpuAvailable;
        _credit.Text = model.Credit + " (model card)";
        SemanticProperties.SetDescription(_credit, "Open " + name + "'s model card");
    }

    // ---------- IStorageParticipant ----------

    /// <inheritdoc/>
    public async Task PrepareStorageDeleteAsync(StorageEntry entry)
    {
        if (entry.Kind != StorageKind.SpeechModel) return;
        OmniSpeech model = OmniSpeech.All.FirstOrDefault(m => m.Name == entry.Name);
        if (model == null) return;
        if (ChosenModel == model) _downloadCancel?.Cancel();
        await model.ReleaseAsync();
        // The chosen one: back to the device's recognizer, so it isn't downloaded again by the next voice message.
        if (SpeechRecognizer.SystemSupported && SpeechRecognizer.Engine == model.Engine)
            await MainThread.InvokeOnMainThreadAsync(() => Choose(SpeechEngine.System));
    }

    /// <inheritdoc/>
    public void StorageChanged(StorageEntry entry) => MainThread.BeginInvokeOnMainThread(UpdateState);
}
