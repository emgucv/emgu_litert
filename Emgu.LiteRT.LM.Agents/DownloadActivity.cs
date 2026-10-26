//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>
/// The model downloads going on now, with their progress, for whatever shows them (Emgu.LiteRT.LM.Maui's <c>BackgroundDownloads</c>
/// keeps one, and its Android notification reads it) and a way to pause them all. It has no platform code; it is thread-safe.
/// </summary>
public sealed class DownloadActivityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DownloadActivity> _active = new();
    private DateTime _lastProgress;

    /// <summary>
    /// Raised when a download begins, ends or is paused, and for progress at most once a second (and when one completes); on the
    /// thread that caused it.
    /// </summary>
    public event Action Changed;

    /// <summary>
    /// Begin tracking a download.
    /// </summary>
    /// <param name="name">What is downloaded, as people see it (e.g. "Gemma 4 E2B")</param>
    /// <param name="cancel">Cancels the download; the activity's <see cref="DownloadActivity.Token"/> follows it</param>
    /// <returns>The activity: report progress on it, pass its token to the download, and dispose it when the download is over</returns>
    public DownloadActivity Begin(string name, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var activity = new DownloadActivity(this, name, cancel);
        lock (_gate) _active.Add(activity.Id, activity);
        Changed?.Invoke();
        return activity;
    }

    /// <summary>
    /// The downloads going on now (not those paused or cancelled).
    /// </summary>
    public DownloadProgress[] Snapshot()
    {
        lock (_gate) return _active.Values.Where(a => !a.Token.IsCancellationRequested)
            .Select(a => new DownloadProgress(a.Name, a.Received, a.Total)).ToArray();
    }

    internal void Report(DownloadActivity activity, long? total, long received)
    {
        bool notify;
        lock (_gate)
        {
            if (!_active.ContainsKey(activity.Id)) return;
            activity.Received = Math.Max(0, received);
            activity.Total = total > 0 ? total : null;
            notify = DateTime.UtcNow - _lastProgress >= TimeSpan.FromSeconds(1)
                || (total.HasValue && received >= total.Value);
            if (notify) _lastProgress = DateTime.UtcNow;
        }
        if (notify) Changed?.Invoke();
    }

    internal void Remove(DownloadActivity activity)
    {
        lock (_gate) _active.Remove(activity.Id);
        Changed?.Invoke();
    }

    /// <summary>
    /// Pause every download: cancel their tokens. A download that keeps its partial file (Emgu.LiteRT.Util's
    /// <c>FileDownloadManager</c>) continues from there when it is started again.
    /// </summary>
    public void PauseAll()
    {
        DownloadActivity[] active;
        lock (_gate) active = _active.Values.ToArray();
        foreach (var activity in active) activity.Pause();
        Changed?.Invoke();
    }
}

/// <summary>
/// One download's progress, as <see cref="DownloadActivityRegistry.Snapshot"/> saw it.
/// </summary>
public sealed class DownloadProgress
{
    /// <summary>What is downloaded.</summary>
    public string Name { get; }
    /// <summary>The bytes received so far.</summary>
    public long Received { get; }
    /// <summary>The size, or null while it isn't known.</summary>
    public long? Total { get; }

    /// <summary>
    /// Create a progress reading.
    /// </summary>
    /// <param name="name">What is downloaded</param>
    /// <param name="received">The bytes received so far</param>
    /// <param name="total">The size, or null while it isn't known</param>
    public DownloadProgress(string name, long received, long? total) { Name = name; Received = received; Total = total; }
}

/// <summary>
/// A download tracked by a <see cref="DownloadActivityRegistry"/>, from <see cref="DownloadActivityRegistry.Begin"/> until it is disposed.
/// </summary>
public sealed class DownloadActivity : IDisposable
{
    private readonly DownloadActivityRegistry _registry;
    private readonly CancellationTokenSource _cancel;
    private int _disposed;
    internal Guid Id { get; } = Guid.NewGuid();
    internal string Name { get; }
    internal long Received { get; set; }
    internal long? Total { get; set; }

    /// <summary>Cancelled when the download is cancelled or paused; pass it to the download.</summary>
    public CancellationToken Token { get; }

    internal DownloadActivity(DownloadActivityRegistry registry, string name, CancellationToken cancel)
    {
        _registry = registry;
        Name = name;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        Token = _cancel.Token;
    }

    /// <summary>
    /// Report the progress.
    /// </summary>
    /// <param name="total">The size, or null (or 0) while it isn't known</param>
    /// <param name="received">The bytes received so far</param>
    public void Report(long? total, long received) => _registry.Report(this, total, received);

    /// <summary>Pause the download: cancel its <see cref="Token"/>.</summary>
    public void Pause()
    {
        try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Stop tracking the download.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registry.Remove(this);
        _cancel.Dispose();
    }
}
