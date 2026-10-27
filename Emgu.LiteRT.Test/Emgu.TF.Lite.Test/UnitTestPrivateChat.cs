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
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    [TestFixture]
    public class UnitTestPrivateChat
    {
        private string _folder;
        private ChatHistoryStore _store;
        private SqliteAgentMemoryStore _memories;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "private-chat-test-" + Guid.NewGuid().ToString("N"));
            _store = new ChatHistoryStore(_folder);
            _memories = new SqliteAgentMemoryStore(_store);
        }

        [TearDown]
        public async Task Cleanup()
        {
            await _store.CloseAsync();
            if (Path.GetDirectoryName(Path.GetFullPath(_folder)) != Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
                || !Path.GetFileName(_folder).StartsWith("private-chat-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test directory.");
            StorageFiles.DeleteDirectory(TestContext.CurrentContext.WorkDirectory, _folder);
        }

        [Test]
        public async Task PrivatePersistenceGuardsDoNotCreateDatabaseOrAttachments()
        {
            _store.IsPrivate = true;
            Assert.ThrowsAsync<InvalidOperationException>(() => _store.CreateChatAsync("PRIVATE TITLE", "Gemma4E2B"));
            Assert.ThrowsAsync<InvalidOperationException>(() => _store.AddMessageAsync(1, ChatMessageRecord.UserRole, "PRIVATE TEXT", "PRIVATE THINKING",
                new[] { ("image", "image/png", new byte[] { 1, 2, 3 }) }));
            Assert.That(await _memories.AddMemoryAsync("PRIVATE MEMORY"), Is.False);
            await _memories.DeleteMemoryAsync("1");
            await _store.SetSummaryAsync(1, "PRIVATE SUMMARY", 1);
            await _store.SetChatDocumentAsync(1, "PRIVATE DOC");
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "PRIVATE DOC", Name = "private.pdf" },
                new[] { new DocumentItemRecord { DocumentId = "PRIVATE DOC", Text = "PRIVATE CONTENT", Data = new byte[] { 4 }, Embedding = new byte[64] } });
            await _store.SaveChatEmbeddingsAsync(1, new List<ChatSearchPassage> {
                new ChatSearchPassage { Id = "private-embedding", MessageId = 1, Text = "PRIVATE SEARCH", Embedding = new byte[64] } });
            Assert.That(await _memories.GetMemoriesAsync(), Is.Empty);
            Assert.That(await _store.GetMessagesAsync<ChatMessageRecord>(1), Is.Empty);
            Assert.That(await _store.GetSearchMessagesAsync(), Is.Empty);
            Assert.That(await _store.GetChatEmbeddingsAsync(), Is.Empty);
            Assert.That(await _store.GetDocumentAsync("PRIVATE DOC"), Is.Null);
            Assert.That(await _store.GetDocumentItemsAsync("PRIVATE DOC"), Is.Empty);
            Assert.That(Directory.EnumerateFileSystemEntries(_folder), Is.Empty);
        }

        [Test]
        public async Task PrivateModeCannotReadOrChangeNormalMemoriesDocumentsOrMessages()
        {
            int chat = await _store.CreateChatAsync("Normal chat", "Gemma4E2B");
            int message = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, "Normal text", null);
            await _memories.AddMemoryAsync("Normal memory");
            string memory = (await _memories.GetMemoriesAsync()).Single().Id;
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "normal", Name = "normal.pdf" },
                new[] { new DocumentItemRecord { DocumentId = "normal", Text = "Normal document" } });
            await _store.SetChatDocumentAsync(chat, "normal");
            await _store.SetSummaryAsync(chat, "Normal summary", message);
            byte[] ReadDatabase()
            {
                using var file = new FileStream(Path.Combine(_folder, "chats.db3"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var copy = new MemoryStream();
                file.CopyTo(copy);
                return copy.ToArray();
            }
            byte[] before = ReadDatabase();
            _store.IsPrivate = true;
            IAgentMemoryStore agentMemory = _memories;
            Assert.That(await agentMemory.GetMemoriesAsync(), Is.Empty);
            Assert.That(await agentMemory.AddMemoryAsync("Private fact"), Is.False);
            await agentMemory.DeleteMemoryAsync(memory);
            await _store.SetSummaryAsync(chat, "Private summary", 999);
            await _store.SetChatDocumentAsync(chat, null);
            await _store.DeleteUnusedDocumentsAsync();
            Assert.That(await _store.GetMessagesAsync<ChatMessageRecord>(chat), Is.Empty);
            Assert.That(await _store.GetDocumentAsync("normal"), Is.Null);
            Assert.That(ReadDatabase(), Is.EqualTo(before));
            _store.IsPrivate = false;
            Assert.That((await _memories.GetMemoriesAsync()).Single().Text, Is.EqualTo("Normal memory"));
            Assert.That((await _store.GetMessagesAsync<ChatMessageRecord>(chat)).Single().Text, Is.EqualTo("Normal text"));
            Assert.That((await _store.GetDocumentAsync("normal")).Name, Is.EqualTo("normal.pdf"));
            var restored = (await _store.GetChatsAsync()).Single();
            Assert.That(restored.Summary, Is.EqualTo("Normal summary"));
            Assert.That(restored.DocumentId, Is.EqualTo("normal"));
            int newChat = await _store.CreateChatAsync("New normal chat", "Gemma4E2B");
            await _store.AddMessageAsync(newChat, ChatMessageRecord.UserRole, "Normal saving works again", null);
            Assert.That((await _store.GetMessagesAsync<ChatMessageRecord>(newChat)).Single().Text, Is.EqualTo("Normal saving works again"));
        }

        [Test]
        public async Task PrivateSearchDoesNotRetrievePastChatsOrInvokeEmbeddingModel()
        {
            int chat = await _store.CreateChatAsync("Trip", "Gemma4E2B");
            int message = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, "Paris", null);
            await _store.SaveChatEmbeddingsAsync(message, new List<ChatSearchPassage> {
                new ChatSearchPassage { Id = "e", MessageId = message, Model = "model-a", Text = "Paris", Embedding = new byte[8] } });
            _store.IsPrivate = true;
            var found = await new ChatSearchIndex(_store).SearchAsync("model-a", new ChatSearchQuery("Paris"), null,
                (_, _) => throw new Exception("Private chat must not perform a semantic query."), CancellationToken.None);
            Assert.That(found, Is.Empty);
            await new ChatSearchIndex(_store).IndexAsync("model-a",
                (_, _) => throw new Exception("Private chat must not embed messages."), null, CancellationToken.None);
        }

        [Test]
        public void TemporaryPickerCleanupKeepsUserOriginalsOutsideCache()
        {
            string cache = Path.Combine(_folder, "cache"), original = Path.Combine(_folder, "original.jpg");
            Directory.CreateDirectory(cache);
            string copy = Path.Combine(cache, "picker-copy.jpg");
            File.WriteAllBytes(original, new byte[] { 1, 2 });
            File.WriteAllBytes(copy, new byte[] { 1, 2 });
            StorageFiles.DeleteTemporaryFile(cache, original);
            StorageFiles.DeleteTemporaryFile(cache, Path.Combine(cache, "..", "original.jpg"));
            Assert.That(File.Exists(original), Is.True);
            StorageFiles.DeleteTemporaryFile(cache, copy);
            Assert.That(File.Exists(copy), Is.False);
            Assert.That(File.Exists(original), Is.True);
        }
    }
}
