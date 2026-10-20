//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite
{
    // What the microphone hears, named by YAMNet (521 sound classes: speech, laughter, a dog barking, a doorbell, a siren, music...), about
    // twice a second. The first step towards a sound-event monitor for the visual agents (YAMNET.md). Sample clips play at their own
    // speed through the same path, so the page can be tried without a microphone.
    public class SoundEventsPage : ContentPage
    {
        private const int Rows = 5;
        private readonly YamNetDetector _detector = new YamNetDetector();
        private readonly Button _listen, _speech, _tone;
        private readonly Label _status, _timing;
        private readonly Label[] _names = new Label[Rows], _values = new Label[Rows];
        private readonly ProgressBar[] _bars = new ProgressBar[Rows];
        private readonly VerticalStackLayout _log;
        private readonly object _lock = new object();
        private readonly List<float> _buffer = new List<float>();
        private ISoundSource _source;
        private int _sinceWindow;
        private volatile bool _busy;
        private Stopwatch _clock;
        private string _lastLogged;
        private bool _running;

        public SoundEventsPage()
        {
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            _listen = Theme.PrimaryButton("Listen", Theme.GlyphPlay);
            _listen.Clicked += async (_, _) => await ToggleAsync(MicrophoneSource);
            _speech = Theme.SecondaryButton("Speech sample", Theme.GlyphText);
            _speech.Clicked += async (_, _) => await ToggleAsync(async () => new ClipSoundSource(await SpeechClipAsync()));
            _tone = Theme.SecondaryButton("Tone and noise sample", Theme.GlyphSparkle);
            _tone.Clicked += async (_, _) => await ToggleAsync(() => Task.FromResult<ISoundSource>(new ClipSoundSource(ToneClip())));

            _status = new Label { Text = "Ready. YAMNet (4 MB) is downloaded on first use.", FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText };
            _timing = new Label { FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText };

            var rows = new VerticalStackLayout { Spacing = 10 };
            for (int i = 0; i < Rows; i++)
            {
                _names[i] = new Label { FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText, LineBreakMode = LineBreakMode.TailTruncation, HorizontalOptions = LayoutOptions.Fill };
                _values[i] = new Label { FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText, HorizontalOptions = LayoutOptions.End };
                _bars[i] = new ProgressBar { ProgressColor = Theme.Accent, HeightRequest = 6 };
                var top = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                top.Add(_names[i]); top.Add(_values[i], 1);
                rows.Add(new VerticalStackLayout { Spacing = 4, Children = { top, _bars[i] } });
            }
            _log = new VerticalStackLayout { Spacing = 6 };
            _log.Add(new Label { Text = "Nothing heard yet.", FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText });

            var content = new VerticalStackLayout
            {
                Spacing = 16,
                Padding = new Thickness(16, 12, 16, 24),
                Children =
                {
                    Theme.PageHeader(this, Theme.GlyphSparkle, "Sound events", "Name the sounds around you."),
                    _listen, _speech, _tone,
                    Theme.Card(new VerticalStackLayout { Spacing = 12, Children = { Theme.SectionTitle("Now"), rows, _timing, _status } }),
                    Theme.Card(new VerticalStackLayout { Spacing = 10, Children = { Theme.SectionTitle("Heard"), _log } }),
                    Theme.AboutCard("Listens with YAMNet, Google's audio event classifier, through LiteRT: 521 kinds of sound (speech, laughter, a dog, a doorbell, a siren, music...). It scores the last second of audio about twice a second. Sounds overlap, so related ones (Dog and Bark) score together. The model (4 MB, Apache-2.0 according to its metadata) is downloaded from Google on first use; the audio never leaves the device.")
                }
            };
            Content = Theme.CenteredScroll(content);
        }

        private static Task<ISoundSource> MicrophoneSource()
        {
#if ANDROID || IOS || MACCATALYST
            return Task.FromResult<ISoundSource>(new MicrophoneSoundSource());
#else
            throw new NotSupportedException("Listening isn't supported on this platform yet; try a sample.");
#endif
        }

        private static async Task<float[]> SpeechClipAsync()
        {
            using Stream stream = await FileSystem.OpenAppPackageFileAsync("audio_sample.wav");
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            float[] samples = YamNet.ReadWave(memory.ToArray(), out int rate);
            return YamNet.Resample(samples, rate);
        }

        // Two seconds each of silence, a 440 Hz tone, white noise and silence.
        private static float[] ToneClip()
        {
            const int second = YamNet.SampleRate;
            var clip = new float[8 * second];
            var random = new Random(1);
            for (int i = 2 * second; i < 4 * second; i++) clip[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / second));
            for (int i = 4 * second; i < 6 * second; i++) clip[i] = (float)(random.NextDouble() * 0.6 - 0.3);
            return clip;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _ = StopAsync();
        }

        private async Task ToggleAsync(Func<Task<ISoundSource>> create)
        {
            if (_running) { await StopAsync(); return; }
            _running = true;
            SetButtons(true);
            try
            {
                await _detector.PrepareAsync(CpuThreadSettings.Count, text => MainThread.BeginInvokeOnMainThread(() => _status.Text = text), CancellationToken.None);
                if (!_running) return;
                lock (_lock) { _buffer.Clear(); _sinceWindow = 0; }
                _lastLogged = null;
                _clock = Stopwatch.StartNew();
                _log.Clear();
                _source = await create();
                _source.Samples += OnSamples;
                if (_source is ClipSoundSource clip) clip.Finished += () => MainThread.BeginInvokeOnMainThread(async () => await StopAsync());
                if (!await _source.StartAsync())
                {
                    _status.Text = "The microphone permission was denied.";
                    await StopAsync();
                    return;
                }
                _status.Text = "Listening...";
            }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                await StopAsync();
            }
        }

        private async Task StopAsync()
        {
            if (!_running) return;
            _running = false;
            ISoundSource source = _source;
            _source = null;
            if (source != null)
            {
                source.Samples -= OnSamples;
                try { await source.StopAsync(); } catch { }
                source.Dispose();
            }
            SetButtons(false);
            if (_status.Text == "Listening...") _status.Text = "Stopped.";
        }

        private void SetButtons(bool running)
        {
            _listen.Text = running ? "Stop" : "Listen";
            _speech.IsEnabled = _tone.IsEnabled = !running;
            _listen.IsEnabled = true;
        }

        // Every 7,800 samples (half a window), once there is a whole window: the last 15,600 samples. A window that arrives while the
        // last one is still being scored is skipped.
        private void OnSamples(float[] samples)
        {
            float[] window = null;
            lock (_lock)
            {
                _buffer.AddRange(samples);
                if (_buffer.Count > 2 * YamNet.WindowSamples) _buffer.RemoveRange(0, _buffer.Count - 2 * YamNet.WindowSamples);
                _sinceWindow += samples.Length;
                if (_sinceWindow >= YamNet.HopSamples && _buffer.Count >= YamNet.WindowSamples && !_busy)
                {
                    _sinceWindow = 0;
                    _busy = true;
                    window = _buffer.GetRange(_buffer.Count - YamNet.WindowSamples, YamNet.WindowSamples).ToArray();
                }
            }
            if (window == null) return;
            Task.Run(() =>
            {
                try
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    var top = _detector.Classify(window, 0, Rows);
                    watch.Stop();
                    double level = Math.Sqrt(window.Average(v => (double)v * v));
                    TimeSpan at = _clock.Elapsed;
                    MainThread.BeginInvokeOnMainThread(() => Show(top, watch.Elapsed.TotalMilliseconds, level, at));
                }
                catch { }
                finally { _busy = false; }
            });
        }

        private void Show(IReadOnlyList<SoundScore> top, double milliseconds, double level, TimeSpan at)
        {
            if (!_running) return;
            for (int i = 0; i < Rows; i++)
            {
                bool has = i < top.Count;
                _names[i].Text = has ? top[i].Label : "";
                _values[i].Text = has ? $"{top[i].Score * 100:0}%" : "";
                _bars[i].Progress = has ? top[i].Score : 0;
            }
            _timing.Text = $"LiteRT CPU · {milliseconds:0.0} ms per window · level {20 * Math.Log10(Math.Max(level, 1e-6)):0} dB";
            // A line in the log when the best sound changes and is fairly sure.
            if (top.Count > 0 && top[0].Score >= 0.4 && top[0].Label != _lastLogged)
            {
                _lastLogged = top[0].Label;
                _log.Insert(0, new Label
                {
                    Text = $"{(int)at.TotalMinutes}:{at.Seconds:00}  {top[0].Label}  {top[0].Score * 100:0}%",
                    FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText
                });
                while (_log.Count > 12) _log.RemoveAt(_log.Count - 1);
            }
        }
    }
}
