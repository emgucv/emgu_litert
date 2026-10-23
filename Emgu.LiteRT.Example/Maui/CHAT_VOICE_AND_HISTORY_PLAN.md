# Plan: voice messages and saved chats for the chat libraries

Status: proposed 2026-10-23; steps 1-2 done 2026-10-23. The follow-up to `CHAT_LIBRARY_PLAN.md` (all 7 steps done): two more
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
4. **`Attach(conversation, chatId)`.** It saves on `ExchangeCompleted` and `Compacted`, so a simple app needs no code of
   its own. The sample reopens its last chat after a restart.
5. **The demo onto the store.**
   - `ChatStore` derives from `ChatHistoryStore` and keeps its own tables and columns.
   - `SaveExchangeAsync` and `OpenConversationAsync` shrink to what is the demo's own: cards, documents, translations,
     the language.
   - Checks:
     - **First copy `~/Library/chats` aside**, so the real saved chats on this Mac are safe; I won't read their
       content.
     - Open chats saved before the change on Mac Catalyst and the S10 (messages, steps, pictures, the summary divider),
       continue one, compact, delete one.
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
