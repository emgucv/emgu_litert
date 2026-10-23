# Plan: a reusable MAUI chat control (`Emgu.LiteRT.LM.Maui`)

Status: decisions taken 2026-10-22 (below). Steps 1 (scaffolding), 2 (`ReplyView`, `ThinkingView`), 3
(`ChatConversation`), 4 (`ChatView`, `ModelLoadCard`, the `MauiChat` sample) and 5 (attachments, read-aloud) done
2026-10-23.

## Decisions

- Package for the MAUI control: **`Emgu.LiteRT.LM.Maui`**.
- The conversation logic goes in a new package, **`Emgu.LiteRT.LM.Chat`** (not in `Emgu.LiteRT.LM.Extensions.AI`, which
  stays the `IChatClient` adapter only).
- The UI is **XAML with templates**, so apps can restyle it: a templated `ChatView` (`ControlTemplate`), the messages
  as `DataTemplate`s chosen by a `DataTemplateSelector`, and the default look in a `ResourceDictionary` of styles.
- Styling is by **templates** (replace the control template, a message template or a style), not only colour keys.

## Goal

Let another MAUI app add an on-device chat with a few lines of code:

```csharp
var model = new Gemma4E2B();                       // Emgu.LiteRT.LM.Models
var chat = new ChatView
{
    Model = model,                                 // downloads/loads it, with the loading card
    // or: ChatClient = anyIChatClient,           // a server, an agent, another provider
};
Content = chat;                                    // inside the app's own ContentPage
```

…and get what the demo's chat already does well: streamed replies with Markdown, code blocks and maths, the thinking
header, the message box with Stop, image/audio attachments, the model loading card, and staying within the model's
context window (summarizing or dropping earlier messages). Everything else is opt-in.

## Why not package `LiteRtLmChatPage` as it is

- It's 33 partial files and ~11,100 lines (`LiteRtLmChatPage.cs` alone 3,800), plus ~4,000 lines it relies on
  (`ChatStore`, `ReplyBody`, `Theme`, `LiveCamera*`, `VoiceRecorder`, `SpeechToText`, `DocumentReader`, `PhotoLibrary`...).
- Most of it is optional features with heavy dependencies: camera monitors (Emgu CV, Camera2), photo search (PhotoKit,
  MediaStore, embeddings), documents (PdfPig, platform PDF renderers), image generation (a 10.6 GB model), MCP
  (`ModelContextProtocol.Core`), the user's own server (`Microsoft.Extensions.AI.OpenAI`), voice chat, ML Kit language
  identification. An app that wants a chat would inherit all of them.
- Each feature needs manifest / `Info.plist` entries (camera, microphone, location, photos, cleartext HTTP,
  `libOpenCL.so`). A library can't declare them for the app, and most apps shouldn't have them.
- App decisions are built in: the SQLite schema, `Theme.cs` (340 references from the chat page), Preferences keys,
  the model list (`ModelChoice`/`ModelOption`), the tool groups and their wording.
- Model handling is mixed into the page: the send path picks between `Chat.SendAsync` (Qwen) and a `ChatClientAgent`
  (Gemma 4, the server), with retries for context-full and servers rejecting `reasoning_effort`.

## Proposed design: two layers

### Layer 1 - conversation logic, no UI (`Emgu.LiteRT.LM.Chat`, new project and package)

The parts of the page that aren't UI become testable classes over `IChatClient`. netstandard2.0 + net8.0 (like
`Emgu.LiteRT.LM.Agents`); depends on `Microsoft.Extensions.AI.Abstractions`, `Emgu.LiteRT.LM.Models` (for
`LanguageModel`: token counting, `MaxNumTokens`, `CanSummarize`-style capabilities) and `Emgu.LiteRT.LM.Extensions.AI`
(`AsIChatClient()` for a local model). Pure managed; no MAUI.

- `ChatConversation`: the messages (text, attachments, a per-message context such as document excerpts), the system
  instructions, `ChatOptions` per send (tools, thinking), and `SendAsync(text, attachments, onUpdate, token)`
  returning the reply with thinking and tool steps. Interrupting (cancel) keeps the partial reply.
- Context window management, moved from the page: token estimates (`LanguageModel.CountTokens` when available, else
  ~3.5 characters a token; the measured image/audio/tool-step costs), the reply reserve and overheads, and compaction -
  summarize with the model (`CanSummarize`) or drop, keep the latest exchanges up to a quarter of the window, the
  context-full retry, priming a new session with the summary. Exposed as `ContextUsage` and `CompactAsync()`.
- Restoring a conversation (`Load(messages, summary, summaryUpTo)`), so an app can save and reopen chats its own way.
- View-model types the MAUI templates bind to, kept UI-free so they are testable here too: `ChatMessageItem`
  (`INotifyPropertyChanged`: role, text as it streams, thinking, thinking duration, tool steps, attachments, state -
  streaming / done / interrupted / failed) and an `ObservableCollection<ChatMessageItem>` on `ChatConversation`.
  Updates are raised on the caller's synchronization context, so the UI thread gets them without the control
  marshalling every chunk.

Why a separate package: an app with its own UI (or no MAUI - a console, WPF, a server) can use the conversation and
compaction logic without the control, and the existing test project tests it without MAUI.

### Layer 2 - MAUI control (`Emgu.LiteRT.LM.Maui`, new project and package)

`net10.0-android;net10.0-ios;net10.0-maccatalyst;net10.0-windows10.0.19041.0`; depends on `Microsoft.Maui.Controls`
and `Emgu.LiteRT.LM.Chat`. No native runtime dependency - the app adds the runtime
packages it wants (base, LM add-on, OpenCL/Metal), as today.

Built with XAML and templates. Public types:

| Type | From the demo | What it is |
|---|---|---|
| `ChatView` (templated `ContentView`, `ControlTemplate` in `Themes/ChatView.xaml`) | the transcript, message box and load card of `LiteRtLmChatPage.cs` | The chat. Bindable properties `Model` or `ChatClient`, `Conversation`, `Instructions`, `ThinkingEnabled`, `ShowAttachButton`, `ReadAloud`, `MessageTemplateSelector`; events `ExchangeCompleted` (for saving), `ModelLoadFailed`. Named template parts (`PART_Transcript`, `PART_Input`, `PART_Send`, `PART_Stop`, `PART_Attach`, `PART_LoadCard`, `PART_Status`) so a replaced template keeps working. |
| `ChatMessageTemplateSelector` + default `DataTemplate`s (`Themes/ChatMessages.xaml`) | `AddUserMessage`, `AddStreamingBubble`, `AddStepLine`, `AttachmentChip` | Picks the template for a user message, a reply, a tool step or a divider; apps replace one template or the selector. |
| `ReplyView` (control with a `ControlTemplate`) | `ReplyBody.cs` | Markdown, code blocks with Copy, maths via `MathText`; bindable `Text`. The parts it generates (paragraph label, code panel, quote, list item) take their look from named styles (`ReplyParagraphStyle`, `ReplyCodeBlockStyle`...), and the code panel is itself a `ControlTemplate`. Usable on its own. |
| `ThinkingView` (control with a `ControlTemplate`) | `ShowThinking`/`ThinkingPreview` | "Thinking…" with a live preview, then "Thought for N s", expandable; bindable `Thinking`, `Duration`, `IsThinking`. |
| `ModelLoadCard` (control with a `ControlTemplate`) | `ShowLoadCard`/`ShowLoadStage`/`ShowDownloadProgress` | Stage, download progress, size, not-on-Wi-Fi note, Cancel, Retry; bindable to a `ModelLoadState` from layer 1. |
| `Themes/ChatStyles.xaml` (`ResourceDictionary`) | `Theme` colours, fonts, `Card`, `PillButton`, `ToggleChip` | The default styles and colours, merged by the app (one line in `App.xaml`); light and dark via `AppThemeBinding`. Apps override any style or template by key. |
| `IChatAttachmentSource` | `OnAttachClicked`, `AddPendingImage/Audio` | Where attachments come from; built-in: photo library/camera (`MediaPicker`) and audio files. Images downscaled to 1024 px JPEG. |
| `IReplySpeaker` | `ReplySpeech`, `ReplyReader` | Optional read-aloud; default uses MAUI `TextToSpeech` with the `SpeechSegmenter` already in `Emgu.LiteRT.LM.Agents`. |

Tools are passed as `ChatOptions.Tools` (as the demo does), so `Emgu.LiteRT.LM.Agents`' tools or the app's own work
without the control knowing about them; tool calls show as step lines (`AgentTools.Describe`).

### What stays in the demo (first version)

Model list and picker, settings sheet and side panel, saved chats (`ChatStore`, SQLite), memories and goals, web
search settings and the Tavily key, documents, photo search, camera and sound monitors, voice chat, image generation,
translation of the thinking, task mode, MCP, the user's own server, device actions, compaction divider UI details,
`UiStallWatchdog`. The demo page becomes a `ContentPage` that hosts `ChatView` and adds these around it, using the
control's extension points (instructions, tools, per-message context, `ExchangeCompleted`, extra toolbar items).

Candidates for later add-on packages once the API has settled: voice chat and recording
(`Emgu.LiteRT.LM.Maui.Voice`), saved chats on SQLite (`Emgu.LiteRT.LM.Maui.Storage`), document questions.

## Steps

Each step builds the demo and is checked before the next. Device checks: Mac Catalyst (M1) and an Android phone
(Galaxy S10 or Pixel Fold, Gemma 4 E2B on the GPU), plus the iOS simulator build in CI.

1. **Project scaffolding** for both packages: `Emgu.LiteRT.LM.Chat/` and `Emgu.LiteRT.LM.Maui/` with
   `.shproj`/`.projitems`, CMake-generated `Directory.Build.props` from `cmake/Emgu.LiteRT.LM.{Chat,Maui}.*.in`,
   package ids from `EMGU_LITERT_LM_CHAT_NUGET_ID` / `EMGU_LITERT_LM_MAUI_CONTROL_NUGET_ID`, READMEs, solution entries (as for
   `Emgu.LiteRT.Models.CV`). The MAUI project compiles XAML (`MauiXaml` items, `UseMaui`). Both build empty for all
   their platforms. **Done:** `Emgu.LiteRT.LM.Chat/NetStandard/` (netstandard2.0, net8.0) and
   `Emgu.LiteRT.LM.Maui/Maui/` (android, ios, maccatalyst; windows on Windows hosts), the latter with an empty
   `Themes/ChatStyles.xaml` dictionary to prove the XAML compiles; both pack, in the CrossPlatform solution only (the
   other LM packages aren't in any solution). `EMGU_LITERT_LM_MAUI_NUGET_ID` was taken (the LM runtime aggregator
   `Emgu.LiteRT.LM.runtime.maui`), hence `..._MAUI_CONTROL_...`. Not yet built on Windows.
2. **`ReplyView` and `ThinkingView`** as templated controls with `Themes/ChatStyles.xaml`; move `ReplyBody`'s Markdown
   parsing into a UI-free part (layer 1 or internal) and its view building into `ReplyView`. The demo uses them from
   the package. Check: Markdown, code blocks, maths and thinking look the same as before (screenshots before/after on
   Mac Catalyst and Android), and a restyled template in the sample takes effect.
   **Done:** `Emgu.LiteRT.LM.Chat`'s `ReplyMarkdown` (`SplitBlocks`, `ParseLine`, `ParseInline`, `WithMath`,
   `Preview`; `UnitTestReplyMarkdown`, model-free) and `Emgu.LiteRT.LM.Maui`'s `ReplyView`, `CodeBlockView` and
   `ThinkingView` (`TemplatedView`s: templates found by key - the app's resources, else `Themes/ChatStyles.xaml` - and
   `PART_` elements bound in `OnApplyTemplate`; a missing part is skipped). The demo's bubble uses them; its
   `ChatTemplates.xaml` (merged in `App.xaml`) replaces `CodeBlockViewTemplate` and `ThinkingViewTemplate` to keep the
   Inter fonts and the Material Symbols chevron, which is also the restyling check. Checked with a temporary page
   showing the old `ReplyBody` beside the new views (three thinking states, headings, lists, quote, link, rule, inline
   and display maths, code, prices): identical on Mac Catalyst and the Pixel 8 emulator, the header tap expands; the
   library's own templates also render (demo overrides removed at run time); and a real Gemma 4 E2B reply with
   thinking streamed correctly on Mac Catalyst. iOS simulator build passes. Not checked: Windows, dark mode.
   Naming note for step 3: inside `Emgu.LiteRT.LM.*` namespaces `Chat` resolves to the namespace
   `Emgu.LiteRT.LM.Chat`, so `Emgu.LiteRT.LM.Models.Chat` needs a qualified name or an alias there.
3. **`ChatConversation` and `ChatMessageItem` (layer 1, `Emgu.LiteRT.LM.Chat`)**, extracted from `SendAsync`,
   `RunAgentAsync`, `PrepareContextAsync`, `CompactAsync`, `SummarizeAsync`, `PrimeModel`, `EstimateTokens`. Unit tests
   with a fake `IChatClient`: streaming updates, cancel keeps the partial reply, compaction by summary and by dropping,
   the context-full retry, restoring.
   **Done:** `ChatConversation` (`SendAsync(text, attachments, options, context, token)`, `CompactAsync`,
   `GetContextUsageAsync`, `Load(messages, summary, contextStart)`, `Clear`; events `ExchangeCompleted`, `Compacted`,
   `Summarizing`; the demo's estimates and limits as settable properties) and `ChatMessageItem` / `ChatToolStep`
   (`INotifyPropertyChanged`; `ChatMessageState` streaming / done / interrupted / failed). Differences from the page:
   the client is stateless (every request carries the summary pair, the messages the model sees and the new one), so
   there is no separate "prime" step; each finished reply is sent back as the client's own response messages (tool
   calls and results included), which `LiteRtLmChatClient` matches by its message keys and so doesn't rebuild its open
   conversation, and as text after compaction or restoring; the summary is a request of its own on the same client; a
   failed exchange is kept in `Messages` but not given to the model again; `SendAsync` continues on the caller's
   context, so the items change on the UI thread. `UnitTestChatConversation` (9 tests, scripted `IChatClient`, no model)
   and `TestChatConversationGemma4E2B` (`TEST_MODELS`: a fact recalled, a summary written, the fact recalled from it;
   9 s on an M1's CPU). The demo doesn't use it yet (step 6).
4. **`ChatView` with plain chat and `ModelLoadCard`** (control template, message templates and selector), plus a
   minimal sample app (`Emgu.LiteRT.Example/MauiChat/`, one page, Gemma 4 E2B or Qwen3) that proves the few-lines goal
   and restyles one message template and the send button to show the templates work.
   **Done:** `Emgu.LiteRT.LM.Chat`'s `ModelLoadState` (wraps `LanguageModel.Init`: `IsDownloaded` - the cheap verified
   check - stage, download progress throttled to one queued update, error, `LoadAsync` / `Cancel`; changes on the
   caller's context), binding flags on `ChatMessageItem` (`HasAnswer`, `IsStreaming`, `IsInterrupted`, `IsFailed`,
   `IsFinishedWithoutAnswer`, `StartsContext` for the compaction divider) and `ChatToolStep.Text`; `ChatConversation(model)`
   now creates the model's `LiteRtLmChatClient` with the first message (its constructor needs the model loaded, which
   crashed the first sample run), and answers lose the line breaks models put after their thinking.
   `Emgu.LiteRT.LM.Maui`: `ChatView` (`Model` / `ChatClient` / `Conversation`, `Instructions`, `ThinkingEnabled` - sent as
   `ReasoningEffort` - `Options`, `MessageTemplateSelector`, `Placeholder`; `SendAsync`, `StopReply`; events
   `ExchangeCompleted`, `ModelLoadFailed`; a downloaded model loads as soon as the view is shown, otherwise the card offers
   the download or the first message starts it; Stop while a message waits for the model gives up the message), the
   transcript a `BindableLayout` with `ChatMessageTemplateSelector` (`ChatUserMessageTemplate`, `ChatReplyTemplate`: steps,
   bubble with `ThinkingView`/`ReplyView`, "…" when interrupted, the error when failed), `ModelLoadCard` (stage,
   progress, Wi-Fi note, Download/Load, Cancel, Retry), styles `ChatSendButtonStyle`/`ChatStopButtonStyle` given to the
   parts by code (a template's `StaticResource` would only see the library's own dictionary), light and dark colours.
   Sample `Emgu.LiteRT.Example/MauiChat/` (namespace `MauiChat`: under `Emgu.LiteRT.*`, `Environment` resolves to
   `Emgu.LiteRT.Environment`; android, ios, maccatalyst; props from `cmake/Emgu.LiteRT.MauiChat.Directory.Build.props.in`;
   in the CrossPlatform solution): one page, `ChatView` with Qwen3 0.6B (`ChatCompaction.Drop`), `App.xaml` replacing
   `ChatSendButtonStyle` ("Ask", green) and `ChatUserMessageTemplate`. Checked with a temporary self-test: Mac Catalyst
   (dark theme; streaming thinking preview, Stop, 12 x 12 = 144 and the name recalled in the second turn) and the Pixel 8
   emulator (light theme; the card downloading Qwen3 with progress and Cancel, then the same two answers). On the emulator
   the first load aborted the app: its data partition was full and LiteRT's XNNPACK weight cache aborts the process when
   a write fails ("cannot append buffer to cache file", SIGABRT) - not catchable from .NET. iOS and Windows not built;
   attachments and read-aloud are step 5.
5. **Attachments and read-aloud** (`IChatAttachmentSource`, `IReplySpeaker`).
   **Done:** `Emgu.LiteRT.LM.Chat`'s `ReplySpeechText` (`CleanForSpeech`, `DetectScriptLanguage` - the demo's
   `ReplySpeech` now calls them; `UnitTestReplySpeechText`). `Emgu.LiteRT.LM.Maui`: `IChatAttachmentSource` /
   `MediaChatAttachmentSource` (a menu of photo library, camera where supported, audio file - WAV/MP3/FLAC - by what the
   model takes; `ChatAttachmentKinds`), `ChatImages.PrepareAsync` (at most 1024 px, JPEG, upright: Android applies the
   EXIF orientation itself with `BitmapFactory`/`ExifInterface`, Apple's decoders already do; no Emgu CV),
   `ChatAttachmentView` (thumbnail or audio chip with its length, ✕ for pending ones; `ChatAttachmentViewTemplate`),
   `IReplySpeaker` / `TextToSpeechReplySpeaker` (reads a reply sentence by sentence while it streams with
   `Emgu.LiteRT.LM.Agents`' `SpeechSegmenter` - a new dependency of the package - in a voice for its language, the
   demo's locale choice; `DetectLanguage` can be replaced, e.g. with a platform identifier; `SpeakAsync` is virtual).
   `ChatView`: `AttachmentSource`, `AttachmentKinds` (null: the model's `SupportsImages` / `SupportsAudio`),
   `ShowAttachButton`, `PendingAttachments` (shown in `PART_Attachments`; a message may be only attachments),
   `ReadAloud`, `ReplySpeaker` (stopped by Stop and by the next message); `ChatAttachButtonStyle`; the user template
   shows attachments and hides an empty bubble (`HasText`, `HasAttachments`). `ModelLoadState.DisplayName` /
   `DownloadSize` now notify (the card kept the class name when they were set after it was shown). Checked with a
   temporary self-test: Mac Catalyst with Gemma 4 E2B - a photo tagged EXIF orientation 6 prepared upright (333 x 500
   from 500 x 333), shown as a pending chip, sent, answered ("These are tulips..."), and the reply passed to the speaker
   in two sentences while streaming (a logging speaker - nothing was spoken); Galaxy S10 - the same photo upright on
   Android, the chip, the Attach menu (photo library, take photo, audio file; the library wasn't opened), ✕ removing it.
   Not run: picking from the library or camera, audio attachments end to end, real speech output, Windows.
6. **Move the demo's chat page onto `ChatView`**, feature by feature (agent tools, documents, photos, camera,
   voice, server, task mode...), keeping each verified behaviour: compaction after reopening a saved chat, the
   "Provided less images than expected" workaround (`FilterChannelContentFromKvCache`), voice interruption, model
   switching while loading, the Android scroll clamp. Device checks after each feature group.
7. **Docs**: package README with the sample, `CLAUDE.md`, the demo `README.md`.

## Risks and open questions

- **Regressions in the demo (step 6)** - the main risk: the chat page's behaviour was verified on devices piece by
  piece. Mitigation: move in small groups, re-run the device checks listed in `CLAUDE.md` for each.
- **Transcript as a templated list**: the demo appends views to a `VerticalStackLayout` in a `ScrollView`; with
  templates the transcript is a bound list. `CollectionView` virtualizes but has had problems with items that change
  height while streaming and with scrolling to the end (the demo already needed an Android scroll clamp); a
  `BindableLayout` on a `VerticalStackLayout` keeps today's behaviour without virtualization. Proposal: start with
  `BindableLayout` (chats are short and the demo's behaviour is proven), behind the `PART_Transcript` part so it can
  change later.
- **Streaming through bindings**: the reply text changes many times a second; bindings must not rebuild the whole
  `ReplyView` per chunk. `ReplyView` keeps its incremental update (only the last paragraph or code block changes), as
  `ReplyBody` does now.
- **Template contract**: replaced templates must keep the named parts; missing parts disable that feature (e.g. no
  `PART_Attach` = no attach button) rather than throwing. Document the parts in the README.
- **Windows**: the demo's chat is enabled there but voice and some features aren't; the control should work there
  with text and images. Not device-tested recently.

## Effort

Roughly: step 1 half a day; step 2 one to two days (templated controls); step 3 one to two days with tests; steps
4-5 two to three days (templates, selector, sample); step 6 two to four days including device checks; step 7 half a
day. Steps 1-5 give a usable package without touching the demo's behaviour;
step 6 can follow separately.
