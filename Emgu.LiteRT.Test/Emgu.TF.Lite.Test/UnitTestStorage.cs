// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.Util;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    [TestFixture]
    public class UnitTestStorage
    {
        private string _folder, _models, _image, _exports;
        private ChatHistoryStore _store;
        private StorageCatalog _catalog;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "storage-test-" + Guid.NewGuid().ToString("N"));
            _models = Path.Combine(_folder, "models");
            _image = Path.Combine(_folder, "image");
            _exports = Path.Combine(_folder, "exports");
            Directory.CreateDirectory(_models);
            _store = new ChatHistoryStore(Path.Combine(_folder, "chats"));
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
        public async Task SpeechModelIsOneRowWithItsOlderFilesAndDeletesThem()
        {
            // A .litertlm speech model with the .tflite and tokenizer an older version used, if still there.
            var catalog = new StorageCatalog(_store, _models, _exports, new[]
            {
                new StorageModel("Chat A", "a.litertlm"),
                StorageModel.Speech("Parakeet", "speech.litertlm", new[] { "speech.tflite", "speech_tokenizer.json" }, "Speech model.", new[] { "speech-model" })
            });
            // Its compiled caches may be named after the model rather than the file ("speech-model_..."): only caches.
            string[] owned = { "speech.litertlm", "speech.litertlm.sha256ok", "speech.litertlm.part", "speech.litertlm_1_2.xnnpack_cache",
                "speech-model_3_4.xnnpack_cache", "speech.tflite", "speech.tflite.sha256ok", "speech_tokenizer.json", "speech_tokenizer.json.sha256ok" };
            string[] unrelated = { "a.litertlm", "other.tflite", "tokenizer.json", "speech-model", "speech-model_notes.txt" };
            foreach (string file in owned.Concat(unrelated)) FileWithSize(Path.Combine(_models, file), 10);
            StorageSnapshot snapshot = await catalog.ReadAsync();
            StorageEntry speech = snapshot.Entries.Single(e => e.Kind == StorageKind.SpeechModel);
            Assert.That((speech.Key, speech.Name, speech.Bytes, speech.Detail), Is.EqualTo(("speech.litertlm", "Parakeet", owned.Length * 10L, "Speech model.")));
            // Not also listed as a chat model (the scan finds every .litertlm).
            Assert.That(snapshot.Entries.Count(e => e.Key == "speech.litertlm"), Is.EqualTo(1));
            await catalog.DeleteAsync(speech);
            foreach (string file in owned) Assert.That(File.Exists(Path.Combine(_models, file)), Is.False, file);
            foreach (string file in unrelated) Assert.That(File.Exists(Path.Combine(_models, file)), Is.True, file);
            // Only a speech model's own files: a chat model's row can't delete a .tflite.
            Assert.ThrowsAsync<InvalidOperationException>(() => catalog.DeleteAsync(new StorageEntry { Kind = StorageKind.ChatModel, Key = "other.tflite" }));
            Assert.ThrowsAsync<InvalidOperationException>(() => catalog.DeleteAsync(new StorageEntry { Kind = StorageKind.SpeechModel, Key = "other.tflite" }));
        }

        [Test]
        public async Task EachImageModelIsItsOwnRowAndDeletingOneKeepsTheOthers()
        {
            var catalog = new StorageCatalog(_store, _models, _exports, new[]
            {
                new StorageModel("Chat A", "a.litertlm"),
                StorageModel.Image("Bonsai", "bonsai.litertlm", "Image model.", "bonsai-flux2-cache"),
                StorageModel.Image("Klein", "klein.litertlm", null, "flux2-klein-cache")
            }, new[] { new StorageFolder("image", "Z-Image-Turbo", _image, "Z-Image.") });
            // LiteRT-LM names both models' caches after the graphs ("flux2_dit_..."), so each has its own folder.
            string[] bonsai = { "bonsai.litertlm", "bonsai.litertlm.sha256ok", "bonsai.litertlm.part",
                "bonsai-flux2-cache/flux2_dit_1_2.xnnpack_cache" };
            string[] others = { "klein.litertlm", "flux2-klein-cache/flux2_dit_1_2.xnnpack_cache", "a.litertlm" };
            foreach (string file in bonsai.Concat(others)) FileWithSize(Path.Combine(_models, file), 10);
            FileWithSize(Path.Combine(_image, "zc_main0.tflite"), 10);
            StorageSnapshot snapshot = await catalog.ReadAsync();
            StorageEntry[] images = snapshot.Entries.Where(e => e.Kind == StorageKind.ImageModel).ToArray();
            Assert.That(images.Select(e => (e.Key, e.Bytes)), Is.EqualTo(new[] { ("bonsai.litertlm", 40L), ("klein.litertlm", 20L) }));
            Assert.That(images[1].Detail, Does.Contain("Image model"));
            // Not also listed as chat models, and Z-Image's folder is still its own row.
            Assert.That(snapshot.Entries.Count(e => e.Key == "bonsai.litertlm" || e.Key == "klein.litertlm"), Is.EqualTo(2));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.Folder).Name, Is.EqualTo("Z-Image-Turbo"));
            await catalog.DeleteAsync(images[0]);
            foreach (string file in bonsai) Assert.That(File.Exists(Path.Combine(_models, file)), Is.False, file);
            Assert.That(Directory.Exists(Path.Combine(_models, "bonsai-flux2-cache")), Is.False);
            foreach (string file in others) Assert.That(File.Exists(Path.Combine(_models, file)), Is.True, file);
            Assert.That(File.Exists(Path.Combine(_image, "zc_main0.tflite")), Is.True);
            // A cache folder outside the models' folder is refused.
            var bad = new StorageCatalog(_store, _models, _exports, new[] { StorageModel.Image("X", "x.litertlm", null, "../image") });
            Assert.ThrowsAsync<InvalidOperationException>(() => bad.ReadAsync());
            // A speech row can't delete an image model's file.
            Assert.ThrowsAsync<InvalidOperationException>(() => catalog.DeleteAsync(new StorageEntry { Kind = StorageKind.SpeechModel, Key = "klein.litertlm" }));
        }

        [Test]
        public async Task TotalsCountDatabaseOnceAndIncludeImageAttachmentsExportsAndLegacyModels()
        {
            FileWithSize(Path.Combine(_models, "a.litertlm"), 100);
            FileWithSize(Path.Combine(_models, "old.litertlm_123_456.xnnpack_cache"), 50);
            FileWithSize(Path.Combine(_image, "cache", "graph.xnnpack_cache"), 200);
            FileWithSize(Path.Combine(_exports, "export-test", "chat.zip"), 30);
            int chat = await _store.CreateChatAsync("Saved chat", "ChatA");
            await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, "Keep this text", null,
                new[] { ("image", "image/png", new byte[40]) });
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "d1", Name = "report.pdf" }, new[] {
                new DocumentItemRecord { DocumentId = "d1", Text = "Saved document", Data = new byte[60], Embedding = new byte[80] } });
            await _store.SavePhotoEmbeddingAsync(new PhotoEmbeddingRecord { PhotoId = "p1", Embedding = new byte[100] });
            await _store.SaveVideoAsync(new ChatVideoEntry { VideoId = "v1", Thumbnail = new byte[50] },
                new[] { new ChatVideoWindow { VideoId = "v1", Embedding = new byte[120] }, new ChatVideoWindow { VideoId = "v1", StartMs = 10000, Embedding = new byte[120] } });
            await _store.SaveChatEmbeddingsAsync((await _store.GetMessagesAsync<ChatMessageRecord>(chat))[0].Id,
                new List<ChatSearchPassage> { new ChatSearchPassage { Id = "e1", MessageId = (await _store.GetMessagesAsync<ChatMessageRecord>(chat))[0].Id, Text = "excerpt", Embedding = new byte[80] } });
            var snapshot = await _catalog.ReadAsync();
            long database = snapshot.Entries.Single(e => e.Kind == StorageKind.Database).Bytes;
            Assert.That(snapshot.DiskBytes, Is.EqualTo(database + 100 + 50 + 200 + 30 + 40));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.Document).Bytes, Is.GreaterThanOrEqualTo(140));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.PhotoIndex).Bytes, Is.GreaterThanOrEqualTo(100));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.VideoIndex).Bytes, Is.GreaterThanOrEqualTo(290));
            Assert.That(snapshot.Entries.Single(e => e.Kind == StorageKind.ChatIndex).Bytes, Is.GreaterThanOrEqualTo(80));
            Assert.That(snapshot.Entries.Single(e => e.Key == "embedding-b.litertlm").CanDownload, Is.True);
            Assert.That(snapshot.Entries.Any(e => e.Key == "old.litertlm"), Is.True);
        }

        [Test]
        public async Task DeletingDocumentDetachesAllChatsAndReclaimsDatabaseSpace()
        {
            int first = await _store.CreateChatAsync("First", "ChatA"), second = await _store.CreateChatAsync("Second", "ChatA");
            await _store.AddMessageAsync(first, ChatMessageRecord.UserRole, "Keep my question", null, context: "Keep the excerpt used in this answer");
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "large", Name = "large.pdf" }, new[] {
                new DocumentItemRecord { DocumentId = "large", Text = "Report", Data = new byte[1024 * 1024] } });
            await _store.SetChatDocumentAsync(first, "large");
            await _store.SetChatDocumentAsync(second, "large");
            var before = await _catalog.ReadAsync();
            await _catalog.DeleteAsync(before.Entries.Single(e => e.Kind == StorageKind.Document));
            Assert.That(await _store.GetDocumentAsync("large"), Is.Null);
            Assert.That(await _store.GetDocumentItemsAsync("large"), Is.Empty);
            Assert.That((await _store.GetChatsAsync()).All(c => c.DocumentId == null), Is.True);
            var saved = (await _store.GetMessagesAsync<ChatMessageRecord>(first)).Single();
            Assert.That(saved.Text, Is.EqualTo("Keep my question"));
            Assert.That(saved.Context, Does.Contain("Keep the excerpt"));
            Assert.That((await _catalog.ReadAsync()).DiskBytes, Is.LessThan(before.DiskBytes - 900_000));
        }

        [Test]
        public async Task DeletingAttachmentsPreservesMessagesAndOtherStorage()
        {
            int chat = await _store.CreateChatAsync("Keep chat", "ChatA");
            await _store.AddMessageAsync(chat, ChatMessageRecord.AssistantRole, "Keep this reply", null,
                new[] { ("generated", "image/png", new byte[] { 1, 2, 3 }) });
            FileWithSize(Path.Combine(_models, "a.litertlm"), 20);
            await _catalog.DeleteAsync((await _catalog.ReadAsync()).Entries.Single(e => e.Kind == StorageKind.Attachments));
            var messages = await _store.GetMessagesAsync<ChatMessageRecord>(chat);
            Assert.That(messages.Single().Text, Is.EqualTo("Keep this reply"));
            Assert.That(messages.Single().Attachments, Is.Null);
            Assert.That(Directory.Exists(_store.AttachmentFolder), Is.False);
            Assert.That(File.Exists(Path.Combine(_models, "a.litertlm")), Is.True);
            await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, "New attachment", null,
                new[] { ("audio", "audio/wav", new byte[] { 4, 5 }) });
            Assert.That(_store.GetAttachments((await _store.GetMessagesAsync<ChatMessageRecord>(chat)).Last()), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task IndexDeletionKeepsChatsDocumentsAndImageDeletionKeepsModels()
        {
            int chat = await _store.CreateChatAsync("Keep", "ChatA");
            int message = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, "Keep text", null);
            await _store.SavePhotoEmbeddingAsync(new PhotoEmbeddingRecord { PhotoId = "p", Embedding = new byte[64] });
            await _store.SaveVideoAsync(new ChatVideoEntry { VideoId = "v" }, new[] { new ChatVideoWindow { VideoId = "v", Embedding = new byte[64] } });
            await _store.SaveChatEmbeddingsAsync(message, new List<ChatSearchPassage> {
                new ChatSearchPassage { Id = "e", MessageId = message, Text = "Keep text", Embedding = new byte[64] } });
            foreach (StorageKind kind in new[] { StorageKind.PhotoIndex, StorageKind.ChatIndex, StorageKind.VideoIndex })
                await _catalog.DeleteAsync((await _catalog.ReadAsync()).Entries.Single(e => e.Kind == kind));
            Assert.That(await _store.GetPhotoEmbeddingsAsync(), Is.Empty);
            Assert.That(await _store.GetChatEmbeddingsAsync(), Is.Empty);
            Assert.That(await _store.GetVideoEntriesAsync(), Is.Empty);
            Assert.That(await _store.GetVideoWindowsAsync(), Is.Empty);
            Assert.That(await _store.GetMessagesAsync<ChatMessageRecord>(chat), Has.Count.EqualTo(1));
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
