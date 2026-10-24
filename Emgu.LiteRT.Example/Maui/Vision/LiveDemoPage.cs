//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.CV;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite;

/// <summary>
/// A photo demo's model on the live camera: each frame is analyzed by the demo page's <see cref="DemoPage.Analyze"/>
/// - the same prepared model, with the backend chosen there - and shown with what it found drawn on it, full screen.
/// The camera is <see cref="LiveCamera"/> (Camera2 on Android, OpenCV's capture on Mac Catalyst and Windows), like the
/// chat's camera monitors. As the Emgu CV demo's live pages: the feed edge to edge, a floating Exit, and a strip with
/// the results.
/// </summary>
internal sealed class LiveDemoPage : ContentPage
{
    private static readonly Color Pill = Color.FromArgb("#E01A1C2E");

    private readonly DemoPage _demo;
    private readonly LiveCameraPreview _preview;
    private readonly Label _resultLabel;
    private readonly Label _statusLabel;
    private readonly LoadingOverlay _loading;
    private readonly Border _flip;
    private readonly Label _flipLabel;   // names the camera it switches to ("Flip" read as mirroring the picture)
    private readonly object _gate = new object();
    private LiveCamera _camera;
    private bool _front;
    private bool _prepared;
    private bool _visible;
    private int _rotation;
    private int _busy;
    // The newest drawn frame waiting for the UI thread, and the one shown (kept until the next is shown).
    private Mat _pending, _shown, _work;
    private bool _presentQueued;
    private int _version;
    // Frames per second over the last second.
    private readonly Queue<long> _frameTimes = new Queue<long>();
    private long _lastText;

    public LiveDemoPage(DemoPage demo, string title)
    {
        _demo = demo;
        Title = title + " (live)";
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Colors.Black;

        _preview = new LiveCameraPreview { Aspect = Aspect.AspectFit, BackgroundColor = Colors.Black };

        var exit = new Border
        {
            BackgroundColor = Pill,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Padding = new Thickness(12, 8, 18, 8),
            Margin = new Thickness(12, 12, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Content = new HorizontalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    Theme.MakeIcon(Theme.GlyphChevronLeft, Colors.White, 24),
                    new Label { Text = "Exit", FontFamily = Theme.TitleFont, FontSize = 17, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center }
                }
            }
        };
        SemanticProperties.SetDescription(exit, "Close the live camera");
        exit.OnTap(async () => await Navigation.PopAsync(false));

        _flip = new Border
        {
            BackgroundColor = Pill,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Padding = new Thickness(14, 8),
            Margin = new Thickness(0, 12, 12, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false,
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    Theme.MakeIcon(Theme.GlyphCamera, Colors.White, 20),
                    (_flipLabel = new Label { FontFamily = Theme.TitleFont, FontSize = 17, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center })
                }
            }
        };
        UpdateFlipLabel();
        _flip.OnTap(async () => await FlipAsync());
#if ANDROID
        _flip.IsVisible = LiveCamera.FindCameraId(true) != null;
#endif

        _resultLabel = new Label { FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, Text = "Starting the camera…" };
        _statusLabel = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Color.FromArgb("#D6D6DE"), HorizontalTextAlignment = TextAlignment.Center };
        var strip = new Border
        {
            BackgroundColor = Pill,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Padding = new Thickness(20, 10),
            Margin = new Thickness(16, 0, 16, 20),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            MaximumWidthRequest = 560,
            Content = new VerticalStackLayout { Spacing = 3, Children = { _resultLabel, _statusLabel } }
        };

        _loading = new LoadingOverlay();
        Content = new Grid { Children = { _preview, exit, _flip, strip, _loading } };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _demo.SetOptionsEnabledForLive(false);
        if (Window != null)
        {
            Window.Stopped -= OnWindowStopped;
            Window.Resumed -= OnWindowResumed;
            Window.Stopped += OnWindowStopped;
            Window.Resumed += OnWindowResumed;
        }
        if (!_prepared)
        {
            _loading.Show("Preparing the model...");
            try
            {
                await _demo.PrepareForLiveAsync(message => MainThread.BeginInvokeOnMainThread(() => _loading.SetMessage(message)));
                _prepared = true;
            }
            catch (Exception ex)
            {
                _loading.Hide();
                _resultLabel.Text = "The model couldn't be loaded";
                _statusLabel.Text = ex.Message;
                return;
            }
            _loading.Hide();
        }
        await StartCameraAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        if (Window != null)
        {
            Window.Stopped -= OnWindowStopped;
            Window.Resumed -= OnWindowResumed;
        }
        await StopCameraAsync();
        _demo.SetOptionsEnabledForLive(true);
    }

    // The camera is let go of while the app is in the background, and opened again when it comes back.
    private async void OnWindowStopped(object sender, EventArgs e) => await StopCameraAsync();

    private async void OnWindowResumed(object sender, EventArgs e)
    {
        if (_visible && _prepared)
            await StartCameraAsync();
    }

    private void UpdateFlipLabel()
    {
        _flipLabel.Text = _front ? "Back camera" : "Selfie camera";
        SemanticProperties.SetDescription(_flip, _front ? "Switch to the back camera" : "Switch to the selfie camera");
    }

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
            _resultLabel.Text = "Camera access is needed";
            _statusLabel.Text = "Allow the camera for this app in the system settings.";
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
            if (_camera != camera) return;
            _resultLabel.Text = "The camera stopped";
            _statusLabel.Text = message;
        });
        _resultLabel.Text = "Starting the camera…";
        _statusLabel.Text = "";
        try
        {
            await camera.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (_camera == camera)
            {
                _resultLabel.Text = "The camera couldn't be opened";
                _statusLabel.Text = ex.Message;
            }
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
        // Wait for a frame being analyzed, then let go of the frames.
        for (int i = 0; i < 100 && Volatile.Read(ref _busy) != 0; i++)
            await Task.Delay(20);
        _preview.ClearFrame();
        lock (_gate)
        {
            _version++;
            _presentQueued = false;
            _pending?.Dispose(); _pending = null;
            _shown?.Dispose(); _shown = null;
            _work?.Dispose(); _work = null;
            _frameTimes.Clear();
        }
    }

    // On the camera's thread: analyze the frame, draw on a copy, and show the newest drawn frame. A frame that comes
    // while one is being analyzed is skipped.
    private void OnFrame(LiveCamera camera, Mat frame)
    {
        if (_camera != camera || Interlocked.Exchange(ref _busy, 1) != 0)
            return;
        try
        {
            Mat render;
            lock (_gate)
            {
                render = _work ?? new Mat();
                _work = null;
            }
            frame.CopyTo(render);
            Stopwatch watch = Stopwatch.StartNew();
            DemoResult result;
            try
            {
                result = _demo.AnalyzeLive(frame, render);
            }
            catch (Exception ex)
            {
                result = new DemoResult { Summary = "Couldn't analyze the frame: " + ex.Message };
            }
            watch.Stop();
            long now = Stopwatch.GetTimestamp();
            int version;
            double fps;
            lock (_gate)
            {
                // The newest drawn frame replaces one not shown yet (whose Mat is used for the next frame).
                _work = _pending;
                _pending = render;
                _frameTimes.Enqueue(now);
                while (_frameTimes.Count > 0 && Stopwatch.GetElapsedTime(_frameTimes.Peek(), now) > TimeSpan.FromSeconds(1))
                    _frameTimes.Dequeue();
                fps = _frameTimes.Count;
                version = _version;
                if (_presentQueued)
                    render = null;
                else
                    _presentQueued = true;
            }
            bool showText = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastText), now) > TimeSpan.FromMilliseconds(250);
            if (showText)
                Interlocked.Exchange(ref _lastText, now);
            string resultText = showText ? ResultText(result) : null;
            string statusText = showText ? $"{result.Summary ?? $"{watch.ElapsedMilliseconds} ms"} · {fps:0} fps" : null;
            if (render != null || showText)
                MainThread.BeginInvokeOnMainThread(() => Present(camera, version, resultText, statusText));
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void Present(LiveCamera camera, int version, string resultText, string statusText)
    {
        if (_camera != camera)
            return;
        Mat newest = null;
        lock (_gate)
        {
            if (version == _version && _presentQueued && _pending != null)
            {
                newest = _pending;
                _pending = _shown;
                _shown = newest;
                _presentQueued = false;
            }
        }
        if (newest != null)
            _preview.Present(newest);
        if (resultText != null)
            _resultLabel.Text = resultText;
        if (statusText != null)
            _statusLabel.Text = statusText;
    }

    // The top results on one or two lines.
    private static string ResultText(DemoResult result)
    {
        var rows = result.Rows.Where(r => !string.IsNullOrEmpty(r.Label)).Take(3)
            .Select(r => string.IsNullOrEmpty(r.Value) ? r.Label : r.Label + " " + r.Value).ToList();
        return rows.Count == 0 ? "Nothing found" : string.Join(" · ", rows);
    }
}
