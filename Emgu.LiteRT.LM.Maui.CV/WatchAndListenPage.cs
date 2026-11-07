//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Text;
using Emgu.CV;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.Models.CV.Maui;
using Microsoft.Maui.Controls.Shapes;
using Gemma4 = Emgu.LiteRT.LM.Models.Gemma4;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>
/// "Tell me what you see and hear": the live camera, and below it what a model that takes images and audio (Gemma 4) sees and
/// hears, a new description every few seconds until Stop (<see cref="SceneNarrator"/>). The model is loaded when Start is first
/// tapped (downloaded after asking), on the chat's backend with Gemma 4's vision encoder on the GPU too (a round took 4.5 s on an
/// M1's GPU, 10 s with the vision encoder on the CPU), and freed when the page closes. The camera is <see cref="LiveCamera"/> and
/// the microphone <see cref="MicrophoneSoundSource"/>, taken through <see cref="MicrophoneGate"/>. Nothing is saved: the
/// descriptions stay on the page, with Copy. It stops by itself after <see cref="SceneNarrationOptions.TimeLimit"/>, and when the
/// device is hot or the battery low. Android, iOS and Mac Catalyst (Windows has no microphone stream yet).
/// </summary>
public sealed class WatchAndListenPage : ContentPage
{
    private const string ModelSetting = "watch_listen_model";
    private const int ContextTokens = 4096;     // ~1.1k in (3 frames, 5-15 s of sound) and 120 out
    private const int MaxCaptions = 100;

    private readonly IChatPageSettings _settings;
    private readonly Action<EngineSettings> _configureEngine;
    private readonly IList<ChatModelOption> _models;
    private readonly LiveCameraPreview _preview;
    private readonly Border _flip;
    private readonly Label _flipLabel;
    private readonly Label _badge;
    private readonly Button _startStop;
    private readonly Button _copy;
    private readonly Picker _modelPicker;
    private readonly Label _status;
    private readonly ModelLoadCard _loadCard;
    private readonly VerticalStackLayout _captions;
    private readonly List<SceneCaption> _captionList = new List<SceneCaption>();
    // The newest cards' frame strips; older cards drop theirs, so 100 cards don't keep 300 JPEGs.
    private readonly Queue<Layout> _frameStrips = new Queue<Layout>();
    private const int CardsWithFrames = 10;
    private readonly object _gate = new object();

    private LiveCamera _camera;
    private bool _front;
    private bool _visible;
    private int _rotation;
    private Mat _pending, _shown;
    private bool _presentQueued;

    private LanguageModel _model;
    private ChatModelOption _modelOption;
    private string _backend;
    private CancellationTokenSource _run;
    private Task _running = Task.CompletedTask;
    private MicrophoneSoundSource _microphone;
    private bool _holdsMicrophone;

    /// <summary>Whether the page can run here: LiteRT-LM is in the app, with a model that takes images and audio, and the microphone streams.</summary>
    public static bool IsSupported => MicrophoneSoundSource.IsSupported && LiveCamera.Supported && UsableModels().Count > 0;

    /// <summary>Create the page.</summary>
    /// <param name="settings">The chat's settings, for its GPU setting (and the model chosen here); null for Preferences under the chat's keys</param>
    /// <param name="configureEngine">Changes the engine's settings as the chat does (<see cref="ChatPageOptions.ConfigureEngine"/>), or null</param>
    /// <param name="options">How it watches and listens; null for the defaults</param>
    public WatchAndListenPage(IChatPageSettings settings = null, Action<EngineSettings> configureEngine = null, SceneNarrationOptions options = null)
    {
        _settings = settings ?? new PreferencesChatPageSettings();
        _configureEngine = configureEngine;
        Options = options ?? new SceneNarrationOptions();
        _models = UsableModels();
        Title = "Watch and listen";
        ChatResources.EnsureTheme();
        ChatResources.SetThemeColor(this, BackgroundColorProperty, "ChatPageBackground");
        Color pill = Color.FromArgb("#E01A1C2E");

        _preview = new LiveCameraPreview { Aspect = Aspect.AspectFit, BackgroundColor = Colors.Black };
        _badge = new Label
        {
            TextColor = Colors.White, FontSize = 13, IsVisible = false,
            BackgroundColor = pill, Padding = new Thickness(10, 4),
            Margin = new Thickness(12), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start
        };
        _flip = new Border
        {
            BackgroundColor = pill,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(12, 6),
            Margin = new Thickness(12),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false,
            Content = _flipLabel = new Label { TextColor = Colors.White, FontSize = 15 }
        };
        var flipTap = new TapGestureRecognizer();
        flipTap.Tapped += async (s, e) => await FlipAsync();
        _flip.GestureRecognizers.Add(flipTap);
        UpdateFlipLabel();
#if ANDROID || (IOS && !MACCATALYST)
        _flip.IsVisible = LiveCamera.HasFrontCamera;
#endif
        var camera = new Grid { BackgroundColor = Colors.Black, Children = { _preview, _badge, _flip } };

        _startStop = new Button { Text = "Start", FontAttributes = FontAttributes.Bold, MinimumWidthRequest = 110 };
        ChatResources.SetThemeColor(_startStop, Button.BackgroundColorProperty, "ChatAccent");
        _startStop.TextColor = Colors.White;
        _startStop.Clicked += async (s, e) => await StartOrStopAsync();
        _copy = new Button { Text = "Copy", IsVisible = false, BackgroundColor = Colors.Transparent };
        ChatResources.SetThemeColor(_copy, Button.TextColorProperty, "ChatAccentText");
        _copy.Clicked += async (s, e) => await CopyAsync();
        _modelPicker = new Picker { ItemsSource = _models.Select(m => m.DisplayName).ToList(), HorizontalOptions = LayoutOptions.Fill };
        string saved = _settings.Get<string>(ModelSetting, null);
        int index = Math.Max(0, _models.ToList().FindIndex(m => m.Id == saved));
        if (_models.Count > 0)
            _modelPicker.SelectedIndex = index;
        _modelPicker.IsVisible = _models.Count > 1;
        _modelPicker.SelectedIndexChanged += async (s, e) => await ModelChangedAsync();
        _status = new Label { FontSize = 13, Text = "Tap Start, and the model describes what the camera sees and the microphone hears, every few seconds." };
        ChatResources.SetThemeColor(_status, Label.TextColorProperty, "ChatSecondaryText");
        _loadCard = new ModelLoadCard();

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10
        };
        buttons.Add(_startStop, 0);
        buttons.Add(_modelPicker, 1);
        buttons.Add(_copy, 2);
        var controls = new VerticalStackLayout { Padding = new Thickness(16, 10, 16, 4), Spacing = 6, Children = { buttons, _status, _loadCard } };

        _captions = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(16, 4, 16, 16) };
        var grid = new Grid
        {
            RowDefinitions = { new RowDefinition(new GridLength(1.2, GridUnitType.Star)), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };
        grid.Add(camera, 0, 0);
        grid.Add(controls, 0, 1);
        grid.Add(new ScrollView { Content = _captions }, 0, 2);
        Content = grid;
    }

    /// <summary>How it watches and listens.</summary>
    public SceneNarrationOptions Options { get; }

    // The catalog's on-device models that take images and audio (Gemma 4), Gemma 4 E2B first.
    private static IList<ChatModelOption> UsableModels() =>
        ChatDevicePlatform.AvailableModels().Where(m => !m.IsRemote && m.AcceptsImages && m.AcceptsAudio)
            .OrderBy(m => m.Id == "Gemma4E2B" ? 0 : 1).ToList();

    private ChatModelOption SelectedModel =>
        _modelPicker.SelectedIndex >= 0 && _modelPicker.SelectedIndex < _models.Count ? _models[_modelPicker.SelectedIndex] : null;

    /// <inheritdoc/>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        if (Window != null)
        {
            Window.Stopped -= OnWindowStopped;
            Window.Resumed -= OnWindowResumed;
            Window.Stopped += OnWindowStopped;
            Window.Resumed += OnWindowResumed;
        }
        await StartCameraAsync();
    }

    /// <inheritdoc/>
    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        if (Window != null)
        {
            Window.Stopped -= OnWindowStopped;
            Window.Resumed -= OnWindowResumed;
        }
        await StopAsync();
        await StopCameraAsync();
        await FreeModelAsync();
    }

    // Foreground only: the run stops and the camera is let go of in the background.
    private async void OnWindowStopped(object sender, EventArgs e)
    {
        await StopAsync();
        await StopCameraAsync();
    }

    private async void OnWindowResumed(object sender, EventArgs e)
    {
        if (_visible)
            await StartCameraAsync();
    }

    private async Task StartOrStopAsync()
    {
        if (_run != null)
        {
            await StopAsync();
            return;
        }
        await StartAsync();
    }

    private async Task StartAsync()
    {
        ChatModelOption option = SelectedModel;
        if (option == null)
            return;
        _startStop.IsEnabled = false;
        _modelPicker.IsEnabled = false;
        try
        {
            if (_camera == null)
                await StartCameraAsync();
            if (_camera == null)
                return;
            if (!await EnsureModelAsync(option))
                return;
            if (!_visible)
                return;

            await MicrophoneGate.EnterAsync();
            _holdsMicrophone = true;
            _microphone = new MicrophoneSoundSource();
            bool listening = await _microphone.StartAsync();
            if (!listening)
            {
                _microphone.Dispose();
                _microphone = null;
                ReleaseMicrophone();
            }

            var policy = new WorkloadPolicy();
            var narrator = new SceneNarrator(new LiteRtLmChatClient(_model), CaptureFrameAsync, _microphone, Options)
            {
                PauseReason = async token => policy.PauseReason(await DeviceWorkload.ReadAsync(), false)
            };
            narrator.Caption += caption => MainThread.BeginInvokeOnMainThread(() => AddCaption(caption));
            narrator.Thinking += round => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_run != null)
                    _badge.Text = "● Round " + round + " · thinking…";
            });
            _run = new CancellationTokenSource();
            CancellationToken token = _run.Token;
            DeviceDisplay.Current.KeepScreenOn = true;
            _startStop.Text = "Stop";
            ChatResources.SetThemeColor(_startStop, Button.BackgroundColorProperty, "ChatDanger");
            _badge.Text = "● Watching" + (listening ? " and listening" : "");
            _badge.IsVisible = true;
            _status.Text = (listening ? "" : "No microphone access: watching only. ") + option.DisplayName + " on the " + _backend.ToUpperInvariant() +
                ", a description every " + Options.Window.TotalSeconds.ToString("0") + " s or so; it stops by itself after " +
                Options.TimeLimit.TotalMinutes.ToString("0") + " minutes.";
            _running = RunAsync(narrator, token);
        }
        catch (Exception ex)
        {
            _status.Text = "Couldn't start: " + ex.Message;
            ReleaseMicrophone();
        }
        finally
        {
            _startStop.IsEnabled = true;
            _modelPicker.IsEnabled = _run == null;
        }
    }

    private async Task RunAsync(SceneNarrator narrator, CancellationToken token)
    {
        string reason = await Task.Run(() => narrator.RunAsync(token));
        MicrophoneSoundSource microphone = _microphone;
        _microphone = null;
        if (microphone != null)
        {
            try { await microphone.StopAsync(); }
            catch (Exception) { }
            microphone.Dispose();
        }
        ReleaseMicrophone();
        DeviceDisplay.Current.KeepScreenOn = false;
        _run?.Dispose();
        _run = null;
        _badge.IsVisible = false;
        _startStop.Text = "Start";
        ChatResources.SetThemeColor(_startStop, Button.BackgroundColorProperty, "ChatAccent");
        _modelPicker.IsEnabled = true;
        _status.Text = reason == SceneNarrator.StoppedReason ? "Stopped." : reason + ".";
    }

    private async Task StopAsync()
    {
        _run?.Cancel();
        try { await _running; }
        catch (Exception) { }
    }

    private void ReleaseMicrophone()
    {
        if (_holdsMicrophone)
        {
            _holdsMicrophone = false;
            MicrophoneGate.Exit();
        }
    }

    // The chosen model, loaded on the chat's backend (Gemma 4's vision encoder there too); downloaded after asking.
    private async Task<bool> EnsureModelAsync(ChatModelOption option)
    {
        if (_model != null && _modelOption == option)
            return true;
        await FreeModelAsync();
        bool downloaded = option.ModelFile?.IsLocalFileVerified ?? true;
        if (!downloaded)
        {
            string size = option.DownloadSize > 0 ? " (" + (option.DownloadSize / 1e9).ToString("0.0") + " GB)" : "";
            if (!await DisplayAlertAsync("Download " + option.DisplayName + "?",
                    "The model" + size + " is downloaded once and then runs on this device.", "Download", "Cancel"))
                return false;
        }
        _status.Text = "Loading " + option.DisplayName + "…";
        using var cancel = new CancellationTokenSource();
        ChatModelLoad load = await ChatModelLoader.LoadAsync(option, ChatModelLoader.PreferredBackend(_settings, option), ContextTokens, true,
            (model, backend) =>
            {
                if (model is Gemma4 gemma)
                    gemma.VisionBackend = backend == "gpu" ? "gpu" : "cpu";
            },
            _configureEngine,
            state => MainThread.BeginInvokeOnMainThread(() => _loadCard.State = state),
            cancel.Token);
        _loadCard.State = null;
        _loadCard.IsVisible = false;
        if (load.Model == null)
        {
            _status.Text = "Couldn't load " + option.DisplayName + ": " + load.Error;
            return false;
        }
        _model = load.Model;
        _modelOption = option;
        _backend = load.Backend;
        return true;
    }

    private async Task ModelChangedAsync()
    {
        ChatModelOption option = SelectedModel;
        if (option == null)
            return;
        _settings.Set(ModelSetting, option.Id);
        if (_run == null && _modelOption != option)
            await FreeModelAsync();
    }

    private async Task FreeModelAsync()
    {
        LanguageModel model = _model;
        _model = null;
        _modelOption = null;
        if (model != null)
            await Task.Run(() => model.Dispose());
    }

    private async Task<byte[]> CaptureFrameAsync(CancellationToken token)
    {
        LiveCamera camera = _camera;
        return camera == null ? null : await camera.CaptureAsync(token);
    }

    private void AddCaption(SceneCaption caption)
    {
        _captionList.Add(caption);
        if (_captionList.Count > MaxCaptions)
            _captionList.RemoveAt(0);
        if (_run != null)
            _badge.Text = "● Round " + caption.Round + " · " + caption.Latency.TotalSeconds.ToString("0.0") + " s";

        var time = new Label
        {
            FontSize = 12,
            Text = caption.End.ToLocalTime().ToString("HH:mm:ss") + " · " + caption.Latency.TotalSeconds.ToString("0.0") + " s" +
                (caption.HeardSound ? "" : " · 🔇")
        };
        ChatResources.SetThemeColor(time, Label.TextColorProperty, "ChatSecondaryText");
        var text = new Label { FontSize = 15, Text = caption.Text };
        ChatResources.SetThemeColor(text, Label.TextColorProperty, "ChatPrimaryText");
        var content = new VerticalStackLayout { Spacing = 2, Children = { time, text } };
        if (caption.Images.Count > 0)
        {
            // What the model saw, to tell an old picture from a wrong description.
            var strip = new HorizontalStackLayout { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            foreach (byte[] jpeg in caption.Images)
                strip.Children.Add(new Image { HeightRequest = 72, Aspect = Aspect.AspectFit, Source = ImageSource.FromStream(() => new MemoryStream(jpeg)) });
            content.Children.Add(strip);
            _frameStrips.Enqueue(strip);
            while (_frameStrips.Count > CardsWithFrames)
            {
                Layout old = _frameStrips.Dequeue();
                (old.Parent as Layout)?.Children.Remove(old);
            }
        }
        var card = new Border
        {
            Padding = new Thickness(12, 8),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = Colors.Transparent,
            Content = content
        };
        ChatResources.SetThemeColor(card, Border.BackgroundColorProperty, "ChatSurface");
        _captions.Insert(0, card);
        while (_captions.Count > MaxCaptions)
            _captions.RemoveAt(_captions.Count - 1);
        _copy.IsVisible = true;
    }

    private async Task CopyAsync()
    {
        var all = new StringBuilder();
        foreach (SceneCaption caption in _captionList)
            all.Append(caption.End.ToLocalTime().ToString("HH:mm:ss")).Append('\n').Append(caption.Text).Append("\n\n");
        await Clipboard.Default.SetTextAsync(all.ToString().TrimEnd());
        _status.Text = "Copied " + _captionList.Count + " descriptions.";
    }

    private void UpdateFlipLabel() => _flipLabel.Text = _front ? "Back camera" : "Selfie camera";

    private async Task FlipAsync()
    {
        _front = !_front;
        UpdateFlipLabel();
        await StopCameraAsync();
        await StartCameraAsync();
    }

    private void OnDisplayChanged(object sender, DisplayInfoChangedEventArgs e) =>
        Volatile.Write(ref _rotation, LiveCamera.FrameRotation(_front) ?? 0);

    private async Task StartCameraAsync()
    {
        if (_camera != null)
            return;
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
        {
            _status.Text = "Camera access is needed: allow the camera for this app in the system settings.";
            return;
        }
        if (!_visible || _camera != null)
            return;
        Volatile.Write(ref _rotation, LiveCamera.FrameRotation(_front) ?? 0);
        DeviceDisplay.Current.MainDisplayInfoChanged += OnDisplayChanged;
        var camera = _camera = new LiveCamera { RotationDegrees = () => Volatile.Read(ref _rotation), Front = _front };
        camera.FrameReady = frame => OnFrame(camera, frame);
        camera.Failed = message => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_camera == camera)
                _status.Text = "The camera stopped: " + message;
        });
        try
        {
            await camera.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (_camera == camera)
                _status.Text = "The camera couldn't be opened: " + ex.Message;
            await StopCameraAsync();
        }
    }

    private async Task StopCameraAsync()
    {
        LiveCamera camera = _camera;
        _camera = null;
        DeviceDisplay.Current.MainDisplayInfoChanged -= OnDisplayChanged;
        if (camera != null)
        {
            camera.FrameReady = null;
            camera.Failed = null;
            await camera.StopAsync();
        }
        _preview.ClearFrame();
        lock (_gate)
        {
            _presentQueued = false;
            _pending?.Dispose(); _pending = null;
            _shown?.Dispose(); _shown = null;
        }
    }

    // On the camera's thread: show the frame (iOS's display layer takes it here; elsewhere the newest copy goes to the UI thread).
    private void OnFrame(LiveCamera camera, Mat frame)
    {
        if (_camera != camera)
            return;
        if (_preview.PresentsFromAnyThread)
        {
            _preview.Present(frame);
            return;
        }
        lock (_gate)
        {
            _pending ??= new Mat();
            frame.CopyTo(_pending);
            if (_presentQueued)
                return;
            _presentQueued = true;
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Mat newest;
            lock (_gate)
            {
                _presentQueued = false;
                if (_camera != camera || _pending == null)
                    return;
                newest = _pending;
                _pending = _shown;
                _shown = newest;
            }
            _preview.Present(newest);
        });
    }
}
