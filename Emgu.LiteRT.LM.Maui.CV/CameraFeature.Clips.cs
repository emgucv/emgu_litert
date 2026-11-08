// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || (IOS && !MACCATALYST)
using System.IO;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.Models.CV.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// The record_clip alert action (TODO item 43): a few seconds of video from the live camera (Camera2 or AVFoundation, LiveCamera.RecordClipAsync), with sound when the
// microphone is allowed: a thumbnail in the camera panel's strip and then in the chat's row for that opening; its viewer plays it (InlineVideoView) with
// Full screen / Share / Save to gallery / Keep in chat. Session only unless kept, like the snapshots: the clips live in the app's cache folder, the latest
// five are kept (CameraCaptures), New chat deletes them, and nothing is sent to the model.
public sealed partial class CameraFeature
{
    private Action _stopPlayingClip;   // the one clip that plays now (in its viewer)

    private Task RecordVisualClipAsync(VisualEvent occurrence, int seconds, CancellationToken token) =>
        RecordClipAsync(() => _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition, seconds, token);

    // What a clip's viewer says set it off: a camera monitor's condition, or the sound that was heard (read when the card is made).
    private async Task RecordClipAsync(Func<string> cause, int seconds, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!CameraUsable || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
                throw new InvalidOperationException("A clip needs the visible live camera.");
            return _camera;
        });
        // Sound only if the microphone is already allowed: an alert shouldn't ask for permissions.
        bool withAudio = await MainThread.InvokeOnMainThreadAsync(async () => await Permissions.CheckStatusAsync<Permissions.Microphone>() == PermissionStatus.Granted);
        string path = System.IO.Path.Combine(FileSystem.CacheDirectory, "clips", "clip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".mp4");
        object activity = await MainThread.InvokeOnMainThreadAsync(() => BeginCameraActivity("Recording clip", TimeSpan.FromSeconds(seconds)));
        LiveCamera.ClipResult clip;
        try { clip = await camera.RecordClipAsync(seconds, path, withAudio, token); }
        finally { await MainThread.InvokeOnMainThreadAsync(() => EndCameraActivity(activity)); }
        byte[] thumbnail = ClipThumbnail(clip.Path);
        DateTimeOffset at = DateTimeOffset.Now;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (token.IsCancellationRequested || !(PageVisible || BackgroundCapture))
            {
                try { File.Delete(clip.Path); } catch (Exception) { }
                token.ThrowIfCancellationRequested();
                return;
            }
            AddCapture(new CameraCapture(CameraCaptureKind.Clip, at, cause(), path: clip.Path, thumbnail: SmallJpeg(thumbnail))
            {
                Duration = clip.Duration, WithSound = clip.WithAudio
            }, thumbnail);
        });
    }

    // A clip's viewer: the first frame that plays in place (tap or Play), its time, length, size and cause, Full screen, Share, Save to
    // gallery and Keep in chat; stop stops it playing (the sheet closed). A restored one plays from its copy in the cache.
    private View ClipViewer(CameraCapture capture, out Action stop)
    {
        string path = capture.Path;
        DateTimeOffset at = capture.TakenAt;
        double boxHeight = CaptureViewerHeight;
        var image = new Image { Aspect = Aspect.AspectFit };
        byte[] thumbnail = (capture.Tag as CaptureViews)?.Preview;
        if (thumbnail != null) image.Source = CvViews.ToImageSource(thumbnail);
        SemanticProperties.SetDescription(image, "Camera clip. Tap to play.");
        var host = new Grid { HeightRequest = boxHeight, BackgroundColor = CvViews.Color("ChatRowBorder"), Children = { image } };
        long bytes = 0;
        try { bytes = new FileInfo(path).Length; } catch (Exception) { }
        string size = (bytes / 1048576.0).ToString("0.0") + " MB";
        Label caption = CaptureCaption(capture, "Camera clip · " + at.ToString("HH:mm:ss") + " · " + Math.Round(capture.Duration.TotalSeconds) + " s · " + size +
            (capture.WithSound ? " · with sound" : " · no sound (microphone not allowed)"));
        var status = new Label { FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText"), IsVisible = false };
        var progress = new ProgressBar { WidthRequest = 200, VerticalOptions = LayoutOptions.Center, ProgressColor = ChatResources.FindColor("ChatAccent") };
        var time = new Label { FontSize = 11, TextColor = CvViews.Color("ChatSecondaryText"), VerticalOptions = LayoutOptions.Center };
        var position = new HorizontalStackLayout { Spacing = 8, IsVisible = false, Children = { progress, time } };
        var play = MonitorButton("Play");
        var fullScreen = MonitorButton("Full screen");
        var share = MonitorButton("Share");
        var save = MonitorButton("Save to gallery");

        // Playing in the viewer: the video replaces the first frame; the end, an error, another clip, closing or New chat put it back.
        InlineVideoView video = null;
        IDispatcherTimer timer = null;
        bool muted = false;
        void StopPlayback()
        {
            if (_stopPlayingClip == StopPlayback) _stopPlayingClip = null;
            timer?.Stop(); timer = null;
            if (video != null) { try { video.Stop(); } catch (Exception) { } host.Children.Remove(video); video = null; }
            image.IsVisible = true;
            position.IsVisible = false;
            play.Text = "Play";
            if (muted) { muted = false; EndSoundMute(); }
        }
        void StartPlayback()
        {
            _stopPlayingClip?.Invoke();
            _stopPlayingClip = StopPlayback;
            video = new InlineVideoView { Source = path };
            video.Prepared += (duration, width, height) =>
            {
                time.Text = FormatClipTime(0) + " / " + FormatClipTime(duration);
                position.IsVisible = true;
                video.Play();
            };
            video.Completed += StopPlayback;
            video.Failed += message => { StopPlayback(); status.Text = message + " Try Full screen."; status.IsVisible = true; };
            host.Children.Add(video);
            image.IsVisible = false;
            play.Text = "Pause";
            // A sound monitor would hear the clip: it doesn't listen while one plays.
            muted = true; BeginSoundMute();
            timer = Host.Page.Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (_, _) =>
            {
                if (video == null) return;
                int length = (int)Math.Round(capture.Duration.TotalMilliseconds);
                int at = video.PositionMs;
                progress.Progress = Math.Clamp(at / Math.Max(1.0, length), 0, 1);
                time.Text = FormatClipTime(at) + " / " + FormatClipTime(length);
            };
            timer.Start();
        }
        void Toggle()
        {
            status.IsVisible = false;
            if (video == null) StartPlayback();
            else if (video.IsPlaying) { video.Pause(); play.Text = "Play"; }
            else { video.Play(); play.Text = "Pause"; }
        }
        image.OnTap(Toggle);
        play.Clicked += (_, _) => Toggle();
        fullScreen.Clicked += async (_, _) =>
        {
            StopPlayback();
            await PlayVisualClipAsync(path);
        };
        share.Clicked += async (_, _) =>
        {
            try { await Share.Default.RequestAsync(new ShareFileRequest("Camera clip", new ShareFile(path, "video/mp4"))); }
            catch (Exception ex) { await Host.AskAsync("Couldn't share the clip", ex.Message, "OK"); }
        };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                status.Text = await PictureGallery.SaveVideoAsync(path, "camera-clip-" + at.ToString("yyyyMMdd-HHmmss") + ".mp4");
                status.IsVisible = true;
            }
            catch (Exception ex) { await Host.AskAsync("Couldn't save the clip", ex.Message, "OK"); save.IsEnabled = true; }
        };
        stop = StopPlayback;
        return new VerticalStackLayout
        {
            Spacing = 8, Children = { host, position, caption, CaptureButtons(play, fullScreen, share, save, KeepInChatButton(capture, caption)), status }
        };
    }

    private static string FormatClipTime(int milliseconds) => (milliseconds / 60000) + ":" + (milliseconds / 1000 % 60).ToString("00");

    private static byte[] ClipThumbnail(string path)
    {
        try
        {
#if IOS
            using var asset = AVFoundation.AVAsset.FromUrl(Foundation.NSUrl.FromFilename(path));
            // The frame at one second exactly. A clip's first frame is black, and without a zero tolerance the nearest
            // keyframe to 0.5 s was taken - the first one when the next came after 1 s (black thumbnails on an iPad).
            using var generator = new AVFoundation.AVAssetImageGenerator(asset)
            {
                AppliesPreferredTrackTransform = true,
                MaximumSize = new CoreGraphics.CGSize(640, 640),
                RequestedTimeToleranceBefore = CoreMedia.CMTime.Zero,
                RequestedTimeToleranceAfter = CoreMedia.CMTime.Zero
            };
            using var frame = generator.CopyCGImageAtTime(CoreMedia.CMTime.FromSeconds(1.0, 600), out _, out Foundation.NSError error)
                ?? generator.CopyCGImageAtTime(CoreMedia.CMTime.Zero, out _, out error);
            if (frame == null) return null;
            using var image = UIKit.UIImage.FromImage(frame);
            using Foundation.NSData jpeg = image.AsJPEG(0.8f);
            return jpeg?.ToArray();
#else
            using var retriever = new Android.Media.MediaMetadataRetriever();
            retriever.SetDataSource(path);
            using Android.Graphics.Bitmap frame = retriever.GetFrameAtTime(500_000, Android.Media.Option.ClosestSync) ?? retriever.GetFrameAtTime(0);
            if (frame == null) return null;
            using var stream = new MemoryStream();
            frame.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 80, stream);
            return stream.ToArray();
#endif
        }
        catch (Exception) { return null; }
    }

    private async Task PlayVisualClipAsync(string path)
    {
        try { await Launcher.Default.OpenAsync(new OpenFileRequest("Camera clip", new ReadOnlyFile(path, "video/mp4"))); }
        catch (Exception ex) { await Host.AskAsync("Couldn't play the clip", ex.Message, "OK"); }
    }

}
#endif
