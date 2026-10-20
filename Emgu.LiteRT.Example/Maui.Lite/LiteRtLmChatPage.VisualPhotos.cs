// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using Emgu.LiteRT.LM.Agents.Events;

namespace Maui.Demo.Lite;

// The take_photo alert action (TODO item 44): one full-resolution photo from the live camera (Camera2, LiveCamera.TakePhotoAsync), shown in the
// transcript as a thumbnail with its size, Open, Share and Save to gallery. Session only like the snapshots and clips: the JPEGs live in the app's
// cache folder, the latest five are kept, New chat deletes them, and nothing is sent to the model.
public partial class LiteRtLmChatPage
{
    private readonly Queue<(View Row, string Path)> _visualPhotos = new();
    private const int MaximumVisualPhotos = 5;

    private Task TakeVisualPhotoAsync(VisualEvent occurrence, CancellationToken token) =>
        TakePhotoAsync(() => _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition, token);

    private async Task TakePhotoAsync(Func<string> cause, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!_pageVisible || _cameraSuspended || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
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
        DateTimeOffset at = DateTimeOffset.Now;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (token.IsCancellationRequested || !_pageVisible)
            {
                try { File.Delete(photo.Path); } catch (Exception) { }
                token.ThrowIfCancellationRequested();
                return;
            }
            string condition = cause();
            var image = new Image { HeightRequest = 150, WidthRequest = 210, Aspect = Aspect.AspectFit, BackgroundColor = Theme.ImageBackground };
            if (thumbnail != null) image.Source = ImageUtil.ToImageSource(thumbnail);
            image.OnTap(() => _ = OpenVisualPhotoAsync(photo.Path));
            SemanticProperties.SetDescription(image, "Full-resolution camera photo. Tap to open.");
            var caption = new Label
            {
                Text = "Camera photo · " + at.ToString("HH:mm:ss") + " · " + photo.Width + "×" + photo.Height + " · " +
                    (photo.Bytes / 1048576.0).ToString("0.0") + " MB\n" + condition + "\nSession only",
                FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText
            };
            var status = new Label { FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText, IsVisible = false };
            var open = MonitorButton("Open");
            var share = MonitorButton("Share");
            var save = MonitorButton("Save to gallery");
            open.Clicked += async (_, _) => await OpenVisualPhotoAsync(photo.Path);
            share.Clicked += async (_, _) =>
            {
                try { await Share.Default.RequestAsync(new ShareFileRequest("Camera photo", new ShareFile(photo.Path, "image/jpeg"))); }
                catch (Exception ex) { await DisplayAlertAsync("Couldn't share the photo", ex.Message, "OK"); }
            };
            save.Clicked += async (_, _) =>
            {
                save.IsEnabled = false;
                try
                {
                    status.Text = await PictureGallery.SavePhotoAsync(photo.Path, "camera-photo-" + at.ToString("yyyyMMdd-HHmmss") + ".jpg");
                    status.IsVisible = true;
                }
                catch (Exception ex) { await DisplayAlertAsync("Couldn't save the photo", ex.Message, "OK"); save.IsEnabled = true; }
            };
            var row = new VerticalStackLayout
            {
                Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
                Children = { image, caption, new HorizontalStackLayout { Spacing = 6, Children = { open, share, save } }, status }
            };
            if (_transcript.Children.Contains(_emptyLabel)) _transcript.Children.Remove(_emptyLabel);
            int index = _agentBubble != null ? _transcript.Children.IndexOf(_agentBubble.Row) : -1;
            if (index >= 0) _transcript.Children.Insert(index, row); else _transcript.Children.Add(row);
            _visualPhotos.Enqueue((row, photo.Path));
            while (_visualPhotos.Count > MaximumVisualPhotos)
            {
                var old = _visualPhotos.Dequeue();
                _transcript.Children.Remove(old.Row);
                try { File.Delete(old.Path); } catch (Exception) { }
            }
            ScrollToEnd();
        });
    }

    // A small JPEG for the card: the photo decoded at a fraction of its size (a full-size bitmap of a 12 MP photo is ~48 MB).
    private static byte[] PhotoThumbnail(string path)
    {
        try
        {
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
        }
        catch (Exception) { return null; }
    }

    private async Task OpenVisualPhotoAsync(string path)
    {
        try { await Launcher.Default.OpenAsync(new OpenFileRequest("Camera photo", new ReadOnlyFile(path, "image/jpeg"))); }
        catch (Exception ex) { await DisplayAlertAsync("Couldn't open the photo", ex.Message, "OK"); }
    }

    private void ClearVisualPhotos()
    {
        while (_visualPhotos.Count > 0)
        {
            var photo = _visualPhotos.Dequeue();
            _transcript.Children.Remove(photo.Row);
            try { File.Delete(photo.Path); } catch (Exception) { }
        }
    }
}
#endif
