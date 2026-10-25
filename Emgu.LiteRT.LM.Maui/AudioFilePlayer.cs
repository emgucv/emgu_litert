//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Plays an audio file (e.g. an alert sound from Emgu.LiteRT.LM.Agents' <c>AlertSoundLibrary</c>) through the platform's own player -
    /// MediaPlayer on Android, AVAudioPlayer on iOS and Mac Catalyst, Windows.Media.Playback on Windows - once, or repeated for a number of
    /// seconds. The volume is the device's media volume.
    /// </summary>
    public static class AudioFilePlayer
    {
        /// <summary>The longest a sound is repeated, in seconds.</summary>
        public const int MaximumRepeatSeconds = 300;
        private static readonly TimeSpan MaximumOnce = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Play the file; returns when the playing is over - the file ended, the time was up, or the token was cancelled, which stops it
        /// at once.
        /// </summary>
        /// <param name="path">The audio file</param>
        /// <param name="repeatSeconds">0 to play it once, else repeat it until this many seconds have passed (at most 300)</param>
        /// <param name="token">Stops playing</param>
        /// <exception cref="FileNotFoundException">There is no such file</exception>
        /// <exception cref="IOException">It couldn't be played</exception>
        /// <exception cref="TimeoutException">Played once, it took more than 10 minutes</exception>
        public static async Task PlayAsync(string path, int repeatSeconds, CancellationToken token)
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
}
