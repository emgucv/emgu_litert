// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Diagnostics;

namespace Maui.Demo.Lite;

// One worker owns the native capture and Mats. Preview consumers must copy a frame before returning.
// Only requested snapshots are JPEG-encoded; the live preview receives raw pixels.
internal sealed class LiveCamera
{
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.Android ||
        DeviceInfo.Platform == DevicePlatform.MacCatalyst || DeviceInfo.Platform == DevicePlatform.WinUI;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<byte[]> _snapshot;
    private Task _worker;
    public bool IsRunning => !_stop.IsCancellationRequested;
    public Action<Mat> FrameReady;
    public Action<string> Failed;
    public Func<int> RotationDegrees;

    public async Task StartAsync(CancellationToken token)
    {
        _worker = Task.Run(RunAsync);
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
    }

    public async Task<byte[]> CaptureAsync(CancellationToken token)
    {
        TaskCompletionSource<byte[]> request;
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) throw new InvalidOperationException("The camera is closed.");
            if (_snapshot != null) throw new InvalidOperationException("A camera capture is already in progress.");
            request = _snapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try { return await request.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
        finally { lock (_gate) { if (_snapshot == request) _snapshot = null; } }
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
            using var capture = new VideoCapture(0,
#if ANDROID
                VideoCapture.API.Android
#else
                VideoCapture.API.Any
#endif
            );
            if (!capture.IsOpened) throw new InvalidOperationException("No camera could be opened. Check that a camera is connected and available to this app.");
            capture.Set(CapProp.FrameWidth, 640);
            capture.Set(CapProp.FrameHeight, 480);
#if ANDROID
            // OpenCV's Android camera backend defaults to raw NV21/YV12, not a displayable BGR image.
            if (!capture.Set(CapProp.FourCC, VideoWriter.Fourcc('B', 'G', 'R', '3')))
                throw new InvalidOperationException("The camera backend could not supply color frames.");
#endif
            using var frame = new Mat();
            using var rotated = new Mat();
            while (!_stop.IsCancellationRequested)
            {
                long started = Stopwatch.GetTimestamp();
                // A request registered after this point must wait for the next frame, not receive an older preview.
                TaskCompletionSource<byte[]> request;
                lock (_gate) request = _snapshot;
                if (!capture.Read(frame) || frame.IsEmpty) throw new InvalidOperationException("The camera stopped delivering frames.");
                Mat image = frame;
                int rotation = RotationDegrees?.Invoke() ?? 0;
                if (rotation != 0)
                {
                    CvInvoke.Rotate(frame, rotated, rotation == 90 ? RotateFlags.Rotate90Clockwise :
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
#endif
