// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Diagnostics;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Maui.CV;

namespace Maui.Demo.Lite;

// Hands-free voice conversation (the "Voice chat" chip, Gemma 4): a full-screen layer that listens, sends what was said
// to the model as audio once the speaker pauses (VoiceActivityDetector), has the reply read aloud while it is written
// (the page's AlertAudioReplySpeaker), and listens again. A tap on the circle sends at once while listening, or stops the
// reply and listens while the model answers. With headphones, speaking interrupts the reply too; through a loudspeaker
// the microphone would hear the reply itself, so it is off until the reply has been read. While it runs the page is
// hands-free (IChatPageHost.SetHandsFree): no thinking first, no tools that need the screen.
internal sealed class VoiceModeFeature : DemoFeature
{
    private enum VoiceState { Listening, Thinking, Speaking }

    // Material Symbols "mic" (Apache 2.0); the bundled icon font is a subset without it.
    private const string MicPathData = "M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z";

    private static readonly TimeSpan VoiceFrame = TimeSpan.FromMilliseconds(50);
    // Voice messages stop (and are sent) after this long: audio models take a limited length of audio.
    private static readonly TimeSpan MaxVoiceLength = TimeSpan.FromSeconds(30);
    // Without speech for this long the recording is started again: a message doesn't begin with minutes of silence
    // (while listening), or with the whole reply's worth (while waiting for an interruption).
    private static readonly TimeSpan ListenRestart = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan BargeInRestart = TimeSpan.FromSeconds(8);

    private readonly VoiceRecorder _recorder = new VoiceRecorder();
    private Grid _voiceView;
    private Border _voiceCircle;
    private Label _voiceStatus, _voiceCaption, _voiceHint;
    private VoiceState _voiceState;
    private bool _voiceMode, _voiceTapped, _voiceHeadphones;
    private CancellationTokenSource _voiceLoop, _voiceInterrupt;
    private IVoiceInput _voiceInput;

    public override string Name => "voice_chat";

    public override string Title => "Voice chat";

    public override bool PersistEnabled => false;

    // Whether a voice chat is on.
    public bool IsOn => _voiceMode;

    // Gemma 4 takes speech; measuring the microphone's level needs a recorder that reports it (not Windows').
    // With a model that doesn't take audio, what was said is sent as text (SpeechRecognizer).
    private bool VoiceModeSupported => VoiceRecorder.SupportsLevel && Host?.Model != null && (Host.Model.AcceptsAudio || SpeechRecognizer.IsSupported);

    private AlertAudioReplySpeaker Speaker => Host.Services.Get<AlertAudioReplySpeaker>();

    public override void RegisterServices(ChatFeatureServices services) => services.Add(this);

    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        BuildVoiceView();
        if (Speaker != null)
            Speaker.Speaking += OnSpeaking;
    }

    public override void OnDetached()
    {
        EndVoiceMode();
        if (Speaker != null)
            Speaker.Speaking -= OnSpeaking;
        base.OnDetached();
    }

    public override ChatChip BuildComposerChip() =>
        new ChatChip("Voice chat", () => _voiceMode, on => { if (on) _ = StartVoiceModeAsync(); else EndVoiceMode(); })
        {
            IsVisible = () => VoiceModeSupported
        };

    public override void OnDisappearing()
    {
        base.OnDisappearing();
        EndVoiceMode();
    }

    private void BuildVoiceView()
    {
        _voiceCircle = new Border
        {
            WidthRequest = 170,
            HeightRequest = 170,
            StrokeShape = new Ellipse(),
            Stroke = Colors.Transparent,
            BackgroundColor = Theme.Accent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Microsoft.Maui.Controls.Shapes.Path
            {
                Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(MicPathData),
                Fill = Colors.White,
                Aspect = Stretch.Uniform,
                WidthRequest = 56,
                HeightRequest = 56,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        _voiceCircle.OnTap(OnVoiceCircleTapped);
        _voiceStatus = new Label
        {
            FontFamily = Theme.TitleFont,
            FontSize = 22,
            TextColor = Theme.PrimaryText,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _voiceCaption = new Label
        {
            FontFamily = Theme.BodyFont,
            FontSize = 16,
            TextColor = Theme.PrimaryText,
            HorizontalTextAlignment = TextAlignment.Center,
            MaxLines = 5,
            LineBreakMode = LineBreakMode.TailTruncation,
            MinimumHeightRequest = 22
        };
        _voiceHint = new Label
        {
            FontFamily = Theme.BodyFont,
            FontSize = 13,
            TextColor = Theme.SecondaryText,
            HorizontalTextAlignment = TextAlignment.Center
        };
        Button end = Theme.SecondaryButton("End voice chat", null);
        end.ImageSource = null;
        end.HorizontalOptions = LayoutOptions.Center;
        end.Clicked += (_, _) => EndVoiceMode();

        _voiceView = new Grid
        {
            BackgroundColor = Theme.PageBackground,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 18,
                    Padding = new Thickness(24),
                    MaximumWidthRequest = 520,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill,
                    Children =
                    {
                        // Room for the circle to grow without moving the text.
                        new Grid { HeightRequest = 240, Children = { _voiceCircle } },
                        _voiceStatus,
                        _voiceCaption,
                        _voiceHint,
                        end
                    }
                }
            }
        };
    }

    /// <summary>
    /// Start a voice conversation with the microphone, or (self-tests) with another input.
    /// </summary>
    public async Task StartVoiceModeAsync(IVoiceInput input = null, bool? headphones = null)
    {
        if (_voiceMode)
            return;
        if (Host.IsBusy || Host.IsReloadingModel || Demo.IsRecording || !VoiceModeSupported)
        {
            Host.SetHandsFree(false);
            return;
        }
        UiStallWatchdog.Mark("Voice chat on");
        _voiceMode = true;
        Host.SetHandsFree(true);
        _voiceInput = input ?? _recorder;
        _voiceHeadphones = headphones ?? VoiceRecorder.HeadphonesConnected();
        Speaker?.Stop();
        _voiceCaption.Text = string.Empty;
        Host.Overlays.ShowLayer(_voiceView);
        try { DeviceDisplay.Current.KeepScreenOn = true; } catch (Exception) { }
        CancellationTokenSource loop = new CancellationTokenSource();
        _voiceLoop = loop;
        try
        {
            await RunVoiceLoopAsync(_voiceInput, loop.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Host.SetStatus("The voice chat stopped: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_voiceLoop, loop))
                EndVoiceMode();
            loop.Dispose();
        }
    }

    public void EndVoiceMode()
    {
        if (!_voiceMode)
            return;
        UiStallWatchdog.Mark("Voice chat off");
        _voiceMode = false;
        _voiceLoop?.Cancel();
        _voiceLoop = null;
        _voiceInterrupt?.Cancel();
        // The reply being written (if any) is finished in the chat, just not read aloud.
        Speaker?.Stop();
        _voiceView.AbortAnimation("voicePulse");
        Host.Overlays.HideLayer(_voiceView);
        Host.SetHandsFree(false);
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch (Exception) { }
    }

    private async Task RunVoiceLoopAsync(IVoiceInput input, CancellationToken token)
    {
        VoiceActivityDetector detector = new VoiceActivityDetector { MaxUtterance = MaxVoiceLength };
        try
        {
            while (true)
            {
                // Listen until the speaker pauses. After an interruption by voice the recording (and the speech) is
                // already under way.
                if (!input.IsRecording)
                {
                    if (!await input.StartAsync())
                    {
                        Host.SetStatus("Microphone access is needed for a voice chat.");
                        return;
                    }
                    detector.Reset();
                }
                SetVoiceState(VoiceState.Listening);
                await ListenAsync(input, detector, VoiceActivity.SpeechEnded, ListenRestart, token);
                byte[] wav = await input.StopAsync();
                token.ThrowIfCancellationRequested();
                if (wav == null || wav.Length <= 44)
                    continue;
                TimeSpan length = TimeSpan.FromSeconds((wav.Length - 44) / (2.0 * VoiceRecorder.SampleRate));

                // Send it, and have the reply read aloud while it is written. Meanwhile a tap - or, with headphones,
                // speech - interrupts.
                SetVoiceState(VoiceState.Thinking);
                _voiceCaption.Text = string.Empty;
                CancellationTokenSource interrupt = CancellationTokenSource.CreateLinkedTokenSource(token);
                _voiceInterrupt = interrupt;
                Task send = Demo.SendVoiceAsync(wav, length);
                Task replied = WaitForReplyAsync(send);
                Task tapped = Task.Delay(Timeout.Infinite, interrupt.Token);
                Task spoke = Task.Delay(Timeout.Infinite, interrupt.Token);
                if (_voiceHeadphones && await input.StartAsync())
                {
                    detector.Reset();
                    spoke = ListenForInterruptionAsync(input, detector, interrupt.Token);
                }
                Task first = await Task.WhenAny(replied, tapped, spoke);
                bool bySpeech = first == spoke && first.Status == TaskStatus.RanToCompletion;
                if (first != replied)
                {
                    UiStallWatchdog.Mark(bySpeech ? "Voice chat: interrupted by speech" : "Voice chat: interrupted");
                    Demo.StopReply();
                    Speaker?.Stop();
                    await send;
                }
                interrupt.Cancel();
                try { await spoke; } catch (Exception) { }
                _voiceInterrupt = null;
                interrupt.Dispose();
                token.ThrowIfCancellationRequested();
                // Interrupted by speech: keep the recording, the speech goes on. Otherwise listen afresh.
                if (!bySpeech && input.IsRecording)
                    await input.StopAsync();
                if (!Host.ModelReady)
                {
                    // The model needs downloading, or failed to load: the chat says why.
                    return;
                }
                if (!bySpeech)
                    await Task.Delay(250, token); // the end of the reply's sound
            }
        }
        finally
        {
            if (input.IsRecording)
            {
                try { await input.StopAsync(); } catch (Exception) { }
            }
        }
    }

    // Completes when speech starts. If the microphone fails, it waits for the reply instead (never completes).
    private async Task ListenForInterruptionAsync(IVoiceInput input, VoiceActivityDetector detector, CancellationToken token)
    {
        try
        {
            await ListenAsync(input, detector, VoiceActivity.SpeechStarted, BargeInRestart, token);
            return;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            UiStallWatchdog.Mark("Voice chat: listening for an interruption failed: " + ex.Message);
        }
        await Task.Delay(Timeout.Infinite, token);
    }

    // The message sent and its reply read to the end.
    private async Task WaitForReplyAsync(Task send)
    {
        await send;
        AlertAudioReplySpeaker speaker = Speaker;
        if (speaker != null)
            await speaker.Completion;
    }

    // Measure the input every VoiceFrame until the detector reports `until`; while listening for a message, a tap
    // during speech ends it at once. The recording starts again after `restart` with nothing heard.
    private async Task ListenAsync(IVoiceInput input, VoiceActivityDetector detector, VoiceActivity until, TimeSpan restart,
        CancellationToken token)
    {
        Stopwatch clock = Stopwatch.StartNew();
        TimeSpan last = TimeSpan.Zero, quietSince = TimeSpan.Zero;
        double scale = _voiceCircle.Scale;
        _voiceTapped = false;
        while (true)
        {
            await Task.Delay(VoiceFrame, token);
            TimeSpan now = clock.Elapsed;
            double level = input.ReadLevelDb();
            VoiceActivity activity = detector.Process(level, now - last);
            last = now;
            if (until == VoiceActivity.SpeechEnded)
            {
                // The circle grows with the voice - up to a quarter at a normal speaking level, 25-35 dB above the
                // background on a Pixel Fold - eased so it follows the voice's loudness rather than every syllable: up
                // over about 0.15 s, down over about 0.3 s. Background noise doesn't move it.
                double above = double.IsNaN(detector.NoiseFloorDb) ? 0 : level - detector.NoiseFloorDb;
                double target = 1 + Math.Clamp((above - 6) / 100, 0, 0.25);
                scale += (target - scale) * (target > scale ? 0.35 : 0.15);
                _voiceCircle.Scale = scale;
                if (activity == VoiceActivity.SpeechStarted)
                    _voiceHint.Text = "Pause when you're done, or tap the circle to send now";
                if (_voiceTapped)
                {
                    _voiceTapped = false;
                    if (detector.InSpeech)
                    {
                        detector.Reset();
                        return;
                    }
                }
            }
            if (activity == until)
                return;
            if (detector.Hearing)
                quietSince = now;
            else if (now - quietSince > restart)
            {
                await input.StopAsync();
                token.ThrowIfCancellationRequested();
                if (!await input.StartAsync())
                    throw new InvalidOperationException("The microphone could not be opened again.");
                detector.Reset();
                quietSince = now;
            }
        }
    }

    private void OnVoiceCircleTapped()
    {
        if (_voiceState == VoiceState.Listening)
            _voiceTapped = true;
        else
            _voiceInterrupt?.Cancel();
    }

    // The reply is being read: the circle says so, with the words.
    private void OnSpeaking(string text)
    {
        if (!_voiceMode)
            return;
        if (_voiceState != VoiceState.Speaking)
            SetVoiceState(VoiceState.Speaking);
        _voiceCaption.Text = ReplySpeechText.CleanForSpeech(text);
    }

    private void SetVoiceState(VoiceState state)
    {
        _voiceState = state;
        _voiceView.AbortAnimation("voicePulse");
        _voiceCircle.Scale = 1;
        switch (state)
        {
            case VoiceState.Listening:
                _voiceCircle.BackgroundColor = Theme.Accent;
                _voiceStatus.Text = "Listening...";
                _voiceHint.Text = "Speak, then pause - the model answers.";
                break;
            case VoiceState.Thinking:
                _voiceCircle.BackgroundColor = Theme.SecondaryText;
                _voiceStatus.Text = "Thinking...";
                _voiceHint.Text = "Tap the circle to stop and speak again.";
                Pulse(0.03, 2000);
                break;
            case VoiceState.Speaking:
                _voiceCircle.BackgroundColor = Theme.Success;
                _voiceStatus.Text = "Speaking...";
                _voiceHint.Text = _voiceHeadphones ? "Speak or tap the circle to interrupt." : "Tap the circle to interrupt.";
                Pulse(0.05, 1600);
                break;
        }
    }

    private void Pulse(double amount, uint milliseconds)
    {
        new Animation(v => _voiceCircle.Scale = 1 + amount * Math.Sin(Math.PI * v))
            .Commit(_voiceView, "voicePulse", 16, milliseconds, Easing.Linear, null, () => _voiceMode);
    }
}
