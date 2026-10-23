# Plan: voice messages and saved chats for the chat libraries

Status: proposed 2026-10-23; steps 1-5 done 2026-10-23. The follow-up to `CHAT_LIBRARY_PLAN.md` (all 7 steps done): two more
parts of the demo's chat page that any chat app would want, moved into libraries the same way.

## Goal

```csharp
var chat = new ChatView { Model = new Qwen3() };   // a microphone button, as well as Send
var history = new ChatHistoryStore(folder);        // saved chats
history.Attach(chat.Conversation, chatId);         // saves each exchange and compaction; Load reopens
```

- **Voice messages**: tap the microphone, speak, tap again to send. A model that hears audio (Gemma 4) gets the
  recording. For any other model or client, the platform's speech recognizer writes it down and the words are sent.
- **Saved chats**: a SQLite store of conversations, messages, tool steps, attachments and the compaction point, which
  reopens a chat with `ChatConversation.Load`.

## What there is now (in the demo)

- **Voice**:
  - `VoiceRecorder.cs` (327 lines): 16 kHz mono WAV through AVAudioRecorder (iOS, Mac Catalyst), AudioRecord
    (Android, 20 ms reads) and MediaCapture (Windows, no level). It asks for microphone permission, implements the
    Agents package's `IVoiceInput` (`ReadLevelDb` for the voice chat) and `HeadphonesConnected`.
  - `SpeechToText.cs` (448 lines): Android 13+'s `SpeechRecognizer` fed from a pipe at recording speed, and Apple's
    Speech framework. It reads the language from the demo's Preferences.
  - `VoiceLanguages.cs` (89 lines): the language list and `Resolve`.
  - `MicrophoneGate` (in `SoundSource.cs`): shares the microphone with the sound monitors.
  - The page's own pieces: the talk button, the 30 s limit with "Listening... 0:03", sending the recording as an
    attachment or transcribed (`TranscribeVoiceAsync`), and the transcript shown under a voice message
    (`LiteRtLmChatPage.Transcript.cs`).
  - Verified on devices, as recorded in `CLAUDE.md`.
- **Saved chats**:
  - `ChatStore.cs` (640 lines): one `chats.db3` with the `Conversation` and `Message` tables, which hold steps as
    `step` rows, attachments as files in `attachments/<id>/`, and the summary with `SummaryUpTo`. The same database
    has the demo's other tables: documents, chat and photo embeddings, memories, goals.
  - Private chats are enforced in the store.
  - `SaveExchangeAsync` and `OpenConversationAsync` (~250 lines) turn `ChatMessageItem`s into rows and back.
  - The test project links `ChatStore.cs`.

## Decisions to take (proposals)

- **Voice goes in `Emgu.LiteRT.LM.Maui`.** It is MAUI and per-platform, like `MediaChatAttachmentSource`, so it needs
  no new package.
- **Saved chats get a new package, `Emgu.LiteRT.LM.Chat.Sqlite`.** It would be netstandard2.0 + net8.0, depend on
  `Emgu.LiteRT.LM.Chat` and `sqlite-net-pcl`, and have no MAUI dependency, since the app passes the folder.
  `Emgu.LiteRT.LM.Chat` itself stays free of SQLite.
- **Keep the demo's schema**, so existing saved chats open unchanged:
  - the same file, the same `Conversation`/`Message` tables and columns, the same attachment folders;
  - the library's record classes are unsealed, and the demo derives from them for its own columns (`DocumentId`,
    `Language`, `ThinkingTranslation`) - sqlite-net maps a class's properties to the table's columns by name;
  - the library exposes its connection so an app keeps its other tables in the same database.
- **`Transcript` becomes a `ChatMessageItem` property** (a voice message's words, shown but never given to the model),
  saved by the store. The demo already has it, in its own `UserMessageInfo`.

## Steps

1. **Voice classes into `Emgu.LiteRT.LM.Maui`, no UI yet.**
   - Move `VoiceRecorder`, `SpeechToText` (renamed `SpeechRecognizer`), `VoiceLanguages` and `MicrophoneGate` into the
     library as public types.
   - `SpeechRecognizer.LanguageTag` becomes a property the app sets, instead of reading the demo's Preferences.
   - The demo uses them from the library: its talk button, voice chat and sound monitors keep working unchanged.
   - Checks: builds for all platforms, then on the Galaxy S10 (Android 12: audio to Gemma 4, no recognizer) a voice
     message, the voice chat and a sound monitor.

   **Done 2026-10-23:**
   - `VoiceRecorder`, `SpeechRecognizer` and `MicrophoneGate` are public in `Emgu.LiteRT.LM.Maui`, and their docs say
     which permissions an app declares.
   - `VoiceLanguages` went into `Emgu.LiteRT.LM.Chat` instead: it has no MAUI types, and the tests (net10.0) can't
     reference a MAUI library. `UnitTestVoiceLanguages` now tests the library's.
   - `SpeechRecognizer.Language` replaces the demo's Preferences lookup, and the demo sets it from its setting.
     `ChooseLanguageHint` ends the "can't recognize this language" errors (the demo's "Choose the language in Settings
     > Voice language.").
   - The unused diagnostic `DescribeSupportAsync` was dropped.
   - Checked with a temporary self-test on the Galaxy S10:
     - the recorder recorded 2.08 s as a valid WAV, with real levels;
     - a listening sound monitor let go of the microphone while recording and took it back afterwards;
     - the recognizer got the language and hint, and correctly reported itself unsupported on Android 12.
   - The voice chat uses the same recorder through `IVoiceInput`; it wasn't run with speech.
   - The demo builds for Android, the iOS simulator and Mac Catalyst (which stops at code signing on this Mac); the
     model-free tests pass (386).
2. **`ChatView`'s microphone.**
   - A `PART_Talk` part with a `ChatTalkButtonStyle` and the `ShowTalkButton` and `MaxVoiceLength` (30 s) properties,
     and the status "Listening... 0:03 - tap again to send".
   - `VoiceMessages`: `Auto` (audio when the model takes it, otherwise transcribed when the platform can, otherwise no
     button), `Audio`, `Text` or `Off`.
   - `TranscribeVoiceMessages` writes the words under an audio message (`ChatMessageItem.Transcript`) and the default
     user template shows them.
   - The `MauiChat` sample (Qwen3: no audio, so it sends the transcribed words) gets the microphone and speech
     permissions in its manifests.
   - Checks: Mac Catalyst (Apple Speech: built, never run before), the S10 (audio path, Gemma 4), and an Android 13+
     device for the transcription path. That device is the Pixel Fold or the Pixel 8 emulator; I'll ask before using
     the Fold.

   **Done 2026-10-23:**
   - `ChatView.Voice.cs`: `PART_Talk` with `ChatTalkButtonStyle` / `ChatTalkStopButtonStyle` (the view switches them
     unless the template styles the part itself), `VoiceMessages` (`ChatVoiceMessages`: `Auto`, `Audio`, `Text`,
     `Off`), `EffectiveVoiceMessages`, `MaxVoiceLength`, `TranscribeVoiceMessages` and `IsRecording`.
   - While recording, Send and attach are disabled. A recording under 0.5 s isn't sent, and leaving the page discards
     one in progress.
   - In `Audio` mode the words are matched to the user message that carries the recording, once both exist.
   - `ChatMessageItem.Transcript` / `HasTranscript` (trimmed; blank is null). The default user template shows it in
     quotes under the attachments; `UnitTestChatConversation.KeepsAVoiceMessagesTranscriptFromTheModel` checks that
     it isn't sent to the model.
   - `MauiChat` declares `RECORD_AUDIO`, the `RecognitionService` query, `NSMicrophoneUsageDescription` and
     `NSSpeechRecognitionUsageDescription`.
   - Checked with a temporary page in the demo (a `ChatView` with the library's own templates: the demo's `App.xaml`
     replaces `ChatViewTemplate` app-wide with its transcript-only one). While recording, the test played a spoken
     "What is the capital of Italy?" through the device's speaker:
     - **Galaxy S10** (Android 12): with Qwen3 there is no microphone, since no recognizer is available. Gemma 4 E2B
       on the GPU got the recording as audio and answered "The capital of Italy is Rome." The microphone turned into
       the red stop button with "Listening... 0:01". No transcript, as expected on Android 12.
     - **Mac Catalyst**, the first run of Apple's Speech framework: Qwen3 got the words ("What is the capital of
       Italy?") and answered Rome. Gemma 4 E2B got the audio, with the transcript "What is the capital of Italy?"
       shown under it. No permission prompt was needed.
   - Not done: an Android 13+ device for the transcription path. Android's recognizer code is unchanged since its
     checks on a Pixel Fold.
3. **`Emgu.LiteRT.LM.Chat.Sqlite` scaffolding and store.**
   - The project, CMake, props/README, package id, solution entry (as for `Emgu.LiteRT.LM.Chat`).
   - `ChatHistoryStore(folder)` with these methods:
     - `GetChatsAsync`, `CreateChatAsync(title, model)` and `DeleteChatAsync` (which also deletes the attachment files);
     - `SaveExchangeAsync(chatId, message, reply)` (text, thinking, steps, attachments, context, transcript);
     - `SaveCompactionAsync(chatId, summary, contextStart message)`;
     - `LoadAsync(chatId)`, which returns the messages, summary and context start that `ChatConversation.Load` takes;
     - `SaveAttachmentAsync(messageId, kind, mediaType, bytes)` for an app's own kinds (e.g. the demo's generated
       pictures and written documents).
   - It also has `IsPrivate` (refuses to save), `ChatsChanged` and `Connection`.
   - Model-free tests, run in CI:
     - saving and loading round trips with steps and attachments, and deleting removes the files;
     - private mode, and compaction mapped to a message id and back;
     - a database written by today's demo schema opens and loads.

   **Done 2026-10-23:**
   - `Emgu.LiteRT.LM.Chat.Sqlite/` (namespace `Emgu.LiteRT.LM.Chat.Sqlite`, package id
     `EMGU_LITERT_LM_CHAT_SQLITE_NUGET_ID`, netstandard2.0 + net8.0, depends on `Emgu.LiteRT.LM.Chat` and
     `sqlite-net-pcl` 1.11.285) is in CMake, the cross-platform solution and `.gitignore`. It builds with no warnings
     and packs.
   - `ChatRecord` and `ChatMessageRecord` are the demo's `Conversation` and `Message` tables without its own columns
     (`DocumentId`, `Language`, `ThinkingTranslation`). An app derives from them, with the same `[Table]` name, for
     its columns.
   - A `SchemaVersion` table records version 1.
   - `ChatHistoryStore`:
     - chats: `GetChatsAsync`, `GetChatAsync`, `CreateChatAsync` and `TitleOf`;
     - saving: `SaveExchangeAsync` (with files of the app's own kinds for the reply), `AddMessageAsync`,
       `SetTranscriptAsync` (it renames a chat still titled "Voice message", as the demo does),
       `SaveCompactionAsync(chatId, conversation)` and `SetSummaryAsync`;
     - reading back: `LoadAsync` / `LoadAsync<TMessage>`, which return a `ChatHistory` (messages, summary, context
       start, `RecordOf`, `AttachmentsOf`, `Load(conversation)`), plus `GetMessagesAsync<T>` and `GetAttachments`;
     - `DeleteChatAsync` (with the `DeletingChat` hook), `IsPrivate` and `ChatsChanged`;
     - for an app's own tables: `GetConnectionAsync` and `CreateTablesAsync`.
   - The store remembers which row each message it saved or loaded is (a `ConditionalWeakTable`; `GetMessageId`), so
     saving the compaction point needs no app data in `Tag`.
   - The demo's formats are kept: attachment lines "kind|media type|file", files named `<id>_<n>.<ext>`, dates as
     ticks, an interrupted reply ending in "…", and step lines saved just before their reply.
   - `UnitTestChatHistoryStore` (7 tests, model-free, so they run in CI):
     - an exchange round trip (image, audio, context, transcript, thinking, steps);
     - an app's own reply files, and deleting a chat with its files;
     - compaction with a summary and dropped;
     - a private chat saves and reads nothing;
     - a voice-message chat gets its title from the words;
     - an app's own column;
     - `ReadsTheChatsTheDemoSaved`: chats written by the demo's current `ChatStore` open with everything, and the demo
       still reads the database after the store has added its schema table and an exchange.
   - The model-free suite passes, now 394 tests.
4. **`Attach(conversation, chatId)`.** It saves on `ExchangeCompleted` and `Compacted`, so a simple app needs no code of
   its own. The sample reopens its last chat after a restart.

   **Done 2026-10-23:**
   - `ChatHistoryStore.Attach(conversation, chatId = 0, model = null)` returns a `ChatHistoryBinding`. It saves:
     - each completed exchange; the first creates the chat (`ChatId`, `ChatCreated`);
     - a regenerated reply in place of the one before, through the new `ReplaceReplyAsync`, which deletes the rows
       saved after the message, with their files;
     - the compaction point;
     - a voice message's transcript written down after the message was saved.
   - Saves are chained, so they run in order in the background. `FlushAsync` waits for them, `SaveFailed` reports a
     failure, and `Dispose` stops saving. Nothing is saved while `IsPrivate`.
   - Four more tests in `UnitTestChatHistoryStore` (11 in all):
     - an attached conversation is saved, compacted, reopened in another conversation and continued in the same
       chat, and nothing is saved after `Dispose`;
     - a regenerated reply replaces the saved one with its steps;
     - a late transcript is saved and renames a "Voice message" chat;
     - a private conversation saves nothing and reports no failure.
   - The `MauiChat` sample keeps its chats in `MauiChat/chats` in the app data folder, a folder of its own: on Mac
     Catalyst that folder is `~/Library`, where the demo keeps `chats/`. It reopens the last chat (Preferences
     `last_chat`) and has a New chat toolbar button.
   - Checked on Mac Catalyst with a temporary self-test across app restarts:
     - "My favourite colour is green." was saved;
     - after a restart the chat reopened with both messages, and Qwen3 answered "What is my favourite colour?" with
       "Your favorite color is green!", so the model was given the reopened chat;
     - New chat started a second chat.
   - The test chats and the preference were deleted afterwards.
5. **The demo onto the store.**
   - `ChatStore` derives from `ChatHistoryStore` and keeps its own tables and columns.
   - `SaveExchangeAsync` and `OpenConversationAsync` shrink to what is the demo's own: cards, documents, translations,
     the language.
   - Checks:
     - **First copy `~/Library/chats` aside**, so the real saved chats on this Mac are safe; I won't read their
       content.
     - Open chats saved before the change on Mac Catalyst and the S10 (messages, steps, pictures, the summary divider),
       continue one, compact, delete one.

   **Done 2026-10-23:**
   - `ChatStore` derives from `ChatHistoryStore`. `ConversationRecord` (with `DocumentId`) and `MessageRecord` (with
     `Language` and `ThinkingTranslation`) derive from the library's records, and its own tables are created in
     `CreateTablesAsync`.
   - A deleted chat's search-index rows go in `DeletingChat`.
   - The library's `SetTranscriptAsync`, `SetSummaryAsync`, `GetAttachments` (`ChatSavedAttachment` replaces
     `SavedAttachment`), `Shorten` and the file naming replace the demo's own. `IsPrivateSession` became `IsPrivate`,
     and `ChatsChanged` is an `EventHandler`.
   - `AddMessageAsync` keeps the demo's meaning (its last parameter is the language) and hides the library's.
     `SetLanguageAsync` is new.
   - The page:
     - `SaveExchangeAsync` calls `ChatHistoryStore.SaveExchangeAsync`. It got a `steps` parameter, so the document's
       step line still comes before the tool steps.
     - `OpenConversationAsync` builds on `LoadAsync<MessageRecord>`, `RecordOf` and `AttachmentsOf`, then
       `ChatHistory.Load`.
     - `OnConversationCompacted` calls `SaveCompactionAsync`; `MessageIdOf` is gone.
   - The existing `ChatStore` tests (private chats, export, chat search, storage, voice transcripts) pass unchanged;
     the model-free suite passes (398).
   - Checked with a temporary self-test, after copying `~/Library/chats` to `~/chats-backup-2026-10-23`:
     - **Existing chats** opened (counts only logged, never text): the three most recent of 5 on the Mac, and both
       on the Galaxy S10, with their steps and attachments. 180-600 ms each.
     - **A new test chat** with Gemma 4 E2B (Metal on the Mac, OpenCL on the S10):
       - the calculator step and the Word-document card;
       - compaction (summary and context start 2 saved);
       - reopened with all of it, and continued: it still knew 838,102.05;
       - the first message's language was saved;
       - deleting it removed its files.
     - Afterwards the Mac's database had the same 5 chats and 41 messages as the backup, with identical attachments;
       the only addition is the `SchemaVersion` table.
     - On the S10 one 1.3 s UI stall: a frame waited on the GPU while Gemma 4 E2B started loading there, as the test
       switched models.
6. **Docs**: the two READMEs (`ChatView`'s microphone and permissions; the new package), `CLAUDE.md`, and this plan.

## Risks and open questions

- **Speech on Windows**: `SpeechRecognizer` has no Windows implementation, and Windows' recorder reports no level. So
  on Windows, `Auto` means audio-only models, and the Windows build is compiled but not tested.
- **Apple Speech has never run** in the demo ("built, not yet run" in `CLAUDE.md`). Step 2 is its first test, and it
  needs `NSSpeechRecognitionUsageDescription`.
- **The schema is the demo's**. Freezing it as a library schema means future changes need migrations. Proposal: a
  `SchemaVersion` table from the start, version 1 = today's schema.
- **`MicrophoneGate`** is shared by the voice recorder and the sound monitors. Moving it into the library makes it
  public API, which an app's own microphone users should also go through.
- **sqlite-net-pcl** brings SQLitePCLRaw's bundled native SQLite. It works in today's CI, since the tests already link
  `ChatStore.cs`, but it is a new dependency for whoever installs the package.
