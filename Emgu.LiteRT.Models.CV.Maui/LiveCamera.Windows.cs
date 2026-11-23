//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS
using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Diagnostics;

namespace Emgu.LiteRT.Models.CV.Maui;

// Photos and clips on Windows, where the camera is OpenCV's DirectShow capture: the worker that reads the frames (LiveCamera.RunAsync) does
// both, so nothing else touches the capture. TakePhotoAsync and RecordClipAsync (LiveCamera.cs) hold the record gate, so at most one runs.
//
// The photo: the capture is asked for 9999x9999, which DirectShow answers with the camera's largest size for its format (1280x720 for a
// Logitech StreamCam), a few frames are read while the exposure settles, the last is written as a JPEG and the preview size comes back.
// Each switch took ~0.9 s there, so the preview and the detectors pause for 2-3 s.
//
// The clip: the preview frames as they are (640x480), into an H.264 MP4 through Media Foundation's encoder (OpenCV's MSMF writer; FFmpeg's
// H.264 needs Cisco's OpenH264 DLL, which isn't shipped). No sound: OpenCV's writer has no audio track. The camera's own rate varies with the
// light (8-20 fps measured), so each frame is written as many times as the 30 fps slots it stands for, and the clip plays at the speed it was
// recorded. 90 frames at 1280x720 encoded in 83 ms, so the writing doesn't slow the preview.
public sealed partial class LiveCamera
{
    private const int WindowsClipFps = 30;

    private sealed class WindowsPhoto
    {
        public string Path { get; init; }
        public TaskCompletionSource<PhotoResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class WindowsClip
    {
        public string Path { get; init; }
        public int Seconds { get; init; }
        public volatile bool Cancelled;
        public VideoWriter Writer;
        public long Started;
        public int Written;
        public TaskCompletionSource<ClipResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private WindowsPhoto _windowsPhoto;   // asked for, not yet taken by the worker
    private WindowsClip _windowsClip;     // being recorded

    private async Task<PhotoResult> TakePhotoWindowsAsync(string path, CancellationToken token)
    {
        var request = new WindowsPhoto { Path = path };
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) throw new InvalidOperationException("The camera is closed.");
            _windowsPhoto = request;
        }
        // Cancelled before the worker took it: it never starts. Once taken, it finishes (2-3 s) before the file can be deleted.
        using (token.Register(() => { lock (_gate) { if (_windowsPhoto == request) { _windowsPhoto = null; request.Done.TrySetCanceled(token); } } }))
        {
            PhotoResult result = await request.Done.Task.WaitAsync(TimeSpan.FromSeconds(30));
            token.ThrowIfCancellationRequested();
            return result;
        }
    }

    private async Task<ClipResult> RecordClipWindowsAsync(int seconds, string path, CancellationToken token)
    {
        var clip = new WindowsClip { Path = path, Seconds = seconds };
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) throw new InvalidOperationException("The camera is closed.");
            _windowsClip = clip;
        }
        // Cancelling is a flag the worker reads at the next frame: it closes the writer first, so the file can then be deleted.
        using (token.Register(() => clip.Cancelled = true))
        {
            ClipResult result = await clip.Done.Task.WaitAsync(TimeSpan.FromSeconds(seconds + 15));
            token.ThrowIfCancellationRequested();
            return result;
        }
    }

    // On the worker, before it reads the next frame.
    private void TakeWindowsPhotoIfAsked(VideoCapture capture)
    {
        WindowsPhoto request;
        lock (_gate) { request = _windowsPhoto; _windowsPhoto = null; }
        if (request == null) return;
        double width = capture.Get(CapProp.FrameWidth), height = capture.Get(CapProp.FrameHeight);
        try
        {
            capture.Set(CapProp.FrameWidth, 9999);
            capture.Set(CapProp.FrameHeight, 9999);
            using var photo = new Mat();
            // The first frames after the switch can be dark or blurred while the exposure and the focus settle.
            for (int i = 0; i < 5; i++)
            {
                if (!capture.Read(photo) || photo.IsEmpty) throw new InvalidOperationException("The camera delivered no frame for the photo.");
            }
            if (!CvInvoke.Imwrite(request.Path, photo, new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 95)))
                throw new IOException("The photo couldn't be written.");
            request.Done.TrySetResult(new PhotoResult
            {
                Path = request.Path, Width = photo.Width, Height = photo.Height, Bytes = new FileInfo(request.Path).Length
            });
        }
        catch (Exception ex) { request.Done.TrySetException(ex); }
        finally
        {
            capture.Set(CapProp.FrameWidth, width);
            capture.Set(CapProp.FrameHeight, height);
        }
    }

    // On the worker, with each frame as the camera delivered it.
    private void WriteWindowsClipFrame(Mat frame)
    {
        WindowsClip clip;
        lock (_gate) clip = _windowsClip;
        if (clip == null) return;
        if (clip.Cancelled) { EndWindowsClip(clip, null, cancelled: true); return; }
        try
        {
            if (clip.Writer == null)
            {
                clip.Writer = new VideoWriter(clip.Path, (int)VideoCapture.API.Msmf, VideoWriter.Fourcc('H', '2', '6', '4'), WindowsClipFps,
                    new System.Drawing.Size(frame.Width, frame.Height), true);
                if (!clip.Writer.IsOpened) throw new InvalidOperationException("Windows' H.264 video encoder couldn't be opened.");
                clip.Started = Stopwatch.GetTimestamp();
            }
            int total = clip.Seconds * WindowsClipFps;
            int due = Math.Min(total, (int)(Stopwatch.GetElapsedTime(clip.Started).TotalSeconds * WindowsClipFps) + 1);
            while (clip.Written < due) { clip.Writer.Write(frame); clip.Written++; }
            if (clip.Written >= total) EndWindowsClip(clip, null, cancelled: false);
        }
        catch (Exception ex) { EndWindowsClip(clip, ex, cancelled: false); }
    }

    private void EndWindowsClip(WindowsClip clip, Exception error, bool cancelled)
    {
        lock (_gate) { if (_windowsClip == clip) _windowsClip = null; }
        try { clip.Writer?.Dispose(); } catch (Exception) { }
        clip.Writer = null;
        if (cancelled) clip.Done.TrySetCanceled();
        else if (error != null) clip.Done.TrySetException(error);
        else clip.Done.TrySetResult(new ClipResult
        {
            Path = clip.Path, Duration = TimeSpan.FromSeconds((double)clip.Written / WindowsClipFps), WithAudio = false,
            Bytes = new FileInfo(clip.Path).Length
        });
    }

    // On the worker as it ends: a clip being recorded keeps what it has (the camera closing ends it early), a photo not taken fails.
    private void EndWindowsCaptures()
    {
        WindowsPhoto photo;
        WindowsClip clip;
        lock (_gate) { photo = _windowsPhoto; _windowsPhoto = null; clip = _windowsClip; }
        photo?.Done.TrySetException(new InvalidOperationException("The camera was closed before the photo was taken."));
        if (clip != null)
        {
            if (clip.Cancelled) EndWindowsClip(clip, null, cancelled: true);
            else if (clip.Written > 0) EndWindowsClip(clip, null, cancelled: false);
            else EndWindowsClip(clip, new InvalidOperationException("The camera was closed before the clip started."), cancelled: false);
        }
    }
}
#endif
