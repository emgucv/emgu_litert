// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Skills;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Agents' ChatSkill / ChatSkillSet: what each message gets from the skills - tools, groups, instructions -
// and how private chats, disabled skills and a model without tools change it. No model or native library.
[TestFixture]
public class UnitTestChatSkills
{
    // A skill with one always-on tool, one group and an instruction line, which records what it was asked.
    private sealed class FakeSkill : ChatSkill
    {
        public int Prepared, NewConversations;
        public bool? LastPrivate;
        public FakeSkill(string name) { _name = name; }
        private readonly string _name;
        public override string Name => _name;
        public override Task PrepareAsync(ChatMessageContext context) { Prepared++; return Task.CompletedTask; }
        public override IEnumerable<AITool> Tools(ChatMessageContext context) =>
            new[] { AIFunctionFactory.Create(() => "ok", _name + "_tool", "A tool of " + _name) };
        public override IEnumerable<ToolGroup> ToolGroups()
        {
            yield return new ToolGroup(_name + "_group", "do " + _name + " things", new[] { _name + "word" },
                () => new[] { AIFunctionFactory.Create(() => "ok", _name + "_group_tool", "A group tool of " + _name) },
                () => "Group instructions of " + _name);
        }
        public override Task<string> InstructionsAsync(ChatMessageContext context) => Task.FromResult("Instructions of " + _name);
        public override string DisabledInstructions => _name + " is off.";
        public override string ToolSummary => _name + " summary";
        public override void OnNewConversation() => NewConversations++;
        public override void OnPrivateModeChanged(bool isPrivate) => LastPrivate = isPrivate;
    }

    private sealed class ThrowingSkill : ChatSkill
    {
        public override string Name => "broken";
        public override IEnumerable<AITool> Tools(ChatMessageContext context) => throw new InvalidOperationException("broken");
    }

    private static IEnumerable<string> Names(ChatSkillComposition composed) => composed.Tools.Select(t => t.Name);

    [Test]
    public async Task ComposesToolsGroupsAndInstructionsPerMessage()
    {
        var a = new FakeSkill("a");
        var b = new FakeSkill("b");
        var skills = new ChatSkillSet();
        skills.AddRange(new ChatSkill[] { a, b });
        ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "hello" }, "Base.");
        // Both skills' tools, and a stub for each group (none routed).
        Assert.That(Names(composed), Is.EquivalentTo(new[] { "a_tool", "b_tool", "use_a_group", "use_b_group" }));
        Assert.That(composed.GroupsAdded, Is.Empty);
        Assert.That(composed.Instructions, Does.StartWith("Base.").And.Contain("Instructions of a").And.Contain("Instructions of b")
            .And.Contain("use_a_group to do a things").And.Not.Contain("Group instructions of a"));
        Assert.That(a.Prepared, Is.EqualTo(1));

        // A message with a group's word gets its tools and instructions, which stay for the conversation.
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "an aword please" }, "Base.");
        Assert.That(composed.GroupsAdded, Is.EqualTo(new[] { "a_group" }));
        Assert.That(Names(composed), Does.Contain("a_group_tool").And.Not.Contain("use_a_group").And.Contain("use_b_group"));
        Assert.That(composed.Instructions, Does.Contain("Group instructions of a"));
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "and now?" }, "Base.");
        Assert.That(Names(composed), Does.Contain("a_group_tool"));

        // A new conversation starts without the group; the skills are told.
        skills.Reset();
        Assert.That(a.NewConversations, Is.EqualTo(1));
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "and now?" }, "Base.");
        Assert.That(Names(composed), Does.Contain("use_a_group").And.Not.Contain("a_group_tool"));

        Assert.That(skills.ToolSummary, Is.EqualTo("a summary, a group, b summary, b group"));
        Assert.That(skills.Find("b"), Is.SameAs(b));
        Assert.That(skills.Find<FakeSkill>(), Is.SameAs(a));
    }

    [Test]
    public async Task DisabledSkillsAddOnlyTheirOffLine()
    {
        var a = new FakeSkill("a");
        var b = new FakeSkill("b") { Enabled = false };
        var skills = new ChatSkillSet();
        skills.AddRange(new ChatSkill[] { a, b });
        ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "bword" }, null);
        Assert.That(Names(composed), Is.EquivalentTo(new[] { "a_tool", "use_a_group" }));
        Assert.That(composed.Instructions, Does.Contain("b is off.").And.Not.Contain("Instructions of b").And.Not.Contain("use_b_group"));
        Assert.That(skills.ToolSummary, Is.EqualTo("a summary, a group"));
        // Turned on again: its group comes with the next message that needs it (the words were seen while it was off).
        b.Enabled = true;
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "bword again" }, null);
        Assert.That(Names(composed), Does.Contain("b_tool").And.Contain("b_group_tool"));
    }

    [Test]
    public async Task AModelWithoutToolsGetsOnlyTheBaseInstructions()
    {
        var a = new FakeSkill("a");
        var skills = new ChatSkillSet();
        skills.Add(a);
        ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "aword", ModelCanUseTools = false }, "Base.");
        Assert.That(composed.Tools, Is.Empty);
        Assert.That(composed.Instructions, Is.EqualTo("Base."));
        Assert.That(a.Prepared, Is.Zero);
    }

    [Test]
    public async Task ASkillThatThrowsIsLeftOutOfTheMessage()
    {
        var skills = new ChatSkillSet();
        skills.Add(new ThrowingSkill());
        skills.Add(new FakeSkill("a"));
        ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "hi" }, null);
        Assert.That(Names(composed), Is.EquivalentTo(new[] { "a_tool", "use_a_group" }));
    }

    [Test]
    public async Task AddingASkillKeepsTheGroupsInUse()
    {
        var a = new FakeSkill("a");
        var skills = new ChatSkillSet();
        skills.Add(a);
        await skills.ComposeAsync(new ChatMessageContext { Prompt = "aword" }, null);
        Assert.That(skills.Router.IsActive("a_group"), Is.True);
        skills.Add(new FakeSkill("b"));
        Assert.That(skills.Router.IsActive("a_group"), Is.True);
        Assert.That(skills.Router.Groups.Select(g => g.Name), Is.EqualTo(new[] { "a_group", "b_group" }));
        // A skill with the same name replaces the one before.
        var a2 = new FakeSkill("a");
        skills.Add(a2);
        Assert.That(skills.Skills, Is.EqualTo(new ChatSkill[] { skills.Find("b"), a2 }));
        skills.SetPrivate(true);
        Assert.That(a2.LastPrivate, Is.True);
    }

    // A model that calls the tools it is told to, one per round, then answers.
    private sealed class ScriptedChatClient : IChatClient
    {
        public readonly Queue<string> Calls = new Queue<string>();
        public readonly List<List<string>> OfferedTools = new List<List<string>>();

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            OfferedTools.Add(options?.Tools?.Select(t => t.Name).ToList() ?? new List<string>());
            if (Calls.Count > 0)
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    new List<AIContent> { new FunctionCallContent("call" + OfferedTools.Count, Calls.Dequeue(), new Dictionary<string, object>()) })));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (ChatResponseUpdate update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
                yield return update;
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class RerunSkill : ChatSkill
    {
        public override string Name => "clock";
        public override IEnumerable<ToolGroup> ToolGroups()
        {
            yield return new ToolGroup("clock", "set a timer", new[] { "timer" },
                () => new[] { AIFunctionFactory.Create(() => "set", "set_timer", "Sets a timer.") }, rerunAfterStub: true);
        }
    }

    [Test]
    public async Task AStubThatEndsTheRequestAsksForARerun()
    {
        // As ChatPage runs it: the set's router adds a stub's group to the request being answered, and a group with
        // RerunAfterStub ends the request instead, so the message is sent again (ChatView.RerunRequested) with its tools.
        var skills = new ChatSkillSet(() => FunctionInvokingChatClient.CurrentContext?.Options, () => FunctionInvokingChatClient.CurrentContext.Terminate = true);
        skills.Add(new RerunSkill());
        var model = new ScriptedChatClient();
        model.Calls.Enqueue("use_clock");
        using var client = new FunctionInvokingChatClient(model);
        // Words the keywords don't know: only the stub can get the group.
        ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "Ping me in ten minutes" }, null);
        Assert.That(Names(composed), Is.EqualTo(new[] { "use_clock" }));
        await client.GetResponseAsync("Ping me in ten minutes", new ChatOptions { Tools = composed.Tools, Instructions = composed.Instructions });
        Assert.That(model.OfferedTools.Count, Is.EqualTo(1), "the request ended after the stub");
        Assert.That(skills.TakeRerun(), Is.True);
        Assert.That(skills.TakeRerun(), Is.False);
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "Ping me in ten minutes" }, null);
        Assert.That(Names(composed), Is.EqualTo(new[] { "set_timer" }));
    }

    [Test]
    public async Task TheBuiltInSkillsFollowMemoryAndPrivateMode()
    {
        var memories = new InMemoryAgentMemoryStore();
        await memories.AddMemoryAsync("The user is vegetarian.");
        var goals = new InMemoryAgentGoalStore();
        await goals.AddGoalAsync(AgentGoal.FromText("Read 20 pages a day", DateTime.Now));
        var skills = new ChatSkillSet();
        skills.AddRange(new ChatSkill[] { new CoreToolsSkill(), new MemorySkill(memories), new WebSkill(), new GoalsSkill(goals) });

        ChatSkillComposition composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "What's the weather?", MemoryOn = true }, ChatSkillSet.DefaultInstructions);
        Assert.That(Names(composed), Is.EquivalentTo(new[] { "get_current_date_time", "calculate", "convert_units", "remember", "forget",
            "get_weather", "search_wikipedia", "convert_currency", "read_web_page", "use_goals" }));
        Assert.That(composed.Instructions, Does.Contain("The user is vegetarian.").And.Contain("no Tavily API key"));

        // A goal of the user's brings the goals group by its own words.
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "I read another chapter tonight", MemoryOn = true }, null);
        Assert.That(composed.GroupsAdded, Is.EqualTo(new[] { "goals" }));
        Assert.That(Names(composed), Does.Contain("log_progress"));
        Assert.That(composed.Instructions, Does.Contain("Read 20 pages a day"));

        // Private: no memories, no goals; the web stays.
        skills.Reset();
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "I read a chapter", MemoryOn = true, IsPrivate = true }, null);
        Assert.That(Names(composed), Does.Not.Contain("remember").And.Not.Contain("use_goals").And.Not.Contain("log_progress").And.Contain("get_weather"));
        Assert.That(composed.Instructions, Does.Not.Contain("vegetarian"));

        // Web search off: the off line, and no web tools; a Tavily key adds the search.
        skills.Reset();
        WebSkill web = skills.Find<WebSkill>();
        web.Enabled = false;
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "hi" }, null);
        Assert.That(Names(composed), Does.Not.Contain("get_weather"));
        Assert.That(composed.Instructions, Does.Contain("Web search is turned off"));
        web.Enabled = true;
        web.TavilyApiKey = "tvly-test";
        composed = await skills.ComposeAsync(new ChatMessageContext { Prompt = "hi" }, null);
        Assert.That(Names(composed), Does.Contain("search_web").And.Not.Contain("read_web_page"));
        Assert.That(composed.Instructions, Does.Contain("search_web"));
    }
}
