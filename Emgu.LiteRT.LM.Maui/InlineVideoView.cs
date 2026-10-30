//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if ANDROID || IOS || MACCATALYST || WINDOWS
using Microsoft.Maui.Handlers;
#if ANDROID
using Android.Media;
using Android.Views;
using Android.Widget;
#elif IOS || MACCATALYST
using AVFoundation;
using CoreMedia;
using Foundation;
using UIKit;
#endif

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A video file played in place (e.g. a camera monitor's clip in a chat card, a video moment in the sheet), with the
    /// platform's own player and no extra packages: Android's VideoView, AVPlayer on iOS and Mac Catalyst, and
    /// MediaPlayerElement on Windows - letterboxed in the view's box with the file's rotation applied. The controls around it
    /// are ordinary MAUI views. Register its handler at startup (<c>UseLiteRtChat()</c> does).
    /// </summary>
    public sealed class InlineVideoView : Microsoft.Maui.Controls.View
    {
        /// <summary>The <see cref="Source"/> property.</summary>
        public static readonly BindableProperty SourceProperty = BindableProperty.Create(nameof(Source), typeof(string), typeof(InlineVideoView));

        /// <summary>The video file's path; null or empty stops playing.</summary>
        public string Source { get => (string)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

        /// <summary>Raised when the video is ready: its duration in ms, and its size as it is shown (after rotation).</summary>
        public event Action<int, int, int> Prepared;
        /// <summary>Raised when the video has played to its end.</summary>
        public event Action Completed;
        /// <summary>Raised with a message when the video can't be played.</summary>
        public event Action<string> Failed;
        internal void RaisePrepared(int duration, int width, int height) => Prepared?.Invoke(duration, width, height);
        internal void RaiseCompleted() => Completed?.Invoke();
        internal void RaiseFailed(string message) => Failed?.Invoke(message);

        private IInlineVideoPlayer Player => Handler as IInlineVideoPlayer;
        /// <summary>True while playing.</summary>
        public bool IsPlaying => Player?.IsPlaying == true;
        /// <summary>Where it is playing, in ms.</summary>
        public int PositionMs => Player?.PositionMs ?? 0;
        /// <summary>Start or resume playing.</summary>
        public void Play() => Player?.Play();
        /// <summary>Pause.</summary>
        public void Pause() => Player?.Pause();
        /// <summary>Go to a position, in ms.</summary>
        public void SeekTo(int milliseconds) => Player?.SeekTo(milliseconds);
        /// <summary>Stop playing and release the file.</summary>
        public void Stop() => Player?.Stop();
    }

    // What InlineVideoView asks of its handler on each platform.
    internal interface IInlineVideoPlayer
    {
        bool IsPlaying { get; }
        int PositionMs { get; }
        void Play();
        void Pause();
        void SeekTo(int milliseconds);
        void Stop();
    }

#if ANDROID
    /// <summary>The Android handler of <see cref="InlineVideoView"/>: a VideoView centred in a black FrameLayout.</summary>
    public sealed class InlineVideoHandler : ViewHandler<InlineVideoView, FrameLayout>, IInlineVideoPlayer
    {
        /// <summary>The property mapper.</summary>
        public static readonly IPropertyMapper<InlineVideoView, InlineVideoHandler> Mapper =
            new PropertyMapper<InlineVideoView, InlineVideoHandler>(ViewMapper)
            {
                [nameof(InlineVideoView.Source)] = (handler, view) => handler.SetSource(view.Source)
            };

        /// <summary>The VideoView playing the file.</summary>
        public VideoView Video { get; private set; }

        /// <summary>Create the handler.</summary>
        public InlineVideoHandler() : base(Mapper) { }

        bool IInlineVideoPlayer.IsPlaying => Video?.IsPlaying == true;
        int IInlineVideoPlayer.PositionMs => Video?.CurrentPosition ?? 0;
        void IInlineVideoPlayer.Play() => Video?.Start();
        void IInlineVideoPlayer.Pause() => Video?.Pause();
        void IInlineVideoPlayer.SeekTo(int milliseconds) => Video?.SeekTo(milliseconds);
        void IInlineVideoPlayer.Stop() => Video?.StopPlayback();

        /// <inheritdoc/>
        protected override FrameLayout CreatePlatformView()
        {
            var frame = new FrameLayout(Context);
            frame.SetBackgroundColor(Android.Graphics.Color.Black);
            Video = new VideoView(Context);
            // Match the box; VideoView shrinks itself to the video's aspect ratio and the gravity centres it.
            frame.AddView(Video, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
            Video.SetOnPreparedListener(new PreparedListener(this));
            Video.Completion += (_, _) => VirtualView?.RaiseCompleted();
            Video.Error += (_, e) =>
            {
                e.Handled = true;
                VirtualView?.RaiseFailed("The video couldn't be played (" + e.What + ").");
            };
            return frame;
        }

        /// <inheritdoc/>
        protected override void DisconnectHandler(FrameLayout platformView)
        {
            try { Video?.StopPlayback(); } catch (Exception) { }
            base.DisconnectHandler(platformView);
        }

        // VideoView's Prepared event doesn't carry the player, which knows the duration and the size as shown.
        private sealed class PreparedListener(InlineVideoHandler owner) : Java.Lang.Object, MediaPlayer.IOnPreparedListener
        {
            public void OnPrepared(MediaPlayer player) => owner.VirtualView?.RaisePrepared(player.Duration, player.VideoWidth, player.VideoHeight);
        }

        private void SetSource(string path)
        {
            if (Video == null) return;
            if (string.IsNullOrEmpty(path)) { Video.StopPlayback(); return; }
            // A file, or a library video's content:// URI (VideoLibrary).
            if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                Video.SetVideoURI(Android.Net.Uri.Parse(path));
            else
                Video.SetVideoPath(path);
        }
    }
#elif IOS || MACCATALYST
    /// <summary>The iOS and Mac Catalyst handler of <see cref="InlineVideoView"/>: an AVPlayer drawn by an AVPlayerLayer, letterboxed on black.</summary>
    public sealed class InlineVideoHandler : ViewHandler<InlineVideoView, UIView>, IInlineVideoPlayer
    {
        /// <summary>The property mapper.</summary>
        public static readonly IPropertyMapper<InlineVideoView, InlineVideoHandler> Mapper =
            new PropertyMapper<InlineVideoView, InlineVideoHandler>(ViewMapper)
            {
                [nameof(InlineVideoView.Source)] = (handler, view) => handler.SetSource(view.Source)
            };

        private AVPlayer _player;
        private NSObject _endObserver;
        private int _loading;

        /// <summary>Create the handler.</summary>
        public InlineVideoHandler() : base(Mapper) { }

        bool IInlineVideoPlayer.IsPlaying => _player != null && _player.Rate > 0;
        int IInlineVideoPlayer.PositionMs => _player == null ? 0 : (int)(_player.CurrentTime.Seconds * 1000);
        void IInlineVideoPlayer.Play() => _player?.Play();
        void IInlineVideoPlayer.Pause() => _player?.Pause();
        void IInlineVideoPlayer.SeekTo(int milliseconds) =>
            _player?.Seek(CMTime.FromSeconds(milliseconds / 1000.0, 600), CMTime.Zero, CMTime.Zero);
        void IInlineVideoPlayer.Stop() => SetSource(null);

        /// <inheritdoc/>
        protected override UIView CreatePlatformView() => new PlayerView { BackgroundColor = UIColor.Black };

        /// <inheritdoc/>
        protected override void DisconnectHandler(UIView platformView)
        {
            SetSource(null);
            base.DisconnectHandler(platformView);
        }

        private async void SetSource(string path)
        {
            int loading = ++_loading;
            if (_endObserver != null)
            {
                NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
                _endObserver = null;
            }
            _player?.Pause();
            _player = null;
            if (PlatformView is PlayerView view)
                view.PlayerLayer.Player = null;
            if (string.IsNullOrEmpty(path))
                return;
            // A file, or a Photos library video ("phasset:" + its id, from VideoLibrary).
            AVAsset asset = path.StartsWith(VideoLibrary.PhotoKitPrefix, StringComparison.Ordinal)
                ? await VideoLibrary.RequestAssetAsync(path)
                : AVUrlAsset.Create(NSUrl.FromFilename(path));
            if (asset != null)
            {
                try
                {
                    await asset.LoadValuesTaskAsync(new[] { "duration", "tracks" });
                }
                catch (Exception)
                {
                }
            }
            if (loading != _loading)
                return;
            AVAssetTrack track = asset?.TracksWithMediaType(AVMediaTypes.Video.GetConstant()).FirstOrDefault();
            if (track == null)
            {
                VirtualView?.RaiseFailed("The video couldn't be played.");
                return;
            }
            var item = new AVPlayerItem(asset);
            _player = new AVPlayer(item);
            if (PlatformView is PlayerView shown)
                shown.PlayerLayer.Player = _player;
            _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(AVPlayerItem.DidPlayToEndTimeNotification,
                _ => VirtualView?.RaiseCompleted(), item);
            // The size as shown: the track's natural size turned by its transform.
            CoreGraphics.CGRect size = track.PreferredTransform.TransformRect(new CoreGraphics.CGRect(CoreGraphics.CGPoint.Empty, track.NaturalSize));
            VirtualView?.RaisePrepared((int)(asset.Duration.Seconds * 1000), (int)Math.Abs(size.Width), (int)Math.Abs(size.Height));
        }

        // A view whose layer draws the player, kept the size of the view.
        private sealed class PlayerView : UIView
        {
            public PlayerView()
            {
                PlayerLayer = new AVPlayerLayer { VideoGravity = AVLayerVideoGravity.ResizeAspect };
                Layer.AddSublayer(PlayerLayer);
            }

            public AVPlayerLayer PlayerLayer { get; }

            public override void LayoutSubviews()
            {
                base.LayoutSubviews();
                PlayerLayer.Frame = Bounds;
            }
        }
    }
#elif WINDOWS
    /// <summary>The Windows handler of <see cref="InlineVideoView"/>: a MediaPlayerElement without its controls.</summary>
    public sealed class InlineVideoHandler : ViewHandler<InlineVideoView, Microsoft.UI.Xaml.Controls.MediaPlayerElement>, IInlineVideoPlayer
    {
        /// <summary>The property mapper.</summary>
        public static readonly IPropertyMapper<InlineVideoView, InlineVideoHandler> Mapper =
            new PropertyMapper<InlineVideoView, InlineVideoHandler>(ViewMapper)
            {
                [nameof(InlineVideoView.Source)] = (handler, view) => handler.SetSource(view.Source)
            };

        private Windows.Media.Playback.MediaPlayer _player;

        /// <summary>Create the handler.</summary>
        public InlineVideoHandler() : base(Mapper) { }

        bool IInlineVideoPlayer.IsPlaying =>
            _player?.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;
        int IInlineVideoPlayer.PositionMs => _player == null ? 0 : (int)_player.PlaybackSession.Position.TotalMilliseconds;
        void IInlineVideoPlayer.Play() => _player?.Play();
        void IInlineVideoPlayer.Pause() => _player?.Pause();
        void IInlineVideoPlayer.SeekTo(int milliseconds)
        {
            if (_player != null)
                _player.PlaybackSession.Position = TimeSpan.FromMilliseconds(milliseconds);
        }
        void IInlineVideoPlayer.Stop() => SetSource(null);

        /// <inheritdoc/>
        protected override Microsoft.UI.Xaml.Controls.MediaPlayerElement CreatePlatformView() =>
            new Microsoft.UI.Xaml.Controls.MediaPlayerElement
            {
                AreTransportControlsEnabled = false,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
            };

        /// <inheritdoc/>
        protected override void DisconnectHandler(Microsoft.UI.Xaml.Controls.MediaPlayerElement platformView)
        {
            SetSource(null);
            base.DisconnectHandler(platformView);
        }

        private void SetSource(string path)
        {
            if (_player != null)
            {
                _player.Pause();
                PlatformView?.SetMediaPlayer(null);
                _player.Dispose();
                _player = null;
            }
            if (string.IsNullOrEmpty(path) || PlatformView == null)
                return;
            var player = new Windows.Media.Playback.MediaPlayer { AutoPlay = false };
            Microsoft.UI.Dispatching.DispatcherQueue queue = PlatformView.DispatcherQueue;
            player.MediaOpened += (s, e) => queue.TryEnqueue(() =>
            {
                if (_player != player) return;
                var session = player.PlaybackSession;
                VirtualView?.RaisePrepared((int)session.NaturalDuration.TotalMilliseconds, (int)session.NaturalVideoWidth, (int)session.NaturalVideoHeight);
            });
            player.MediaEnded += (s, e) => queue.TryEnqueue(() => { if (_player == player) VirtualView?.RaiseCompleted(); });
            player.MediaFailed += (s, e) => queue.TryEnqueue(() => { if (_player == player) VirtualView?.RaiseFailed("The video couldn't be played (" + e.Error + ")."); });
            player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
            _player = player;
            PlatformView.SetMediaPlayer(player);
        }
    }
#endif
}
#endif
