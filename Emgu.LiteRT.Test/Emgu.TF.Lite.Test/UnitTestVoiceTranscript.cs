// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat.Sqlite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    // What the chat store keeps of a voice message's words, a message's language and a reply's translated thinking
    // (TODO items 22 and 39).
    [TestFixture]
    public class UnitTestVoiceTranscript
    {
        private string _folder;
        private ChatHistoryStore _store;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "voice-transcript-test-" + Guid.NewGuid().ToString("N"));
            _store = new ChatHistoryStore(_folder);
        }

        [TearDown]
        public async Task Cleanup()
        {
            await _store.CloseAsync();
            string folder = Path.GetFullPath(_folder);
            if (Path.GetDirectoryName(folder) != Path.GetFullPath(TestContext.CurrentContext.WorkDirectory)
                || !Path.GetFileName(folder).StartsWith("voice-transcript-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(folder, recursive: true);
        }

        private async Task<ChatMessageRecord> Message(int chat, int id) =>
            (await _store.GetMessagesAsync<ChatMessageRecord>(chat)).Single(m => m.Id == id);

        [Test]
        public async Task TheWordsAreKeptAndTitleAChatCalledVoiceMessage()
        {
            int chat = await _store.CreateChatAsync("Voice message", "Gemma4E2B");
            int voice = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, string.Empty, null,
                new[] { ("audio", "audio/wav", new byte[] { 1, 2, 3 }) });
            Assert.That((await Message(chat, voice)).Transcript, Is.Null);
            int changes = 0;
            _store.ChatsChanged += (_, _) => changes++;
            await _store.SetTranscriptAsync(chat, voice, "  What is the weather like in Paris?  ");
            Assert.That((await Message(chat, voice)).Transcript, Is.EqualTo("What is the weather like in Paris?"));
            Assert.That((await _store.GetChatsAsync()).Single().Title, Is.EqualTo("What is the weather like in Paris?"));
            Assert.That(changes, Is.EqualTo(1));
            // The audio is still kept with the message.
            Assert.That(_store.GetAttachments(await Message(chat, voice)).Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task AChatWithAnotherTitleKeepsIt()
        {
            int chat = await _store.CreateChatAsync("Trip planning", "Gemma4E2B");
            int voice = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, string.Empty, null);
            await _store.SetTranscriptAsync(chat, voice, new string('x', 200));
            Assert.That((await _store.GetChatsAsync()).Single().Title, Is.EqualTo("Trip planning"));
            Assert.That((await Message(chat, voice)).Transcript.Length, Is.EqualTo(200));
        }

        [Test]
        public async Task EmptyWordsAndPrivateChatsWriteNothing()
        {
            int chat = await _store.CreateChatAsync("Voice message", "Gemma4E2B");
            int voice = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, string.Empty, null);
            await _store.SetTranscriptAsync(chat, voice, "   ");
            await _store.SetTranscriptAsync(chat, 0, "words");
            Assert.That((await Message(chat, voice)).Transcript, Is.Null);
            Assert.That((await _store.GetChatsAsync()).Single().Title, Is.EqualTo("Voice message"));
            _store.IsPrivate = true;
            await _store.SetTranscriptAsync(chat, voice, "words");
            _store.IsPrivate = false;
            Assert.That((await Message(chat, voice)).Transcript, Is.Null);
        }

        [Test]
        public async Task ALanguageAndATranslatedThinkingComeBack()
        {
            int chat = await _store.CreateChatAsync("Manzanas", "Gemma4E2B");
            int question = await _store.AddMessageAsync(chat, ChatMessageRecord.UserRole, "¿Cuántas manzanas me quedan?", null);
            await _store.SetLanguageAsync(question, "es");
            int reply = await _store.AddMessageAsync(chat, ChatMessageRecord.AssistantRole, "Te quedan 29.", "Thinking Process: 36 - 7 = 29");
            Assert.That((await Message(chat, question)).Language, Is.EqualTo("es"));
            Assert.That((await Message(chat, reply)).Language, Is.Null);
            Assert.That((await Message(chat, reply)).ThinkingTranslation, Is.Null);
            await _store.SetThinkingTranslationAsync(reply, "es", "Proceso de pensamiento: 36 - 7 = 29");
            ChatMessageRecord saved = await Message(chat, reply);
            Assert.That(saved.Language, Is.EqualTo("es"));
            Assert.That(saved.ThinkingTranslation, Is.EqualTo("Proceso de pensamiento: 36 - 7 = 29"));
            Assert.That(saved.Thinking, Is.EqualTo("Thinking Process: 36 - 7 = 29"));
            // Nothing for an unsaved reply (id 0) or in a private chat.
            await _store.SetThinkingTranslationAsync(0, "es", "x");
            _store.IsPrivate = true;
            await _store.SetThinkingTranslationAsync(reply, "fr", "changed");
            _store.IsPrivate = false;
            Assert.That((await Message(chat, reply)).ThinkingTranslation, Is.EqualTo("Proceso de pensamiento: 36 - 7 = 29"));
        }
    }
}
