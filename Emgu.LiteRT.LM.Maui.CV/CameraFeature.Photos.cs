// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || (IOS && !MACCATALYST)
using System.IO;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.Models.CV.Maui;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// The take_photo alert action (TODO item 44): one full-resolution photo from the live camera (Camera2 or AVFoundation, LiveCamera.TakePhotoAsync), shown in the
// transcript as a thumbnail with its size, Open, Share, Save to gallery and Keep in chat, and in the camera panel's strip. Session only unless kept like
// the snapshots and clips: the JPEGs live in the app's cache folder, the latest five are kept (CameraCaptures), New chat deletes them, and nothing is
// sent to the model.
public sealed partial class CameraFeature
{
    private Task TakeVisualPhotoAsync(VisualEvent occurrence, CancellationToken token) =>
        TakePhotoAsync(() => _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition, token);

    private async Task TakePhotoAsync(Func<string> cause, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!CameraUsable || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
                throw new InvalidOperationException("A photo needs the visible live camera.");
            return _camera;
        });
        string path = System.IO.Path.Combine(FileSystem.CacheDirectory, "photos", "photo-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".jpg");
        object activity = await MainThread.InvokeOnMainThreadAsync(() => BeginCameraActivity("Taking photo"));
        LiveCamera.PhotoResult photo;
        Task click = PlayCameraClickAsync(token);
        try { photo = await camera.TakePhotoAsync(path, token); }
        finally { await MainThread.InvokeOnMainThreadAsync(() => EndCameraActivity(activity)); await click; }
        byte[] thumbnail = PhotoThumbnail(photo.Path);
        byte[] small = SmallJpeg(thumbnail);
        DateTimeOffset at = DateTimeOffset.Now;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (token.IsCancellationRequested || !(PageVisible || BackgroundCapture))
            {
                try { File.Delete(photo.Path); } catch (Exception) { }
                token.ThrowIfCancellationRequested();
                return;
            }
            var capture = new CameraCapture(CameraCaptureKind.Photo, at, cause(), path: photo.Path, thumbnail: small)
            {
                Width = photo.Width, Height = photo.Height
            };
            ChatMessageItem reply = CaptureReply();
            View card = PhotoCard(capture, thumbnail, reply);
            Host.AddCardToReply(card);
            AddCapture(capture, new CaptureViews { Card = card, Reply = reply });
        });
    }

    // A photo's card: the thumbnail (tap to open), its time, size and cause, Open, Share, Save to gallery, and Keep in chat (for a live
    // one with a reply). A restored one is opened from its copy in the cache.
    private View PhotoCard(CameraCapture capture, byte[] thumbnail, ChatMessageItem reply)
    {
        string path = capture.Path;
        DateTimeOffset at = capture.TakenAt;
        long bytes = 0;
        try { bytes = new FileInfo(path).Length; } catch (Exception) { }
        var image = new Image { HeightRequest = 150, WidthRequest = 210, Aspect = Aspect.AspectFit, BackgroundColor = CvViews.Color("ChatRowBorder") };
        if (thumbnail != null) image.Source = CvViews.ToImageSource(thumbnail);
        image.OnTap(() => _ = OpenVisualPhotoAsync(path));
        SemanticProperties.SetDescription(image, "Full-resolution camera photo. Tap to open.");
        var caption = new Label
        {
            Text = "Camera photo · " + at.ToLocalTime().ToString("HH:mm:ss") + (capture.Width > 0 ? " · " + capture.Width + "×" + capture.Height : "") + " · " +
                (bytes / 1048576.0).ToString("0.0") + " MB" + (string.IsNullOrEmpty(capture.Condition) ? "" : "\n" + capture.Condition) + "\n" + CaptureKeptText(capture.Kept),
            FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText")
        };
        var status = new Label { FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText"), IsVisible = false };
        var open = MonitorButton("Open");
        var share = MonitorButton("Share");
        var save = MonitorButton("Save to gallery");
        open.Clicked += async (_, _) => await OpenVisualPhotoAsync(path);
        share.Clicked += async (_, _) =>
        {
            try { await Share.Default.RequestAsync(new ShareFileRequest("Camera photo", new ShareFile(path, "image/jpeg"))); }
            catch (Exception ex) { await Host.AskAsync("Couldn't share the photo", ex.Message, "OK"); }
        };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                status.Text = await PictureGallery.SavePhotoAsync(path, "camera-photo-" + at.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".jpg");
                status.IsVisible = true;
            }
            catch (Exception ex) { await Host.AskAsync("Couldn't save the photo", ex.Message, "OK"); save.IsEnabled = true; }
        };
        var buttons = CaptureButtons(open, share, save, capture.Kept ? null : KeepInChatButton(capture, reply, caption));
        return new VerticalStackLayout
        {
            Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
            Children = { image, caption, buttons, status }
        };
    }

    // A card's buttons, wrapping to the next line rather than squeezed: a squeezed "Full screen" was cut to "Full screer" on an iPad.
    private static FlexLayout CaptureButtons(params Button[] buttons)
    {
        var layout = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, MaximumWidthRequest = 320, HorizontalOptions = LayoutOptions.Start };
        foreach (Button button in buttons)
        {
            if (button == null) continue;
            button.Margin = new Thickness(0, 0, 6, 4);
            FlexLayout.SetShrink(button, 0);
            layout.Children.Add(button);
        }
        return layout;
    }

    // A small JPEG for the card: the photo decoded at a fraction of its size (a full-size bitmap of a 12 MP photo is ~48 MB).
    private static byte[] PhotoThumbnail(string path)
    {
        try
        {
#if IOS
            // ImageIO makes the small image straight from the file, upright (the orientation tag applied).
            using var source = ImageIO.CGImageSource.FromUrl(Foundation.NSUrl.FromFilename(path));
            if (source == null) return null;
            using var small = source.CreateThumbnail(0, new ImageIO.CGImageThumbnailOptions
            {
                CreateThumbnailFromImageAlways = true, CreateThumbnailWithTransform = true, MaxPixelSize = 640
            });
            if (small == null) return null;
            using var image = UIKit.UIImage.FromImage(small);
            using Foundation.NSData jpeg = image.AsJPEG(0.8f);
            return jpeg?.ToArray();
#else
            var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeFile(path, bounds);
            int sample = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= 640) sample *= 2;
            using var decoded = Android.Graphics.BitmapFactory.DecodeFile(path, new Android.Graphics.BitmapFactory.Options { InSampleSize = sample });
            if (decoded == null) return null;
            // The JPEG's orientation tag isn't applied by BitmapFactory.
            int degrees = 0;
            using (var exif = new AndroidX.ExifInterface.Media.ExifInterface(path))
                degrees = exif.GetAttributeInt(AndroidX.ExifInterface.Media.ExifInterface.TagOrientation, 1) switch { 6 => 90, 3 => 180, 8 => 270, _ => 0 };
            using var matrix = new Android.Graphics.Matrix();
            matrix.PostRotate(degrees);
            using Android.Graphics.Bitmap upright = degrees == 0 ? decoded : Android.Graphics.Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
            using var stream = new MemoryStream();
            upright.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 80, stream);
            return stream.ToArray();
#endif
        }
        catch (Exception) { return null; }
    }

    private async Task OpenVisualPhotoAsync(string path)
    {
        try { await Launcher.Default.OpenAsync(new OpenFileRequest("Camera photo", new ReadOnlyFile(path, "image/jpeg"))); }
        catch (Exception ex) { await Host.AskAsync("Couldn't open the photo", ex.Message, "OK"); }
    }
}
#endif
