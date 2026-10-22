// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using Emgu.LiteRT.LM.Agents.Audio;

namespace Maui.Demo.Lite;

// Plays an alert sound file (AlertSoundLibrary) through the platform's own player (TODO item 53): once, or repeated for a number of seconds. It returns when
// the playing is over - the file ended, the time was up, or the token was cancelled, which stops it at once. The volume is the device's media volume.
internal static class AlertFilePlayer
{
    /// <summary>The longest a sound is repeated, and the longest a single play may take.</summary>
    internal const int MaximumRepeatSeconds = 300;
    private static readonly TimeSpan MaximumOnce = TimeSpan.FromMinutes(10);

    /// <param name="path">The audio file</param>
    /// <param name="repeatSeconds">0 to play it once, else repeat it until this many seconds have passed (at most 300)</param>
    internal static async Task PlayAsync(string path, int repeatSeconds, CancellationToken token)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The sound's file is gone.", path);
        repeatSeconds = Math.Clamp(repeatSeconds, 0, MaximumRepeatSeconds);
        token.ThrowIfCancellationRequested();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action stop = null, cleanup = null;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                token.ThrowIfCancellationRequested();
#if ANDROID
                var player = new Android.Media.MediaPlayer();
                cleanup = () => { try { player.Release(); } catch (Exception) { } player.Dispose(); };
                stop = () => { try { if (player.IsPlaying) player.Stop(); } catch (Exception) { } };
                player.Looping = repeatSeconds > 0;
                player.Completion += (_, _) => completed.TrySetResult(true);
                player.Error += (_, e) => { e.Handled = true; completed.TrySetException(new IOException("Audio playback failed: " + e.What)); };
                player.SetDataSource(path);
                player.Prepare();
                player.Start();
#elif MACCATALYST || IOS
                var player = AVFoundation.AVAudioPlayer.FromUrl(Foundation.NSUrl.FromFilename(path));
                if (player == null) throw new IOException("The sound could not be opened.");
                cleanup = () => { player.Stop(); player.Dispose(); };
                stop = () => player.Stop();
                player.NumberOfLoops = repeatSeconds > 0 ? -1 : 0;
                player.FinishedPlaying += (_, e) => { if (e.Status) completed.TrySetResult(true); else completed.TrySetException(new IOException("Audio playback failed.")); };
                if (!player.Play()) throw new IOException("Audio playback could not start.");
#elif WINDOWS
                var player = new Windows.Media.Playback.MediaPlayer { IsLoopingEnabled = repeatSeconds > 0 };
                var media = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
                cleanup = () => { player.Pause(); player.Dispose(); media.Dispose(); };
                stop = () => player.Pause();
                player.MediaEnded += (_, _) => completed.TrySetResult(true);
                player.MediaFailed += (_, e) => completed.TrySetException(new IOException("Audio playback failed: " + e.ErrorMessage));
                player.Source = media;
                player.Play();
#endif
            });
            TimeSpan limit = repeatSeconds > 0 ? TimeSpan.FromSeconds(repeatSeconds) : MaximumOnce;
            try { await completed.Task.WaitAsync(limit, token); }
            catch (TimeoutException) when (repeatSeconds > 0) { }       // the time was up: that is how a repeated sound ends
            catch (TimeoutException) { throw new TimeoutException("The sound played for more than 10 minutes."); }
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => { stop?.Invoke(); cleanup?.Invoke(); });
        }
    }
}
#endif
