// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.Models.CV.Maui;

namespace Maui.Demo.Lite;

// The record_clip alert action (TODO item 43): a few seconds of video from the live camera (Camera2, LiveCamera.Camera2.cs), with sound when the
// microphone is allowed, shown in the transcript with the first frame, Play (in the card, InlineVideoView) / Full screen / Share / Save to gallery. Session only, like the snapshots: the clips
// live in the app's cache folder, the latest five are kept, New chat deletes them, and nothing is sent to the model.
public partial class LiteRtLmChatPage
{
    private readonly Queue<(View Row, string Path, Action Stop)> _visualClips = new();
    private Action _stopPlayingClip;   // the one clip that plays in the chat now
    private const int MaximumVisualClips = 5;

    private Task RecordVisualClipAsync(VisualEvent occurrence, int seconds, CancellationToken token) =>
        RecordClipAsync(() => _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition, seconds, token);

    // What a clip's card says set it off: a camera monitor's condition, or the sound that was heard (read when the card is made).
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
            if (token.IsCancellationRequested || !(_pageVisible || BackgroundCapture))
            {
                try { File.Delete(clip.Path); } catch (Exception) { }
                token.ThrowIfCancellationRequested();
                return;
            }
            string condition = cause();
            var image = new Image { Aspect = Aspect.AspectFit };
            if (thumbnail != null) image.Source = ImageUtil.ToImageSource(thumbnail);
            SemanticProperties.SetDescription(image, "Camera clip. Tap to play.");
            var host = new Grid { HeightRequest = 150, WidthRequest = 210, HorizontalOptions = LayoutOptions.Start, BackgroundColor = Theme.ImageBackground, Children = { image } };
            string size = (clip.Bytes / 1048576.0).ToString("0.0") + " MB";
            var caption = new Label
            {
                Text = "Camera clip · " + at.ToString("HH:mm:ss") + " · " + Math.Round(clip.Duration.TotalSeconds) + " s · " + size +
                    (clip.WithAudio ? " · with sound" : " · no sound (microphone not allowed)") + "\n" + condition + "\nSession only",
                FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText
            };
            var status = new Label { FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText, IsVisible = false };
            var progress = new ProgressBar { WidthRequest = 150, VerticalOptions = LayoutOptions.Center, ProgressColor = Theme.Accent };
            var time = new Label { FontSize = 11, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center };
            var position = new HorizontalStackLayout { Spacing = 8, IsVisible = false, Children = { progress, time } };
            var play = MonitorButton("Play");
            var fullScreen = MonitorButton("Full screen");
            var share = MonitorButton("Share");
            var save = MonitorButton("Save to gallery");

            // Playing in the card: the video replaces the thumbnail; the end, an error, another clip or New chat put the thumbnail back.
            InlineVideoView video = null;
            IDispatcherTimer timer = null;
            bool muted = false;
            void StopPlayback()
            {
                if (_stopPlayingClip == StopPlayback) _stopPlayingClip = null;
                timer?.Stop(); timer = null;
                if (video != null) { try { video.Stop(); } catch (Exception) { } host.Children.Remove(video); video = null; }
                image.IsVisible = true;
                host.HeightRequest = 150;
                position.IsVisible = false;
                play.Text = "Play";
                if (muted) { muted = false; EndSoundMute(); }
            }
            void StartPlayback()
            {
                _stopPlayingClip?.Invoke();
                _stopPlayingClip = StopPlayback;
                video = new InlineVideoView { Source = clip.Path };
                video.Prepared += (duration, width, height) =>
                {
                    // A portrait clip needs a taller box than the thumbnail's.
                    host.HeightRequest = height > width ? 300 : 150;
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
                timer = Dispatcher.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(250);
                timer.Tick += (_, _) =>
                {
                    if (video == null) return;
                    int length = (int)Math.Round(clip.Duration.TotalMilliseconds);
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
                await PlayVisualClipAsync(clip.Path);
            };
            share.Clicked += async (_, _) =>
            {
                try { await Share.Default.RequestAsync(new ShareFileRequest("Camera clip", new ShareFile(clip.Path, "video/mp4"))); }
                catch (Exception ex) { await DisplayAlertAsync("Couldn't share the clip", ex.Message, "OK"); }
            };
            save.Clicked += async (_, _) =>
            {
                save.IsEnabled = false;
                try
                {
                    status.Text = await PictureGallery.SaveVideoAsync(clip.Path, "camera-clip-" + at.ToString("yyyyMMdd-HHmmss") + ".mp4");
                    status.IsVisible = true;
                }
                catch (Exception ex) { await DisplayAlertAsync("Couldn't save the clip", ex.Message, "OK"); save.IsEnabled = true; }
            };
            var buttons = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, MaximumWidthRequest = 300, HorizontalOptions = LayoutOptions.Start };
            foreach (Button button in new[] { play, fullScreen, share, save }) { button.Margin = new Thickness(0, 0, 6, 4); buttons.Children.Add(button); }
            var row = new VerticalStackLayout
            {
                Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
                Children = { host, position, caption, buttons, status }
            };
            AddCard(_agentBubble, row);
            _visualClips.Enqueue((row, clip.Path, StopPlayback));
            while (_visualClips.Count > MaximumVisualClips)
            {
                var old = _visualClips.Dequeue();
                old.Stop();
                RemoveCard(old.Row);
                try { File.Delete(old.Path); } catch (Exception) { }
            }
        });
    }

    private static string FormatClipTime(int milliseconds) => (milliseconds / 60000) + ":" + (milliseconds / 1000 % 60).ToString("00");

    private static byte[] ClipThumbnail(string path)
    {
        try
        {
            using var retriever = new Android.Media.MediaMetadataRetriever();
            retriever.SetDataSource(path);
            using Android.Graphics.Bitmap frame = retriever.GetFrameAtTime(500_000, Android.Media.Option.ClosestSync) ?? retriever.GetFrameAtTime(0);
            if (frame == null) return null;
            using var stream = new MemoryStream();
            frame.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 80, stream);
            return stream.ToArray();
        }
        catch (Exception) { return null; }
    }

    private async Task PlayVisualClipAsync(string path)
    {
        try { await Launcher.Default.OpenAsync(new OpenFileRequest("Camera clip", new ReadOnlyFile(path, "video/mp4"))); }
        catch (Exception ex) { await DisplayAlertAsync("Couldn't play the clip", ex.Message, "OK"); }
    }

    private void ClearVisualClips()
    {
        while (_visualClips.Count > 0)
        {
            var clip = _visualClips.Dequeue();
            clip.Stop();
            RemoveCard(clip.Row);
            try { File.Delete(clip.Path); } catch (Exception) { }
        }
    }
}
#endif
