//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.IO;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

// Adding a generated picture to the device's own photo collection: Android - MediaStore, in Pictures/Emgu LiteRT (no
// permission needed to add one's own pictures from Android 10); iOS and Mac Catalyst - the Photos library through PhotoKit,
// add-only access (NSPhotoLibraryAddUsageDescription; the system asks the first time); Windows - the Pictures folder.
internal static class PictureGallery
{
    /// <summary>What the destination is called in the app's buttons.</summary>
    public static string DestinationName =>
#if ANDROID
        "gallery";
#elif WINDOWS
        "Pictures folder";
#else
        "Photos";
#endif

    /// <summary>Add the picture; returns a short description of where it went. Call on the UI thread.</summary>
    public static async Task<string> SaveAsync(byte[] png, string fileName)
    {
#if ANDROID
        await Task.CompletedTask;
        Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "image/png");
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Pictures/Emgu LiteRT");
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
        return "Saved to the gallery (Pictures/Emgu LiteRT)";
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
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Emgu LiteRT");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, png);
        return "Saved to " + path;
#endif
    }
}

#endif
