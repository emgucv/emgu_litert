//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if IOS && !MACCATALYST
using System.Diagnostics;
using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Foundation;
using UIKit;

namespace Emgu.LiteRT.Models.CV.Maui;

// The iOS live camera through AVFoundation, the counterpart of Camera2 on Android: an AVCaptureSession in a 4:3 format (1920x1440 on an
// iPad Air M4, for full-sensor photos; the 720p preset where there is none) with a BGRA video data
// output on its own queue. Each frame's pixel buffer is wrapped as a Mat without a copy, scaled down to 960 px and converted to BGR into reused
// Mats (about a millisecond), then given to FrameReady. The frames come upright from AVFoundation itself (the connection's orientation follows
// the interface's, so no CvInvoke.Rotate is needed: FrameRotation stays null here) and unmirrored from the selfie camera, as on Android. Late
// frames are discarded by the output, so a slow consumer costs dropped frames, never a stall. Photos and clips come from the same session
// (TakePhotoAvFoundationAsync, RecordClipAvFoundationAsync), without the session changes Camera2 needs.
public sealed partial class LiveCamera
{
    private AVCaptureSession _session;
    private AVCaptureDeviceInput _videoInput;
    private AVCaptureVideoDataOutput _videoOutput;
    private AVCaptureConnection _videoConnection;
    private AVCapturePhotoOutput _photoOutput;
    private PhotoDelegate _photoDelegate;
    private DispatchQueue _queue;
    private FrameDelegate _frameDelegate;
    private readonly List<NSObject> _observers = new();
    private volatile bool _avFoundation;
    private volatile Recording _recording;
    private readonly object _sessionLock = new();   // the session's stop and a clip's microphone removal, one at a time
    private AVCaptureVideoOrientation _orientation = AVCaptureVideoOrientation.Portrait;
    private volatile bool _orientationFrozen;
    private Mat _bgra, _bgrFrame;
    private long _frameCount;

    /// <summary>How many frames the camera has delivered (for diagnostics).</summary>
    public long FrameCount => Interlocked.Read(ref _frameCount);

    /// <summary>The frames' size as the camera delivers them now, before the 960 px cap (0 before the first frame).</summary>
    public System.Drawing.Size CaptureSize { get; private set; }

    private sealed class FrameDelegate(LiveCamera owner) : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            // The queue is a GCD queue: the Objective-C objects a frame makes need a pool of their own.
            using var pool = new NSAutoreleasePool();
            try { owner.OnSampleBuffer(sampleBuffer); }
            finally { sampleBuffer.Dispose(); }
        }
    }

    // One photo: its JPEG (the AVCapturePhoto lives only through the callback, so the bytes are copied out) and the resolved pixel size.
    private sealed class PhotoDelegate(TaskCompletionSource<(byte[] Bytes, int Width, int Height)> done) : AVCapturePhotoCaptureDelegate
    {
        public override void DidFinishProcessingPhoto(AVCapturePhotoOutput output, AVCapturePhoto photo, NSError error)
        {
            try
            {
                if (error != null) throw new InvalidOperationException("The camera couldn't take the photo: " + error.LocalizedDescription);
                using NSData data = photo.FileDataRepresentation ?? throw new InvalidOperationException("The camera gave no photo data.");
                CMVideoDimensions size = photo.ResolvedSettings.PhotoDimensions;
                done.TrySetResult((data.ToArray(), size.Width, size.Height));
            }
            catch (Exception ex) { done.TrySetException(ex); }
        }
    }

    // The microphone's samples while a clip records (on their own queue).
    private sealed class AudioDelegate(Recording recording) : AVCaptureAudioDataOutputSampleBufferDelegate
    {
        public override void DidOutputSampleBuffer(AVCaptureOutput captureOutput, CMSampleBuffer sampleBuffer, AVCaptureConnection connection)
        {
            using var pool = new NSAutoreleasePool();
            try { recording.AppendAudio(sampleBuffer); }
            finally { sampleBuffer.Dispose(); }
        }
    }

    // A clip being written: the camera's own buffers go to the writer's video input from the camera's queue, the microphone's samples to its
    // audio input from theirs, under one lock. The writer's session starts at the first video frame's time; audio before it is dropped.
    private sealed class Recording(AVAssetWriter writer, AVAssetWriterInput video, AVAssetWriterInputPixelBufferAdaptor adaptor, AVAssetWriterInput audio) : IDisposable
    {
        private readonly object _lock = new();
        private bool _started, _stopped;
        public CMTime Start = CMTime.Invalid, Last = CMTime.Invalid;
        public int Frames, DroppedFrames;
        public string Error;

        public void AppendVideo(CVPixelBuffer buffer, CMTime time)
        {
            lock (_lock)
            {
                if (_stopped || Error != null) return;
                if (!_started)
                {
                    writer.StartSessionAtSourceTime(time);
                    Start = time;
                    _started = true;
                }
                if (!video.ReadyForMoreMediaData) { DroppedFrames++; return; }
                if (adaptor.AppendPixelBufferWithPresentationTime(buffer, time)) { Frames++; Last = time; }
                else Error = writer.Error?.LocalizedDescription ?? "a video frame couldn't be written";
            }
        }

        public void AppendAudio(CMSampleBuffer sample)
        {
            lock (_lock)
            {
                if (_stopped || !_started || audio == null || Error != null || !audio.ReadyForMoreMediaData) return;
                if (!audio.AppendSampleBuffer(sample)) Error = writer.Error?.LocalizedDescription ?? "the sound couldn't be written";
            }
        }

        // No more samples; then the inputs can be finished.
        public void Stop() { lock (_lock) _stopped = true; }

        public void Dispose()
        {
            adaptor.Dispose();
            video.Dispose();
            audio?.Dispose();
            writer.Dispose();
        }
    }

    // The built-in wide-angle camera facing the user, or away.
    private static AVCaptureDevice CaptureDevice(bool front) =>
        AVCaptureDevice.GetDefaultDevice(AVCaptureDeviceType.BuiltInWideAngleCamera, AVMediaTypes.Video,
            front ? AVCaptureDevicePosition.Front : AVCaptureDevicePosition.Back);

    // Runs until the camera is stopped; throws if the session can't be set up.
    private async Task RunAvFoundationAsync()
    {
        try
        {
            var device = CaptureDevice(Front) ?? throw new InvalidOperationException(Front ? "This device has no selfie camera." : "This device has no camera.");
            // The interface's orientation is read on the main thread.
            _orientation = await MainThread.InvokeOnMainThreadAsync(InterfaceOrientation);
            _session = new AVCaptureSession();
            _session.BeginConfiguration();
            try
            {
                if (_session.CanSetSessionPreset(AVCaptureSession.Preset1280x720)) _session.SessionPreset = AVCaptureSession.Preset1280x720;
                // The app's audio session (the microphone source, the voice recorder) is left alone.
                _session.AutomaticallyConfiguresApplicationAudioSession = false;
                _videoInput = AVCaptureDeviceInput.FromDevice(device, out NSError error);
                if (_videoInput == null || !_session.CanAddInput(_videoInput))
                    throw new InvalidOperationException("The camera couldn't be opened" + (error != null ? ": " + error.LocalizedDescription : "."));
                _session.AddInput(_videoInput);
                // A 4:3 format, so photos are the full sensor (4032x3024 on an iPad Air M4) rather than the 720p format's
                // 16:9 crop; the session's preset becomes "input priority". The frames follow (960x720 after the
                // MaxFrameSide scale) and so do the clips (the format's video size). Kept at 720p without one.
                FormatChosen = UseFourByThreeFormat(device);
                _videoOutput = new AVCaptureVideoDataOutput
                {
                    UncompressedVideoSetting = new AVVideoSettingsUncompressed { PixelFormatType = CVPixelFormatType.CV32BGRA },
                    AlwaysDiscardsLateVideoFrames = true
                };
                _queue = new DispatchQueue("emgu-camera");
                _frameDelegate = new FrameDelegate(this);
                _videoOutput.SetSampleBufferDelegate(_frameDelegate, _queue);
                if (!_session.CanAddOutput(_videoOutput)) throw new InvalidOperationException("The camera can't deliver frames to the app.");
                _session.AddOutput(_videoOutput);
                _videoConnection = _videoOutput.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant());
                if (_videoConnection != null)
                {
                    if (_videoConnection.SupportsVideoMirroring)
                    {
                        _videoConnection.AutomaticallyAdjustsVideoMirroring = false;
                        _videoConnection.VideoMirrored = false;
                    }
                    if (_videoConnection.SupportsVideoOrientation) _videoConnection.VideoOrientation = _orientation;
                }
                // Photos come from the same session, so the frames never pause for one (Camera2 has to rebuild its session).
                _photoOutput = new AVCapturePhotoOutput();
                if (_session.CanAddOutput(_photoOutput)) _session.AddOutput(_photoOutput);
                else { _photoOutput.Dispose(); _photoOutput = null; }
            }
            finally { _session.CommitConfiguration(); }
            if (_photoOutput != null) ConfigurePhotoOutput(device);
            _bgra = new Mat();
            _bgrFrame = new Mat();
            var center = NSNotificationCenter.DefaultCenter;
            _observers.Add(center.AddObserver(AVCaptureSession.RuntimeErrorNotification, OnRuntimeError, _session));
            _observers.Add(center.AddObserver(AVCaptureSession.WasInterruptedNotification, OnInterrupted, _session));
            _observers.Add(center.AddObserver(UIDevice.OrientationDidChangeNotification, _ => UpdateOrientation()));
            MainThread.BeginInvokeOnMainThread(() => UIDevice.CurrentDevice.BeginGeneratingDeviceOrientationNotifications());
            // Blocks until the camera runs: this is the camera's worker thread, not the UI's.
            _session.StartRunning();
            if (!_session.Running) throw new InvalidOperationException("The camera didn't start.");
            _avFoundation = true;
        }
        catch (Exception)
        {
            ReleaseAvFoundation();
            throw;
        }
        try { await Task.Delay(Timeout.Infinite, _stop.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            _avFoundation = false;
            lock (_gate) _snapshot?.TrySetException(new InvalidOperationException("The camera was closed before capture completed."));
            // Not on the camera's queue, which may be the one asking for the stop (a frame that failed).
            await Task.Run(ReleaseAvFoundation);
        }
    }

    private void ReleaseAvFoundation()
    {
        foreach (NSObject observer in _observers) NSNotificationCenter.DefaultCenter.RemoveObserver(observer);
        _observers.Clear();
        lock (_sessionLock)
        {
            try { if (_session != null && _session.Running) _session.StopRunning(); } catch (Exception) { }
        }
        try { _videoOutput?.SetSampleBufferDelegate(null, null); } catch (Exception) { }
        // A frame in flight on the queue finishes before the Mats go.
        try { _queue?.DispatchSync(() => { }); } catch (Exception) { }
        _videoConnection = null;
        _videoOutput?.Dispose(); _videoOutput = null;
        _photoOutput?.Dispose(); _photoOutput = null;
        _videoInput?.Dispose(); _videoInput = null;
        _session?.Dispose(); _session = null;
        _queue = null;
        _frameDelegate = null;
        _bgra?.Dispose(); _bgra = null;
        _bgrFrame?.Dispose(); _bgrFrame = null;
    }

    // On the camera's queue, once per frame: the pixel buffer as a Mat (no copy), scaled to the cap and converted to BGR into the reused Mats,
    // to the preview and the detectors, and to a snapshot request.
    private void OnSampleBuffer(CMSampleBuffer sampleBuffer)
    {
        if (_stop.IsCancellationRequested || _bgrFrame == null) return;
        using var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
        if (pixelBuffer == null) return;
        try
        {
            TaskCompletionSource<byte[]> request;
            lock (_gate) request = _snapshot;
            // A clip takes the camera's buffer as it is (the writer encodes it on its own); the detectors get the scaled copy below.
            Recording recording = _recording;
            if (recording != null) recording.AppendVideo(pixelBuffer, sampleBuffer.PresentationTimeStamp);
            pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
            try
            {
                int width = (int)pixelBuffer.Width, height = (int)pixelBuffer.Height;
                CaptureSize = new System.Drawing.Size(width, height);
                using var wrapped = new Mat(height, width, DepthType.Cv8U, 4, pixelBuffer.BaseAddress, (int)pixelBuffer.BytesPerRow);
                Mat source = wrapped;
                int longest = Math.Max(width, height);
                if (longest > MaxFrameSide)
                {
                    // Scaled while still 4 channels, then converted: less work than the other way round.
                    double scale = (double)MaxFrameSide / longest;
                    CvInvoke.Resize(wrapped, _bgra, new System.Drawing.Size((int)(width * scale), (int)(height * scale)), 0, 0, Inter.Area);
                    source = _bgra;
                }
                CvInvoke.CvtColor(source, _bgrFrame, ColorConversion.Bgra2Bgr);
            }
            finally { pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly); }
            Interlocked.Increment(ref _frameCount);
            FrameReady?.Invoke(_bgrFrame);
            _ready.TrySetResult(true);
            if (request != null)
                request.TrySetResult(CvInvoke.Imencode(".jpg", _bgrFrame, new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 85)));
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    // The interface's orientation as AVFoundation names it (the two enums match value for value). Main thread.
    private static AVCaptureVideoOrientation InterfaceOrientation()
    {
        UIInterfaceOrientation orientation = UIInterfaceOrientation.Portrait;
        UIWindowScene scene = null;
        foreach (UIScene candidate in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (candidate is not UIWindowScene window) continue;
            if (scene == null || window.ActivationState == UISceneActivationState.ForegroundActive) scene = window;
        }
        if (scene != null) orientation = scene.InterfaceOrientation;
        return orientation switch
        {
            UIInterfaceOrientation.LandscapeLeft => AVCaptureVideoOrientation.LandscapeLeft,
            UIInterfaceOrientation.LandscapeRight => AVCaptureVideoOrientation.LandscapeRight,
            UIInterfaceOrientation.PortraitUpsideDown => AVCaptureVideoOrientation.PortraitUpsideDown,
            _ => AVCaptureVideoOrientation.Portrait
        };
    }

    // The device turned (main thread): the connection follows the interface, so the next frames come upright.
    private void UpdateOrientation()
    {
        AVCaptureConnection connection = _videoConnection;
        // A clip keeps the orientation it started with: a change would change the buffers' size mid-file.
        if (connection == null || _stop.IsCancellationRequested || _orientationFrozen) return;
        AVCaptureVideoOrientation orientation = InterfaceOrientation();
        if (orientation == _orientation) return;
        _orientation = orientation;
        _queue?.DispatchAsync(() =>
        {
            try { if (connection.SupportsVideoOrientation) connection.VideoOrientation = orientation; }
            catch (Exception) { }
        });
    }

    private void OnRuntimeError(NSNotification notification)
    {
        var error = notification.UserInfo?[AVCaptureSession.ErrorKey] as NSError;
        Fail("The camera failed" + (error != null ? ": " + error.LocalizedDescription : "."));
    }

    // Interrupted by the system. The camera resumes by itself when the app comes back from the background, so that case is left alone (the
    // chat closes the camera when the app stops anyway); when another app has the camera or the device is too hot, the user is told.
    private void OnInterrupted(NSNotification notification)
    {
        if (notification.UserInfo?[AVCaptureSession.InterruptionReasonKey] is not NSNumber number) return;
        switch ((AVCaptureSessionInterruptionReason)number.Int32Value)
        {
            case AVCaptureSessionInterruptionReason.VideoDeviceNotAvailableWithMultipleForegroundApps:
                Fail("The camera isn't available while another app is beside this one."); break;
            case AVCaptureSessionInterruptionReason.VideoDeviceInUseByAnotherClient:
                Fail("Another app is using the camera."); break;
            case AVCaptureSessionInterruptionReason.VideoDeviceNotAvailableDueToSystemPressure:
                Fail("The camera stopped because the device is under pressure (too hot or short of memory)."); break;
        }
    }

    /// <summary>The 4:3 capture format chosen for the camera (e.g. "1920x1440, photos 4032x3024"), or null when it kept the 720p preset.</summary>
    public string FormatChosen { get; private set; }

    // The 4:3 format that takes the largest photos, with video of at least 1280x960 at 30 fps, the smallest such video (cheaper buffers and
    // clips), in 8-bit 4:2:0 (what the video output converts to BGRA in hardware). Null (the 720p preset stays) when there is none or iOS
    // is older than 16 (no SupportedMaxPhotoDimensions).
    private static string UseFourByThreeFormat(AVCaptureDevice device)
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(16)) return null;
        try
        {
            AVCaptureDeviceFormat best = null;
            long bestPhoto = 0, bestVideo = long.MaxValue;
            CMVideoDimensions bestSize = default, bestPhotoSize = default;
            foreach (AVCaptureDeviceFormat format in device.Formats)
            {
                if (format.FormatDescription is not CMVideoFormatDescription description) continue;
                uint subtype = description.MediaSubType;
                if (subtype != (uint)CVPixelFormatType.CV420YpCbCr8BiPlanarFullRange && subtype != (uint)CVPixelFormatType.CV420YpCbCr8BiPlanarVideoRange) continue;
                CMVideoDimensions size = description.Dimensions;
                if (size.Width * 3 != size.Height * 4 || size.Width < 1280) continue;
                if (format.VideoSupportedFrameRateRanges == null || !format.VideoSupportedFrameRateRanges.Any(range => range.MaxFrameRate >= 30)) continue;
                CMVideoDimensions[] photos = format.SupportedMaxPhotoDimensions;
                if (photos == null || photos.Length == 0) continue;
                CMVideoDimensions photo = photos.MaxBy(p => (long)p.Width * p.Height);
                long photoArea = (long)photo.Width * photo.Height, videoArea = (long)size.Width * size.Height;
                if (photoArea > bestPhoto || (photoArea == bestPhoto && videoArea < bestVideo))
                {
                    best = format; bestPhoto = photoArea; bestVideo = videoArea; bestSize = size; bestPhotoSize = photo;
                }
            }
            if (best == null || !device.LockForConfiguration(out NSError error) || error != null) return null;
            try
            {
                device.ActiveFormat = best;
                // 30 fps, as the 720p preset gave.
                var frame = new CMTime(1, 30);
                device.ActiveVideoMinFrameDuration = frame;
                device.ActiveVideoMaxFrameDuration = frame;
            }
            finally { device.UnlockForConfiguration(); }
            return bestSize.Width + "x" + bestSize.Height + ", photos " + bestPhotoSize.Width + "x" + bestPhotoSize.Height;
        }
        catch (Exception) { return null; }
    }

    // After the session is configured (the active format is the preset's, or the 4:3 one, by then): stills at the largest size that format offers - with the
    // 720p preset alone they would be 720p - at the balanced quality (the full quality can hold the video output up), and with the quicker
    // shutter where iOS 17 offers it.
    private void ConfigurePhotoOutput(AVCaptureDevice device)
    {
        try
        {
            if (OperatingSystem.IsIOSVersionAtLeast(16))
            {
                CMVideoDimensions[] sizes = device.ActiveFormat.SupportedMaxPhotoDimensions;
                if (sizes != null && sizes.Length > 0)
                    _photoOutput.MaxPhotoDimensions = sizes.MaxBy(size => (long)size.Width * size.Height);
            }
            else _photoOutput.IsHighResolutionCaptureEnabled = true;
            _photoOutput.MaxPhotoQualityPrioritization = AVCapturePhotoQualityPrioritization.Balanced;
            if (OperatingSystem.IsIOSVersionAtLeast(17) && _photoOutput.ResponsiveCaptureSupported) _photoOutput.ResponsiveCaptureEnabled = true;
        }
        catch (Exception) { }
    }

    /// <summary>The largest photo the camera takes (0 when there is no photo output, or before the camera runs).</summary>
    public System.Drawing.Size PhotoSize
    {
        get
        {
            AVCapturePhotoOutput output = _photoOutput;
            if (output == null) return default;
            if (!OperatingSystem.IsIOSVersionAtLeast(16)) return CaptureSize;
            CMVideoDimensions size = output.MaxPhotoDimensions;
            return new System.Drawing.Size(size.Width, size.Height);
        }
    }

    // The AVFoundation photo (TakePhotoAsync holds the gate and deletes the file on failure): one capture from the photo output, JPEG at
    // quality 0.95, at the output's largest size. The photo connection follows the interface's orientation, so the photo is upright through its
    // JPEG orientation tag with the sensor's pixels, as Android's; the selfie camera's isn't mirrored. Exposure and focus are AVFoundation's.
    private async Task<PhotoResult> TakePhotoAvFoundationAsync(string path, CancellationToken token)
    {
        AVCapturePhotoOutput output = _photoOutput ?? throw new InvalidOperationException("This camera can't take photos.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        var captured = new TaskCompletionSource<(byte[] Bytes, int Width, int Height)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var compression = new NSDictionary(AVVideo.QualityKey, NSNumber.FromDouble(0.95));
        using var format = new NSDictionary<NSString, NSObject>(
            new[] { AVVideo.CodecKey, AVVideo.CompressionPropertiesKey }, new NSObject[] { AVVideo.CodecJPEG, compression });
        using AVCapturePhotoSettings settings = AVCapturePhotoSettings.FromFormat(format);
        if (OperatingSystem.IsIOSVersionAtLeast(16)) settings.MaxPhotoDimensions = output.MaxPhotoDimensions;
        else settings.IsHighResolutionPhotoEnabled = output.IsHighResolutionCaptureEnabled;
        settings.PhotoQualityPrioritization = AVCapturePhotoQualityPrioritization.Balanced;
        AVCaptureConnection connection = output.ConnectionFromMediaType(AVMediaTypes.Video.GetConstant());
        if (connection != null)
        {
            if (connection.SupportsVideoMirroring)
            {
                connection.AutomaticallyAdjustsVideoMirroring = false;
                connection.VideoMirrored = false;
            }
            if (connection.SupportsVideoOrientation) connection.VideoOrientation = _orientation;
        }
        var photoDelegate = new PhotoDelegate(captured);
        _photoDelegate = photoDelegate;
        try
        {
            output.CapturePhoto(settings, photoDelegate);
            (byte[] bytes, int width, int height) = await captured.Task.WaitAsync(TimeSpan.FromSeconds(8), linked.Token);
            await File.WriteAllBytesAsync(path, bytes, token);
            return new PhotoResult { Path = path, Width = width, Height = height, Bytes = bytes.Length };
        }
        finally { _photoDelegate = null; }
    }

    // The app's audio session must allow recording before the microphone joins the capture session (the session is told not to configure it,
    // so that the microphone source and the voice recorder keep theirs). The microphone source sets the same category, so nothing changes
    // under a sound monitor.
    private static void PrepareAudioSessionForRecording()
    {
        AVAudioSession session = AVAudioSession.SharedInstance();
        string category = session.Category;
        if (category != AVAudioSession.CategoryPlayAndRecord && category != AVAudioSession.CategoryRecord)
        {
            NSError error = session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker);
            if (error != null) throw new InvalidOperationException("The microphone can't be used for the clip: " + error.LocalizedDescription);
        }
        session.SetActive(true, out NSError activation);
        if (activation != null) throw new InvalidOperationException("The microphone can't be used for the clip: " + activation.LocalizedDescription);
    }

    // The AVFoundation clip (RecordClipAsync holds the gate and deletes the file unless the clip finished): an AVAssetWriter fed the camera's own
    // buffers (the 4:3 format's, e.g. 1920x1440, or 1280x720) from the frame callback (H.264 at 6 Mbps, the frames to the preview and the detectors go on), and the microphone's
    // samples (AAC) through an audio input and output in a capture session of their own for the clip: added to the camera's session they
    // made the camera restart, and a clip with sound began black and faded in over half a second (an iPad, 2026-11-05). Both sessions
    // time their samples on the host clock, so the sound lines up with the picture. The buffers are upright already, so the file needs no
    // transform; the orientation is frozen while the clip records. The camera stopping ends the clip early and keeps it; cancelling discards it.
    private async Task<ClipResult> RecordClipAvFoundationAsync(int seconds, string path, bool withAudio, CancellationToken token)
    {
        AVCaptureSession session = _session ?? throw new InvalidOperationException("The camera isn't running.");
        System.Drawing.Size size = CaptureSize;
        if (size.Width == 0) throw new InvalidOperationException("The camera hasn't delivered a frame yet.");
        _orientationFrozen = true;
        AVCaptureDeviceInput audioInput = null;
        AVCaptureAudioDataOutput audioOutput = null;
        AudioDelegate audioDelegate = null;
        DispatchQueue audioQueue = null;
        AVCaptureSession audioSession = null;
        Recording recording = null;
        try
        {
            if (withAudio)
            {
                PrepareAudioSessionForRecording();
                AVCaptureDevice microphone = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Audio) ?? throw new InvalidOperationException("This device has no microphone.");
                audioInput = AVCaptureDeviceInput.FromDevice(microphone, out NSError error);
                if (audioInput == null) throw new InvalidOperationException("The microphone couldn't be opened" + (error != null ? ": " + error.LocalizedDescription : "."));
                audioOutput = new AVCaptureAudioDataOutput();
                audioQueue = new DispatchQueue("emgu-camera-audio");
            }
            var writer = new AVAssetWriter(NSUrl.FromFilename(path), AVFileTypes.Mpeg4.GetConstant(), out NSError writerError);
            if (writerError != null) throw new InvalidOperationException("The clip file couldn't be created: " + writerError.LocalizedDescription);
            writer.ShouldOptimizeForNetworkUse = true;
            var video = AVAssetWriterInput.Create(AVMediaTypes.Video.GetConstant(), new AVVideoSettingsCompressed
            {
                CodecType = AVVideoCodecType.H264,
                Width = size.Width,
                Height = size.Height,
                CodecSettings = new AVVideoCodecSettings { AverageBitRate = 6_000_000, MaxKeyFrameInterval = 30, ProfileLevelH264 = AVVideoProfileLevelH264.HighAutoLevel }
            });
            video.ExpectsMediaDataInRealTime = true;
            var adaptor = AVAssetWriterInputPixelBufferAdaptor.Create(video,
                new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA, Width = size.Width, Height = size.Height });
            if (!writer.CanAddInput(video)) throw new InvalidOperationException("The clip's video can't be written at " + size.Width + "x" + size.Height + ".");
            writer.AddInput(video);
            AVAssetWriterInput audio = null;
            if (withAudio)
            {
                audio = AVAssetWriterInput.Create(AVMediaTypes.Audio.GetConstant(),
                    new AudioSettings { Format = AudioToolbox.AudioFormatType.MPEG4AAC, SampleRate = 44100, NumberChannels = 1, EncoderBitRate = 96000 });
                audio.ExpectsMediaDataInRealTime = true;
                if (writer.CanAddInput(audio)) writer.AddInput(audio);
                else { audio.Dispose(); audio = null; }
            }
            recording = new Recording(writer, video, adaptor, audio);
            if (audioInput != null)
            {
                audioDelegate = new AudioDelegate(recording);
                audioOutput.SetSampleBufferDelegate(audioDelegate, audioQueue);
                // The app's audio session is set up above (PrepareAudioSessionForRecording).
                audioSession = new AVCaptureSession { AutomaticallyConfiguresApplicationAudioSession = false };
                if (!audioSession.CanAddInput(audioInput)) throw new InvalidOperationException("The microphone can't be recorded.");
                audioSession.AddInput(audioInput);
                if (!audioSession.CanAddOutput(audioOutput)) throw new InvalidOperationException("The microphone's sound can't be delivered.");
                audioSession.AddOutput(audioOutput);
                AVCaptureSession starting = audioSession;
                await Task.Run(starting.StartRunning);
            }
            if (!writer.StartWriting()) throw new InvalidOperationException("The clip couldn't be started" + (writer.Error != null ? ": " + writer.Error.LocalizedDescription : "."));
            _recording = recording;
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token))
            {
                try { await Task.Delay(TimeSpan.FromSeconds(seconds), linked.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            }
            _recording = null;
            // The frame or sample on each queue finishes before the inputs close.
            try { _queue?.DispatchSync(() => { }); } catch (Exception) { }
            try { audioQueue?.DispatchSync(() => { }); } catch (Exception) { }
            recording.Stop();
            if (token.IsCancellationRequested)
            {
                writer.CancelWriting();
                token.ThrowIfCancellationRequested();
            }
            if (recording.Error != null) throw new InvalidOperationException("The clip couldn't be written: " + recording.Error);
            if (recording.Frames == 0) throw new InvalidOperationException("The clip got no frames.");
            video.MarkAsFinished();
            audio?.MarkAsFinished();
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            writer.FinishWriting(() => finished.TrySetResult(true));
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            if (writer.Status != AVAssetWriterStatus.Completed)
                throw new InvalidOperationException("The clip couldn't be finished" + (writer.Error != null ? ": " + writer.Error.LocalizedDescription : "."));
            // Up to the last frame, plus that frame's own time.
            double duration = (recording.Last - recording.Start).Seconds + 1.0 / 30;
            return new ClipResult { Path = path, Duration = TimeSpan.FromSeconds(duration), WithAudio = audio != null, Bytes = new FileInfo(path).Length };
        }
        finally
        {
            _recording = null;
            if (audioInput != null || audioOutput != null)
            {
                // The microphone's own session: the camera's goes on untouched.
                if (audioSession != null)
                {
                    try { audioSession.StopRunning(); } catch (Exception) { }
                    try
                    {
                        if (audioOutput != null) audioSession.RemoveOutput(audioOutput);
                        if (audioInput != null) audioSession.RemoveInput(audioInput);
                    }
                    catch (Exception) { }
                    audioSession.Dispose();
                }
                try { audioOutput?.SetSampleBufferDelegate(null, null); } catch (Exception) { }
                try { audioQueue?.DispatchSync(() => { }); } catch (Exception) { }
                audioOutput?.Dispose();
                audioInput?.Dispose();
            }
            recording?.Dispose();
            _orientationFrozen = false;
            MainThread.BeginInvokeOnMainThread(UpdateOrientation);
        }
    }
}
#endif
