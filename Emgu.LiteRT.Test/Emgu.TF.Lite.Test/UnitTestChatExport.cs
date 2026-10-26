// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    [TestFixture]
    public class UnitTestChatExport
    {
        private string _folder;
        private ChatStore _store;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-export-test-" + Guid.NewGuid().ToString("N"));
            _store = new ChatStore(_folder);
        }

        [TearDown]
        public async Task Cleanup()
        {
            await _store.CloseAsync();
            string folder = Path.GetFullPath(_folder);
            if (Path.GetDirectoryName(folder) != Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
                || !Path.GetFileName(folder).StartsWith("chat-export-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(folder, recursive: true);
        }

        private static async Task<string> ReadText(ZipArchive archive, string name)
        {
            using var reader = new StreamReader(archive.GetEntry(name).Open());
            return await reader.ReadToEndAsync();
        }

        private static async Task<byte[]> ReadBytes(ZipArchive archive, string name)
        {
            using var stream = archive.GetEntry(name).Open();
            using var output = new MemoryStream();
            await stream.CopyToAsync(output);
            return output.ToArray();
        }

        [Test]
        public async Task ExportsCompleteMarkdownAndExactAttachmentsWithRelativeLinks()
        {
            int id = await _store.CreateConversationAsync("Trip / 日本 [2026]", "Gemma4E2B");
            string text = "日本へ行こう\n\n```csharp\nvar x = 42;\n```";
            byte[] photo = { 1, 2, 3, 4 }, audio = { 5, 6, 7 }, generated = { 8, 9, 10 };
            int user = await _store.AddMessageAsync(id, MessageRecord.UserRole, text, null,
                new[] { ("image", "image/jpeg", photo), ("audio", "audio/wav", audio) }, "A passage from the itinerary.");
            await _store.AddMessageAsync(id, MessageRecord.StepRole, "Calculated travel time", null);
            int reply = await _store.AddMessageAsync(id, MessageRecord.AssistantRole, "**Train booked.**", "Reasoning with ``` and </details> delimiters.",
                new[] { ("generated", "image/png", generated) });
            await _store.SetSummaryAsync(id, "A compacted model context", user);
            int other = await _store.CreateConversationAsync("Do not export", "Other model");
            await _store.AddMessageAsync(other, MessageRecord.UserRole, "OTHER_CHAT_PRIVATE", null);
            await _store.Memories.AddMemoryAsync("GLOBAL_MEMORY_PRIVATE");
            using var output = new MemoryStream();
            Assert.That(await ChatExporter.ExportAsync(_store, id, output), Is.Zero);
            Assert.That(output.CanWrite, Is.True, "The caller owns the output stream.");
            output.Position = 0;
            using var archive = new ZipArchive(output, ZipArchiveMode.Read);
            string markdown = await ReadText(archive, "chat.md");
            Assert.That(markdown, Does.Contain("日本 \\[2026\\]"));
            Assert.That(markdown, Does.Contain(text));
            Assert.That(markdown, Does.Contain("Calculated travel time"));
            Assert.That(markdown, Does.Contain("**Train booked.**"));
            Assert.That(markdown.Replace("\r\n", "\n"), Does.Contain("````text\nReasoning with ``` and </details> delimiters.\n````"));
            Assert.That(markdown, Does.Contain("A passage from the itinerary."));
            Assert.That(markdown, Does.Not.Contain("OTHER_CHAT_PRIVATE").And.Not.Contain("GLOBAL_MEMORY_PRIVATE"));
            Assert.That(markdown.IndexOf("## You", StringComparison.Ordinal), Is.LessThan(markdown.IndexOf("## Assistant", StringComparison.Ordinal)));
            Assert.That(await ReadBytes(archive, $"attachments/message-{user}-1.jpg"), Is.EqualTo(photo));
            Assert.That(await ReadBytes(archive, $"attachments/message-{user}-2.wav"), Is.EqualTo(audio));
            Assert.That(await ReadBytes(archive, $"attachments/message-{reply}-1.png"), Is.EqualTo(generated));
            foreach (Match link in Regex.Matches(markdown, @"\]\(([^)]+)\)"))
                Assert.That(archive.GetEntry(link.Groups[1].Value), Is.Not.Null);
            Assert.That(archive.Entries, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task MissingAttachmentsAreReportedWithoutBrokenLinks()
        {
            int chat = await _store.CreateConversationAsync("Missing file", "Gemma4E2B");
            await _store.AddMessageAsync(chat, MessageRecord.UserRole, "Look at this", null,
                new[] { ("image", "image/png", new byte[] { 1 }) });
            var message = (await _store.GetMessagesAsync(chat)).Single();
            File.Delete(_store.GetAttachments(message).Single().Path);
            using var output = new MemoryStream();
            Assert.That(await ChatExporter.ExportAsync(_store, chat, output), Is.EqualTo(1));
            output.Position = 0;
            using var archive = new ZipArchive(output, ZipArchiveMode.Read);
            Assert.That(await ReadText(archive, "chat.md"), Does.Contain("saved file is missing").And.Not.Contain("![Attached image]"));
            Assert.That(archive.Entries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExportsStoredDocumentContentAndImagesWithoutPretendingToHaveOriginalPdf()
        {
            int chat = await _store.CreateConversationAsync("Document chat", "Gemma4E2B");
            await _store.AddMessageAsync(chat, MessageRecord.UserRole, "Summarize the report", null);
            byte[] image = { 9, 8, 7, 6 };
            await _store.SaveDocumentAsync(new DocumentRecord { Id = "doc-1", Name = "Report [final].pdf", PageCount = 2 },
                new List<DocumentItemRecord>
                {
                    new DocumentItemRecord { DocumentId = "doc-1", Position = 0, Text = "Quarterly revenue grew.", Location = "Page 1" },
                    new DocumentItemRecord { DocumentId = "doc-1", Position = 1, IsImage = true, MediaType = "image/png", Data = image, Location = "Page 2" }
                });
            await _store.SetConversationDocumentAsync(chat, "doc-1");
            using var output = new MemoryStream();
            Assert.That(await ChatExporter.ExportAsync(_store, chat, output), Is.Zero);
            output.Position = 0;
            using var archive = new ZipArchive(output, ZipArchiveMode.Read);
            Assert.That(await ReadText(archive, "chat.md"), Does.Contain("document/document.md"));
            string document = await ReadText(archive, "document/document.md");
            Assert.That(document, Does.Contain("Quarterly revenue grew.").And.Contain("not a copy of the original PDF"));
            Match link = Regex.Match(document, @"!\[Document image\]\(([^)]+)\)");
            Assert.That(await ReadBytes(archive, "document/" + link.Groups[1].Value), Is.EqualTo(image));
            Assert.That(archive.Entries.Any(e => e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)), Is.False);
        }

        [Test]
        public async Task DeletedChatAndCancellationCannotProduceASuccessfulExport()
        {
            int chat = await _store.CreateConversationAsync("Deleted", "Gemma4E2B");
            await _store.DeleteConversationAsync(chat);
            using var output = new MemoryStream();
            Assert.ThrowsAsync<InvalidOperationException>(() => ChatExporter.ExportAsync(_store, chat, output));
            chat = await _store.CreateConversationAsync("Cancelled", "Gemma4E2B");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            Assert.ThrowsAsync<OperationCanceledException>(() => ChatExporter.ExportAsync(_store, chat, output, cancel.Token));
            Assert.That(output.Length, Is.Zero);
        }

        [Test]
        public void AttachmentReferencesCannotReadOutsideTheirChatFolder()
        {
            foreach (string name in new[] { "../secret.txt", "..\\secret.txt", "C:secret.txt", "/secret.txt", ".." })
                Assert.That(_store.GetAttachments(new MessageRecord { ConversationId = 1, Attachments = "image|image/png|" + name }, includeMissing: true), Is.Empty);
        }
    }
}
