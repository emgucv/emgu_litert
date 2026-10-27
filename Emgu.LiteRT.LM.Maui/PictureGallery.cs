//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// Adding a picture or a video to the device's own photo collection: Android - MediaStore, in Pictures/&lt;app&gt; or
/// Movies/&lt;app&gt; (no permission needed to add one's own from Android 10); iOS and Mac Catalyst - the Photos library
/// through PhotoKit, add-only access (NSPhotoLibraryAddUsageDescription; the system asks the first time); Windows - the
/// Pictures folder.
/// </summary>
public static class PictureGallery
{
    /// <summary>The folder's name in the gallery (Pictures/&lt;name&gt;, Movies/&lt;name&gt;); the app's name by default.</summary>
    public static string FolderName { get; set; } = AppInfo.Current?.Name ?? "Pictures";

    /// <summary>
    /// Whether a file picker's or saver's failure was the user cancelling: the Windows file picker reports cancelling as a
    /// plain exception, "Operation cancelled or Path doesn't exist.", not an OperationCanceledException (Apple and Android do use that).
    /// </summary>
    /// <param name="exception">The result's exception, or null</param>
    public static bool SaveWasCancelled(Exception exception)
    {
        return exception is OperationCanceledException
            || (exception != null && exception.Message.StartsWith("Operation cancelled", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What the destination is called in the app's buttons.</summary>
    public static string DestinationName =>
#if ANDROID
        "gallery";
#elif WINDOWS
        "Pictures folder";
#else
        "Photos";
#endif

#if ANDROID
    /// <summary>Add a video file to the gallery (Movies/&lt;app&gt;); returns a short description of where it went.</summary>
    public static Task<string> SaveVideoAsync(string path, string fileName) => Task.Run(() =>
    {
        Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "video/mp4");
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Movies/" + FolderName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
        Android.Net.Uri uri = resolver.Insert(Android.Provider.MediaStore.Video.Media.ExternalContentUri, values)
            ?? throw new IOException("The gallery wouldn't take the video.");
        try
        {
            using (Stream output = resolver.OpenOutputStream(uri) ?? throw new IOException("The gallery wouldn't take the video."))
            using (Stream input = File.OpenRead(path))
                input.CopyTo(output);
            values.Clear();
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
        return "Saved to the gallery (Movies/&lt;app&gt;)";
    });

    /// <summary>Add a JPEG file to the gallery (Pictures/&lt;app&gt;) without reading it into memory; returns a short description of where it went.</summary>
    public static Task<string> SavePhotoAsync(string path, string fileName) => Task.Run(() =>
    {
        Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "image/jpeg");
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Pictures/" + FolderName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
        Android.Net.Uri uri = resolver.Insert(Android.Provider.MediaStore.Images.Media.ExternalContentUri, values)
            ?? throw new IOException("The gallery wouldn't take the photo.");
        try
        {
            using (Stream output = resolver.OpenOutputStream(uri) ?? throw new IOException("The gallery wouldn't take the photo."))
            using (Stream input = File.OpenRead(path))
                input.CopyTo(output);
            values.Clear();
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
        return "Saved to the gallery (Pictures/&lt;app&gt;)";
    });
#endif

    /// <summary>Add the picture; returns a short description of where it went. Call on the UI thread.</summary>
    private static string ImageMediaType(string fileName) =>
        fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";

    public static async Task<string> SaveAsync(byte[] png, string fileName)
    {
#if ANDROID
        await Task.CompletedTask;
        Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, ImageMediaType(fileName));
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Pictures/" + FolderName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
        Android.Net.Uri uri = resolver.Insert(Android.Provider.MediaStore.Images.Media.ExternalContentUri, values)
            ?? throw new IOException("The gallery wouldn't take the picture.");
        try
        {
            using (Stream output = resolver.OpenOutputStream(uri) ?? throw new IOException("The gallery wouldn't take the picture."))
                output.Write(png, 0, png.Length);
            values.Clear();
            values.Put(Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
        return "Saved to the gallery (Pictures/&lt;app&gt;)";
#elif IOS || MACCATALYST
        Photos.PHAuthorizationStatus status = await Photos.PHPhotoLibrary.RequestAuthorizationAsync(Photos.PHAccessLevel.AddOnly);
        if (status != Photos.PHAuthorizationStatus.Authorized && status != Photos.PHAuthorizationStatus.Limited)
            throw new UnauthorizedAccessException("Photos access is off. Allow adding photos to this app in the system settings.");
        using var data = Foundation.NSData.FromArray(png);
        using UIKit.UIImage image = UIKit.UIImage.LoadFromData(data) ?? throw new IOException("The picture couldn't be read.");
        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Photos.PHPhotoLibrary.SharedPhotoLibrary.PerformChanges(
            () => Photos.PHAssetChangeRequest.FromImage(image),
            (success, error) =>
            {
                if (success) changed.TrySetResult(true);
                else changed.TrySetException(new IOException(error?.LocalizedDescription ?? "Photos wouldn't take the picture."));
            });
        await changed.Task;
        return "Saved to Photos";
#else
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), FolderName);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, png);
        return "Saved to " + path;
#endif
    }
}
