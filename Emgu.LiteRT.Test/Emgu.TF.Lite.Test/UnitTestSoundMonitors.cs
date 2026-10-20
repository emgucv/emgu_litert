// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Audio;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Sound monitors (TODO item 42): the sound groups, the temporal state machine, the runtime and the chat tools, with synthetic scores.
[TestFixture]
public class UnitTestSoundMonitors
{
    private static readonly string[] Names = { "Dog", "Bark", "Bow-wow", "Howl", "Yip", "Doorbell", "Ding-dong", "Sine wave", "Speech" };

    // 521 labels like YAMNet's, with the ones these tests need in known places.
    private static List<string> Labels()
    {
        var labels = Enumerable.Range(0, 521).Select(i => "Class " + i).ToList();
        for (int i = 0; i < Names.Length; i++) labels[i + 3] = Names[i];
        return labels;
    }

    private static float[] Scores(params (string Label, float Score)[] values)
    {
        var labels = Labels();
        var scores = new float[521];
        foreach (var (label, score) in values) scores[labels.IndexOf(label)] = score;
        return scores;
    }

    private static TimeSpan T(double seconds) => TimeSpan.FromSeconds(seconds);

    [TestCase("doorbell", "doorbell")]
    [TestCase("Tell me when the DOG barks!", "dog")]
    [TestCase("my baby is crying", "baby_cry")]
    [TestCase("the smoke detector goes off", "smoke_alarm")]     // longer than "alarm"
    [TestCase("an alarm clock", "alarm_clock")]
    [TestCase("some alarm", "alarm")]
    [TestCase("someone at the door", "doorbell")]
    [TestCase("glass breaking in the kitchen", "glass_breaking")]
    [TestCase("running water", "running_water")]
    public void TheUsersWordsFindTheirGroup(string said, string id)
    {
        Assert.That(SoundGroups.Find(said)?.Id, Is.EqualTo(id));
    }

    [Test]
    public void AnExactClassNameIsAGroupOfItsOwn()
    {
        Assert.That(SoundGroups.Find("sine wave"), Is.Null);
        var group = SoundGroups.Find("Sine wave", Labels());
        Assert.That(group.Classes, Is.EqualTo(new[] { "Sine wave" }));
        Assert.That(SoundGroups.Find("a unicorn", Labels()), Is.Null);
        Assert.That(SoundGroups.Find(""), Is.Null);
        Assert.That(SoundGroups.All.Select(g => g.Id), Is.Unique);
    }

    [Test]
    public void ASoundStartsAtOnceAndEndsAfterTheReleaseTime()
    {
        var monitor = new SoundPresenceMonitor(0.5, TimeSpan.Zero);
        Assert.That(monitor.Advance(T(0), 0.1), Is.Null);
        var started = monitor.Advance(T(0.5), 0.9, "Doorbell");
        Assert.That(started.Phase, Is.EqualTo(SoundEventPhase.Started));
        Assert.That(started.Label, Is.EqualTo("Doorbell"));
        Assert.That(monitor.Advance(T(1.0), 0.95), Is.Null);       // still the same episode
        Assert.That(monitor.Advance(T(2.0), 0.1), Is.Null);        // quiet, but less than 3 s since the last positive
        Assert.That(monitor.Advance(T(3.5), 0.1), Is.Null);
        Assert.That(monitor.Advance(T(3.9), 0.9), Is.Null);        // rang again: not a new episode
        var ended = monitor.Advance(T(7.0), 0.1);
        Assert.That(ended.Phase, Is.EqualTo(SoundEventPhase.Ended));
        Assert.That(ended.EpisodeId, Is.EqualTo(started.EpisodeId));
        Assert.That(monitor.IsActive, Is.False);
        Assert.That(monitor.Advance(T(8), 0.9).Phase, Is.EqualTo(SoundEventPhase.Started));   // armed again
    }

    [Test]
    public void ASoundThatMustLastIsConfirmedAcrossShortGaps()
    {
        var monitor = new SoundPresenceMonitor(0.5, T(3));
        Assert.That(monitor.Advance(T(0), 0.9), Is.Null);
        Assert.That(monitor.Advance(T(1), 0.1), Is.Null);          // a gap shorter than 2 s continues the run (a dog's barks)
        Assert.That(monitor.Advance(T(1.5), 0.9), Is.Null);
        Assert.That(monitor.Advance(T(2.5), 0.9), Is.Null);
        Assert.That(monitor.Advance(T(2.9), 0.9), Is.Null);
        Assert.That(monitor.Advance(T(3.0), 0.9).Phase, Is.EqualTo(SoundEventPhase.Started));
    }

    [Test]
    public void ALongGapRestartsTheRunAndAnInterruptionResets()
    {
        var monitor = new SoundPresenceMonitor(0.5, T(3));
        monitor.Advance(T(0), 0.9);
        monitor.Advance(T(0.5), 0.1);
        monitor.Advance(T(3), 0.1);                                // 3 s of quiet: the run is over
        Assert.That(monitor.Advance(T(3.5), 0.9), Is.Null);        // a new run starts at 3.5 s
        Assert.That(monitor.Advance(T(5), 0.9), Is.Null);
        Assert.That(monitor.Advance(T(6.5), 0.9).Phase, Is.EqualTo(SoundEventPhase.Started));
        // Listening stopped for a while: nothing is claimed about the gap, and the episode is dropped without an Ended.
        Assert.That(monitor.Advance(T(30), 0.1), Is.Null);
        Assert.That(monitor.IsActive, Is.False);
        // Repeated and earlier times are ignored.
        Assert.That(monitor.Advance(T(30), 0.9), Is.Null);
        Assert.That(monitor.Advance(T(10), 0.9), Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SoundPresenceMonitor(0, T(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SoundPresenceMonitor(0.5, T(-1)));
    }

    private static async Task<bool> WaitAsync(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++) await Task.Delay(10);
        return done();
    }

    [Test]
    public async Task TheRuntimeAlertsOncePerEpisodeAndPassesTheSettings()
    {
        var alerts = new List<(SoundEvent Event, SoundMonitorInfo Info)>();
        using var runtime = new SoundMonitorRuntime(Labels(), (e, info, token) => { lock (alerts) alerts.Add((e, info)); return Task.CompletedTask; });
        var info = runtime.Create("dog", 0.5, 0, new SoundAlert(true, "  Someone is barking  "));
        Assert.That(info.Sound, Is.EqualTo("dog"));
        Assert.That(info.SpeakText, Is.EqualTo("Someone is barking"));
        Assert.That(info.Description, Does.Contain("dog barking").And.Contain("play a chime").And.Contain("Someone is barking"));
        Assert.That(runtime.WantsAudio, Is.True);

        runtime.Advance(T(0), Scores(("Speech", 0.9f)));                       // another sound
        runtime.Advance(T(0.5), Scores(("Bark", 0.8f), ("Dog", 0.6f)));         // the best class of the group counts
        runtime.Advance(T(1.0), Scores(("Bark", 0.9f)));
        Assert.That(await WaitAsync(() => alerts.Count == 1), Is.True);
        Assert.That(alerts[0].Event.Label, Is.EqualTo("Bark"));
        Assert.That(alerts[0].Event.Score, Is.EqualTo(0.8).Within(1e-6));
        Assert.That(alerts[0].Info.Chime, Is.True);
        var after = runtime.Snapshot().Single();
        Assert.That(after.Heard, Is.EqualTo(1));
        Assert.That(after.LastEvent, Does.StartWith("Heard bark at "));

        runtime.Advance(T(10), Scores());                                       // ended: no alert for that
        runtime.Advance(T(11), Scores(("Howl", 0.7f)));                         // a new episode
        Assert.That(await WaitAsync(() => alerts.Count == 2), Is.True);
        Assert.That(runtime.Snapshot().Single().Heard, Is.EqualTo(2));
    }

    [Test]
    public async Task AMonitorWithoutAnAlertOnlyRecordsAndAPausedOneIsDeaf()
    {
        int alerts = 0;
        using var runtime = new SoundMonitorRuntime(Labels(), (e, info, token) => { Interlocked.Increment(ref alerts); return Task.CompletedTask; });
        var quiet = runtime.Create("doorbell");
        runtime.Advance(T(0), Scores(("Doorbell", 0.9f)));
        await Task.Delay(100);
        Assert.That(alerts, Is.Zero);
        Assert.That(runtime.Snapshot().Single().Heard, Is.EqualTo(1));

        runtime.SetEnabled(quiet.Id, false);
        Assert.That(runtime.WantsAudio, Is.False);
        runtime.Advance(T(20), Scores(("Doorbell", 0.9f)));
        Assert.That(runtime.Snapshot().Single().Heard, Is.EqualTo(1));
        runtime.SetEnabled(quiet.Id, true);
        runtime.Advance(T(21), Scores(("Doorbell", 0.9f)));
        Assert.That(runtime.Snapshot().Single().Heard, Is.EqualTo(2));
        runtime.Remove(quiet.Id);
        Assert.That(runtime.Snapshot(), Is.Empty);
        Assert.Throws<ArgumentException>(() => runtime.Remove(quiet.Id));
    }

    [Test]
    public void TheRuntimeValidatesAndDeduplicates()
    {
        using var runtime = new SoundMonitorRuntime(Labels());
        var a = runtime.Create("dog", 0.5, 0, new SoundAlert(true));
        Assert.That(runtime.Create("dog barking", 0.5, 0, new SoundAlert(true)).Id, Is.EqualTo(a.Id));      // the same rule
        Assert.That(runtime.Create("dog", 0.5, 0, new SoundAlert(false)).Id, Is.Not.EqualTo(a.Id));
        var unknown = Assert.Throws<ArgumentException>(() => runtime.Create("a unicorn"));
        Assert.That(unknown.Message, Does.Contain("Unknown sound").And.Contain("doorbell"));
        Assert.Throws<ArgumentException>(() => runtime.Create("dog", 2));
        Assert.Throws<ArgumentException>(() => runtime.Create("dog", 0.5, -1));
        Assert.Throws<ArgumentException>(() => new SoundAlert(false, new string('x', 201)));
        Assert.That(new SoundAlert().IsEmpty, Is.True);
        // A group none of whose classes the model has can't be created.
        using var other = new SoundMonitorRuntime(Enumerable.Range(0, 521).Select(i => "X" + i).ToList());
        Assert.Throws<ArgumentException>(() => other.Create("dog"));
        // At most eight.
        using var full = new SoundMonitorRuntime(Labels());
        foreach (double seconds in new double[] { 0, 1, 2, 3, 4, 5, 6, 7 }) full.Create("dog", 0.5, seconds);
        Assert.Throws<ArgumentException>(() => full.Create("dog", 0.5, 8));
        full.Clear();
        Assert.That(full.Snapshot(), Is.Empty);
        Assert.Throws<ArgumentException>(() => full.Advance(T(0), new float[3]));
    }

    [Test]
    public async Task AFailingAlertIsReportedAndDoesNotStopTheNext()
    {
        int calls = 0;
        using var runtime = new SoundMonitorRuntime(Labels(), (e, info, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("no speaker");
            return Task.CompletedTask;
        });
        runtime.Create("doorbell", 0.5, 0, new SoundAlert(true));
        runtime.Advance(T(0), Scores(("Doorbell", 0.9f)));
        Assert.That(await WaitAsync(() => runtime.Snapshot().Single().LastEvent.StartsWith("The alert failed")), Is.True);
        runtime.Advance(T(10), Scores());
        runtime.Advance(T(11), Scores(("Doorbell", 0.9f)));
        Assert.That(await WaitAsync(() => calls == 2), Is.True);
    }

    [Test]
    public async Task TheToolsAreNamedAndCallTheHost()
    {
        string seen = null;
        var tools = SoundMonitorTools.Create(
            (sound, minimum, play, speak, threshold, token) => { seen = $"{sound}|{minimum}|{play}|{speak}|{threshold}"; return Task.FromResult("ok"); },
            () => "list", (id, operation, token) => Task.FromResult(id + " " + operation));
        Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "create_sound_monitor", "list_sound_monitors", "control_sound_monitor" }));
        var create = (AIFunction)tools[0];
        await create.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object>
            { ["sound"] = "dog", ["minimumSeconds"] = 5.0, ["playSound"] = true, ["speakText"] = "hi", ["threshold"] = 0.0 }));
        Assert.That(seen, Is.EqualTo("dog|5|True|hi|0.5"));                   // 0 means the normal sensitivity
        Assert.That(create.Description, Does.Contain("doorbell").And.Contain("baby_cry"));
        Assert.That(SoundMonitorTools.Describe(new SoundMonitorRuntime(Labels()).Create("dog")), Does.Contain("listening"));
    }
}
