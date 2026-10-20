// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using Emgu.LiteRT.LM.Agents.Events;

namespace Maui.Demo.Lite;

// The record_clip alert action (TODO item 43): a few seconds of video from the live camera (Camera2, LiveCamera.Camera2.cs), with sound when the
// microphone is allowed, shown in the transcript with the first frame, Play, Share and Save to gallery. Session only, like the snapshots: the clips
// live in the app's cache folder, the latest five are kept, New chat deletes them, and nothing is sent to the model.
public partial class LiteRtLmChatPage
{
    private readonly Queue<(View Row, string Path)> _visualClips = new();
    private const int MaximumVisualClips = 5;

    private async Task RecordVisualClipAsync(VisualEvent occurrence, int seconds, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!_pageVisible || _cameraSuspended || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
                throw new InvalidOperationException("A clip needs the visible live camera.");
            return _camera;
        });
        // Sound only if the microphone is already allowed: an alert shouldn't ask for permissions.
        bool withAudio = await MainThread.InvokeOnMainThreadAsync(async () => await Permissions.CheckStatusAsync<Permissions.Microphone>() == PermissionStatus.Granted);
        string path = System.IO.Path.Combine(FileSystem.CacheDirectory, "clips", "clip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".mp4");
        LiveCamera.ClipResult clip = await camera.RecordClipAsync(seconds, path, withAudio, token);
        byte[] thumbnail = ClipThumbnail(clip.Path);
        DateTimeOffset at = DateTimeOffset.Now;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (token.IsCancellationRequested || !_pageVisible)
            {
                try { File.Delete(clip.Path); } catch (Exception) { }
                token.ThrowIfCancellationRequested();
                return;
            }
            string condition = _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition;
            var image = new Image { HeightRequest = 150, WidthRequest = 210, Aspect = Aspect.AspectFit, BackgroundColor = Theme.ImageBackground };
            if (thumbnail != null) image.Source = ImageUtil.ToImageSource(thumbnail);
            image.OnTap(() => _ = PlayVisualClipAsync(clip.Path));
            SemanticProperties.SetDescription(image, "Camera clip. Tap to play.");
            string size = (clip.Bytes / 1048576.0).ToString("0.0") + " MB";
            var caption = new Label
            {
                Text = "Camera clip · " + at.ToString("HH:mm:ss") + " · " + Math.Round(clip.Duration.TotalSeconds) + " s · " + size +
                    (clip.WithAudio ? " · with sound" : " · no sound (microphone not allowed)") + "\n" + condition + "\nSession only",
                FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText
            };
            var status = new Label { FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText, IsVisible = false };
            var play = MonitorButton("Play");
            var share = MonitorButton("Share");
            var save = MonitorButton("Save to gallery");
            play.Clicked += async (_, _) => await PlayVisualClipAsync(clip.Path);
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
            var row = new VerticalStackLayout
            {
                Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
                Children = { image, caption, new HorizontalStackLayout { Spacing = 6, Children = { play, share, save } }, status }
            };
            if (_transcript.Children.Contains(_emptyLabel)) _transcript.Children.Remove(_emptyLabel);
            int index = _agentBubble != null ? _transcript.Children.IndexOf(_agentBubble.Row) : -1;
            if (index >= 0) _transcript.Children.Insert(index, row); else _transcript.Children.Add(row);
            _visualClips.Enqueue((row, clip.Path));
            while (_visualClips.Count > MaximumVisualClips)
            {
                var old = _visualClips.Dequeue();
                _transcript.Children.Remove(old.Row);
                try { File.Delete(old.Path); } catch (Exception) { }
            }
            ScrollToEnd();
        });
    }

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
            _transcript.Children.Remove(clip.Row);
            try { File.Delete(clip.Path); } catch (Exception) { }
        }
    }
}
#endif
