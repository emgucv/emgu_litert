//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System.Diagnostics;
using Emgu.LiteRT.LM.Models;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Agents.Audio;

namespace Maui.Demo.Lite;

// Hands-free voice conversation (the "Voice chat" chip, Gemma 4): a full-screen view that listens, sends what was said
// to the model as audio once the speaker pauses (VoiceActivityDetector), reads the reply aloud while it is written
// (ReplyReader), and listens again. A tap on the circle sends at once while listening, or stops the reply and listens
// while the model answers. With headphones, speaking interrupts the reply too; through a loudspeaker the microphone
// would hear the reply itself, so it is off until the reply has been read.
public partial class LiteRtLmChatPage
{
    private enum VoiceState { Listening, Thinking, Speaking }

    private static readonly TimeSpan VoiceFrame = TimeSpan.FromMilliseconds(50);
    // Without speech for this long the recording is started again: a message doesn't begin with minutes of silence
    // (while listening), or with the whole reply's worth (while waiting for an interruption).
    private static readonly TimeSpan ListenRestart = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan BargeInRestart = TimeSpan.FromSeconds(8);

    private ToggleChip _voiceChip;
    private Grid _voiceView;
    private Border _voiceCircle;
    private Label _voiceStatus, _voiceCaption, _voiceHint;
    private VoiceState _voiceState;
    private bool _voiceMode, _voiceTapped, _voiceHeadphones;
    private CancellationTokenSource _voiceLoop, _voiceInterrupt;
    private IVoiceInput _voiceInput;

    // The reply being generated (an interruption cancels it), the reader reading it aloud and its latest text.
    private CancellationTokenSource _replyCancel;
    private ReplyReader _replyReader;

    // Gemma 4 takes speech; measuring the microphone's level needs a recorder that reports it (not Windows').
    // With a model that doesn't take audio, what was said is sent as text (SpeechToText).
    private bool VoiceModeSupported => VoiceRecorder.SupportsLevel && TakesVoice(SelectedOption);

    private ToggleChip CreateVoiceChip()
    {
        _voiceChip = new ToggleChip("Voice chat");
        SemanticProperties.SetDescription(_voiceChip, "Voice chat: talk with the model hands-free");
        _voiceChip.Toggled += async (_, e) =>
        {
            if (e.Value && !_voiceMode)
                await StartVoiceModeAsync();
            else if (!e.Value)
                EndVoiceMode();
        };
        return _voiceChip;
    }

    private View BuildVoiceView()
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
            IsVisible = false,
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
        return _voiceView;
    }

    /// <summary>
    /// Start a voice conversation with the microphone, or (self-tests) with another input.
    /// </summary>
    private async Task StartVoiceModeAsync(IVoiceInput input = null, bool? headphones = null)
    {
        if (_voiceMode)
            return;
        if (_busy || _reloadingCpuThreads || _recorder.IsRecording || !VoiceModeSupported)
        {
            _voiceChip.IsToggled = false;
            return;
        }
        UiStallWatchdog.Mark("Voice chat on");
        _voiceMode = true;
        _voiceChip.IsToggled = true;
        _voiceInput = input ?? _recorder;
        _voiceHeadphones = headphones ?? VoiceRecorder.HeadphonesConnected();
        StopSpeaking();
        _voiceCaption.Text = string.Empty;
        _voiceView.IsVisible = true;
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
            SetStatus("The voice chat stopped: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_voiceLoop, loop))
                EndVoiceMode();
            loop.Dispose();
        }
    }

    private void EndVoiceMode()
    {
        if (!_voiceMode)
            return;
        UiStallWatchdog.Mark("Voice chat off");
        _voiceMode = false;
        _voiceLoop?.Cancel();
        _voiceLoop = null;
        _voiceInterrupt?.Cancel();
        // The reply being written (if any) is finished in the chat, just not read aloud.
        StopSpeaking();
        this.AbortAnimation("voicePulse");
        _voiceView.IsVisible = false;
        _voiceChip.IsToggled = false;
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
                        SetStatus("Microphone access is needed for a voice chat.");
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

                // Send it, and read the reply aloud while it is written. Meanwhile a tap - or, with headphones, speech -
                // interrupts.
                SetVoiceState(VoiceState.Thinking);
                _voiceCaption.Text = string.Empty;
                CancellationTokenSource interrupt = CancellationTokenSource.CreateLinkedTokenSource(token);
                _voiceInterrupt = interrupt;
                List<PendingAttachment> attachments = new List<PendingAttachment>
                {
                    new PendingAttachment
                    {
                        Attachment = ChatAttachment.Audio(wav),
                        Data = wav,
                        MediaType = "audio/wav",
                        Name = string.Format("Voice message ({0:m\\:ss})", length)
                    }
                };
                Task send = SendAsync(string.Empty, attachments);
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
                    _replyCancel?.Cancel();
                    StopSpeaking();
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
                if (!ModelReady)
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

    private async Task WaitForReplyAsync(Task send)
    {
        await send;
        ReplyReader reader = _replyReader;
        if (reader != null)
            await reader.Completion;
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

    private void SetVoiceState(VoiceState state)
    {
        _voiceState = state;
        this.AbortAnimation("voicePulse");
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
            .Commit(this, "voicePulse", 16, milliseconds, Easing.Linear, null, () => _voiceMode);
    }

    // A reader for the reply about to be written, with read-aloud on or in a voice chat; null otherwise.
    private ReplyReader StartReplyReader()
    {
        StopSpeaking();
        if (!_voiceMode && !_readAloudSwitch.IsToggled)
            return null;
        ReplyReader reader = new ReplyReader((text, token) => _alertAudio.SpeakReplyAsync(text, token));
        reader.Speaking += text =>
        {
            if (!_voiceMode || !ReferenceEquals(_replyReader, reader))
                return;
            if (_voiceState != VoiceState.Speaking)
                SetVoiceState(VoiceState.Speaking);
            _voiceCaption.Text = ReplySpeech.CleanForSpeech(text);
        };
        _replyReader = reader;
        return reader;
    }

    private void StopSpeaking()
    {
        _replyReader?.Stop();
        _replyReader = null;
    }


    /// <summary>
    /// Reads a reply aloud while it is being written: <see cref="SpeechSegmenter"/> cuts it into sentences and lines
    /// (no thinking - only the answer's text is given to it - code or tables), which are spoken in order, everything
    /// that arrived during one piece together as the next. Used on the UI thread only.
    /// </summary>
    private sealed class ReplyReader
    {
        private readonly SpeechSegmenter _segmenter = new SpeechSegmenter();
        private readonly List<string> _queue = new List<string>();
        private readonly Func<string, CancellationToken, Task> _speak;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _speaking, _finished;

        public ReplyReader(Func<string, CancellationToken, Task> speak)
        {
            _speak = speak;
        }

        /// <summary>The text about to be read.</summary>
        public event Action<string> Speaking;

        /// <summary>Completes when the whole reply has been read, or reading was stopped.</summary>
        public Task Completion => _done.Task;

        /// <summary>The reply's text so far.</summary>
        public void Update(string text)
        {
            if (_stop.IsCancellationRequested)
                return;
            _queue.AddRange(_segmenter.Add(text));
            Pump();
        }

        /// <summary>The whole reply.</summary>
        public void Finish(string text)
        {
            if (!_stop.IsCancellationRequested)
                _queue.AddRange(_segmenter.Finish(text));
            _finished = true;
            Pump();
        }

        public void Stop()
        {
            _stop.Cancel();
            _queue.Clear();
            _finished = true;
            if (!_speaking)
                _done.TrySetResult(true);
        }

        private async void Pump()
        {
            if (_speaking)
                return;
            _speaking = true;
            try
            {
                while (_queue.Count > 0 && !_stop.IsCancellationRequested)
                {
                    string text = string.Join(" ", _queue);
                    _queue.Clear();
                    Speaking?.Invoke(text);
                    try
                    {
                        await _speak(text, _stop.Token);
                    }
                    catch (Exception)
                    {
                        // Stopped, or no speech engine.
                    }
                }
            }
            finally
            {
                _speaking = false;
            }
            if (_finished)
                _done.TrySetResult(true);
        }
    }
}

#endif
