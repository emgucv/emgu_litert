// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;

namespace Maui.Demo.Lite;

// Capture/presentation belongs to the host. Binding only validates configuration.
internal sealed class CameraSnapshotFactory(Func<VisualEvent, CancellationToken, Task> capture) : IVisualActionFactory
{
    public VisualActionDescriptor Descriptor => VisualHostCatalog.Snapshot;
    public VisualActionBinding Bind(JsonElement parameters)
    {
        VisualActionParameters.AllowOnly(parameters);
        return new VisualActionBinding(new CameraSnapshotAction(capture), VisualActionParameters.Empty);
    }
}

internal sealed class CameraSnapshotAction(Func<VisualEvent, CancellationToken, Task> capture) : IVisualAction
{
    public string Id => "take_snapshot";
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
