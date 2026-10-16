// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Maui.Demo.Lite;
using System.Text.Json;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture, NonParallelizable]
public class UnitTestContextManagement
{
    private static bool TrimOldestExchange(List<ChatMessage> messages) =>
        (bool)typeof(LiteRtLmChatClient).GetMethod("TrimOldestExchange", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { messages });

    [Test]
    public void CompactionRemovesWholeOlderExchangesWithoutSplittingCurrentTools()
    {
        var current = new ChatMessage(ChatRole.User, "When a person appears, take a picture");
        var call = new ChatMessage(ChatRole.Assistant, new AIContent[] { new FunctionCallContent("current", "create_visual_monitor", new Dictionary<string, object>()) });
        var result = new ChatMessage(ChatRole.Tool, new AIContent[] { new FunctionResultContent("current", "Monitor already created") });
        var messages = new List<ChatMessage> {
            new(ChatRole.System, "Instructions retained separately"), new(ChatRole.User, "Earlier request"),
            new(ChatRole.Assistant, new AIContent[] { new FunctionCallContent("old", "calculate", new Dictionary<string, object>()) }),
            new(ChatRole.Tool, new AIContent[] { new FunctionResultContent("old", "2") }), new(ChatRole.Assistant, "2"), current, call, result };
        Assert.That(TrimOldestExchange(messages), Is.True);
        Assert.That(messages, Is.EqualTo(new[] { current, call, result }));
        Assert.That(TrimOldestExchange(messages), Is.False, "The current request and completed side effects cannot be discarded.");
        Assert.That(messages, Is.EqualTo(new[] { current, call, result }));
    }
    [Test]
    public void CompactionDoesNotMistakeMediaToolResultsForNewUserRequests()
    {
        var mediaResult = new FunctionResultContent("capture", new AIContent[] { new TextContent("Captured"), new DataContent(new byte[] { 1, 2 }, "image/jpeg") });
        var messages = new List<ChatMessage> { new(ChatRole.User, "Describe the camera"),
            new(ChatRole.Assistant, new AIContent[] { new FunctionCallContent("capture", "capture_camera_frame", new Dictionary<string, object>()) }),
            new(ChatRole.Tool, new AIContent[] { mediaResult }) };
        Assert.That(TrimOldestExchange(messages), Is.False);
        Assert.That(messages.Last().Contents.Single(), Is.SameAs(mediaResult));
    }
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; requires cached Gemma 4 E2B.")]
#endif
    [TestCase(4096), TestCase(8192)]
    public async Task OversizedCurrentRequestFailsBeforeAnyToolCanExecute(int configuredCapacity)
    {
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await model.Init(configureSettings: s => { s.EnableBenchmark(); s.NumThreads = 4; s.MaxNumTokens = configuredCapacity; }, cancellationToken: stop.Token);
        using var native = new LiteRtLmChatClient(model) { EnableContextManagement = true };
        var diagnostics = new List<LiteRtLmContextInfo>(); native.ContextChanged += diagnostics.Add;
        int effects = 0;
        using var client = new FunctionInvokingChatClient(native);
        var options = new ChatOptions { Tools = new List<AITool> { AIFunctionFactory.Create(() => { effects++; return "done"; }, "effect") },
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium } };
        Assert.ThrowsAsync<LiteRtLmContextException>(async () => await client.GetResponseAsync(string.Concat(Enumerable.Repeat("Long current request. ", 4000)), options, stop.Token));
        Assert.That(effects, Is.Zero);
        Assert.That(diagnostics.First().Capacity, Is.EqualTo(configuredCapacity), "Use loaded engine settings, including configureSettings overrides.");
        var response = await client.GetResponseAsync("Say hello briefly.", new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } }, stop.Token);
        Assert.That(response.Text, Is.Not.Empty, "A rejected request must leave the client usable.");
    }
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; requires cached Gemma 4 E2B.")]
#endif
    [Test]
    public async Task OlderHistoryCanCompactWhileCurrentToolRunsOnlyOnceWithThinkingOn()
    {
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await model.Init(configureSettings: s => { s.EnableBenchmark(); s.NumThreads = 4; s.MaxNumTokens = 4096; }, cancellationToken: stop.Token);
        using var native = new LiteRtLmChatClient(model) { EnableContextManagement = true };
        var diagnostics = new List<LiteRtLmContextInfo>(); native.ContextChanged += diagnostics.Add;
        int effects = 0;
        using var client = new FunctionInvokingChatClient(native) { MaximumIterationsPerRequest = 4 };
        var messages = new List<ChatMessage> { new(ChatRole.User, string.Concat(Enumerable.Repeat("Old irrelevant history. ", 2000))), new(ChatRole.Assistant, "Earlier answer"), new(ChatRole.User, "Call record_alert once, then acknowledge success in one sentence.") };
        var response = await client.GetResponseAsync(messages, new ChatOptions {
            Tools = new List<AITool> { AIFunctionFactory.Create(() => { effects++; return "Alert recorded successfully. Write a short acknowledgement."; }, "record_alert") },
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Medium } }, stop.Token);
        TestContext.Progress.WriteLine(response.Text); foreach (var info in diagnostics) TestContext.Progress.WriteLine(info);
        Assert.That(effects, Is.EqualTo(1)); Assert.That(response.Text, Is.Not.Empty);
        Assert.That(diagnostics.Any(d => d.Stage == "compact"), Is.True);
        Assert.That(diagnostics.Where(d => d.OutputBudget > 0 && (d.Stage == "prepare" || d.Stage == "compact")).All(d => d.ThinkingBudget > 0), Is.True);
    }
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; requires cached Gemma 4 E2B.")]
#endif
    [Test]
    public async Task NativeOutputLimitIsNotMisreportedAsSuccessfulEmptyStop()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        await model.Init(configureSettings: s => { s.EnableBenchmark(); s.NumThreads = 4; }, cancellationToken: stop.Token);
        using var native = new LiteRtLmChatClient(model) { EnableContextManagement = true };
        var diagnostics = new List<LiteRtLmContextInfo>(); native.ContextChanged += diagnostics.Add;
        ChatResponse response = null;
        try { response = await native.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Count from one to one hundred.") },
            new ChatOptions { MaxOutputTokens = 1, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } }, stop.Token); }
        catch (LiteRtLmContextException) { /* A limit with no text must surface actionable failure. */ }
        var completion = diagnostics.Single(d => d.Stage == "complete");
        Assert.That(completion.Completion, Is.EqualTo("length"));
        Assert.That(completion.DecodedTokens, Is.GreaterThanOrEqualTo(1));
        if (response != null) { Assert.That(response.FinishReason, Is.EqualTo(ChatFinishReason.Length)); Assert.That(response.Text, Is.Not.Empty); }
    }

    private sealed class CatalogDetector : IVisualAgent
    {
        public VisualAgentDescriptor Descriptor => VisualHostCatalog.Detector();
        public Task PrepareAsync(Action<string> progress, CancellationToken token) => Task.CompletedTask;
        public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken token) => throw new NotSupportedException("Planner fixture has no camera feed.");
        public void Dispose() { }
    }
    private sealed class CatalogAction(VisualActionDescriptor descriptor) : IVisualActionFactory, IVisualAction
    {
        public VisualActionDescriptor Descriptor => descriptor;
        public string Id => descriptor.Id;
        public int Effects;
        public VisualActionBinding Bind(JsonElement parameters)
        {
            VisualActionParameters.AllowOnly(parameters, Id == "speak_text" ? new[] { "text" } : Array.Empty<string>());
            return new(this, Id == "speak_text" ? VisualActionParameters.Text(VisualActionParameters.RequiredText(parameters, "text", 200)) : VisualActionParameters.Empty);
        }
        public Task ExecuteAsync(VisualEvent occurrence, CancellationToken token) { Effects++; return Task.CompletedTask; }
    }
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; requires cached Gemma 4 E2B.")]
#endif
    [TestCase("when you see a person, play a sound and take a picture", true, false)]
    [TestCase("when you see a person, play a sound and take a picture", false, false)]
    [TestCase("When you see a person, take a snapshot, play a sound and say 'Person detected'.", true, true)]
    public async Task FullDemoPlannerCatalogCreatesAlertAt4k(string prompt, bool thinking, bool speech)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        await model.Init(configureSettings: s => { s.EnableBenchmark(); s.NumThreads = 4; s.MaxNumTokens = 4096; }, cancellationToken: stop.Token);
        var detector = new CatalogDetector(); var agents = new VisualAgentRegistry(); agents.Register(detector);
        var actions = new[] { new CatalogAction(VisualHostCatalog.Chime), new CatalogAction(VisualHostCatalog.Speech), new CatalogAction(VisualHostCatalog.Snapshot) };
        var catalog = new VisualActionRegistry(); foreach (var action in actions) catalog.Register(action);
        var runtime = new VisualMonitorRuntime(agents, catalog); int creations = 0, unrelatedCalls = 0;
        Task<string> Unexpected(string request, CancellationToken token) { unrelatedCalls++; throw new InvalidOperationException("Unrelated tool: " + request); }
        var tools = ChatAgentTools.SessionTools(false, null, Unexpected, Unexpected, Unexpected);
        tools.Add(AgentTools.OpenCamera(_ => Task.FromResult("Live feed is visible.")));
        tools.Add(AgentTools.CaptureCameraFrame(_ => throw new InvalidOperationException("No direct camera capture requested.")));
        foreach (var tool in VisualAgentTools.Create(() => JsonSerializer.Serialize(new[] { detector.Descriptor }),
            () => JsonSerializer.Serialize(catalog.Describe()), catalog.Describe(),
            (label, count, confidence, selected, token) => {
                creations++; var rule = runtime.CreateObjectMonitor(label, selected, count, confidence);
                runtime.SetFeedVisible(true); return Task.FromResult(VisualHostCatalog.MonitorResult(runtime.Snapshot().Single()));
            }, () => JsonSerializer.Serialize(runtime.Snapshot()), (_, _, _) => throw new InvalidOperationException("Unexpected monitor control"), (_, _, _) => throw new InvalidOperationException("Unexpected image analysis"))) tools.Add(tool);
        using var native = new LiteRtLmChatClient(model) { EnableContextManagement = true };
        var diagnostics = new List<LiteRtLmContextInfo>(); native.ContextChanged += diagnostics.Add;
        using var client = new FunctionInvokingChatClient(native) { MaximumIterationsPerRequest = 6 };
        try {
            var response = await client.GetResponseAsync(prompt, new ChatOptions { Tools = tools,
                Instructions = string.Join("\n\n", ChatPlannerInstructions.Agent, ChatPlannerInstructions.Photos, ChatPlannerInstructions.ChatSearch,
                    ChatPlannerInstructions.Images, ChatPlannerInstructions.Camera, VisualAgentTools.Instructions),
                Reasoning = new ReasoningOptions { Effort = thinking ? ReasoningEffort.Medium : ReasoningEffort.None } }, stop.Token);
            TestContext.Progress.WriteLine(response.Text); foreach (var info in diagnostics) TestContext.Progress.WriteLine(info);
            Assert.That(creations, Is.EqualTo(1)); Assert.That(unrelatedCalls, Is.Zero); Assert.That(response.Text, Is.Not.Empty);
            Assert.That(runtime.Snapshot().Single().Actions.Select(a => a.ActionId), Is.EquivalentTo(speech ? new[] { "play_sound", "take_snapshot", "speak_text" } : new[] { "play_sound", "take_snapshot" }));
            Assert.That(actions.Sum(a => a.Effects), Is.Zero, "Planning must not execute alerts before a detection event.");
            Assert.That(diagnostics.Where(d => d.OutputBudget > 0).All(d => d.InputTokens + d.OutputBudget <= d.Capacity), Is.True);
        } finally { await runtime.DisposeAsync(); }
    }

}
