// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using System.Diagnostics;
using System.Runtime.InteropServices;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Views;
using Emgu.CV;
using Emgu.CV.CvEnum;

namespace Maui.Demo.Lite;

// The Android live camera through Camera2 instead of OpenCV's camera backend (TODO item 43): a session with a 640x480 YUV image reader (what the
// preview and the detectors get, as before). A clip adds a MediaRecorder's surface: the session is configured again with it when the clip starts
// and without it when it ends (a persistent MediaCodec input surface, which would have avoided that, isn't accepted when a session is created:
// it has no size until a recorder takes it), so the frames pause briefly at both ends while the preview and the detector keep running during the
// clip. The S10's and the Pixel Fold's cameras all ran an analysis stream and a 720p recording together (TODO item 43).
internal sealed partial class LiveCamera
{
    internal sealed class ClipResult
    {
        public string Path;
        public TimeSpan Duration;
        public bool WithAudio;
        public long Bytes;
    }

    private readonly SemaphoreSlim _recordGate = new(1, 1);
    private CameraDevice _device;
    private CameraCaptureSession _session;
    private ImageReader _reader;
    private Handler _handler;
    private Android.Util.Size _recordSize;
    private volatile bool _camera2;
    private Mat _bgr, _rotatedBgr;
    private byte[] _y, _u, _v, _nv21;

    /// <summary>Whether this camera runs through Camera2, which clips need.</summary>
    public bool CanRecordClip => _camera2 && !_stop.IsCancellationRequested;

    private sealed class FrameListener(LiveCamera owner) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader reader) => owner.OnFrame(reader);
    }

    private sealed class DeviceCallback(LiveCamera owner) : CameraDevice.StateCallback
    {
        public readonly TaskCompletionSource<CameraDevice> Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void OnOpened(CameraDevice camera) => Opened.TrySetResult(camera);
        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            if (!Opened.TrySetException(new InvalidOperationException("The camera was disconnected."))) owner.Fail("The camera was disconnected.");
        }
        public override void OnError(CameraDevice camera, CameraError error)
        {
            camera.Close();
            if (!Opened.TrySetException(new InvalidOperationException("The camera reported " + error + "."))) owner.Fail("The camera reported " + error + ".");
        }
    }

    private sealed class SessionCallback : CameraCaptureSession.StateCallback
    {
        public readonly TaskCompletionSource<CameraCaptureSession> Configured = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void OnConfigured(CameraCaptureSession session) => Configured.TrySetResult(session);
        public override void OnConfigureFailed(CameraCaptureSession session) => Configured.TrySetException(new InvalidOperationException("The camera session couldn't be configured."));
    }

    private static Android.Util.Size Nearest(Android.Util.Size[] sizes, int width, int height) =>
        sizes.OrderBy(s => Math.Abs(s.Width * s.Height - width * height)).First();

    // Runs until the camera is stopped. False if Camera2 couldn't be set up, so that OpenCV's backend can be tried.
    private async Task<bool> RunCamera2Async()
    {
        HandlerThread thread = null;
        try
        {
            var manager = (CameraManager)Android.App.Application.Context.GetSystemService(Context.CameraService);
            string id = manager.GetCameraIdList().FirstOrDefault() ?? throw new InvalidOperationException("No camera.");
            Android.Util.Size analysisSize;
            using (var characteristics = manager.GetCameraCharacteristics(id))
            {
                var map = (StreamConfigurationMap)characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap);
                analysisSize = Nearest(map.GetOutputSizes((int)ImageFormatType.Yuv420888), 640, 480);
                _recordSize = Nearest(map.GetOutputSizes(Java.Lang.Class.FromType(typeof(MediaRecorder))), 1280, 720);
            }
            thread = new HandlerThread("emgu-camera2");
            thread.Start();
            _handler = new Handler(thread.Looper);
            _bgr = new Mat();
            _rotatedBgr = new Mat();
            _reader = ImageReader.NewInstance(analysisSize.Width, analysisSize.Height, ImageFormatType.Yuv420888, 3);
            _reader.SetOnImageAvailableListener(new FrameListener(this), _handler);

            var opened = new DeviceCallback(this);
            manager.OpenCamera(id, opened, _handler);
            _device = await opened.Opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ConfigureSessionAsync(null);
            _camera2 = true;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("EmguLiteRT", "Camera2 couldn't be set up, using OpenCV's camera: " + ex.Message);
            ReleaseCamera2(thread);
            return false;
        }
        try { await Task.Delay(Timeout.Infinite, _stop.Token); }
        catch (System.OperationCanceledException) { }
        finally
        {
            _camera2 = false;
            lock (_gate) _snapshot?.TrySetException(new InvalidOperationException("The camera was closed before capture completed."));
            // Not on the camera's own thread, which may have asked for the stop (a frame that failed) and is the one to be joined.
            await Task.Run(() => ReleaseCamera2(thread));
        }
        return true;
    }

    private void ReleaseCamera2(HandlerThread thread)
    {
        try { _session?.Close(); } catch (Exception) { }
        try { _device?.Close(); } catch (Exception) { }
        _session = null; _device = null;
        // No more frames after the reader is closed; the handler thread ends once its last callback has run.
        try { _reader?.Close(); } catch (Exception) { }
        _reader = null;
        if (thread != null)
        {
            thread.QuitSafely();
            try { thread.Join(2000); } catch (Exception) { }
        }
        _bgr?.Dispose(); _bgr = null;
        _rotatedBgr?.Dispose(); _rotatedBgr = null;
    }

    private void Fail(string message)
    {
        lock (_gate) _snapshot?.TrySetException(new InvalidOperationException(message));
        _ready.TrySetException(new InvalidOperationException(message));
        if (!_stop.IsCancellationRequested) { _stop.Cancel(); Failed?.Invoke(message); }
    }

    // A new session: the analysis reader, and a recorder's surface while a clip records. The old one is closed first.
    private async Task ConfigureSessionAsync(Surface recorderSurface)
    {
        var device = _device ?? throw new InvalidOperationException("The camera is closed.");
        var old = _session;
        _session = null;
        try { old?.Close(); } catch (Exception) { }
        var surfaces = new List<Surface> { _reader.Surface };
        if (recorderSurface != null) surfaces.Add(recorderSurface);
        var configured = new SessionCallback();
        device.CreateCaptureSession(surfaces, configured, _handler);
        var session = await configured.Configured.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _session = session;
        using var request = device.CreateCaptureRequest(CameraTemplate.Record);
        request.AddTarget(_reader.Surface);
        if (recorderSurface != null) request.AddTarget(recorderSurface);
        request.Set(CaptureRequest.ControlAfMode, (int)ControlAFMode.ContinuousVideo);
        session.SetRepeatingRequest(request.Build(), null, _handler);
    }

    // On the camera's thread, once per frame: to BGR, rotated as the page asks, to the preview and the detectors, and to a snapshot request.
    private void OnFrame(ImageReader reader)
    {
        Android.Media.Image image = null;
        try { image = reader.AcquireLatestImage(); }
        catch (Exception) { return; }
        if (image == null) return;
        try
        {
            if (_stop.IsCancellationRequested || _bgr == null) return;
            TaskCompletionSource<byte[]> request;
            lock (_gate) request = _snapshot;
            ConvertToBgr(image, _bgr);
            Mat output = _bgr;
            int rotation = RotationDegrees?.Invoke() ?? 0;
            if (rotation != 0)
            {
                CvInvoke.Rotate(_bgr, _rotatedBgr, rotation == 90 ? RotateFlags.Rotate90Clockwise :
                    rotation == 180 ? RotateFlags.Rotate180 : RotateFlags.Rotate90CounterClockwise);
                output = _rotatedBgr;
            }
            FrameReady?.Invoke(output);
            _ready.TrySetResult(true);
            if (request != null)
                request.TrySetResult(CvInvoke.Imencode(".jpg", output, new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 85)));
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally { image.Close(); }
    }

    private static void CopyPlane(Android.Media.Image.Plane plane, ref byte[] destination)
    {
        Java.Nio.ByteBuffer buffer = plane.Buffer;
        int length = buffer.Remaining();
        if (destination == null || destination.Length != length) destination = new byte[length];
        buffer.Get(destination, 0, length);
    }

    // YUV_420_888 (any row and pixel strides) to NV21, then OpenCV's conversion.
    private void ConvertToBgr(Android.Media.Image image, Mat bgr)
    {
        int width = image.Width, height = image.Height;
        var planes = image.GetPlanes();
        int length = width * height * 3 / 2;
        if (_nv21 == null || _nv21.Length != length) _nv21 = new byte[length];
        CopyPlane(planes[0], ref _y);
        int yRow = planes[0].RowStride;
        if (yRow == width) Buffer.BlockCopy(_y, 0, _nv21, 0, width * height);
        else for (int r = 0; r < height; r++) Buffer.BlockCopy(_y, r * yRow, _nv21, r * width, width);
        CopyPlane(planes[1], ref _u);
        CopyPlane(planes[2], ref _v);
        int uRow = planes[1].RowStride, uPixel = planes[1].PixelStride, vRow = planes[2].RowStride, vPixel = planes[2].PixelStride;
        int at = width * height;
        for (int r = 0; r < height / 2; r++)
            for (int c = 0; c < width / 2; c++)
            {
                _nv21[at++] = _v[r * vRow + c * vPixel];
                _nv21[at++] = _u[r * uRow + c * uPixel];
            }
        using var yuv = new Mat(height * 3 / 2, width, DepthType.Cv8U, 1);
        Marshal.Copy(_nv21, 0, yuv.DataPointer, length);
        CvInvoke.CvtColor(yuv, bgr, ColorConversion.Yuv2BgrNv21);
    }

    /// <summary>
    /// Records <paramref name="seconds"/> of video (H.264, 720p, with sound from the microphone if asked) into an MP4 file while the preview and the
    /// detectors go on. Cancelling stops the recording and deletes the file.
    /// </summary>
    public async Task<ClipResult> RecordClipAsync(int seconds, string path, bool withAudio, CancellationToken token)
    {
        if (!CanRecordClip) throw new InvalidOperationException("Video clips need the live camera, and it isn't running through Camera2.");
        await _recordGate.WaitAsync(token);
        MediaRecorder recorder = null;
        bool started = false, finished = false;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            recorder = Build.VERSION.SdkInt >= BuildVersionCodes.S ? new MediaRecorder(Android.App.Application.Context) : new MediaRecorder();
            if (withAudio) recorder.SetAudioSource(AudioSource.Mic);
            recorder.SetVideoSource(VideoSource.Surface);
            recorder.SetOutputFormat(OutputFormat.Mpeg4);
            recorder.SetOutputFile(path);
            recorder.SetVideoEncoder(VideoEncoder.H264);
            if (withAudio)
            {
                recorder.SetAudioEncoder(AudioEncoder.Aac);
                recorder.SetAudioEncodingBitRate(96000);
                recorder.SetAudioSamplingRate(44100);
            }
            recorder.SetVideoSize(_recordSize.Width, _recordSize.Height);
            recorder.SetVideoFrameRate(30);
            recorder.SetVideoEncodingBitRate(6_000_000);
            recorder.SetOrientationHint(RotationDegrees?.Invoke() ?? 0);
            recorder.Prepare();
            var clipStarted = Stopwatch.StartNew();
            await ConfigureSessionAsync(recorder.Surface);
            recorder.Start();
            Android.Util.Log.Info("EmguLiteRT", "Clip session ready after " + clipStarted.ElapsedMilliseconds + " ms");
            started = true;
            var clock = Stopwatch.StartNew();
            // The camera closing ends the clip early (what was recorded is kept); cancelling by the caller discards it.
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token).Token); }
            catch (System.OperationCanceledException) when (!token.IsCancellationRequested) { }
            token.ThrowIfCancellationRequested();
            TimeSpan duration = clock.Elapsed;
            started = false;
            recorder.Stop();
            finished = true;
            return new ClipResult { Path = path, Duration = duration, WithAudio = withAudio, Bytes = new FileInfo(path).Length };
        }
        finally
        {
            if (started) { try { recorder.Stop(); } catch (Exception) { } }
            try
            {
                if (!_stop.IsCancellationRequested && _device != null)
                {
                    var ending = Stopwatch.StartNew();
                    await ConfigureSessionAsync(null);
                    Android.Util.Log.Info("EmguLiteRT", "Camera session without the recorder after " + ending.ElapsedMilliseconds + " ms");
                }
            }
            catch (Exception) { }
            try { recorder?.Release(); } catch (Exception) { }
            if (!finished) { try { File.Delete(path); } catch (Exception) { } }
            _recordGate.Release();
        }
    }
}
#endif
