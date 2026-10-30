//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

#if ANDROID
// The videos (READ_MEDIA_VIDEO on Android 13+; READ_EXTERNAL_STORAGE before 13 is declared by PhotoLibrary), for the
// app's manifest.
[assembly: Android.App.UsesPermission("android.permission.READ_MEDIA_VIDEO")]
#endif

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// Read access to the device's videos, for "Search my videos" (<see cref="VideoLibraryIndex"/>): the newest ones, and
/// each opened for its frames and sound. Android: MediaStore (READ_MEDIA_VIDEO), ids are content:// URIs; iOS and Mac
/// Catalyst: PhotoKit, only videos on the device (no iCloud downloads), ids are "phasset:" + the local identifier;
/// Windows: the Videos folder and its subfolders, ids are paths.
/// </summary>
public sealed class VideoLibrary : IVideoLibrarySource
{
    /// <summary>The prefix of a Photos library video's id on iOS and Mac Catalyst.</summary>
    public const string PhotoKitPrefix = "phasset:";

    /// <summary>Whether this platform has a video library to search.</summary>
    public static bool IsSupported => VideoSource.IsSupported;

    /// <summary>Ask for access to the videos (the system asks the user the first time). Call on the UI thread.</summary>
    public static async Task<bool> RequestAccessAsync()
    {
#if WINDOWS
        await Task.CompletedTask;
        return true;
#elif ANDROID
        PermissionStatus status = await Permissions.CheckStatusAsync<ReadVideosPermission>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<ReadVideosPermission>();
        // Android 14+ also lets the user allow only some videos: MediaStore then returns those.
        return status == PermissionStatus.Granted || OperatingSystem.IsAndroidVersionAtLeast(34);
#elif IOS || MACCATALYST
        PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Photos>();
        if (status != PermissionStatus.Granted && status != PermissionStatus.Limited)
            status = await Permissions.RequestAsync<Permissions.Photos>();
        return status == PermissionStatus.Granted || status == PermissionStatus.Limited;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

#if ANDROID
    // READ_MEDIA_VIDEO on Android 13+, READ_EXTERNAL_STORAGE before.
    private sealed class ReadVideosPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            OperatingSystem.IsAndroidVersionAtLeast(33)
                ? new[] { ("android.permission.READ_MEDIA_VIDEO", true) }
                : new[] { ("android.permission.READ_EXTERNAL_STORAGE", true) };
    }
#endif

    /// <inheritdoc/>
    public Task<IReadOnlyList<LibraryVideo>> GetRecentVideosAsync(int max)
    {
        return Task.Run<IReadOnlyList<LibraryVideo>>(() =>
        {
            var videos = new List<LibraryVideo>();
            int limit = max > 0 ? max : int.MaxValue;
#if ANDROID
            Android.Content.ContentResolver resolver = Android.App.Application.Context.ContentResolver;
            Android.Net.Uri collection = Android.Provider.MediaStore.Video.Media.ExternalContentUri;
            string[] projection = { "_id", "datetaken", "date_modified", "date_added", "duration", "_display_name" };
            using (Android.Database.ICursor cursor = resolver.Query(collection, projection, null, null, "date_added DESC"))
            {
                while (cursor != null && cursor.MoveToNext() && videos.Count < limit)
                {
                    long taken = cursor.GetLong(1);
                    videos.Add(new LibraryVideo
                    {
                        Id = Android.Content.ContentUris.WithAppendedId(collection, cursor.GetLong(0)).ToString(),
                        Taken = taken > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(taken) : DateTimeOffset.FromUnixTimeSeconds(cursor.GetLong(3)),
                        Version = cursor.GetLong(2),
                        DurationMs = cursor.GetLong(4),
                        Name = cursor.GetString(5)
                    });
                }
            }
#elif IOS || MACCATALYST
            var options = new Photos.PHFetchOptions
            {
                SortDescriptors = new[] { new Foundation.NSSortDescriptor("creationDate", false) },
                FetchLimit = (nuint)Math.Max(0, max)
            };
            Photos.PHFetchResult result = Photos.PHAsset.FetchAssets(Photos.PHAssetMediaType.Video, options);
            for (nint i = 0; i < (nint)result.Count && videos.Count < limit; i++)
            {
                if (!(result[i] is Photos.PHAsset asset))
                    continue;
                videos.Add(new LibraryVideo
                {
                    Id = PhotoKitPrefix + asset.LocalIdentifier,
                    Taken = asset.CreationDate != null ? new DateTimeOffset((DateTime)asset.CreationDate) : DateTimeOffset.MinValue,
                    Version = asset.ModificationDate != null ? (long)asset.ModificationDate.SecondsSinceReferenceDate : 0,
                    DurationMs = (long)(asset.Duration * 1000),
                    Name = Photos.PHAssetResource.GetAssetResources(asset).FirstOrDefault()?.OriginalFilename ?? "Video"
                });
            }
#elif WINDOWS
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            string[] types = { ".mp4", ".mov", ".m4v", ".avi", ".wmv", ".mkv" };
            if (Directory.Exists(folder))
            {
                IEnumerable<FileInfo> files = new DirectoryInfo(folder)
                    .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(f => types.Contains(f.Extension.ToLowerInvariant()));
                foreach (FileInfo file in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(limit))
                    videos.Add(new LibraryVideo { Id = file.FullName, Taken = file.LastWriteTime, Version = file.LastWriteTimeUtc.Ticks, Name = file.Name });
            }
#endif
            return videos;
        });
    }

    /// <inheritdoc/>
    public async Task<ILibraryVideoReader> OpenAsync(string id, CancellationToken cancel)
    {
        VideoSource source = await OpenSourceAsync(id, cancel);
        return source == null ? null : new Reader(source);
    }

    /// <summary>Open a library video for its frames and sound; null if it isn't on the device.</summary>
    /// <param name="id">The video's id (<see cref="LibraryVideo.Id"/>)</param>
    /// <param name="cancel">Stops waiting for the Photos library</param>
    public static async Task<VideoSource> OpenSourceAsync(string id, CancellationToken cancel = default)
    {
#if IOS || MACCATALYST
        AVFoundation.AVAsset asset = await RequestAssetAsync(id, cancel);
        return asset == null ? null : await VideoSource.OpenAsync(asset, id);
#else
        try
        {
            return await VideoSource.OpenAsync(id);
        }
        catch (IOException)
        {
            return null;
        }
#endif
    }

#if IOS || MACCATALYST
    /// <summary>A Photos library video as an AVAsset (only if it is on the device), or null.</summary>
    /// <param name="id">The video's id ("phasset:" + its local identifier)</param>
    /// <param name="cancel">Stops waiting</param>
    public static async Task<AVFoundation.AVAsset> RequestAssetAsync(string id, CancellationToken cancel = default)
    {
        Photos.PHAsset asset = FetchAsset(id);
        if (asset == null)
            return null;
        var done = new TaskCompletionSource<AVFoundation.AVAsset>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new Photos.PHVideoRequestOptions
        {
            NetworkAccessAllowed = false,
            Version = Photos.PHVideoRequestOptionsVersion.Current,
            DeliveryMode = Photos.PHVideoRequestOptionsDeliveryMode.Automatic
        };
        Photos.PHImageManager.DefaultManager.RequestAVAsset(asset, options, (avAsset, mix, info) => done.TrySetResult(avAsset));
        using (cancel.Register(() => done.TrySetCanceled(cancel)))
            return await done.Task;
    }

    /// <summary>A Photos library video ready to play, or null.</summary>
    /// <param name="id">The video's id</param>
    public static Task<AVFoundation.AVPlayerItem> RequestPlayerItemAsync(string id)
    {
        Photos.PHAsset asset = FetchAsset(id);
        if (asset == null)
            return Task.FromResult<AVFoundation.AVPlayerItem>(null);
        var done = new TaskCompletionSource<AVFoundation.AVPlayerItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new Photos.PHVideoRequestOptions { NetworkAccessAllowed = false, Version = Photos.PHVideoRequestOptionsVersion.Current };
        Photos.PHImageManager.DefaultManager.RequestPlayerItem(asset, options, (item, info) => done.TrySetResult(item));
        return done.Task;
    }

    private static Photos.PHAsset FetchAsset(string id)
    {
        if (id == null || !id.StartsWith(PhotoKitPrefix, StringComparison.Ordinal))
            return null;
        Photos.PHFetchResult result = Photos.PHAsset.FetchAssetsUsingLocalIdentifiers(new[] { id.Substring(PhotoKitPrefix.Length) }, null);
        return result.Count > 0 ? result[0] as Photos.PHAsset : null;
    }
#endif

    /// <summary>Whether an id is a library video's (not a file of the app's): a content:// URI or a Photos library asset.</summary>
    public static bool IsLibraryId(string id) =>
        id != null && (id.StartsWith(PhotoKitPrefix, StringComparison.Ordinal) || id.StartsWith("content://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Copy a library video to a file (for the full moment search of one video): a copy of a content:// video on
    /// Android, an export of a Photos library video on iOS and Mac Catalyst, the file itself on Windows. Null if it
    /// can't be read.
    /// </summary>
    /// <param name="id">The video's id</param>
    /// <param name="folder">Where to put the copy</param>
    /// <param name="cancel">Stops it</param>
    public static async Task<string> ExportAsync(string id, string folder, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(folder);
#if ANDROID
        string target = Path.Combine(folder, "library_video_" + Guid.NewGuid().ToString("N") + ".mp4");
        return await Task.Run(() =>
        {
            using Stream input = Android.App.Application.Context.ContentResolver.OpenInputStream(Android.Net.Uri.Parse(id));
            if (input == null)
                return null;
            using (FileStream output = File.Create(target))
                input.CopyTo(output);
            return target;
        }, cancel);
#elif IOS || MACCATALYST
        AVFoundation.AVAsset asset = await RequestAssetAsync(id, cancel);
        if (asset == null)
            return null;
        string target = Path.Combine(folder, "library_video_" + Guid.NewGuid().ToString("N") + ".mov");
        using var export = new AVFoundation.AVAssetExportSession(asset, AVFoundation.AVAssetExportSessionPreset.Passthrough)
        {
            OutputUrl = Foundation.NSUrl.FromFilename(target),
            OutputFileType = AVFoundation.AVFileTypesExtensions.GetConstant(AVFoundation.AVFileTypes.QuickTimeMovie)
        };
        using (cancel.Register(() => export.CancelExport()))
            await export.ExportTaskAsync();
        return export.Status == AVFoundation.AVAssetExportSessionStatus.Completed ? target : null;
#else
        await Task.CompletedTask;
        return File.Exists(id) ? id : null;
#endif
    }

    // A library video for VideoLibraryIndex: its frames, and its sound read once (up to the indexed part) and sliced.
    private sealed class Reader : ILibraryVideoReader
    {
        private readonly VideoSource _source;
        private Task<short[]> _audio;

        public Reader(VideoSource source) => _source = source;

        public long DurationMs => _source.DurationMs;
        public bool HasAudio => _source.HasAudio;

        public Task<byte[]> FrameAtAsync(long ms, int maxSize, CancellationToken cancel) => _source.FrameAtAsync(ms, maxSize);

        public async Task<short[]> ReadAudioAsync(long fromMs, long toMs, CancellationToken cancel)
        {
            if (!HasAudio)
                return null;
            const long Indexed = (long)VideoLibraryIndex.StrideMs * VideoLibraryIndex.MaxWindows + VideoLibraryIndex.WindowMs;
            _audio ??= _source.ReadAudioAsync(cancel, Indexed);
            short[] pcm = await _audio;
            int from = (int)Math.Min(pcm.Length, fromMs * 16);
            int to = (int)Math.Min(pcm.Length, toMs * 16);
            if (to <= from)
                return null;
            var slice = new short[to - from];
            Array.Copy(pcm, from, slice, 0, slice.Length);
            return slice;
        }

        public void Dispose() => _source.Dispose();
    }
}
