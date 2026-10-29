# MAUI demo features

How the demo's chat features work, and what was measured or verified on which device. File names are the demo's
(`Chat/`, `Services/`... - see [ARCHITECTURE.md](ARCHITECTURE.md)) unless a library is named. Open work
is in [TODO.md](TODO.md); the GPU and add-on packages themselves are in [docs/packaging.md](../../docs/packaging.md).

## Saved chats

- The library's `ChatHistoryStore` (`Emgu.LiteRT.LM.Chat.Sqlite`; the demo's own `ChatStore` until 2026-10-27) keeps the chats in SQLite (`sqlite-net-pcl`), in `chats/chats.db3` in the app data folder - a subfolder,
  because an unsandboxed Mac Catalyst app's data folder is `~/Library` itself. Photos and audio are files in
  `chats/attachments/<id>/`.
- Each completed exchange is saved: the user's message with its attachments, the tool step lines, and the answer with its
  thinking.
- A history button (narrow windows) or a Chats section in the side panel (wide ones) lists the chats, with delete, which
  asks first. A new chat, or switching to another model, starts a new saved chat.
- Opening a chat switches to its model, redraws the transcript and gives the model the conversation with the next message
  (`ChatConversation.Load(messages, summary, contextStart)`); each reply's saved steps and cards are restored with it.
- Verified across an app restart on Mac Catalyst with Gemma 4 E2B (agent) and Qwen3 (plain chat).

## Loading models

- When the page opens, the last-used model (Preferences) is selected. A selected model starts loading in the background
  straight away if its file is on the device; otherwise a card offers the download, with its size and a note when not on
  Wi-Fi.
- The card at the top of the conversation shows the stage, a download progress bar, Cancel, and errors with Retry. A message
  sent while the model loads waits for it.
- Switching models cancels the load, waits for a cancelled load to finish before loading another, and releases the old
  model on a background thread.
- Verified on the Pixel Fold: the first load took 11 s (9 s of it a one-time hash) with no UI stall; later loads take under
  3 s.
- `Chat.SendAsync` and `LiteRtLmChatClient` create their LiteRT-LM conversation on a background thread: on the caller's (UI)
  thread it stalled the demo for ~2.5 s after Send on a Pixel Fold, which the stall watchdog found.

## Layout and controls

- Since 2026-10-26 the page is `Emgu.LiteRT.LM.Maui`'s `ChatPage` (`Chat/LiteRtLmChatPage.cs` derives from it; see
  [ARCHITECTURE.md](ARCHITECTURE.md)): a one-line header - back, a model button that opens the model list, new chat,
  settings - then the conversation, then the message box with per-message chips: "Think" and "Web search" (the library's),
  "Plan" (`TaskFeature`) and "Voice chat" (`VoiceModeFeature`). Each feature's settings section comes from its
  `BuildSettingsSection`, in the order the page lists the features.
- Read-aloud and the Tavily key are in the settings sheet.
- Windows at least 820 points wide - desktops, a foldable's inner screen - show the model list and the settings in a side
  panel instead (the library's layout). Verified on Mac Catalyst (wide) and a Pixel Fold's cover screen (narrow).
- On phones the settings open in the library's `BottomSheet`, whose content scrolls with the title and closing button
  fixed - a port of the demo's sheet, which had no scroll view or height limit, so a long settings view ran off the top of
  the screen and its end (the context window) couldn't be reached (verified on a Galaxy Z Fold3 and a Pixel 8 emulator).
  The demo's own sheet, `MenuSheet` (`Theme.cs`), remains for menus of rows: the "+" menu (`DemoAttachmentSource`) and the
  vision demo page.
- The model picker offers Qwen3, Qwen3.5 0.8B VL, Qwen3.5 4B, Gemma 4 E2B and E4B (and Gemma 4 12B on Mac Catalyst and
  Windows, from `Emgu.LiteRT.LM.Chat`'s `ChatModelCatalog`). Each model's `AcceptsImages`, `AcceptsAudio` and `CanThink` flags decide which of the
  attach, talk and thinking controls show.
- Gemma 4 always gets the agent's tools and instructions. The Web search chip sets `ChatToolMode.Auto` or `None` per
  message, so it can change in the middle of a conversation.

## Replies: Markdown, code, maths and thinking

- `Emgu.LiteRT.LM.Maui`'s `ReplyView` (it was the demo's `ReplyBody.cs`; the parsing is `Emgu.LiteRT.LM.Chat`'s
  `ReplyMarkdown`) renders basic Markdown as `FormattedString` spans: headings, **bold**, *italic*, `inline code`, bullet and
  numbered lists, quotes, rules and links (a tap opens http/https links only). Underscores inside words, as in snake_case,
  aren't italics, and unclosed markup shows as typed while it streams.
- Tables (since 2026-10-27): `ReplyMarkdown` makes a header row followed by a separator row (`|---|:---:|---:|`, any number
  of dashes, a bar required) and the rows with bars after it a `ReplyBlockKind.Table` block (`TableHeader`, `TableRows`,
  `TableAlignments`; a missing cell is empty, an escaped `\|` stays in its cell; a table still streaming grows row by row).
  `ReplyView` draws it as a grid with a full border - lines around it and between every row and column (in thin grid rows
  and columns of their own, so they show over any bubble colour), the header in bold on a light shade, inline Markdown in
  cells - in a horizontal `ScrollView`, and a reply with a table uses the full width like one with code. A column whose
  text is longer than 220 points gets that width and wraps: an `Auto` column measured a wrapping label at its one-line
  width, so the row was one line high and the rest was cut off ("No confirmed natural" without "satellites").
  Read aloud, a row is its cells ("Chip, A18.") and the separator row is skipped. Checked on Mac Catalyst: Gemma 4 E2B's
  table of Mercury, Venus and Earth (three rows, wrapped cells) drew while streaming and in full - though its numbers came
  from Wikipedia snippets and one was wrong (Mercury's diameter as 1,550 km). Not yet seen on a phone's width, where the
  horizontal `ScrollView` is what to watch (see the code blocks below).
- Code blocks are dark monospace panels with the language and a Copy button (`Clipboard`). A reply with code uses the full
  width instead of the 420-point bubble limit. The reply updates in place while streaming; code lines wrap, because a
  horizontal `ScrollView` around the code label was measured once and showed only the first line of streamed code.
- LaTeX maths is converted to Unicode text by `Emgu.LiteRT.LM.Models`' `MathText`: `$...$` and `\(...\)` within a line,
  `$$...$$` and `\[...\]` as centred blocks (also across lines, and shown while streaming), also inside bold, italic and link
  text, and for read-aloud.
  - It uses superscripts and subscripts where Unicode has them, else `^(...)`; `\frac` becomes a/b with parentheses around
    multi-term parts; it handles `\sqrt`, Greek letters, operators, arrows, sets, `\mathbb`, `\text` and accents;
    `lim_{x \to 0}` becomes "lim x→0"; unknown commands lose their backslash.
  - There is no typesetting - no stacked fractions or matrices, which would need a WebView with KaTeX per reply.
  - A `$` only starts maths when followed by a non-space and closed by a `$` after a non-space and not before a digit, so
    "$5 and $10" stays as it is.
  - Verified on Mac Catalyst with Gemma 4 E2B explaining the quadratic formula.
- Thinking: the Think chip is on by default and remembers its last setting (Preferences `litert_lm_chat_think`); it only
  applies to `CanThink` models.
  - While the model thinks, the reply shows a "Thinking..." header with a spinner and the thinking's last ~180 characters.
    That preview stays plain text - maths converted; code fences, `**`, backticks and heading marks removed - since a tail
    cut out of formatted text can't be laid out.
  - Once the answer starts, it collapses to a tappable "Thought for N s" header (the time from the first thinking text to
    the first answer text; "Thoughts" in reopened chats). It stays expanded if a reply ends without an answer.
  - Expanded, the thinking is rendered like the answer: `Emgu.LiteRT.LM.Maui`'s `ThinkingView`, whose body is a `ReplyView`
    in the thinking colour at 13 pt, with Markdown, code blocks with Copy, and maths. Verified on Mac Catalyst with Gemma 4
    E2B.

## Attachments and voice messages

- For the Gemma models a "+" button attaches, to the next message: a photo (from the library, or "Take Photo" through
  `MediaPicker.CapturePhotoAsync` when `IsCaptureSupported`), a sample image, a WAV, MP3 or FLAC file, or LiteRT-LM's
  `audio_sample.wav` (linked from the submodule as a `MauiAsset`).
  - Camera access is declared for every platform: Android's `CAMERA` plus an `IMAGE_CAPTURE` intent query,
    `NSCameraUsageDescription` on iOS and Mac Catalyst, and the Windows `webcam` capability. (The image demos' existing
    "Take Photo" lacked them.)
  - Images are decoded with Emgu CV, downscaled to 1024 px and re-encoded as JPEG, since LiteRT-LM's stb can't read e.g.
    HEIC.
  - Verified on Mac Catalyst: Gemma 4 E2B describes an image and transcribes audio, though given both in one message it
    ignored the audio.
- A microphone button records a voice message with `Emgu.LiteRT.LM.Maui`'s `VoiceRecorder`: 16 kHz mono WAV through
  AVAudioRecorder, Android's AudioRecord or Windows' MediaCapture, at most 30 s, with the microphone permission in each
  platform's manifest. It is sent with empty text, which Gemma 4 answers directly (checked with WAVs generated by `say`).
- The voice classes moved from the demo into the libraries step by step (history/CHAT_VOICE_AND_HISTORY_PLAN.md):
  - Step 1: `VoiceRecorder`, `SpeechRecognizer` and `MicrophoneGate` into `Emgu.LiteRT.LM.Maui`, and `VoiceLanguages` into
    `Emgu.LiteRT.LM.Chat`.
  - Step 2 gave `ChatView` a microphone (`ChatView.Voice.cs`, `PART_Talk`, `VoiceMessages` Auto/Audio/Text/Off) and
    `ChatMessageItem.Transcript`; verified on a Galaxy S10 and Mac Catalyst by playing a spoken question through the device's
    speaker while recording. An app that replaces `ChatViewTemplate` in its resources replaces it for every `ChatView`, so a
    test page in the demo sets the library's `ChatStyles` templates itself. (The demo's own `ChatViewTemplate` override - the
    transcript without the message box, for its old page - went with the move to `ChatPage` on 2026-10-26.)
  - Step 3 added the saved-chats package `Emgu.LiteRT.LM.Chat.Sqlite`: `ChatHistoryStore` over the demo's
    `Conversation`/`Message` tables, `ChatHistory`, and `UnitTestChatHistoryStore`, which also reads the demo's saved chats.
  - Step 4 added `Attach(conversation, chatId)` (`ChatHistoryBinding`), which saves exchanges, regenerated replies,
    compaction and late transcripts in order. The `MauiChat` sample reopens its last chat, kept in `MauiChat/chats` in the
    app data folder - on Mac Catalyst that is `~/Library`, where the demo keeps `chats/`.
  - Step 5 made the demo's `ChatStore` a `ChatHistoryStore` with its own tables and columns: `ConversationRecord` and
    `MessageRecord` derive from the library's records, and its `AddMessageAsync` takes a language as its last parameter and
    hides the library's. Verified with the existing saved chats on Mac Catalyst and a Galaxy S10.

## Reading replies aloud

- "Read replies aloud" uses MAUI's TextToSpeech through `Emgu.LiteRT.LM.Maui`'s `TextToSpeechReplySpeaker` (the demo's
  own `ReplySpeech.cs` was a copy of it, removed on 2026-10-25; the monitors' spoken alerts use one too). It picks a voice for the reply's language -
  detected from the writing system most of its letters use (Han → zh, kana → ja, Hangul → ko, Cyrillic, Arabic...),
  preferring the device's own region - because the default voice read e.g. Chinese with an English voice. It strips
  Markdown, links and emoji first. Verified on a Pixel Fold: zh-CN and ja-JP voices were chosen.
- Replies are read while they stream, sentence by sentence: `ReplyReader` over `SpeechSegmenter`
  (`Emgu.LiteRT.LM.Agents/VoiceConversation.cs`, namespace `Emgu.LiteRT.LM.Agents.Audio`, with `VoiceActivityDetector` and
  `IVoiceInput`). Thinking, code blocks and table rows aren't read.

## Voice chat (hands-free)

- The "Voice chat" chip (Gemma 4; not on Windows, whose recorder reports no level) is a hands-free conversation
  (the library's `VoiceModeFeature` since phase C, 2026-10-27, a layer over the page; it follows the page's `IReplySpeaker`,
  whose `Speaking` event and `Completion` task are the interface's now, and sends through `IChatPageHost.SendVoiceMessageAsync`). A full-screen view listens; `VoiceActivityDetector` ends the message after 0.9 s of quiet
  (levels from AVAudioRecorder's metering, or Android's samples); the audio is sent without thinking, the reply is read, and
  it listens again.
- A tap interrupts: the reply is cancelled, kept with "…" (`ChatMessageState.Interrupted`), and given to the model as text
  from then on. With headphones, speaking interrupts too.
- Verified on Mac Catalyst with `say` recordings in place of the microphone (`IVoiceInput`), and with real microphones and
  loudspeakers on Mac Catalyst (a Mac mini with a USB microphone) and a Pixel Fold. On the Pixel Fold, Android's recorder
  read 200 ms at a time and left most 50 ms level readings empty; as digital silence they dragged the background level down,
  so a noisy room's peaks kept speech from ending. It now reads 20 ms at a time, and the detector ignores digital silence.
  Not yet tried with headphones.

## Context window and compaction

- Sizes: the models run with a 4,096-token context window (`LanguageModel.MaxNumTokens`, set on the engine; LiteRT-LM picked
  4096 anyway), except that Gemma 4 E2B and E4B get 8,192 on Mac Catalyst and Windows (`ModelOption.DesktopContextTokens`).
  The Qwen files are fixed at 4096; both Gemma 4 files allow up to ~32k (E2B's model info says dynamic 32003; E4B's says 0,
  but its model has the same 32003 magic number and accepted 8k and 16k).
- What a bigger window costs, measured by filling the window with a fact at its start (recalled at every size). Reading the
  whole conversation - what opening a saved chat or compacting costs:

  | | 4k | 8k | 16k | 32k |
  |---|---|---|---|---|
  | Gemma 4 E2B, Apple Silicon Mac CPU | 12 s (2.9 GB) | 27 s (4.1 GB) | 82 s (4.6 GB) | 262 s (5.2 GB) |
  | Gemma 4 E4B, Apple Silicon Mac CPU | 28 s | 61 s | 168 s | |
  | Gemma 4 E2B, Pixel Fold CPU | 123 s | 342 s (4.9 GB; 1.5 GB of the phone's RAM left) | | |

  (Peak RSS in brackets.) A short follow-up in the open conversation took 6-8 s either way.
- So phones kept 4k by default - until the agent's instructions and tool definitions alone grew to ~4k tokens (~14.5k
  characters; 7 of the 21 tools were the live camera's). Adding the goal tools made every Gemma 4 E2B message on a Galaxy
  S10 fail with "This request and its tools do not fit in the current context window". Now Automatic gives models with
  tools (`CanUseTools`: Gemma 4) their `DesktopContextTokens`, 8k, on phones too; the other models stay at 4k.
- Settings > Context window (Gemma 4 only, up to `ModelOption.MaxContextTokens` 32000, under the files' 32003 limit; Qwen
  hides it) chooses Automatic - 8k for Gemma 4, else 4k - or 4k, 8k, 16k or 32k, saved for all models
  (the page's "context_tokens" setting). `ContextTokensFor(option)` is what `LoadModelAsync` sets `MaxNumTokens` to, and
  `ModelReady`/`PrepareModelAsync` compare it with the loaded or loading window, so a change reloads the model and the chat
  continues (compacted with the next message if it no longer fits).
  - Verified with memories off, a code word recalled after each reload: Mac Catalyst's Metal GPU 8k → 16k → 32k → 8k,
    reloads 1-8 s; a Galaxy Z Fold3's OpenCL GPU 4k → 16k → 32k → 4k, reloads 8-17 s. The GPU cache grows with the window
    (Z Fold3 program cache 15 → 64 MB).
- A conversation that outgrows the window fails with "Input token ids are too long..." (a new conversation) or "Prefill
  input length exceeds available state entries" (a continued one); a reply that hits the limit just ends. While the model
  re-reads the conversation, the status says "Reading the conversation so far...".
- Before each send the page estimates the conversation: text by `LanguageModel.CountTokens` (the tokenizer, within ~1% of
  LiteRT-LM's count), plus 8 per message, 270 per image and 25 per second of audio, plus thinking and 450 per tool step until
  the conversation is rebuilt. The image and audio costs were measured with a 128-token window, whose error message reports
  the exact count: Gemma 4 and Qwen3.5 VL images ~260 tokens, `audio_sample.wav` (5.8 s) ~146.
- If the estimate plus the message exceeds the window less 1024 for the reply and 50 (chat template) or 600 (agent
  instructions and tools):
  - Models that summarize well (`CanSummarize`: Gemma 4, Qwen3.5 4B) summarize the earlier messages in a conversation of
    their own, keeping the latest exchanges up to a quarter of the window.
  - The others (Qwen3 0.6B, Qwen3.5 0.8B) drop them - Qwen3 0.6B's summaries copied the instructions' example and repeated
    themselves.
  - The model is then given the summary as a user/assistant message pair plus the kept messages (`ChatConversation` sends
    the summary pair and the messages since with every request).
  - If LiteRT-LM still reports the context full, all earlier messages are dropped and the send is retried once.
- The transcript keeps everything, with a divider above the first message the model still sees (tap it for the summary).
  The summary and the last message it covers are saved with the chat (`ConversationRecord.Summary`/`SummaryUpTo`) and
  restored on reopening. Settings shows the estimated usage and "Compact now".
- Verified on Mac Catalyst: Gemma 4 E2B summarized and then answered from the summary, also after reopening the chat; Qwen3
  dropped messages; the retry and "Compact now" worked.

## The GPU

- Android: Gemma 4 E2B runs on the GPU (`ModelOption.GpuOnAndroid`; Settings "Use the GPU", on by default) through LiteRT's
  OpenCL accelerator `libLiteRtOpenClAccelerator.so`, with `Init(backend: "gpu")` and `ActivationDataType.Float32`. If the
  engine fails at the LoadingEngine stage, `_gpuUnavailable` falls back to the CPU for the rest of the session. The app must
  declare `<uses-native-library android:name="libOpenCL.so" android:required="false" />` (Android 12+); our `libLiteRt.so`
  (`gpu_registry.cc`) dlopens the accelerator by name.
- Gemma 4 E4B on Android's GPU too since 2026-10-27 (`ChatModelCatalog`; it had never been tried). On the Pixel Fold
  (11.7 GB, 5.7 GB free): the first message 118 s including the GPU load, the next one 38 s with thinking and a tool call,
  both right ("17 times 23" = 391, then back); the iPhone 16e/17e comparison 404 s (284 s thinking): it searched "iPhone 16e
  vs iPhone 17e specs comparison", read MacRumors' buyer's guide with `read_web_page`, and wrote a 9-row table (A18 vs
  A19, 128 vs 256 GB base, Qi 7.5 W vs MagSafe, C1 vs C1X...) without calling the 17e a rumour, where E2B on the GPU
  had made one search and called it "hypothetical". Long chats, 16k/32k and memory pressure on E4B's GPU aren't measured.
- fp32, because with the default fp16 Gemma 4 E2B doubled digits after a long conversation ("73342" for 7342) at every
  window size, while short chats were fine. fp32 fixed it at ~1.5 times the time. On the Pixel Fold a full 4k chat re-reads
  in 8 s (CPU 123 s), and follow-ups take ~1 s (CPU ~6 s). Details and the fp16/fp32 table are in
  [Performance.md](Performance.md).
- LiteRT-LM's GPU top-k sampler prebuilt isn't shipped: it added ~25 s to every load and fixed nothing (sampling falls back
  to the CPU).
- The GPU keeps `<model>_..._mldrift_{program,weight}_cache.bin` next to the model (~0.8 GB for E2B; it grows when the
  window size changes): the first load takes ~30 s, later ones ~2.6 s. Turning the switch off reloads on the CPU, continues
  the chat (a new client, given the conversation) and deletes the cache.
- Mac Catalyst: the Gemma 4 models (`ModelOption.GpuOnMac`: E2B, E4B, 12B) run on the GPU through LiteRT's Metal accelerator
  `libLiteRtMetalAccelerator.dylib`. fp16 was wrong on the M1's GPU as well (Gemma 4 E2B "732" for 7342, 12B "7341"), so
  `Gemma4.CreateEngineSettings` itself sets `ActivationDataType.Float32` for the "gpu" backend (an Init `configureSettings`
  can override it), and the demo no longer sets it.
- `Gemma4_12B` (`litert-community/gemma-4-12B-it-litert-lm`'s `gemma-4-12B-it.litertlm`, 6.9 GB, with the vision and audio
  encoders - the repo's 6.0 GB `-gpu`/`-web` files lack them) is offered on Mac Catalyst and Windows (`ChatModelCatalog`;
  ~8 GB of memory). On an M1 with 16 GB: ~8.6 tokens/s, a full 4k chat re-read in 38 s, warm load 3.3 s, GPU cache 6.0 GB.
- The Catalyst demo keeps its models in `~/Documents/LiteRT-LM` (the test runner in `~/Library/Application Support/LiteRT-LM`).
- `UnitTest1.TestLiteRtMobilenetGpu` (skipped without the Metal plugin) runs MobileNet through `CompiledModel` with
  `HwAccelerators.Gpu`: the same top class as the Interpreter, max abs diff 0.0015 on an M1.

## The chat agent and its tools

- Every message goes through `Emgu.LiteRT.LM.Chat`'s `ChatConversation`, over `ChatClientBuilder(...).UseFunctionInvocation()`
  on the model's `LiteRtLmChatClient` or the server's client (`ChatPage`'s stable client proxy, so a reloaded model keeps
  the conversation; the server's client comes from the page's `CreateChatClient` override). It replaced a `ChatClientAgent`
  and, for Qwen, `Models.Chat`.
- For Gemma 4 each message passes its tools in `ChatOptions.Tools` (`ChatPage.ComposeOptionsAsync`, `ChatToolMode.Auto`),
  so settings can change them in the middle of a conversation: the page's `ChatSkillSet` asks each feature for its tools
  (`Tools`, or the `ToolGroups` its router turns on when the message mentions them) and instructions. The core tools,
  memories, web search, location and the clipboard are the library's features (`CoreToolsFeature`, `MemoryFeature`,
  `WebFeature`, `LocationFeature`, `ClipboardFeature`); the memories are an `Emgu.LiteRT.LM.Chat.Sqlite`
  `SqliteAgentMemoryStore` over the chat store. Each tool call shows as a step line.
- Always: `get_current_date_time`, `convert_units` and `calculate` (`Emgu.LiteRT.LM.Agents.Calculator`, a recursive-descent
  evaluator: + - * / ^ %, parentheses, pi and e, sqrt/ln/log/sin/.../round/min/max/pow, × ÷ − ·, a trailing % as percent;
  -2 ^ 2 is -4). Small models get arithmetic wrong: Gemma 4 E2B called it for "1234.5 times 678.9" and answered 838,102.05.
- Memories - `remember` and `forget`, with Settings > "Memories about you" > "Remember things about me" on (on by default):
  - Kept in the chats database's `Memory` table (`SqliteAgentMemoryStore`), on the device only. The latest memories, up to 1,200 characters, go into each
    message's `ChatOptions.Instructions`, after the agent's own instructions.
  - `remember` skips a fact whose words (less stop words) are all in one already saved, since the model re-saves known facts
    in other words; the step line then says "Already remembered". Settings lists the memories, with delete.
  - Under a reply, a line names the memories it drew on ("Used a memory: The user is vegetarian."); tapping it lists them with
    delete. The model doesn't say which it used, so `AgentTools.MemoriesUsed` judges by words: a memory with a distinctive word
    in the reply that the message didn't have, or two in the reply. Not for a memory deleted in the same turn. Saved with the
    chat as a step line. Checked on Gemma 4 E2B's replies (Metal GPU, six memories, ten questions): "What should I cook
    tonight?" -> vegetarian, a birthday message -> Mia, a sleep tip -> night shifts, a weekend plan -> Paris and hiking, maths
    and general questions -> none; missed: a Spanish phrase for "learning Spanish" (no shared word). The line and its sheet verified in the app on a Pixel Fold (2026-10-25).
  - Verified: it saved "The user is vegetarian and their daughter's name is Mia." and, in a new chat, answered "Your
    daughter's name is Mia" and suggested a vegetarian dinner.
- `get_current_location`, with Settings > "Share my location" on (off by default):
  - `Permissions.LocationWhenInUse` is asked on first use; then `Geolocation`'s last known position if under 30 minutes
    old, else a low-accuracy fix; then `Geocoding.GetPlacemarksAsync` for "sub-locality, locality, region, country" (online,
    through Apple or Google; coordinates only where it fails, e.g. Windows without a map key). Coordinates are rounded to
    2 decimals, and it all runs on the UI thread.
  - Declared: Android's `ACCESS_COARSE/FINE_LOCATION`, `NSLocationWhenInUseUsageDescription` on iOS and Mac Catalyst, the
    Windows `location` capability.
  - Verified on the arm64 API 35 emulator with a `geo fix`: "The user is in Shibuya, Tokyo, Japan (latitude 35.66, longitude
    139.70)", and "Location services are turned off" when they are.
- With the Web search chip on:
  - `get_weather` (Open-Meteo, no key): its geocoding API for a place name - the whole name, then its first comma-separated
    part - or coordinates, then the current conditions and a 3-day forecast with the WMO weather codes in words. Verified
    for Paris.
  - `convert_currency`, and `read_web_page` (the built-in reader, or Tavily's with a key).
  - `search_wikipedia`: MediaWiki search plus intro extracts, with most of the ~1,500-character budget for the best match.
  - With a Tavily API key (kept in `SecureStorage`): `search_web` and `read_web_page` through Tavily's `/search` and
    `/extract`. Since 2026-10-27 a search asks for 5 results at "advanced" depth (2 credits, so the free plan's 1,000 monthly
    credits are about 500 searches) and gives the model about 2,500 characters of them; before, 3 "basic" results in 1,500
    characters gave Gemma 4 E2B little to go on. A query with news words ("news", "latest", "recent", "update"...) searches
    Tavily's news over the past month (`AgentTools.NewsTimeRange`; "day" for today, yesterday or breaking news, "week" for
    this week), each result with its publication date and the answer starting with today's date; if that finds nothing, an
    ordinary search follows. The tool's description asks the model to put "news" or "latest" in such queries. Checked on
    the Pixel Fold: "the latest news about the James Webb Space Telescope" gave recent articles, where the ordinary search
    had included one dated January 2025.
  - With a Twelve Data API key (free at twelvedata.com: 800 requests a day; kept in `SecureStorage` as
    "twelvedata_api_key", entered under Settings > Web search since 2026-10-27): `get_stock_quote` (the price, the change
    from the previous close, the day's and the 52-week range) and `get_stock_history` ("1 week" to "10 years", or "year to
    date": daily bars up to 3 months, weekly up to 2 years, monthly beyond; the tool works out the change, the high and the
    low and lists up to 12 closes, so the model needs no arithmetic). Both take a ticker or a company name, looked up with
    `/symbol_search` (a US stock or ETF first). The key goes in the request's header, not its URL. `AgentTools.Stocks.cs`;
    `UnitTestStocks` checks the reading of Twelve Data's responses. Checked against the live service with its demo key
    (Apple's quote, its past year and this year so far).
  - Charts: a quote also fetches the day's prices every 5 minutes (one more credit) and a history plots its closes - a
    card above the reply (`StockChartView`, `Emgu.LiteRT.LM.Maui`: the last price and its change, green or red, a line with
    the previous close dashed, the times or dates below and the high and low beside it), from the skill's
    `StockChartReady` (`StockChart`, `Emgu.LiteRT.LM.Agents`, kept with the reply as a saved file of kind "stock_chart" and
    drawn again when the chat is reopened). Checked on Mac Catalyst with the demo key: Apple's day (09:30-15:55), its past
    year, and the day's chart back on reopening the chat.
  - Verified on Mac Catalyst and the iOS simulator (Wikipedia; Tavily wasn't verified - no key).

## Document questions

- "+" > Document attaches a PDF, Word (.docx) or text file to the chat (the library's `DocumentsFeature` in `Emgu.LiteRT.LM.Maui.Documents`
  since 2026-10-26, over the demo's `ChatHistoryStore` and `SearchServices`; `DocumentReader` and the saved-document search are in
  the `Emgu.LiteRT.LM.Agents.Documents` package, `PdfPageRenderer` in `Emgu.LiteRT.LM.Maui`, since 2026-10-25). It shows as a chip above the message box - indexing progress, then e.g. "5 pages · 2 pictures ·
  1 page as image". Its cross removes it, as do New chat and opening a saved chat.
- Reading:
  - `DocumentReader` extracts each PDF page's text with PdfPig 0.1.16 (`ContentOrderTextExtractor`) and its pictures
    (`page.GetImages()`: `TryGetPng`, else the raw JPEG; skipped below 100 px or 4% of the page, and image masks).
  - Pages with fewer than 80 letters or digits of text - scans, vector charts - are rendered with the platform's renderer
    (`PdfPageRenderer`: Android's `PdfRenderer` from a temporary file, PDFKit's `PdfPage.GetThumbnail` on iOS and Mac
    Catalyst, `Windows.Data.Pdf` on Windows - the Windows one is unbuilt and untested).
  - .docx: the paragraphs from `word/document.xml` and the pictures from `word/media`.
  - All images are re-encoded as JPEG of at most 1024 px with Emgu CV. Pictures and page images (at most 24 on desktops, 10
    on phones, page images first) are only indexed when the selected model takes images; only the first 150 pages are read.
- The index uses the shared search model (`SearchEmbedder`, EmbeddingGemma 2 Text Vision by default; downloaded on first use,
  kept while the page is open, released with it).
- Indexes are saved in the chat database: `ChatHistoryStore`'s `Document` (id = the file's SHA-256 in hex, name, pages, chip
  summary, `Settings`) and `DocumentItem` (each passage's text and location, or each image's bytes, media type, location and
  page flag, with its embedding as floats), plus `Conversation.DocumentId`.
  - Attaching a file whose saved index has the current settings (`IndexSettings`: embedding model file, passage size and
    overlap, image and page limits) loads it instead of indexing: 0.02 s instead of 6.2 s for the test report on an M1.
    `DocumentIndex.AddPassage(text, location, embedding)` and `AddImage(..., embedding)` rebuild a saved index without
    `Build`.
  - Each saved exchange records the chat's document, and opening a saved chat restores it from the database without the
    file (`RestoreDocumentAsync`); passages that its visible messages' contexts already contain are marked as given.
  - Deleting a chat deletes the documents no other chat uses, as does opening the page for documents attached to chats that
    were never saved.
- Each message gets a context: `ChatMessageItem.Context`, saved as `MessageRecord.Context` and given to the model before the
  message text (`ModelText`); the transcript shows only the message.
  - It is excerpts in document order with their pages, up to 1,600 characters with a 4k window or 3,200 with 8k: the whole
    text if it fits; the beginning for summary-type questions; else the best passages and any within 0.1 of the best, with
    the passages of a page the message names first.
  - Passages already given since the last compaction are left out (`_givenPassages`, reset by compaction and new chats),
    except for summary-type questions, which always get the text.
  - If sending the message compacts the conversation, the excerpts are searched again afterwards
    (`RefreshDocumentContextAsync`, `_compactions`). They were chosen before it, so a passage given earlier and then
    summarized away was left out: on a Galaxy Z Fold3, "How many people does the bakery employ?" after compaction got no
    excerpt, and the summary lacked the number.
- For image models, one picture or page image also goes with the message as an attachment:
  - the one on a page the message names; or on the page whose text has a figure caption the message names ("Figure 1"); or,
    for a question about a picture, the one on the best passage's page;
  - else the best image, if it scores at least 0.6 and no more than 0.02 below the best passage (0.52 and 0.08 for questions
    about a picture). An absolute threshold alone failed: a paper's diagrams, full of text, scored 0.62-0.76 against every
    question about it;
  - always for a document of scans only, and never for a summary-type question about a document with text (given one page's
    image with "Summarize this document", Gemma 4 E2B summarized only that page).
- A grey step line says what was used, e.g. "Looked in report.pdf: pages 2 and 5 and the image of page 3" (not counted as a
  tool step).
- Verified:
  - Mac Catalyst, Gemma 4 E2B: a generated 5-page report (text, two photos with captions, a scanned page) - 5/5 answers
    right, including the departure time read from the scanned page, and "What does Figure 1 show?"; "Attention Is All You
    Need" (15 pages) - 5/5 (the heads and d_k, BLEU 28.4, 3.5 days on 8 P100s, Figure 1, the attention formula); a .docx -
    3/3. Indexing took 4.5-14 s on the M1 once the search model was loaded.
  - Pixel Fold, Gemma 4 E2B on the GPU, 4k window: the report's first four questions were right (the scanned page rendered by
    Android's `PdfRenderer`), with indexing 318 s including the first 388 MB download, and 60-140 s per answer (each turn's
    images are encoded on the CPU, the previous message's again too). The fifth failed with LiteRT-LM's "Provided less images
    than expected in the prompt", which led to the thinking fix in [docs/litert-lm.md](../../docs/litert-lm.md). With it,
    all six questions (the five plus a sixth) were right on the phone, the chat compacting twice on the way (questions 4 and
    6): indexing 21 s with the model already downloaded, 60-140 s per answer.
  - Galaxy Z Fold3 (SM-F926W, Snapdragon 888, Android 15, 11.6 GB; Gemma 4 E2B on its GPU through the OpenCL plugin): the
    same six were right after both fixes; model load 4-11 s, indexing 28-42 s, 51-136 s per answer.

## Photo search

- Settings > "Search my photos" (off by default; since 2026-10-26 the library's `PhotoSearchFeature`, `PhotoIndex` and
  `PhotoLibrary` in `Emgu.LiteRT.LM.Maui`, over the demo's `ChatHistoryStore` and `SearchServices`; the place list is embedded in
  the library) asks for photo access:
  - Android: `READ_MEDIA_IMAGES` - a custom permission class, since MAUI's `Permissions.Photos` also asks for videos - plus
    `READ_MEDIA_VISUAL_USER_SELECTED`, and `READ_EXTERNAL_STORAGE` up to API 32.
  - iOS and Mac Catalyst: `NSPhotoLibraryUsageDescription` and PhotoKit, local images only.
  - Windows: the `picturesLibrary` capability and the Pictures folder (unbuilt and untested).
- Indexing, in the background, of the most recent photos (Settings > Search my photos > "Photos to index": 200, 1,000, 5,000 or
  All, the "photos_to_index" setting since 2026-10-27 - by default 1000 on desktops, 200 on phones, `MaxIndexedPhotos`; a
  smaller choice removes the older photos from the index; newest first; thumbnails at
  512 px from MediaStore's `LoadThumbnail`, `PHImageManager` or Emgu CV), with the shared search model:
  - At 140 vision tokens per image - EmbeddingGemma 2's default and higher-quality setting (`EmbeddingModel.EmbedImage(...,
    visionTokensPerImage)` chooses). 70 was 2.3 times faster on an M1's CPU (461 vs 1041 ms per photo) with the same 6/6
    text matches, but sees less detail. `PhotoEmbedding.VisionTokens` records it, so a change re-embeds the photos.
  - It pauses while the chat answers, and shares the model with document search through `SearchEmbedder.Gate`.
  - Embeddings are kept in `ChatHistoryStore`'s `PhotoEmbedding` table (photo id, version, date, floats), so later runs only embed
    new or edited photos (by the library's modification date: MediaStore's `date_modified`, PhotoKit's `ModificationDate`)
    and drop those no longer among the most recent. Turning the setting off deletes them.
  - A pass runs when the page opens, when it is shown again, and when the app returns to the foreground (`Window.Resumed`,
    subscribed when the page's `Window` is set - it is still null in `OnAppearing` when the page is pushed in the Shell), at
    most once a minute.
  - `find_photos` checks its results against the library (`PhotoLibrary.GetExistingAsync`; nothing if photo access was taken
    away), forgets photos deleted since the last pass, and searches again. Verified on the Pixel 8 emulator: a duplicate
    deleted in between dropped out of the results (index 26 → 25), and a photo added while the app was in the background was
    indexed on its return.
- Each photo's details are kept with it (`PhotoEmbedding`: latitude and longitude, `Place` from `PlaceLookup`, camera, size,
  kinds; `DetailsVersion` re-reads them without re-embedding):
  - Android: MediaStore's size, `relative_path` (Screenshots) and `is_favorite`; then the EXIF through AndroidX's
    `ExifInterface` (Android's own `android.media.ExifInterface` returned an empty LensModel) for GPS (which needs
    `ACCESS_MEDIA_LOCATION`, asked for separately and optional, and `MediaStore.SetRequireOriginal`), the camera, the capture
    date (MediaStore's `datetaken` was the file's date for copied photos) and selfies (a LensModel naming the front camera).
    While the locations can't be read, Settings > Search my photos shows a note and "Allow photo locations…"
    (`PhotoLibrary.RequestLocationsAsync`: the permission request, or the app's settings page when Android answers at once
    without asking). On the Pixel Fold (API 37, photo access allowed) Android granted it at once, without a dialog. Once
    the locations can be read, the next pass reads every photo's details again with its embedding kept (the
    "photo_locations_indexed" setting records what the index was built with), so the places appear without re-embedding.
  - iOS and Mac Catalyst: PhotoKit's location, size, favourite flag and subtypes (`Screenshot`, `PhotoPanorama`,
    `PhotoDepthEffect`), the Selfies smart album, and the camera from the image data's `{TIFF}` properties.
  - Windows: `StorageFile`'s image properties (untested).
  - Very wide or tall photos count as panoramas.
- Searching: Gemma 4 always gets `find_photos`, with an instruction to pass the request in the user's words.
  - With the setting off, it returns that photo search is off and where to turn it on (the settings button, or the side
    panel), so the model tells the user. Verified on Mac Catalyst with Gemma 4 E2B: "You can turn on "Search my photos" in the
    settings panel beside the chat".
  - The page parses the request with the indexed photos' places and cameras, filters by the details, then ranks by the
    description - or, without one, lists the 12 most recent matches.
  - It returns photos that stand out: `Emgu.LiteRT.LM.Chat`'s `ChatPhotos.SelectMatches` (since 2026-10-27) keeps a photo
    whose similarity is at least 0.6 and whose lift - the similarity minus its similarity to "a photo" - is at least 0.035
    and within 0.04 of the best lift, up to 8. The old rule (a similarity of 0.6, within 0.04 of the best) showed eight photos
    of walls for "show me cat photos" on the Pixel Fold, which has no cat photos: with nothing to find, ordinary photos score
    0.62-0.65 against any text. Measured with EmbeddingGemma 2 Text Vision at 140 tokens on 18 photos (the repository's test
    photos, and drawn walls, doorways and a screenshot): the right photo's lift was 0.040-0.162, the best photo's with nothing
    to find ("cat", "a car", "food", "a birthday cake", "mountains", "snow") 0.026 at most; `UnitTestPhotoMatches` keeps the
    numbers. Checked on the Fold: "show me cat photos" finds none. (Before, 0.06 below the best let three screenshots of shapes
    through for "a piece of fruit", at 0.63 against the apple's 0.687.) They show as a row of thumbnails above the reply, with date
    and town captions (tap one to see it larger, in a sheet), and the model is told how many were found and when, where and
    with what each was taken.
- Verified:
  - Pixel 8 emulator, 6 test photos among 25: each of six descriptions found its photo first (0.69-0.78, at 70 tokens);
    indexing 65 s including the model download; searches 0.4-1 s. `TestAgentTools` checks that Gemma 4 E2B calls
    `find_photos` for "Show me my photos of the beach".
  - With EXIF-tagged test photos on the emulator (Paris, Arlington, Amsterdam, Santa Cruz, Cape Canaveral, Valencia;
    iPhone, Canon and Pixel; one front-camera selfie): "photos from Paris", "people surfing in California", "photos from
    Spain in summer 2025", "selfies", "photos taken with my Pixel in 2025", "pictures from the USA", "flowers in the
    Netherlands" and "a rocket" each found exactly the right photos, and "photos from Japan" and "screenshots" none.
    Searches by details only take under 60 ms.
  - `TestPlaceLookup`, `TestPhotoRequest` (14 requests against a fixed date) and `TestAgentTools` (Gemma passes "beach
    photos from Spain last summer") cover the parts.
  - Not tried on a real phone's library yet.

## Image generation

- Settings > "Image generation" (off by default; the library's `ImageFeature` in `Emgu.LiteRT.LM.Maui.Images` since
  2026-10-27, its PNG written by `Emgu.LiteRT.Models`' `PngEncoder` instead of Emgu CV; the demo gives it its model
  selector and a Save As dialog) asks before downloading `Emgu.LiteRT.Models`'
  `ZImageTurbo` (10.6 GB) to its default folder, `LocalApplicationData/Z-Image-Turbo` (`~/Documents/Z-Image-Turbo` for the
  Catalyst app - on this Mac a link to the data volume), with progress in the settings. An interrupted download resumes on
  the next start; Storage deletes the model.
- Gemma 4 then gets `generate_image` (with `ChatImageGeneration`'s instructions). The page checks the description's length
  (`CountPromptTokens`, at most 24 tokens - if it's too long, it asks the model for a shorter one), draws 8 steps with a random
  seed on the CPU, and shows a 220-point tile above the reply (a spinner with the stage and the seconds while drawing; tap it
  to see it larger, in a sheet).
- The PNG is saved with the reply as an attachment of kind `generated` and restored above the reply when the chat is
  reopened, but never put in the message's `Attachments`, so the model, the token estimate and summaries don't see it.
- Like `find_photos`, the tool is offered with the setting off, and then tells the model where to turn it on (or that the
  model is still downloading).
- The tool's first result text ("...shown to the user above your reply. You can't see it: say briefly what you drew...") made
  Gemma 4 E2B end its turn without any answer, twice in a row. Asking for "one short sentence that says what you drew, e.g.
  "Here is ..."" fixed it.
- The enlarged picture (`ShowGeneratedImageLarge`, TODO item 17) shows its description and seed - kept in the PNG as iTXt
  chunks (`Emgu.LiteRT.Models`' `PngMetadata`), so saved chats and files have them - with "Draw it again" (which sends "Draw
  this again, using exactly this description: ..."), "Add to Photos" (the library's `PictureGallery` since 2026-10-26: MediaStore on Android, add-only
  PhotoKit with `NSPhotoLibraryAddUsageDescription` on iOS and Mac Catalyst, the Pictures folder on Windows), "Share..." and
  "Save image...".
- iOS: `Emgu.LiteRT` uses `LiteRt.framework` there, and the model folder (the app's Documents; `LocalApplicationData` on
  iOS) is excluded from iCloud backups (`NSUrl.IsExcludedFromBackupKey`).
  - Verified in an iPhone 18 Pro simulator (iOS 27) with the models linked in from the Mac: two 8-step pictures, and Gemma 4
    E2B drawing through the chat, at ~205-225 s per picture there - about 10 times slower than the Mac's CPU with the weight
    caches in use, probably XNNPACK picking slower kernels in the simulator. Not tried on an iPhone (memory: the text encoder
    maps 3.5 GB).
  - In Mono's interpreter, which iOS Debug builds use for hot reload, the second picture crashed: an
    `ArrayTypeMismatchException` (`RunGraph`'s returned `float[]` came back as the `finally` block's `TensorBuffer`) and,
    with that worked around, a SIGSEGV in `mono_interp_exec_method`. Tiering wasn't the cause (`--interp=-tiering` changed
    nothing). Ahead-of-time compiled code drew picture after picture, so the demo sets `UseInterpreter=false` for iOS (Debug
    builds compile ahead of time too; no hot reload).
  - That needed the demo's `System.Drawing.Common` exclusion (Emgu.CV.Models' netstandard2.0 dependency, whose
    `System.Private.Windows.Core.dll` crashes Mono's AOT compiler with exit code 139) on iOS as well as Android; iOS Release
    builds had been failing that way.
- Verified on Mac Catalyst with Gemma 4 E2B (temporary self-test): "Draw a red fox sitting in fresh snow, in watercolor
  style." and "Can you paint a lighthouse at sunset?" were drawn and answered ("Here is a lighthouse at sunset, dramatic
  lighting, painterly style."), saved, and shown again on reopening. Not yet tried on Android or Windows.

## Photo effects

- "Blur the background of this photo" (since 2026-10-28; `Emgu.LiteRT.LM.Maui.CV`'s `PhotoEffectsFeature`, the router's
  `photo_effects` group - "blur", "bokeh", "portrait mode"...): the model calls `blur_photo_background` (an image reference
  and "light", "medium" or "strong"); the feature takes the message's photo, or the latest one in the chat for a follow-up
  ("make the blur stronger"), estimates its depth with Depth Anything V2 Small (`DepthEstimator`, a 27.7 MB download on
  first use into the app's cache folder, GPU at FP16 when it passes its check) and blurs what is behind the subject
  (`DepthEffects.BlurBackground`). The photo is shown above the reply (tap: large, with "Save photo..." - the demo's Save As
  dialog, as for snapshots) and kept with the reply (saved file kind `edited_photo`, not given to the model).
- Verified on Mac Catalyst with Gemma 4 12B (GPU): the yoga photo blurred in one call, then "Make the blur stronger" reused
  it with "strong"; both photos saved with their replies and shown again when the chat is reopened. The first call took
  ~85 s with the model's thinking and the depth model's download.
- On the Pixel Fold with Gemma 4 E2B (GPU), the surfers sample: the depth model passed its GPU check (OpenCL, FP16), and
  each blur took 0.5 s ("Background blurred in 0.5 s (GPU)" under the photo); the follow-up "Make the blur stronger" worked
  too. E2B answers with the tool's result itself - word for word with a longer result that had an example reply - so
  `AgentTools.PhotoBackgroundBlurredResult` starts with the sentence the user should read ("Here is your photo with its
  background blurred (medium blur) - I can make the blur lighter or stronger.").

## Your own server

- Settings > "Your own server" (the library's `ServerFeature`, in `Emgu.LiteRT.LM.Maui.OpenAI` since phase C, 2026-10-26; the
  its own remote option, id "Server", is last in the model list) takes an OpenAI-compatible server's address ("host:port" gets
  `http://` and `/v1`), an optional API key (kept in `SecureStorage`) and one of its models (`GET /models`). LM Studio's
  `/api/v0/models` adds the model type - embeddings are left out, `vlm` takes images - and a loaded model's context length;
  otherwise 4096 is assumed until the first reply loads it. The feature is the option's `IRemoteChatModel`
  (`ChatModelOption.Remote`): the library page connects through it before the first message, makes the client, drops the
  reasoning option it rejected, writes the summaries and answers `AskModelOnceAsync` - the demo's page overrides nothing for it.
- "Your server" in the model list (not one of the catalogue's models: it's index `Models.Length`, and saved chats record
  `Server`) chats through the same conversation, tools and compaction as Gemma 4, over `Microsoft.Extensions.AI.OpenAI`'s
  `IChatClient`. Its version 10.10.1 turns the servers' `reasoning_content` into `TextReasoningContent`, so thinking shows as
  it does for local models.
- The Think chip is sent as `reasoning_effort` (medium or none; LM Studio honours both, `none` giving 0 reasoning tokens). A
  server whose 400 response names it gets the message again without it, and no more for that address
  (`ServerRejectedReasoning`, through `IRemoteChatModel.ShouldResend` - the page's `ShouldResend`, asked by `ChatView.DefaultSendAsync`'s
  `ResendRequested` and the demo's send loop); the message is sent again into the same reply (`ChatConversation.RegenerateAsync`), so nothing
  is duplicated. A model that doesn't take images (`llm` in LM Studio's list) has the attach button and the pending images
  dropped (`IChatPageHost.ModelCapabilitiesChanged`). Checked on Catalyst with a mock server (the scratchpad's
  `mock_openai_server.py`: the model list, a 400 for `reasoning_effort`, streamed replies): connect in the settings, the row
  in the model list, a reply after the resend, a second message without the option, the chat saved under "Your own server".
- Token counts are estimated (`ApproximateTokens`: ~3.5 ASCII characters a token). Summaries are written by the server's
  model without thinking, and allowed `SummaryMaxTokens` + 1600 tokens in case it thinks anyway: LM Studio runs Gemma 4 with
  thinking on by default, which used up 400 tokens before any summary came out.
- Voice messages and the voice chat are sent as text, written down by the platform's speech recognizer (`SpeechRecognizer`,
  which was the demo's `SpeechToText.cs`; also used for the Qwen models, which don't take audio):
  - Android 13+: `SpeechRecognizer` reads the recording from a pipe (`EXTRA_AUDIO_SOURCE`), fed at recording speed, then
    0.6 s of silence and `StopListening`, falling back to the last partial results, with one recognizer per recording -
    Google's on-device recognizer dropped words when given the audio at once, didn't end at the pipe's end, and passed a
    reused recognizer's late results on. Verified on a Pixel Fold with a voice message to LM Studio.
  - iOS and Mac Catalyst: Apple's Speech framework (built, not yet run; it asks for permission on first use). Not on Windows.
- Verified on Mac Catalyst (temporary self-test) against a mock server and LM Studio on a Windows PC
  (`google/gemma-4-26b-a4b-qat`, 16k context read from `/api/v0/models`): a tool call (`get_current_date_time`), the
  conversation carried on, an image (the tulips described), compaction and the summary used afterwards, thinking shown with
  Think on and none with it off, and (mock) a wrong address and a server rejecting `reasoning_effort`; 5-60 s per answer.
- Also works on a Pixel Fold, which needed `android:usesCleartextTraffic="true"` in the Android manifest: such servers are
  plain `http://` at an address the user types, which Android blocks by default (on iOS and Mac Catalyst, ATS exempts IP
  addresses and unqualified host names).
- Requests to the server go through `ServerRetryHandler`, which sends a request that failed without a response once more
  after the app is back in front (`Window.Stopped`/`Resumed`). On a Galaxy S10 the connection ended ("unexpected end of
  stream") when Google Maps, opened by `open_directions`, came to the front, and the reply was lost. (The retry itself hasn't
  been seen to run.)

## Device actions

- The agent gets `AgentTools.DeviceActions` - timers, alarms, calendar events, reminders and directions (the library's
  `DeviceActionsFeature` and `DeviceActions` in `Emgu.LiteRT.LM.Maui` since phase C, 2026-10-26, with the group's constants
  in `Emgu.LiteRT.LM.Chat`'s `ChatDeviceActions`; TODO item 24) - except in the voice chat. Each call shows a card above the
  reply with Confirm and Cancel.
- A cancelled action returns `DeviceActionCancelled`, which starts "CANCELLED - NOT done": with a gentler text, Gemma 4 E2B
  told the user the alarm was set.
- Android does timers and alarms through `AlarmClock` intents (`EXTRA_SKIP_UI`; alarms within 24 hours), events through the
  calendar's insert intent (the user saves there), and directions through a Google Maps URL. iOS and Mac Catalyst save
  events and reminders with EventKit and open Apple Maps (built, not run). Windows only does directions.
- Verified on a Galaxy S10 (Android 12) with Gemma 4 E2B (a timer) and LM Studio's Gemma 4 26B-A4B with thinking on (an
  alarm cancelled, a calendar event, directions on foot, by car and by transit).

## The clipboard

- `read_clipboard` (`AgentTools.ReadClipboard`, TODO item 26) gives the model the text the user last copied, wrapped as their
  data, cut to 3,000 characters, and ending with an instruction to act on it - without that, Gemma 4 E2B repeated the text
  for "Summarize".
- It isn't a tool group, so there is no stub the model could call by itself: the library's `ClipboardFeature` adds it only
  to a message for which `AgentTools.MentionsClipboard` is true ("clipboard", "what I just copied", "the text I copied"; not a
  plain "paste" or "copy"), with the step line "Reading the clipboard". Verified on Mac Catalyst with Gemma 4 E2B;
  `UnitTestClipboardTool`.

## Languages, translation and voice transcripts

- `Emgu.LiteRT.LM.Maui`'s `TextLanguage` (TODO item 39) gives the language of a text: the script check first
  (`ReplySpeechText.DetectScriptLanguage`), then for Latin script `TextLanguage.IdentifyLatin` - the demo sets it on Android to ML
  Kit's language identification (`Services/MlKitLanguage.cs`; ML Kit stays out of the library because of the pin below), with its bundled model:
  26 of 26 test phrases right on a Galaxy S10, 2-3 ms each, short ones included) and Apple's `NLLanguageRecognizer` on iOS and
  Mac Catalyst. ML Kit's Common library needs `Xamarin.AndroidX.Fragment.Ktx` 1.9.0 pinned in the demo for Android; otherwise
  D8 fails with `FragmentKt` defined twice.
- `TextToSpeechReplySpeaker` uses it to pick the reading voice, and each saved user message keeps it
  (`ChatMessageItem.Language`, the base store's `ChatMessageRecord.Language`).
- Gemma 4 thinks in English whatever the language, so the opened thinking of a reply to a non-English message offers
  "Translate to <language>" (the library's `TranslationFeature`, `Emgu.LiteRT.LM.Maui/Features/`, since 2026-10-27; the
  demo's before). The row is the reply's `ThinkingAccessory`, which the thinking view shows under its header; the feature
  detects the message's language when the reply is done and saves it with `IChatPageHost.SaveMessageChangesAsync`. The
  model translates the thinking on request, in conversations of its own like a summary (the agent's conversation is
  released and re-read with the next message; `LanguageText.SplitForTranslation` cuts a long one into pieces). The
  translation is the reply's `ThinkingTranslation`, shown instead of the English through `ThinkingDisplayText`, saved with
  the reply (`ChatMessageRecord.ThinkingTranslation`, the same column the demo used, so older chats keep theirs) and shown
  again after reopening. Verified on Mac Catalyst with Spanish, German and Chinese; after the move, again with Spanish
  (Gemma 4 E2B on the GPU: translated, the chat reopened with the translation and the row, the row back to the English).
- A voice message that a model hears as audio (Gemma 4) also gets its words written down by the platform's speech recognizer
  in the background (`SpeechRecognizer`; the base `ChatPage` keeps them for a page that saves its chats itself, as the
  demo's does - the demo's own `TranscriptFeature` until 2026-10-27). The words show under the chip, are kept in
  `ChatMessageRecord.Transcript` (not given to the model), become the title of a chat still called "Voice message", and go into
  exports; the recording is kept too (`chats/attachments/...`). Verified on a Pixel Fold.
- Settings > "Voice language" (the library's `VoiceLanguageFeature` since phase C, 2026-10-27; `VoiceLanguages`, set as
  `SpeechRecognizer.Language`; the errors end with `SpeechRecognizer.ChooseLanguageHint`) is the language the speech
  recognizer listens in. It's the device's by default, which
  gave nothing for Chinese speech on an English phone. On Android 13+ it asks the recognizer what it supports
  (`CheckRecognitionSupport`; the on-device recognizer lists only installed languages, Mandarin as `cmn-Hans-CN`), requests the
  language pack, and falls back to the normal recognizer meanwhile. Verified on a Pixel Fold with Chinese and Japanese
  recordings.

## Task mode

- The Plan chip (TODO item 23; the library's `TaskFeature` in `Emgu.LiteRT.LM.Maui` since phase C, 2026-10-26 - the page runs
  the message through the client of its `CreateMessageClient`, a `ChatFeature` hook; `Emgu.LiteRT.LM.Agents`' `TaskPlan`) has the model write a short numbered
  plan (`TaskPlan.PlanPrompt`/`Parse`), shown as a checklist card the user can edit, skip steps of, and stop.
- The card is kept with the reply when it closes (a saved file of kind `task_plan`, JSON: the steps, their states and
  results, how it ended) and comes back, closed, when the chat is reopened (2026-10-27; checked on Catalyst).
- Each step then runs as a request of its own (`TaskModeClient`, the `IChatClient` of that one message), given only the task,
  the plan and the earlier steps' short results (`StepPrompt`). Its tool calls show as step lines (its own bubble stays
  hidden), and the final answer is written from the results into the reply (`FinalPrompt`).
- The saved chat holds the request, the step lines and the answer; the reply is given to the model as text afterwards (a
  reply written by a per-message client).
- Verified on Mac Catalyst with Gemma 4 E2B: a Paris weekend (weather, Wikipedia, a web page and a packing-list document in
  ~2.5 minutes), including skip, stop and a follow-up.
- Attachments (since 2026-10-28): the planner sees them, and with them a page of numbered questions is planned as a step
  per question (`PlanPrompt(..., attachments: true)`); every step and the final answer get them again
  (`TaskPlan.WithAttachments`; research still gives them to its first part only). Before, only the first step had them:
  with a photo of a five-question geometry worksheet (cylinders, prisms, paper tubes), Gemma 4 12B on the Mac's GPU planned
  by process ("extract the text", "measure", "calculate"...), the later steps worked from the earlier ones' 700-character
  results, and the answer had questions 10 and 11, half of 12 and called 13 and 14 "not found". After: the plan was "Solve
  question 10" ... "Solve question 14", and all five were answered correctly (24 minutes).

## Research mode

- The "🔎 Research" chip (since 2026-10-28; `TaskFeature`, the same step runner and card as task mode, `TaskPlan` with
  `research: true`). It shows for a model with tools when a Tavily key is set (the web feature registers itself as a
  service now, with `HasSearchKey` and `TurnOn`); turning it on turns web search on and the Plan chip off. A feature can
  now declare several chips (`ChatFeature.BuildComposerChips`), re-checked with each `RefreshChips` - a chip view of its
  own was shown or hidden only once, before the key had loaded.
- The question becomes 1 to 6 sub-questions (`ResearchPlanPrompt`: facts to find, never how or where to find them; 1 or 2
  for a simple question, one per stock for prices), in a "Research" card with an estimate (up to 5 Tavily credits a part,
  following the skipped parts) and "Start research", Edit, "Just answer".
- A part uses the tools that answer directly before the web (`StepPrompt` with `TaskPlan.ToolNames`): the stock tools for
  prices (yesterday's close from `get_stock_history` over "1 week"), `get_weather`, `convert_currency`; they are listed as
  sources (`ToolSource`: Twelve Data, Open-Meteo, Frankfurter). Before this the plan for "Can you show me yesterday's intel
  stock price" on the Mac was "how to find" questions answered from web searches. Twelve Data's name search then gave the
  OTC penny stock "AI Maverick Intel" for "Intel" ($0.13) - it lists no INTC for the bare name - and today's price so far
  as yesterday's close; the tools now ask for tickers, take only listings named like the company (NASDAQ/NYSE first,
  searching again with " Inc"/" Corp"), and a daily history says which close is today's so far (2026-10-28: $93.74,
  INTC's close of 27 October). A part researches only its own question ("Research only this part", when more than one runs): asked to
  compare Intel's and AMD's closes, Gemma 4 E2B on the Fold fetched AMD's week in Intel's part too (seen with a trace of the
  parts' tool calls, 1 run in 1), then again in AMD's own, and the reply showed two identical AMD charts; with the line, 3
  runs of 3 made one call a part. The web feature also shows a chart only once per reply (by its data).
- Each part is a request of its own (`StepPrompt`): search, read the best page with `read_web_page` and `lookFor`, at most
  2 of each by the prompt and 6 tool calls in all (`MaxResearchToolCalls`: the part is stopped beyond that), then up to 8
  sentences of notes (1,200 characters kept, `ResearchNotesLength`), "not found" for gaps. Its sources are the pages it
  read - titled from the search results - or the search's top two (`SearchResultPages`, `AddSources`: one number per
  address across the parts).
- The report is written without tools from the notes alone (`FinalPrompt`: each part's notes with its sources' numbers),
  citing [n]; the sources follow it as Markdown links with the pages' real addresses (`SourcesMarkdown`, not the model's).
  Stopped, the notes so far and the sources are the reply. The card is saved with the reply and comes back as "Research".
  Without a search tool in the message's options, it says how to turn web search on rather than answer from memory.
- Research goes on in the background (`BackgroundWork` in `Emgu.LiteRT.LM.Maui`): on Android a foreground service of type
  data sync (`WorkForegroundService`) with a partial wake lock and a notification - "Researching", the part running ("1 of
  2: ..."), "Writing the report...", a Stop button - and, when it ends while the app isn't on the screen, "The research is
  done: tap to read the report."; on Mac Catalyst a user-initiated `NSProcessInfo` activity (no App Nap); on iOS only the
  short background time iOS gives. Without it, on the first run on the Pixel Fold the phone locked after a few minutes,
  Android froze the app, and at 04:33 closed it ("Async binder space running out while frozen"); the exchange is saved only
  when the reply is complete, so the research was lost (the screen was then kept on while research ran, until the
  service). Checked on the Fold (2026-10-28, Gemma 4 E2B on the GPU, "Compare the closing prices of Intel and AMD
  yesterday", 2 parts): sent to the background with Home right after Start research, the process stayed up and the
  notification moved through both parts and the report in 5 minutes - OpenCL kept working in the background - and the
  complete report (both closes of 27 October, in a table) was there on return. The progress counts only the parts that
  run ("Researching 2 of 4", not "of 6" with two skipped).
- A sources section the model writes itself despite the prompt ("Sources:" and bare "[1]" lines, by Gemma 4 E2B) is dropped
  from the report as it streams (`TaskPlan.IsSourcesHeading`; lines are held until they end), so only the real list follows;
  so is a line that only cites them ("Sources: [1], [2]", `IsCitationsLine`), the report going on after it.
- Checked on the Pixel Fold (2026-10-28, Gemma 4 E2B on the GPU, "Compare the iPhone 16e and the iPhone 17e side by side in a
  table", 4 parts): 12 minutes, the screen held on throughout; 4 searches and a page read; a report with a summary and a
  9-row table citing [1], [2] and [6] ("not found" for the 17e's price), and 6 linked sources (CNET, Wikipedia, MacRumors...).
  One cell was wrong: the 16e's charging as "7.5W MagSafe" (it has Qi at 7.5 W, no MagSafe). A part's notes can carry the
  model's own planning ("3. **Synthesize the...") into the card. On the Mac (Catalyst, signed by `tools/run_catalyst` so the
  keys stay), the Intel question above.

## Goals

- The goals feature is the library's since 2026-10-26 (`Emgu.LiteRT.LM.Maui`'s `GoalsFeature` over `GoalsSkill`; the demo gives it a `SqliteAgentGoalStore` over its chat store, and `ExerciseFeature` adds the exercise counter to its group as the skill's extra tools, words and summary). Goals have streaks and a history (`GoalHistory.cs` in `Emgu.LiteRT.LM.Agents`, TODO item 25): per
  period (day, week from Monday, month) what was logged and whether the target was reached, the streak of periods in a row
  (the current period never breaks it), and the best streak.
- Settings > Your goals shows a strip of the latest 12 periods and the streak, with Edit, Undo last (`IAgentGoalStore.DeleteLastProgressAsync`) and Delete, and `GoalStatusAsync` (so `get_goals` and the
  message instructions) adds "2-week-streak" from two periods in a row.

## Tool servers (MCP)

- Settings > "Tool servers (MCP)" (the library's `McpFeature` in `Emgu.LiteRT.LM.Maui.Mcp` since 2026-10-26; TODO item 28) adds Model Context Protocol servers: an address
  (Streamable HTTP or SSE) with an optional authorization header kept in `SecureStorage`, or, on Mac Catalyst and Windows, a
  program started with stdio. It uses the official `ModelContextProtocol.Core` 2.2.0 client (referenced by the demo and the
  test project, not by `Emgu.LiteRT.LM.Agents`).
- `McpToolServer` (in the `Emgu.LiteRT.LM.Agents.Mcp` package, moved out of `Maui.Demo.Lite.Core` on 2026-10-25) turns a server's tools into functions named `<server>_<tool>`:
  - Each connected server is a `ToolGroup` (`mcp_<server>`): its tools come when a message mentions the server or its tools'
    subject, else through the `use_mcp_<server>` stub. Tools the user switched off cost nothing.
  - A tool without the `readOnlyHint` annotation asks with the device actions' Confirm / Cancel card, unless the user trusts
    the server (DeepWiki annotates none).
  - Results are wrapped as the server's data - not instructions - and cut to 2,500 characters. Step lines read "Calling
    <tool> on <server> (...)".
- `UnitTestMcp` runs a server in the test process over pipes (`ConnectsToAPublicServerOverHttp` is `TEST_MODELS`-gated and
  uses DeepWiki). Verified on a Galaxy S10 with Gemma 4 E2B on the GPU calling DeepWiki's `read_wiki_contents`; stdio is
  untested.
- Apple Intelligence (Microsoft.Maui.Essentials.AI, an `IChatClient` too) only ships for .NET 11, so it's left for when the
  repo moves to .NET 11.

## Camera and sound monitors

- Since 2026-10-26 the camera cluster is the library's: `CameraFeature` (its partials below), `ExerciseFeature`, the camera actions,
  `AlertAudioReplySpeaker`, `MonitoringForegroundService`, `PoseDrawing` and the alert sounds live in the `Emgu.LiteRT.LM.Maui.CV`
  package (Emgu CV; the demo gives it its `DetectorFactory` pictures as a `VisualDetectors` and a Save As dialog for snapshots),
  the pure parts (`VisualHostCatalog`, the snapshot and play_file actions, the `ChatCamera` group constants) in
  `Emgu.LiteRT.LM.Agents.CV`. The file names below are the package's.
- Sound monitors (TODO item 42): `Emgu.LiteRT.LM.Agents`' `Audio/SoundMonitors.cs` (`SoundGroups`: 30 sounds as groups of
  YAMNet classes with the words people use; `SoundPresenceMonitor`, `SoundMonitorRuntime`, `SoundMonitorTools` -
  `create_sound_monitor` and the rest) and `CameraFeature.Sound.cs`: a `sound_monitor` tool group, the
  microphone on while a monitor is enabled, a monitor card, and chime or spoken alerts that the page doesn't listen to;
  `MicrophoneGate` gives `VoiceRecorder` the microphone first. Not on Windows. Verified with Gemma 4 E2B on Mac Catalyst and
  with a Galaxy S10's microphone (`Emgu.LiteRT.Models/YAMNET.md`).
- On Android a sound monitor can also take a full-resolution photo and record a clip (TODO item 49; `SoundAlert.ClipSeconds`
  and `TakePhoto`, `create_sound_monitor`'s `clipSeconds` and `takePhoto` when `SoundMonitorTools.Create` gets
  `cameraActions`): the live camera opens when the monitor is created, the photo comes first, one capture per 30 s plus the
  clip, through the same `TakePhotoAsync`/`RecordClipAsync` as the camera monitors.
- `take_photo` also plays a short shutter click (`camera_click.wav`, `CameraChimeAction("camera_click.wav")`, inside the sound
  mute) unless Settings > "Camera sounds" is off (TODO item 50); clips are silent.
- On Android the live camera runs through Camera2 (`Emgu.LiteRT.Models.CV.Maui/LiveCamera.Camera2.cs`, moved out of the demo on 2026-10-25; OpenCV's camera is the fallback). A 640x480 YUV
  reader feeds the preview and the detectors.
  - The `record_clip` visual-alert action (TODO item 43) adds a `MediaRecorder` surface by configuring the session again for
    the length of a clip: 720p H.264 MP4, with sound when the microphone is already allowed, 3-30 s. Frames keep coming at 30
    fps during a clip and pause ~0.3 s at each end.
  - The clip's card in the transcript (`CameraFeature.Clips.cs`) plays it in place - Play/Pause with a progress bar,
    through Android's `VideoView` in `Emgu.LiteRT.LM.Maui`'s `InlineVideoView` (no extra packages), one clip at a time, the sound monitors not
    listening meanwhile - with Full screen (the system player), Share and Save to gallery.
  - The `take_photo` action (TODO item 44) takes a full-resolution still - the camera's largest JPEG, 12 MP on a Galaxy S10
    and a Pixel Fold - in the same session-reconfiguring way (`LiveCamera.TakePhotoAsync`; a card in
    `CameraFeature.Photos.cs` with Open, Share and Save to gallery), while `take_snapshot` stays a small analysis
    frame. A photo asked for together with a clip is taken first.
- The `hand-gesture` visual agent (`HandVisualAgent`, in `Emgu.LiteRT.LM.Agents.CV`, TODO item 47) makes MediaPipe's seven gestures object classes of the
  camera monitors ("thumb up", "victory"...; `VisualClasses.CanonicalGesture` maps the words people use, and the visual
  instructions say which label fits which words), so "take a picture when you see a thumb up" is an ordinary monitor with
  every action. Verified on a Galaxy S10 with Gemma 4 E2B, with MediaPipe's photos as frames.
  - With a real hand on a Pixel Fold's selfie camera, the gestures were found in 94-100% of frames - except the closed fist
    and "I love you" (pose-sensitive "none") - after three changes: the palm threshold went from 0.5 to 0.35; `HandTracker.TiltAngles`
    asks the classifier again on landmarks turned by ±30 and ±60 degrees when the upright reading is weak (a tilted thumb up
    went from 5% to 92%; not MediaPipe's behaviour, so the parity tests turn it off); and gesture monitors default to a 0.4
    confidence.
- The `holistic` visual agent (`HolisticVisualAgent`, in `Emgu.LiteRT.LM.Agents.CV`, MediaPipe's holistic landmarker) watches for two things at once - a posture with an
  expression, "hand raised and smiling", "waving and smiling" - and for covering the eyes (peekaboo), a hand over the mouth or a hand on the face.
  `VisualClasses.HolisticForMonitor` reads the request when the label Gemma 4 E2B gives is half of a combination or a mistake (it gave `person`,
  `baby`, `hand_gesture`, `face-expression` for four of five such requests); checked with Gemma on an M1 and on synthetic pictures, not yet with a
  real person or on a phone (`Emgu.LiteRT.Models/HOLISTIC.md`). Its frames, and the Holistic page's live camera, go through MediaPipe's video
  mode (tracked and smoothed from frame to frame by their timestamps): steadier hands and a covered-eyes video with 1 stray "hand over mouth"
  instead of 13. As in MediaPipe, a wrong guess for a hidden wrist can stick while the body is tracked, so a raised or waving wrist counts only
  with its hand found: on a video with one hand up, "arms raised" in 0 of 90 frames instead of 29, waving in 0 instead of 15.
- Alert sounds (TODO item 53): `AlertSoundLibrary` (`Emgu.LiteRT.LM.Agents.Audio`, with `AlertAudioCoordinator`, which keeps
  alert sounds and spoken replies from talking over each other) holds two synthesized built-in sounds - white noise and a
  music-box lullaby (`tools/make_alert_sounds.py`) - and the user's own, added from a file or recorded in Settings > "Alert
  sounds".
  - The `play_file` visual action and `create_sound_monitor`'s `playFile`/`playSeconds` play one, once or repeated for up to
    300 s, through `Emgu.LiteRT.LM.Maui`'s `AudioFilePlayer`, inside the sound mute. Names are matched loosely (`Find`), and a wrong one is refused
    with the list.
  - With Gemma 4 E2B, the sound group's keywords and its stub summary had to say it's for sounds heard; otherwise "play white
    noise when you see a person" went to the sound tool.
- Background monitoring (TODO item 54): on Android, `MonitoringForegroundService` (the library's since 2026-10-26; microphone, plus the camera when a sound
  monitor photographs or records) keeps sound monitors listening with the app in the background or the screen off - an
  ongoing notification with Stop, a partial wake lock, and a notification per alert. `CameraFeature.Background.cs` starts
  it from the foreground (Android 14 refuses to start such services from the background). The camera may then be opened for
  an alert (`CameraUsable`) and is closed again afterwards. Camera monitors don't run in the background yet. Verified on a
  Galaxy S10 (Home, and screen off). Meeting recordings use the same service.
- The selfie camera (TODO item 48; `LiveCamera.Front`, `FindCameraId`): a request that names a camera ("use the selfie
  camera", `VisualHostCatalog.CameraFacingRequest`) or the preview's camera button switches camera while the monitors go on.
  The button is labelled with the camera it switches to, "Selfie camera" or "Back camera" (it was "Flip", which read as
  mirroring). A front camera's rotation is (sensor + display) % 360; its preview is mirrored, but its photos, clips and
  detector frames aren't. New chat goes back to the back camera.
- Each camera and sound monitor's card shows the user's request that created it (`CameraFeature.MonitorInfo.cs`, TODO item
  45: kept in memory, not in private chats; tap to expand), and the camera preview's label says what the camera is doing
  ("Watching · 2 monitors", "● Recording clip · 7 s left").

## Exercise counting

- `count_exercise` (`Emgu.LiteRT.LM.Agents`' `ExerciseTools.cs`, part of the goals tool group; the library's `ExerciseFeature` in `Emgu.LiteRT.LM.Maui.CV` since 2026-10-26)
  counts jumping jacks, squats or high knees in front of the camera. The call lasts as long as the exercise; a card shows the
  count, the time and Done, and the feature runs its own pose detector on every frame it keeps up with. The count is logged
  toward a goal that names the exercise (`AgentTools.LogExerciseAsync`) and returned to the model.
- The counters (`Emgu.LiteRT.Models`, `IExerciseCounter`):
  - `JumpingJackCounter`: from closed (wrists below the shoulders, feet together) to open (wrists 0.4 shoulder widths above
    them, feet 1.2 shoulder widths apart) and back. Feet only count when both ankles are visible.
  - `SquatCounter` (`ExerciseCounters.cs`): the hip's height above the ankle compared with the knee's.
  - `HighKneesCounter` (`ExerciseCounters.cs`): each knee near its hip's height, in front of it.
- `GoalParser` reads two-word exercises ("30 jumping jacks a day") as the unit.
- The count is also said aloud with the device's text-to-speech: "Go!" when the body is first in view, each number (skipping
  to the latest when behind), and "Done. N jumping jacks." The sound monitors' mute is held meanwhile. The card's speaker
  button turns it off (on by default).
- Settings > "Developer mode" (the library's switch, `IChatPageHost.DeveloperMode`; off by default) draws the counted body on the camera preview
  with the "Body pose" page's skeleton (`PoseDrawing.cs`) and shows the position read on the card. The preview's Maximize
  button makes it fill the conversation's area (Restore, or closing the camera, makes it small again), and while counting a
  maximized preview shows the count over the picture.
- Tested by `UnitTestJumpingJacks` on synthetic pictures (see `POSE.md`); not yet with a real person or Gemma 4.

## Meeting notes

- `record_meeting` (in the `write_document` tool group, so it adds no stub; `Emgu.LiteRT.LM.Agents`' `MeetingNotes.cs`; since
  2026-10-26 the library's `MeetingFeature` and `WrittenDocumentsFeature` in `Emgu.LiteRT.LM.Maui`, which the demo wires to its
  camera feature's foreground service and its document attaching) works on Android 13+, iOS and Mac Catalyst - wherever `SpeechRecognizer` works. The call lasts
  as long as the recording.
- Recording: `Emgu.LiteRT.LM.Maui`'s `MicrophoneSoundSource` (through `MicrophoneGate`, so the sound monitors pause) feeds `MeetingAudioChunker`, which
  cuts 30-50 s pieces at the quietest 200 ms. Each piece is written down by the platform's speech recognizer while the
  recording goes on. The card shows the end of the transcript, Pause, and Stop and write notes. On Android the monitoring
  foreground service keeps it going with the screen off; on iOS the screen is kept on.
- After Stop, the model writes the notes with `AskModelOnceAsync` (`MeetingNotes.WriteAsync`): from the whole transcript if it
  fits, else from section summaries - combined again if still too long - with Summary, Key points, Decisions, Action items and
  Open questions. They show in the card.
- The notes and the timed transcript are saved as a Word document with the reply, the transcript is attached as the chat's
  document for later questions, and the notes go back to the model.
- What writes it down (since 2026-10-28; Settings > "Meeting transcripts", `Emgu.LiteRT.LM.Agents`' `MeetingTranscription`):
  Automatic (the default) is the chat model where it takes audio on a desktop and the speech recognizer on a phone; the
  model where there is no recognizer (Android before 13, or none installed); or "Recognizer, then chat model" - the
  recognizer while recording, and the model again over every piece after Stop (their audio kept in temporary files until
  then). For the model, pieces are 15-28 s (Gemma's audio clips are at most 30 s), each written down in a request of its
  own with `MeetingTranscription.Prompt`; pieces without speech (`MeetingAudioChunk.HasSpeech`: loud parts above -40 dBFS
  and 4x the quiet ones) aren't sent - Gemma 4 E2B answered room noise with "I'm sorry, but I cannot transcribe the speech
  in the recording", which `MeetingTranscription.Clean` now also drops.
  - Measured on the synthetic meeting's 10 turns (158 words, clean and with room noise at ~15 dB SNR, a temporary self-test
    in the demo): Gemma 4 E2B 0% and 2.5% of words wrong on both devices; Apple's recognizer 0.6%/5.7% (Mac), Google's
    3.2%/4.4% (Pixel Fold). Speed: Gemma 5-6x real time on the M1's GPU but 0.3x on the Fold's (8 s, then 22 s per 6 s
    clip as it warmed up); the recognizers ~20x (Mac) and ~0.9x (Fold).
  - Through the Mac's speakers and microphone with Gemma 4 E2B: 152-153 words written down live (Apple's recognizer:
    67-89), nearly every word right ("data" for "date", "dude" for "did").
  - Windows has no microphone source for meetings yet (`MicrophoneSoundSource`), so meetings stay Android, iOS and Mac.
    "Recognizer, then chat model" isn't yet tried on a phone.
- Skipping ahead (since 2026-10-29): on a phone the live transcript fell behind the conversation (Google's recognizer
  ~0.9x real time on the Fold, plus each piece's voiceprint; Gemma 0.3x). Once more than a minute of pieces waits
  (`MeetingTranscription.PiecesToSkip`, `MaxLiveDelaySeconds`), the oldest are set aside as temporary WAVs and the live
  transcript goes on with the newest; after Stop the skipped pieces are written down by what writes the meeting down
  (the recognizer, or the model for "Chat model" and "Recognizer, then chat model"; `MeetingAudioChunk.FromWav`) and go
  back in their place, so the notes cover the whole meeting. The card says "Skipped 0:05 to keep up - written down after
  Stop", then "Writing down the parts skipped while recording: n of m…"; a stopped reply leaves them out and says so.
  Pixel Fold, the synthetic meeting played twice from a Mac (2:38): with "Chat model" it skipped 3 pieces (7 waiting),
  wrote them down in ~15 s after Stop, and the transcript had 323 words for the 316 spoken, 3 speakers; with the
  recognizer it kept up (nothing waiting at 2:04) and didn't skip. The same "Chat model" run showed Gemma echoing its
  prompt into short pieces through the phone's microphone ("...how many installs we can transcribe the speech in this
  recording exactly, word for word..."); `MeetingTranscription.Clean` now cuts those sentences out (a second run: none).
  Speaker counts through the phone's microphone varied for the same 3 voices (3, 4 and 5 in three runs).
- In the background (since 2026-10-29): what comes after Stop - the last and skipped pieces, the recording's encoding,
  the notes and the model's reply - runs as `BackgroundWork` ("Writing the meeting notes"), begun at Stop while the
  microphone's foreground service still runs and ended with the reply (`OnReplyCompleted`, when the chat is saved). On
  Android its notification shows the progress ("Writing down the last 7 pieces…", "Saving the recording…", "Writing the
  notes…", "Writing the reply…"), its Stop stops the reply, and with the app away it ends with "The notes of the meeting
  are ready." (tapping it opens the app). Before, the microphone's service and its wake lock stopped at Stop, and with the
  app away Android froze it. `WorkForegroundService`'s notification id moved from 1402 (also the monitoring service's) to
  1404, and it asks for the notification permission only when it isn't granted. Pixel Fold, "Stop and write notes" then
  Home: the work's service ran with the launcher in front and finished in 110 s, the chat saved with its reply. Stop from
  the meeting's notification with the app away isn't tried yet. iOS gets only its short background time (and Metal can't
  run in the background), so there the notes still need the app in front.
- The recording (since 2026-10-29; Settings > "Keep meeting recordings", on by default): the microphone's samples also go
  to a temporary WAV as they arrive (`Emgu.LiteRT.LM.Agents`' `WavFileWriter`, paused stretches left out), encoded at Stop
  to AAC in an .m4a (`Emgu.LiteRT.LM.Maui`'s `AudioEncoder`: AVAudioFile on iOS and Mac Catalyst, MediaCodec and
  MediaMuxer on Android, 32 kbit/s, ~14 MB an hour; the WAV is kept where that fails) and kept with the reply (saved file
  kind `meeting_recording`, never given to the model; the chat store now names `audio/mp4` files `.m4a`). The card plays
  it (`AudioClipPlayer`: Play/Pause, progress and time), Save... (the demo's Save As dialog) and Share; a reopened chat
  shows the same player, both from one copy in the cache named by the content. Verified on Mac Catalyst: a 1:25 meeting
  became a 360 KB AAC .m4a that played in the card and again after reopening the chat, stored as `<id>_0.m4a`. On the
  Pixel Fold (2026-10-29, Gemma 4 E2B, the synthetic meeting played aloud from a Mac): 1:40 became a 405 KB .m4a (AAC
  mono 16 kHz, 32 kbit/s, 100.9 s by `afinfo`), the temporary WAV was gone, it played to the end, played again from the
  start, and played after reopening the chat. Both cards show the saved file's length from its header
  (`AudioFileLength`: the WAV's data size or the .m4a's movie header), so a reopened one shows it before it plays.
  (A 10 s clip cut off mid-sentence got 3 speakers for 2: the second voice's two 1-2 s fragments were
  too unlike each other to be one turn and too unlike the first voice to join it - a few seconds from someone can still
  come out as a speaker of their own.)
- Speaker labels (since 2026-10-28; the feature's `Voiceprint`, which the demo sets to `Emgu.LiteRT.Models`'
  `SpeakerVoiceprints`, downloaded at the first meeting, 13.4 MB): the recording is also cut at each pause once a piece is
  2 s long (people take turns at pauses), each written-down piece gets its voice's fingerprint, and after Stop the pieces
  are grouped by voice - "[0:09] Speaker 2: ..." in the transcript (a speaker's pieces in a row are one line), the
  document and what the model writes the notes from, with a note that the voices were told apart automatically and names
  are only known when said. One voice: no labels. If the voice model fails, the card says so and the transcript has no labels.
  Verified end to end except the speech recognizer on a synthetic three-voice meeting (`UnitTestSpeakerVoiceprints`, with
  room noise, a late start and a turn split in two), and on Mac Catalyst with the microphone: the same meeting played
  through the Mac's speakers, recorded by the meeting card (Gemma 4 12B writing the notes). The first try found 5
  speakers for 3 - a piece that was mostly the silence before the playback had the room's fingerprint, and a 3 s piece of
  a split turn made a speaker of its own; since fingerprints are taken from the speech frames only, speakers are counted
  in seconds of speech (`MeetingAudioChunk.VoicedSeconds`) and need 5 of them, the second try (2:44 of silence first) had
  3 speakers with every line right, and the notes named "Speaker 3" for the action item she took. With Gemma writing
  it down (pieces cut at sentence pauses too, 1-5 s of speech each), two similar voices were merged: one voice's pieces
  were only 0.48-0.75 alike, as alike as the two voices (0.40-0.52), so none grouped, and each joined the other voice as
  a "speaker" with too little speech. Since then `SpeakerClustering.ClusterSequence` first makes pieces in a row that
  sound alike (at least 0.6) one turn with an averaged, steadier fingerprint, and a speaker with too little speech joins
  another only if it is at least 0.5 alike; the same recording then had every line right. The recognizer lost
  words through the speakers and one whole turn. Not yet tried with real people. Speaker changes without a pause, and a
  turn of under 2 s, go with the piece around them.
- Tested by `UnitTestMeetingNotes`; not yet tried with a real meeting.

## The TF Lite pages, the live camera and the About page

- The TF Lite tab's photo demos (`DemoPage` subclasses: COCO SSD MobileNet, YOLOX, PicoDet, hand gestures, body pose,
  MobileNet, Inception; not D-FINE) have a Live Camera button where `LiveCamera.Supported` (Android, Mac Catalyst, Windows).
  `LiveDemoPage` runs the page's `PrepareAsync` once and its `Analyze(input, render)` on every frame, skipping frames while
  one is analyzed. The page's default `RunAsync` uses the same two, so photo and camera share one model; classifiers set
  `DrawsOnPhoto` false and draw labels on live frames only. On a Galaxy S10: 30 fps at 12-23 ms; on an M1 with a Logitech
  StreamCam: 30 fps at 6-9 ms.
- `LiveCamera` doesn't set a frame size on Mac Catalyst any more (OpenCV's AVFoundation backend turned 640x480, larger than the
  StreamCam's active format, into an Objective-C exception that ended the app). It scales frames over 960 px down, and retries
  empty first reads for 3 s (AVFoundation starts asynchronously). `LiveCamera.FrameRotation(front)` is the Android rotation
  the chat used.
- The About page lists the TensorFlow Lite, LiteRT and LiteRT-LM versions (not XNNPACK, though Mac Catalyst's static
  tfliteextern is built without it). LiteRT's and LiteRT-LM's come from the `Emgu.LiteRT` and `Emgu.LiteRT.LM` assembly
  versions, since neither C API has a version function. LiteRT-LM's is shown once `liblitert-lm` loads (in the background):
  "older than 0.18.0" for an older library, and "Not included" without one.
- It links "Open-source licences" (`LicensesPage.cs`: grouped, searchable, each entry with its licence text), built from
  `Resources/Raw/licenses.json.gz`, which `tools/make_licenses.py` generates. Rerun it after changing packages, native
  dependencies, models or assets. It collects:
  - the .NET packages `dotnet list package --include-transitive` reports for Android, iOS and Mac Catalyst (licences from the
    NuGet cache; Microsoft's and the AndroidX bindings grouped);
  - the Bazel repositories that `deps(//c:litert-lm)` and our runtime targets depend on (licence files from
    `bazel_output/*/external`; Rust crates with their Cargo licence; build-only and other-platform crates left out);
  - OpenCV and the libraries its build information lists in Emgu CV's runtime;
  - a hand-kept list of models (all Apache-2.0 per their Hugging Face cards), fonts (Inter OFL-1.1, Open Sans and Material
    Symbols Apache-2.0), data and services (GeoNames and Open-Meteo CC BY 4.0, Wikipedia CC BY-SA 4.0, Frankfurter, Tavily).
  - Texts not on the machine are fetched at pinned versions with curl into `tools/.license_cache` (gitignored).
- On Mac Catalyst a long label in a `Theme.CenteredScroll` column widened its card and was cut off instead of wrapping, so the
  pages limit such labels to the column's width (`LicensesPage.LimitWidth`).

## Platform notes

- The chat (and its `Emgu.LiteRT.LM` and `.LM.Models` references) is enabled for Windows, iOS, Android and Mac Catalyst. On
  Android, iOS and Mac Catalyst the demo also references the `Emgu.LiteRT.Runtime/Maui/LM/{Android,iOS,Mac}` add-on projects,
  which carry `liblitert-lm.so` and `libGemmaModelConstraintProvider.so`, the two xcframeworks, and the macOS dylibs.
- Verified on the arm64 API 35 emulator with Qwen3, including a fix: the transcript scrolls to `Math.Max(0, content -
  viewport)` rather than `double.MaxValue`, which Android doesn't clamp, so it scrolled the transcript out of view.
- Like the Mac Catalyst app, the iOS app needs the UIScene life cycle (`Platforms/iOS/Info.plist`'s
  `UIApplicationSceneManifest` and `SceneDelegate.cs`), or UIKit kills it at launch when it's built with the iOS 27 SDK.
  Building it with Xcode 27 needs `-p:ValidateXcodeVersion=false`.
