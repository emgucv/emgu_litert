# Plan: a reusable MAUI chat control (`Emgu.LiteRT.LM.Maui`)

Status: decisions taken 2026-10-22 (below). Step 1 (scaffolding) done 2026-10-23; no code has moved yet.

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
3. **`ChatConversation` and `ChatMessageItem` (layer 1, `Emgu.LiteRT.LM.Chat`)**, extracted from `SendAsync`,
   `RunAgentAsync`, `PrepareContextAsync`, `CompactAsync`, `SummarizeAsync`, `PrimeModel`, `EstimateTokens`. Unit tests
   with a fake `IChatClient`: streaming updates, cancel keeps the partial reply, compaction by summary and by dropping,
   the context-full retry, restoring.
4. **`ChatView` with plain chat and `ModelLoadCard`** (control template, message templates and selector), plus a
   minimal sample app (`Emgu.LiteRT.Example/MauiChat/`, one page, Gemma 4 E2B or Qwen3) that proves the few-lines goal
   and restyles one message template and the send button to show the templates work.
5. **Attachments and read-aloud** (`IChatAttachmentSource`, `IReplySpeaker`).
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
