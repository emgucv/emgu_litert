// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Documents;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Maui.Demo.Lite;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    [TestFixture]
    public class UnitTestChatSearch
    {
        private readonly List<(ChatStore Store, string Folder)> _stores = new();

        private static ChatSearchQuery ToQuery(PhotoQuery query) => new ChatSearchQuery(query.Description, query.From, query.To);

        private ChatStore CreateStore()
        {
            string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-search-test-" + Guid.NewGuid().ToString("N"));
            var store = new ChatStore(folder);
            _stores.Add((store, folder));
            return store;
        }

        [TearDown]
        public async Task Cleanup()
        {
            foreach (var entry in _stores)
            {
                await entry.Store.CloseAsync();
                string folder = Path.GetFullPath(entry.Folder);
                if (Path.GetDirectoryName(folder) != Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
                    || !Path.GetFileName(folder).StartsWith("chat-search-test-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected test directory.");
                Directory.Delete(folder, true);
            }
            _stores.Clear();
        }

        [Test]
        public void ChunkPreservesTextAndOverlap()
        {
            string text = string.Concat(Enumerable.Range(0, 2500).Select(i => (char)('a' + i % 26)));
            var chunks = ChatSearchIndex.Chunk(text).ToList();
            Assert.That(chunks, Has.Count.EqualTo(4));
            Assert.That(chunks.All(c => c.Length <= 900));
            string restored = chunks[0] + string.Concat(chunks.Skip(1).Select(c => c.Substring(150)));
            Assert.That(restored, Is.EqualTo(text));
            Assert.That(ChatSearchIndex.Chunk(null), Is.Empty);
            Assert.That(ChatSearchIndex.Chunk("  "), Is.Empty);
        }

        [Test]
        public void DateFiltersKeepChatSubjectsAndPlaces()
        {
            var now = new DateTime(2026, 10, 14, 15, 0, 0);
            var query = ChatSearch.Parse("our trip to Paris last week", now);
            Assert.That(query.From, Is.EqualTo(new DateTime(2026, 10, 5)));
            Assert.That(query.To, Is.EqualTo(new DateTime(2026, 10, 12).AddTicks(-1)));
            Assert.That(query.Description, Does.Contain("trip to Paris"));
            Assert.That(ChatSearch.Parse("my wedding plans", now).From, Is.Null);
            Assert.That(ChatSearch.Parse("plans yesterday", now).From, Is.EqualTo(now.Date.AddDays(-1)));
        }

        [Test]
        public async Task ToolForwardsRequestAndCancellation()
        {
            using var cancel = new CancellationTokenSource();
            var tool = AgentTools.SearchChats((request, token) =>
            {
                Assert.That(token, Is.EqualTo(cancel.Token));
                return Task.FromResult("Found: " + request);
            });
            Assert.That(tool.Name, Is.EqualTo("search_chats"));
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["request"] = "trip last week" }, cancel.Token);
            Assert.That(result?.ToString(), Is.EqualTo("Found: trip last week"));
            Assert.That(AgentTools.Describe(new FunctionCallContent("1", "search_chats",
                new Dictionary<string, object> { ["request"] = "trip last week" })), Does.Contain("trip last week"));
            Assert.Throws<ArgumentNullException>(() => AgentTools.SearchChats(null));
        }

        [Test]
        public async Task DocumentSearchToolForwardsArguments()
        {
            var tool = AgentTools.SearchDocuments((request, document, token) => Task.FromResult(request + "|" + document));
            Assert.That(tool.Name, Is.EqualTo("search_documents"));
            var result = await tool.InvokeAsync(new AIFunctionArguments { ["request"] = "notice period", ["document"] = "lease" });
            Assert.That(result?.ToString(), Is.EqualTo("notice period|lease"));
            Assert.That(AgentTools.Describe(new FunctionCallContent("1", "search_documents",
                new Dictionary<string, object> { ["request"] = "notice period" })), Does.Contain("notice period"));
            Assert.Throws<ArgumentNullException>(() => AgentTools.SearchDocuments(null));
        }

        [Test]
        public async Task DocumentLibrarySearchRanksFiltersAndSkipsOtherModels()
        {
            var store = CreateStore();
            byte[] Vector(params float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }
            async Task Save(string id, string name, string model, params (string Text, string Page, float[] V)[] items)
            {
                await store.SaveDocumentAsync(
                    new DocumentRecord { Id = id, Name = name, PageCount = 2, Settings = "v1|" + model + "|x", Created = DateTime.UtcNow },
                    items.Select((p, i) => new DocumentItemRecord { DocumentId = id, Position = i, Text = p.Text, Location = p.Page, Embedding = Vector(p.V) }));
            }
            await Save("a", "lease.pdf", "m1", ("Notice period is three months.", "page 2", new[] { 1f, 0f }),
                ("The garden is shared.", "page 1", new[] { 0f, 1f }));
            await Save("b", "recipes.docx", "m1", ("Notice the pepper.", "page 1", new[] { 0.9f, 0.1f }));
            await Save("c", "old.pdf", "m2", ("Stale vector.", "page 1", new[] { 1f, 0f }));
            // As DocumentsFeature does: the store's passages for one embedding model, narrowed to a document, ranked against the request.
            float[] query = { 1f, 0f };
            async Task<List<DocumentLibraryPassage>> Passages(string model) => (await store.GetDocumentPassagesAsync(model))
                .Select(p => new DocumentLibraryPassage
                {
                    DocumentId = p.DocumentId,
                    DocumentName = p.DocumentName,
                    Location = p.Location,
                    Text = p.Text,
                    Embedding = Enumerable.Range(0, p.Embedding.Length / 4).Select(i => BitConverter.ToSingle(p.Embedding, i * 4)).ToArray()
                }).ToList();
            var passages = await Passages("m1");
            Assert.That(passages.Select(p => p.Embedding.Length), Is.All.EqualTo(2));

            var all = DocumentLibrarySearch.Rank(passages, query, 3200);
            Assert.That(all.Select(m => m.DocumentName), Is.EqualTo(new[] { "lease.pdf", "recipes.docx" }));
            Assert.That(all[0].Location, Is.EqualTo("page 2"));
            Assert.That(all[0].DocumentId, Is.EqualTo("a"));

            var named = DocumentLibrarySearch.Rank(DocumentLibrarySearch.Named(passages, "RECIPES"), query, 3200);
            Assert.That(named.Select(m => m.DocumentName), Is.EqualTo(new[] { "recipes.docx" }));

            Assert.That(DocumentLibrarySearch.Rank(passages, query, 10), Has.Count.EqualTo(1));

            Assert.That(await Passages("m3"), Is.Empty);
            Assert.That(DocumentLibrarySearch.Format(all), Does.Contain("[lease.pdf, page 2").And.Contain("three months"));
        }

        [Test]
        public async Task IndexPersistsReplacesPrunesAndClearsWithoutDeletingChats()
        {
            var store = CreateStore();
            int chat = await store.CreateConversationAsync("Trip decisions", "Gemma4E2B");
            int user = await store.AddMessageAsync(chat, MessageRecord.UserRole, "Visit Paris in June", null);
            int reply = await store.AddMessageAsync(chat, MessageRecord.AssistantRole, "Book the train", "private reasoning");
            await store.AddMessageAsync(chat, MessageRecord.StepRole, "Searching", null);
            var sources = await store.GetSearchMessagesAsync();
            Assert.That(sources.Select(s => s.Id), Is.EquivalentTo(new[] { user, reply }));
            List<ChatSearchPassage> Passages(string model) => new List<ChatSearchPassage>
            {
                new ChatSearchPassage { Id = user + "_0", MessageId = user, Position = 0,
                    Model = model, Text = "Visit Paris in June", Embedding = new byte[] { 1, 2, 3, 4 } }
            };
            await store.SaveChatEmbeddingsAsync(user, Passages("model-a"));
            var reopened = new ChatStore(_stores.Last().Folder);
            var found = await reopened.GetChatEmbeddingsAsync();
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Title, Is.EqualTo("Trip decisions"));
            Assert.That(found[0].Role, Is.EqualTo("user"));
            Assert.That(found[0].ConversationId, Is.EqualTo(chat));
            Assert.That(found[0].Text, Is.EqualTo("Visit Paris in June"));
            Assert.That(found[0].Embedding, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            await reopened.SaveChatEmbeddingsAsync(user, Passages("model-b"));
            found = await store.GetChatEmbeddingsAsync();
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Model, Is.EqualTo("model-b"));
            await store.ClearChatEmbeddingsAsync();
            Assert.That(await reopened.GetChatEmbeddingsAsync(), Is.Empty);
            Assert.That(await store.GetMessagesAsync(chat), Has.Count.EqualTo(3));
            await store.SaveChatEmbeddingsAsync(user, Passages("model-b"));
            await store.DeleteConversationAsync(chat);
            Assert.That(await reopened.GetChatEmbeddingsAsync(), Is.Empty);
            // An in-flight embedding must not resurrect a deleted message's index.
            await reopened.SaveChatEmbeddingsAsync(user, Passages("model-b"));
            Assert.That(await store.GetChatEmbeddingsAsync(), Is.Empty);
            Assert.That(await store.GetSearchMessagesAsync(), Is.Empty);
        }

        [Test]
        public async Task IncrementalIndexRebuildsForNewModelAndResumesAfterCancellation()
        {
            var store = CreateStore();
            var index = new ChatSearchIndex(store);
            int chat = await store.CreateConversationAsync("Long chat", "Gemma4E2B");
            await store.AddMessageAsync(chat, MessageRecord.UserRole, new string('a', 1900), null);
            using var cancelled = new CancellationTokenSource();
            int calls = 0;
            Assert.ThrowsAsync<OperationCanceledException>(async () => await index.IndexAsync("model-a", (text, token) =>
            {
                if (++calls == 2) cancelled.Cancel();
                return Task.FromResult(new[] { 1f, 0f });
            }, null, cancelled.Token));
            Assert.That(await store.GetChatEmbeddingsAsync(), Is.Empty);
            calls = 0;
            Task<float[]> Embed(string text, CancellationToken token) { calls++; return Task.FromResult(new[] { 1f, 0f }); }
            await index.IndexAsync("model-a", Embed, null, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(3));
            await index.IndexAsync("model-a", Embed, null, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(3), "Unchanged messages must not be re-embedded.");
            await store.AddMessageAsync(chat, MessageRecord.AssistantRole, "New reply", null);
            await index.IndexAsync("model-a", Embed, null, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(4));
            await index.IndexAsync("model-b", Embed, null, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(8));
            Assert.That((await store.GetChatEmbeddingsAsync()).All(e => e.Model == "model-b"));
        }

        [Test]
        public async Task SearchFiltersDatesCurrentChatModelAndUnrelatedResults()
        {
            var store = CreateStore();
            var index = new ChatSearchIndex(store);
            int trip = await store.CreateConversationAsync("Travel", "Gemma4E2B");
            int cooking = await store.CreateConversationAsync("Recipes", "Gemma4E2B");
            await store.AddMessageAsync(trip, MessageRecord.UserRole, "Visit Paris", null);
            await store.AddMessageAsync(cooking, MessageRecord.AssistantRole, "Bake bread", null);
            Task<float[]> Embed(string text, CancellationToken token) => Task.FromResult(
                text.Contains("Paris") ? new[] { 1f, 0f, 0f } : new[] { 0f, 1f, 0f });
            await index.IndexAsync("model-a", Embed, null, CancellationToken.None);
            var query = new ChatSearchQuery("Paris");
            var found = await index.SearchAsync("model-a", query, null, Embed, CancellationToken.None);
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Title, Is.EqualTo("Travel"));
            Assert.That(await index.SearchAsync("model-a", query, trip, Embed, CancellationToken.None), Is.Empty);
            Assert.That(await index.SearchAsync("model-b", query, null, Embed, CancellationToken.None), Is.Empty);
            query.To = DateTime.Now.AddDays(-2);
            Assert.That(await index.SearchAsync("model-a", query, null, Embed, CancellationToken.None), Is.Empty);
            query.To = null;
            query.From = DateTime.Now.AddDays(2);
            Assert.That(await index.SearchAsync("model-a", query, null, Embed, CancellationToken.None), Is.Empty);
            query.From = null;
            Assert.That(await index.SearchAsync("model-a", query, null,
                (text, token) => Task.FromResult(new[] { 0f, 0f, 1f }), CancellationToken.None), Is.Empty);
            var dateOnly = ToQuery(ChatSearch.Parse("today", DateTime.Now));
            Assert.That(await index.SearchAsync("model-a", dateOnly, null,
                (text, token) => throw new Exception("A date-only search must not embed an empty query."), CancellationToken.None),
                Has.Count.EqualTo(2));
        }

#if !TEST_MODELS
        [Ignore("Opt in with -p:TestModels=true; loads the embedding and Gemma 4 E2B models.")]
#endif
        [Test]
        public async Task RealModelsRetrievePastChatThroughAgentTool()
        {
            var store = CreateStore();
            var index = new ChatSearchIndex(store);
            int trip = await store.CreateConversationAsync("Paris travel decisions", "Gemma4E2B");
            await store.AddMessageAsync(trip, MessageRecord.UserRole, "For Paris, let's travel by train on June 12 and stay at Hotel Lantern.", null);
            await store.AddMessageAsync(trip, MessageRecord.AssistantRole, "Agreed: the train on June 12, with a room at Hotel Lantern in Paris.", null);
            int food = await store.CreateConversationAsync("Dinner preferences", "Gemma4E2B");
            await store.AddMessageAsync(food, MessageRecord.UserRole, "I am vegetarian and prefer mushroom risotto for dinner.", null);
            int code = await store.CreateConversationAsync("Software project", "Gemma4E2B");
            await store.AddMessageAsync(code, MessageRecord.AssistantRole, "Use SQLite transactions to update database records atomically.", null);
            using var embedding = new EmbeddingGemma2TextVision();
            await embedding.Init(configureSettings: settings => settings.NumThreads = 4);
            Task<float[]> Document(string text, CancellationToken token) => Task.Run(() => embedding.EmbedDocuments(new[] { text })[0], token);
            Task<float[]> Query(string text, CancellationToken token) => Task.Run(() => embedding.EmbedQuery(text), token);
            const string modelId = "embedding-440m|chat-v1";
            await index.IndexAsync(modelId, Document, null, CancellationToken.None);
            foreach (var example in new[] { ("How are we getting to Paris?", trip), ("What food do I like?", food) })
            {
                var matches = await index.SearchAsync(modelId, ToQuery(ChatSearch.Parse(example.Item1, DateTime.Now)), null, Query, CancellationToken.None);
                Console.WriteLine(example.Item1 + " -> " + string.Join("; ", matches.Select(e => e.Title + ": " + e.Text)));
                Assert.That(matches, Is.Not.Empty);
                Assert.That(matches[0].ConversationId, Is.EqualTo(example.Item2));
            }
            int calls = 0;
            using var language = new Gemma4E2B();
            await language.Init(configureSettings: settings => { settings.NumThreads = 4; settings.MaxNumTokens = 4096; });
            using var client = new ChatClientBuilder(language.AsIChatClient()).UseFunctionInvocation().Build();
            var options = new ChatOptions
            {
                Tools = new List<AITool> { AgentTools.SearchChats(async (request, token) =>
                {
                    calls++;
                    var matches = await index.SearchAsync(modelId, ToQuery(ChatSearch.Parse(request, DateTime.Now)), null, Query, token);
                    return string.Join("\n", matches.Select(e => e.Title + " | " + e.Created.ToString("yyyy-MM-dd") + " | " + e.Text));
                }) },
                MaxOutputTokens = 128,
                Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None }
            };
            var response = await client.GetResponseAsync("What travel arrangements did we decide for Paris in a previous chat? Search my saved chats.", options);
            Console.WriteLine("search_chats calls: " + calls + "; answer: " + response.Text);
            Assert.That(calls, Is.GreaterThan(0));
            Assert.That(response.Text, Does.Contain("train").IgnoreCase);
            Assert.That(response.Text, Does.Contain("Lantern").IgnoreCase);
        }
    }
}
