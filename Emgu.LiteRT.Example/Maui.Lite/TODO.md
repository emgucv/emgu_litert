# Chat page TODO

Planned work on the MAUI demo's LiteRT-LM chat page (`LiteRtLmChatPage*.cs`).

Sorted by priority. First what every model depends on - resumable downloads and showing what is downloaded - and
testing what was built but never run. Then small, visible fixes (the Model section, the app icon), and safety nets
that make the rest less risky (model-free tests in CI, an agent regression test). Then storage and the platform: a
Storage page, the next LiteRT-LM release, and GPU follow-ups on platforms not yet verified,
the Pixel Fold freeze, being gentle with phones, background downloads. Then chat features and new agent tools,
smallest and most useful first - with a hands-free voice mode and a task mode before the tools that act outside the
app - and last the largest: other system models, MCP, serving the Mac's models to phones, background tasks (which work
best through that server), image generation on iOS, the privacy manifests an App Store release needs, and
splitting the iOS natives into frameworks.

## 1. Resume partial downloads — completed (.NET / MAUI)

`FileDownloadManager` now keeps `.part` files and strong ETag metadata, resumes with `Range` / `If-Range`,
checks the expected SHA-256 before publishing the final file, and preserves bytes on cancellation/network failure.
Servers without a strong ETag restart safely; interrupted legacy final files have no resume metadata and start over.
The Unity coroutine downloader is separate and is not changed by this MAUI task.

- [x] Download into `<file>.part` and rename it when complete.
- [x] Resume with an HTTP Range request for the missing bytes; start over on 200 instead of appending it.
- [x] Keep the `.part` file when cancelled, interrupted, or the app closes.
- [x] Store the strong ETag with the partial file and send it as If-Range; reject inconsistent ranges/validators.
- [x] Check SHA-256 before publishing; discard checksum failures and retry from zero.
- [x] Demo: "Download paused · 1.2 of 2.6 GB" and Continue in model rows and the download card.
- [x] Local HTTP regression tests: interruption/restart, cancellation, progress including retained bytes, ignored ranges,
      changed validators/checksums, bad ranges, complete partials (416), and publish-after-verification.
- [x] Real Hugging Face pause/resume probe: resumed from the saved offset; retained prefix matched an already verified
      model. Windows MAUI build checked; interactive Continue/cancel UI still needs device verification.

## 2. Show which models are downloaded

Chat model rows show downloaded/downloading/paused state and size. Storage (item 8) provides centralized sizes and
confirmed deletion for chat, image and search models, including caches and partial downloads.
- [x] Mark each row with "Downloaded · size on disk" versus "Download · 2.6 GB", using
      `IsDownloaded(option)`, which only looks at the file and never hashes it.
- [x] Device/cloud icons alongside the text download states: a green phone for on this device, a cloud-download for not
      downloaded or downloading, a pause mark for paused (vector paths, since the icon font subset lacks them; the text
      still carries the state). Checked on Windows; other platforms use the same MAUI shapes but weren't built.
- [x] Drop "~2.6 GB download." from the description of downloaded models; show the size on disk instead.
- [x] A row for a model that is downloading shows its progress, and a partly downloaded one "Download paused"
      with its progress (item 1).
- [x] Refresh status when a download finishes, fails, or is cancelled.
- [x] Refresh after model deletion.
- [x] Centralized model deletion with caches and confirmation in Settings > Storage, replacing optional per-row deletion.
- [x] Equivalent size and delete controls for image and search models in Storage; the image settings link opens it.

## 3. Test the paths that were never run — partially verified

Features that were built and compiled but never run on some platforms (see CLAUDE.md), each checked once on a
device and fixed or noted:

- [x] Pixel Fold Android image generation, memory alongside Gemma 4 GPU, and native image-settings download,
      pause/resume and storage-delete dialogs (details below).
- [ ] Windows image generation; first-run cache preparation with Gemma resident and interactive image saving
      remain additional device checks.
- [ ] Recording a voice message live (`VoiceRecorder.cs`: AVAudioRecorder, Android AudioRecord, Windows MediaCapture) -
      Android capture/model input passed; Mac Catalyst and Windows live capture remain pending.
- [ ] Windows: the PDF page renderer (`PdfPageRenderer`, `Windows.Data.Pdf`) and the photo library (`StorageFile`
      details).
- [x] Tavily web search and page reading: real Mac Catalyst production-tool calls succeeded with the saved key
      (2026-10-16). Search returned 1,978 characters; extraction returned 1,503. No API key or response body logged.
      A full model-driven recent-news reply with citations remains a separate end-to-end check.
- [ ] Photo search on a real phone's library (only the emulator's test photos so far), on a phone whose owner agrees
      to have their photos indexed for the test.

**2026-10-16–17 checks:** [Device check guide and evidence](DEVICE_PATH_CHECKS.md).

- [x] Mac Catalyst image-model download progress, cancellation/resume and storage deletion, using an isolated
      fixture and a real 50 MB Hugging Face graph download. Retained 8,407,809 bytes, resumed with matching SHA-256,
      and deleted the test weights/cache through the production storage catalog; the existing model stayed intact.
- [x] Fixed image-download accounting: `ZImageTurbo.DownloadedBytes` and per-file resumed progress now include
      `.part` bytes, exclude unusable incomplete final files, and avoid double counting completed files.
      Two new image-progress and six existing storage regressions passed.
- [x] Pixel Fold native image Download/Cancel, pause/resume to Ready and storage Delete/Cancel dialogs passed
      using an isolated settings fixture. Fixed small remaining downloads displaying as “0.0 GB”; use MB below 1 GB.
- [x] Pixel Fold standalone CPU image generation: verified all 21 model files after the interrupted/resumed download,
      generated and inspected the eight-step watercolor fox; 883.11 s including first-run cache preparation,
      peak process RSS 3.67 GiB.
- [x] Pixel Fold real private-chat image request with Gemma 4 E2B GPU, 4K context, thinking off and prepared image
      caches: one tool call, one inspected PNG, nonempty reply and image-viewer action; 1,040.71 s total, peak process
      RSS 4.92 GiB (not complete GPU memory). Image generation works but is slow on this phone; investigate performance
      separately. Temporary test models/caches were removed and the clean normal app redeployed.
- [ ] Mac live recording reached the OS microphone permission request; no completed capture is claimed.
- [x] Pixel Fold live microphone capture and Gemma 4 E2B GPU audio input: two actual microphone recordings of
      synthesized speech passed format/signal checks; the model returned a nonempty transcription.
- [x] Fixed recording continuing after navigation: cancel capture on page disappearance and window stop.
      Native Pixel Fold navigation and Home/background checks passed.
- [ ] Real-library photo indexing awaits owner agreement; Windows runtime checks await a Windows device/VM.

## 4. Collapsible "Model" section in settings — implemented; foldable UI check pending

In wide windows (desktop, a foldable's inner screen) the side panel shows Chats, Model and Settings
(`_sidePanel` in `LiteRtLmChatPage.cs`); the model list takes a lot of room.

- [x] Make CPU Models and GPU Models separately collapsible (implemented in both sheet and sidebar).
- [x] Make the outer "Model" title a tappable header with a chevron that shows or hides `_sideModelList`.
- [x] When collapsed, show the selected model and CPU/GPU backend on the header line.
- [x] Remember the outer section and explicit CPU/GPU group states independently in Preferences.
- [x] Preserve access from the model chip on phones; the sheet also has collapsible CPU/GPU groups.

The selected backend's group opens by default until the user explicitly expands or collapses it. Saved choices
survive page recreation and app restarts, including a collapsed selected group. The outer header remains usable
while the model is busy and includes screen-reader descriptions of its state and selection.

- [x] Mac Catalyst: build, expand/collapse, selected-model summary and outer/CPU/GPU state restoration after relaunch;
      compact model chip opens the sheet with the same saved group states.
- [x] Android arm64 build and install on Galaxy Z Fold3, preserving app data.
- [ ] Foldable inner-screen UI check (Z Fold3 installed; initially locked, then wireless debugging disconnected).

## 5. A new app icon — implemented; Windows visual check remains

The demo ("Emgu LiteRT", `com.emgu.litert`) now uses a white chat bubble with a spark cutout on the app's accent blue
(`#3D7BF7`). The editable SVGs replace the .NET template artwork; the foreground and splash share the same mark.

- [x] Simple high-contrast design, checked at 16 px and 32 px and against light and dark surroundings.
- [x] Separate opaque background (`Resources/AppIcon/appicon.svg`) and transparent foreground (`appiconfg.svg`),
      centred with safe margins for Android's adaptive circle and squircle crops.
- [x] Android 13+ monochrome layer: MAUI's generated adaptive icon references the foreground's white alpha mask;
      the spark is a transparent cutout so it survives launcher tinting.
- [x] Opaque 1024 px Apple icon assets verified; the updated Mac Finder/Dock artwork was checked through NSWorkspace.
- [x] Matching `MauiSplashScreen` mark and blue background; Android theme colours updated to match.
- [x] Android builds/install/launch and launcher icons checked on the Pixel API 35 emulator and Samsung Galaxy Z Fold3
      (One UI), preserving existing app data. Circular adaptive-icon rendering checked from generated assets.
- [x] iOS simulator build/install/launch, Home Screen icon and matching splash checked on iPhone 18 Pro (iOS 27).
- [x] Signed Mac Catalyst build and launch verified.
- [ ] Windows taskbar and Start menu visual check on a Windows machine.

Launchers cache icons: reinstall the updated app without clearing its data; on Mac, re-register the app with Launch
Services and relaunch if needed. Do not uninstall just to refresh an icon, since that removes downloaded models and
saved chats. Older demos with the separate `com.emgu.tf.lite.maui.demo` identifier keep their own original icons.

## 6. Run the model-free tests in CI — completed

The native workflow only builds. `.github/workflows/tests.yml` now runs the tests that need neither a model nor this
repo's native libraries on every push and pull request (Ubuntu 26.04, .NET 10): CMake generates the projects'
`Directory.Build.props`, then `dotnet test --filter "TestCategory!=Native"`.

- [x] The two test classes that load `tfliteextern`/`libLiteRt` (`UnitTest1`, `UnitTestLiteRtModels`: version, GPU,
      MobileNet, Inception, COCO SSD) are in the `Native` category; the model tests stay ignored without
      `-p:TestModels=true`. Everything else - calculator, unit converter, HTML text, memory tools, photo requests and
      places, document splitting, Z-Image math, chat storage/export/search, visual agents, downloads, workload policy -
      runs: 111 tests, 49 skipped, ~4 s.
- [x] Rehearsed in a .NET 10 Linux container (arm64) from the tracked files only, without `lib/`: 111 passed. On the
      Mac with the natives present, `TestCategory=Native` runs the other 9.
- [x] First GitHub run (commit 028c7ac0): 111 passed, 49 skipped, 0 failed; the job takes ~1 minute.

## 7. An agent regression test — implemented

Small models are sensitive to tool wording: the first `generate_image` result text made Gemma 4 E2B end its turn with
no answer, which only a run with the model showed.

- [x] A fixed set of prompts, each with the tool(s) Gemma 4 E2B should call and argument checks, e.g. "what's 17% of
      230?" -> `calculate`; "weather in Paris" -> `get_weather`; "draw a cat" -> `generate_image`; "show my beach
      photos" -> `find_photos`; "remember I'm vegetarian" -> `remember`; plus "the reply isn't empty".
- [x] Run with fake external tools (fixed results) and isolated in-memory memories so it needs no network after
      caching Gemma, no photos or image model; `TEST_MODELS`-gated. Real tool schemas/descriptions are retained.
- [x] [Run guide and baselines](../../Emgu.LiteRT.Test/Emgu.TF.Lite.Test/AgentRegression.md) for LiteRT-LM updates,
      model changes and tool wording edits. JSON/TRX reports preserve calls, arguments, results, final replies and pass rates.

Eight scenarios include unit conversion, past-chat search and disabled image generation, with fresh sessions and a
nonempty final assistant message required after tool execution. Image-success wording is shared with the demo; memory
tools use the real result text. The first 3-repeat run caught empty replies after `remember` (21/24 scenarios passed);
its result now explicitly asks for a brief acknowledgement, including the already-remembered case.
The corrected 3-repeat baseline passed 24/24 scenarios on macOS arm64 CPU with LiteRT-LM v0.18.0; all three
model-free harness checks and the existing memory-tool test passed too.

## 8. A Storage page in settings

The app can use a lot of space: chat models (0.35-6.9 GB each), their GPU caches (0.8-6 GB), the image model
(~21 GB with its caches), the search model, saved document indexes and attachments, the photo index.

- [x] One list: each item with its size on disk and Delete (asking first): chat models with their GPU caches, the
      image model, the search model, saved documents, the photo index, chats' attachments; the total, and the device's
      free space.
- [x] Deleting a model the chat is using releases it first; deleting the search model turns document/photo search off
      until it is downloaded again.
- [x] Replaces the per-row delete in item 2 (or both open the same action).

Implemented **Settings > Storage**, including partial downloads, CPU/GPU caches, legacy models, Z-Image's host data
and caches, documents, photo/past-chat indexes, attachments and shared export copies. The total counts the SQLite
database once; individual database items show stored-data estimates since their disk pages are shared. Deletion
stops indexing/download work and releases the relevant native model first. Removing the selected search model
persists a block on automatic reloads, turns photo/chat search off, and removes the current document from the page;
Storage offers an explicit Download action to restore the search model. Deleting documents detaches all saved-chat
references, while keeping messages and their saved excerpts. Database deletions compact SQLite to reclaim space.
Tests use temporary files/databases to verify accounting, cache boundaries, deletion and space recovery. Windows
build checked. Windows interactive check (2026-10-17): confirmation dialogs with a working Cancel, deleting a loaded
model released it cleanly and one Delete removed the model and its GPU cache, and deleting the search model turned the
search switches off and offered Download. Fixed on the way: rows for things not on the device said "0 B on disk" next to a
greyed Delete (now "Not on this device", no button), and the photo and past-chat search switches were just disabled after
the search model was deleted - each now shows a note with its own Download button. (The packaged app sees the real
LocalApplicationData folder through the package's copy, so test models copied into both places need deleting twice.)

## 9. Update to the next LiteRT-LM release

LiteRT-LM v0.18.0 is the current pin. The next release brings upstream fixes and unblocks the embedding models' GPU
(item 11: its `main` already pins a LiteRT with `dd3db3123`).

- [ ] Move `LiteRT-LM` to the release and `litert` to its `LITERT_REF` (CLAUDE.md, "The `litert` submodule must match
      LiteRT-LM's LiteRT version").
- [ ] Rebuild the natives for macOS, Mac Catalyst, iOS, Android (all four ABIs) and Windows; check the Windows export
      list (`litert_windows_exports.def`) and the C API wrappers against header changes.
- [ ] Re-run the model tests, check every model file in the demo still loads and answers correctly (some community files
      broke with v0.18.0), and the agent regression test (item 7).

## 10. GPU on Windows — core support completed; follow-ups remain

The original prebuilt accelerator did not match our core's private C++ ABI. A reproducible source build now supplies
matching core/WebGPU/Dawn DLLs. See the [Windows GPU guide](../../platforms/windows/README.windows-gpu.md),
[investigation](../../platforms/windows/INVESTIGATION.md), and
[compatibility report](../../platforms/windows/GPU-MODEL-COMPATIBILITY.md) for build pins, packaging and hardware results.

- [x] Build a matching core/WebGPU/Dawn bundle with `bazel_build_windows_gpu.ps1`, isolated from CPU build outputs.
- [x] Verify plugin discovery beside the Windows app and standalone apphost executables.
- [x] Package GPU DLLs as optional `Emgu.LiteRT.GPU.runtime.windows`, depending on the matching
      `Emgu.LiteRT.runtime.windows` core package; the demo references it in GPU builds.
- [x] Expose verified models in a GPU Models section above CPU Models, replacing the global GPU switch;
      retain the selected model/backend across restarts and offer explicit CPU fallback on GPU loading failure.
- [x] Test five chat models on RTX 3070 with FP32 where required; record results and Qwen fixes in the compatibility
      report. Gemma 4 12B is CPU-only on Windows after GPU image replies failed at 8k and 4k contexts.
- [x] Retain GPU program/weight caches next to the model across CPU/GPU selection changes (the old proposal to delete
      them on switching off GPU is superseded).
- [x] Verify Windows multimodal embedding GPU support (item 11) and investigate Z-Image-Turbo: the complete Windows GPU
      image-generation pipeline failed compilation; CPU remains available.
- [x] Build/run the Windows MAUI demo on a Direct3D 12 GPU machine.
- [x] Compare MobileNet CPU/GPU outputs in an automated Windows `TestLiteRtMobilenetGpu`: it copies the source-built
      bundle named by `EMGU_LITERT_WEBGPU_DIR` next to the test and is skipped without the plugin. RTX 3070 / Direct3D 12:
      same top class, max abs diff 0.0082 (tolerance 0.01). Run it alone with `--filter`, since `libLiteRt.dll` can't be
      replaced once loaded.
- [x] Long-chat CPU/GPU accuracy and performance comparisons in `Performance.md` (Windows section, RTX 3070): `UnitTestLongChatBenchmark`
      at 4k and 8k for CPU, GPU fp32 and GPU fp16. fp32 and CPU answer correctly; fp16 garbles the digits, as on the
      other platforms. 16k/32k, E4B and other GPUs aren't measured.

## 11. GPU for the embedding models — Windows completed; Apple/Android pending

Windows now uses a matched source-built core/accelerator pair, avoiding the old prebuilt WeightLoader mismatch.
Apple/Android still retain the default GPU refusal until their runtimes match. No submodule update was needed for
the Windows source-built bundle.

- [x] Add an explicit `EmbeddingModel.EnableWindowsWebGpu` opt-in, honored only on Windows; keep other platforms guarded.
- [x] Test Text Vision 440M and Multimodal 740M on Windows GPU: text/images, plus 740M audio, finite normalized vectors,
      cosine similarity > 0.997 against CPU, matching retrieval rankings, no Dawn validation errors.
- [x] Demo: independent embedding model/backend selector with CPU fallback, reload and correct document/photo cache
      handling. Only multimodal models are offered; Windows GPU entries appear first.
- [x] Regression test for the default GPU guard and platform restriction; local GPU test harness and results recorded
      in `platforms/windows/GPU-MODEL-COMPATIBILITY.md`.
- [ ] Update LiteRT-LM/LiteRT together for matching prebuilt Metal/OpenCL accelerators (item 9); rebuild Apple/Android.
- [ ] Verify CPU/GPU text/image/audio embeddings and search on Mac and a real Android phone before enabling them.
- [x] Add a persistent automated GPU embedding test skipped without the matching accelerator plugin:
      `TestEmbeddingGemma2TextVisionGpuMatchesCpu` (`TEST_MODELS`, Windows) - on the RTX 3070, GPU/CPU cosine 0.998-0.999
      for three texts and an image.
- [ ] Verify simultaneous chat/search GPU memory use and indexing performance on phones.

## 12. Pixel Fold freeze — rendering stalls fixed; original ANR unconfirmed

A Pixel Fold "isn't responding" at 20:17 on 2026-10-10 has no retained matching trace (CLAUDE.md). The
[2026-10-15 investigation](PixelFoldFreeze.md) reproduced a separate, concrete long-chat rendering problem.

- [x] Collect app watchdog logs/Java main-thread stacks and Android exit history on the physical Pixel Fold (Android 17).
      Exercise a synthetic 160-message saved chat, streaming, drop/summary compaction, document/photo indexing,
      app background/return and shell-requested folded/unfolded configurations, using a separate cache database.
- [x] Fix reproduced 4,767 ms opening and 4,159 ms layout stalls: yield between short restore slices, suppress
      per-row animated scrolling and coalesce subsequent scrolling. Prevent other chat actions during restoration.
- [x] Fixed comparison: no one-second watchdog event through opening, streaming, compaction, document indexing,
      four completed photo embeddings and a repeated 162-message reopen. Keep the Debug watchdog; remove probe data.
      Android/Mac Catalyst builds and 13 private/workload tests pass. Total large-history rendering time remains a limit.
- [ ] Extended manual hinge testing and full native image-generation stress (model absent; only ~22 GB free, near
      the model/cache requirement). Shell fold-state transitions do not cover all physical hinge behavior.
- [ ] Capture a matching trace if the original dialog recurs. Exit history also has un-attributed non-user-perceptible
      job-start ANR annotations without a retrievable trace; the rendering fix does not prove those resolved.

## 13. Be gentle with phones — implemented

Photo indexing (~20 minutes for 200 photos on a phone's CPU), document indexing and image generation keep the CPU busy.

- [x] Photo, past-chat and document indexing, plus image generation, pause at 20% battery or lower without external power. Resume on power
      or at 25% to avoid repeatedly pausing near the threshold. Unknown battery levels do not count as low.
- [x] Pause at Android severe-or-higher thermal status and Apple serious/critical thermal state, including while
      charging. Mac Catalyst also checks thermal state; phone battery restrictions do not apply to desktops.
- [x] Settings show the photo/chat/image pause reason; document chips and image tiles also explain the pause.
      Work resumes automatically at the next check (about one second while paused), and cancellation remains usable.
- [x] Optional persistent "Index photos only while charging" switch on Android/iOS, off by default.
- [x] Check between photos, document batches/images and image-generation stages. Finish the active native call
      before pausing; retain completed embeddings and image-generation state. Release the shared embedder while
      waiting so interactive searches remain available. Downloads and normal chat replies use their existing behavior.
- [x] Eight model-free tests cover battery thresholds/recovery, charging preference, heat, cancellation and retained
      document embeddings. Related download/chat-index regressions also pass.
- [x] Android API 35 emulator: native thermal/battery readings triggered heat and low-battery pauses; external power
      resumed work, charging-only indexing waited at 80% until power was connected, and a paused wait was cancellable.
- [ ] Extended real-device indexing/generation checks under heat and low battery; iOS battery APIs need a physical device.

## 14. Downloads that continue in the background — implemented with bounded Apple completion time

- [x] Android: a non-exported `dataSync` foreground service with aggregate progress, notification permission request,
      a Pause action and an app-opening content intent. A partial wake lock keeps transfers moving with the screen
      off; completion, cancellation, failure, service shutdown and Android 15+ timeout release it and stop the service.
- [x] iOS / Mac Catalyst: UIKit background-task lifetime around transfers, expiring into resumable cancellation.
      Mac Catalyst also holds a process activity. The app explains Apple's finite finishing time and Continue/retry.
- [x] Chat, search/embedding (including Storage restore), and image-model downloads use the shared lifetime;
      engine loading/inference does not keep a service or background task running.
- [x] Builds on item 1: Range/strong-ETag resume and checksum verification remain in the existing downloader.
      Apple's native HTTP handler can report cancellation as a timeout; normalize this to cancellation so it is
      presented as a paused transfer. Image downloads also recognize platform cancellation.
- [x] Model-free tests cover concurrent lifetimes, cancellation isolation, pause/complete races, progress snapshots
      and a real loopback transfer paused then resumed with its retained offset and validator (19 focused tests).
- [x] Android API 35 emulator: local 16 MB transfer continued with the app backgrounded and screen asleep, resumed
      retained bytes, verified SHA-256, and released service/wake lock. Notification Pause action verified through UI.
- [x] iOS 27 simulator: a throttled transfer continued behind Settings; cancellation retained bytes, and a later
      attempt resumed with Range/If-Range and verified the final SHA-256.
- [x] Mac Catalyst: a throttled transfer continued behind Finder; pausing retained bytes, and a later attempt
      resumed and verified the final SHA-256 with all download activities released.
- [ ] Extended screen-off / battery-policy checks on physical Android and iOS devices.

Apple support uses the explicitly allowed bounded-task approach. Durable background URLSession transfers that can
finish arbitrarily large files while suspended remain a separate follow-up, not a guarantee of this implementation.

## 15. Agent tool: search documents (`search_documents`)

A document only helps the chat it is attached to, and its excerpts go with every message whether they are needed or not.
Its index is saved in the chat database (`Document` / `DocumentItem` tables), so every document ever attached can be
searched.

- [x] Tool in `Emgu.LiteRT.LM.Agents` (`AgentTools.SearchDocuments(search)`, the app provides the search, like
      `FindPhotos`): a query in the user's words, optionally a document name.
- [x] Demo: search the saved indexes with the embedding model already used for documents and photos
      (`EmbeddingGemma2TextVision`, `_embedderGate`); return the best passages with the document name and page,
      within the excerpt budget the attached-document path uses (1,600 / 3,200 characters).
- [x] Keep attaching a document to a chat as it is; the tool adds "in any of my documents" and lets the model decide
      when to look.
- [x] Saved documents with their size and delete: in the Storage page (item 8).

Implemented as `DocumentLibrarySearch` (cosine ranking of the saved passages, the chat search's cutoffs: score at least
0.5 and within 0.12 of the best; optional case-insensitive document-name filter). Indexes built with another embedding
model are skipped rather than rebuilt. The tool is omitted in private chats and reports a deleted search model. Model-free
tests cover the tool, ranking, filtering, budget and model mismatch; the Windows build passes. Still to check: Gemma
actually choosing the tool on a device, and a regression scenario in the agent test (item 7).

## 16. Agent tool: search past chats (`search_chats`)

"What did we decide about the trip last week?" Memories only keep the facts the model chose to save; the saved
messages have everything.

- [x] Embed the saved messages (user and assistant, chunked) in the background with the same embedding model, like the
      photo index; a table of message embeddings, updated when exchanges are saved and pruned when chats are deleted.
- [x] Tool (`AgentTools.SearchChats(search)`): returns matching snippets with the chat's title and date; the demo
      also shows them as tappable links that open the chat.
- [x] Optional date filter ("last week") with the same parsing as `PhotoRequest`.
- [x] Setting to turn it off (and delete its index), like photo search.

Implemented with an opt-in, persisted **Search past chats** setting, incremental SQLite passage embeddings,
model-specific rebuilding, and links above the reply. Indexing pauses between passages while the chat is busy and
is cancelled before embedding-model reloads. The current conversation, tool steps, reasoning and attachments are
excluded from search. Automated tests cover tool invocation, date parsing, chunking, incremental/cancelled indexing,
model changes, filtering, and deletion. Windows (2026-10-17): the setting indexes in the background, Gemma calls the tool and the links open the chat, turning it off and on re-indexes.
Windows CPU verification with EmbeddingGemma 2 Text Vision 440M and Gemma 4 E2B passed: semantic queries retrieved
the correct saved chats, and Gemma called `search_chats` and answered from the returned travel decision.

## 17. Image generation extras

- [ ] "Draw it again" with a new seed (a button on the picture, or the model calls the tool again with the same
      description).
- [x] Save the original PNG to a user-selected file from the enlarged image view, including images reopened from saved
      chats (`CommunityToolkit.Maui.Core` FileSaver). Windows save dialog verified, and a cancelled dialog no longer shows a
      "Couldn't save" alert (the Windows picker reports a cancel as a plain exception).
- [ ] Save the picture to Photos / the gallery.
- [ ] Share the picture (MAUI `Share`).
- [ ] Show the description and seed under the picture (tap), so a picture can be made again.

## 18. Agent tool: create documents (`create_document`)

"Make me a packing list as a document", "write that up as a Word file".

- [ ] Tool in `Emgu.LiteRT.LM.Agents` (`AgentTools.CreateDocument(save)`, the app saves and shows the file): a title, the
      content in Markdown, and a format - Word (.docx), PDF or Markdown.
- [ ] Writing: Markdown as is; .docx with the Open XML basics (headings, paragraphs, lists, bold/italic, tables) -
      the document reader already parses `word/document.xml`; PDF with a small writer, or left out at first.
- [ ] Demo: the file appears as a card in the reply (name, size, format) with Open and Share (MAUI `Launcher` / `Share`),
      saved with the chat like the generated pictures (a new attachment kind, never given back to the model).
- [ ] Long content: the model writes it in its reply budget (`MaxOutputTokens`); for longer documents, ask it to write
      section by section.

## 19. A private chat

- [x] A "private chat" option: not saved, no memories read or written, no chat search; its pictures and documents are
      discarded when it ends. Shown clearly in the header while on.

Implemented a per-session **Private chat** switch in chat settings and a **Private · Not saved** header badge.
Changing modes starts a fresh conversation; opening saved history ends the private chat after confirmation.
Private messages, summaries, tool steps, pictures, recordings and document indexes are not saved to the chat
database or attachment folders. Memory and past-chat tools are omitted, with store-level read/write guards as a
second boundary; private documents do not reuse saved indexes. New-chat/mode/navigation cleanup clears transcript,
pending media, native conversations, generated-image prompt caches and document references, and waits for private
document tasks before releasing their embedding model. App-cache picker copies and normal recorder/renderer
temporary files are cleaned up; user originals are retained. Debug breadcrumbs redact activity details while the
private session is active. Web tools retain their separate toggle, and explicitly saved images remain saved.
Private mode is not persisted across app restarts. Regression tests verify storage isolation, memory/tool/search
boundaries, regular saving after leaving private mode, and cache-copy cleanup. Windows build checked; the interactive
mode transitions were verified on Windows (badge, not listed in history, New chat, the End private chat dialog skipped
for an empty chat and shown, with Cancel, otherwise). Apple/Android media cleanup still needs device verification.

## 20. Export or share a chat

- [x] Export a chat as Markdown (or text) with its pictures and attachments, through the share sheet; from the chat
      list and the chat's header.
- [ ] Optionally import it back.

Export creates a ZIP containing `chat.md` and byte-for-byte copies of saved images (including generated pictures)
and audio, linked with relative paths. Full saved messages survive context compaction; thinking, tool steps and
saved document excerpts are included. The currently attached document's stored text and images are exported too;
the original PDF is not retained and cannot be recreated. Missing files are noted in the transcript. Sharing uses
MAUI's native share sheet; ZIPs remain in a dedicated cache folder so recipients can read them after the sheet
closes, with old exports removed on a later export. Automated archive tests and a Windows build verify the export;
the Windows share sheet opens and the shared ZIP holds chat.md. Import remains optional follow-up work.

## 21. Accessibility

- [ ] Screen-reader labels for the newer controls: image tiles, step lines, thinking headers, the settings switches and
      download states, photo thumbnails (`SemanticProperties`).
- [ ] Large text: check the chat, settings and sheets with the largest font sizes on Android and iOS.
- [ ] Contrast of the secondary text in light and dark mode.

## 22. Hands-free voice conversation — implemented; verified on a Pixel Fold and on Mac Catalyst

Voice messages and "Read replies aloud" exist, but each turn needs a tap. A conversation mode: speak, hear the reply,
speak again.

- [x] A "Voice chat" chip (Gemma 4; not on Windows, whose recorder can't measure the microphone level) opens a
      full-screen view that listens, finds the end of speech (`VoiceActivityDetector`: the level against a tracked
      background - 12 dB above it for 0.2 s starts speech, 0.9 s below +9 dB ends it (a single loud 50 ms frame
      doesn't), 30 s at most - from AVAudioRecorder's metering or the RMS of Android's 16 kHz samples, read 20 ms at a
      time; on a Pixel Fold the 200 ms reads left most 50 ms frames empty, which counted as digital silence, pulled
      the background down to it and kept a noisy room's peaks above the -50 dBFS minimum, so speech never ended),
      sends the audio to Gemma 4 (without thinking, for a quick answer) and reads the reply aloud, then listens again.
- [x] Read aloud while the reply streams, sentence by sentence (`SpeechSegmenter`, `ReplyReader`): sentences and line
      ends, code blocks and table rows skipped, thinking never read; also for "Read replies aloud" outside the voice chat.
- [x] Interrupt: a tap on the circle stops the reply (generation cancelled, the text so far kept with "…", the model
      given the conversation again with the next message) and listens; while listening, a tap sends at once. With
      headphones, speaking interrupts too and becomes the next message.
- [x] The voice view: a circle that grows with the voice (blue, listening), breathes while the model thinks (grey) and
      pulses while it speaks (green), with the sentence being read and a hint; the transcript stays in the chat. The
      growth is eased (up over ~0.15 s, down over ~0.3 s) and at most a quarter: following the level directly, by up
      to 35%, it jumped with every syllable.
- [x] Verified on Mac Catalyst with Gemma 4 E2B on the GPU (temporary self-test: `say` recordings played in place of
      the microphone, speech output replaced by a log): two questions in a row ("Paris", then "Berlin" for "And what
      about Germany?"), a long explanation interrupted by a tap and then "What were you explaining?" ("how a bicycle
      works"), and with headphones a question spoken over the reply ("2 + 2 equals 4"). Unit tests: `UnitTestVoiceConversation`
      (end of speech in LiteRT-LM's `audio_sample.wav`, clicks, a growing noise, pauses, the length limit; streamed
      sentence splitting).
- [x] Verified with a real microphone and loudspeaker on a Pixel Fold (Gemma 4 E2B on the GPU), without headphones:
      the end of speech is found in a room 25-35 dB quieter than the voice, and the reply is read and listening
      resumes; and on Mac Catalyst (a Mac mini with a USB microphone - the app records from the system's input
      device - and its built-in speakers).
- [x] Models that don't take audio (your own server's, the Qwen models) get voice messages and the voice chat too:
      `SpeechToText.cs` writes the recording down with the platform's recognizer and the words are sent as the
      message ("Writing down what you said..."). Android 13+: `SpeechRecognizer` (on the device when it can - Google's
      offline recognizer) reading the recording from a pipe (`EXTRA_AUDIO_SOURCE`, segmented session, partial
      results). It needs the audio at recording speed (given all at once it dropped words), doesn't take the end of
      the pipe as the end of speech (its final result came minutes later) and passed a reused recognizer's late
      results to the next recording - so the samples are fed 20 ms at a time, followed by 0.6 s of silence and
      `StopListening`, the last partial results are used if no final one comes within 2.5 s, and each recording gets
      its own recognizer. Verified on a Pixel Fold: LiteRT-LM's `audio_sample.wav` and a `say` question, twice each,
      word for word, ~1 s after the recording's length; a voice message to LM Studio (Gemma 4 26B) answered "The
      capital of France is Paris." iOS/Mac Catalyst: the Speech framework (`SFSpeechUrlRecognitionRequest`, on the
      device when supported, `NSSpeechRecognitionUsageDescription`) - built, not yet run (the first use asks for
      permission).
- [ ] Speech-to-text on Mac Catalyst and an iPhone; Windows has none (its recorder has no level for the voice chat
      either, but voice messages could use `Windows.Media.SpeechRecognition`).
- [ ] An iPhone with a real microphone, and any device with headphones (speaking over the reply).
- [ ] Headphone detection (`VoiceRecorder.HeadphonesConnected`: the audio route on iOS/Mac Catalyst, output devices
      on Android) is untested; so is recording while text-to-speech plays on iOS.

## 23. Task mode: multi-step tasks with a visible plan

Gemma 4 already chains tools within a message, out of sight. For longer tasks ("plan a weekend in Paris: the weather,
three museums open on Sunday, and a packing list as a document"), a task mode that shows the work.

- [ ] The model first writes a short numbered plan (an instruction plus a structured output), shown as a checklist card.
- [ ] Each step runs in turn (its tool calls as step lines), ticking the checklist; the user can stop, skip a step or
      edit the plan.
- [ ] Steps that change something outside the app (device actions, item 24, MCP tools with side effects) wait for the
      user's approval in the card.
- [ ] The context window: each step gets the plan and the earlier steps' short results, not their full tool output.
- [ ] Turned on per message (a chip like "Think" / "Web search") or proposed by the model for long requests.

## 24. Agent tools: device actions, confirmed by the user — implemented; Android verified, iOS/Mac Catalyst untested

Tools that change something outside the app, so each shows a card with what it will do and Confirm / Cancel before
anything happens (the model can get a date or time wrong).

- [x] `Emgu.LiteRT.LM.Agents`: `AgentTools.DeviceActions(kinds, perform)` builds `set_timer`, `set_alarm`,
      `add_calendar_event`, `add_reminder` and `open_directions`; the model passes times in the user's words, which
      `WhenParser` reads (durations - "half an hour", "1:30", "2 hours and a half" - and dates/times - "tomorrow at
      6:45 am", "next Friday at noon", "3rd of March", "in 2 weeks"; an hour without am/pm is the next one to come), since
      small models get exact dates wrong; an unreadable or past time goes back to the model. The app's `perform` gets a
      typed request (`TimerRequest`, `AlarmRequest`, ...) with a title, details and confirm text, and returns
      `DeviceActionDone`/`DeviceActionCancelled`/`DeviceActionFailed` for the model. `DeviceActionInstructions(kinds)`
      goes into the agent's instructions: without it, Gemma 4 26B-A4B (LM Studio, thinking off) answered "I've set your
      alarm" without calling `set_alarm` about half the time. Unit tests: `UnitTestDeviceActions`.
- [x] Android (`DeviceActions.cs`): timers and alarms through `AlarmClock` intents (`EXTRA_SKIP_UI`, so the app stays in
      front; alarms only within 24 hours - the intent takes an hour and minute), events through the calendar's insert
      intent (opens the calendar app filled in; the user saves there, and the model is told it isn't saved yet - it
      said "added" otherwise), directions through a Google Maps URL with the travel mode. iOS / Mac Catalyst: events
      and reminders saved with EventKit (write-only calendar access, full reminders access), directions in Apple Maps;
      no public timer/alarm API, so those tools aren't offered. Windows: directions (Bing Maps) only.
- [x] The confirmation card above the reply (icon, title, details, Confirm / Cancel, then the outcome: "✓ Timer started",
      "Cancelled"...); stopping the reply cancels it. The step line ends with "- done" / "- cancelled" / "- failed" and is
      saved with the chat. Not offered in the voice chat, whose full-screen view would hide the card.
- [x] Manifest entries: Android `SET_ALARM` and intent queries for `SET_TIMER`, `SET_ALARM` and the calendar insert;
      iOS / Mac Catalyst calendar and reminders usage descriptions.
- [x] Verified on a Galaxy S10 (Android 12): with Gemma 4 E2B on the device, a 1-minute timer ("tea") set in Samsung
      Clock from the card; with LM Studio's Gemma 4 26B-A4B (thinking on), an alarm cancelled from its card, a calendar
      event (title, Friday's date, 12:30-1:30, location; the phone's three calendar apps brought up Android's chooser)
      and directions on foot, by car and by transit opened in Google Maps, each answered. With thinking off the server's
      model still skipped the tool in 20-40% of replayed requests, with thinking on in none of 16.
- [x] Your own server: the first directions reply was lost ("unexpected end of stream") when Maps came to the front -
      the connection dropped as Android stopped the app - so a failed server request is now sent again once, after
      the app is back in front (`ServerRetryHandler`); the drop didn't recur in 4 later runs, so the retry is untested.
- [ ] iOS / Mac Catalyst: EventKit events and reminders, Apple Maps (built, not run; the first use asks for access).
- [ ] Alarms more than 24 hours ahead (a calendar event is suggested instead); timers/alarms on iOS (no public API).

## 25. Goals and check-ins

"I want to run three times a week", "read 20 pages a day".

- [ ] A goals list kept like memories (`ChatStore`, local only): the goal, its target and period, and progress entries;
      tools `add_goal`, `log_progress`, `get_goals` (`Emgu.LiteRT.LM.Agents`, with an app-provided store like
      `IAgentMemoryStore`).
- [ ] The goals go into the agent's instructions with the memories, so "I ran today" is logged without asking.
- [ ] Settings: the goals with their progress, edit and delete.
- [ ] Check-ins: a reminder (device actions, item 24) or a scheduled task (item 30) asks how it went.

## 26. Agent tool: clipboard (`read_clipboard`)

"Summarize what I just copied", "translate the text I copied".

- [ ] Tool (`AgentTools.ReadClipboard(read)`, the app reads MAUI's `Clipboard`): text only, trimmed to the excerpt
      budget.
- [ ] Only when a message asks for it (the tool description says so), since a clipboard can hold passwords; show a step
      line "Reading the clipboard" so the user sees it happened.

## 27. More models: Gemini Nano and Apple's on-device model

System models, offered in the model picker only where the device has them (detected at run time):

- [ ] Gemini Nano through ML Kit GenAI's Prompt API (Android: Pixel 9/10, Galaxy Z Fold7 and other recent flagships;
      not the emulator, the Pixel Fold or the Z Fold3): a .NET for Android binding of `com.google.mlkit:genai-prompt`,
      wrapped as an `IChatClient`; check whether it supports tool calling.
- [ ] Apple's Foundation Models through `Microsoft.Maui.Essentials.AI` (an `IChatClient`), once the repo moves to
      .NET 11 (iOS / Mac Catalyst with Apple Intelligence).

## 28. MCP support

Let the chat's agent use tools from Model Context Protocol (MCP) servers, next to its built-in ones
(`ChatAgentTools.Create`).

- [ ] Client: the official C# SDK (`ModelContextProtocol` NuGet package); its tools are Microsoft.Extensions.AI
      `AIFunction`s, so they go straight into `ChatOptions.Tools` with the built-in ones.
- [ ] Transports: Streamable HTTP / SSE for remote servers on every platform; stdio (starting a local server process)
      only on Mac Catalyst and Windows, since iOS and Android can't launch processes.
- [ ] Settings: a "Tool servers (MCP)" section to add, remove and switch off servers (name, URL, optional
      authorization header kept in `SecureStorage`), showing each server's connection state and tool count.
- [ ] Which tools: Gemma 4 E2B has a 4k-8k token window, and every tool description takes part of it. Let the user
      pick a server's tools, or offer only the tools of servers switched on for the chat.
- [ ] Step lines: describe MCP tool calls by server and tool name (`AgentTools.Describe` falls back to
      "Calling <name>").
- [ ] Safety: ask before running a tool that the server marks as having side effects (MCP tool annotations), and
      show which server a result came from. Tool results are untrusted text.
- [ ] Docs and a test against a public MCP server.

## 29. Serve the Mac's models to phones ("Server" option)

The Mac Catalyst app serves the model it runs (e.g. Gemma 4 12B on the Metal GPU, which no phone can hold) over the
local network, and phone apps - this demo, or any other - use it as if it were a local language model.

### Protocol

- [ ] OpenAI-compatible HTTP API, so any OpenAI client works with it (and our clients work with Ollama / LM Studio too):
      `POST /v1/chat/completions` (streaming over server-sent events, and non-streaming), `GET /v1/models`; later
      `POST /v1/embeddings` (the search model) and `POST /v1/images/generations` (Z-Image-Turbo, so a phone gets
      pictures without the 21 GB model).
- [ ] Map requests onto `LiteRtLmChatClient` (`IChatClient`): text, images and audio (`image_url` data URIs,
      `input_audio`) as `DataContent`, thinking (`reasoning_effort`) as `ChatOptions.Reasoning`, `tools` /
      `tool_choice` as function declarations, `max_tokens`; the reply's thinking, text and `tool_calls` back, with ids.
- [ ] Tools stay on the phone: the server only returns tool calls and the phone runs them (location, photos, memories
      and the calendar never leave it); only the model's input and output cross the network.

### Server library

- [ ] A new package, e.g. `Emgu.LiteRT.LM.Server`: serves any `IChatClient` (and optionally an embedding model and an
      image generator) over that API; testable from a console app.
- [ ] HTTP server: ASP.NET Core (Kestrel) isn't available to Mac Catalyst apps, so `System.Net.HttpListener` or a small
      embedded server. First check `HttpListener` works inside the Catalyst app (listening on all interfaces, not just
      localhost); if not, the fallback is a plain macOS console host with ASP.NET Core (losing the in-app switch).
- [ ] One model, one conversation at a time: queue requests, and serve one active client at first (several phones
      would take turns, or each need its own LiteRT-LM conversation and KV cache - more memory).
- [ ] The API is stateless, so every request resends the whole conversation: keep the open conversation when a request
      only adds to it (what `LiteRtLmChatClient` already does for one client), so the model doesn't re-read it.
- [ ] Cancel generation when the client disconnects.

### Mac Catalyst app

- [ ] Settings: "Serve models on this network" switch, the model it serves (the chat's selected model), the address,
      and a list of connected devices with their last request; turning it off stops the server.
- [ ] Pairing: show a QR code with the address and a random access token; every request must carry the token
      (`Authorization: Bearer ...`); a button to make a new token (unpairing every phone).
- [ ] Discovery: announce the server with Bonjour (`_emgu-litert._tcp`, with the model's name), so paired phones find
      the Mac again when its address changes.
- [ ] Keep the Mac awake while serving (and say so in the settings); the app must stay running.
- [ ] macOS asks whether the app may accept incoming connections the first time; a sandboxed build would also need the
      `com.apple.security.network.server` entitlement.

### Phone app (this demo on Android / iOS, or any app)

- [ ] Client: `Microsoft.Extensions.AI.OpenAI` -
      `new OpenAIClient(token, new() { Endpoint = macUrl }).GetChatClient(model).AsIChatClient()` - used exactly like a
      local model, including `UseFunctionInvocation()` and `ChatClientAgent`; no LiteRT needed on the phone.
- [ ] "Connect to a Mac": scan the QR code (or type the address and token); the token in `SecureStorage`.
- [ ] The remote model in the model picker, e.g. "Gemma 4 12B on Studio Mac" (named after the Mac), with its state (connected, Mac asleep
      or unreachable); its context window and capabilities (images, audio, thinking, tools) come from the server.
- [ ] Find the Mac again with Bonjour: Android `NsdManager`; iOS needs `NSLocalNetworkUsageDescription` and
      `NSBonjourServices` in `Info.plist`.
- [ ] iOS allows plain HTTP to local addresses with `NSAllowsLocalNetworking` (App Transport Security); Android needs
      cleartext allowed for local addresses (`network_security_config`).

### Security and remote use

- [ ] Plain HTTP on the home network, protected by the token; away from home, Tailscale (encrypted, and the Mac's
      Tailscale address works like the local one, as with wireless adb); HTTPS with a self-signed certificate is
      possible but fiddly on phones.
- [ ] Never listen without a token; rate-limit failed tokens; log requests (device, time, size) for the settings list.

### Tests

- [ ] Library: a console host and a test that sends text, an image and a tool round through
      `Microsoft.Extensions.AI.OpenAI` and compares with calling `LiteRtLmChatClient` directly.
- [ ] End to end: the Catalyst app serving Gemma 4 12B, the demo on a Pixel Fold / Galaxy Z Fold3 using it with its
      own tools (location, photos), over Wi-Fi and over Tailscale.

## 30. Background tasks and monitoring

"Every morning, summarise the weather and my reminders", "tell me when this page mentions a new release": tasks the
agent runs on a schedule, with a notification when there is something to say.

- [ ] Tasks: a description, a schedule (daily at a time, every N hours) and a condition for notifying; created by the
      model (`schedule_task` tool) or in the settings, listed there with their last run and result.
- [ ] A run: a new agent conversation with the task's description and its tools (web, weather, memories...), then a
      local notification with the answer if the condition holds; the run is saved like a chat, tapping the
      notification opens it.
- [ ] Mac Catalyst: runs while the app is open (or a login item); the model is already loaded or loads for the run.
- [ ] Phones: Android WorkManager / iOS `BGTaskScheduler` only give short, occasional runs, and loading a 2.6 GB model
      on a phone's CPU takes most of that - so run tasks on the Mac when a phone is paired with it (item 29), or
      limit phones to light tasks while charging.
- [ ] Limits: at most a few runs a day per task; web tasks only with Web search allowed; nothing that changes things
      outside the app without approval.

## 31. Image generation on iOS — implemented; untested on an iPhone

- [x] On iOS `Emgu.LiteRT` loads `LiteRt.framework`, which `tfliteextern` and LiteRT-LM are linked to (item 36). (At
      first it used the LiteRT inside LiteRT-LM's `CLiteRTLM`, so it needed the LiteRT-LM add-on and iOS 26.4.)
- [x] The demo offers "Image generation" on iOS; the model folder (Documents/Z-Image-Turbo) is excluded from iCloud
      backups.
- [x] Verified in an iPhone 18 Pro simulator (iOS 27): pictures drawn directly and by Gemma 4 E2B through the chat.
- [x] Mono's interpreter (iOS Debug builds) crashed at the second picture; the demo now compiles iOS Debug builds
      ahead of time (`UseInterpreter=false`), which needed the `System.Drawing.Common` exclusion on iOS too.
- [ ] Speed: ~205-225 s per picture in the simulator, about 10x the Mac's CPU with the caches in use; measure on an
      iPhone (if it is as slow, check which XNNPACK kernels the iOS build uses).
- [ ] Memory and storage on an iPhone: the text encoder maps 3.5 GB; ~21 GB with the caches. Consider the
      `com.apple.developer.kernel.increased-memory-limit` entitlement if the app is killed.
- [x] A standalone LiteRT framework, so `Emgu.LiteRT` works without the LiteRT-LM add-on - item 36.

## 32. Live camera tools — first version implemented

- [x] `open_camera` opens a visible in-app preview; `capture_camera_frame` opens it if needed and returns a fresh image
      for Gemma to describe. Available to image-capable agent models without Web search or a Tavily key.
- [x] Emgu CV captures and encodes frames on a worker; bounded preview updates, Android color conversion and sensor/display
      orientation, draggable preview and Close control. Closing, changing chats, leaving the page or backgrounding stops capture.
- [x] Show captured snapshots in the transcript and save them with regular chats; private snapshots remain in session memory.
- [x] The LiteRT-LM adapter handles image tool results as real media and batches appended turns to preserve the conversation
      cache. Regression tests cover bytes, cancellation, failed results and history; native Gemma tests describe a fixed image
      twice and verify cache reuse. Existing text-tool and private-chat tests pass too (10 tests total).
- [x] Pixel Fold: live color preview, actual Gemma 4 E2B GPU camera descriptions, repeated fresh capture, close/reopen and
      background release verified. Normal Debug build deployed after removing the temporary private-session probe.
- [x] Replace per-frame JPEG/MAUI ImageSource reloads with native raw-pixel rendering, following Emgu CV's CvImageView
      bitmap buffers on Android. Reuse pending/displayed Mats with one queued UI update; encode JPEG only on capture.
      Idle 10-second Pixel Fold comparison: 88 vs 281 captured preview frames, 196/585 vs 0/597 blank display samples;
      process managed allocations ~24.1 vs ~4.2 MB. Requested JPEG capture and close passed both runs. This is a short
      preview-only comparison, not a sustained-inference benchmark. Mac Catalyst build checked; no webcam on this Mac.
- [x] Android arm64 and Mac Catalyst builds pass. CPU camera opening works, but the full CPU description run was stopped
      to switch to GPU validation; responses can take tens of seconds even on the phone's GPU.
- [x] Windows webcam (Logitech StreamCam): `UnitTestCameraHardware` (`EMGU_CAMERA_TEST=1`) reads 30 FPS at 640x480 with
      correct colours and runs the COCO detector on a captured frame. In the packaged demo (a temporary self-test, removed):
      the Windows camera consent prompt appears on first use and blocks until answered; live preview rendered, 240 frames
      in 8 s, snapshot JPEG, detector ready on the CPU, chime and system speech played. Found and fixed: the default
      Media Foundation backend took 11-12 s to open this camera, near `StartAsync`'s 15 s timeout, so `LiveCamera` now
      opens DirectShow first on Windows (1.3 s) and falls back to the default.
- [ ] Real webcam check on Mac Catalyst (this Mac has no camera); iOS live capture is not enabled yet.
- [ ] Follow-ups: camera selection and YOLO detection/tracking. Continuous detector monitoring is covered by item 33;
      model image descriptions still capture only when asked. Neither feature records video.

## 33. Visual agents and event monitors — actions and snapshots implemented

- [x] Pure managed visual-agent descriptors/registry, owned timestamped BGR8 frames and typed observations. Still-image
      analysis and live analysis share adapters, with serialized model access and no model-specific event predicates.
- [x] Reusable event conditions, presence debounce/grace periods, Started/Updated/Ended episode notifications and
      independent monitor lifecycle. Generic condition/action registration supports additional detector adapters.
- [x] Session-only monitors (maximum eight), equivalent-rule suppression, one in-flight/latest pending frame at up to
      5 FPS, separate bounded action dispatcher, cancellation and stale-result rejection. No per-frame LLM calls.
- [x] COCO SSD MobileNet host adapter, local chime, capability discovery/create/list/control/image-analysis chat tools,
      status card, preparation/download progress and pause/resume/remove. Monitoring requires a visible live feed;
      hiding pauses/cancels, reopening resets confirmation, and new chats discard rules. No automatic rule persistence.
- [x] Pixel Fold: actual Gemma 4 E2B GPU request “When you see a person, play a sound” selected capability discovery
      and monitor creation; the first cold conversation took about 105 s after model load. Detector events and sound
      playback continued while Gemma generated its final response. Existing preview and fresh JPEG capture still work.
- [x] Controlled empty/surfers/empty/surfers images through the camera's detector queue produced two distinct episodes
      with one completed chime each. First Started appeared ~0.43 s after switching to people; Ended ~1.66 s after
      switching to empty. Separate single-image analysis found two people and three surfboards without creating events.
      Warm image analysis (12 calls, including decode/serialization) median 23.1 ms, max 28.1 ms. With monitoring,
      425 live capture callbacks in 16.517 s (~25.7 FPS); this is a short run, not a sustained thermal benchmark.
- [x] Device UI: Close/Open feed, explicit pause retained across reopen, resume, preview dragging, background camera
      release, and New chat clearing rules verified. Frames/observations/event history remain in memory.
- [x] 26 focused tests pass, including real Gemma capability selection/monitor creation and repeated camera-image
      description/cache reuse. Synthetic-clock tests cover presence timing, unknown gaps, stale work, cancellation,
      bounded frame ownership, serialized model access, faults, duplicate rules and tool contracts.
- [x] Framework builds for netstandard2.0; Android arm64 and Mac Catalyst builds pass. Normal Android Debug app
      deployed to Pixel Fold after removing temporary diagnostic code and device markers.
- [x] V2 action descriptors/factories and `list_visual_actions`; explicit action-array schema generated from the
      available registry. Whole-set validation before rule/camera side effects, one of each type, immutable normalized
      parameters and order-independent equivalent-rule suppression. Legacy single-action APIs remain available.
- [x] Bundled chime and fixed-text system speech (up to 200 characters), selected independently or together. Event
      groups stay FIFO while siblings run concurrently and report independent outcomes. Hide/pause cancels the group.
- [x] Alert audio interrupts chat read-aloud and awaits cleanup; interrupted replies are not restarted. New reply
      speech waits for alerts, retaining only the latest pending reply. Explicit alerts ignore the read-aloud toggle.
- [x] Pixel Fold: production JSON tool binding created a combined rule, controlled empty/person frames through the
      live camera queue triggered overlapping chime/speech with both succeeding, and Close cancelled both during
      the next episode. Native Android TTS/MediaPlayer initialized successfully. Mac camera/audio and Windows
      hardware remain untested.
- [x] V2: 37 focused tests pass, including real Gemma sound-only/speech-only/combined action selection, existing
      camera image regression, concurrent sibling execution, independent failures, FIFO groups, validation and
      audio interruption/cleanup/latest-pending-reply behavior. Pixel Fold confirmed chat speech interruption.
- [x] `take_snapshot` alert action: one fresh live-camera JPEG after confirmed appearance, independently or alongside
      sound/speech. Session-only transcript thumbnails (latest twenty), enlarged viewer and explicit JPEG Save.
      Images are not automatically saved/exported or sent to the model; New chat clears them, including private chats.
      Chat and alert capture requests serialize on the existing camera. Snapshot-only groups do not interrupt audio.
- [x] 43 focused tests pass, including real Gemma snapshot-only/all-three selection, snapshot factory validation,
      single-episode dispatch, cancellation, independent failures and audio independence. Pixel Fold verified a valid
      480x640 JPEG, viewer opening, three concurrent capture requests, twenty-image limit and New chat cleanup;
      the model's per-turn camera input stayed empty. Temporary probes were removed after validation.
- [x] Pixel Fold full chat workflow: Gemma 4 E2B GPU, explicit 8192-token context and Thinking off, fresh normal
      chat with the current tool/settings set. Exact request “when you see a person, play a sound and take a picture”
      finished in 39.66 s after GPU load (65.57 s), selected only `play_sound` + `take_snapshot`, and acknowledged
      without a context-capacity error. Controlled person detection completed both actions and showed one snapshot.
      Temporary test chat, probe code and markers removed; saved context/thinking preferences were unchanged.
      This verifies the workaround; automatic token budgeting/within-tool-loop recovery still needs improvement.
- [x] Pixel Fold comparison: same exact request, fresh normal chats, Gemma 4 E2B GPU with 4096 tokens and Thinking
      on, tested twice. Requests ended after 85.72 s / 88.56 s with detector/action discovery only, an empty answer
      and no monitor; neither run exposed the user's exact remaining-capacity error. This configuration failed the
      workflow in both runs, compared with successful 8k/Thinking-off. The empty-result cause is not yet established.
      Both test chats and temporary probes/markers removed; saved preferences unchanged.
- [ ] Additional adapters: YOLO, face detection, OCR and plate detection/reading; tracking individuals, recurring
      alerts, saved rules, richer conditions, and background monitoring remain future work.
- [x] Windows hardware: camera preview, snapshot, detector on a live frame, chime and spoken alert all ran in the packaged
      demo (see item 32). The chat-driven flow with Gemma (person alert created from a request) passed as tests on
      Windows (`GemmaSelectsVisualMonitorForPersonAlert`, camera-image description) but wasn't driven through the UI.
- [ ] Mac Catalyst camera/audio hardware, the full Windows chat UI flow, and long-running thermal/battery tests.

## 34. Visual-alert context exhaustion and empty replies — fixed; long-session follow-ups remain

- [x] Fix the full chat/tool workflow on Pixel Fold with **Gemma 4 E2B GPU**, **4k (4096-token) context** and
      **Thinking on**. Track both the reported prefill-capacity exception and the reproduced empty-reply failure;
      they may have different causes. Do not assume that increasing context alone fixes the underlying behavior.

**Primary reproducer (reported by the user and tested on-device):**

> when you see a person, play a sound and take a picture

Start a New chat, select Gemma 4 E2B GPU, set the context window to 4k and turn Thinking on. Send the prompt
through the normal chat UI with the production instructions/tool catalog. Also repeat in an existing conversation
near its context limit. Record enabled tool/settings differences, history size and effective loaded context size.

**Observed failures (2026-10-16):**

- User report: lengthy thinking, followed by `FAILED_PRECONDITION: Prefill input length exceeds available state
  entries (remaining capacity: 224)`.
- Two fresh-chat device runs at 4k/Thinking-on: ended after **85.72 s** and **88.56 s** after calling
  `list_visual_agents` and `list_visual_actions`, with **an empty reply and no monitor**. No capacity exception
  surfaced in these runs; the cause was subsequently confirmed by token diagnostics below.
- Control: the identical prompt at 8k (8192 tokens)/Thinking-off completed in **39.66 s**, selected exactly
  `play_sound` and `take_snapshot`, and acknowledged. Controlled person detection completed both actions and
  displayed one snapshot. Timing excludes model load. This control changes two variables; isolate context size
  and Thinking independently before attributing the difference to either one.

**Related prompts to include in regression coverage** (not yet confirmed to trigger the 4k/Thinking-on failure):

> When you see a person, play a sound.
>
> When you see a person, take a snapshot.
>
> When you see a person, take a snapshot, play a sound and say 'Person detected'.

- [x] Instrument token use and native completion/stop reasons across each model/tool round: production system
      instructions, schemas, history, thinking, tool calls/results and remaining cache capacity. Investigate why
      discovery can end in an apparently successful empty response without monitor creation.
- [x] Replace fixed tool-overhead estimates with budgeting based on the actual request and reserve room for
      intermediate tool results, thinking and the final answer. The host still uses an early history-summary estimate; the native adapter enforces per-round rendered-input,
      cache-occupancy and output/thinking budgets.
- [x] Handle capacity exhaustion within the tool loop, including first-turn requests with no history to compact.
      Keep retries bounded and preserve/deduplicate already completed tool side effects; never create duplicate
      monitors or replay alerts. Report a clear, actionable message if the requested workflow cannot fit.
- [x] Preserve the requested Thinking/context settings; do not silently disable Thinking. Detect incomplete/empty
      agent results and avoid presenting them as a completed request. Check native stop/length reporting in
      `LiteRtLmChatClient`: available native cache/benchmark evidence now distinguishes token-limit completion.
      Unknown empty completion is surfaced as actionable failure rather than silently succeeding.
- [x] Test the full production tool catalog/instructions, not only the smaller visual-tool fixture. Cover
      4k/Thinking-on, 4k/Thinking-off, 8k/Thinking-on and 8k/Thinking-off, fresh and near-full chats, and repeated
      requests. New production-catalog tests cover the primary prompt and the three-action variant; older-history
      compaction, current tool/media retention and one-time side effects have dedicated tests.
- [x] Acceptance: the primary prompt creates one monitor containing exactly `play_sound` + `take_snapshot`,
      returns a nonempty acknowledgement with actual status, and both actions execute on confirmed presence.
      No raw prefill exception, silent empty answer, duplicate rule or replayed side effect. Verify on Pixel Fold
      GPU with the failing configuration; retain a regression test and record any explicit capacity limitation.

**Implementation and validation (2026-10-16):**

- Baseline diagnostics confirmed the empty-answer run ended at exactly **4096 cached tokens**, after discovery,
  with no text/tool call. LiteRT-LM can finish a max-token run normally; the old adapter reported Stop.
- `LiteRtLmChatClient.EnableContextManagement` is enabled in the demo. It reads the loaded engine capacity,
  native cache count and rendered template; estimates payload/media costs without JSON-escaping inflation;
  reserves 64 safety tokens; and applies adaptive output and positive Thinking budgets (at most 256 per round).
- Compaction runs between model/tool rounds, removes completed reasoning and, when necessary, whole older
  exchanges. It retains the current request and all its tool/media results. The adapter does not execute tools;
  the host's whole-agent context-error retry is disabled so completed side effects are not replayed.
- Monitor creation returns concise configuration/status facts; full snapshots remain available in the UI/list tool.
  Core adapter capacity/empty errors provide guidance. Native benchmark counts help distinguish length limits.
- Full chat UI on Pixel Fold, Gemma 4 E2B GPU, fresh normal chats, exact primary prompt:

  | Context | Thinking | Response time | Result |
  |---|---|---:|---|
  | 4096 | On | 88.12 s | One sound + snapshot monitor, nonempty acknowledgement |
  | 4096 | Off | 37.29 s | One sound + snapshot monitor, nonempty acknowledgement |
  | 8192 | On | 87.82 s | One sound + snapshot monitor, nonempty acknowledgement |
  | 8192 | Off | 36.06 s | One sound + snapshot monitor, nonempty acknowledgement |

  Times exclude model loading and are single runs, not a sustained thermal benchmark. All monitors reached
  Watching. The 4k/Thinking-on case confirmed both actions succeeding on a controlled person-detection episode.
  Two snapshots were present after an initial detection and a controlled absence/reappearance (distinct episodes).
  Saved context/Thinking preferences were unchanged; test chats and temporary probe files were removed.
- Planner text and visual catalog metadata are shared with the permanent test fixture, avoiding shortened mock
  instructions/schemas. Tests cover loaded-settings overrides, output-length reporting and oversized-current-input
  rejection without tool execution. Other camera/media and visual-action regressions are retained.
- 55 focused tests passed, including real Gemma, full default planner metadata, native length reporting and camera
  image/cache reuse. After final ownership cleanup, all 11 targeted context/camera checks passed again.
  netstandard2.0 and Mac Catalyst builds passed. Temporary device probes and markers were removed.
- [ ] Follow up with extended, near-capacity Pixel Fold conversations, user setting/tool combinations, thermal/battery
      runs and Mac/Windows hardware checks. Essential requests/catalogs can still exceed 4k; report that limitation
      rather than silently disabling Thinking, truncating current evidence or repeating actions.


### 35. COCO CompiledModel GPU accuracy and device performance

**Status:** CompiledModel migration implemented; Pixel Fold GPU rejected by startup accuracy check (2026-10-16).

- Both the photo demo and `CocoVisualAgent` now share `CocoDetector`, using
  `Emgu.LiteRT.Models.CocoSsdMobilenetV3`, locked `TensorBuffer`s and `CompiledModel.Run`.
  Resize precedes BGR-to-RGB conversion; the photo path now also supplies the model's expected RGB pixels.
- CPU thread settings are passed through `CompiledImageModel.NumThreads` / `Options.AddCpuOptions`.
  Live analysis uses at most two threads; the photo demo uses the app setting.
- Android/OpenCL and Mac Catalyst/Metal try `Gpu | Cpu`. GPU-only compilation fails because the custom
  `TFLite_Detection_PostProcess` operation requires CPU fallback. Pixel Fold logs assigned **164/165 operations
  to GPU**, with the final post-processing operation on CPU.
- GPU execution compiles, but `surfers.jpg` scores are incorrect: leading scores about **0.247/0.161** versus
  CPU **0.627/0.597**, and no detections reach the 0.5 threshold. FP32 gives about **0.242/0.159**; changing
  `AllowSrcQuantizedFcConvOps` does not correct it. The underlying GPU/runtime cause remains unconfirmed.
- The host qualifies GPU execution against compiled CPU on bundled `surfers.jpg`: equal detection counts,
  matched classes, score and box differences at most 0.05, permitting reordered detections. A failed check,
  compilation or execution failure releases that GPU candidate and falls back to compiled CPU. This is a
  startup sanity check, not exhaustive numerical validation. Cancellation does not initiate fallback.
- GPU qualification also requires a speed win after accuracy passes: three warmups per backend, twelve
  timed runs each, alternating CPU/GPU order, using median full detection time on a 640×480 version of
  the bundled image. Includes preprocessing, transfers, inference and results; excludes compilation/decode.
  Ties, slower GPU timing and invalid timing values keep CPU. Cancellation is checked between native calls.
  This is a startup/load-dependent comparison, not a sustained benchmark; inaccurate GPUs skip speed testing.
  Eight deterministic timing-policy regressions cover faster/slower GPU, ties, invalid medians, warmups,
  alternating order, outlier resistance and cancellation; these and five native model parity checks passed.
- The standalone photo demo offers CPU or GPU with CPU fallback. Monitor/photo results show the selected
  backend and GPU failure/fallback; detector selection is independent of Gemma's backend.
- Early warmed inference-only samples on Pixel Fold (same uint8 model and RGB input, two CPU threads,
  five warmups, thirty timed iterations): Interpreter/NNAPI median **10.6–10.7 ms**, compiled CPU median
  **7.4–9.9 ms**, matching detections. GPU mixed execution medians **16.8–24.3 ms**, inaccurate results.
  These are short samples, not a sustained foreground/thermal benchmark. Later device sleeping/background
  scheduling inflated both CPU measurements to roughly 100–130 ms; those runs do not establish a speedup.
- Production checks confirmed CPU fallback after the GPU accuracy failure, two people recognized by the
  visual adapter with padded input stride, and correct photo-page labels/rendering. The optimized 640×480
  path returned about **9–14 ms** in awake-device samples, including preprocessing and result extraction.
  Final short 640×480 comparison: old preprocessing + Interpreter median **13.37 ms**, compiled CPU
  path **12.87 ms** (explicit CPU) / **13.84 ms** (after rejecting GPU). Inference-only medians were
  **10.68 ms** versus **7.41 ms**. Total latency therefore does not show a reliable material improvement.
- Compiled-model parity and visual-monitor/production-contract regressions passed: **38 tests**, one
  opt-in Gemma test skipped. Mac Catalyst and Android builds passed; phone camera hardware is not needed
  for these controlled image/frame tests.
- [ ] Isolate the uint8 OpenCL GPU numerical failure (input quantization/buffer layout, accelerator/runtime
      version and GPU kernels); repeat known-image score/box parity on more images before accepting GPU.
- [ ] Repeat controlled foreground, alternating CPU/Interpreter/GPU timings, including initialization,
      preprocessing, transfers, result extraction, sustained camera FPS, battery and thermals; check Metal
      on Mac Catalyst and other Android devices. Do not infer a general speedup from the short samples.

## 35. Privacy manifests for an App Store release (iOS, Mac Catalyst)

Apple asks App Store apps, and SDKs that use "required-reason" APIs, to declare those APIs and the reasons for using
them in a `PrivacyInfo.xcprivacy`. The demo has none yet, and neither do the iOS frameworks it embeds
(`LiteRt`, `tfliteextern`, `LiteRtLm` and `GemmaModelConstraintProvider`, item 36).

- [ ] Check which required-reason APIs the app and its frameworks use: `nm -u` on each framework and the app binary
      for file timestamps (`stat`/`fstat`/`getattrlist`, `NSFileModificationDate`), disk space (`statfs`,
      `NSFileSystemFreeSize`), system boot time (`systemUptime`), user defaults (`NSUserDefaults`) and active
      keyboards. The demo itself uses `Preferences` (user defaults) and file modification times (`DownloadableFile`'s
      `.sha256ok` markers); Microsoft's guidance for .NET MAUI apps also lists the .NET runtime's file timestamp,
      boot time and disk space uses.
- [ ] Add a `PrivacyInfo.xcprivacy` to the demo (`Platforms/iOS`, and Mac Catalyst's, as a bundle resource) declaring
      those APIs with their reason codes. Decide whether the frameworks get their own manifests (needed if they are
      shipped to other apps through the NuGet packages): the runtime packages could carry one per framework.
- [ ] Data collection (`NSPrivacyCollectedDataTypes`) and the App Store privacy labels: the models run on the device
      and chats stay local, but the web tools send queries (and, with location shared, the place) to Open-Meteo,
      Wikipedia, Frankfurter and Tavily; none track the user (`NSPrivacyTracking` false).
- [ ] Confirm by uploading a Release build to App Store Connect (or `xcrun altool --validate-app`) and fixing what
      validation reports - also the embedded frameworks' `Info.plist` and `MinimumOSVersion` (the Gemma library needs
      iOS 26.4).

## 36. Split the iOS natives into frameworks — implemented; verified in the simulator

The iOS natives are now frameworks that depend on each other, like the Android/macOS libraries: a base
`LiteRt.framework`, with `tfliteextern.framework` and LiteRT-LM's `LiteRtLm.framework` linked to it instead of
carrying their own LiteRT. Before, `tfliteextern` was a static library with its own TFLite, and `Emgu.LiteRT` used the
LiteRT inside LiteRT-LM's `CLiteRTLM` (item 31).

- Why: `Emgu.LiteRT` (CompiledModel, `Emgu.LiteRT.Models`, `ZImageTurbo`) and TF Lite work on iOS without the
  LiteRT-LM add-on and iOS 26.4 (the base frameworks target iOS 15); TF Lite and LiteRT share one runtime; and the
  packages match Android's (base runtime with LiteRT, LM add-on with only LiteRT-LM + the Gemma library).
- Not for size: TFLite's kernels, XNNPACK and much of LiteRT's runtime stay inside LiteRT-LM (its C++ code calls them;
  only 34 `LiteRt*` functions come from `LiteRt.framework`). Per device slice: `LiteRt` 6.5 MB + `tfliteextern`
  0.1 MB + `LiteRtLm` 27.0 MB, vs `CLiteRTLM` 28.3 MB with everything.
- [x] `platforms/ios/bazel_build_tflite_ios_frameworks` (also run by `bazel_build_xcframework`) builds the macOS
      targets `//tfliteextern:litert_runtime` / `:tfliteextern_dylib` for `ios_arm64` and `ios_sim_arm64` and wraps
      them into `lib/ios/LiteRt.xcframework` and `lib/ios/tfliteextern.xcframework` (`platforms/ios/ios_frameworks.sh`).
- [x] `platforms/ios/bazel_build_litert_lm_ios` builds `//c:litert-lm` in LiteRT-LM's dynamic mode, linked to
      `LiteRt.framework` (`--linkopt`: LiteRT's own iOS `libLiteRt.dylib` is a macOS binary, so it was skipped and the
      `LiteRt*` imports left to `-undefined dynamic_lookup`), into `lib/ios/LiteRtLm.xcframework`.
- [x] A check that every imported symbol is bound to a library (LiteRT-LM's `.bazelrc` allows undefined symbols on a
      macOS host); it found TFLite's iOS signpost profiler missing from `libLiteRt`, now linked in.
- [x] DllImports: `@rpath/LiteRt.framework/LiteRt`, `@rpath/tfliteextern.framework/tfliteextern`,
      `@rpath/LiteRtLm.framework/LiteRtLm` on iOS (Mac Catalyst and Unity iOS keep `__Internal`). The base
      `Emgu.LiteRT.runtime.maui.ios` embeds `LiteRt` + `tfliteextern`, the LM add-on `LiteRtLm` + the Gemma library.
- [x] Verified in an iPhone 18 Pro simulator (iOS 27), one process: TF Lite Interpreter and LiteRT CompiledModel
      MobileNet (space shuttle, 1.0), Gemma 4 E2B ("Paris"), Z-Image (the fox, 225 s), one `LiteRt.framework` loaded.
- [x] An app that references only the base runtime (no LM add-on): a plain .NET iOS app in the simulator ran TF Lite
      and CompiledModel MobileNet (space shuttle, 1.0) with only `LiteRt` and `tfliteextern` in its bundle. (The
      simulator runs iOS 27, so the iOS 15 minimum itself is untested.)
- [ ] A real iPhone, and App Store validation of the frameworks (item 35).
