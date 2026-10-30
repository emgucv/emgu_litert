// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The search model's gate: searches before background indexing, one embedding at a time.
[TestFixture]
public class UnitTestSearchFirstGate
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Test]
    public async Task ASearchGoesBeforeTheNextBackgroundEmbedding()
    {
        var gate = new SearchFirstGate();
        var order = new List<string>();
        // A background embedding is running; then another background one and a search wait, in that order.
        IDisposable running = await gate.EnterBackgroundAsync();
        Task next = Task.Run(async () =>
        {
            using (await gate.EnterBackgroundAsync())
                lock (order) order.Add("background");
        });
        await Task.Delay(100);
        Task search = Task.Run(async () =>
        {
            using (await gate.EnterSearchAsync())
            {
                lock (order) order.Add("search");
                await Task.Delay(50);
            }
        });
        await Task.Delay(100);
        Assert.That(gate.SearchWaiting, Is.True);
        running.Dispose();
        await Task.WhenAll(next, search).WaitAsync(Timeout);
        Assert.That(order, Is.EqualTo(new[] { "search", "background" }));
        Assert.That(gate.SearchWaiting, Is.False);
    }

    [Test]
    public async Task ARunningEmbeddingFinishesFirst()
    {
        var gate = new SearchFirstGate();
        IDisposable running = await gate.EnterBackgroundAsync();
        Task<IDisposable> search = gate.EnterSearchAsync();
        await Task.Delay(100);
        Assert.That(search.IsCompleted, Is.False);
        running.Dispose();
        (await search.WaitAsync(Timeout)).Dispose();
    }

    [Test]
    public async Task BackgroundWorkWaitsWhileHeld()
    {
        bool replying = true;
        var gate = new SearchFirstGate { HoldBackground = () => replying, HoldPollInterval = TimeSpan.FromMilliseconds(20) };
        Task<IDisposable> background = gate.EnterBackgroundAsync();
        await Task.Delay(150);
        Assert.That(background.IsCompleted, Is.False);
        // A search isn't held.
        (await gate.EnterSearchAsync().WaitAsync(Timeout)).Dispose();
        replying = false;
        (await background.WaitAsync(Timeout)).Dispose();
    }

    [Test]
    public async Task WaitingCanBeCancelled()
    {
        var gate = new SearchFirstGate();
        IDisposable search = await gate.EnterSearchAsync();
        using (var cancel = new CancellationTokenSource(100))
            Assert.CatchAsync<OperationCanceledException>(() => gate.EnterBackgroundAsync(cancel.Token));
        using (var cancel = new CancellationTokenSource(100))
            Assert.CatchAsync<OperationCanceledException>(() => gate.EnterSearchAsync(cancel.Token));
        // The cancelled search no longer counts: background work goes on once the first search is done.
        search.Dispose();
        Assert.That(gate.SearchWaiting, Is.False);
        (await gate.EnterBackgroundAsync().WaitAsync(Timeout)).Dispose();
        // Disposing twice releases once.
        IDisposable again = await gate.EnterSearchAsync();
        again.Dispose();
        again.Dispose();
        Assert.That(gate.Semaphore.CurrentCount, Is.EqualTo(1));
    }
}
