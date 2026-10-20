// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using System.Diagnostics;
using Emgu.LiteRT.LM.Agents.Audio;
using Microsoft.Extensions.AI;

namespace Maui.Demo.Lite;

// Sound monitors (TODO item 42, YAMNET.md): "tell me when the doorbell rings". The agent's create_sound_monitor tool (Emgu.LiteRT.LM.Agents'
// SoundMonitorTools) adds a monitor to a SoundMonitorRuntime; while any is enabled the page listens through the microphone, cuts the audio
// into YAMNet windows (SoundWindower) and feeds each window's 521 scores to the runtime, which confirms sounds and runs the alerts - the
// chime and spoken words - on a worker of its own. No audio is kept or sent anywhere, and the language model isn't involved per sound.
// The microphone is let go of while the voice recorder uses it (MicrophoneGate), and the page's own alert audio is not listened to.
public partial class LiteRtLmChatPage
{
#if ANDROID || IOS || MACCATALYST
    private const bool SoundSupported = true;
#else
    private const bool SoundSupported = false;     // no Windows microphone stream yet
#endif
    private const string SoundInstructions = SoundMonitorTools.Instructions +
        " Use the camera tools instead when the user wants to see something rather than hear it: \"when you see a person, play white noise\" is a camera monitor with the play_file action, not a sound monitor. When the user wants a video or photo taken because of a sound, " +
        "that is still a sound monitor: use create_sound_monitor with clipSeconds or takePhoto, not a camera monitor.";

    private SoundMonitorRuntime _soundRuntime;
    private readonly YamNetDetector _soundDetector = new YamNetDetector();
    private readonly SemaphoreSlim _soundGate = new SemaphoreSlim(1, 1);
    private readonly SoundWindower _soundWindower = new SoundWindower();
    private readonly Stopwatch _soundClock = Stopwatch.StartNew();
    private readonly CameraChimeAction _soundChime = new CameraChimeAction();
    private ISoundSource _soundSource;
#pragma warning disable CS0649     // assigned by the temporary self-tests that replace the microphone
    private Func<ISoundSource> _soundSourceFactory;
#pragma warning restore CS0649
    private long _soundMutedUntil;
    private int _soundMuteHolds;       // alert audio, or a clip playing in the chat: the microphone would hear it
    private bool _soundPaused, _soundDisposed;
    private string _soundStatus;
    private Border _soundPanel;
    private VerticalStackLayout _soundRows;
    private int _soundUiQueued;

    private void BuildSoundMonitoring()
    {
        _soundRows = new VerticalStackLayout { Spacing = 6 };
        _soundPanel = Theme.Card(new ScrollView { Content = _soundRows }, 8);
        _soundPanel.WidthRequest = 310;
        _soundPanel.MaximumHeightRequest = 220;
        _soundPanel.HorizontalOptions = LayoutOptions.End;
        _soundPanel.VerticalOptions = LayoutOptions.End;
        _soundPanel.Margin = new Thickness(8);
        _soundPanel.IsVisible = false;
        _soundPanel.ZIndex = 6;
        _mainColumn.Add(_soundPanel, 0, 1);
        _scroll.SizeChanged += (_, _) =>
        {
            _soundPanel.WidthRequest = Math.Min(310, Math.Max(180, _mainColumn.Width - 16));
            _soundPanel.MaximumHeightRequest = Math.Max(90, Math.Min(220, _scroll.Height - 16));
        };
        MicrophoneGate.Pausing = PauseSoundListeningAsync;
        MicrophoneGate.Released = () => { _soundPaused = false; _ = UpdateSoundListeningAsync(); };
    }

    private async Task DisposeSoundMonitoringAsync()
    {
        _soundDisposed = true;
        MicrophoneGate.Pausing = null;
        MicrophoneGate.Released = null;
        _soundRuntime?.Dispose();
        await UpdateSoundListeningAsync();
        _soundDetector.Dispose();
    }

    private void ClearSoundMonitors()
    {
        if (_soundRuntime == null) return;
        _soundRuntime.Clear();
        _ = UpdateSoundListeningAsync();
    }

    private IEnumerable<AITool> SoundTools() => !SoundSupported ? Array.Empty<AITool>()
        : SoundMonitorTools.Create(CreateSoundMonitorAsync, ListSoundMonitors, ControlSoundMonitorAsync, CameraAlertsSupported, _alertSounds.Names());

    // A sound can also set off the live camera's clip and photo, where the camera records (Android; TODO item 49).
#if ANDROID
    private const bool CameraAlertsSupported = true;
#else
    private const bool CameraAlertsSupported = false;
#endif
    private readonly Dictionary<Guid, long> _soundCameraUntil = new();   // per monitor: no new clip or photo before this timestamp
    private const int SoundCameraCooldownSeconds = 30;

    // ---------- tools ----------

    private async Task<string> CreateSoundMonitorAsync(string sound, double minimumSeconds, bool playSound, string speakText, double threshold,
        int clipSeconds, bool takePhoto, string playFile, int playSeconds, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (!SoundSupported) return "Sound monitoring isn't supported on this device yet.";
            await EnsureSoundRuntimeAsync(token);
            if (!CameraAlertsSupported && (clipSeconds != 0 || takePhoto)) return "Recording a clip or taking a photo isn't supported on this device yet.";
            if (clipSeconds < 0 || (clipSeconds > 0 && clipSeconds < SoundAlert.MinimumClipSeconds)) clipSeconds = SoundAlert.DefaultClipSeconds;
            // A recorded sound is named in the user's words: find it now, so a wrong name is refused with the names there are, and keep its real name.
            if (!string.IsNullOrWhiteSpace(playFile))
            {
                await _alertSounds.EnsureBuiltInsAsync(token);
                AlertSound found = _alertSounds.Find(playFile, out string problem);
                if (found == null) return "Could not create the sound monitor: " + problem;
                playFile = found.Name;
                if (playSeconds < 0 || playSeconds > SoundAlert.MaximumPlaySeconds) playSeconds = SoundAlert.DefaultPlaySeconds;
            }
            else playSeconds = 0;
            var monitor = _soundRuntime.Create(sound, threshold, minimumSeconds, new SoundAlert(playSound, speakText, clipSeconds, takePhoto, playFile, playSeconds));
            RememberMonitorRequest(monitor.Id);
            string status = await UpdateSoundListeningAsync();
            var current = _soundRuntime.Snapshot().First(m => m.Id == monitor.Id);
            string camera = "";
            if (clipSeconds > 0 || takePhoto)
            {
                // The camera is what records: open it now, so the user sees it works (and can allow the permission) before the sound comes.
                string opened = await OpenCameraAsync(token);
                camera = _camera?.IsRunning == true ? " The live camera is open and will be used when the sound is heard." : " " + opened;
            }
            return SoundMonitorTools.Describe(current) + " " + status + camera;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "Could not create the sound monitor: " + ex.Message; }
    }

    private string ListSoundMonitors() => _soundRuntime == null || _soundRuntime.Snapshot().Count == 0
        ? "There are no sound monitors." : string.Join("\n", _soundRuntime.Snapshot().Select(SoundMonitorTools.Describe));

    private async Task<string> ControlSoundMonitorAsync(string monitorId, string operation, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (_soundRuntime == null || !Guid.TryParse(monitorId, out var id)) return "Could not control the monitor: invalid ID. Use list_sound_monitors.";
            switch (operation?.Trim().ToLowerInvariant())
            {
                case "pause": _soundRuntime.SetEnabled(id, false); break;
                case "resume": _soundRuntime.SetEnabled(id, true); break;
                case "remove":
                    _soundRuntime.Remove(id);
                    ForgetMonitorRequest(id);
                    await UpdateSoundListeningAsync();
                    return "Sound monitor removed.";
                default: return "Could not control the monitor: operation must be pause, resume or remove.";
            }
            string status = await UpdateSoundListeningAsync();
            return SoundMonitorTools.Describe(_soundRuntime.Snapshot().First(m => m.Id == id)) + " " + status;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return "Could not control the sound monitor: " + ex.Message; }
    }

    // ---------- listening ----------

    private async Task EnsureSoundRuntimeAsync(CancellationToken token)
    {
        await _soundGate.WaitAsync(token);
        try
        {
            if (_soundRuntime != null) return;
            await _soundDetector.PrepareAsync(Math.Min(2, CpuThreadSettings.Count), SetStatus, token);
            var runtime = new SoundMonitorRuntime(_soundDetector.Labels, SoundAlertAsync);
            runtime.Changed += QueueSoundUi;
            _soundRuntime = runtime;
            SetStatus(null);
        }
        finally { _soundGate.Release(); }
    }

    private Task PauseSoundListeningAsync()
    {
        _soundPaused = true;
        return UpdateSoundListeningAsync();
    }

    // Starts or stops the microphone to match the monitors (any enabled one), the page being alive and the voice recorder being idle.
    private async Task<string> UpdateSoundListeningAsync()
    {
        await _soundGate.WaitAsync();
        try
        {
            bool want = !_soundDisposed && !_soundPaused && _soundRuntime?.WantsAudio == true;
            if (want && _soundSource == null)
            {
                ISoundSource source = null;
                try
                {
                    source = _soundSourceFactory != null ? _soundSourceFactory() :
#if ANDROID || IOS || MACCATALYST
                        new MicrophoneSoundSource();
#else
                        throw new NotSupportedException("Listening isn't supported on this platform yet.");
#endif
                    source.Samples += OnSoundSamples;
                    _soundWindower.Reset();
                    if (await source.StartAsync()) { _soundSource = source; _soundStatus = "Listening through the microphone."; }
                    else { source.Dispose(); _soundStatus = "The microphone permission was denied, so it can't listen. The user can allow it in the system settings."; }
                }
                catch (Exception ex)
                {
                    source?.Dispose();
                    _soundStatus = "It couldn't start listening: " + ex.Message;
                }
            }
            else if (!want && _soundSource != null)
            {
                ISoundSource source = _soundSource;
                _soundSource = null;
                source.Samples -= OnSoundSamples;
                try { await source.StopAsync(); } catch (Exception) { }
                source.Dispose();
                _soundStatus = _soundPaused ? "Paused while the microphone is used for a voice message." : null;
            }
            QueueSoundUi();
            return _soundSource != null ? "Listening through the microphone." : _soundStatus ?? "Not listening.";
        }
        finally { _soundGate.Release(); }
    }

    // On the audio thread: cut into windows and score them elsewhere. Not while alert audio plays and just after, or it hears itself.
    private void OnSoundSamples(float[] samples)
    {
        float[] window = _soundWindower.Add(samples);
        if (window == null) return;
        Task.Run(() =>
        {
            try
            {
                if (Volatile.Read(ref _soundMuteHolds) > 0 || Stopwatch.GetTimestamp() < Interlocked.Read(ref _soundMutedUntil)) return;
                _soundRuntime?.Advance(_soundClock.Elapsed, _soundDetector.Scores(window));
            }
            catch (Exception) { }
            finally { _soundWindower.Done(); }
        });
    }

    private async Task SoundAlertAsync(SoundEvent occurrence, SoundMonitorInfo monitor, CancellationToken token)
    {
        BeginSoundMute();
        try
        {
            var actions = new List<Task>();
            if (monitor.ClipSeconds > 0 || monitor.TakePhoto) actions.Add(SoundCameraAlertAsync(occurrence, monitor, token));
            if (monitor.PlayFile != null)
            {
                AlertSound file = _alertSounds.Find(monitor.PlayFile, out string problem) ?? throw new InvalidOperationException(problem);
                actions.Add(PlayAlertSoundAsync(file, monitor.PlayFileSeconds, token));
            }
            if (monitor.Chime) actions.Add(_soundChime.ExecuteAsync(null, token));
            if (monitor.SpeakText != null) actions.Add(MainThread.InvokeOnMainThreadAsync(() => ReplySpeech.SpeakAsync(monitor.SpeakText, token)));
            await Task.WhenAll(actions);
        }
        finally { EndSoundMute(); }
    }

    // The photo first, then the clip; a continuing sound makes one capture each SoundCameraCooldownSeconds, not one per episode of it.
    // The clip's audio is the room's, so listening is off for the clip (the page's alert audio mute): it must not start itself again.
    private async Task SoundCameraAlertAsync(SoundEvent occurrence, SoundMonitorInfo monitor, CancellationToken token)
    {
        lock (_soundCameraUntil)
        {
            long now = Stopwatch.GetTimestamp();
            if (_soundCameraUntil.TryGetValue(monitor.Id, out long until) && now < until) return;
            _soundCameraUntil[monitor.Id] = now + (long)(SoundCameraCooldownSeconds + monitor.ClipSeconds) * Stopwatch.Frequency;
        }
#if ANDROID
        if (_camera?.IsRunning != true) await OpenCameraCoreAsync(token, false);
        string cause() => "Heard " + (occurrence.Label ?? monitor.Sound).ToLowerInvariant() + " at " + occurrence.At.ToString("HH:mm:ss") +
            " (" + Math.Round(occurrence.Score * 100) + "%) · " + monitor.Description;
        // A failure reaches the monitor's card through the runtime ("The alert failed: ...").
        if (monitor.TakePhoto) await TakePhotoAsync(cause, token);
        if (monitor.ClipSeconds > 0) await RecordClipAsync(cause, monitor.ClipSeconds, token);
#else
        await Task.CompletedTask;
#endif
    }

    // Listening is off while something plays, and for 2 s after. Holds can overlap (an alert while a clip plays).
    private void BeginSoundMute() => Interlocked.Increment(ref _soundMuteHolds);

    private void EndSoundMute()
    {
        Interlocked.Exchange(ref _soundMutedUntil, Stopwatch.GetTimestamp() + 2 * Stopwatch.Frequency);
        Interlocked.Decrement(ref _soundMuteHolds);
    }

    // ---------- the panel ----------

    private void QueueSoundUi()
    {
        if (Interlocked.Exchange(ref _soundUiQueued, 1) != 0) return;
        MainThread.BeginInvokeOnMainThread(() => { Interlocked.Exchange(ref _soundUiQueued, 0); UpdateSoundUi(); });
    }

    private void UpdateSoundUi()
    {
        var monitors = _soundRuntime?.Snapshot() ?? (IReadOnlyList<SoundMonitorInfo>)Array.Empty<SoundMonitorInfo>();
        _soundPanel.IsVisible = monitors.Count > 0;
        _soundRows.Children.Clear();
        if (monitors.Count == 0) return;
        _soundRows.Children.Add(new Label { Text = "Sound monitors · session only", FontSize = 13, FontFamily = Theme.TitleFont, TextColor = Theme.PrimaryText });
        foreach (var monitor in monitors)
        {
            _soundRows.Children.Add(new Label { Text = monitor.Description, FontSize = 12, TextColor = Theme.PrimaryText });
            if (MonitorRequestLabel(monitor.Id) is View request) _soundRows.Children.Add(request);
            string state = !monitor.Enabled ? "Paused" : _soundSource != null ? "Listening" : _soundStatus ?? "Not listening";
            _soundRows.Children.Add(new Label { Text = state + (monitor.Heard > 0 ? " · heard " + monitor.Heard + (monitor.Heard == 1 ? " time" : " times") : ""),
                FontSize = 11, TextColor = Theme.SecondaryText });
            if (monitor.LastEvent != null)
                _soundRows.Children.Add(new Label { Text = monitor.LastEvent, FontSize = 11, TextColor = Theme.SecondaryText });
            var buttons = new HorizontalStackLayout { Spacing = 6 };
            var toggle = MonitorButton(monitor.Enabled ? "Pause" : "Resume");
            var remove = MonitorButton("Remove");
            toggle.Clicked += async (_, _) => await ControlSoundMonitorAsync(monitor.Id.ToString(), monitor.Enabled ? "pause" : "resume", CancellationToken.None);
            remove.Clicked += async (_, _) => await ControlSoundMonitorAsync(monitor.Id.ToString(), "remove", CancellationToken.None);
            SemanticProperties.SetDescription(toggle, (monitor.Enabled ? "Pause " : "Resume ") + monitor.Description);
            SemanticProperties.SetDescription(remove, "Remove monitor: " + monitor.Description);
            buttons.Children.Add(toggle); buttons.Children.Add(remove);
            _soundRows.Children.Add(buttons);
        }
    }
}
#endif
