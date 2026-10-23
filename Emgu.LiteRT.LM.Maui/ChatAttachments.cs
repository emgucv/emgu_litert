//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The kinds of attachments a model takes.
    /// </summary>
    [Flags]
    public enum ChatAttachmentKinds
    {
        /// <summary>None</summary>
        None = 0,
        /// <summary>Images (JPEG, PNG...)</summary>
        Image = 1,
        /// <summary>Audio (WAV, MP3, FLAC)</summary>
        Audio = 2
    }

    /// <summary>
    /// Where a <see cref="ChatView"/>'s attachments come from when its attach button is tapped.
    /// </summary>
    public interface IChatAttachmentSource
    {
        /// <summary>
        /// Let the user pick attachments of the given kinds.
        /// </summary>
        /// <param name="kinds">What the model takes</param>
        /// <param name="anchor">The chat view, e.g. to find its page for a menu</param>
        /// <returns>The attachments, ready to send (images downscaled); empty if the user cancelled</returns>
        Task<IReadOnlyList<DataContent>> PickAsync(ChatAttachmentKinds kinds, View anchor);
    }

    /// <summary>
    /// The default attachment source: a menu with the photo library, the camera (where the device has one) and an audio
    /// file (WAV, MP3, FLAC), as the model takes them. Images are downscaled to at most <see cref="MaxImageSize"/> pixels
    /// and sent as JPEG (<see cref="ChatImages.PrepareAsync"/>). Taking photos needs the camera permission in the app's
    /// manifest / Info.plist; picking files needs nothing.
    /// </summary>
    public class MediaChatAttachmentSource : IChatAttachmentSource
    {
        private static readonly FilePickerFileType AudioFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "com.microsoft.waveform-audio", "public.mp3", "org.xiph.flac" } },
            { DevicePlatform.MacCatalyst, new[] { "com.microsoft.waveform-audio", "public.mp3", "org.xiph.flac" } },
            { DevicePlatform.Android, new[] { "audio/wav", "audio/x-wav", "audio/mpeg", "audio/flac", "audio/x-flac" } },
            { DevicePlatform.WinUI, new[] { ".wav", ".mp3", ".flac" } },
        });

        /// <summary>The longest side of an image sent to the model; the vision encoders resize them anyway.</summary>
        public int MaxImageSize { get; set; } = 1024;

        /// <summary>The menu's title.</summary>
        public string Title { get; set; } = "Attach";

        /// <inheritdoc/>
        public virtual async Task<IReadOnlyList<DataContent>> PickAsync(ChatAttachmentKinds kinds, View anchor)
        {
            const string Library = "Photo library", Camera = "Take photo", Audio = "Audio file";
            List<string> choices = new List<string>();
            if (kinds.HasFlag(ChatAttachmentKinds.Image))
            {
                choices.Add(Library);
                if (MediaPicker.Default.IsCaptureSupported)
                    choices.Add(Camera);
            }
            if (kinds.HasFlag(ChatAttachmentKinds.Audio))
                choices.Add(Audio);
            if (choices.Count == 0)
                return Array.Empty<DataContent>();
            Page page = anchor?.Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            string choice = choices.Count == 1 ? choices[0]
                : page != null ? await page.DisplayActionSheetAsync(Title, "Cancel", null, choices.ToArray()) : choices[0];
            FileResult file = null;
            switch (choice)
            {
                case Library:
                    file = (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                    break;
                case Camera:
                    file = await MediaPicker.Default.CapturePhotoAsync();
                    break;
                case Audio:
                    file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an audio file", FileTypes = AudioFileTypes });
                    break;
            }
            if (file == null)
                return Array.Empty<DataContent>();
            byte[] data;
            using (Stream stream = await file.OpenReadAsync())
            using (MemoryStream copy = new MemoryStream())
            {
                await stream.CopyToAsync(copy);
                data = copy.ToArray();
            }
            DataContent content = choice == Audio
                ? new DataContent(data, AudioMediaType(file.FileName))
                : await ChatImages.PrepareAsync(data, MaxImageSize);
            content.Name = choice == Camera ? "Photo" : file.FileName;
            return new[] { content };
        }

        private static string AudioMediaType(string fileName)
        {
            switch (Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant())
            {
                case ".mp3": return "audio/mpeg";
                case ".flac": return "audio/flac";
                default: return "audio/wav";
            }
        }
    }

    /// <summary>
    /// Images for the chat.
    /// </summary>
    public static class ChatImages
    {
        /// <summary>
        /// Downscale an image to at most <paramref name="maxSize"/> pixels on its longest side and encode it as JPEG,
        /// upright (a photo's EXIF orientation applied). An image the platform can't decode is returned as it is.
        /// </summary>
        /// <param name="data">The image file's bytes (JPEG, PNG, HEIC where the platform reads it...)</param>
        /// <param name="maxSize">The longest side, in pixels</param>
        /// <returns>The image as a JPEG DataContent</returns>
        public static Task<DataContent> PrepareAsync(byte[] data, int maxSize = 1024)
        {
            return Task.Run(() =>
            {
                try
                {
                    byte[] jpeg = Downscale(data, maxSize);
                    if (jpeg != null)
                        return new DataContent(jpeg, "image/jpeg");
                }
                catch (Exception)
                {
                }
                return new DataContent(data, GuessImageMediaType(data));
            });
        }

#if ANDROID
        // Android's decoders ignore the EXIF orientation, so a portrait photo would reach the model on its side.
        private static byte[] Downscale(byte[] data, int maxSize)
        {
            Android.Graphics.BitmapFactory.Options bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return null;
            int sample = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxSize)
                sample *= 2;
            using (Android.Graphics.Bitmap decoded = Android.Graphics.BitmapFactory.DecodeByteArray(data, 0, data.Length,
                new Android.Graphics.BitmapFactory.Options { InSampleSize = sample }))
            {
                if (decoded == null)
                    return null;
                int rotation = 0;
                using (MemoryStream exifStream = new MemoryStream(data))
                {
                    var exif = new Android.Media.ExifInterface(exifStream);
                    switch (exif.GetAttributeInt(Android.Media.ExifInterface.TagOrientation, 1))
                    {
                        case 6: rotation = 90; break;
                        case 3: rotation = 180; break;
                        case 8: rotation = 270; break;
                    }
                }
                double scale = Math.Min(1.0, maxSize / (double)Math.Max(decoded.Width, decoded.Height));
                using (Android.Graphics.Matrix matrix = new Android.Graphics.Matrix())
                {
                    matrix.PostScale((float)scale, (float)scale);
                    matrix.PostRotate(rotation);
                    using (Android.Graphics.Bitmap result = Android.Graphics.Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true))
                    using (MemoryStream output = new MemoryStream())
                    {
                        result.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 88, output);
                        return output.ToArray();
                    }
                }
            }
        }
#else
        // UIKit draws images upright; Windows' decoder too.
        private static byte[] Downscale(byte[] data, int maxSize)
        {
            using (MemoryStream input = new MemoryStream(data))
            {
                Microsoft.Maui.Graphics.IImage image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(input);
                if (image == null)
                    return null;
                if (Math.Max(image.Width, image.Height) > maxSize)
                    image = image.Downsize(maxSize, true);
                return image.AsBytes(Microsoft.Maui.Graphics.ImageFormat.Jpeg, 0.88f);
            }
        }
#endif

        private static string GuessImageMediaType(byte[] data)
        {
            if (data != null && data.Length > 4 && data[0] == 0x89 && data[1] == 0x50)
                return "image/png";
            return "image/jpeg";
        }
    }
}
