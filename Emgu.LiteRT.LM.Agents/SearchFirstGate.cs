//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>
/// One embedding at a time on a shared search model, with searches first: a background index doesn't start its next
/// embedding while a search waits or runs, or while <see cref="HoldBackground"/> says so (e.g. while the chat model
/// replies). An embedding already running isn't interrupted, so a search waits at most for that one. A plain
/// <see cref="SemaphoreSlim"/> served the two in turn: on a Galaxy Z Fold 3 a search waited about 7 s behind photo
/// indexing.
/// </summary>
public sealed class SearchFirstGate
{
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly object _lock = new object();
    private int _searches;
    private TaskCompletionSource<bool> _noSearch = Completed();

    /// <summary>
    /// The one-slot semaphore under the gate, for code that only needs to exclude everything else (e.g. releasing the
    /// model): it doesn't go before the searches or after them.
    /// </summary>
    public SemaphoreSlim Semaphore => _gate;

    /// <summary>True while a search waits for the gate or holds it.</summary>
    public bool SearchWaiting
    {
        get { lock (_lock) return _searches > 0; }
    }

    /// <summary>
    /// While true, background work doesn't start an embedding (checked about every quarter second); null for never.
    /// </summary>
    public Func<bool> HoldBackground { get; set; }

    /// <summary>How often <see cref="HoldBackground"/> is checked again while it holds.</summary>
    public TimeSpan HoldPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Take the gate for a search: it goes before any background work waiting. Dispose the result to release it.</summary>
    /// <param name="cancel">Stops waiting</param>
    public async Task<IDisposable> EnterSearchAsync(CancellationToken cancel = default)
    {
        lock (_lock)
        {
            if (_searches++ == 0)
                _noSearch = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            await _gate.WaitAsync(cancel).ConfigureAwait(false);
        }
        catch
        {
            LeaveSearch();
            throw;
        }
        return new Releaser(this, true);
    }

    /// <summary>
    /// Take the gate for one background embedding: waits while a search waits or runs, or <see cref="HoldBackground"/>
    /// holds. Dispose the result to release it.
    /// </summary>
    /// <param name="cancel">Stops waiting</param>
    public async Task<IDisposable> EnterBackgroundAsync(CancellationToken cancel = default)
    {
        while (true)
        {
            Task noSearch;
            lock (_lock)
                noSearch = _noSearch.Task;
            await WaitAsync(noSearch, cancel).ConfigureAwait(false);
            Func<bool> hold = HoldBackground;
            if (hold != null && hold())
            {
                await Task.Delay(HoldPollInterval, cancel).ConfigureAwait(false);
                continue;
            }
            await _gate.WaitAsync(cancel).ConfigureAwait(false);
            lock (_lock)
            {
                // A search that came while this waited for the gate goes first.
                if (_searches == 0)
                    return new Releaser(this, false);
            }
            _gate.Release();
        }
    }

    private void LeaveSearch()
    {
        lock (_lock)
        {
            if (--_searches == 0)
                _noSearch.TrySetResult(true);
        }
    }

    private static async Task WaitAsync(Task task, CancellationToken cancel)
    {
        if (task.IsCompleted || !cancel.CanBeCanceled)
        {
            await task.ConfigureAwait(false);
            return;
        }
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancel.Register(() => cancelled.TrySetCanceled(cancel)))
            await (await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> Completed()
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult(true);
        return done;
    }

    private sealed class Releaser : IDisposable
    {
        private SearchFirstGate _owner;
        private readonly bool _search;

        public Releaser(SearchFirstGate owner, bool search)
        {
            _owner = owner;
            _search = search;
        }

        public void Dispose()
        {
            SearchFirstGate owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null)
                return;
            owner._gate.Release();
            if (_search)
                owner.LeaveSearch();
        }
    }
}
