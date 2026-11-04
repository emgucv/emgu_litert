//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.CV;
#if ANDROID
using PreviewBase = Emgu.CV.Platform.Maui.UI.CvImageView;
#else
using PreviewBase = Microsoft.Maui.Controls.Image;
#endif

namespace Emgu.LiteRT.Models.CV.Maui;

/// <summary>
/// Shows <see cref="LiveCamera"/>'s frames (or any BGR Mats) through each platform's own view, as Emgu CV's MAUI demo does: Emgu CV's CvImageView
/// on Android, a display layer on iOS (<c>LiveCameraPreviewHandler</c>, registered by <c>UseLiveCamera()</c>; a UIImageView without it), a
/// UIImageView on Mac Catalyst, a WriteableBitmap on Windows. The view and its measured size stay the same between frames. Use it from the UI
/// thread (on iOS with the handler, any thread).
/// </summary>
public sealed class LiveCameraPreview : PreviewBase
{
#if MACCATALYST || IOS
    private UIKit.UIImage _appleImage;
#elif WINDOWS
    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap _bitmap;
    private Mat _bgra;
    private byte[] _pixels;
#endif

    /// <summary>Show a frame. Keep the Mat unchanged until the next one is presented (CvImageView also keeps it for handler changes).</summary>
    public void Present(Mat frame)
    {
#if ANDROID
        // CvImageView converts into its two reusable Android bitmaps and calls native ImageView.SetImageBitmap.
        SetImage(frame);
#elif IOS && !MACCATALYST
        if (Handler is LiveCameraPreviewHandler fast) { fast.Present(frame); return; }
        PresentUIImage(frame);
#elif MACCATALYST
        PresentUIImage(frame);
#elif WINDOWS
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Image view) return;
        _bgra ??= new Mat();
        Emgu.CV.CvInvoke.CvtColor(frame, _bgra, Emgu.CV.CvEnum.ColorConversion.Bgr2Bgra);
        if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap(frame.Width, frame.Height);
            _pixels = new byte[frame.Width * frame.Height * 4];
        }
        if (view.Source != _bitmap) view.Source = _bitmap;
        System.Runtime.InteropServices.Marshal.Copy(_bgra.DataPointer, _pixels, 0, _pixels.Length);
        using (var stream = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsStream(_bitmap.PixelBuffer))
            stream.Write(_pixels, 0, _pixels.Length);
        _bitmap.Invalidate();
#endif
    }

#if MACCATALYST || IOS
    // A UIImage a frame into the MAUI Image's UIImageView: Mac Catalyst's path, and iOS's when the app didn't register the handler.
    private void PresentUIImage(Mat frame)
    {
        if (Handler?.PlatformView is not UIKit.UIImageView view) return;
        var next = frame.ToUIImage();
        var previous = _appleImage;
        view.Image = next;
        _appleImage = next;
        previous?.Dispose();
    }

    private void ClearUIImage()
    {
        if (Handler?.PlatformView is UIKit.UIImageView view) view.Image = null;
        _appleImage?.Dispose();
        _appleImage = null;
    }
#endif

    /// <summary>Show nothing, and release the platform image.</summary>
    public void ClearFrame()
    {
#if ANDROID
        SetImage(null);
#elif IOS && !MACCATALYST
        if (Handler is LiveCameraPreviewHandler fast) fast.Clear();
        ClearUIImage();
#elif MACCATALYST
        ClearUIImage();
#elif WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Image view) view.Source = null;
        _bitmap = null;
        _pixels = null;
        _bgra?.Dispose();
        _bgra = null;
#endif
    }
}
