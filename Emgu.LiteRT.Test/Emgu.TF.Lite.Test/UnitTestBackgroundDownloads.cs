using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public class UnitTestBackgroundDownloads
{
    [Test]
    public void ConcurrentDownloadsRemainTrackedUntilEachFinishes()
    {
        var registry = new DownloadActivityRegistry();
        using var first = registry.Begin("chat", CancellationToken.None);
        using var second = registry.Begin("images", CancellationToken.None);
        first.Report(100, 30);
        second.Report(200, 40);
        Assert.That(registry.Snapshot().Sum(p => p.Received), Is.EqualTo(70));
        first.Dispose();
        first.Report(100, 99); // Late progress must never resurrect a completed transfer.
        first.Dispose();
        Assert.That(registry.Snapshot().Single().Name, Is.EqualTo("images"));
        Assert.That(second.Token.IsCancellationRequested, Is.False);
    }

    [Test]
    public void PauseCancelsEveryActiveDownloadWithoutCancellingItsCaller()
    {
        var registry = new DownloadActivityRegistry();
        using var caller = new CancellationTokenSource();
        using var first = registry.Begin("chat", caller.Token);
        using var second = registry.Begin("search", CancellationToken.None);
        registry.PauseAll();
        Assert.That(first.Token.IsCancellationRequested && second.Token.IsCancellationRequested, Is.True);
        Assert.That(caller.IsCancellationRequested, Is.False);
        Assert.That(registry.Snapshot(), Is.Empty);
        using var resumed = registry.Begin("chat", caller.Token);
        Assert.That(resumed.Token.IsCancellationRequested, Is.False);
        Assert.That(registry.Snapshot().Length, Is.EqualTo(1));
    }

    [Test]
    public void UserCancellationStopsOnlyItsOwnDownload()
    {
        var registry = new DownloadActivityRegistry();
        using var caller = new CancellationTokenSource();
        using var first = registry.Begin("chat", caller.Token);
        using var second = registry.Begin("images", CancellationToken.None);
        caller.Cancel();
        Assert.That(first.Token.IsCancellationRequested, Is.True);
        Assert.That(registry.Snapshot().Single().Name, Is.EqualTo("images"));
        Assert.Throws<OperationCanceledException>(() => registry.Begin("cancelled", caller.Token));
    }

    [Test]
    public async Task PauseCanRaceWithDownloadCompletion()
    {
        var registry = new DownloadActivityRegistry();
        var activities = Enumerable.Range(0, 50).Select(i => registry.Begin(i.ToString(), CancellationToken.None)).ToArray();
        await Task.WhenAll(Task.Run(() => registry.PauseAll()), Task.Run(() =>
        {
            foreach (var activity in activities) { activity.Report(null, 10); activity.Dispose(); }
        }));
        Assert.That(registry.Snapshot(), Is.Empty);
    }

    [Test]
    public void UnknownTotalsStayIndeterminateAndProgressIncludesResumedBytes()
    {
        var registry = new DownloadActivityRegistry();
        using var activity = registry.Begin("resumed", CancellationToken.None);
        activity.Report(null, 512);
        Assert.That(registry.Snapshot().Single().Total, Is.Null);
        activity.Report(1024, 512);
        var snapshot = registry.Snapshot().Single();
        activity.Report(1024, 1024);
        Assert.That(snapshot.Received, Is.EqualTo(512)); // Snapshots are immutable.
        Assert.That(registry.Snapshot().Single().Received, Is.EqualTo(1024));
    }
}
