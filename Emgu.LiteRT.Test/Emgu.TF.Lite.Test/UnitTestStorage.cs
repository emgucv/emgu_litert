// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.Util;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    [TestFixture]
    public class UnitTestStorage
    {
        private string _folder, _models, _image, _exports;
        private ChatStore _store;
        private StorageCatalog _catalog;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "storage-test-" + Guid.NewGuid().ToString("N"));
            _models = Path.Combine(_folder, "models");
            _image = Path.Combine(_folder, "image");
            _exports = Path.Combine(_folder, "exports");
            Directory.CreateDirectory(_models);
            _store = new ChatStore(Path.Combine(_folder, "chats"));
            _catalog = new StorageCatalog(_store, _models, _exports,
                new[] { new StorageModel("Chat A", "a.litertlm"), new StorageModel("Search B", "embedding-b.litertlm", isSearch: true) { Download = new DownloadableFile("https://example.com/embedding-b.litertlm", "storage-test") } },
                new[] { new StorageFolder("image", "Z-Image-Turbo", _image, "Image model, host tensors, and prepared weight caches.") });
        }

        [TearDown]
        public async Task Cleanup()
        {
            await _store.CloseAsync();
            string folder = Path.GetFullPath(_folder);
            if (Path.GetDirectoryName(folder) != Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
                || !Path.GetFileName(folder).StartsWith("storage-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test directory.");
            StorageFiles.DeleteDirectory(TestContext.CurrentContext.WorkDirectory, folder);
        }

        private static void FileWithSize(string path, int size)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[size]);
        }

        [Test]
        public async Task ModelDeletionIncludesOnlyItsCachesMarkersAndPartialDownloads()
        {
            string[] owned = { "a.litertlm", "a.litertlm.sha256ok", "a.litertlm.part", "a.litertlm.part.etag",
                "a.litertlm.part.sha256ok", "a.litertlm_123_456.xnnpack_cache", "a.litertlm_12_45.vision_encoder.xnnpack_cache",
                "a.litertlm_123_456_mldrift_program_cache.bin", "a.litertlm_123_456.mldrift_weight_cache.vision_encoder.bin" };
            string[] unrelated = { "a.litertlm.backup", "a.litertlm2_123.xnnpack_cache", "other.litertlm", "unrelated.bin" };
            foreach (string file in owned.Concat(unrelated)) FileWithSize(Path.Combine(_models, file), 10);
            StorageSnapshot snapshot = await _catalog.ReadAsync();
            var item = snapshot.Entries.Single(e => e.Key == "a.litertlm");
            Assert.That(item.Bytes, Is.EqualTo(owned.Length * 10));
            await _catalog.DeleteAsync(item);
            foreach (string file in owned) Assert.That(File.Exists(Path.Combine(_models, file)), Is.False, file);
            foreach (string file in unrelated) Assert.That(File.Exists(Path.Combine(_models, file)), Is.True, file);
            Assert.That((await _catalog.ReadAsync()).Entries.Single(e => e.Key == "a.litertlm").Bytes, Is.Zero);
        }

        [Test]
        public async Task TotalsCountDatabaseOnceAndIncludeImageAttachmentsExportsAndLegacyModels()
        {
            FileWithSize(Path.Combine(_models, "a.litertlm"), 100);
            FileWithSize(Path.Combine(_models, "old.litertlm_123_456.xnnpack_cache"), 50);
            FileWithSize(Path.Combine(_image, "cache", "graph.xnnpack_cache"), 200);
            FileWithSize(Path.Combine(_exports, "export-test", "chat.zip"), 30);
            int chat = await _store.CreateConversationAsync("Saved chat", "ChatA");
            await _store.AddMessageAsync(chat, MessageRecord.UserRole, "Keep this text", null,
                new[] { ("image", "image/png", new byte[40]) });
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "d1", Name = "report.pdf" }, new[] {
                new DocumentItemRecord { DocumentId = "d1", Text = "Saved document", Data = new byte[60], Embedding = new byte[80] } });
            await _store.SavePhotoEmbeddingAsync(new PhotoEmbeddingRecord { PhotoId = "p1", Embedding = new byte[100] });
            await _store.SaveChatEmbeddingsAsync((await _store.GetMessagesAsync(chat))[0].Id,
                new List<ChatSearchPassage> { new ChatSearchPassage { Id = "e1", MessageId = (await _store.GetMessagesAsync(chat))[0].Id, Text = "excerpt", Embedding = new byte[80] } });
            var snapshot = await _catalog.ReadAsync();
            long database = snapshot.Entries.Single(e => e.Kind == StorageKind.Database).Bytes;
            Assert.That(snapshot.DiskBytes, Is.EqualTo(database + 100 + 50 + 200 + 30 + 40));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.Document).Bytes, Is.GreaterThanOrEqualTo(140));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.PhotoIndex).Bytes, Is.GreaterThanOrEqualTo(100));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.ChatIndex).Bytes, Is.GreaterThanOrEqualTo(80));
            Assert.That(snapshot.Entries.Single(e => e.Key == "embedding-b.litertlm").CanDownload, Is.True);
            Assert.That(snapshot.Entries.Any(e => e.Key == "old.litertlm"), Is.True);
        }

        [Test]
        public async Task DeletingDocumentDetachesAllChatsAndReclaimsDatabaseSpace()
        {
            int first = await _store.CreateConversationAsync("First", "ChatA"), second = await _store.CreateConversationAsync("Second", "ChatA");
            await _store.AddMessageAsync(first, MessageRecord.UserRole, "Keep my question", null, context: "Keep the excerpt used in this answer");
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "large", Name = "large.pdf" }, new[] {
                new DocumentItemRecord { DocumentId = "large", Text = "Report", Data = new byte[1024 * 1024] } });
            await _store.SetConversationDocumentAsync(first, "large");
            await _store.SetConversationDocumentAsync(second, "large");
            var before = await _catalog.ReadAsync();
            await _catalog.DeleteAsync(before.Entries.Single(e => e.Kind == StorageKind.Document));
            Assert.That(await _store.GetDocumentAsync("large"), Is.Null);
            Assert.That(await _store.GetDocumentItemsAsync("large"), Is.Empty);
            Assert.That((await _store.GetConversationsAsync()).All(c => c.DocumentId == null), Is.True);
            var saved = (await _store.GetMessagesAsync(first)).Single();
            Assert.That(saved.Text, Is.EqualTo("Keep my question"));
            Assert.That(saved.Context, Does.Contain("Keep the excerpt"));
            Assert.That((await _catalog.ReadAsync()).DiskBytes, Is.LessThan(before.DiskBytes - 900_000));
        }

        [Test]
        public async Task DeletingAttachmentsPreservesMessagesAndOtherStorage()
        {
            int chat = await _store.CreateConversationAsync("Keep chat", "ChatA");
            await _store.AddMessageAsync(chat, MessageRecord.AssistantRole, "Keep this reply", null,
                new[] { ("generated", "image/png", new byte[] { 1, 2, 3 }) });
            FileWithSize(Path.Combine(_models, "a.litertlm"), 20);
            await _catalog.DeleteAsync((await _catalog.ReadAsync()).Entries.Single(e => e.Kind == StorageKind.Attachments));
            var messages = await _store.GetMessagesAsync(chat);
            Assert.That(messages.Single().Text, Is.EqualTo("Keep this reply"));
            Assert.That(messages.Single().Attachments, Is.Null);
            Assert.That(Directory.Exists(_store.AttachmentFolder), Is.False);
            Assert.That(File.Exists(Path.Combine(_models, "a.litertlm")), Is.True);
            await _store.AddMessageAsync(chat, MessageRecord.UserRole, "New attachment", null,
                new[] { ("audio", "audio/wav", new byte[] { 4, 5 }) });
            Assert.That(_store.GetAttachments((await _store.GetMessagesAsync(chat)).Last()), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task IndexDeletionKeepsChatsDocumentsAndImageDeletionKeepsModels()
        {
            int chat = await _store.CreateConversationAsync("Keep", "ChatA");
            int message = await _store.AddMessageAsync(chat, MessageRecord.UserRole, "Keep text", null);
            await _store.SavePhotoEmbeddingAsync(new PhotoEmbeddingRecord { PhotoId = "p", Embedding = new byte[64] });
            await _store.SaveChatEmbeddingsAsync(message, new List<ChatSearchPassage> {
                new ChatSearchPassage { Id = "e", MessageId = message, Text = "Keep text", Embedding = new byte[64] } });
            foreach (StorageKind kind in new[] { StorageKind.PhotoIndex, StorageKind.ChatIndex })
                await _catalog.DeleteAsync((await _catalog.ReadAsync()).Entries.Single(e => e.Kind == kind));
            Assert.That(await _store.GetPhotoEmbeddingsAsync(), Is.Empty);
            Assert.That(await _store.GetChatEmbeddingsAsync(), Is.Empty);
            Assert.That(await _store.GetMessagesAsync(chat), Has.Count.EqualTo(1));
            FileWithSize(Path.Combine(_image, "host", "weights.bin"), 20);
            FileWithSize(Path.Combine(_image, "cache", "compiled.xnnpack_cache"), 30);
            FileWithSize(Path.Combine(_models, "a.litertlm"), 10);
            await _catalog.DeleteAsync((await _catalog.ReadAsync()).Entries.Single(e => e.Kind == StorageKind.Folder && e.Key == "image"));
            Assert.That(Directory.Exists(_image), Is.False);
            Assert.That(File.Exists(Path.Combine(_models, "a.litertlm")), Is.True);
        }

        [Test]
        public async Task RejectsDeletionOutsideOwnedRootsAndDatabaseDeletion()
        {
            string sentinel = Path.Combine(_folder, "keep.txt");
            FileWithSize(sentinel, 10);
            Assert.Throws<InvalidOperationException>(() => StorageFiles.DeleteDirectory(_models, _folder));
            Assert.Throws<InvalidOperationException>(() => StorageFiles.DeleteDirectory(_models, _models));
            Assert.Throws<InvalidOperationException>(() => StorageFiles.DeleteModel(_models, Path.Combine(_folder, "a.litertlm")));
            Assert.ThrowsAsync<InvalidOperationException>(() => _catalog.DeleteAsync(new StorageEntry { Kind = StorageKind.ChatModel, Key = "../keep.txt" }));
            Assert.ThrowsAsync<InvalidOperationException>(() => _catalog.DeleteAsync(new StorageEntry { Kind = StorageKind.Database }));
            Assert.That(File.Exists(sentinel), Is.True);
            // Exports are independent copies; deleting them leaves the chat database intact.
            FileWithSize(Path.Combine(_exports, "export-x", "chat.zip"), 10);
            await _catalog.DeleteAsync((await _catalog.ReadAsync()).Entries.Single(e => e.Kind == StorageKind.Exports));
            Assert.That(Directory.Exists(_exports), Is.False);
            Assert.That(File.Exists(Path.Combine(_store.Folder, "chats.db3")), Is.True);
        }
    }
}
