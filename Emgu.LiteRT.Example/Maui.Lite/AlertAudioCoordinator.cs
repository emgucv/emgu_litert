// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;

namespace Maui.Demo.Lite;

// No MAUI dependency: the host supplies speech playback, including its UI-thread dispatch.
// Visual groups reserve audio first; chime and one speech action can then run concurrently.
internal sealed class AlertAudioCoordinator : IVisualActionGroupCoordinator
{
    private readonly Func<string, CancellationToken, Task> _speak;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _groups = new(1, 1);
    private TaskCompletionSource<bool> _changed = Signal();
    private Task _reply;
    private CancellationTokenSource _replyStop, _pendingStop;
    private bool _reserved;

    public AlertAudioCoordinator(Func<string, CancellationToken, Task> speak) => _speak = speak;
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Pulse()
    {
        var previous = _changed; _changed = Signal(); previous.TrySetResult(true);
    }
    public async Task SpeakReplyAsync(string text, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        TaskCompletionSource<bool> completed = null;
        lock (_gate)
        {
            _pendingStop?.Cancel(); // Keep only the newest pending reply.
            _pendingStop = stop;
        }
        try
        {
            while (true)
            {
                Task wait;
                lock (_gate)
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (!_reserved && _reply == null)
                    {
                        completed = Signal(); _reply = completed.Task; _replyStop = stop; _pendingStop = null;
                        break;
                    }
                    wait = _changed.Task;
                }
                await wait.WaitAsync(stop.Token).ConfigureAwait(false);
            }
            stop.Token.ThrowIfCancellationRequested();
            await _speak(text, stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pendingStop, stop)) _pendingStop = null;
                if (ReferenceEquals(_replyStop, stop)) { _replyStop = null; _reply = null; }
                Pulse();
            }
            completed?.TrySetResult(true); // Complete only after native speech has unwound.
        }
    }
    public async Task<IDisposable> EnterAsync(VisualEvent occurrence, CancellationToken token)
    {
        await _groups.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Task reply;
            lock (_gate)
            {
                _reserved = true; reply = _reply; _replyStop?.Cancel();
            }
            if (reply != null) await reply.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new Reservation(this);
        }
        catch { Release(); throw; }
    }
    public async Task SpeakAlertAsync(string text, CancellationToken token)
    {
        lock (_gate)
            if (!_reserved) throw new InvalidOperationException("Alert speech needs an active action-group reservation.");
        token.ThrowIfCancellationRequested();
        await _speak(text, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
    private void Release()
    {
        lock (_gate) { _reserved = false; Pulse(); }
        _groups.Release();
    }
    private sealed class Reservation(AlertAudioCoordinator owner) : IDisposable
    {
        private AlertAudioCoordinator _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
