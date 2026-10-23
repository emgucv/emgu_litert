// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// ChatConversation: sending, streaming, cancelling, compaction and restoring, over a scripted IChatClient. No model.
[TestFixture]
public class UnitTestChatConversation
{
    // An IChatClient that answers each request with the next scripted reply and records the requests.
    private sealed class FakeChatClient : IChatClient
    {
        public readonly List<(List<ChatMessage> Messages, ChatOptions Options)> Requests = new();
        public readonly Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> Replies = new();
        // Answers summary requests (their prompt starts with "Summarize").
        public string Summary = "The user likes tea.";
        public int SummaryRequests;

        public void Reply(params AIContent[] contents) => Replies.Enqueue(_ => Stream(contents));

        public void Fail(Exception exception) => Replies.Enqueue(_ => Throw(exception));

        private static async IAsyncEnumerable<ChatResponseUpdate> Stream(AIContent[] contents)
        {
            foreach (AIContent content in contents)
            {
                await Task.Yield();
                ChatRole role = content is FunctionResultContent ? ChatRole.Tool : ChatRole.Assistant;
                yield return new ChatResponseUpdate(role, new List<AIContent> { content }) { MessageId = role.Value + (content is FunctionCallContent ? "call" : "") };
            }
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> Throw(Exception exception)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            List<ChatMessage> list = messages.ToList();
            if (list.Count == 1 && list[0].Text.StartsWith("Summarize", StringComparison.Ordinal))
            {
                SummaryRequests++;
                await Task.Yield();
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, Summary));
            }
            List<ChatResponseUpdate> updates = new();
            await foreach (ChatResponseUpdate update in GetStreamingResponseAsync(list, options, cancellationToken))
                updates.Add(update);
            return updates.ToChatResponse();
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, CancellationToken cancellationToken = default)
        {
            Requests.Add((messages.ToList(), options));
            return Replies.Dequeue()(cancellationToken);
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // One token per word, so sizes are easy to reason about.
    private static int Words(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static ChatConversation Conversation(FakeChatClient client, int window = 4096) => new ChatConversation(client)
    {
        CountTokens = Words,
        ContextWindow = window,
        ReplyReserveTokens = 20,
        OverheadTokens = 0,
        MessageOverheadTokens = 0,
        SummaryMaxTokens = 10
    };

    private static string Texts(IEnumerable<ChatMessage> messages) => string.Join(" | ", messages.Select(m => m.Role.Value + ":" + m.Text));

    [Test]
    public async Task StreamsThinkingAndText()
    {
        FakeChatClient client = new FakeChatClient();
        client.Reply(new TextReasoningContent("Let me "), new TextReasoningContent("think."), new TextContent("Hello"), new TextContent(" there."));
        ChatConversation conversation = Conversation(client);
        conversation.Instructions = "Be brief.";
        List<string> seen = new();
        ChatExchangeEventArgs completed = null;
        conversation.ExchangeCompleted += (s, e) => completed = e;
        conversation.Messages.CollectionChanged += (s, e) =>
        {
            foreach (ChatMessageItem item in e.NewItems?.OfType<ChatMessageItem>() ?? Enumerable.Empty<ChatMessageItem>())
                item.PropertyChanged += (o, p) =>
                {
                    if (p.PropertyName == nameof(ChatMessageItem.IsThinking) && item.IsThinking)
                        seen.Add("thinking: " + item.Thinking);
                    if (p.PropertyName == nameof(ChatMessageItem.Text))
                        seen.Add("text: " + item.Text);
                };
        };

        ChatMessageItem reply = await conversation.SendAsync("Hi", options: new ChatOptions { Instructions = "Use English." });

        Assert.That(reply.State, Is.EqualTo(ChatMessageState.Done));
        Assert.That(reply.Thinking, Is.EqualTo("Let me think."));
        Assert.That(reply.Text, Is.EqualTo("Hello there."));
        Assert.That(reply.ThinkingDuration, Is.Not.Null);
        Assert.That(reply.IsThinking, Is.False);
        Assert.That(seen, Is.EqualTo(new[] { "thinking: Let me ", "text: Hello", "text: Hello there." }));
        Assert.That(conversation.Messages.Select(m => m.Role), Is.EqualTo(new[] { ChatMessageRole.User, ChatMessageRole.Assistant }));
        Assert.That(completed.Message.Text, Is.EqualTo("Hi"));
        Assert.That(completed.Reply, Is.SameAs(reply));
        Assert.That(client.Requests[0].Options.Instructions, Is.EqualTo("Be brief.\n\nUse English."));
    }

    [Test]
    public async Task ShowsToolStepsAndSendsTheReplyBackAsItWas()
    {
        FakeChatClient client = new FakeChatClient();
        client.Reply(
            new FunctionCallContent("c1", "get_weather", new Dictionary<string, object> { ["place"] = "Paris" }),
            new FunctionResultContent("c1", "Sunny, 21 °C"),
            new TextContent("It's sunny in Paris."));
        client.Reply(new TextContent("You're welcome."));
        ChatConversation conversation = Conversation(client);
        conversation.DescribeToolCall = call => "Checking the weather in " + call.Arguments["place"];

        ChatMessageItem reply = await conversation.SendAsync("Weather in Paris?");
        Assert.That(reply.Steps.Count, Is.EqualTo(1));
        Assert.That(reply.Steps[0].Description, Is.EqualTo("Checking the weather in Paris"));
        Assert.That(reply.Steps[0].IsRunning, Is.False);
        Assert.That(reply.Steps[0].Result.Result, Is.EqualTo("Sunny, 21 °C"));
        Assert.That(reply.Text, Is.EqualTo("It's sunny in Paris."));

        await conversation.SendAsync("Thanks");
        // The second request holds the first reply's own messages - the call, the result and the answer - so a client
        // that keeps its conversation open sees only the new message added.
        List<ChatMessage> second = client.Requests[1].Messages;
        Assert.That(second.Count, Is.EqualTo(5));
        Assert.That(second[1].Contents.OfType<FunctionCallContent>().Single().Name, Is.EqualTo("get_weather"));
        Assert.That(second[2].Contents.OfType<FunctionResultContent>().Single().CallId, Is.EqualTo("c1"));
        Assert.That(second[3].Text, Is.EqualTo("It's sunny in Paris."));
        Assert.That(second[4].Text, Is.EqualTo("Thanks"));
    }

    [Test]
    public async Task CancellingKeepsThePartialReply()
    {
        FakeChatClient client = new FakeChatClient();
        using CancellationTokenSource cancel = new CancellationTokenSource();
        client.Replies.Enqueue(token => Partial(cancel, token));
        client.Reply(new TextContent("Sure."));
        ChatConversation conversation = Conversation(client);

        ChatMessageItem reply = await conversation.SendAsync("Tell me a story", cancellationToken: cancel.Token);
        Assert.That(reply.State, Is.EqualTo(ChatMessageState.Interrupted));
        Assert.That(reply.Text, Is.EqualTo("Once upon"));

        await conversation.SendAsync("Go on");
        Assert.That(Texts(client.Requests[1].Messages), Is.EqualTo("user:Tell me a story | assistant:Once upon | user:Go on"));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Partial(CancellationTokenSource cancel, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Once upon");
        cancel.Cancel();
        await Task.Delay(Timeout.Infinite, token);
        yield return new ChatResponseUpdate(ChatRole.Assistant, " a time");
    }

    [Test]
    public async Task SummarizesWhenTheConversationOutgrowsTheWindow()
    {
        FakeChatClient client = new FakeChatClient();
        // Window 80, reserve 20: 60 tokens for the conversation; each exchange below is 10 + 10.
        ChatConversation conversation = Conversation(client, 80);
        ChatCompactedEventArgs compacted = null;
        conversation.Compacted += (s, e) => compacted = e;
        string ten = string.Join(" ", Enumerable.Repeat("word", 9));
        for (int i = 0; i < 3; i++)
        {
            client.Reply(new TextContent("answer " + i + " " + ten.Substring(5)));
            await conversation.SendAsync("question " + i + " " + ten.Substring(5));
        }
        Assert.That(client.SummaryRequests, Is.EqualTo(0));
        Assert.That(compacted, Is.Null);

        // 60 tokens in the conversation already: the next message doesn't fit, so the earlier ones are summarized, keeping
        // the latest exchanges up to a quarter of the window (20 tokens: one exchange).
        client.Reply(new TextContent("ok"));
        await conversation.SendAsync("question 3 " + ten.Substring(5));
        Assert.That(client.SummaryRequests, Is.EqualTo(1));
        Assert.That(compacted.Summarized, Is.True);
        Assert.That(compacted.Summary, Is.EqualTo("The user likes tea."));
        Assert.That(conversation.Summary, Is.EqualTo("The user likes tea."));
        Assert.That(conversation.ContextStart, Is.EqualTo(4));
        Assert.That(compacted.ContextStart, Is.EqualTo(4));
        List<ChatMessage> request = client.Requests.Last().Messages;
        Assert.That(request[0].Text, Does.StartWith("Here is a summary of our conversation so far:\nThe user likes tea."));
        Assert.That(request[1].Role, Is.EqualTo(ChatRole.Assistant));
        Assert.That(request.Skip(2).Select(m => m.Text.Split(' ')[0] + " " + m.Text.Split(' ')[1]),
            Is.EqualTo(new[] { "question 2", "answer 2", "question 3" }));
        // The transcript keeps everything.
        Assert.That(conversation.Messages.Count, Is.EqualTo(8));
    }

    [Test]
    public async Task DropsEarlierMessagesForSmallModels()
    {
        FakeChatClient client = new FakeChatClient();
        ChatConversation conversation = Conversation(client, 80);
        conversation.Compaction = ChatCompaction.Drop;
        string ten = string.Join(" ", Enumerable.Repeat("word", 8));
        for (int i = 0; i < 4; i++)
        {
            client.Reply(new TextContent("answer " + i + " " + ten));
            await conversation.SendAsync("question " + i + " " + ten);
        }
        Assert.That(client.SummaryRequests, Is.EqualTo(0));
        Assert.That(conversation.Summary, Is.Null);
        // Dropping keeps as many exchanges as fit with the new message (60 - 10 = 50: two exchanges).
        Assert.That(client.Requests.Last().Messages.Select(m => m.Text.Split(' ')[0] + " " + m.Text.Split(' ')[1]),
            Is.EqualTo(new[] { "question 1", "answer 1", "question 2", "answer 2", "question 3" }));
        ChatContextUsage usage = await conversation.GetContextUsageAsync();
        Assert.That(usage.UsedTokens, Is.EqualTo(60));
        Assert.That(usage.WindowTokens, Is.EqualTo(80));
    }

    [Test]
    public async Task RetriesOnceWithoutEarlierMessagesWhenTheContextIsFull()
    {
        FakeChatClient client = new FakeChatClient();
        ChatConversation conversation = Conversation(client);
        client.Reply(new TextContent("First answer"));
        await conversation.SendAsync("First question");
        client.Fail(new InvalidOperationException("Input token ids are too long. Exceeding the maximum number of tokens allowed."));
        client.Reply(new TextContent("Second answer"));

        ChatMessageItem reply = await conversation.SendAsync("Second question");
        Assert.That(reply.State, Is.EqualTo(ChatMessageState.Done));
        Assert.That(reply.Text, Is.EqualTo("Second answer"));
        Assert.That(client.Requests.Count, Is.EqualTo(3));
        Assert.That(Texts(client.Requests[1].Messages), Is.EqualTo("user:First question | assistant:First answer | user:Second question"));
        Assert.That(Texts(client.Requests[2].Messages), Is.EqualTo("user:Second question"));
        Assert.That(conversation.ContextStart, Is.EqualTo(2));
    }

    [Test]
    public async Task AFailedExchangeIsNotGivenToTheModelAgain()
    {
        FakeChatClient client = new FakeChatClient();
        ChatConversation conversation = Conversation(client);
        client.Fail(new InvalidOperationException("The engine failed"));
        client.Reply(new TextContent("Hello"));

        Assert.ThrowsAsync<InvalidOperationException>(() => conversation.SendAsync("Broken"));
        Assert.That(conversation.Messages[1].State, Is.EqualTo(ChatMessageState.Failed));
        Assert.That(conversation.Messages[1].Error, Is.EqualTo("The engine failed"));
        Assert.That(conversation.IsBusy, Is.False);

        await conversation.SendAsync("Hi");
        Assert.That(Texts(client.Requests[1].Messages), Is.EqualTo("user:Hi"));
        Assert.That(conversation.Messages.Count, Is.EqualTo(4));
    }

    [Test]
    public async Task RestoresASavedConversation()
    {
        FakeChatClient client = new FakeChatClient();
        ChatConversation conversation = Conversation(client);
        byte[] png = { 0x89, 0x50, 0x4E, 0x47 };
        conversation.Load(new[]
        {
            new ChatMessageItem(ChatMessageRole.User, "Old question"),
            new ChatMessageItem(ChatMessageRole.Assistant, "Old answer"),
            new ChatMessageItem(ChatMessageRole.User, "What is this?", new[] { new DataContent(png, "image/png") }, "Excerpt: ..."),
            new ChatMessageItem(ChatMessageRole.Assistant, "A cat.") { Thinking = "It has whiskers." },
        }, "The user asked something before.", 2);
        Assert.That(conversation.ContextStart, Is.EqualTo(2));
        client.Reply(new TextContent("Yes."));

        await conversation.SendAsync("Really?");
        List<ChatMessage> request = client.Requests[0].Messages;
        Assert.That(request.Count, Is.EqualTo(5));
        Assert.That(request[0].Text, Does.EndWith("The user asked something before."));
        Assert.That(request[2].Contents.OfType<DataContent>().Single().MediaType, Is.EqualTo("image/png"));
        Assert.That(request[2].Text, Is.EqualTo("Excerpt: ...What is this?"));
        Assert.That(request[3].Text, Is.EqualTo("A cat."));
        Assert.That(request[4].Text, Is.EqualTo("Really?"));

        conversation.Clear();
        Assert.That(conversation.Messages, Is.Empty);
        Assert.That(conversation.Summary, Is.Null);
    }

    [Test]
    public async Task CompactNowKeepsTheLatestExchange()
    {
        FakeChatClient client = new FakeChatClient();
        ChatConversation conversation = Conversation(client);
        Assert.That(await conversation.CompactAsync(), Is.False);
        for (int i = 0; i < 3; i++)
        {
            client.Reply(new TextContent("answer " + i));
            await conversation.SendAsync("question " + i);
        }
        Assert.That(await conversation.CompactAsync(), Is.True);
        Assert.That(conversation.ContextStart, Is.EqualTo(4));
        Assert.That(conversation.Summary, Is.EqualTo("The user likes tea."));

        // A failed summary drops the messages instead.
        conversation.Load(conversation.Messages.ToList());
        client.Summary = "  ";
        Assert.That(await conversation.CompactAsync(), Is.True);
        Assert.That(conversation.Summary, Is.Null);
        Assert.That(conversation.ContextStart, Is.EqualTo(4));
    }
}
