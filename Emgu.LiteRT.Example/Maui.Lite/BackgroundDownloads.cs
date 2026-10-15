using Emgu.LiteRT.Util;

namespace Maui.Demo.Lite;

internal sealed class BackgroundDownloads : IAsyncDisposable
{
#if ANDROID
    internal const string Hint = "Downloads continue in the background. Allow notifications to see progress and pause them. Reopen the app and choose Continue after an interruption.";
#elif IOS
    internal const string Hint = "Keep the app open for large downloads. iOS allows limited finishing time in the background; unfinished downloads pause safely. Return and choose Continue to resume.";
#elif MACCATALYST
    internal const string Hint = "Downloads continue while this app is in the background. Keep your Mac awake and connected; interrupted downloads can be continued.";
#else
    internal const string Hint = "Interrupted model downloads keep their progress. Choose Continue to resume.";
#endif
    internal static DownloadActivityRegistry Activities { get; } = new();
    private readonly DownloadActivity _activity;
#if IOS || MACCATALYST
    private nint _backgroundTask = UIKit.UIApplication.BackgroundTaskInvalid;
#endif
#if MACCATALYST
    private Foundation.NSObject _processActivity;
#endif
    public CancellationToken Token => _activity.Token;
    private BackgroundDownloads(DownloadActivity activity) => _activity = activity;
    public void Report(long? total, long received) => _activity.Report(total, received);

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

    // Download first, then let Init load the verified local model without keeping a service/background task alive.
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
