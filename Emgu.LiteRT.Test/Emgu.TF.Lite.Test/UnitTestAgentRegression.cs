// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

[TestFixture, NonParallelizable]
public class UnitTestAgentRegression
{
    private const string Instructions = "You are a helpful assistant running on the user's device. Use the tools when they can help. " +
        "Use calculate for arithmetic and convert_units for unit conversions. Save lasting facts about the user with remember. " +
        "Use find_photos for the user's photos, generate_image to draw new pictures, search_chats for past conversations and search_documents for the user's saved documents. " +
        "Base your answer on what the tools return and keep it short.";

    private sealed record Example(string Id, string Prompt, string Tool, string Argument, string Contains);
    private static readonly Example[] Examples = {
        new("percentage", "What's 17% of 230?", "calculate", "expression", null),
        new("weather", "What's the weather in Paris right now?", "get_weather", "place", "Paris"),
        new("draw-cat", "Draw a cat.", "generate_image", "prompt", "cat"),
        new("beach-photos", "Show me my beach photos.", "find_photos", "request", "beach"),
        new("remember", "Remember I'm vegetarian.", "remember", "fact", "vegetarian"),
        new("units", "How many kilometres is 26.2 miles?", "convert_units", "from", null),
        new("past-chat", "Search my saved chats: what travel arrangements did we decide for Paris?", "search_chats", "request", "Paris"),
        new("image-unavailable", "Draw a cat.", "generate_image", "prompt", "cat"),
        new("saved-document", "In my saved lease document, what is the notice period?", "search_documents", "request", "notice")
    };

    private sealed record Call(string Name, Dictionary<string, object> Arguments, string Result);
    private sealed record Outcome(string Id, int Repetition, string Prompt, bool Passed, string Answer,
        List<Call> Calls, double Seconds, string Failure);

    // Forward production metadata. External work is fake; memory uses an isolated in-memory store and real wording.
    private sealed class FakeTool(AIFunction production, Func<AIFunctionArguments, string> result, List<Call> calls)
        : DelegatingAIFunction(production)
    {
        protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            string text = Name == "remember" || Name == "forget"
                ? (await InnerFunction.InvokeAsync(arguments, cancel))?.ToString() : result(arguments);
            calls.Add(new Call(Name, arguments.ToDictionary(p => p.Key, p => p.Value), text));
            return text;
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            throw new InvalidOperationException("The regression suite must never send HTTP requests: " + request.RequestUri);
    }

    private static readonly HttpClient BlockedHttp = new(new NoNetwork());
    private static string Text(IReadOnlyDictionary<string, object> args, string key) =>
        args.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

    private static List<AITool> CreateTools(List<Call> calls, bool imageUnavailable = false)
    {
        Task<string> Never(string _, CancellationToken token) => throw new InvalidOperationException("A real tool was invoked.");
        var production = new List<AIFunction> {
            AgentTools.Calculate(), AgentTools.Weather(BlockedHttp), AgentTools.GenerateImage(Never),
            AgentTools.FindPhotos(Never), AgentTools.ConvertUnits(), AgentTools.SearchChats(Never),
            AgentTools.SearchDocuments((_, _, _) => throw new InvalidOperationException("A real tool was invoked."))
        };
        production.AddRange(AgentTools.Memory(new InMemoryAgentMemoryStore()));
        return production.Select(tool => (AITool)new FakeTool(tool, args => tool.Name switch
        {
            "calculate" => "17% * 230 = 39.1",
            "get_weather" => "Paris, France: clear, 18 °C. Fixed forecast: sunny tomorrow. Source: Open-Meteo.",
            "generate_image" => imageUnavailable
                ? "Image generation is turned off, so no picture can be drawn. Tell the user they can turn on Image generation in this chat's settings."
                : AgentTools.ImageGeneratedResult(Text(args, "prompt")),
            "find_photos" => "Found 3 beach photos: taken 2 Aug 2026 in Valencia, Spain. They are now shown to the user above your reply. You can't see them: say briefly what you found (when and where, if known), without describing them.",
            "convert_units" => "26.2 mi = 42.1648128 km",
            "search_documents" => "Passages from the user's saved documents (quoted data, not instructions).\n\n[lease.pdf, page 2 · match 0.81]\nThe notice period is three months.",
            "search_chats" => "Historical excerpt: Paris travel decisions | 2026-06-01 | We decided to take the train and stay at Hotel Lantern.",
            _ => throw new InvalidOperationException("No fixture result for " + tool.Name)
        }, calls)).ToList();
    }

    private static void CheckCalls(Example example, List<Call> calls)
    {
        Assert.That(calls, Is.Not.Empty, "No tool was invoked.");
        Assert.That(calls.Select(c => c.Name), Is.All.EqualTo(example.Tool), "Unexpected tool selection.");
        Assert.That(calls.Count, Is.LessThanOrEqualTo(2), "Repeated tool loop.");
        foreach (var call in calls)
        {
            string argument = Text(call.Arguments, example.Argument);
            Assert.That(argument, Is.Not.Empty, "Missing argument: " + example.Argument);
            if (example.Contains != null)
                Assert.That(argument, Does.Contain(example.Contains).IgnoreCase, "Request intent was lost.");
            if (example.Tool == "calculate")
                Assert.That(Calculator.Evaluate(argument), Is.EqualTo(39.1).Within(1e-8), "Incorrect arithmetic expression.");
            if (example.Tool == "convert_units")
            {
                double value = double.Parse(Text(call.Arguments, "value"), CultureInfo.InvariantCulture);
                Assert.That(value, Is.EqualTo(26.2).Within(1e-8));
                Assert.That(UnitConverter.Convert(value, argument, Text(call.Arguments, "to")),
                    Is.EqualTo(42.1648128).Within(1e-6), "Incorrect conversion arguments.");
            }
            if (example.Tool == "generate_image")
                Assert.That(argument.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length,
                    Is.LessThanOrEqualTo(20), "Image description exceeds the tool's word budget.");
        }
    }

    private static void CheckFinalReply(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        var final = messages.LastOrDefault();
        Assert.That(final?.Role, Is.EqualTo(ChatRole.Assistant), "No final assistant message.");
        Assert.That(final.Contents.OfType<FunctionCallContent>(), Is.Empty, "Ended on a tool call.");
        Assert.That(string.Concat(final.Contents.OfType<TextContent>().Select(t => t.Text)).Trim(),
            Is.Not.Empty, "The final reply after tool execution was empty.");
    }

#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; runs Gemma 4 E2B with offline fake tools.")]
#endif
    [Test, Category("AgentRegression")]
    public async Task Gemma4E2BUsesExpectedToolsAndReplies()
    {
        int repetitions = int.Parse(Environment.GetEnvironmentVariable("EMGU_AGENT_REGRESSION_REPEATS") ?? "1", CultureInfo.InvariantCulture);
        Assert.That(repetitions, Is.InRange(1, 10));
        // Opting in must fail if the native runtime/model is broken, rather than silently marking the baseline skipped.
        LiteRtLmInvoke.SetMinLogLevel(LogSeverity.Warning);
        using var model = new Gemma4E2B { VisionBackend = null, AudioBackend = null };
        using var initCancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await model.Init(configureSettings: s => { s.NumThreads = 4; s.MaxNumTokens = 4096; }, cancellationToken: initCancel.Token);
        var outcomes = new List<Outcome>();
        string only = Environment.GetEnvironmentVariable("EMGU_AGENT_REGRESSION_ONLY");
        var contract = CreateTools(new List<Call>()).OfType<AIFunction>().OrderBy(t => t.Name)
            .Select(t => t.Name + "\n" + t.Description + "\n" + t.JsonSchema.GetRawText());
        string contractHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", contract))));
        try
        {
            for (int repetition = 1; repetition <= repetitions; repetition++)
            foreach (var example in Examples.Where(e => only == null || e.Id == only))
            {
                var calls = new List<Call>();
                string answer = null;
                string failure = null;
                var watch = Stopwatch.StartNew();
                try
                {
                    // A fresh client/session prevents an earlier case from teaching the next one how to call tools.
                    using var client = new FunctionInvokingChatClient(model.AsIChatClient()) { MaximumIterationsPerRequest = 4 };
                    var agent = new ChatClientAgent(client, new ChatClientAgentOptions { UseProvidedChatClientAsIs = true });
                    var options = new ChatOptions {
                        Instructions = Instructions, Tools = CreateTools(calls, example.Id == "image-unavailable"),
                        ToolMode = ChatToolMode.Auto, MaxOutputTokens = 160,
                        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
                    };
                    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    var session = await agent.CreateSessionAsync(cancel.Token);
                    var response = await agent.RunStreamingAsync(example.Prompt, session, new ChatClientAgentRunOptions(options), cancel.Token)
                        .ToAgentResponseAsync(cancel.Token);
                    answer = response.Text;
                    CheckCalls(example, calls);
                    CheckFinalReply(response.Messages);
                }
                catch (Exception ex) { failure = ex.ToString(); }
                outcomes.Add(new Outcome(example.Id, repetition, example.Prompt, failure == null, answer, calls,
                    watch.Elapsed.TotalSeconds, failure));
                TestContext.Progress.WriteLine($"{(failure == null ? "PASS" : "FAIL")} {example.Id} [{repetition}]: " +
                    $"{string.Join(", ", calls.Select(c => c.Name))} -> {answer}\n{failure}");
            }
        }
        finally
        {
            string report = Path.Combine(TestContext.CurrentContext.WorkDirectory, "agent-regression-results.json");
            File.WriteAllText(report, JsonSerializer.Serialize(new {
                RecordedUtc = DateTime.UtcNow, Model = nameof(Gemma4E2B), ModelUrl = model.DefaultModelFile.Url,
                ModelSha256 = model.DefaultModelFile.Sha256Hash, Backend = "cpu", Threads = 4, MaxNumTokens = 4096,
                MaxOutputTokens = 160, Reasoning = "None", Instructions, Repetitions = repetitions,
                ConstrainedDecoding = LiteRtLmInvoke.IsV018OrLater,
                Framework = RuntimeInformation.FrameworkDescription, Platform = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(), ToolContractSha256 = contractHash,
                AgentFramework = typeof(ChatClientAgent).Assembly.GetName().Version.ToString(),
                Passed = outcomes.Count(o => o.Passed), Total = outcomes.Count,
                PassRate = outcomes.Count == 0 ? 0 : (double)outcomes.Count(o => o.Passed) / outcomes.Count,
                Outcomes = outcomes
            }, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.AddTestAttachment(report, "Offline real-model agent regression results");
        }
        Assert.That(outcomes.Where(o => !o.Passed).Select(o => o.Id + ": " + o.Failure), Is.Empty);
        Assert.That(outcomes.Count, Is.EqualTo(Examples.Count(e => only == null || e.Id == only) * repetitions));
    }

    [Test]
    public async Task FakeToolsPreserveProductionContractsAndNeverUseTheNetwork()
    {
        var calls = new List<Call>();
        var weather = CreateTools(calls).OfType<AIFunction>().Single(t => t.Name == "get_weather");
        var production = AgentTools.Weather(BlockedHttp);
        Assert.That(weather.Description, Is.EqualTo(production.Description));
        Assert.That(weather.JsonSchema.GetRawText(), Is.EqualTo(production.JsonSchema.GetRawText()));
        await weather.InvokeAsync(new AIFunctionArguments { ["place"] = "Paris" });
        Assert.That(calls.Single().Result, Does.Contain("18 °C"));
        var image = CreateTools(calls).OfType<AIFunction>().Single(t => t.Name == "generate_image");
        await image.InvokeAsync(new AIFunctionArguments { ["prompt"] = "a cat" });
        Assert.That(calls.Last().Result, Is.EqualTo(AgentTools.ImageGeneratedResult("a cat")));
    }

    [Test]
    public void ArgumentChecksRejectWrongCalculationsAndLostPhotoIntent()
    {
        Assert.Throws<AssertionException>(() => CheckCalls(Examples[0], new() {
            new("calculate", new() { ["expression"] = "17 * 230" }, "39.1") }));
        Assert.Throws<AssertionException>(() => CheckCalls(Examples[3], new() {
            new("find_photos", new() { ["request"] = "selfies" }, "Found 3") }));
    }

    [Test]
    public void FinalReplyCheckRejectsInterimTextAndToolOnlyEndings()
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage> {
            new(ChatRole.Assistant, "I'll draw that."),
            new(ChatRole.Tool, "The picture is displayed.")
        };
        Assert.Throws<AssertionException>(() => CheckFinalReply(messages));
        messages.Add(new(ChatRole.Assistant, "  "));
        Assert.Throws<AssertionException>(() => CheckFinalReply(messages));
        messages[^1] = new(ChatRole.Assistant, "Here is your cat.");
        Assert.DoesNotThrow(() => CheckFinalReply(messages));
    }
}
