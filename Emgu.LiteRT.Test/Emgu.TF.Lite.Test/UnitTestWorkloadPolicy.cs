using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestWorkloadPolicy
{
    [Test]
    public void LowBatteryRecoversAtTwentyFivePercentOrOnPower()
    {
        var policy = new WorkloadPolicy();
        Assert.That(policy.PauseReason(new(.20, false, false), false), Does.Contain("battery"));
        Assert.That(policy.PauseReason(new(.24, false, false), false), Does.Contain("battery"));
        Assert.That(policy.PauseReason(new(.25, false, false), false), Is.Null);
        Assert.That(policy.PauseReason(new(.21, false, false), false), Is.Null);
        Assert.That(policy.PauseReason(new(.05, true, false), false), Is.Null);
    }

    [Test]
    public void HeatAlwaysPausesEvenWhileChargingOrOnDesktop()
    {
        var policy = new WorkloadPolicy();
        Assert.That(policy.PauseReason(new(.90, true, true), true), Does.Contain("hot"));
        Assert.That(policy.PauseReason(new(-1, false, true, false), false), Does.Contain("hot"));
        Assert.That(policy.PauseReason(new(.90, true, false), true), Is.Null);
    }

    [Test]
    public void ChargingPreferenceIsOptionalAndDoesNotConstrainDesktop()
    {
        var policy = new WorkloadPolicy();
        Assert.That(policy.PauseReason(new(.90, false, false), false), Is.Null);
        Assert.That(policy.PauseReason(new(.90, false, false), true), Does.Contain("charger"));
        Assert.That(policy.PauseReason(new(.90, true, false), true), Is.Null);
        Assert.That(policy.PauseReason(new(0, false, false, false), true), Is.Null);
        Assert.That(policy.PauseReason(new(-1, false, false), false), Is.Null);
    }

    [Test]
    public async Task WaitingReportsChangingReasonsAndResumesWithoutRestartingWork()
    {
        var conditions = new Queue<WorkloadConditions>(new[] {
            new WorkloadConditions(.10, false, true), new(.10, false, true),
            new(.10, false, false), new(.24, false, false), new(.25, false, false) });
        var status = new List<string>();
        int waits = 0;
        await new WorkloadPolicy().WaitAsync(() => Task.FromResult(conditions.Dequeue()), () => false,
            status.Add, CancellationToken.None, _ => { waits++; return Task.CompletedTask; });
        Assert.That(waits, Is.EqualTo(4));
        Assert.That(status.Count, Is.EqualTo(3));
        Assert.That(status[0], Does.Contain("hot"));
        Assert.That(status[1], Does.Contain("battery"));
        Assert.That(status[2], Is.Null);
    }

    [Test]
    public async Task TurningOffChargingPreferenceResumesPendingWork()
    {
        bool chargingOnly = true;
        await new WorkloadPolicy().WaitAsync(() => Task.FromResult(new WorkloadConditions(.80, false, false)),
            () => chargingOnly, null, CancellationToken.None,
            _ => { chargingOnly = false; return Task.CompletedTask; });
        Assert.That(chargingOnly, Is.False);
    }

    [Test]
    public void CancellationWhilePausedClearsStatusAndNeverStartsNextWork()
    {
        using var cancel = new CancellationTokenSource();
        var status = new List<string>();
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await new WorkloadPolicy().WaitAsync(() => Task.FromResult(new WorkloadConditions(.10, false, false)),
                () => false, status.Add, cancel.Token, token =>
                {
                    cancel.Cancel();
                    return Task.Delay(Timeout.Infinite, token);
                }));
        Assert.That(status.Count, Is.EqualTo(2));
        Assert.That(status.Last(), Is.Null);
    }

    [Test]
    public async Task DocumentPauseRetainsEmbeddingsAndDoesNotCallNextBatchUntilRecovery()
    {
        var index = new DocumentIndex();
        for (int i = 0; i < 10; i++) index.AddPassage("passage " + i);
        index.AddImage(new byte[] { 42 }, "image/jpeg");
        var recover = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int batches = 0;
        Task build = index.BuildAsync(async (texts, token) =>
        {
            batches++;
            if (batches == 2)
            {
                paused.SetResult(true);
                await recover.Task.WaitAsync(token);
            }
            return texts.Select(_ => new[] { 1f, 0f }).ToArray();
        }, (image, token) => Task.FromResult(new[] { 0f, 1f }));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(index.Passages.Count(p => p.Embedding != null), Is.EqualTo(8));
        Assert.That(index.IsBuilt, Is.False);
        Assert.That(index.Images[0].Embedding, Is.Null);
        recover.SetResult(true);
        await build.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(index.IsBuilt, Is.True);
        Assert.That(batches, Is.EqualTo(2));
    }

    [Test]
    public async Task DocumentCancellationKeepsCompletedBatchesForRetry()
    {
        var index = new DocumentIndex();
        for (int i = 0; i < 10; i++) index.AddPassage("passage " + i);
        using var cancel = new CancellationTokenSource();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await index.BuildAsync((texts, token) =>
        {
            cancel.Cancel();
            return Task.FromResult(texts.Select(_ => new[] { 1f }).ToArray());
        }, null, cancellationToken: cancel.Token));
        Assert.That(index.Passages.Count(p => p.Embedding != null), Is.EqualTo(8));
        int remaining = 0;
        await index.BuildAsync((texts, token) =>
        {
            remaining += texts.Length;
            return Task.FromResult(texts.Select(_ => new[] { 1f }).ToArray());
        }, null);
        Assert.That(remaining, Is.EqualTo(2));
        Assert.That(index.IsBuilt, Is.True);
    }
}
