// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Maui.Demo.Lite;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using SQLite;

namespace Emgu.TF.Lite.Test
{
    // Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore: saving exchanges, reading them back, compaction, private chats, and
    // reading the chats the MAUI demo's ChatStore saved. No model.
    [TestFixture]
    public class UnitTestChatHistoryStore
    {
        private string _folder;
        private ChatHistoryStore _store;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-history-test-" + Guid.NewGuid().ToString("N"));
            _store = new ChatHistoryStore(_folder);
        }

        [TearDown]
        public async Task Cleanup()
        {
            await _store.CloseAsync();
            SQLiteAsyncConnection.ResetPool();
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        private static readonly byte[] Wav = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 4, 5 };

        private static ChatMessageItem Reply(string text, string thinking = null, params string[] steps)
        {
            var reply = new ChatMessageItem(ChatMessageRole.Assistant, text) { Thinking = thinking ?? string.Empty };
            foreach (string step in steps)
                reply.Steps.Add(new ChatToolStep(step));
            return reply;
        }

        [Test]
        public async Task SavesAnExchangeAndReadsItBack()
        {
            int changes = 0;
            _store.ChatsChanged += (s, e) => changes++;
            var message = new ChatMessageItem(ChatMessageRole.User, "What is in this picture?",
                new[] { new DataContent(Jpeg, "image/jpeg"), new DataContent(Wav, "audio/wav") }, "Excerpt: a fox.")
            {
                Transcript = "What is in this picture"
            };
            ChatMessageItem reply = Reply("A fox in the snow.", "It looks like a fox.", "Looked in report.pdf", "Searching Wikipedia for “fox”");
            reply.ThinkingDuration = TimeSpan.FromSeconds(4.5);

            int chatId = await _store.CreateChatAsync(ChatHistoryStore.TitleOf(message), "Gemma4E2B");
            ChatSavedExchange saved = await _store.SaveExchangeAsync(chatId, message, reply);

            Assert.That(changes, Is.EqualTo(2));
            Assert.That(saved.ReplyId, Is.GreaterThan(saved.MessageId + 2), "the two step lines are saved between them");
            Assert.That(_store.GetMessageId(message), Is.EqualTo(saved.MessageId));
            Assert.That(_store.GetMessageId(reply), Is.EqualTo(saved.ReplyId));
            ChatRecord chat = (await _store.GetChatsAsync()).Single();
            Assert.That(chat.Title, Is.EqualTo("What is in this picture?"));
            Assert.That(chat.Model, Is.EqualTo("Gemma4E2B"));

            ChatHistory history = await _store.LoadAsync(chatId);
            Assert.That(history.Messages.Select(m => m.Role), Is.EqualTo(new[] { ChatMessageRole.User, ChatMessageRole.Assistant }));
            ChatMessageItem user = history.Messages[0], answer = history.Messages[1];
            Assert.That(user.Text, Is.EqualTo("What is in this picture?"));
            Assert.That(user.Context, Is.EqualTo("Excerpt: a fox."));
            Assert.That(user.Transcript, Is.EqualTo("What is in this picture"));
            Assert.That(user.Attachments.Select(a => a.MediaType), Is.EqualTo(new[] { "image/jpeg", "audio/wav" }));
            Assert.That(user.Attachments[0].Data.ToArray(), Is.EqualTo(Jpeg));
            Assert.That(user.Attachments[1].Data.ToArray(), Is.EqualTo(Wav));
            Assert.That(answer.Text, Is.EqualTo("A fox in the snow."));
            Assert.That(answer.Thinking, Is.EqualTo("It looks like a fox."));
            // How long it thought is kept too (since 2026-10-26: "Thought for 5 s" after a reopen, not "Thoughts").
            Assert.That(answer.ThinkingDuration, Is.EqualTo(TimeSpan.FromSeconds(4.5)));
            Assert.That(answer.Steps.Select(s => s.Description), Is.EqualTo(new[] { "Looked in report.pdf", "Searching Wikipedia for “fox”" }));
            Assert.That(history.Summary, Is.Null);
            Assert.That(history.ContextStart, Is.EqualTo(0));
            Assert.That(history.RecordOf(user).Id, Is.EqualTo(saved.MessageId));
            Assert.That(history.AttachmentsOf(user).Select(a => a.Kind), Is.EqualTo(new[] { "image", "audio" }));
            Assert.That(_store.GetMessageId(answer), Is.EqualTo(saved.ReplyId), "loaded messages are known too");

            // Shown in a conversation, which goes on from there.
            var conversation = new ChatConversation(new ThrowingClient());
            history.Load(conversation);
            Assert.That(conversation.Messages, Is.EqualTo(history.Messages));
        }

        [Test]
        public async Task KeepsTheLanguageAndTheThinkingTranslation()
        {
            // The message's language and the reply's translated thinking (the library's TranslationFeature): saved with the
            // exchange when known, saved later with SaveMessageChangesAsync, and read back onto the items.
            int chatId = await _store.CreateChatAsync("¿Cuánto es 36 - 7?", null);
            var message = new ChatMessageItem(ChatMessageRole.User, "¿Cuánto es 36 - 7?") { Language = "es" };
            ChatMessageItem reply = Reply("29.", "36 - 7 = 29.");
            await _store.SaveExchangeAsync(chatId, message, reply);

            reply.Language = "es";
            reply.ThinkingTranslation = "36 - 7 = 29 (pensamiento).";
            await _store.SaveMessageChangesAsync(reply);
            await _store.SaveMessageChangesAsync(new ChatMessageItem(ChatMessageRole.User, "never saved"));   // nothing to update

            ChatHistory history = await _store.LoadAsync(chatId);
            Assert.That(history.Messages[0].Language, Is.EqualTo("es"));
            ChatMessageItem loaded = history.Messages[1];
            Assert.That((loaded.Language, loaded.ThinkingTranslation), Is.EqualTo(("es", "36 - 7 = 29 (pensamiento).")));
            Assert.That(loaded.ShowThinkingTranslation, Is.True, "a translated reply opens on its translation");
            Assert.That(loaded.ThinkingDisplayText, Is.EqualTo("36 - 7 = 29 (pensamiento)."));
            loaded.ShowThinkingTranslation = false;
            Assert.That(loaded.ThinkingDisplayText, Is.Null, "the English again");

            _store.IsPrivate = true;
            loaded.ThinkingTranslation = "private";
            await _store.SaveMessageChangesAsync(loaded);
            _store.IsPrivate = false;
            Assert.That((await _store.LoadAsync(chatId)).Messages[1].ThinkingTranslation, Is.EqualTo("36 - 7 = 29 (pensamiento)."), "nothing saved while private");
        }

        [Test]
        public async Task SavesAReplysOwnFilesWithoutBeingGivenThem()
        {
            // A feature kept a document with the reply (ChatMessageItem.SavedFiles, through the page's AddCardToReply):
            // the store saves it with the reply by default - the binding's path - and reads it back.
            int chatId = await _store.CreateChatAsync("Write a packing list", null);
            var message = new ChatMessageItem(ChatMessageRole.User, "Write a packing list");
            ChatMessageItem reply = Reply("Here is your packing list.");
            byte[] docx = { 0x50, 0x4B, 3, 4 };
            reply.SavedFiles.Add(new ChatSavedFile("written_document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document; name=Packing list.docx", docx));
            await _store.SaveExchangeAsync(chatId, message, reply);

            ChatHistory history = await _store.LoadAsync(chatId);
            ChatSavedFile file = history.Messages[1].SavedFiles.Single();
            Assert.That(file.Kind, Is.EqualTo("written_document"));
            Assert.That(file.MediaType, Does.EndWith("name=Packing list.docx"));
            Assert.That(file.Data, Is.EqualTo(docx));
            Assert.That(history.Messages[1].Attachments, Is.Empty);

            // A regenerated reply replaces the file with its own.
            ChatMessageItem again = Reply("A shorter list.");
            again.SavedFiles.Add(new ChatSavedFile("written_document", "text/markdown; name=Packing list.md", new byte[] { 1 }));
            await _store.ReplaceReplyAsync(chatId, message, again);
            history = await _store.LoadAsync(chatId);
            Assert.That(history.Messages, Has.Count.EqualTo(2));
            Assert.That(history.Messages[1].SavedFiles.Single().MediaType, Is.EqualTo("text/markdown; name=Packing list.md"));
        }

        [Test]
        public async Task KeepsAnAppsOwnFilesWithAReplyAndDeletesAChatWithItsFiles()
        {
            int chatId = await _store.CreateChatAsync("Draw a fox", null);
            var message = new ChatMessageItem(ChatMessageRole.User, "Draw a fox");
            byte[] png = { 0x89, (byte)'P', (byte)'N', (byte)'G' };
            await _store.SaveExchangeAsync(chatId, message, Reply("Here is a fox."), new[] { ("generated", "image/png", png) });
            int other = await _store.CreateChatAsync("Another", null);
            await _store.SaveExchangeAsync(other, new ChatMessageItem(ChatMessageRole.User, "Hi"), Reply("Hello."));

            ChatHistory history = await _store.LoadAsync(chatId);
            ChatMessageItem reply = history.Messages[1];
            Assert.That(reply.Attachments, Is.Empty, "the app's own files aren't given to the model");
            ChatSavedAttachment picture = history.AttachmentsOf(reply).Single();
            Assert.That(picture.Kind, Is.EqualTo("generated"));
            Assert.That(picture.MediaType, Is.EqualTo("image/png"));
            Assert.That(File.ReadAllBytes(picture.Path), Is.EqualTo(png));
            // And read back as the reply's own files, for the app to show again (since 2026-10-26).
            ChatSavedFile file = reply.SavedFiles.Single();
            Assert.That((file.Kind, file.MediaType, file.Data), Is.EqualTo(("generated", "image/png", png)));
            Assert.That(history.Messages[0].SavedFiles, Is.Empty);
            string folder = Path.GetDirectoryName(picture.Path);

            await _store.DeleteChatAsync(chatId);
            Assert.That(Directory.Exists(folder), Is.False);
            Assert.That(await _store.LoadAsync(chatId), Is.Null);
            Assert.That(await _store.GetMessagesAsync<ChatMessageRecord>(chatId), Is.Empty);
            Assert.That((await _store.GetChatsAsync()).Select(c => c.Id), Is.EqualTo(new[] { other }));
        }

        [Test]
        public async Task KeepsWhereTheModelsMemoryStarts()
        {
            int chatId = await _store.CreateChatAsync("Tea", null);
            var messages = new List<ChatMessageItem>();
            for (int i = 0; i < 3; i++)
            {
                var message = new ChatMessageItem(ChatMessageRole.User, "Question " + i);
                ChatMessageItem reply = Reply("Answer " + i, null, "A step of " + i);
                await _store.SaveExchangeAsync(chatId, message, reply);
                messages.Add(message);
                messages.Add(reply);
            }

            // Summarized up to the third question.
            var conversation = new ChatConversation(new ThrowingClient());
            conversation.Load(messages, "The user asked two questions.", 4);
            await _store.SaveCompactionAsync(chatId, conversation);
            ChatHistory history = await _store.LoadAsync(chatId);
            Assert.That(history.Summary, Is.EqualTo("The user asked two questions."));
            Assert.That(history.ContextStart, Is.EqualTo(4));
            Assert.That(history.Messages[4].Text, Is.EqualTo("Question 2"));
            Assert.That(history.Messages[5].Steps.Single().Description, Is.EqualTo("A step of 2"));

            // Dropped (no summary), from the loaded history's own messages.
            var reopened = new ChatConversation(new ThrowingClient());
            reopened.Load(history.Messages, null, 2);
            await _store.SaveCompactionAsync(chatId, reopened);
            history = await _store.LoadAsync(chatId);
            Assert.That(history.Summary, Is.Null);
            Assert.That(history.ContextStart, Is.EqualTo(2));
        }

        [Test]
        public async Task SavesNothingInAPrivateChat()
        {
            int chatId = await _store.CreateChatAsync("Saved before", null);
            await _store.SaveExchangeAsync(chatId, new ChatMessageItem(ChatMessageRole.User, "Hi"), Reply("Hello."));
            _store.IsPrivate = true;

            Assert.ThrowsAsync<InvalidOperationException>(() => _store.CreateChatAsync("Secret", null));
            Assert.ThrowsAsync<InvalidOperationException>(() => _store.SaveExchangeAsync(chatId, new ChatMessageItem(ChatMessageRole.User, "Secret"), Reply("Ok.")));
            await _store.SetTranscriptAsync(chatId, 1, "secret words");
            await _store.SetSummaryAsync(chatId, "secret summary", 1);
            Assert.That(await _store.GetChatsAsync(), Is.Empty);
            Assert.That(await _store.LoadAsync(chatId), Is.Null);

            _store.IsPrivate = false;
            ChatHistory history = await _store.LoadAsync(chatId);
            Assert.That(history.Messages.Select(m => m.Text), Is.EqualTo(new[] { "Hi", "Hello." }));
            Assert.That(history.Messages[0].Transcript, Is.Null);
            Assert.That(history.Summary, Is.Null);
        }

        [Test]
        public async Task TitlesAVoiceMessageChatWithItsWords()
        {
            var message = new ChatMessageItem(ChatMessageRole.User, string.Empty, new[] { new DataContent(Wav, "audio/wav") });
            Assert.That(ChatHistoryStore.TitleOf(message), Is.EqualTo("Voice message"));
            Assert.That(ChatHistoryStore.TitleOf(new ChatMessageItem(ChatMessageRole.User, "", new[] { new DataContent(Jpeg, "image/jpeg") })), Is.EqualTo("Image"));
            int chatId = await _store.CreateChatAsync(ChatHistoryStore.TitleOf(message), null);
            ChatSavedExchange saved = await _store.SaveExchangeAsync(chatId, message, Reply("Rome."));

            await _store.SetTranscriptAsync(chatId, saved.MessageId, "  What is the capital of Italy?  ");
            Assert.That((await _store.GetChatAsync(chatId)).Title, Is.EqualTo("What is the capital of Italy?"));
            Assert.That((await _store.LoadAsync(chatId)).Messages[0].Transcript, Is.EqualTo("What is the capital of Italy?"));
            // Only a chat still called "Voice message" is renamed.
            await _store.SetTranscriptAsync(chatId, saved.MessageId, "Something else");
            Assert.That((await _store.GetChatAsync(chatId)).Title, Is.EqualTo("What is the capital of Italy?"));
            Assert.That(ChatHistoryStore.Shorten(new string('a', 70), 60), Has.Length.EqualTo(60).And.EndsWith("…"));
        }

        // An app's own column on a message.
        [Table("Message")]
        public class RatedMessageRecord : ChatMessageRecord
        {
            public string Rating { get; set; }
        }

        private sealed class AppStore : ChatHistoryStore
        {
            public AppStore(string folder) : base(folder) { }

            protected override async Task CreateTablesAsync(SQLiteAsyncConnection connection)
            {
                await connection.CreateTableAsync<RatedMessageRecord>();
            }
        }

        [Test]
        public async Task KeepsAnAppsOwnColumns()
        {
            await _store.CloseAsync();
            _store = new AppStore(_folder);
            int chatId = await _store.CreateChatAsync("Hola", null);
            ChatSavedExchange saved = await _store.SaveExchangeAsync(chatId, new ChatMessageItem(ChatMessageRole.User, "Hola"), Reply("¡Hola!"));
            SQLiteAsyncConnection db = await _store.GetConnectionAsync();
            await db.ExecuteAsync("UPDATE Message SET Rating = ? WHERE Id = ?", "good", saved.MessageId);

            ChatHistory history = await _store.LoadAsync<RatedMessageRecord>(chatId);
            Assert.That(((RatedMessageRecord)history.RecordOf(history.Messages[0])).Rating, Is.EqualTo("good"));
            Assert.That(await db.ExecuteScalarAsync<int>("SELECT Version FROM SchemaVersion WHERE Name = ?", "Emgu.LiteRT.LM.Chat.Sqlite"),
                Is.EqualTo(ChatHistoryStore.SchemaVersion));
        }

        // The demo's chat page saved its chats with its own ChatStore (linked into the tests) before this package existed:
        // they must open unchanged.
        [Test]
        public async Task ReadsTheChatsTheDemoSaved()
        {
            await _store.CloseAsync();
            var demo = new ChatStore(_folder);
            int chatId = await demo.CreateConversationAsync("Voice message", "Gemma4E2B");
            int first = await demo.AddMessageAsync(chatId, MessageRecord.UserRole, "", null,
                new List<(string, string, byte[])> { ("audio", "audio/wav", Wav) }, null, "en");
            await demo.SetTranscriptAsync(chatId, first, "How far is the Moon?");
            await demo.AddMessageAsync(chatId, MessageRecord.StepRole, "Searching Wikipedia for “Moon”", null);
            int firstReply = await demo.AddMessageAsync(chatId, MessageRecord.AssistantRole, "About 384,400 km.", "The Moon...");
            await demo.AddMessageAsync(chatId, MessageRecord.UserRole, "And the Sun?", null, null, "Excerpt: the Sun.");
            await demo.AddMessageAsync(chatId, MessageRecord.AssistantRole, "About 150 million km.", null,
                new List<(string, string, byte[])> { ("generated", "image/png", new byte[] { 1, 2 }) });
            await demo.SetSummaryAsync(chatId, "The user asked how far the Moon is.", firstReply);
            await demo.CloseAsync();
            SQLiteAsyncConnection.ResetPool();

            _store = new ChatHistoryStore(_folder);
            ChatRecord chat = (await _store.GetChatsAsync()).Single();
            Assert.That(chat.Title, Is.EqualTo("How far is the Moon?"));
            Assert.That(chat.Model, Is.EqualTo("Gemma4E2B"));
            Assert.That(chat.Updated, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
            ChatHistory history = await _store.LoadAsync(chatId);
            Assert.That(history.Messages.Select(m => m.Text), Is.EqualTo(new[] { "", "About 384,400 km.", "And the Sun?", "About 150 million km." }));
            Assert.That(history.Messages[0].Attachments.Single().Data.ToArray(), Is.EqualTo(Wav));
            Assert.That(history.Messages[0].Transcript, Is.EqualTo("How far is the Moon?"));
            Assert.That(history.Messages[1].Thinking, Is.EqualTo("The Moon..."));
            Assert.That(history.Messages[1].Steps.Single().Description, Is.EqualTo("Searching Wikipedia for “Moon”"));
            Assert.That(history.Messages[2].Context, Is.EqualTo("Excerpt: the Sun."));
            Assert.That(history.AttachmentsOf(history.Messages[3]).Single().Kind, Is.EqualTo("generated"));
            Assert.That(history.Summary, Is.EqualTo("The user asked how far the Moon is."));
            Assert.That(history.ContextStart, Is.EqualTo(2));

            // And the demo still reads the chat after the store added its schema table and a new exchange.
            await _store.SaveExchangeAsync(chatId, new ChatMessageItem(ChatMessageRole.User, "Thanks"), Reply("You're welcome."));
            await _store.CloseAsync();
            SQLiteAsyncConnection.ResetPool();
            demo = new ChatStore(_folder);
            List<MessageRecord> rows = await demo.GetMessagesAsync(chatId);
            Assert.That(rows.Select(r => r.Text).TakeLast(2), Is.EqualTo(new[] { "Thanks", "You're welcome." }));
            Assert.That(rows.First().Language, Is.EqualTo("en"));
            await demo.CloseAsync();
            SQLiteAsyncConnection.ResetPool();
            _store = new ChatHistoryStore(_folder);
        }

        // ---------- Attach: saving a conversation as it goes on ----------

        // Streams the next scripted reply; answers summary requests (their prompt starts with "Summarize").
        private sealed class ReplyingClient : IChatClient
        {
            public readonly Queue<string> Replies = new Queue<string>();

            public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                List<ChatMessage> list = messages.ToList();
                if (list.Count == 1 && list[0].Text.StartsWith("Summarize", StringComparison.Ordinal))
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, "The user said hello twice."));
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, Replies.Dequeue()));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, Replies.Dequeue()) { MessageId = "a" };
            }

            public object GetService(Type serviceType, object serviceKey = null) => null;

            public void Dispose()
            {
            }
        }

        [Test]
        public async Task SavesAnAttachedConversationAsItGoesOn()
        {
            var client = new ReplyingClient();
            client.Replies.Enqueue("Hello!");
            client.Replies.Enqueue("Hello again!");
            var conversation = new ChatConversation(client);
            int created = 0;
            using (ChatHistoryBinding saving = _store.Attach(conversation, model: "Qwen3"))
            {
                saving.ChatCreated += (s, e) => created++;
                await conversation.SendAsync("Hi");
                await conversation.SendAsync("Hi again");
                await saving.FlushAsync();
                Assert.That(created, Is.EqualTo(1));
                Assert.That(saving.ChatId, Is.GreaterThan(0));
                ChatRecord chat = await _store.GetChatAsync(saving.ChatId);
                Assert.That(chat.Title, Is.EqualTo("Hi"));
                Assert.That(chat.Model, Is.EqualTo("Qwen3"));

                // Compacted: everything but the latest exchange is summarized.
                Assert.That(await conversation.CompactAsync(), Is.True);
                await saving.FlushAsync();
                ChatHistory history = await _store.LoadAsync(saving.ChatId);
                Assert.That(history.Messages.Select(m => m.Text), Is.EqualTo(new[] { "Hi", "Hello!", "Hi again", "Hello again!" }));
                Assert.That(history.Summary, Is.EqualTo("The user said hello twice."));
                Assert.That(history.ContextStart, Is.EqualTo(conversation.ContextStart));

                // Reopened in another conversation and continued in the same chat.
                var reopened = new ChatConversation(client);
                history.Load(reopened);
                using (ChatHistoryBinding continuing = _store.Attach(reopened, saving.ChatId))
                {
                    client.Replies.Enqueue("Goodbye!");
                    await reopened.SendAsync("Bye");
                    await continuing.FlushAsync();
                }
                Assert.That((await _store.LoadAsync(saving.ChatId)).Messages.Count, Is.EqualTo(6));
                Assert.That(await _store.GetChatsAsync(), Has.Count.EqualTo(1));
            }

            // Not attached any more: nothing more is saved.
            client.Replies.Enqueue("Not saved.");
            await conversation.SendAsync("Anyone there?");
            Assert.That((await _store.GetChatsAsync()).Single().Id, Is.Not.Zero);
            Assert.That((await _store.LoadAsync((await _store.GetChatsAsync()).Single().Id)).Messages.Count, Is.EqualTo(6));
        }

        [Test]
        public async Task WorksAsAnIChatHistoryStore()
        {
            // What a chat UI (Emgu.LiteRT.LM.Maui's ChatPage) uses, without the records.
            IChatHistoryStore store = _store;
            var client = new ReplyingClient();
            client.Replies.Enqueue("Hello!");
            var conversation = new ChatConversation(client);
            int changed = 0;
            store.ChatsChanged += (s, e) => changed++;
            using (IChatHistoryBinding saving = store.Attach(conversation, model: "Qwen3"))
            {
                await conversation.SendAsync("Hi");
                await ((ChatHistoryBinding)saving).FlushAsync();
                Assert.That(saving.ChatId, Is.GreaterThan(0));
                IReadOnlyList<ChatHistoryEntry> chats = await store.ListChatsAsync();
                Assert.That(chats.Select(c => (c.Id, c.Title, c.Model)), Is.EqualTo(new[] { (saving.ChatId, "Hi", "Qwen3") }));
                var reopened = new ChatConversation(client);
                Assert.That(await store.LoadChatAsync(saving.ChatId, reopened), Is.True);
                Assert.That(reopened.Messages.Select(m => m.Text), Is.EqualTo(new[] { "Hi", "Hello!" }));
                Assert.That(await store.LoadChatAsync(saving.ChatId + 100, reopened), Is.False);
                await store.DeleteChatAsync(saving.ChatId);
                Assert.That(await store.ListChatsAsync(), Is.Empty);
            }
            Assert.That(changed, Is.GreaterThanOrEqualTo(2));
            store.IsPrivate = true;
            Assert.That(_store.IsPrivate, Is.True);
        }

        // A synchronization context like a UI thread's: what is posted to it runs on the thread that pumps it.
        private sealed class PumpedContext : System.Threading.SynchronizationContext
        {
            private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object)> _queue = new();

            public override void Post(SendOrPostCallback d, object state) => _queue.Add((d, state));

            // Run what is posted until the task completes.
            public T Pump<T>(Task<T> task)
            {
                while (!task.IsCompleted)
                    if (_queue.TryTake(out (SendOrPostCallback Callback, object State) item, 50))
                        item.Callback(item.State);
                return task.GetAwaiter().GetResult();
            }
        }

        [Test]
        public async Task LoadsAChatIntoTheConversationOnTheCallersContext()
        {
            // The MAUI page loads a saved chat into a conversation whose messages are bound to views, which may only change on
            // the UI thread: the messages must be added on the caller's synchronization context, not the database's thread
            // (a ConfigureAwait(false) before the load crashed the page on Android).
            var client = new ReplyingClient();
            client.Replies.Enqueue("Hello!");
            var conversation = new ChatConversation(client);
            int chatId;
            using (ChatHistoryBinding saving = _store.Attach(conversation))
            {
                await conversation.SendAsync("Hi");
                await saving.FlushAsync();
                chatId = saving.ChatId;
            }
            IChatHistoryStore store = _store;
            var reopened = new ChatConversation(client);
            var threads = new List<int>();
            reopened.Messages.CollectionChanged += (s, e) => threads.Add(Environment.CurrentManagedThreadId);
            var context = new PumpedContext();
            System.Threading.SynchronizationContext previous = System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                Assert.That(context.Pump(store.LoadChatAsync(chatId, reopened)), Is.True);
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(previous);
            }
            Assert.That(reopened.Messages.Count, Is.EqualTo(2));
            Assert.That(threads, Is.Not.Empty.And.All.EqualTo(Environment.CurrentManagedThreadId));
        }

        [Test]
        public async Task ReplacesARegeneratedReply()
        {
            var client = new ReplyingClient();
            client.Replies.Enqueue("First answer.");
            client.Replies.Enqueue("Better answer.");
            var conversation = new ChatConversation(client);
            using (ChatHistoryBinding saving = _store.Attach(conversation))
            {
                ChatMessageItem reply = await conversation.SendAsync("Question");
                reply.Steps.Add(new ChatToolStep("A step of the first answer"));
                await saving.FlushAsync();
                await conversation.RegenerateAsync(reply);
                await saving.FlushAsync();
                ChatHistory history = await _store.LoadAsync(saving.ChatId);
                Assert.That(history.Messages.Select(m => m.Text), Is.EqualTo(new[] { "Question", "Better answer." }));
                Assert.That(history.Messages[1].Steps, Is.Empty);
                Assert.That(await _store.GetMessagesAsync<ChatMessageRecord>(saving.ChatId), Has.Count.EqualTo(2));
            }
        }

        [Test]
        public async Task SavesATranscriptWrittenDownLater()
        {
            var client = new ReplyingClient();
            client.Replies.Enqueue("Rome.");
            var conversation = new ChatConversation(client);
            using (ChatHistoryBinding saving = _store.Attach(conversation))
            {
                await conversation.SendAsync(string.Empty, new[] { new DataContent(Wav, "audio/wav") });
                await saving.FlushAsync();
                Assert.That((await _store.GetChatAsync(saving.ChatId)).Title, Is.EqualTo("Voice message"));
                conversation.Messages[0].Transcript = "What is the capital of Italy?";
                await saving.FlushAsync();
                Assert.That((await _store.LoadAsync(saving.ChatId)).Messages[0].Transcript, Is.EqualTo("What is the capital of Italy?"));
                Assert.That((await _store.GetChatAsync(saving.ChatId)).Title, Is.EqualTo("What is the capital of Italy?"));
            }
        }

        [Test]
        public async Task SavesNothingOfAnAttachedPrivateConversation()
        {
            var client = new ReplyingClient();
            client.Replies.Enqueue("Hello.");
            var conversation = new ChatConversation(client);
            _store.IsPrivate = true;
            Exception failed = null;
            using (ChatHistoryBinding saving = _store.Attach(conversation))
            {
                saving.SaveFailed += (s, e) => failed = e;
                await conversation.SendAsync("Secret");
                await saving.FlushAsync();
                Assert.That(saving.ChatId, Is.Zero);
            }
            Assert.That(failed, Is.Null);
            _store.IsPrivate = false;
            Assert.That(await _store.GetChatsAsync(), Is.Empty);
        }

        [Test]
        public async Task KeepsTheAgentsMemoriesAndGoalsInTheChatDatabase()
        {
            var memories = new SqliteAgentMemoryStore(_store);
            Assert.That(await memories.AddMemoryAsync(" The user is vegetarian. "), Is.True);
            Assert.That(await memories.AddMemoryAsync("the user is VEGETARIAN."), Is.False);
            Assert.That(await memories.AddMemoryAsync("  "), Is.False);
            Assert.That(await memories.AddMemoryAsync("The user lives in Lyon."), Is.True);
            IList<AgentMemory> saved = await memories.GetMemoriesAsync();
            Assert.That(saved.Select(m => m.Text), Is.EqualTo(new[] { "The user is vegetarian.", "The user lives in Lyon." }));
            await memories.DeleteMemoryAsync(saved[0].Id);
            await memories.DeleteMemoryAsync("not an id");
            Assert.That((await memories.GetMemoriesAsync()).Single().Text, Is.EqualTo("The user lives in Lyon."));

            var goals = new SqliteAgentGoalStore(_store);
            var created = new DateTime(2026, 10, 1, 9, 0, 0);
            string id = await goals.AddGoalAsync(new AgentGoal(null, "Run three times a week", 3, "runs", GoalPeriod.Week, created));
            await goals.UpdateGoalAsync(new AgentGoal(id, "Run four times a week", 4, "runs", GoalPeriod.Week, created));
            await goals.AddProgressAsync(new GoalProgress(id, 1, created.AddDays(1)));
            await goals.AddProgressAsync(new GoalProgress(id, 1, created.AddDays(3)));
            AgentGoal goal = (await goals.GetGoalsAsync()).Single();
            Assert.That((goal.Text, goal.Target, goal.Period, goal.Created), Is.EqualTo(("Run four times a week", (double?)4, GoalPeriod.Week, created)));
            Assert.That((await goals.GetProgressAsync(id, created.AddDays(2))).Select(p => p.When), Is.EqualTo(new[] { created.AddDays(3) }));
            Assert.That(await goals.DeleteLastProgressAsync(id), Is.True);
            Assert.That((await goals.GetProgressAsync(id, DateTime.MinValue)).Single().When, Is.EqualTo(created.AddDays(1)));
            await goals.DeleteGoalAsync(id);
            Assert.That(await goals.GetGoalsAsync(), Is.Empty);

            // The demo's table names, in the chats' database; deleting a goal took its progress with it.
            SQLiteAsyncConnection db = await _store.GetConnectionAsync();
            Assert.That(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Memory"), Is.EqualTo(1));
            Assert.That(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GoalProgress"), Is.Zero);
        }

        [Test]
        public async Task ReadsTheMemoriesAndGoalsTheDemoSaved()
        {
            // The tables as the MAUI demo's ChatStore created them before the stores moved here.
            SQLiteAsyncConnection db = await _store.GetConnectionAsync();
            await db.ExecuteAsync("CREATE TABLE \"Memory\" (\"Id\" integer primary key autoincrement not null, \"Text\" varchar, \"Created\" bigint)");
            await db.ExecuteAsync("CREATE TABLE \"Goal\" (\"Id\" integer primary key autoincrement not null, \"Text\" varchar, \"Target\" float, \"Unit\" varchar, \"Period\" integer, \"Created\" bigint)");
            await db.ExecuteAsync("CREATE TABLE \"GoalProgress\" (\"Id\" integer primary key autoincrement not null, \"GoalId\" integer, \"Amount\" float, \"When\" bigint)");
            var when = new DateTime(2026, 9, 30, 18, 0, 0);
            await db.ExecuteAsync("INSERT INTO Memory (Text, Created) VALUES (?, ?)", "The user is vegetarian.", when.Ticks);
            await db.ExecuteAsync("INSERT INTO Goal (Text, Target, Unit, Period, Created) VALUES (?, ?, ?, ?, ?)", "Drink water", 8.0, "glasses", (int)GoalPeriod.Day, when.Ticks);
            await db.ExecuteAsync("INSERT INTO GoalProgress (GoalId, Amount, \"When\") VALUES (1, 2, ?)", when.AddHours(1).Ticks);

            Assert.That((await new SqliteAgentMemoryStore(_store).GetMemoriesAsync()).Single().Text, Is.EqualTo("The user is vegetarian."));
            var goals = new SqliteAgentGoalStore(_store);
            AgentGoal goal = (await goals.GetGoalsAsync()).Single();
            Assert.That((goal.Text, goal.Target, goal.Unit, goal.Period, goal.Created), Is.EqualTo(("Drink water", (double?)8, "glasses", GoalPeriod.Day, when)));
            Assert.That((await goals.GetProgressAsync(goal.Id, DateTime.MinValue)).Single().Amount, Is.EqualTo(2));
        }

        [Test]
        public async Task PrivateChatsNeitherReadNorSaveMemoriesOrGoals()
        {
            _store.IsPrivate = true;
            var memories = new SqliteAgentMemoryStore(_store);
            var goals = new SqliteAgentGoalStore(_store);
            Assert.That(await memories.AddMemoryAsync("Secret"), Is.False);
            Assert.That(await memories.GetMemoriesAsync(), Is.Empty);
            await memories.DeleteMemoryAsync("1");
            Assert.ThrowsAsync<InvalidOperationException>(() => goals.AddGoalAsync(new AgentGoal(null, "Secret", null, null, GoalPeriod.None, DateTime.Now)));
            Assert.ThrowsAsync<InvalidOperationException>(() => goals.AddProgressAsync(new GoalProgress("1", 1, DateTime.Now)));
            Assert.That(await goals.GetGoalsAsync(), Is.Empty);
            Assert.That(await goals.DeleteLastProgressAsync("1"), Is.False);
            Assert.That(Directory.Exists(_folder) && Directory.EnumerateFileSystemEntries(_folder).Any(), Is.False, "The database was opened.");
        }

        // A client that is never called (the conversations above are only loaded).
        private sealed class ThrowingClient : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException();

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions options = null, System.Threading.CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException();

            public object GetService(Type serviceType, object serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }
}
