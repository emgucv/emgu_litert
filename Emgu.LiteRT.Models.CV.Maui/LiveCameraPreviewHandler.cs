//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if IOS && !MACCATALYST
using AVFoundation;
using CoreAnimation;
using CoreMedia;
using CoreVideo;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Emgu.LiteRT.Models.CV.Maui;

/// <summary>
/// The iOS handler of <see cref="LiveCameraPreview"/>, registered by <c>UseLiveCamera()</c>: the frames go to an AVSampleBufferDisplayLayer as
/// IOSurface-backed pixel buffers from a pool, which the display reads directly - one BGR-to-BGRA conversion a frame (about half a millisecond
/// at 960 x 540) and no allocation, no CGImage and no texture upload, against the UIImage a frame of the plain Image view (a conversion, a
/// CGBitmapContext draw, a copy and an allocation a frame). When the layer fails twice it falls back to that UIImage path. Without the handler
/// the preview still works through MAUI's Image handler.
/// </summary>
public sealed class LiveCameraPreviewHandler : ViewHandler<LiveCameraPreview, UIView>
{
    /// <summary>The property mapper: the view's properties, and <see cref="Image.Aspect"/> as the layer's video gravity.</summary>
    public static readonly IPropertyMapper<LiveCameraPreview, LiveCameraPreviewHandler> Mapper =
        new PropertyMapper<LiveCameraPreview, LiveCameraPreviewHandler>(ViewMapper)
        {
            [nameof(LiveCameraPreview.Aspect)] = (handler, view) => handler.ApplyAspect(view.Aspect)
        };

    // The display layer's video gravity takes AVFoundation's constant names (the binding types it as a string).
    private const string GravityAspectFit = "AVLayerVideoGravityResizeAspect", GravityAspectFill = "AVLayerVideoGravityResizeAspectFill", GravityFill = "AVLayerVideoGravityResize";

    private CVPixelBufferPool _pool;
    private CVPixelBufferPoolAllocationSettings _allocation;
    private CMVideoFormatDescription _format;
    private int _poolWidth, _poolHeight;
    private int _failures;
    private bool _fallback;
    private UIImage _fallbackImage;
    private long _presented, _dropped;
    // Present runs on the camera's thread; Clear and the disconnect on the main thread: the pool and the layer's state are shared.
    private readonly object _lock = new object();

    /// <summary>Create the handler.</summary>
    public LiveCameraPreviewHandler() : base(Mapper) { }

    /// <summary>How many frames the layer took (for diagnostics).</summary>
    public long Presented => Interlocked.Read(ref _presented);

    /// <summary>How many frames were dropped because the layer wasn't ready or the pool was empty (for diagnostics).</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>True once the layer failed and the frames go through a UIImage each instead.</summary>
    public bool UsingFallback => _fallback;

    /// <summary>The display layer (true, the default), or a UIImage a frame into a UIImageView, as the plain Image view does.</summary>
    public bool UseDisplayLayer { get; set; } = true;

    /// <inheritdoc/>
    protected override UIView CreatePlatformView() => new DisplayView();

    /// <inheritdoc/>
    protected override void ConnectHandler(UIView platformView)
    {
        base.ConnectHandler(platformView);
        if (VirtualView != null) ApplyAspect(VirtualView.Aspect);
    }

    /// <inheritdoc/>
    protected override void DisconnectHandler(UIView platformView)
    {
        Clear();
        lock (_lock) ReleasePool();
        base.DisconnectHandler(platformView);
    }

    private void ApplyAspect(Aspect aspect)
    {
        if (PlatformView is not DisplayView view) return;
        view.VideoLayer.VideoGravity = aspect switch
        {
            Aspect.AspectFill => GravityAspectFill,
            Aspect.Fill => GravityFill,
            _ => GravityAspectFit
        };
        view.Fallback.ContentMode = aspect switch
        {
            Aspect.AspectFill => UIViewContentMode.ScaleAspectFill,
            Aspect.Fill => UIViewContentMode.ScaleToFill,
            _ => UIViewContentMode.ScaleAspectFit
        };
    }

    /// <summary>
    /// Show a frame (8-bit BGR, BGRA or gray). Thread-safe: the layer takes frames from any thread, and the frame is converted into a buffer of
    /// the pool before this returns, so the caller may reuse the Mat at once.
    /// </summary>
    public void Present(Mat frame)
    {
        if (frame == null || frame.IsEmpty || PlatformView is not DisplayView view) return;
        if (_fallback || !UseDisplayLayer) { PresentFallback(view, frame); return; }
        lock (_lock) PresentLocked(view, frame);
    }

    private void PresentLocked(DisplayView view, Mat frame)
    {
        if (PlatformView != view) return;
        try
        {
            AVSampleBufferDisplayLayer layer = view.VideoLayer;
            if (layer.Status == AVQueuedSampleBufferRenderingStatus.Failed || layer.RequiresFlushToResumeDecoding)
            {
                layer.Flush();
                if (layer.Status == AVQueuedSampleBufferRenderingStatus.Failed)
                {
                    if (++_failures >= 2) { _fallback = true; PresentFallback(view, frame); return; }
                    // The view is UIKit's: replaced on the main thread, and this frame dropped when it came from another.
                    if (!MainThread.IsMainThread)
                    {
                        MainThread.BeginInvokeOnMainThread(() => { lock (_lock) { view.ReplaceLayer(); if (VirtualView != null) ApplyAspect(VirtualView.Aspect); } });
                        Interlocked.Increment(ref _dropped);
                        return;
                    }
                    view.ReplaceLayer();
                    layer = view.VideoLayer;
                    if (VirtualView != null) ApplyAspect(VirtualView.Aspect);
                }
            }
            if (!layer.ReadyForMoreMediaData) { Interlocked.Increment(ref _dropped); return; }
            int width = frame.Width, height = frame.Height;
            if (_pool == null || width != _poolWidth || height != _poolHeight) CreatePool(width, height);
            using CVPixelBuffer buffer = _pool.CreatePixelBuffer(_allocation, out CVReturn status);
            if (buffer == null) { Interlocked.Increment(ref _dropped); return; }
            buffer.Lock(CVPixelBufferLock.None);
            try
            {
                using var target = new Mat(height, width, DepthType.Cv8U, 4, buffer.BaseAddress, (int)buffer.BytesPerRow);
                switch (frame.NumberOfChannels)
                {
                    case 4: frame.CopyTo(target); break;
                    case 1: CvInvoke.CvtColor(frame, target, ColorConversion.Gray2Bgra); break;
                    default: CvInvoke.CvtColor(frame, target, ColorConversion.Bgr2Bgra); break;
                }
            }
            finally { buffer.Unlock(CVPixelBufferLock.None); }
            if (_format == null)
            {
                _format = CMVideoFormatDescription.CreateForImageBuffer(buffer, out CMFormatDescriptionError formatError);
                if (_format == null) throw new InvalidOperationException("No format description for the frame: " + formatError);
            }
            var timing = new CMSampleTimingInfo { DecodeTimeStamp = CMTime.Invalid, PresentationTimeStamp = CMTime.Invalid, Duration = CMTime.Invalid };
            using CMSampleBuffer sample = CMSampleBuffer.CreateReadyWithImageBuffer(buffer, _format, ref timing, out CMSampleBufferError sampleError);
            if (sample == null) throw new InvalidOperationException("No sample buffer for the frame: " + sampleError);
            // Shown as soon as it arrives: no timebase.
            CMSampleBufferAttachmentSettings[] attachments = sample.GetSampleAttachments(true);
            if (attachments != null && attachments.Length > 0) attachments[0].DisplayImmediately = true;
            layer.Enqueue(sample);
            Interlocked.Increment(ref _presented);
            _failures = 0;
        }
        catch (Exception)
        {
            if (++_failures >= 2) _fallback = true;
            ReleasePool();
            PresentFallback(view, frame);
        }
    }

    /// <summary>Show nothing, and release the frames shown.</summary>
    public void Clear()
    {
        if (PlatformView is not DisplayView view) return;
        lock (_lock) ClearLocked(view);
    }

    private void ClearLocked(DisplayView view)
    {
        try { view.VideoLayer.FlushAndRemoveImage(); } catch (Exception) { }
        view.VideoLayer.Hidden = false;
        view.Fallback.Image = null;
        view.Fallback.Hidden = true;
        _fallbackImage?.Dispose();
        _fallbackImage = null;
    }

    // A new pool when the frame's size changes (the camera's orientation does that), IOSurface-backed so the display reads the buffers in place.
    private void CreatePool(int width, int height)
    {
        ReleasePool();
        var attributes = new CVPixelBufferAttributes { PixelFormatType = CVPixelFormatType.CV32BGRA, Width = width, Height = height };
        attributes.Dictionary[CVPixelBuffer.IOSurfacePropertiesKey] = new NSDictionary();
        _pool = new CVPixelBufferPool(new CVPixelBufferPoolSettings { MinimumBufferCount = 2 }, attributes);
        // The layer holds a few buffers while it shows them; past this many a frame is dropped rather than another buffer made.
        _allocation = new CVPixelBufferPoolAllocationSettings { Threshold = 4 };
        _poolWidth = width;
        _poolHeight = height;
    }

    private void ReleasePool()
    {
        _format?.Dispose(); _format = null;
        _pool?.Dispose(); _pool = null;
        _allocation = null;
        _poolWidth = _poolHeight = 0;
    }

    // The plain path: a UIImage a frame into a UIImageView (set on the main thread; at once when already there).
    private void PresentFallback(DisplayView view, Mat frame)
    {
        UIImage next = frame.ToUIImage();
        if (MainThread.IsMainThread) Show(view, next);
        else MainThread.BeginInvokeOnMainThread(() => Show(view, next));
    }

    private void Show(DisplayView view, UIImage next)
    {
        UIImage previous = _fallbackImage;
        if (!view.VideoLayer.Hidden) { try { view.VideoLayer.FlushAndRemoveImage(); } catch (Exception) { } view.VideoLayer.Hidden = true; }
        view.Fallback.Image = next;
        view.Fallback.Hidden = false;
        _fallbackImage = next;
        previous?.Dispose();
    }

    // The native view: the display layer the size of the view, and the fallback image view over it.
    private sealed class DisplayView : UIView
    {
        public DisplayView()
        {
            ClipsToBounds = true;
            VideoLayer = new AVSampleBufferDisplayLayer { VideoGravity = GravityAspectFit };
            Layer.AddSublayer(VideoLayer);
            Fallback = new UIImageView { ContentMode = UIViewContentMode.ScaleAspectFit, Hidden = true };
            AddSubview(Fallback);
        }

        public AVSampleBufferDisplayLayer VideoLayer { get; private set; }
        public UIImageView Fallback { get; }

        // A failed layer is replaced (its state can't be reset).
        public void ReplaceLayer()
        {
            string gravity = VideoLayer.VideoGravity;
            VideoLayer.RemoveFromSuperLayer();
            VideoLayer.Dispose();
            VideoLayer = new AVSampleBufferDisplayLayer { VideoGravity = gravity, Frame = Bounds };
            Layer.InsertSublayer(VideoLayer, 0);
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            // Without this the layer animates to the new size.
            CATransaction.Begin();
            CATransaction.DisableActions = true;
            VideoLayer.Frame = Bounds;
            CATransaction.Commit();
            Fallback.Frame = Bounds;
        }
    }
}
#endif
