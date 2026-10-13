//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// A photo in the device's photo library.
    /// </summary>
    internal sealed class PhotoItem
    {
        // The platform's id: a MediaStore id (Android), a PhotoKit local identifier (iOS, Mac Catalyst), a file path (Windows).
        public string Id;
        public DateTimeOffset Taken;
        // Changes when the photo is edited, so its embedding is computed again.
        public long Version;
        // Details (LoadDetailsAsync fills the ones listing doesn't): where (GPS, if the photo has it and the app may read
        // it), the camera, the size, and kinds: "screenshot", "selfie", "panorama", "portrait", "favorite".
        public double? Latitude;
        public double? Longitude;
        public string Camera;
        public int Width;
        public int Height;
        public HashSet<string> Kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Read access to the device's photos, for the chat's photo search: the most recent ones (newest first) and an image
    /// of one at a given size. Android: MediaStore (READ_MEDIA_IMAGES); iOS and Mac Catalyst: PhotoKit (the Photos library,
    /// only what is on the device - no iCloud downloads); Windows: the Pictures folder and its subfolders.
    /// </summary>
    internal static class PhotoLibrary
    {
        /// <summary>
        /// Ask for access to the photos (the system asks the user the first time), and on Android 10+ separately for
        /// their locations (ACCESS_MEDIA_LOCATION: without it Android removes the GPS position from photos it hands out;
        /// photo search works without it, just without places). Call on the UI thread.
        /// </summary>
        public static async Task<bool> RequestAccessAsync()
        {
#if WINDOWS
            await Task.CompletedTask;
            return true;
#elif ANDROID
            PermissionStatus status = await Permissions.CheckStatusAsync<ReadPhotosPermission>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<ReadPhotosPermission>();
            if (status == PermissionStatus.Granted || OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                try
                {
                    if (await Permissions.CheckStatusAsync<MediaLocationPermission>() != PermissionStatus.Granted)
                        await Permissions.RequestAsync<MediaLocationPermission>();
                }
                catch (Exception)
                {
                }
            }
            // Android 14+ also lets the user allow only some photos (READ_MEDIA_VISUAL_USER_SELECTED): MediaStore then
            // returns those, so go on and let the query tell.
            return status == PermissionStatus.Granted || OperatingSystem.IsAndroidVersionAtLeast(34);
#else
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Photos>();
            if (status != PermissionStatus.Granted && status != PermissionStatus.Limited)
                status = await Permissions.RequestAsync<Permissions.Photos>();
            return status == PermissionStatus.Granted || status == PermissionStatus.Limited;
#endif
        }

#if ANDROID
        // READ_MEDIA_IMAGES on Android 13+, READ_EXTERNAL_STORAGE before (MAUI's Permissions.Photos also asks for videos).
        private sealed class ReadPhotosPermission : Permissions.BasePlatformPermission
        {
            public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
                OperatingSystem.IsAndroidVersionAtLeast(33)
                    ? new[] { ("android.permission.READ_MEDIA_IMAGES", true) }
                    : new[] { ("android.permission.READ_EXTERNAL_STORAGE", true) };
        }

        // The GPS positions in photos (Android 10+).
        private sealed class MediaLocationPermission : Permissions.BasePlatformPermission
        {
            public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
                new[] { ("android.permission.ACCESS_MEDIA_LOCATION", true) };
        }

        private static bool HasMediaLocation()
        {
            return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(Android.App.Application.Context,
                "android.permission.ACCESS_MEDIA_LOCATION") == Android.Content.PM.Permission.Granted;
        }
#endif

        /// <summary>
        /// The most recent photos, newest first. Runs on a background thread.
        /// </summary>
        public static Task<List<PhotoItem>> GetRecentPhotosAsync(int max)
        {
            return Task.Run(() =>
            {
                List<PhotoItem> photos = new List<PhotoItem>();
#if ANDROID
                Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
                // is_favorite exists from Android 11.
                bool favorites = OperatingSystem.IsAndroidVersionAtLeast(30);
                string[] projection = favorites
                    ? new[] { "_id", "datetaken", "date_modified", "date_added", "width", "height", "relative_path", "is_favorite" }
                    : new[] { "_id", "datetaken", "date_modified", "date_added", "width", "height", "relative_path" };
                using (Android.Database.ICursor cursor = resolver.Query(
                    Android.Provider.MediaStore.Images.Media.ExternalContentUri, projection, null, null, "date_added DESC"))
                {
                    while (cursor != null && cursor.MoveToNext() && photos.Count < max)
                    {
                        long taken = cursor.GetLong(1);
                        PhotoItem photo = new PhotoItem
                        {
                            Id = cursor.GetLong(0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Taken = taken > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(taken) : DateTimeOffset.FromUnixTimeSeconds(cursor.GetLong(3)),
                            Version = cursor.GetLong(2),
                            Width = cursor.GetInt(4),
                            Height = cursor.GetInt(5)
                        };
                        string path = cursor.GetString(6) ?? string.Empty;
                        if (path.IndexOf("Screenshot", StringComparison.OrdinalIgnoreCase) >= 0)
                            photo.Kinds.Add("screenshot");
                        if (favorites && cursor.GetInt(7) != 0)
                            photo.Kinds.Add("favorite");
                        photos.Add(photo);
                    }
                }
#elif IOS || MACCATALYST
                Photos.PHFetchOptions options = new Photos.PHFetchOptions
                {
                    SortDescriptors = new[] { new Foundation.NSSortDescriptor("creationDate", false) },
                    FetchLimit = (nuint)Math.Max(0, max)
                };
                Photos.PHFetchResult result = Photos.PHAsset.FetchAssets(Photos.PHAssetMediaType.Image, options);
                HashSet<string> selfies = SelfieIds();
                for (nint i = 0; i < (nint)result.Count; i++)
                {
                    if (!(result[i] is Photos.PHAsset asset))
                        continue;
                    PhotoItem photo = new PhotoItem
                    {
                        Id = asset.LocalIdentifier,
                        Taken = asset.CreationDate != null ? new DateTimeOffset((DateTime)asset.CreationDate) : DateTimeOffset.MinValue,
                        Version = asset.ModificationDate != null ? (long)asset.ModificationDate.SecondsSinceReferenceDate : 0,
                        Width = (int)asset.PixelWidth,
                        Height = (int)asset.PixelHeight
                    };
                    if (asset.Location != null)
                    {
                        photo.Latitude = asset.Location.Coordinate.Latitude;
                        photo.Longitude = asset.Location.Coordinate.Longitude;
                    }
                    if (asset.MediaSubtypes.HasFlag(Photos.PHAssetMediaSubtype.Screenshot))
                        photo.Kinds.Add("screenshot");
                    if (asset.MediaSubtypes.HasFlag(Photos.PHAssetMediaSubtype.PhotoPanorama))
                        photo.Kinds.Add("panorama");
                    if (asset.MediaSubtypes.HasFlag(Photos.PHAssetMediaSubtype.PhotoDepthEffect))
                        photo.Kinds.Add("portrait");
                    if (asset.Favorite)
                        photo.Kinds.Add("favorite");
                    if (selfies.Contains(asset.LocalIdentifier))
                        photo.Kinds.Add("selfie");
                    photos.Add(photo);
                }
#elif WINDOWS
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string[] types = { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" };
                if (Directory.Exists(folder))
                {
                    IEnumerable<FileInfo> files = new DirectoryInfo(folder)
                        .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                        .Where(f => types.Contains(f.Extension.ToLowerInvariant()));
                    foreach (FileInfo file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(max))
                        photos.Add(new PhotoItem
                        {
                            Id = file.FullName,
                            Taken = file.LastWriteTime,
                            Version = file.LastWriteTimeUtc.Ticks
                        });
                }
#endif
                return photos;
            });
        }

#if IOS || MACCATALYST
        // The Photos library's Selfies smart album.
        private static HashSet<string> SelfieIds()
        {
            HashSet<string> ids = new HashSet<string>();
            Photos.PHFetchResult albums = Photos.PHAssetCollection.FetchAssetCollections(
                Photos.PHAssetCollectionType.SmartAlbum, Photos.PHAssetCollectionSubtype.SmartAlbumSelfPortraits, null);
            for (nint i = 0; i < (nint)albums.Count; i++)
            {
                if (!(albums[i] is Photos.PHAssetCollection album))
                    continue;
                Photos.PHFetchResult assets = Photos.PHAsset.FetchAssets(album, null);
                for (nint j = 0; j < (nint)assets.Count; j++)
                    if (assets[j] is Photos.PHAsset asset)
                        ids.Add(asset.LocalIdentifier);
            }
            return ids;
        }
#endif

        /// <summary>
        /// Fill in the details listing doesn't give: on Android the GPS position (with ACCESS_MEDIA_LOCATION) and camera
        /// from the photo's EXIF, and selfies (the EXIF lens model names the front camera); on iOS / Mac Catalyst the
        /// camera from the image's metadata; on Windows the file's image properties. Runs on a background thread; a photo
        /// whose details can't be read keeps what it has.
        /// </summary>
        public static Task LoadDetailsAsync(PhotoItem photo)
        {
            return Task.Run(async () =>
            {
                try
                {
#if ANDROID
                    Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
                    Android.Net.Uri uri = Android.Content.ContentUris.WithAppendedId(
                        Android.Provider.MediaStore.Images.Media.ExternalContentUri, long.Parse(photo.Id, System.Globalization.CultureInfo.InvariantCulture));
                    // Without ACCESS_MEDIA_LOCATION Android hands out photos with their GPS position removed.
                    if (HasMediaLocation())
                        uri = Android.Provider.MediaStore.SetRequireOriginal(uri);
                    using Stream stream = resolver.OpenInputStream(uri);
                    // AndroidX's EXIF reader: Android's own (android.media.ExifInterface) returned an empty LensModel, which
                    // tells selfies apart.
                    AndroidX.ExifInterface.Media.ExifInterface exif = new AndroidX.ExifInterface.Media.ExifInterface(stream);
                    double[] position = exif.GetLatLong();
                    if (position != null && position.Length == 2 && (position[0] != 0 || position[1] != 0))
                    {
                        photo.Latitude = position[0];
                        photo.Longitude = position[1];
                    }
                    string make = exif.GetAttribute(AndroidX.ExifInterface.Media.ExifInterface.TagMake);
                    string model = exif.GetAttribute(AndroidX.ExifInterface.Media.ExifInterface.TagModel);
                    photo.Camera = CameraName(make, model);
                    string lens = exif.GetAttribute("LensModel");
                    if (lens != null && lens.IndexOf("front", StringComparison.OrdinalIgnoreCase) >= 0)
                        photo.Kinds.Add("selfie");
                    // When it was taken, by the camera's clock: MediaStore's date can be the file's (e.g. a copied photo).
                    if (TryParseExifDate(exif.GetAttribute("DateTimeOriginal") ?? exif.GetAttribute("DateTime"), out DateTime taken))
                        photo.Taken = new DateTimeOffset(taken);
#elif IOS || MACCATALYST
                    Photos.PHFetchResult result = Photos.PHAsset.FetchAssetsUsingLocalIdentifiers(new[] { photo.Id }, null);
                    if (result.Count == 0 || !(result[0] is Photos.PHAsset asset))
                        return;
                    Photos.PHImageRequestOptions options = new Photos.PHImageRequestOptions
                    {
                        Synchronous = true,
                        NetworkAccessAllowed = false,
                        Version = Photos.PHImageRequestOptionsVersion.Current
                    };
                    Foundation.NSData data = null;
                    Photos.PHImageManager.DefaultManager.RequestImageDataAndOrientation(asset, options, (imageData, uti, orientation, info) => data = imageData);
                    if (data == null)
                        return;
                    using (data)
                    using (ImageIO.CGImageSource source = ImageIO.CGImageSource.FromData(data))
                    {
                        Foundation.NSDictionary properties = source?.CopyProperties((Foundation.NSDictionary)null, 0);
                        if (properties?["{TIFF}"] is Foundation.NSDictionary tiff)
                            photo.Camera = CameraName(tiff["Make"]?.ToString(), tiff["Model"]?.ToString());
                    }
#elif WINDOWS
                    Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(photo.Id);
                    Windows.Storage.FileProperties.ImageProperties properties = await file.Properties.GetImagePropertiesAsync();
                    if (properties.Latitude.HasValue && properties.Longitude.HasValue)
                    {
                        photo.Latitude = properties.Latitude.Value;
                        photo.Longitude = properties.Longitude.Value;
                    }
                    photo.Camera = CameraName(properties.CameraManufacturer, properties.CameraModel);
                    photo.Width = (int)properties.Width;
                    photo.Height = (int)properties.Height;
                    if (properties.DateTaken.Year > 1601)
                        photo.Taken = properties.DateTaken;
                    if (photo.Id.IndexOf("Screenshot", StringComparison.OrdinalIgnoreCase) >= 0)
                        photo.Kinds.Add("screenshot");
#endif
                }
                catch (Exception)
                {
                }
                // A very wide (or tall) photo is a panorama.
                if (photo.Width > 0 && photo.Height > 0 && Math.Max(photo.Width, photo.Height) >= 2.5 * Math.Min(photo.Width, photo.Height)
                    && !photo.Kinds.Contains("screenshot"))
                    photo.Kinds.Add("panorama");
                await Task.CompletedTask;
            });
        }

        // EXIF dates: "2025:08:02 19:00:00", local time.
        private static bool TryParseExifDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text?.Trim(), "yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out date) && date.Year > 1900;
        }

        // "Google Pixel 8" from make "Google" and model "Pixel 8"; the model alone when it already names the maker.
        private static string CameraName(string make, string model)
        {
            make = make?.Trim().Trim('\0');
            model = model?.Trim().Trim('\0');
            if (string.IsNullOrEmpty(model))
                return string.IsNullOrEmpty(make) ? null : make;
            if (string.IsNullOrEmpty(make) || model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
                return model;
            return make + " " + model;
        }

        /// <summary>
        /// A JPEG of the photo at most maxSize pixels on its longer side, or null if it can't be read (deleted, or only in
        /// iCloud). Runs on a background thread.
        /// </summary>
        /// <summary>
        /// Which of these photos are still in the library, or null if that can't be told (no access to the photos). Runs on
        /// a background thread.
        /// </summary>
        public static Task<HashSet<string>> GetExistingAsync(IReadOnlyCollection<string> ids)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!HasAccess())
                        return null;
                    HashSet<string> existing = new HashSet<string>();
                    if (ids.Count == 0)
                        return existing;
#if ANDROID
                    string selection = "_id IN (" + string.Join(",", ids.Select(_ => "?")) + ")";
                    using (Android.Database.ICursor cursor = Android.App.Application.Context.ContentResolver.Query(
                        Android.Provider.MediaStore.Images.Media.ExternalContentUri, new[] { "_id" }, selection, ids.ToArray(), null))
                    {
                        while (cursor != null && cursor.MoveToNext())
                            existing.Add(cursor.GetLong(0).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
#elif IOS || MACCATALYST
                    Photos.PHFetchResult result = Photos.PHAsset.FetchAssetsUsingLocalIdentifiers(ids.ToArray(), null);
                    for (nint i = 0; i < (nint)result.Count; i++)
                        if (result[i] is Photos.PHAsset asset)
                            existing.Add(asset.LocalIdentifier);
#else
                    foreach (string id in ids)
                        if (File.Exists(id))
                            existing.Add(id);
#endif
                    return existing;
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }

        // Whether the app may read the photos now (the user can take access away in the system settings at any time).
        private static bool HasAccess()
        {
#if ANDROID
            Android.Content.Context context = Android.App.Application.Context;
            foreach (string permission in new[] { "android.permission.READ_MEDIA_IMAGES", "android.permission.READ_MEDIA_VISUAL_USER_SELECTED", "android.permission.READ_EXTERNAL_STORAGE" })
                if (AndroidX.Core.Content.ContextCompat.CheckSelfPermission(context, permission) == Android.Content.PM.Permission.Granted)
                    return true;
            return false;
#elif IOS || MACCATALYST
            Photos.PHAuthorizationStatus status = Photos.PHPhotoLibrary.GetAuthorizationStatus(Photos.PHAccessLevel.ReadWrite);
            return status == Photos.PHAuthorizationStatus.Authorized || status == Photos.PHAuthorizationStatus.Limited;
#else
            return true;
#endif
        }

        public static Task<byte[]> LoadImageAsync(string id, int maxSize)
        {
            return Task.Run(() =>
            {
                try
                {
#if ANDROID
                    Android.Net.Uri uri = Android.Content.ContentUris.WithAppendedId(
                        Android.Provider.MediaStore.Images.Media.ExternalContentUri, long.Parse(id, System.Globalization.CultureInfo.InvariantCulture));
                    using Android.Graphics.Bitmap bitmap = Android.App.Application.Context.ContentResolver.LoadThumbnail(
                        uri, new Android.Util.Size(maxSize, maxSize), null);
                    using MemoryStream stream = new MemoryStream();
                    bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 88, stream);
                    return stream.ToArray();
#elif IOS || MACCATALYST
                    Photos.PHFetchResult result = Photos.PHAsset.FetchAssetsUsingLocalIdentifiers(new[] { id }, null);
                    if (result.Count == 0 || !(result[0] is Photos.PHAsset asset))
                        return null;
                    Photos.PHImageRequestOptions options = new Photos.PHImageRequestOptions
                    {
                        Synchronous = true,
                        DeliveryMode = Photos.PHImageRequestOptionsDeliveryMode.HighQualityFormat,
                        ResizeMode = Photos.PHImageRequestOptionsResizeMode.Fast,
                        NetworkAccessAllowed = false
                    };
                    UIKit.UIImage image = null;
                    Photos.PHImageManager.DefaultManager.RequestImageForAsset(asset, new CoreGraphics.CGSize(maxSize, maxSize),
                        Photos.PHImageContentMode.AspectFit, options, (img, info) => image = img);
                    using Foundation.NSData jpeg = image?.AsJPEG(0.88f);
                    return jpeg?.ToArray();
#else
                    Emgu.CV.Mat mat = ImageUtil.Decode(File.ReadAllBytes(id));
                    if (mat == null)
                        return null;
                    using Emgu.CV.Mat small = ImageUtil.Downscale(mat, maxSize);
                    using Emgu.CV.Util.VectorOfByte buffer = new Emgu.CV.Util.VectorOfByte();
                    Emgu.CV.CvInvoke.Imencode(".jpg", small, buffer);
                    return buffer.ToArray();
#endif
                }
                catch (Exception)
                {
                    return null;
                }
            });
        }
    }
}

#endif
