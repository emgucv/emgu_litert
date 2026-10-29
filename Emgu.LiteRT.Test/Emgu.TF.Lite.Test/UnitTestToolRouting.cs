using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Agents.Skills;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Agents.CV;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Tool groups added on demand (Emgu.LiteRT.LM.Agents' ToolRouter, the groups' constants in Emgu.LiteRT.LM.Chat): which messages get
// which group, the stub tools that add a group in the middle of a request, and the size of what every message carries;
// no model or native library.
[TestFixture]
public class UnitTestToolRouting
{
    // A model that calls the tools it is told to, one per round, then answers; it records the tools it was offered.
    private sealed class ScriptedChatClient : IChatClient
    {
        public readonly Queue<string> Calls = new Queue<string>();
        public readonly List<List<string>> OfferedTools = new List<List<string>>();
        public readonly List<string> Instructions = new List<string>();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            OfferedTools.Add(options?.Tools?.Select(t => t.Name).ToList() ?? new List<string>());
            Instructions.Add(options?.Instructions ?? string.Empty);
            if (Calls.Count > 0)
            {
                string name = Calls.Dequeue();
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    new List<AIContent> { new FunctionCallContent("call" + OfferedTools.Count, name, new Dictionary<string, object>()) })));
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);
            foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
                yield return update;
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static Task<string> Unused(string request, CancellationToken token) => Task.FromResult("unused");

    // The demo's groups with the library's real tools (the camera's from VisualAgentTools with the demo's catalog).
    // done: called with what was done - "open_camera", "monitor:{label}:{actions}" or a device action's tool name.
    private static List<ToolGroup> DemoGroups(Action snapshot = null, Action<string> done = null, Func<string> request = null)
    {
        var actions = new[] { VisualHostCatalog.Chime, VisualHostCatalog.Speech, VisualHostCatalog.Snapshot };
        IEnumerable<AITool> Camera()
        {
            var tools = new List<AITool>
            {
                AgentTools.OpenCamera(_ => { snapshot?.Invoke(); return Task.FromResult("open"); }),
                AgentTools.CaptureCameraFrame(_ => Task.FromResult<IEnumerable<AIContent>>(Array.Empty<AIContent>()))
            };
            tools.AddRange(VisualAgentTools.Create(() => JsonSerializer.Serialize(new[] { CocoVisualAgent.Describe() }),
                () => JsonSerializer.Serialize(actions), actions,
                (label, count, confidence, selected, token) =>
                {
                    done?.Invoke("monitor:" + label + ":" + string.Join(",", selected.Select(a => a.ActionId)));
                    return Task.FromResult("Monitor 1: Watching. Actions: " + string.Join(", ", selected.Select(a => a.ActionId)) + ".");
                },
                () => "[]", (id, op, token) => Task.FromResult("ok"), (agent, image, token) => Task.FromResult("none")));
            return tools;
        }
        var store = new InMemoryAgentGoalStore();
        return new List<ToolGroup>
        {
            new ToolGroup(ChatCamera.GroupName, ChatCamera.GroupSummary, ChatCamera.GroupKeywords, Camera,
                () => ChatCamera.Instructions + "\n\n" + VisualAgentTools.Instructions),
            new ToolGroup(ChatCamera.SoundGroupName, ChatCamera.SoundGroupSummary, ChatCamera.SoundGroupKeywords,
                () => SoundMonitorTools.Create((a, b, c, d, e, t) => Task.FromResult("ok"), () => "none", (a, b, t) => Task.FromResult("ok")),
                () => SoundMonitorTools.Instructions),
            new ToolGroup(ChatDeviceActions.GroupName, ChatDeviceActions.GroupSummary, ChatDeviceActions.GroupKeywords,
                () => AgentTools.DeviceActions(DeviceActionKinds.Timer | DeviceActionKinds.Alarm | DeviceActionKinds.CalendarEvent | DeviceActionKinds.Directions,
                    (request, token) => { done?.Invoke(request.ToolName); return Task.FromResult(AgentTools.DeviceActionDone(request.Summary)); }),
                () => AgentTools.DeviceActionInstructions(DeviceActionKinds.Timer | DeviceActionKinds.Alarm | DeviceActionKinds.CalendarEvent | DeviceActionKinds.Directions),
                rerunAfterStub: true),
            // The goals group as the demo has it: the library's skill with the exercise counter as its extra tools.
            new GoalsSkill(store)
            {
                ExtraTools = () => new AITool[] { AgentTools.CountExercise((exercise, target, seconds, token) => Task.FromResult("")) },
                ExtraKeywords = new[] { "jumping jack", "jumping-jack", "star jump", "squat", "high knee", "knee raise", "exercise", "workout", "work out", "reps", "count my" },
                ExtraSummary = "count jumping jacks, squats or high knees with the camera"
            }.ToolGroups().Single(),
            new ToolGroup(ChatPhotos.GroupName, ChatPhotos.GroupSummary, ChatPhotos.GroupKeywords,
                () => new AITool[] { AgentTools.FindPhotos(Unused) }, () => ChatPhotos.Instructions),
            new ToolGroup(ChatDocuments.GroupName, ChatDocuments.GroupSummary, ChatDocuments.GroupKeywords,
                () => new AITool[] { AgentTools.SearchDocuments((request, document, token) => Task.FromResult("none")) }, () => ChatDocuments.Instructions),
            new ToolGroup(ChatSearchIndex.GroupName, ChatSearchIndex.GroupSummary, ChatSearchIndex.GroupKeywords,
                () => new AITool[] { AgentTools.SearchChats(Unused) }, () => ChatSearchIndex.Instructions),
            new ToolGroup(ChatWrittenDocuments.GroupName, ChatWrittenDocuments.GroupSummary, ChatWrittenDocuments.GroupKeywords,
                () => new AITool[] { AgentTools.CreateDocument((document, token) =>
                {
                    done?.Invoke("document:" + document.Format + ":" + (document.Data.Length > 200 ? "content" : "short"));
                    return Task.FromResult(AgentTools.DocumentCreatedResult(document));
                }, request), AgentTools.RecordMeeting((title, token) => Task.FromResult("none")) },
                () => "When the user asks for a document or a file (Word, PDF, Markdown), write the whole content into create_document's content in Markdown - not into your reply - then say in one short sentence that it is ready."),
            new ToolGroup(ChatImageGeneration.GroupName, ChatImageGeneration.GroupSummary, ChatImageGeneration.GroupKeywords,
                () => new AITool[] { AgentTools.GenerateImage(Unused) }, () => ChatImageGeneration.Instructions),
            new ToolGroup(ChatPhotoEffects.GroupName, ChatPhotoEffects.GroupSummary, ChatPhotoEffects.GroupKeywords,
                () => new AITool[] { AgentTools.BlurPhotoBackground((image, strength, token) => Task.FromResult("ok")) }, () => ChatPhotoEffects.Instructions),
        };
    }

    // blur_photo_background: which photo ("image-2", "2", or the last) and which strength the model meant.
    [Test]
    public async Task PhotoEffectsReadTheModelsArguments()
    {
        Assert.That(ChatPhotoEffects.ImageIndex("image-2", 3), Is.EqualTo(1));
        Assert.That(ChatPhotoEffects.ImageIndex("2", 3), Is.EqualTo(1));
        Assert.That(ChatPhotoEffects.ImageIndex("", 3), Is.EqualTo(2));
        Assert.That(ChatPhotoEffects.ImageIndex("image-7", 3), Is.EqualTo(2));
        Assert.That(ChatPhotoEffects.ImageIndex(null, 1), Is.EqualTo(0));
        Assert.That(ChatPhotoEffects.NormalizeStrength("Strong"), Is.EqualTo("strong"));
        Assert.That(ChatPhotoEffects.NormalizeStrength("a little"), Is.EqualTo("light"));
        Assert.That(ChatPhotoEffects.NormalizeStrength("more"), Is.EqualTo("strong"));
        Assert.That(ChatPhotoEffects.NormalizeStrength(null), Is.EqualTo("medium"));
        Assert.That(ChatPhotoEffects.Strengths, Does.Contain(ChatPhotoEffects.NormalizeStrength("whatever")));
        (string Image, string Strength) got = default;
        AIFunction tool = AgentTools.BlurPhotoBackground((image, strength, token) => { got = (image, strength); return Task.FromResult("done"); });
        Assert.That(tool.Name, Is.EqualTo("blur_photo_background"));
        Assert.That((await tool.InvokeAsync(new AIFunctionArguments { ["strength"] = "strong" }))?.ToString(), Does.Contain("done"));
        Assert.That(got, Is.EqualTo(("", "strong")));
        Assert.That(AgentTools.PhotoBackgroundBlurredResult("light"), Does.Contain("(light blur)").And.Contain("one short sentence"));
    }

    [Test]
    public void RoutesMessagesToGroups()
    {
        (string Message, string[] Groups)[] cases =
        {
            ("When you see a person, take a picture", new[] { ChatCamera.GroupName }),
            ("Let me know when someone walks in", new[] { ChatCamera.GroupName }),
            ("Tell me if a dog shows up at the front door", new[] { ChatCamera.GroupName }),
            ("What am I holding?", new[] { ChatCamera.GroupName }),
            ("Switch the monitor to YOLOX", new[] { ChatCamera.GroupName }),
            ("Can you read the label on this bottle through the camera?", new[] { ChatCamera.GroupName }),
            ("Beep when I stand up", new[] { ChatCamera.GroupName }),
            ("Take a picture when you see me waving", new[] { ChatCamera.GroupName }),
            ("Say hello when someone raises their hand", new[] { ChatCamera.GroupName }),
            ("Record a clip when the person is lying down", new[] { ChatCamera.GroupName }),
            ("Play white noise for 6 seconds when you see a person", new[] { ChatCamera.GroupName }),
            ("Play my rain sound when the dog appears", new[] { ChatCamera.GroupName }),
            ("Play the lullaby when the baby cries", new[] { ChatCamera.SoundGroupName }),
            ("Tell me if you hear a strange noise at night", new[] { ChatCamera.SoundGroupName }),
            ("Let me know when you hear the doorbell", new[] { ChatCamera.SoundGroupName }),
            ("Beep if my dog starts to bark", new[] { ChatCamera.SoundGroupName }),
            ("Alert me when a baby cries", new[] { ChatCamera.SoundGroupName }),
            ("When you hear the baby cry, take a video", new[] { ChatCamera.SoundGroupName }),
            ("Listen for the smoke alarm while I'm out", new[] { ChatCamera.SoundGroupName }),
            ("Set a 10 minute timer for the pasta", new[] { ChatDeviceActions.GroupName }),
            ("Wake me up tomorrow at 6:45 am.", new[] { ChatDeviceActions.GroupName }),
            ("Remind me to call mum on Sunday", new[] { ChatDeviceActions.GroupName }),
            ("Put lunch with Sam in my calendar for Friday at 12:30", new[] { ChatDeviceActions.GroupName }),
            ("How do I walk to Stanley Park?", new[] { ChatDeviceActions.GroupName }),
            ("I want to run three times a week.", new[] { GoalsSkill.GroupName }),
            ("How am I doing with my goals?", new[] { GoalsSkill.GroupName }),
            ("Count my jumping jacks", new[] { GoalsSkill.GroupName }),
            ("Let's do 30 seconds of star jumps", new[] { GoalsSkill.GroupName }),
            ("I want to do 30 jumping jacks every day", new[] { GoalsSkill.GroupName }),
            ("Count my squats", new[] { GoalsSkill.GroupName }),
            ("Let's do high knees for a minute", new[] { GoalsSkill.GroupName }),
            ("Show me my photos from Paris", new[] { ChatPhotos.GroupName }),
            ("Find my selfies from last summer", new[] { ChatPhotos.GroupName }),
            ("What does my rental contract say about pets?", new[] { ChatDocuments.GroupName }),
            ("What did we talk about last time?", new[] { ChatSearchIndex.GroupName }),
            ("What was the restaurant you told me about?", new[] { ChatSearchIndex.GroupName }),
            ("Draw a red fox in the snow", new[] { ChatImageGeneration.GroupName }),
            ("Can you paint a lighthouse at sunset?", new[] { ChatImageGeneration.GroupName }),
            ("Blur the background of this photo", new[] { ChatPhotoEffects.GroupName }),
            ("Can you make it look like portrait mode?", new[] { ChatPhotoEffects.GroupName }),
            ("Make the blur stronger", new[] { ChatPhotoEffects.GroupName }),
            ("Make me a packing list as a Word document", new[] { ChatWrittenDocuments.GroupName }),
            ("Write that up as a PDF", new[] { ChatWrittenDocuments.GroupName }),
            ("Can you export this as Markdown?", new[] { ChatWrittenDocuments.GroupName }),
            ("Take notes of my meeting", new[] { ChatWrittenDocuments.GroupName }),
            ("Record this lecture and transcribe it", new[] { ChatWrittenDocuments.GroupName }),
            ("Can you write the minutes of our team call?", new[] { ChatWrittenDocuments.GroupName }),
            // Everyday messages need no group.
            ("Boil the eggs for 7 minutes", new string[0]),
            ("Who holds the world record for the marathon?", new string[0]),
            ("What's the weather in Paris?", new string[0]),
            ("What was the background of the Cold War?", new string[0]),
            ("Look up the capital of France", new string[0]),
            ("How many people live in Tokyo?", new string[0]),
            ("What is 1234.5 times 678.9?", new string[0]),
            ("Convert 26.2 miles to km", new string[0]),
            ("Tell me a joke", new string[0]),
            ("Explain how a bicycle works", new string[0]),
            ("When are museums open on Sunday?", new string[0]),
            ("Plan a weekend in Paris", new string[0]),
        };
        ToolRouter router = new ToolRouter(DemoGroups(), null);
        List<string> wrong = new List<string>();
        foreach (var c in cases)
        {
            IList<string> groups = router.Match(c.Message);
            if (!c.Groups.All(groups.Contains) || (c.Groups.Length == 0 && groups.Count > 0))
                wrong.Add(c.Message + " -> " + string.Join(", ", groups));
        }
        Assert.That(wrong, Is.Empty);

        // A goal of the user's routes by its own words.
        IList<AgentGoal> goals = new List<AgentGoal> { AgentGoal.FromText("Read 12 books this year", DateTime.Now) };
        Assert.That(AgentTools.MentionsGoal(goals, "I finished another book!"), Is.True);
        Assert.That(AgentTools.MentionsGoal(goals, "What's the weather?"), Is.False);
    }

    [Test]
    public async Task StubAddsItsGroupInTheSameRequest()
    {
        ScriptedChatClient model = new ScriptedChatClient();
        model.Calls.Enqueue("use_camera");
        model.Calls.Enqueue("open_camera");
        int opened = 0;
        ToolRouter router = new ToolRouter(DemoGroups(() => opened++), () => FunctionInvokingChatClient.CurrentContext?.Options);
        string requested = null;
        router.GroupRequested += name => requested = name;
        // Words the keywords don't know: only the stub can get the camera.
        Assert.That(router.Route("Ping me whenever my cat jumps on the table"), Is.Empty);
        ChatOptions options = new ChatOptions { Tools = router.Tools(), Instructions = router.Instructions() };
        Assert.That(options.Tools.Select(t => t.Name), Does.Contain("use_camera").And.Not.Contain("create_visual_monitor"));

        ChatClientAgent agent = new ChatClientAgent(model, "Test", "Assistant", null, null);
        AgentSession session = await agent.CreateSessionAsync();
        await foreach (AgentResponseUpdate update in agent.RunStreamingAsync(
            new[] { new ChatMessage(ChatRole.User, "Ping me whenever my cat jumps on the table") }, session, new ChatClientAgentRunOptions(options)))
        {
        }
        Assert.That(requested, Is.EqualTo(ChatCamera.GroupName));
        Assert.That(model.OfferedTools[1], Does.Contain("open_camera").And.Contain("create_visual_monitor").And.Not.Contain("use_camera"));
        Assert.That(model.Instructions[1], Does.Contain("create_visual_monitor"));
        Assert.That(opened, Is.EqualTo(1));
        // And it stays for the rest of the conversation.
        Assert.That(router.IsActive(ChatCamera.GroupName), Is.True);
        Assert.That(router.Tools().Select(t => t.Name), Does.Contain("create_visual_monitor"));
        router.Reset();
        Assert.That(router.Tools().Select(t => t.Name), Does.Contain("use_camera"));
    }

#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; downloads Gemma 4 E2B (2.6 GB).")]
#endif
    [TestCase("When you see a person, take a picture", true, "camera", "monitor:person:take_snapshot")]
    [TestCase("Beep when a bird lands on the feeder", false, "camera", "monitor:bird:play_sound")]
    [TestCase("Set a timer for 10 minutes", false, "clock_calendar_maps", "set_timer")]
    [TestCase("Make me a packing list for a weekend of camping as a Word document.", true, "write_document", "document:Word:content")]
    [TestCase("Put together a moving-house checklist I can print and stick on the fridge.", false, "write_document", "document:PDF:content")]
    [TestCase("Say welcome out loud whenever a person comes into view", false, "camera", "monitor:person:speak_text")]
    // Through the stub, continuing the request, Gemma 4 E2B asked for details it had been given (the date, the title),
    // however the stub's result was worded: the stub ends the request and the message is sent again.
    [TestCase("Add the dentist on Friday at 3 pm to my calendar", false, "clock_calendar_maps", "add_calendar_event")]
    [TestCase("Add the dentist on Friday at 3 pm to my calendar", true, "clock_calendar_maps", "add_calendar_event")]
    public async Task Gemma4E2BUsesGroupsAndStubsAt4k(string prompt, bool route, string group, string expected)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        await model.Init(configureSettings: s => { s.NumThreads = 4; s.MaxNumTokens = 4096; }, cancellationToken: stop.Token);
        List<string> done = new List<string>();
        // As the demo does: a stub ends the request, and the message is sent again with the group.
        ToolRouter router = new ToolRouter(DemoGroups(null, done.Add, () => prompt), () => FunctionInvokingChatClient.CurrentContext?.Options,
            () => FunctionInvokingChatClient.CurrentContext.Terminate = true);
        List<string> requested = new List<string>();
        router.GroupRequested += requested.Add;
        // Without routing, only the stub can get the group.
        if (route)
            Assert.That(router.Route(prompt), Does.Contain(group));
        using var native = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmChatClient(model) { EnableContextManagement = true };
        using var client = new FunctionInvokingChatClient(native) { MaximumIterationsPerRequest = 6 };
        ChatResponse response = null;
        for (int run = 0; run < 2; run++)
        {
            IList<AITool> tools = ChatSessionTools.Create(false, null, null, null, null);
            foreach (AITool tool in router.Tools())
                tools.Add(tool);
            response = await client.GetResponseAsync(prompt, new ChatOptions
            {
                Tools = tools,
                Instructions = string.Join("\n\n", new[] { ChatSkillSet.DefaultInstructions, router.Instructions() }.Where(t => t != null)),
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
            }, stop.Token);
            if (!router.TakeRerun())
                break;
        }
        TestContext.Progress.WriteLine(prompt + " -> requested [" + string.Join(", ", requested) + "], done [" + string.Join(", ", done) + "]: " + response.Text);
        if (!route)
            Assert.That(requested, Does.Contain(group));
        Assert.That(done, Does.Contain(expected));
    }

#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true; downloads Gemma 4 E2B (2.6 GB).")]
#endif
    [TestCase("Ring me at 7 tomorrow morning.")]
    [TestCase("Set a timer for 10 minutes.")]
    [TestCase("Put lunch with Sam in my calendar for Friday at 12:30.")]
    public async Task Gemma4E2BSaysSoWhenTheUserCancels(string prompt)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var model = new Emgu.LiteRT.LM.Models.Gemma4E2B { VisionBackend = null, AudioBackend = null };
        await model.Init(configureSettings: s => { s.NumThreads = 4; s.MaxNumTokens = 4096; }, cancellationToken: stop.Token);
        int asked = 0;
        var kinds = DeviceActionKinds.Timer | DeviceActionKinds.Alarm | DeviceActionKinds.CalendarEvent | DeviceActionKinds.Directions;
        IList<AITool> tools = ChatSessionTools.Create(false, null, null, null, null);
        foreach (AITool tool in AgentTools.DeviceActions(kinds, (request, token) => { asked++; return Task.FromResult(AgentTools.DeviceActionCancelled); }))
            tools.Add(tool);
        using var native = new Emgu.LiteRT.LM.Extensions.AI.LiteRtLmChatClient(model) { EnableContextManagement = true };
        using var client = new FunctionInvokingChatClient(native) { MaximumIterationsPerRequest = 4 };
        ChatResponse response = await client.GetResponseAsync(prompt, new ChatOptions
        {
            Tools = tools,
            Instructions = ChatSkillSet.DefaultInstructions + "\n\n" + AgentTools.DeviceActionInstructions(kinds),
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
        }, stop.Token);
        TestContext.Progress.WriteLine(prompt + " -> " + response.Text);
        Assert.That(asked, Is.EqualTo(1));
        Assert.That(response.Text, Does.Match("(?i)cancel|not set|didn't|did not|wasn't|was not|nothing"));
    }

    [Test]
    public async Task StubCanEndTheRequestForARerun()
    {
        ScriptedChatClient model = new ScriptedChatClient();
        model.Calls.Enqueue("use_clock_calendar_maps");
        ToolRouter router = new ToolRouter(DemoGroups(), () => FunctionInvokingChatClient.CurrentContext?.Options,
            () => FunctionInvokingChatClient.CurrentContext.Terminate = true);
        using var client = new FunctionInvokingChatClient(model);
        await client.GetResponseAsync("Put the dentist on Friday at 3 in my diary", new ChatOptions { Tools = router.Tools() });
        // The request ended after the stub: no second round, and the message is to be sent again with the group.
        Assert.That(model.OfferedTools.Count, Is.EqualTo(1));
        Assert.That(router.TakeRerun(), Is.True);
        Assert.That(router.TakeRerun(), Is.False);
        Assert.That(router.Tools().Select(t => t.Name), Does.Contain("add_calendar_event").And.Not.Contain("use_clock_calendar_maps"));
    }

    private static int Size(IEnumerable<AITool> tools) =>
        tools.OfType<AIFunction>().Sum(f => f.Name.Length + f.Description.Length + f.JsonSchema.GetRawText().Length);

    [Test]
    public void EveryMessageCarriesLittle()
    {
        IList<AITool> core = ChatSessionTools.Create(false, null, null, null, null);
        core.Add(AgentTools.Memory(new InMemoryAgentMemoryStore())[0]);
        core.Add(AgentTools.Memory(new InMemoryAgentMemoryStore())[1]);
        ToolRouter router = new ToolRouter(DemoGroups(), null);
        int coreSize = Size(core) + ChatSkillSet.DefaultInstructions.Length;
        int stubs = Size(router.Tools());
        foreach (ToolGroup group in router.Groups)
            router.Activate(group.Name);
        int all = Size(router.Tools()) + router.Instructions().Length;
        TestContext.Out.WriteLine($"core tools and instructions {coreSize} characters, stubs {stubs}, all groups {all}");
        foreach (ToolGroup group in router.Groups)
            TestContext.Out.WriteLine($"  {group.Name}: {Size(group.CreateTools()) + (group.Instructions?.Invoke()?.Length ?? 0)}");
        // ~4 characters a token: what every message carries stays at about a quarter of a 4k window (each group's stub
        // adds ~200 characters).
        Assert.That(coreSize + stubs, Is.LessThan(4500));
    }
}
