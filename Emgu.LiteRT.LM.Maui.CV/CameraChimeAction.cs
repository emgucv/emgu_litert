// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents.Events;

namespace Emgu.LiteRT.LM.Maui.CV;

internal sealed class CameraChimeAction : IVisualAction
{
    public string Id => "play_sound";
    private readonly string _asset;
    private Action _stop;
    // The alert chime, or another short sound from the app package (the camera's shutter click).
    public CameraChimeAction(string asset = "visual_chime.wav") { _asset = asset; }
    // Called synchronously on the UI thread when hiding the feed.
    public void Stop() => _stop?.Invoke();

    public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken cancellationToken)
    {
        string path = Path.Combine(FileSystem.CacheDirectory, _asset);
        if (!File.Exists(path))
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                using (var source = await CvAssets.OpenAsync(_asset))
                using (var destination = File.Create(temporary))
                    await source.CopyToAsync(destination, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action cleanup = null;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
#if ANDROID
                var player = new Android.Media.MediaPlayer();
                cleanup = () => { player.Release(); player.Dispose(); };
                _stop = () => { if (player.IsPlaying) player.Stop(); completed.TrySetCanceled(); };
                player.Completion += (_, _) => completed.TrySetResult(true);
                player.Error += (_, e) => { e.Handled = true; completed.TrySetException(new IOException("Audio playback failed: " + e.What)); };
                player.SetDataSource(path);
                player.Prepare();
                player.Start();
#elif MACCATALYST || IOS
                var player = AVFoundation.AVAudioPlayer.FromUrl(Foundation.NSUrl.FromFilename(path));
                if (player == null) throw new IOException("The alert sound could not be opened.");
                cleanup = () => { player.Stop(); player.Dispose(); };
                _stop = () => { player.Stop(); completed.TrySetCanceled(); };
                player.FinishedPlaying += (_, e) => { if (e.Status) completed.TrySetResult(true); else completed.TrySetException(new IOException("Audio playback failed.")); };
                if (!player.Play()) throw new IOException("Audio playback could not start.");
#elif WINDOWS
                var player = new Windows.Media.Playback.MediaPlayer();
                var media = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
                cleanup = () => { player.Pause(); player.Dispose(); media.Dispose(); };
                _stop = () => { player.Pause(); completed.TrySetCanceled(); };
                player.MediaEnded += (_, _) => completed.TrySetResult(true);
                player.MediaFailed += (_, e) => completed.TrySetException(new IOException("Audio playback failed: " + e.ErrorMessage));
                player.Source = media;
                player.Play();
#endif
            });
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => { _stop = null; cleanup?.Invoke(); });
        }
    }
}
