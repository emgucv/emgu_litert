//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if ANDROID
using Android.Media;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A video file played in place (e.g. a camera monitor's clip in a chat card), Android only: Android's own VideoView, no extra packages,
    /// letterboxed in the view's box with the file's rotation applied. The controls around it are ordinary MAUI views. Register its handler
    /// at startup: <c>builder.ConfigureMauiHandlers(h => h.AddHandler&lt;InlineVideoView, InlineVideoHandler&gt;())</c>.
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

        private InlineVideoHandler Native => Handler as InlineVideoHandler;
        /// <summary>True while playing.</summary>
        public bool IsPlaying => Native?.Video.IsPlaying == true;
        /// <summary>Where it is playing, in ms.</summary>
        public int PositionMs => Native?.Video.CurrentPosition ?? 0;
        /// <summary>Start or resume playing.</summary>
        public void Play() => Native?.Video.Start();
        /// <summary>Pause.</summary>
        public void Pause() => Native?.Video.Pause();
        /// <summary>Go to a position, in ms.</summary>
        public void SeekTo(int milliseconds) => Native?.Video.SeekTo(milliseconds);
        /// <summary>Stop playing and release the file.</summary>
        public void Stop() => Native?.Video.StopPlayback();
    }

    /// <summary>The Android handler of <see cref="InlineVideoView"/>: a VideoView centred in a black FrameLayout.</summary>
    public sealed class InlineVideoHandler : ViewHandler<InlineVideoView, FrameLayout>
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
            Video.SetVideoPath(path);
        }
    }
}
#endif
