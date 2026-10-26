// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Globalization;
using System.IO;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

// Alert sounds (TODO item 53): the sounds the play_file alert action and a sound monitor's playFile option can play - the app's soft white noise and music-box
// lullaby, and the user's own, added from a file or recorded - kept by AlertSoundLibrary and managed in Settings > "Alert sounds" (preview with play,
// delete). While one plays the sound monitors don't listen (BeginSoundMute), so a lullaby can't be heard as a baby, and it stops if its monitor goes.
internal sealed partial class CameraFeature
{
    private readonly AlertSoundLibrary _alertSounds = new(System.IO.Path.Combine(FileSystem.AppDataDirectory, "alert-sounds"),
        async (name, _) => await FileSystem.OpenAppPackageFileAsync(name));
    private VerticalStackLayout _alertSoundRows;
    private Label _alertSoundStatus;
    private Button _alertSoundRecordButton;
    private readonly VoiceRecorder _alertRecorder = new VoiceRecorder();
    private DateTime _alertRecordingStarted;
    private CancellationTokenSource _alertPreview;

    private static readonly FilePickerFileType AlertFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.iOS, new[] { "com.microsoft.waveform-audio", "public.mp3", "public.mpeg-4-audio", "org.xiph.flac", "public.aac-audio" } },
        { DevicePlatform.MacCatalyst, new[] { "com.microsoft.waveform-audio", "public.mp3", "public.mpeg-4-audio", "org.xiph.flac", "public.aac-audio" } },
        { DevicePlatform.Android, new[] { "audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/aac", "audio/flac", "audio/x-flac", "audio/ogg" } },
        { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg" } },
    });

    // The play_file action and a sound monitor's playFile: the sound plays through the platform player, with the sound monitors not listening meanwhile.
    private async Task PlayAlertSoundAsync(AlertSound sound, int seconds, CancellationToken token)
    {
        BeginSoundMute();
        try { await AudioFilePlayer.PlayAsync(sound.Path, seconds, token); }
        finally { EndSoundMute(); }
    }

    private void StartAlertSounds() => _ = Task.Run(async () =>
    {
        try { await _alertSounds.EnsureBuiltInsAsync(); }
        catch (Exception) { }
        MainThread.BeginInvokeOnMainThread(RefreshAlertSoundRows);
    });

    private View BuildAlertSoundsSection()
    {
        StartAlertSounds();
        _alertSoundRows = new VerticalStackLayout { Spacing = 4 };
        _alertSoundStatus = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false };
        var add = new Button { Text = "Add a sound…", FontSize = 13, MinimumHeightRequest = 40, Padding = new Thickness(10, 0) };
        add.Clicked += async (_, _) => await AddAlertSoundFromFileAsync();
        _alertSoundRecordButton = new Button { Text = "Record a message", FontSize = 13, MinimumHeightRequest = 40, Padding = new Thickness(10, 0) };
        _alertSoundRecordButton.Clicked += async (_, _) => await ToggleAlertRecordingAsync();
        RefreshAlertSoundRows();
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = "Alert sounds", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText },
                new Label
                {
                    Text = "Sounds an alert can play: \"play the lullaby when the baby cries\", \"play my rain sound when you see the dog\". Add your own from a file (WAV, MP3, M4A, AAC, FLAC or OGG, up to 25 MB) or record a message. They stay on this device. They play at the device's media volume.",
                    FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText
                },
                _alertSoundRows,
                new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { add, _alertSoundRecordButton } },
                _alertSoundStatus
            }
        };
    }

    private void RefreshAlertSoundRows()
    {
        if (_alertSoundRows == null) return;
        _alertSoundRows.Children.Clear();
        var sounds = _alertSounds.List();
        if (sounds.Count == 0)
            _alertSoundRows.Children.Add(new Label { Text = "Preparing the built-in sounds…", FontSize = 12, TextColor = Theme.SecondaryText });
        foreach (AlertSound sound in sounds)
        {
            AlertSound item = sound;
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 6 };
            row.Add(new Label { Text = item.Name + (item.BuiltIn ? " (built in)" : ""), FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }, 0, 0);
            var play = new Button { Text = "Play", FontSize = 12, MinimumHeightRequest = 40, Padding = new Thickness(8, 0) };
            play.Clicked += async (_, _) => await PreviewAlertSoundAsync(item, play);
            SemanticProperties.SetDescription(play, "Play " + item.Name);
            row.Add(play, 1, 0);
            if (!item.BuiltIn)
            {
                var delete = new Button { Text = "Delete", FontSize = 12, MinimumHeightRequest = 40, Padding = new Thickness(8, 0) };
                delete.Clicked += async (_, _) => await DeleteAlertSoundAsync(item);
                SemanticProperties.SetDescription(delete, "Delete " + item.Name);
                row.Add(delete, 2, 0);
            }
            _alertSoundRows.Children.Add(row);
        }
    }

    private void ShowAlertSoundStatus(string text)
    {
        if (_alertSoundStatus == null) return;
        _alertSoundStatus.Text = text;
        _alertSoundStatus.IsVisible = !string.IsNullOrEmpty(text);
    }

    // One preview at a time: Play again (or on another sound) stops the one that plays.
    private async Task PreviewAlertSoundAsync(AlertSound sound, Button button)
    {
        bool wasPlaying = _alertPreview != null;
        _alertPreview?.Cancel();
        if (wasPlaying && button.Text == "Stop") return;
        var source = _alertPreview = new CancellationTokenSource();
        button.Text = "Stop";
        try { await PlayAlertSoundAsync(sound, 0, source.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowAlertSoundStatus("It couldn't play: " + ex.Message); }
        finally
        {
            if (_alertPreview == source) _alertPreview = null;
            if (button.Text == "Stop") button.Text = "Play";
        }
    }

    private async Task AddAlertSoundFromFileAsync()
    {
        try
        {
            FileResult file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a sound", FileTypes = AlertFileTypes });
            if (file == null) return;
            using Stream stream = await file.OpenReadAsync();
            AlertSound sound = await _alertSounds.AddAsync(stream, file.FileName);
            ShowAlertSoundStatus("Added '" + sound.Name + "'. Ask for it by name: \"play " + sound.Name + " when …\".");
        }
        catch (Exception ex) { ShowAlertSoundStatus("It couldn't be added: " + ex.Message); }
        RefreshAlertSoundRows();
    }

    private async Task DeleteAlertSoundAsync(AlertSound sound)
    {
        if (!await Host.AskAsync("Delete this sound?", "'" + sound.Name + "' will be removed. Alerts that use it will say it's gone.", "Delete", "Keep")) return;
        try { _alertSounds.Delete(sound); ShowAlertSoundStatus("Deleted '" + sound.Name + "'."); }
        catch (Exception ex) { ShowAlertSoundStatus("It couldn't be deleted: " + ex.Message); }
        RefreshAlertSoundRows();
    }

    // Record a message with the microphone (up to 30 s, 16 kHz mono), then name it; the sound monitors pause while the microphone is used.
    private async Task ToggleAlertRecordingAsync()
    {
        if (_alertRecorder.IsRecording)
        {
            byte[] wav;
            TimeSpan length = DateTime.UtcNow - _alertRecordingStarted;
            try { wav = await _alertRecorder.StopAsync(); }
            catch (Exception ex) { _alertSoundRecordButton.Text = "Record a message"; ShowAlertSoundStatus("It couldn't record: " + ex.Message); return; }
            _alertSoundRecordButton.Text = "Record a message";
            if (wav == null || length < TimeSpan.FromSeconds(0.5)) { ShowAlertSoundStatus("That was too short."); return; }
            string name = await Host.PromptAsync("Name this message", "Alerts play it by this name.", "Save", "Discard", "My message", 40);
            if (name == null) { ShowAlertSoundStatus("Discarded."); return; }
            try
            {
                using var stream = new MemoryStream(wav);
                AlertSound sound = await _alertSounds.AddAsync(stream, "message.wav", name);
                ShowAlertSoundStatus("Saved '" + sound.Name + "' (" + length.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s).");
            }
            catch (Exception ex) { ShowAlertSoundStatus("It couldn't be saved: " + ex.Message); }
            RefreshAlertSoundRows();
            return;
        }
        try
        {
            if (!await _alertRecorder.StartAsync()) { ShowAlertSoundStatus("Microphone access is needed to record a message."); return; }
        }
        catch (Exception ex) { ShowAlertSoundStatus("It couldn't start recording: " + ex.Message); return; }
        _alertRecordingStarted = DateTime.UtcNow;
        _alertSoundRecordButton.Text = "Stop and save";
        ShowAlertSoundStatus("Recording… tap Stop and save when you're done (30 s at most).");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            if (_alertRecorder.IsRecording && DateTime.UtcNow - _alertRecordingStarted >= TimeSpan.FromSeconds(29.5))
                await MainThread.InvokeOnMainThreadAsync(ToggleAlertRecordingAsync);
        });
    }

    private void ReleaseAlertSounds()
    {
        _alertPreview?.Cancel();
        if (_alertRecorder.IsRecording) _ = _alertRecorder.StopAsync();
    }
}
