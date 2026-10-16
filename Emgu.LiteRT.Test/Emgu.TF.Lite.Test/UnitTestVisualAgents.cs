// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.LM.Extensions.AI;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture]
public partial class UnitTestVisualAgents
{
    private static VisualFrame Frame(double seconds, Action release = null, string source = "camera") =>
        new(new byte[3], 1, 1, 3, source, (long)(seconds * 1000), DateTimeOffset.UnixEpoch.AddSeconds(seconds), TimeSpan.FromSeconds(seconds), release);
    private static VisualAnalysis Analysis(double seconds, int people = 1, double score = 0.9, string label = "person")
    {
        using var frame = Frame(seconds);
        return new VisualAnalysis("test", frame, Enumerable.Range(0, people).Select(_ => new VisualObservation("object", label, score)));
    }
    private static VisualPresenceMonitor Presence() => new(new ObjectCountCondition("person"));
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Drain(VisualMonitorRuntime runtime) => await runtime.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));

    [Test]
    public void PresenceDebouncesBriefMissesAndEmitsOneStartPerEpisode()
    {
        var monitor = Presence();
        Assert.That(monitor.Advance(Analysis(0)), Is.Null);
        var first = monitor.Advance(Analysis(.2));
        Assert.That(first.Phase, Is.EqualTo(VisualEventPhase.Started));
        Assert.That(monitor.Advance(Analysis(.4)), Is.Null);
        Assert.That(monitor.Advance(Analysis(.6, 0)), Is.Null);
        Assert.That(monitor.Advance(Analysis(.8)), Is.Null);
        var updated = monitor.Advance(Analysis(1, 2));
        Assert.That(updated.Phase, Is.EqualTo(VisualEventPhase.Updated));
        Assert.That(updated.EpisodeId, Is.EqualTo(first.EpisodeId));
        Assert.That(monitor.Advance(Analysis(1.2, 0)), Is.Null);
        Assert.That(monitor.Advance(Analysis(2, 0)), Is.Null);
        var ended = monitor.Advance(Analysis(2.8, 0));
        Assert.That(ended.Phase, Is.EqualTo(VisualEventPhase.Ended));
        Assert.That(ended.EpisodeId, Is.EqualTo(first.EpisodeId));
        Assert.That(monitor.Advance(Analysis(3)), Is.Null);
        var second = monitor.Advance(Analysis(3.2));
        Assert.That(second.Phase, Is.EqualTo(VisualEventPhase.Started));
        Assert.That(second.EpisodeId, Is.Not.EqualTo(first.EpisodeId));
    }
    [Test]
    public void ConfirmationRequiresTwoFreshPositivesAndIgnoresOutOfOrderResults()
    {
        var monitor = Presence();
        Assert.That(monitor.Advance(Analysis(0)), Is.Null);
        Assert.That(monitor.Advance(Analysis(0)), Is.Null);
        Assert.That(monitor.Advance(Analysis(1.2)), Is.Null);
        Assert.That(monitor.Advance(Analysis(.8)), Is.Null);
        Assert.That(monitor.Advance(Analysis(1.4)).Phase, Is.EqualTo(VisualEventPhase.Started));
    }
    [Test]
    public void UnknownGapResetsConfirmationWithoutClaimingPersonLeft()
    {
        var monitor = Presence();
        monitor.Advance(Analysis(0)); monitor.Advance(Analysis(.2));
        Assert.That(monitor.Advance(Analysis(3, 0)), Is.Null);
        Assert.That(monitor.IsPresent, Is.False);
        Assert.That(monitor.Advance(Analysis(3.2)), Is.Null);
        Assert.That(monitor.Advance(Analysis(3.4)).Phase, Is.EqualTo(VisualEventPhase.Started));
    }
    [Test]
    public void ObjectPredicateFiltersClassConfidenceAndMinimumCount()
    {
        var predicate = new ObjectCountCondition(" PERSON ", 2, .7);
        Assert.That(predicate.Count(Analysis(0, 3, .69)), Is.Zero);
        Assert.That(predicate.Count(Analysis(0, 3, .9, "cat")), Is.Zero);
        Assert.That(predicate.Matches(predicate.Count(Analysis(0, 1))), Is.False);
        Assert.That(predicate.Matches(predicate.Count(Analysis(0, 2))), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectCountCondition("person", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectCountCondition("person", confidence: double.NaN));
    }
    [Test]
    public void ResetStartsFreshEpisodeInsteadOfEmittingEnded()
    {
        var monitor = Presence(); monitor.Advance(Analysis(0)); var first = monitor.Advance(Analysis(.2));
        monitor.Reset();
        Assert.That(monitor.Advance(Analysis(.4)), Is.Null);
        Assert.That(monitor.Advance(Analysis(.6)).EpisodeId, Is.Not.EqualTo(first.EpisodeId));
    }
    [Test]
    public void FrameValidatesStrideAndReleasesOnlyOnce()
    {
        Assert.Throws<ArgumentException>(() => new VisualFrame(new byte[3], 2, 1, 3, "camera", 1, default, default));
        int released = 0;
        var frame = Frame(0, () => released++); frame.Dispose(); frame.Dispose();
        Assert.That(released, Is.EqualTo(1));
    }

    private sealed class FakeAgent : IVisualAgent
    {
        public bool Available = true;
        public VisualAgentDescriptor Descriptor => new("test", "Test detector", new[] { "object_detection" }, new[] { "person", "cat" }, available: Available);
        public Func<CancellationToken, Task> Prepare;
        public Func<VisualFrame, CancellationToken, Task<VisualAnalysis>> Analyze;
        public List<long> Analyzed = new();
        public int Disposals;
        public Task PrepareAsync(Action<string> progress, CancellationToken token) => Prepare?.Invoke(token) ?? Task.CompletedTask;
        public async Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken token)
        {
            Analyzed.Add(frame.FrameId);
            return Analyze != null ? await Analyze(frame, token) :
                new VisualAnalysis("test", frame, new[] { new VisualObservation("object", "person", .9) });
        }
        public void Dispose() => Disposals++;
    }
    private sealed class FakeAction : IVisualAction
    {
        public string Id => "play_sound";
        public int Effects;
        public Func<CancellationToken, Task> BeforeEffect;
        public bool Fail;
        public async Task ExecuteAsync(VisualEvent occurrence, CancellationToken token)
        {
            if (BeforeEffect != null) await BeforeEffect(token);
            token.ThrowIfCancellationRequested();
            if (Fail) throw new InvalidOperationException("speaker unavailable");
            Effects++;
        }
    }
    private static VisualMonitorRuntime Runtime(FakeAgent agent, FakeAction action)
    {
        var registry = new VisualAgentRegistry(); registry.Register(agent);
        return new VisualMonitorRuntime(registry, new[] { action });
    }
    [Test]
    public async Task UnavailableAdaptersCannotBeSelectedButAreStillDisposed()
    {
        var agent = new FakeAgent { Available = false }; var runtime = Runtime(agent, new FakeAction());
        Assert.Throws<ArgumentException>(() => runtime.CreateObjectMonitor("person"));
        await runtime.DisposeAsync(); Assert.That(agent.Disposals, Is.EqualTo(1));
    }
    [Test]
    public async Task DuplicateRequestsDoNotDuplicateRulesOrSounds()
    {
        var agent = new FakeAgent(); var sound = new FakeAction(); var runtime = Runtime(agent, sound);
        var rule = runtime.CreateObjectMonitor("person");
        Assert.That(runtime.CreateObjectMonitor("person").Id, Is.EqualTo(rule.Id));
        Assert.Throws<ArgumentException>(() => runtime.CreateObjectMonitor("face"));
        Assert.Throws<ArgumentException>(() => runtime.CreateObjectMonitor("person", actionId: "email"));
        runtime.SetFeedVisible(true);
        foreach (double time in new[] { 0, .2, .4, .6 }) { runtime.SubmitFrame(Frame(time)); await Drain(runtime); }
        Assert.That(sound.Effects, Is.EqualTo(1));
        Assert.That(runtime.Snapshot().Single().State, Is.EqualTo(VisualMonitorState.Watching));
        await runtime.DisposeAsync(); Assert.That(agent.Disposals, Is.EqualTo(1));
    }
    [Test]
    public async Task LatestFrameReplacesPendingAndAllOwnedBuffersAreReleased()
    {
        var entered = Signal(); var release = Signal(); int disposed = 0;
        var agent = new FakeAgent { Prepare = async token => { entered.TrySetResult(true); await release.Task.WaitAsync(token); } };
        var runtime = Runtime(agent, new FakeAction()); runtime.CreateObjectMonitor("person"); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0, () => Interlocked.Increment(ref disposed)));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        foreach (double time in new[] { .1, .2, .4, .6 }) runtime.SubmitFrame(Frame(time, () => Interlocked.Increment(ref disposed)));
        release.SetResult(true); await Drain(runtime);
        Assert.That(agent.Analyzed, Is.EqualTo(new long[] { 0, 600 }));
        Assert.That(disposed, Is.EqualTo(5));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task HiddenFeedCancelsPreparationAndCannotDispatchStaleResults()
    {
        var entered = Signal(); var release = Signal();
        var agent = new FakeAgent { Prepare = async _ => { entered.TrySetResult(true); await release.Task; } };
        var sound = new FakeAction(); var runtime = Runtime(agent, sound); runtime.CreateObjectMonitor("person"); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.SetFeedVisible(false); release.SetResult(true); await Drain(runtime);
        Assert.That(agent.Analyzed, Is.Empty);
        Assert.That(sound.Effects, Is.Zero);
        Assert.That(runtime.Snapshot().Single().State, Is.EqualTo(VisualMonitorState.Paused));
        runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(.2)); await Drain(runtime); Assert.That(sound.Effects, Is.Zero);
        runtime.SubmitFrame(Frame(.4)); await Drain(runtime); Assert.That(sound.Effects, Is.EqualTo(1));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task ExplicitPauseResumeRejectsInferenceFromBeforePause()
    {
        var entered = Signal(); var release = Signal();
        var agent = new FakeAgent { Analyze = async (frame, _) => { entered.TrySetResult(true); await release.Task; return Analysis(frame.Timestamp.TotalSeconds); } };
        var sound = new FakeAction(); var runtime = Runtime(agent, sound); var id = runtime.CreateObjectMonitor("person").Id; runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.SetEnabled(id, false); runtime.SetEnabled(id, true); release.SetResult(true); await Drain(runtime);
        runtime.SubmitFrame(Frame(.2)); await Drain(runtime); Assert.That(sound.Effects, Is.Zero);
        runtime.SubmitFrame(Frame(.4)); await Drain(runtime); Assert.That(sound.Effects, Is.EqualTo(1));
        runtime.SetEnabled(id, false); runtime.SetFeedVisible(false); runtime.SetFeedVisible(true);
        Assert.That(runtime.WantsFrames, Is.False);
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task FrameQueuedBeforeResumeCannotSeedNewPresenceConfirmation()
    {
        var entered = Signal(); var release = Signal();
        var agent = new FakeAgent { Prepare = async _ => { entered.TrySetResult(true); await release.Task; } };
        var sound = new FakeAction(); var runtime = Runtime(agent, sound);
        var id = runtime.CreateObjectMonitor("person").Id; runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.SubmitFrame(Frame(.2)); // Queued while the older inference owns the worker.
        runtime.SetEnabled(id, false); runtime.SetEnabled(id, true);
        release.SetResult(true); await Drain(runtime);
        runtime.SubmitFrame(Frame(.4)); await Drain(runtime);
        Assert.That(sound.Effects, Is.Zero, "The queued pre-resume frame is not a fresh positive.");
        runtime.SubmitFrame(Frame(.6)); await Drain(runtime); Assert.That(sound.Effects, Is.EqualTo(1));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task HidingFeedSuppressesInFlightActionAndQueuedActions()
    {
        var entered = Signal(); var release = Signal();
        var sound = new FakeAction { BeforeEffect = async _ => { entered.TrySetResult(true); await release.Task; } };
        var runtime = Runtime(new FakeAgent(), sound);
        runtime.CreateObjectMonitor("person"); runtime.CreateObjectMonitor("person", confidence: .6); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await Drain(runtime);
        runtime.SubmitFrame(Frame(.2)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.SetFeedVisible(false); release.SetResult(true); await Drain(runtime);
        Assert.That(sound.Effects, Is.Zero);
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task DetectorFaultIsNotAbsenceAndRequiresExplicitRetry()
    {
        var agent = new FakeAgent();
        var runtime = Runtime(agent, new FakeAction()); var id = runtime.CreateObjectMonitor("person").Id; runtime.SetFeedVisible(true);
        var events = new List<VisualEvent>(); runtime.EventOccurred += events.Add;
        runtime.SubmitFrame(Frame(0)); await Drain(runtime);
        runtime.SubmitFrame(Frame(.2)); await Drain(runtime);
        agent.Analyze = (_, _) => throw new InvalidOperationException("camera input failed");
        runtime.SubmitFrame(Frame(.4)); await Drain(runtime);
        Assert.That(runtime.Snapshot().Single().State, Is.EqualTo(VisualMonitorState.Faulted));
        Assert.That(events.Select(e => e.Phase), Is.EqualTo(new[] { VisualEventPhase.Started }));
        runtime.SetFeedVisible(false); runtime.SetFeedVisible(true); Assert.That(runtime.WantsFrames, Is.False);
        agent.Analyze = null; runtime.SetEnabled(id, true);
        runtime.SubmitFrame(Frame(.6)); await Drain(runtime);
        Assert.That(runtime.Snapshot().Single().State, Is.EqualTo(VisualMonitorState.Watching));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task ActionFailureIsReportedWithoutRetryOrFaultingDetector()
    {
        var sound = new FakeAction { Fail = true }; var runtime = Runtime(new FakeAgent(), sound);
        runtime.CreateObjectMonitor("person"); runtime.SetFeedVisible(true);
        foreach (double time in new[] { 0, .2, .4 }) { runtime.SubmitFrame(Frame(time)); await Drain(runtime); }
        Assert.That(runtime.Snapshot().Single().LastAction, Does.Contain("speaker unavailable"));
        Assert.That(runtime.Snapshot().Single().State, Is.EqualTo(VisualMonitorState.Watching));
        sound.Fail = false; runtime.SubmitFrame(Frame(.6)); await Drain(runtime); Assert.That(sound.Effects, Is.Zero);
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task SingleImageAnalysisDoesNotAdvanceMonitorsOrPlaySound()
    {
        var sound = new FakeAction(); var runtime = Runtime(new FakeAgent(), sound); runtime.CreateObjectMonitor("person");
        using var frame = Frame(0, source: "image-1");
        var result = await runtime.AnalyzeImageAsync("test", frame, CancellationToken.None);
        Assert.That(result.SourceId, Is.EqualTo("image-1")); Assert.That(result.Observations.Single().Label, Is.EqualTo("person"));
        Assert.That(sound.Effects, Is.Zero); Assert.That(runtime.Snapshot().Single().LastEvent, Is.Null);
        runtime.Remove(runtime.Snapshot().Single().Id); Assert.That(runtime.Snapshot(), Is.Empty);
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task ClearDiscardsRulesAndOldInferencesEvenIfEquivalentRuleIsRecreated()
    {
        var entered = Signal(); var release = Signal();
        var agent = new FakeAgent { Prepare = async _ => { entered.TrySetResult(true); await release.Task; } };
        var sound = new FakeAction(); var runtime = Runtime(agent, sound); var old = runtime.CreateObjectMonitor("person").Id; runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.Clear(); var fresh = runtime.CreateObjectMonitor("person").Id;
        Assert.That(fresh, Is.Not.EqualTo(old)); release.SetResult(true); await Drain(runtime);
        runtime.SubmitFrame(Frame(.2)); await Drain(runtime); Assert.That(sound.Effects, Is.Zero);
        runtime.SubmitFrame(Frame(.4)); await Drain(runtime); Assert.That(sound.Effects, Is.EqualTo(1));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task VisualToolSchemasForwardParametersAndCancellation()
    {
        var observed = new List<string>(); using var stop = new CancellationTokenSource();
        var tools = VisualAgentTools.Create(() => "capabilities", (label, count, score, token) =>
        { Assert.That(token, Is.EqualTo(stop.Token)); observed.Add($"{label}:{count}:{score}"); return Task.FromResult("created"); },
            () => "monitors", (id, operation, token) => { observed.Add(operation); return Task.FromResult(id); },
            (agent, image, token) => Task.FromResult(agent + ":" + image));
        var functions = tools.Cast<AIFunction>().ToDictionary(f => f.Name);
        Assert.That(functions.Count, Is.EqualTo(5));
        var schema = functions["create_visual_monitor"].JsonSchema.GetRawText();
        Assert.That(schema, Does.Contain("minimumCount").And.Contain("confidence"));
        await functions["create_visual_monitor"].InvokeAsync(new AIFunctionArguments { ["label"] = "person", ["minimumCount"] = 1, ["confidence"] = .5 }, stop.Token);
        Assert.That(observed.Single(), Does.StartWith("person:1:"));
        var imageResult = await functions["analyze_visual_image"].InvokeAsync(new AIFunctionArguments { ["agentId"] = "test", ["imageId"] = "image-1" });
        Assert.That(imageResult?.ToString(), Is.EqualTo("test:image-1"));
    }

    [Test]
    public async Task ModelAccessIsSerializedAcrossStillImagesAndLiveFrames()
    {
        var entered = Signal(); var release = Signal(); int inFlight = 0, maximum = 0;
        var agent = new FakeAgent { Analyze = async (frame, token) =>
        {
            maximum = Math.Max(maximum, Interlocked.Increment(ref inFlight)); entered.TrySetResult(true);
            await release.Task.WaitAsync(token); Interlocked.Decrement(ref inFlight);
            return new VisualAnalysis("test", frame, Array.Empty<VisualObservation>());
        }};
        var runtime = Runtime(agent, new FakeAction()); runtime.CreateObjectMonitor("person"); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var image = Frame(0, source: "image-1");
        var analysis = runtime.AnalyzeImageAsync("test", image, CancellationToken.None);
        Assert.That(analysis.IsCompleted, Is.False);
        release.SetResult(true); await analysis.WaitAsync(TimeSpan.FromSeconds(5)); await Drain(runtime);
        Assert.That(maximum, Is.EqualTo(1));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task WrongSourceAndDisabledFeedRejectAndReleaseFrames()
    {
        int released = 0; var agent = new FakeAgent(); var runtime = Runtime(agent, new FakeAction());
        runtime.CreateObjectMonitor("person"); runtime.SubmitFrame(Frame(0, () => released++));
        runtime.SetFeedVisible(true); runtime.SubmitFrame(Frame(.2, () => released++, "other-camera"));
        await Drain(runtime);
        Assert.That(agent.Analyzed, Is.Empty); Assert.That(released, Is.EqualTo(2));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var image = Frame(0, source: "image-1");
        Assert.CatchAsync<OperationCanceledException>(async () => await runtime.AnalyzeImageAsync("test", image, cancel.Token));
        await runtime.DisposeAsync();
    }

#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; requires cached Gemma 4 E2B.")]
#endif
    [Test, Category("AgentRegression")]
    public async Task GemmaSelectsVisualMonitorForPersonAlert()
    {
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await model.Init(configureSettings: s => { s.NumThreads = 4; s.MaxNumTokens = 4096; }, cancellationToken: cancel.Token);
        var registry = new VisualAgentRegistry(); registry.Register(new FakeAgent());
        var sound = new FakeAction(); var runtime = new VisualMonitorRuntime(registry, new[] { sound });
        int listings = 0, creations = 0;
        var tools = VisualAgentTools.Create(() => { listings++; return "Agent test: object_detection; supported classes person, cat; live frames and images."; },
            (label, count, confidence, token) =>
            {
                Assert.That(listings, Is.GreaterThan(0));
                Assert.That(label, Is.EqualTo("person")); Assert.That(count, Is.EqualTo(1)); Assert.That(confidence, Is.EqualTo(.5));
                var rule = runtime.CreateObjectMonitor(label, count, confidence); creations++;
                return Task.FromResult(System.Text.Json.JsonSerializer.Serialize(rule));
            }, () => "[]", (_, _, _) => throw new InvalidOperationException("Unexpected control call"),
            (_, _, _) => throw new InvalidOperationException("Unexpected still-image call"));
        try
        {
            using var client = new FunctionInvokingChatClient(model.AsIChatClient())
                { MaximumIterationsPerRequest = 4 };
            var response = await client.GetResponseAsync("When you see a person, play a sound.", new ChatOptions
            {
                Instructions = VisualAgentTools.LegacyInstructions, Tools = tools,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
            }, cancel.Token);
            TestContext.Progress.WriteLine(response.Text);
            TestContext.Progress.WriteLine($"Visual tool calls: listings={listings}, creations={creations}, finish={response.FinishReason}");
            foreach (var message in response.Messages) TestContext.Progress.WriteLine(message.Role + ": " + string.Join(" | ", message.Contents.Select(c => c.ToString())));
            Assert.That(creations, Is.EqualTo(1)); Assert.That(runtime.Snapshot().Single().Condition, Does.Contain("person"));
            Assert.That(response.Text, Is.Not.Empty); Assert.That(sound.Effects, Is.Zero, "Planning must not execute the action before an observed event.");
        }
        finally { await runtime.DisposeAsync(); }
    }
}
