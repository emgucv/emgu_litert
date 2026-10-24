// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Diagnostics;

namespace Maui.Demo.Lite;

// One worker owns the native capture and Mats. Preview consumers must copy a frame before returning.
// Only requested snapshots are JPEG-encoded; the live preview receives raw pixels.
internal sealed partial class LiveCamera
{
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.Android ||
        DeviceInfo.Platform == DevicePlatform.MacCatalyst || DeviceInfo.Platform == DevicePlatform.WinUI;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<byte[]> _snapshot;
    private Task _worker;
    public bool IsRunning => !_stop.IsCancellationRequested;
    public Action<Mat> FrameReady;
    public Action<string> Failed;
    public Func<int> RotationDegrees;
    /// <summary>The selfie camera instead of the back one (Android, Camera2 only); set before <see cref="StartAsync"/>.</summary>
    public bool Front { get; init; }
    // Frames larger than this (on the long side) are scaled down: the detectors work at 192-640 px anyway.
    private const int MaxFrameSide = 960;

    /// <summary>
    /// How far to turn the camera's frames so they are upright on the display as it is now (for
    /// <see cref="RotationDegrees"/>); null where the frames need no turning (only Android's cameras do).
    /// </summary>
    public static int? FrameRotation(bool front)
    {
#if ANDROID
        var manager = (Android.Hardware.Camera2.CameraManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.CameraService);
        string id = FindCameraId(front) ?? manager.GetCameraIdList().FirstOrDefault();
        if (id == null) return null;
        using var characteristics = manager.GetCameraCharacteristics(id);
        int sensor = ((Java.Lang.Integer)characteristics.Get(Android.Hardware.Camera2.CameraCharacteristics.SensorOrientation)).IntValue();
        int display = DeviceDisplay.Current.MainDisplayInfo.Rotation switch
        {
            DisplayRotation.Rotation90 => 90,
            DisplayRotation.Rotation180 => 180,
            DisplayRotation.Rotation270 => 270,
            _ => 0
        };
        // Android's rotation compensation: a front camera turns the other way.
        return front ? (sensor + display) % 360 : (sensor - display + 360) % 360;
#else
        return null;
#endif
    }

    public async Task StartAsync(CancellationToken token)
    {
        _worker = Task.Run(RunAsync);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
    }

    public async Task<byte[]> CaptureAsync(CancellationToken token)
    {
        // Chat snapshots and alert snapshots share this camera; each waits for its own fresh frame.
        await _captureGate.WaitAsync(token);
        try
        {
            TaskCompletionSource<byte[]> request;
            lock (_gate)
            {
                if (_stop.IsCancellationRequested) throw new InvalidOperationException("The camera is closed.");
                request = _snapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            try { return await request.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
            finally { lock (_gate) { if (_snapshot == request) _snapshot = null; } }
        }
        finally { _captureGate.Release(); }
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        lock (_gate) _snapshot?.TrySetException(new InvalidOperationException("The camera was closed before capture completed."));
        if (_worker != null) await _worker;
    }

    private async Task RunAsync()
    {
        try
        {
#if ANDROID
            // Camera2 first: it can record a video clip while the preview and the detector keep running (LiveCamera.Camera2.cs);
            // OpenCV's camera backend below takes over on a device where Camera2 can't be set up.
            if (await RunCamera2Async()) return;
            if (Front) throw new InvalidOperationException("The selfie camera needs Camera2, which couldn't be set up on this phone.");
#endif
#if WINDOWS
            // The default Media Foundation backend took 11-12 s to open a Logitech StreamCam (DirectShow 1.3 s) - longer
            // than the start timeout; fall back to it for cameras DirectShow can't open.
            VideoCapture capture = new VideoCapture(0, VideoCapture.API.DShow);
            if (!capture.IsOpened) { capture.Dispose(); capture = new VideoCapture(0, VideoCapture.API.Any); }
            using var captureOwner = capture;
#else
            using var capture = new VideoCapture(0,
#if ANDROID
                VideoCapture.API.Android
#else
                VideoCapture.API.Any
#endif
            );
#endif
            if (!capture.IsOpened) throw new InvalidOperationException("No camera could be opened. Check that a camera is connected and available to this app.");
#if !MACCATALYST && !IOS
            capture.Set(CapProp.FrameWidth, 640);
            capture.Set(CapProp.FrameHeight, 480);
#endif
            // On Mac Catalyst OpenCV's AVFoundation backend applies a size it is given as the output's video settings,
            // and AVFoundation throws an Objective-C exception - which ends the app - when that is larger than the
            // camera's active format (it was, for a Logitech StreamCam). So the camera's own size is kept there, and
            // large frames are scaled down below.
#if ANDROID
            // OpenCV's Android camera backend defaults to raw NV21/YV12, not a displayable BGR image.
            if (!capture.Set(CapProp.FourCC, VideoWriter.Fourcc('B', 'G', 'R', '3')))
                throw new InvalidOperationException("The camera backend could not supply color frames.");
#endif
            using var frame = new Mat();
            using var rotated = new Mat();
            using var smaller = new Mat();
            long opened = Stopwatch.GetTimestamp();
            while (!_stop.IsCancellationRequested)
            {
                long started = Stopwatch.GetTimestamp();
                // A request registered after this point must wait for the next frame, not receive an older preview.
                TaskCompletionSource<byte[]> request;
                lock (_gate) request = _snapshot;
                if (!capture.Read(frame) || frame.IsEmpty)
                {
                    // AVFoundation (Mac Catalyst) starts the camera asynchronously: the first reads can come back empty.
                    if (!_ready.Task.IsCompleted && Stopwatch.GetElapsedTime(opened) < TimeSpan.FromSeconds(3))
                    {
                        await Task.Delay(50, _stop.Token);
                        continue;
                    }
                    throw new InvalidOperationException(_ready.Task.IsCompleted ? "The camera stopped delivering frames." : "The camera delivered no frames.");
                }
                Mat image = frame;
                int longest = Math.Max(frame.Width, frame.Height);
                if (longest > MaxFrameSide)
                {
                    double scale = (double)MaxFrameSide / longest;
                    CvInvoke.Resize(frame, smaller, new System.Drawing.Size((int)(frame.Width * scale), (int)(frame.Height * scale)), 0, 0, Inter.Area);
                    image = smaller;
                }
                int rotation = RotationDegrees?.Invoke() ?? 0;
                if (rotation != 0)
                {
                    CvInvoke.Rotate(image, rotated, rotation == 90 ? RotateFlags.Rotate90Clockwise :
                        rotation == 180 ? RotateFlags.Rotate180 : RotateFlags.Rotate90CounterClockwise);
                    image = rotated;
                }
                FrameReady?.Invoke(image);
                _ready.TrySetResult(true);
                if (request != null)
                {
                    byte[] jpeg = CvInvoke.Imencode(".jpg", image,
                        new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 85));
                    request.TrySetResult(jpeg);
                }
                // Pace to at most 30 FPS, including the camera read/render time rather than adding a fixed gap.
                TimeSpan delay = TimeSpan.FromSeconds(1.0 / 30) - Stopwatch.GetElapsedTime(started);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            lock (_gate) _snapshot?.TrySetException(ex);
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            _stop.Cancel();
            _ready.TrySetCanceled();
        }
    }
}
