//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Keeps a large model download going when the app leaves the screen, as far as each platform allows, for as long as one
    /// is held (begin it with <see cref="BeginAsync"/>, dispose it when the download is over):
    /// <list type="bullet">
    /// <item>Android: a foreground service (<c>DownloadForegroundService</c>, declared by this library with the permissions it
    /// needs) with a progress notification that can pause the downloads, and a partial wake lock. The first download asks for the
    /// notification permission on Android 13 and later; the download goes on without it.</item>
    /// <item>iOS: a background task, which iOS ends after a short time: the download is then paused.</item>
    /// <item>Mac Catalyst: a background task and a user-initiated activity, so the Mac doesn't nap the app.</item>
    /// <item>Windows: nothing is needed.</item>
    /// </list>
    /// A paused download (its <see cref="Token"/> cancelled) keeps what it received when it is done with Emgu.LiteRT.Util's
    /// <see cref="FileDownloadManager"/>, and continues from there when it is started again.
    /// </summary>
    public sealed class BackgroundDownloads : IAsyncDisposable
    {
#if ANDROID
        /// <summary>What to tell people about downloads on this platform.</summary>
        public const string Hint = "Downloads continue in the background. Allow notifications to see progress and pause them. Reopen the app and choose Continue after an interruption.";
#elif IOS
        /// <summary>What to tell people about downloads on this platform.</summary>
        public const string Hint = "Keep the app open for large downloads. iOS allows limited finishing time in the background; unfinished downloads pause safely. Return and choose Continue to resume.";
#elif MACCATALYST
        /// <summary>What to tell people about downloads on this platform.</summary>
        public const string Hint = "Downloads continue while this app is in the background. Keep your Mac awake and connected; interrupted downloads can be continued.";
#else
        /// <summary>What to tell people about downloads on this platform.</summary>
        public const string Hint = "Interrupted model downloads keep their progress. Choose Continue to resume.";
#endif
        /// <summary>The downloads going on now, which the Android notification shows.</summary>
        public static DownloadActivityRegistry Activities { get; } = new();
        private readonly DownloadActivity _activity;
#if IOS || MACCATALYST
        private nint _backgroundTask = UIKit.UIApplication.BackgroundTaskInvalid;
#endif
#if MACCATALYST
        private Foundation.NSObject _processActivity;
#endif
        /// <summary>Cancelled when the download is cancelled or paused (from the notification, or when the platform's time is up); pass it to the download.</summary>
        public CancellationToken Token => _activity.Token;
        private BackgroundDownloads(DownloadActivity activity) => _activity = activity;

        /// <summary>
        /// Report the progress, for the notification.
        /// </summary>
        /// <param name="total">The size, or null while it isn't known</param>
        /// <param name="received">The bytes received so far</param>
        public void Report(long? total, long received) => _activity.Report(total, received);

        /// <summary>
        /// Start keeping the app going for a download; call it while the app is on the screen (Android only starts a foreground
        /// service from the foreground).
        /// </summary>
        /// <param name="name">What is downloaded, for the notification</param>
        /// <param name="cancel">Cancels the download</param>
        /// <exception cref="InvalidOperationException">The platform wouldn't allow it (no background time, or the service couldn't start)</exception>
        public static async Task<BackgroundDownloads> BeginAsync(string name, CancellationToken cancel)
        {
            var lease = new BackgroundDownloads(Activities.Begin(name, cancel));
            try
            {
#if ANDROID
                await DownloadForegroundService.EnsureStartedAsync();
#elif IOS || MACCATALYST
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    lease._backgroundTask = UIKit.UIApplication.SharedApplication.BeginBackgroundTask("Model download", () =>
                    {
                        lease._activity.Pause();
                        lease.EndAppleTask();
                    });
                    if (lease._backgroundTask == UIKit.UIApplication.BackgroundTaskInvalid)
                        throw new InvalidOperationException("Background download time is unavailable. Return to the app and continue the download.");
#if MACCATALYST
                    lease._processActivity = Foundation.NSProcessInfo.ProcessInfo.BeginActivity(
                        Foundation.NSActivityOptions.UserInitiated, "Downloading a model requested by the user");
#endif
                });
#else
                await Task.CompletedTask;
#endif
                lease.Token.ThrowIfCancellationRequested();
                return lease;
            }
            catch { await lease.DisposeAsync(); throw; }
        }

#if IOS || MACCATALYST
        private void EndAppleTask()
        {
            if (_backgroundTask == UIKit.UIApplication.BackgroundTaskInvalid) return;
            UIKit.UIApplication.SharedApplication.EndBackgroundTask(_backgroundTask);
            _backgroundTask = UIKit.UIApplication.BackgroundTaskInvalid;
        }
#endif

        /// <summary>The download is over: stop keeping the app going for it (the Android service stops with the last one).</summary>
        public async ValueTask DisposeAsync()
        {
            _activity.Dispose();
#if IOS || MACCATALYST
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                EndAppleTask();
#if MACCATALYST
                if (_processActivity != null)
                {
                    Foundation.NSProcessInfo.ProcessInfo.EndActivity(_processActivity);
                    _processActivity.Dispose();
                    _processActivity = null;
                }
#endif
            });
#else
            await Task.CompletedTask;
#endif
        }

        /// <summary>
        /// Download a model's file in the background, unless it is already there and verified. Load the model after this returns,
        /// so the service or background task isn't kept going while it loads.
        /// </summary>
        /// <param name="file">The file</param>
        /// <param name="name">What is downloaded, for the notification</param>
        /// <param name="progress">Told the size (null while unknown), the bytes received and the percentage; may be null</param>
        /// <param name="cancel">Cancels the download</param>
        public static async Task DownloadFileAsync(DownloadableFile file, string name,
            Action<long?, long, double?> progress, CancellationToken cancel)
        {
            if (file.IsLocalFileVerified) return;
            await using var lease = await BeginAsync(name, cancel);
            var manager = new FileDownloadManager();
            manager.AddFile(file);
            manager.OnDownloadProgressChanged += (total, received, percent) =>
            {
                lease.Report(total, received);
                progress?.Invoke(total, received, percent);
            };
            await Task.Run(() => manager.Download(1, lease.Token), lease.Token);
        }
    }
}
