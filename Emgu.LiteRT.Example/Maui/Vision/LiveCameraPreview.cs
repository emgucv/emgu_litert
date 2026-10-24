// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
#if ANDROID
using PreviewBase = Emgu.CV.Platform.Maui.UI.CvImageView;
#else
using PreviewBase = Microsoft.Maui.Controls.Image;
#endif

namespace Maui.Demo.Lite;

// Match the Emgu CV demo's native rendering path. Keep the MAUI view and its measured size stable between frames.
internal sealed class LiveCameraPreview : PreviewBase
{
#if MACCATALYST || IOS
    private UIKit.UIImage _appleImage;
#elif WINDOWS
    private Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap _bitmap;
    private Mat _bgra;
    private byte[] _pixels;
#endif

    // The caller retains the Mat until the next presentation (CvImageView also retains it for handler changes).
    public void Present(Mat frame)
    {
#if ANDROID
        // CvImageView converts into its two reusable Android bitmaps and calls native ImageView.SetImageBitmap.
        SetImage(frame);
#elif MACCATALYST || IOS
        if (Handler?.PlatformView is not UIKit.UIImageView view) return;
        var next = frame.ToUIImage();
        var previous = _appleImage;
        view.Image = next;
        _appleImage = next;
        previous?.Dispose();
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

    public void ClearFrame()
    {
#if ANDROID
        SetImage(null);
#elif MACCATALYST || IOS
        if (Handler?.PlatformView is UIKit.UIImageView view) view.Image = null;
        _appleImage?.Dispose();
        _appleImage = null;
#elif WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Image view) view.Source = null;
        _bitmap = null;
        _pixels = null;
        _bgra?.Dispose();
        _bgra = null;
#endif
    }
}
