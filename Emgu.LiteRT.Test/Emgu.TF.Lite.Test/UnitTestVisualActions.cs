// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Microsoft.Extensions.AI;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents.Audio;
using Maui.Demo.Lite;
using NUnit.Framework;
using Emgu.LiteRT.LM.Agents.CV;

namespace Emgu.TF.Lite.Test;

public partial class UnitTestVisualAgents
{
    private sealed class TestAction(string id, Func<CancellationToken, Task> run) : IVisualAction
    {
        public string Id => id;
        public Task ExecuteAsync(VisualEvent occurrence, CancellationToken token) => run(token);
    }
    private sealed class TestFactory(string id, Func<CancellationToken, Task> run = null, bool available = true) : IVisualActionFactory
    {
        public VisualActionDescriptor Descriptor { get; } = new(id, id, "Test action", VisualActionParameters.Parse(id == "speak_text"
            ? "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"maxLength\":200}},\"required\":[\"text\"],\"additionalProperties\":false}"
            : "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"), available);
        public VisualActionBinding Bind(JsonElement parameters)
        {
            VisualActionParameters.AllowOnly(parameters, id == "speak_text" ? new[] { "text" } : Array.Empty<string>());
            var normalized = id == "speak_text" ? VisualActionParameters.Text(VisualActionParameters.RequiredText(parameters, "text", 200)) : VisualActionParameters.Empty;
            return new(new TestAction(id, run ?? (_ => Task.CompletedTask)), normalized);
        }
    }
    private static VisualActionRequest Sound() => new("play_sound", VisualActionParameters.Empty);
    private static VisualActionRequest Speech(string text = "Person detected") => new("speak_text", VisualActionParameters.Text(text));
    private static VisualActionRegistry ActionRegistry(params IVisualActionFactory[] factories)
    {
        var registry = new VisualActionRegistry(); foreach (var factory in factories) registry.Register(factory); return registry;
    }
    private static VisualMonitorRuntime ActionRuntime(VisualActionRegistry actions, IVisualActionGroupCoordinator coordinator = null)
    {
        var agents = new VisualAgentRegistry(); agents.Register(new FakeAgent());
        return new(agents, actions, actionCoordinator: coordinator);
    }
    [Test]
    public async Task ActionSetsValidateBeforeCreatingRulesAndNormalizeDuplicateIdentity()
    {
        var actions = ActionRegistry(new TestFactory("play_sound"), new TestFactory("speak_text"), new TestFactory("offline", available: false));
        var runtime = ActionRuntime(actions);
        var invalid = new[] {
            Array.Empty<VisualActionRequest>(), new[] { Sound(), Sound() }, new[] { new VisualActionRequest("unknown", VisualActionParameters.Empty) },
            new[] { new VisualActionRequest("offline", VisualActionParameters.Empty) }, new[] { Speech("") }, new[] { Speech(new string('x', 201)) },
            new[] { new VisualActionRequest("play_sound", VisualActionParameters.Parse("{\"volume\":1}")) },
            new[] { new VisualActionRequest("speak_text", VisualActionParameters.Parse("{\"text\":\"a\",\"text\":\"b\"}")) },
            new[] { new VisualActionRequest("speak_text", VisualActionParameters.Parse("{\"text\":4}")) }
        };
        foreach (var requests in invalid) Assert.Throws<ArgumentException>(() => runtime.CreateObjectMonitor("person", requests));
        Assert.That(runtime.Snapshot(), Is.Empty);
        var first = runtime.CreateObjectMonitor("person", new[] { Sound(), Speech("  Hello  ") });
        var reversed = runtime.CreateObjectMonitor("person", new[] { Speech("Hello"), Sound() });
        Assert.That(reversed.Id, Is.EqualTo(first.Id));
        Assert.That(runtime.CreateObjectMonitor("person", new[] { Sound(), Speech("Goodbye") }).Id, Is.Not.EqualTo(first.Id));
        Assert.That(first.Actions.Single(a => a.ActionId == "speak_text").Parameters.GetProperty("text").GetString(), Is.EqualTo("Hello"));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task ConcurrentActionsKeepIndependentFailureResults()
    {
        var soundStarted = Signal(); var speechStarted = Signal(); var release = Signal();
        var actions = ActionRegistry(new TestFactory("play_sound", async token => { soundStarted.SetResult(true); await release.Task.WaitAsync(token); throw new InvalidOperationException("speaker failed"); }),
            new TestFactory("speak_text", async token => { speechStarted.SetResult(true); await release.Task.WaitAsync(token); }));
        var runtime = ActionRuntime(actions); runtime.CreateObjectMonitor("person", new[] { Sound(), Speech() }); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await Drain(runtime); runtime.SubmitFrame(Frame(.2));
        await Task.WhenAll(soundStarted.Task, speechStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(runtime.Snapshot().Single().LastActionResults.Select(r => r.State), Is.All.EqualTo(VisualActionState.Running));
        release.SetResult(true); await Drain(runtime);
        var results = runtime.Snapshot().Single().LastActionResults;
        Assert.That(results.Single(r => r.ActionId == "play_sound").State, Is.EqualTo(VisualActionState.Failed));
        Assert.That(results.Single(r => r.ActionId == "speak_text").State, Is.EqualTo(VisualActionState.Succeeded));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task HidingFeedCancelsEveryActionAndPreservesCancelledResults()
    {
        var started = new[] { Signal(), Signal() }; var cancelled = new[] { Signal(), Signal() };
        var actions = ActionRegistry(new[] { "play_sound", "speak_text" }.Select((id, i) => (IVisualActionFactory)new TestFactory(id, async token => {
            started[i].SetResult(true); try { await Task.Delay(Timeout.Infinite, token); } finally { cancelled[i].SetResult(true); }
        })).ToArray());
        var runtime = ActionRuntime(actions); runtime.CreateObjectMonitor("person", new[] { Sound(), Speech() }); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await Drain(runtime); runtime.SubmitFrame(Frame(.2));
        await Task.WhenAll(started.Select(s => s.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        runtime.SetFeedVisible(false); await Drain(runtime);
        Assert.That(cancelled.All(c => c.Task.IsCompleted), Is.True);
        Assert.That(runtime.Snapshot().Single().LastActionResults.Select(r => r.State), Is.All.EqualTo(VisualActionState.Cancelled));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task ExplicitToolSchemaAndJsonArgumentsMatchRegisteredActions()
    {
        var actions = ActionRegistry(new TestFactory("play_sound"), new TestFactory("speak_text"), new TestFactory("offline", available: false));
        VisualActionRequest[] received = null;
        var tools = VisualAgentTools.Create(() => "agents", () => "actions", actions.Describe(),
            (_, _, _, requests, _) => { received = requests; return Task.FromResult("created"); }, () => "[]", (_, _, _) => Task.FromResult("ok"), (_, _, _) => Task.FromResult("ok"));
        var create = tools.OfType<AIFunction>().Single(f => f.Name == "create_visual_monitor");
        var schema = create.JsonSchema.GetProperty("properties").GetProperty("actions");
        Assert.That(schema.GetProperty("items").GetProperty("oneOf").GetArrayLength(), Is.EqualTo(2));
        Assert.That(schema.ToString(), Does.Not.Contain("offline"));
        await create.InvokeAsync(new AIFunctionArguments { ["label"] = "person", ["minimumCount"] = 1, ["confidence"] = .5,
            ["actions"] = VisualActionParameters.Parse("[{\"actionId\":\"speak_text\",\"parameters\":{\"text\":\"Hello\"}}]") });
        Assert.That(received.Single().ActionId, Is.EqualTo("speak_text"));
        Assert.That(received.Single().Parameters.GetProperty("text").GetString(), Is.EqualTo("Hello"));
    }
    [Test]
    public async Task AlertWaitsForInterruptedReplyCleanupAndKeepsOnlyLatestPendingReply()
    {
        var replyStarted = Signal(); var cleanupStarted = Signal(); var finishCleanup = Signal(); var spoken = new List<string>();
        var audio = new AlertAudioCoordinator(async (text, token) => {
            lock (spoken) spoken.Add(text);
            if (text == "reply") {
                replyStarted.SetResult(true);
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { cleanupStarted.SetResult(true); await finishCleanup.Task; }
            }
        });
        var reply = audio.SpeakReplyAsync("reply", CancellationToken.None); await replyStarted.Task;
        var reservation = audio.EnterAsync(null, CancellationToken.None); await cleanupStarted.Task;
        Assert.That(reservation.IsCompleted, Is.False, "Native reply cleanup must finish before alert speech starts.");
        finishCleanup.SetResult(true); using var lease = await reservation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.ThrowsAsync<TaskCanceledException>(async () => await reply);
        var old = audio.SpeakReplyAsync("old pending", CancellationToken.None);
        var latest = audio.SpeakReplyAsync("latest pending", CancellationToken.None);
        await audio.SpeakAlertAsync("alert", CancellationToken.None);
        Assert.That(latest.IsCompleted, Is.False);
        lease.Dispose(); await latest.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.ThrowsAsync<TaskCanceledException>(async () => await old);
        Assert.That(spoken, Is.EqualTo(new[] { "reply", "alert", "latest pending" }));
    }
    [Test]
    public async Task CancellingAudioReservationDoesNotStrandWaitingReplies()
    {
        var started = Signal(); var cleanup = Signal(); var release = Signal();
        var audio = new AlertAudioCoordinator(async (text, token) => {
            if (text != "reply") return;
            started.SetResult(true); try { await Task.Delay(Timeout.Infinite, token); }
            finally { cleanup.SetResult(true); await release.Task; }
        });
        var reply = audio.SpeakReplyAsync("reply", CancellationToken.None); await started.Task;
        using var stop = new CancellationTokenSource(); var reservation = audio.EnterAsync(null, stop.Token); await cleanup.Task;
        stop.Cancel(); Assert.ThrowsAsync<TaskCanceledException>(async () => await reservation);
        var next = audio.SpeakReplyAsync("next", CancellationToken.None); Assert.That(next.IsCompleted, Is.False);
        release.SetResult(true); Assert.ThrowsAsync<TaskCanceledException>(async () => await reply);
        await next.WaitAsync(TimeSpan.FromSeconds(5));
        using var lease = await audio.EnterAsync(null, CancellationToken.None);
    }
    [Test]
    public async Task AudioCancellationIsObservedWhenNativeSpeechReturnsNormally()
    {
        var started = Signal();
        var audio = new AlertAudioCoordinator(async (_, token) => {
            started.TrySetResult(true);
            var cancelled = Signal(); using var registration = token.Register(() => cancelled.TrySetResult(true));
            await cancelled.Task;
        });
        using var stop = new CancellationTokenSource();
        var reply = audio.SpeakReplyAsync("reply", stop.Token); await started.Task; stop.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await reply);
        using var lease = await audio.EnterAsync(null, CancellationToken.None);
        using var alertStop = new CancellationTokenSource(); var alert = audio.SpeakAlertAsync("alert", alertStop.Token);
        alertStop.Cancel(); Assert.ThrowsAsync<OperationCanceledException>(async () => await alert);
    }
    [Test]
    public async Task EventGroupsStayFifoWhileSiblingActionsRunConcurrently()
    {
        var release = Signal(); var firstGroupStarted = Signal(); var secondGroupStarted = Signal();
        int active = 0, calls = 0, completed = 0;
        var actions = ActionRegistry(new[] { "play_sound", "speak_text" }.Select(id => (IVisualActionFactory)new TestFactory(id, async token => {
            int call = Interlocked.Increment(ref calls); Interlocked.Increment(ref active);
            if (call <= 2) { if (call == 2) firstGroupStarted.SetResult(true); await release.Task.WaitAsync(token); }
            else { Assert.That(Volatile.Read(ref completed), Is.GreaterThanOrEqualTo(2)); secondGroupStarted.TrySetResult(true); }
            Interlocked.Decrement(ref active); Interlocked.Increment(ref completed);
        })).ToArray());
        var runtime = ActionRuntime(actions);
        runtime.CreateObjectMonitor("person", new[] { Sound(), Speech() });
        runtime.CreateObjectMonitor("person", new[] { Sound(), Speech() }, confidence: .6);
        runtime.SetFeedVisible(true); runtime.SubmitFrame(Frame(0)); await Drain(runtime); runtime.SubmitFrame(Frame(.2));
        await firstGroupStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(active, Is.EqualTo(2)); Assert.That(calls, Is.EqualTo(2)); Assert.That(secondGroupStarted.Task.IsCompleted, Is.False);
        release.SetResult(true); await Drain(runtime); Assert.That(completed, Is.EqualTo(4)); await runtime.DisposeAsync();
    }
    private static VisualActionRequest SnapshotAction() => new("take_snapshot", VisualActionParameters.Empty);
    [Test]
    public async Task SnapshotFactoryValidatesBeforeCaptureAndCapturesOncePerEpisode()
    {
        var events = new List<VisualEvent>();
        var factory = new CameraSnapshotFactory((occurrence, token) => { token.ThrowIfCancellationRequested(); events.Add(occurrence); return Task.CompletedTask; });
        var runtime = ActionRuntime(ActionRegistry(factory));
        Assert.Throws<ArgumentException>(() => runtime.CreateObjectMonitor("person", new[] { new VisualActionRequest("take_snapshot", VisualActionParameters.Text("invalid")) }));
        Assert.That(runtime.Snapshot(), Is.Empty); Assert.That(events, Is.Empty);
        var rule = runtime.CreateObjectMonitor("person", new[] { SnapshotAction() });
        Assert.That(runtime.CreateObjectMonitor("person", new[] { SnapshotAction() }).Id, Is.EqualTo(rule.Id));
        Assert.That(events, Is.Empty, "Binding must not capture.");
        runtime.SetFeedVisible(true);
        foreach (double seconds in new[] { 0, .2, .4, .6 }) { runtime.SubmitFrame(Frame(seconds)); await Drain(runtime); }
        Assert.That(events.Count, Is.EqualTo(1)); Assert.That(events[0].MonitorId, Is.EqualTo(rule.Id));
        Assert.That(events[0].Evidence.SourceId, Is.EqualTo("camera"));
        Assert.That(runtime.Snapshot().Single().LastActionResults.Single().State, Is.EqualTo(VisualActionState.Succeeded));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task SnapshotFailureDoesNotCancelAudioSiblings()
    {
        var actions = ActionRegistry(new TestFactory("play_sound"), new TestFactory("speak_text"),
            new CameraSnapshotFactory((_, _) => throw new InvalidOperationException("Camera capture unavailable")));
        var runtime = ActionRuntime(actions); runtime.CreateObjectMonitor("person", new[] { Sound(), Speech(), SnapshotAction() }); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await Drain(runtime); runtime.SubmitFrame(Frame(.2)); await Drain(runtime);
        var results = runtime.Snapshot().Single().LastActionResults;
        Assert.That(results.Single(r => r.ActionId == "take_snapshot").State, Is.EqualTo(VisualActionState.Failed));
        Assert.That(results.Where(r => r.ActionId != "take_snapshot").Select(r => r.State), Is.All.EqualTo(VisualActionState.Succeeded));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task SnapshotCancellationPreventsLatePresentation()
    {
        var started = Signal(); bool presented = false;
        var factory = new CameraSnapshotFactory(async (_, token) => {
            started.SetResult(true); await Task.Delay(Timeout.Infinite, token); token.ThrowIfCancellationRequested(); presented = true;
        });
        var runtime = ActionRuntime(ActionRegistry(factory)); runtime.CreateObjectMonitor("person", new[] { SnapshotAction() }); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await Drain(runtime); runtime.SubmitFrame(Frame(.2)); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtime.SetFeedVisible(false); await Drain(runtime);
        Assert.That(presented, Is.False); Assert.That(runtime.Snapshot().Single().LastActionResults.Single().State, Is.EqualTo(VisualActionState.Cancelled));
        await runtime.DisposeAsync();
    }
    [Test]
    public async Task SnapshotOnlyGroupLeavesReadAloudPlaying()
    {
        var speaking = Signal(); var stopReply = Signal(); bool captured = false;
        var audio = new AlertAudioCoordinator(async (_, token) => { speaking.SetResult(true); await stopReply.Task.WaitAsync(token); }, _ => false);
        var reply = audio.SpeakReplyAsync("reply", CancellationToken.None); await speaking.Task;
        var runtime = ActionRuntime(ActionRegistry(new CameraSnapshotFactory((_, _) => { captured = true; return Task.CompletedTask; })), audio);
        runtime.CreateObjectMonitor("person", new[] { SnapshotAction() }); runtime.SetFeedVisible(true);
        runtime.SubmitFrame(Frame(0)); await Drain(runtime); runtime.SubmitFrame(Frame(.2)); await Drain(runtime);
        Assert.That(captured, Is.True); Assert.That(reply.IsCompleted, Is.False);
        stopReply.SetResult(true); await reply; await runtime.DisposeAsync();
    }
#if TEST_MODELS
    [TestCase("When you see a person, play a sound.", true, false, false)]
    [TestCase("When you see a person, say 'Person detected'.", false, true, false)]
    [TestCase("When you see a person, play a sound and say 'Person detected'.", true, true, false)]
    [TestCase("When you see a person, take a snapshot.", false, false, true)]
    [TestCase("When you see a person, take a snapshot, play a sound and say 'Person detected'.", true, true, true)]
    public async Task GemmaSelectsExplicitVisualActions(string request, bool sound, bool speech, bool snapshot)
    {
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await model.Init(configureSettings: settings => { settings.NumThreads = 4; settings.MaxNumTokens = 4096; }, cancellationToken: stop.Token);
        var actions = ActionRegistry(new TestFactory("play_sound"), new TestFactory("speak_text"), new CameraSnapshotFactory((_, _) => Task.CompletedTask));
        var runtime = ActionRuntime(actions); int agentListings = 0, actionListings = 0, creations = 0;
        var tools = VisualAgentTools.Create(() => { agentListings++; return "Agent test supports object_detection with person, cat; live camera and images."; },
            () => { actionListings++; return JsonSerializer.Serialize(actions.Describe()); }, actions.Describe(),
            (label, count, confidence, selected, token) => {
                Assert.That(agentListings, Is.GreaterThan(0)); Assert.That(actionListings, Is.GreaterThan(0));
                creations++; return Task.FromResult(JsonSerializer.Serialize(runtime.CreateObjectMonitor(label, selected, count, confidence)));
            }, () => "[]", (_, _, _) => throw new InvalidOperationException("Unexpected control"), (_, _, _) => throw new InvalidOperationException("Unexpected image"));
        try {
            using var client = new FunctionInvokingChatClient(model.AsIChatClient()) { MaximumIterationsPerRequest = 6 };
            var response = await client.GetResponseAsync(request, new ChatOptions { Instructions = VisualAgentTools.Instructions, Tools = tools,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } }, stop.Token);
            TestContext.Progress.WriteLine(request + " => " + response.Text);
            foreach (var message in response.Messages) TestContext.Progress.WriteLine(message.Role + ": " + string.Join(" | ", message.Contents.Select(c => c.ToString())));
            Assert.That(creations, Is.EqualTo(1)); var selected = runtime.Snapshot().Single().Actions;
            Assert.That(selected.Select(a => a.ActionId), Is.EquivalentTo((sound ? new[] { "play_sound" } : Array.Empty<string>()).Concat(speech ? new[] { "speak_text" } : Array.Empty<string>()).Concat(snapshot ? new[] { "take_snapshot" } : Array.Empty<string>())));
            if (speech) Assert.That(selected.Single(a => a.ActionId == "speak_text").Parameters.GetProperty("text").GetString(), Is.EqualTo("Person detected"));
            Assert.That(response.Text, Is.Not.Empty);
        } finally { await runtime.DisposeAsync(); }
    }
#endif

}
