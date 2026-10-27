// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;

namespace Emgu.LiteRT.LM.Agents.CV;

/// <summary>The take_snapshot action's factory: the capture and its showing belong to the host; binding only checks the parameters.</summary>
/// <param name="capture">Captures a frame for an event</param>
public sealed class CameraSnapshotFactory(Func<VisualEvent, CancellationToken, Task> capture) : IVisualActionFactory
{
    /// <inheritdoc/>
    public VisualActionDescriptor Descriptor => VisualHostCatalog.Snapshot;
    /// <inheritdoc/>
    public VisualActionBinding Bind(JsonElement parameters)
    {
        VisualActionParameters.AllowOnly(parameters);
        return new VisualActionBinding(new CameraSnapshotAction(capture), VisualActionParameters.Empty);
    }
}

/// <summary>The take_snapshot action: the host's capture, within 15 seconds.</summary>
/// <param name="capture">Captures a frame for an event</param>
public sealed class CameraSnapshotAction(Func<VisualEvent, CancellationToken, Task> capture) : IVisualAction
{
    /// <inheritdoc/>
    public string Id => "take_snapshot";
    /// <inheritdoc/>
    public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await capture(occurrence, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new TimeoutException("Snapshot capture exceeded 15 seconds."); }
    }
}
