using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Maui.Demo.Lite;

// Platform-independent lifetime/progress accounting, shared by every model download in the demo.
internal sealed class DownloadActivityRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DownloadActivity> _active = new();
    private DateTime _lastProgress;
    public event Action Changed;

    public DownloadActivity Begin(string name, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var activity = new DownloadActivity(this, name, cancel);
        lock (_gate) _active.Add(activity.Id, activity);
        Changed?.Invoke();
        return activity;
    }

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

    public void PauseAll()
    {
        DownloadActivity[] active;
        lock (_gate) active = _active.Values.ToArray();
        foreach (var activity in active) activity.Pause();
        Changed?.Invoke();
    }
}

internal sealed class DownloadProgress
{
    public string Name { get; }
    public long Received { get; }
    public long? Total { get; }
    public DownloadProgress(string name, long received, long? total) { Name = name; Received = received; Total = total; }
}

internal sealed class DownloadActivity : IDisposable
{
    private readonly DownloadActivityRegistry _registry;
    private readonly CancellationTokenSource _cancel;
    private int _disposed;
    internal Guid Id { get; } = Guid.NewGuid();
    internal string Name { get; }
    internal long Received { get; set; }
    internal long? Total { get; set; }
    public CancellationToken Token { get; }

    internal DownloadActivity(DownloadActivityRegistry registry, string name, CancellationToken cancel)
    {
        _registry = registry;
        Name = name;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        Token = _cancel.Token;
    }

    public void Report(long? total, long received) => _registry.Report(this, total, received);
    public void Pause()
    {
        try { _cancel.Cancel(); } catch (ObjectDisposedException) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registry.Remove(this);
        _cancel.Dispose();
    }
}
