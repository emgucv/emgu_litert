//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Plays an audio file with play, pause and its position (e.g. a meeting's recording in its card), through the platform's
    /// own player: MediaPlayer on Android, AVAudioPlayer on iOS and Mac Catalyst, Windows.Media.Playback on Windows. Use it on
    /// the UI thread. (<see cref="AudioFilePlayer"/> plays a sound once or repeated, without control.)
    /// </summary>
    public sealed class AudioClipPlayer : IDisposable
    {
#if ANDROID
        private Android.Media.MediaPlayer _player;
#elif IOS || MACCATALYST
        private AVFoundation.AVAudioPlayer _player;
#elif WINDOWS
        private Windows.Media.Playback.MediaPlayer _player;
        private Windows.Media.Core.MediaSource _source;
#endif

        private AudioClipPlayer()
        {
        }

        /// <summary>Raised on the UI thread when it played to the end.</summary>
        public event Action Completed;

        /// <summary>Open a file to play.</summary>
        /// <param name="path">The audio file (WAV, M4A, MP3...)</param>
        /// <exception cref="IOException">It can't be played</exception>
        public static AudioClipPlayer Open(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The recording's file is gone.", path);
            var clip = new AudioClipPlayer();
#if ANDROID
            clip._player = new Android.Media.MediaPlayer();
            clip._player.Completion += (_, _) => MainThread.BeginInvokeOnMainThread(() => clip.Completed?.Invoke());
            clip._player.SetDataSource(path);
            clip._player.Prepare();
#elif IOS || MACCATALYST
            clip._player = AVFoundation.AVAudioPlayer.FromUrl(Foundation.NSUrl.FromFilename(path))
                ?? throw new IOException("The recording couldn't be opened.");
            clip._player.FinishedPlaying += (_, _) => MainThread.BeginInvokeOnMainThread(() => clip.Completed?.Invoke());
            clip._player.PrepareToPlay();
#elif WINDOWS
            clip._source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
            clip._player = new Windows.Media.Playback.MediaPlayer { Source = clip._source };
            clip._player.MediaEnded += (_, _) => MainThread.BeginInvokeOnMainThread(() => clip.Completed?.Invoke());
#else
            throw new PlatformNotSupportedException("Playing audio isn't supported on this platform.");
#endif
            return clip;
        }

        /// <summary>Its length, in seconds (0 until known).</summary>
        public double Duration =>
#if ANDROID
            _player == null ? 0 : _player.Duration / 1000.0;
#elif IOS || MACCATALYST
            _player?.Duration ?? 0;
#elif WINDOWS
            _player?.PlaybackSession.NaturalDuration.TotalSeconds ?? 0;
#else
            0;
#endif

        /// <summary>Where it is, in seconds.</summary>
        public double Position
        {
            get =>
#if ANDROID
                _player == null ? 0 : _player.CurrentPosition / 1000.0;
#elif IOS || MACCATALYST
                _player?.CurrentTime ?? 0;
#elif WINDOWS
                _player?.PlaybackSession.Position.TotalSeconds ?? 0;
#else
                0;
#endif
            set
            {
#if ANDROID
                _player?.SeekTo((int)(value * 1000));
#elif IOS || MACCATALYST
                if (_player != null) _player.CurrentTime = value;
#elif WINDOWS
                if (_player != null) _player.PlaybackSession.Position = TimeSpan.FromSeconds(value);
#endif
            }
        }

        /// <summary>Whether it is playing.</summary>
        public bool IsPlaying =>
#if ANDROID
            _player?.IsPlaying ?? false;
#elif IOS || MACCATALYST
            _player?.Playing ?? false;
#elif WINDOWS
            _player?.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;
#else
            false;
#endif

        /// <summary>Play from where it is (from the start after the end).</summary>
        public void Play()
        {
            if (Duration > 0 && Position >= Duration - 0.05) Position = 0;
#if ANDROID
            _player?.Start();
#elif IOS || MACCATALYST
            _player?.Play();
#elif WINDOWS
            _player?.Play();
#endif
        }

        /// <summary>Pause.</summary>
        public void Pause()
        {
#if ANDROID
            if (_player?.IsPlaying == true) _player.Pause();
#elif IOS || MACCATALYST
            _player?.Pause();
#elif WINDOWS
            _player?.Pause();
#endif
        }

        /// <inheritdoc/>
        public void Dispose()
        {
#if ANDROID
            try { _player?.Stop(); } catch (Exception) { }
            _player?.Release();
            _player?.Dispose();
#elif IOS || MACCATALYST
            _player?.Stop();
            _player?.Dispose();
#elif WINDOWS
            _player?.Pause();
            _player?.Dispose();
            _source?.Dispose();
#endif
            _player = null;
        }
    }
}
