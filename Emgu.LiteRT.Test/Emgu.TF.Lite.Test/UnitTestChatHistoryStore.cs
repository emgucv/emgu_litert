// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        public class LanguageMessageRecord : ChatMessageRecord
        {
            public string Language { get; set; }
        }

        private sealed class AppStore : ChatHistoryStore
        {
            public AppStore(string folder) : base(folder) { }

            protected override async Task CreateTablesAsync(SQLiteAsyncConnection connection)
            {
                await connection.CreateTableAsync<LanguageMessageRecord>();
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
            await db.ExecuteAsync("UPDATE Message SET Language = ? WHERE Id = ?", "es", saved.MessageId);

            ChatHistory history = await _store.LoadAsync<LanguageMessageRecord>(chatId);
            Assert.That(((LanguageMessageRecord)history.RecordOf(history.Messages[0])).Language, Is.EqualTo("es"));
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
