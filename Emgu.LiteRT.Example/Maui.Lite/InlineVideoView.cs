// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using Android.Content;
using Android.Media;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Maui.Demo.Lite;

// A video played in place: Android's own VideoView (no extra packages), letterboxed in the view's box with the file's rotation applied. The chat's
// clip cards use it (LiteRtLmChatPage.VisualClips.cs); the controls around it are ordinary MAUI views.
internal sealed class InlineVideoView : Microsoft.Maui.Controls.View
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(nameof(Source), typeof(string), typeof(InlineVideoView));
    public string Source { get => (string)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <summary>Duration in ms, and the video's size as it is shown (after rotation).</summary>
    public event Action<int, int, int> Prepared;
    public event Action Completed;
    public event Action<string> Failed;
    internal void RaisePrepared(int duration, int width, int height) => Prepared?.Invoke(duration, width, height);
    internal void RaiseCompleted() => Completed?.Invoke();
    internal void RaiseFailed(string message) => Failed?.Invoke(message);

    private InlineVideoHandler Native => Handler as InlineVideoHandler;
    public bool IsPlaying => Native?.Video.IsPlaying == true;
    public int PositionMs => Native?.Video.CurrentPosition ?? 0;
    public void Play() => Native?.Video.Start();
    public void Pause() => Native?.Video.Pause();
    public void SeekTo(int milliseconds) => Native?.Video.SeekTo(milliseconds);
    public void Stop() => Native?.Video.StopPlayback();
}

internal sealed class InlineVideoHandler : ViewHandler<InlineVideoView, FrameLayout>
{
    public static readonly IPropertyMapper<InlineVideoView, InlineVideoHandler> Mapper =
        new PropertyMapper<InlineVideoView, InlineVideoHandler>(ViewMapper)
        {
            [nameof(InlineVideoView.Source)] = (handler, view) => handler.SetSource(view.Source)
        };

    public VideoView Video { get; private set; }

    public InlineVideoHandler() : base(Mapper) { }

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
#endif
