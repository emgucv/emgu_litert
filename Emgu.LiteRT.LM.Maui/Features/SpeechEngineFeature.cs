//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Settings > "Speech recognition": what writes down voice messages, the voice chat's words and meetings
/// (<see cref="SpeechRecognizer.Engine"/>; the <see cref="SpeechEngines.SettingKey"/> setting) - the device's own recognizer,
/// or NVIDIA's Parakeet on the device (<see cref="ParakeetSpeech"/>: 25 European languages, the others still go to the
/// device's). Choosing Parakeet downloads it (614 MB, in the background); without a recognizer of the device's (Windows) it
/// is the only one. Shown where Parakeet can run. Storage lists it, and deleting it goes back to the device's recognizer.
/// </summary>
public sealed class SpeechEngineFeature : ChatFeature, IStorageParticipant
{
    private readonly List<(SpeechEngine Value, ToggleChip Chip)> _chips = new();
    private Label _note, _state;
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
    public override void RegisterServices(ChatFeatureServices services) => services.Add<IStorageParticipant>(this);

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        SpeechRecognizer.Engine = SpeechEngines.Parse(host.Settings.Get(SpeechEngines.SettingKey, string.Empty), SpeechRecognizer.SystemSupported);
        ParakeetSpeech.Default.Changed += OnParakeetChanged;
    }

    /// <inheritdoc/>
    public override void OnDetached()
    {
        ParakeetSpeech.Default.Changed -= OnParakeetChanged;
        _downloadCancel?.Cancel();
        base.OnDetached();
    }

    private void OnParakeetChanged() => MainThread.BeginInvokeOnMainThread(UpdateState);

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        if (!ParakeetSpeech.IsAvailable)
            return null;
        var section = new VerticalStackLayout { Spacing = 6, Children = { ChatPage.SectionTitle(Title) } };
        _chips.Clear();
        if (SpeechRecognizer.SystemSupported)
        {
            var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach ((SpeechEngine value, string text) in new[]
            {
                (SpeechEngine.System, "This device's"), (SpeechEngine.Parakeet, "Parakeet (on device, " + ParakeetSpeech.DownloadMegabytes + " MB)")
            })
            {
                var chip = new ToggleChip(text) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == SpeechRecognizer.Engine };
                SpeechEngine captured = value;
                chip.Toggled += (_, e) => OnChipToggled(captured, e.Value);
                _chips.Add((value, chip));
                chips.Children.Add(chip);
            }
            section.Children.Add(chips);
        }
        _note = ChatPage.Note(string.Empty);
        _state = ChatPage.Note(string.Empty);
        _download = new Button { Text = "Download now", FontSize = 14, HorizontalOptions = LayoutOptions.Start, IsVisible = false };
        _download.Clicked += (_, _) => StartDownload();
        SemanticProperties.SetDescription(_download, "Download the Parakeet speech model");
        var credit = ChatPage.Note(ParakeetSpeech.Credit + " (model card)");
        credit.TextDecorations = TextDecorations.Underline;
        var open = new TapGestureRecognizer();
        open.Tapped += async (_, _) =>
        {
            try { await Launcher.Default.OpenAsync(ParakeetSpeech.ModelCardUrl); }
            catch (Exception ex) { Host?.SetStatus("Couldn't open the model card: " + ex.Message); }
        };
        credit.GestureRecognizers.Add(open);
        SemanticProperties.SetDescription(credit, "Open Parakeet's model card");
        section.Children.Add(_note);
        section.Children.Add(_state);
        section.Children.Add(_download);
        section.Children.Add(credit);
        UpdateState();
        return section;
    }

    // The chips act as one choice: tapping the chosen one keeps it. Choosing Parakeet downloads it.
    private void OnChipToggled(SpeechEngine value, bool on)
    {
        if (_updatingChips) return;
        if (on) Choose(value);
        UpdateState();
        if (on && value == SpeechEngine.Parakeet && !ParakeetSpeech.Default.IsDownloaded)
            StartDownload();
    }

    private void Choose(SpeechEngine engine)
    {
        SpeechRecognizer.Engine = engine;
        Host?.Settings.Set(SpeechEngines.SettingKey, SpeechEngines.ToSetting(engine));
    }

    private async void StartDownload()
    {
        if (_downloadCancel != null) return;
        _downloadCancel = new CancellationTokenSource();
        try
        {
            await ParakeetSpeech.Default.EnsureAsync(new Progress<string>(_ => UpdateState()), _downloadCancel.Token);
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

    // The note says what writes recordings down now; the state line, Parakeet's download.
    private void UpdateState()
    {
        if (_note == null) return;
        _updatingChips = true;
        foreach (var (value, chip) in _chips) chip.IsToggled = value == SpeechRecognizer.Engine;
        _updatingChips = false;
        bool parakeet = SpeechRecognizer.Engine == SpeechEngine.Parakeet;
        string languages = "Parakeet knows 25 European languages (English, French, German, Spanish, Italian, Portuguese, Dutch, Polish, Russian, Ukrainian...) and finds which is spoken";
        _note.Text = !SpeechRecognizer.SystemSupported
            ? "Parakeet writes down voice messages on this device. " + languages + "."
            : parakeet
                ? "Parakeet writes down voice messages, the voice chat and meetings on this device, without sending anything away. " + languages + "; for others (Chinese, Japanese...) the device's recognizer is used. In a noisy room it makes more mistakes than the device's recognizer."
                : "The device's speech recognizer writes down voice messages, the voice chat and meetings. Parakeet does it on the device in " + ParakeetSpeech.DownloadMegabytes + " MB: " + languages.Substring("Parakeet knows ".Length) + ".";
        ParakeetSpeech model = ParakeetSpeech.Default;
        bool downloading = _downloadCancel != null && !model.IsDownloaded;
        _state.Text = model.DownloadPercent is double percent ? string.Format("Downloading Parakeet: {0:F0}%", percent)
            : downloading ? "Downloading Parakeet..."
            : model.IsDownloaded ? (model.IsLoaded ? "Parakeet is downloaded and loaded." : "Parakeet is downloaded.")
            : parakeet ? "Parakeet isn't downloaded yet (" + ParakeetSpeech.DownloadMegabytes + " MB)." + (SpeechRecognizer.SystemSupported ? " Until it is, the device's recognizer writes down recordings." : "")
            : string.Empty;
        _state.IsVisible = _state.Text.Length > 0;
        _download.IsVisible = parakeet && !model.IsDownloaded && _downloadCancel == null;
    }

    // ---------- IStorageParticipant ----------

    /// <inheritdoc/>
    public async Task PrepareStorageDeleteAsync(StorageEntry entry)
    {
        if (entry.Kind != StorageKind.SpeechModel) return;
        _downloadCancel?.Cancel();
        await ParakeetSpeech.Default.ReleaseAsync();
        // Back to the device's recognizer, so the 614 MB isn't downloaded again by the next voice message.
        if (SpeechRecognizer.SystemSupported)
            await MainThread.InvokeOnMainThreadAsync(() => Choose(SpeechEngine.System));
    }

    /// <inheritdoc/>
    public void StorageChanged(StorageEntry entry) => MainThread.BeginInvokeOnMainThread(UpdateState);
}
