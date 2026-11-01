//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui.Features;

/// <summary>
/// Settings > "Voice": what says replies, the voice chat, alerts and counts aloud (<see cref="ChatVoice.Engine"/>; the
/// <see cref="VoiceEngines.SettingKey"/> setting) - the device's voices, or Kokoro on the device
/// (<see cref="KokoroSpeech"/>: 9 languages and dialects, the others still in the device's voices), with a voice per
/// language (<see cref="VoiceEngines.VoiceSettingKey"/>) and "Try it". Choosing Kokoro downloads it (295 MB, in the
/// background). Shown where Kokoro can run. Storage lists it, and deleting it goes back to the device's voices.
/// </summary>
public sealed class VoiceFeature : ChatFeature, IStorageParticipant
{
    private readonly List<(VoiceEngine Value, ToggleChip Chip)> _chips = new();
    private Label _note, _state;
    private Button _download, _voice, _try;
    private View _kokoroRow;
    private bool _updatingChips;
    private CancellationTokenSource _downloadCancel, _trying;
    private string _language;

    /// <inheritdoc/>
    public override string Name => "voice_engine";

    /// <inheritdoc/>
    public override string Title => "Voice";

    /// <summary>Always on (nothing to switch).</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services)
    {
        services.Add(this);
        services.Add<IStorageParticipant>(this);
    }

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        ChatVoice.Engine = VoiceEngines.Parse(host.Settings.Get(VoiceEngines.SettingKey, string.Empty));
        ChatVoice.KokoroVoiceFor = language =>
        {
            string voice = Host?.Settings.Get(VoiceEngines.VoiceSettingKey(language), string.Empty);
            return string.IsNullOrEmpty(voice) ? null : voice;
        };
        _language = VoiceEngines.KokoroLanguage(null, ChatVoice.DeviceRegion, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? "en-US";
        KokoroSpeech.Default.Changed += OnKokoroChanged;
    }

    /// <inheritdoc/>
    public override void OnDetached()
    {
        KokoroSpeech.Default.Changed -= OnKokoroChanged;
        _downloadCancel?.Cancel();
        _trying?.Cancel();
        base.OnDetached();
    }

    private void OnKokoroChanged() => MainThread.BeginInvokeOnMainThread(UpdateState);

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        if (!KokoroSpeech.IsAvailable)
            return null;
        var section = new VerticalStackLayout { Spacing = 6, Children = { ChatPage.SectionTitle(Title) } };
        _chips.Clear();
        var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach ((VoiceEngine value, string text) in new[]
        {
            (VoiceEngine.System, "This device's"), (VoiceEngine.Kokoro, "Kokoro (on device, " + KokoroSpeech.DownloadMegabytes + " MB)")
        })
        {
            var chip = new ToggleChip(text) { Margin = new Thickness(0, 0, 6, 6), IsToggled = value == ChatVoice.Engine };
            VoiceEngine captured = value;
            chip.Toggled += (_, e) => OnChipToggled(captured, e.Value);
            _chips.Add((value, chip));
            chips.Children.Add(chip);
        }
        section.Children.Add(chips);
        _note = ChatPage.Note(string.Empty);
        _state = ChatPage.Note(string.Empty);
        _download = new Button { Text = "Download now", FontSize = 14, HorizontalOptions = LayoutOptions.Start, IsVisible = false };
        _download.Clicked += (_, _) => StartDownload();
        SemanticProperties.SetDescription(_download, "Download the Kokoro voice model");
        // The voice for a language, and "Try it".
        _voice = new Button { FontSize = 14, HorizontalOptions = LayoutOptions.Fill };
        _voice.Clicked += OnChooseVoiceClicked;
        SemanticProperties.SetDescription(_voice, "Choose Kokoro's voice for a language");
        _try = new Button { Text = "Try it", FontSize = 14, HorizontalOptions = LayoutOptions.Start };
        _try.Clicked += OnTryClicked;
        SemanticProperties.SetDescription(_try, "Hear the chosen voice");
        _kokoroRow = new VerticalStackLayout { Spacing = 6, Children = { _voice, _try } };
        var credit = ChatPage.Note(KokoroSpeech.Credit + " (model card)");
        credit.TextDecorations = TextDecorations.Underline;
        var open = new TapGestureRecognizer();
        open.Tapped += async (_, _) =>
        {
            try { await Launcher.Default.OpenAsync(KokoroSpeech.ModelCardUrl); }
            catch (Exception ex) { Host?.SetStatus("Couldn't open the model card: " + ex.Message); }
        };
        credit.GestureRecognizers.Add(open);
        SemanticProperties.SetDescription(credit, "Open Kokoro's model card");
        section.Children.Add(_note);
        section.Children.Add(_state);
        section.Children.Add(_download);
        section.Children.Add(_kokoroRow);
        section.Children.Add(credit);
        UpdateState();
        return section;
    }

    // The chips act as one choice: tapping the chosen one keeps it. Choosing Kokoro downloads it.
    private void OnChipToggled(VoiceEngine value, bool on)
    {
        if (_updatingChips) return;
        if (on) Choose(value);
        UpdateState();
        if (on && value == VoiceEngine.Kokoro && !KokoroSpeech.Default.IsDownloaded)
            StartDownload();
    }

    private void Choose(VoiceEngine engine)
    {
        ChatVoice.Engine = engine;
        Host?.Settings.Set(VoiceEngines.SettingKey, VoiceEngines.ToSetting(engine));
    }

    private async void StartDownload()
    {
        if (_downloadCancel != null) return;
        _downloadCancel = new CancellationTokenSource();
        try
        {
            await KokoroSpeech.Default.EnsureAsync(new Progress<string>(_ => UpdateState()), _downloadCancel.Token);
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

    private static string LanguageName(string tag) => VoiceEngines.KokoroLanguages.FirstOrDefault(l => l.Tag == tag).Name ?? tag;

    private string VoiceOf(string language) => ChatVoice.KokoroVoiceFor?.Invoke(language);

    // First the language, then its voices (from the model, loaded if needed).
    private async void OnChooseVoiceClicked(object sender, EventArgs e)
    {
        string language = await Host.ChooseAsync("Language", VoiceEngines.KokoroLanguages.Select(l =>
            new ChatChoice(l.Tag, l.Name, VoiceOf(l.Tag) is string v ? Kokoro82M.DescribeVoice(v) : "The default voice")), _language);
        if (language == null) return;
        _language = language;
        string[] voices;
        try
        {
            _state.Text = "Loading the voices...";
            _state.IsVisible = true;
            voices = await KokoroSpeech.Default.GetVoicesAsync(language, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _state.Text = "Couldn't load the voices: " + ex.Message;
            return;
        }
        UpdateState();
        string chosen = await Host.ChooseAsync(LanguageName(language) + " voice",
            voices.Select(v => new ChatChoice(v, Kokoro82M.DescribeVoice(v), v)), VoiceOf(language) ?? voices.FirstOrDefault());
        if (chosen == null) return;
        Host.Settings.Set(VoiceEngines.VoiceSettingKey(language), chosen);
        UpdateState();
    }

    private async void OnTryClicked(object sender, EventArgs e)
    {
        _trying?.Cancel();
        var trying = _trying = new CancellationTokenSource();
        _try.IsEnabled = false;
        try
        {
            await ChatVoice.Kokoro.SpeakInAsync(SampleSentence(_language), _language, VoiceOf(_language), trying.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Host?.SetStatus("Kokoro couldn't speak: " + ex.Message);
        }
        finally
        {
            if (_try != null) _try.IsEnabled = true;
        }
    }

    private static string SampleSentence(string language) => language switch
    {
        "es" => "Hola. Así suena esta voz cuando lee tus respuestas.",
        "fr" => "Bonjour. Voici comment cette voix lit vos réponses.",
        "it" => "Ciao. Ecco come questa voce legge le tue risposte.",
        "pt-BR" => "Olá. É assim que esta voz lê as suas respostas.",
        "hi" => "नमस्ते। यह आवाज़ आपके जवाब ऐसे पढ़ती है।",
        "zh-CN" => "你好。这个声音会这样朗读你的回复。",
        "ja" => "こんにちは。この声で返事を読み上げます。",
        _ => "Hello. This is how this voice reads your replies aloud.",
    };

    // The note says what speaks now; the state line, Kokoro's download.
    private void UpdateState()
    {
        if (_note == null) return;
        _updatingChips = true;
        foreach (var (value, chip) in _chips) chip.IsToggled = value == ChatVoice.Engine;
        _updatingChips = false;
        bool kokoro = ChatVoice.Engine == VoiceEngine.Kokoro;
        const string languages = "American and British English, Spanish, French, Italian, Brazilian Portuguese, Hindi, Mandarin Chinese and Japanese";
        _note.Text = kokoro
            ? "Kokoro reads replies, the voice chat, alerts and counts aloud on this device, in " + languages + "; other languages use the device's voices."
            : "The device's voices read replies, the voice chat, alerts and counts aloud. Kokoro does it on the device (" + KokoroSpeech.DownloadMegabytes + " MB), in " + languages + ".";
        KokoroSpeech model = KokoroSpeech.Default;
        bool downloading = _downloadCancel != null && !model.IsDownloaded;
        _state.Text = model.DownloadPercent is double percent ? string.Format("Downloading Kokoro: {0:F0}%", percent)
            : downloading ? "Downloading Kokoro..."
            : model.IsDownloaded ? (model.GpuFailure != null ? "Kokoro runs on the CPU here (the GPU couldn't load it)." : string.Empty)
            : kokoro ? "Kokoro isn't downloaded yet (" + KokoroSpeech.DownloadMegabytes + " MB). Until it is, the device's voices speak."
            : string.Empty;
        _state.IsVisible = _state.Text.Length > 0;
        _download.IsVisible = kokoro && !model.IsDownloaded && _downloadCancel == null;
        _kokoroRow.IsVisible = kokoro && model.IsDownloaded;
        string voice = VoiceOf(_language);
        _voice.Text = LanguageName(_language) + ": " + (voice == null ? "the default voice" : Kokoro82M.DescribeVoice(voice));
    }

    // ---------- IStorageParticipant ----------

    /// <inheritdoc/>
    public async Task PrepareStorageDeleteAsync(StorageEntry entry)
    {
        if (entry.Kind != StorageKind.VoiceModel) return;
        _downloadCancel?.Cancel();
        _trying?.Cancel();
        await KokoroSpeech.Default.ReleaseAsync();
        // Back to the device's voices, so the 295 MB isn't downloaded again.
        await MainThread.InvokeOnMainThreadAsync(() => Choose(VoiceEngine.System));
    }

    /// <inheritdoc/>
    public void StorageChanged(StorageEntry entry) => MainThread.BeginInvokeOnMainThread(UpdateState);
}
