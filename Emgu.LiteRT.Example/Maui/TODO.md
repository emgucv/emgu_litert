# Chat page TODO

Planned work on the MAUI demo's LiteRT-LM chat page (`LiteRtLmChatPage*.cs`). Only open work is listed; finished work, with how it was done and verified, is in [history/TODO-done.md](history/TODO-done.md) under the same item numbers.

Sorted by priority. First what every model depends on - resumable downloads and showing what is downloaded - and
testing what was built but never run. Then small, visible fixes (the Model section, the app icon), and safety nets
that make the rest less risky (model-free tests in CI, an agent regression test). Then storage and the platform: a
Storage page, the next LiteRT-LM release, and GPU follow-ups on platforms not yet verified,
the Pixel Fold freeze, being gentle with phones, background downloads. Then chat features and new agent tools,
smallest and most useful first - with a hands-free voice mode and a task mode before the tools that act outside the
app - and last the largest: other system models, MCP, serving the Mac's models to phones, background tasks (which work
best through that server), image generation on iOS, the privacy manifests an App Store release needs, and
splitting the iOS natives into frameworks.

## Finished items

Their full write-ups are in [history/TODO-done.md](history/TODO-done.md):

- 1. Resume partial downloads — completed (.NET / MAUI)
- 2. Show which models are downloaded
- 6. Run the model-free tests in CI — completed
- 7. An agent regression test — implemented
- 8. A Storage page in settings
- 10. GPU on Windows — core support completed; follow-ups remain
- 15. Agent tool: search documents (`search_documents`)
- 16. Agent tool: search past chats (`search_chats`)
- 19. A private chat
- 43. (Notes from the feasibility checks, before it was built)
- 50. A shutter click for photos taken by an alert

## 3. Test the paths that were never run — partially verified

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Windows image generation; first-run cache preparation with Gemma resident and interactive image saving
      remain additional device checks.
- [ ] Recording a voice message live (`VoiceRecorder.cs`: AVAudioRecorder, Android AudioRecord, Windows MediaCapture) -
      Android capture/model input passed; Mac Catalyst and Windows live capture remain pending.
- [ ] Windows: the PDF page renderer (`Emgu.LiteRT.LM.Maui`'s `PdfPageRenderer`, `Windows.Data.Pdf`) and the photo library (`StorageFile`
      details).
- [ ] Photo search on a real phone's library (only the emulator's test photos so far), on a phone whose owner agrees
      to have their photos indexed for the test.
- [ ] Mac live recording reached the OS microphone permission request; no completed capture is claimed.
- [ ] Real-library photo indexing awaits owner agreement; Windows runtime checks await a Windows device/VM.

## 4. Collapsible "Model" section in settings — implemented; foldable UI check pending

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Foldable inner-screen UI check (Z Fold3 installed; initially locked, then wireless debugging disconnected).

## 5. A new app icon — implemented; Windows visual check remains

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Windows taskbar and Start menu visual check on a Windows machine.

## 9. Update to the next LiteRT-LM release

LiteRT-LM v0.18.0 is the current pin. The next release brings upstream fixes and unblocks the embedding models' GPU
(item 11: its `main` already pins a LiteRT with `dd3db3123`).

**v0.18.1 (tagged 2026-10-20) was skipped: it doesn't build.** It changes no LiteRT-LM source, only appends a second
`LITERT_REF` (`ca21dc99`, which contains `dd3db3123`) and `TENSORFLOW_REF` (`a1179d3c`) to `WORKSPACE`, and that
TensorFlow loads `compatibility_proxy_repo` from `@rules_cc//cc:extensions.bzl`, which the WORKSPACE's `rules_cc`
lacks - the failure the WORKSPACE's own comment above the first `TENSORFLOW_REF` warns about. Our macOS build and all six
of Google's CI workflows on the tag failed with it. `main` fixed it by pinning `rules_cc` 1.9.0 (CI-Mac green on
`25016d6e`, 2026-10-23), so wait for the next tag (v0.18.2 or later). Found while checking v0.18.1, still true for
the next release:
- LiteRT renamed the CPU accelerator's options identifier from `"xnnpack"` to `"cpu_options"` (`ca21dc99`); each
  runtime ignores the other's. `Emgu.LiteRT`'s `Options.AddCpuOptions` already sends the payload under both, so thread
  counts and the XNNPACK weight cache (Z-Image-Turbo) keep working - checked with v0.18.0's `libLiteRt` (the weight
  cache is written). The GPU options keys and identifier (`"gpu_options"`) are unchanged.
- `ca21dc99` only adds exports (12: delegation metrics, local response normalization options), so the checked-in
  `litert_windows_exports.def` still links; regenerate it on Windows (`generate_litert_windows_exports.ps1`) to
  export them. All 39 functions `Emgu.LiteRT` imports are still there.

- [ ] Move `LiteRT-LM` to the release and `litert` to its `LITERT_REF` (CLAUDE.md, "The `litert` submodule must match
      LiteRT-LM's LiteRT version").
- [ ] Rebuild the natives for macOS, Mac Catalyst, iOS, Android (all four ABIs) and Windows; check the Windows export
      list (`litert_windows_exports.def`) and the C API wrappers against header changes.
- [ ] Re-run the model tests, check every model file in the demo still loads and answers correctly (some community files
      broke with v0.18.0), and the agent regression test (item 7).

## 11. GPU for the embedding models — Windows completed; Apple/Android pending

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Update LiteRT-LM/LiteRT together for matching prebuilt Metal/OpenCL accelerators (item 9); rebuild Apple/Android.
- [ ] Verify CPU/GPU text/image/audio embeddings and search on Mac and a real Android phone before enabling them.
- [ ] Verify simultaneous chat/search GPU memory use and indexing performance on phones.

## 12. Pixel Fold freeze — rendering stalls fixed; original ANR unconfirmed

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Extended manual hinge testing and full native image-generation stress (model absent; only ~22 GB free, near
      the model/cache requirement). Shell fold-state transitions do not cover all physical hinge behavior.
- [ ] Capture a matching trace if the original dialog recurs. Exit history also has un-attributed non-user-perceptible
      job-start ANR annotations without a retrievable trace; the rendering fix does not prove those resolved.

## 13. Be gentle with phones — implemented

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Extended real-device indexing/generation checks under heat and low battery; iOS battery APIs need a physical device.

## 14. Downloads that continue in the background — implemented with bounded Apple completion time

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Extended screen-off / battery-policy checks on physical Android and iOS devices.

## 17. Image generation extras — implemented; verified on Mac Catalyst (draw again) and a Galaxy S10 (gallery)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Pictures made before this have no description: the sheet shows only the picture and the buttons (without "Draw it again").
- [ ] Try the Photos, Share and Save buttons by hand on an iPhone / Windows; the share sheet and the Photos permission prompt weren't driven.

## 18. Agent tool: create documents (`create_document`) — implemented; verified on Mac Catalyst with Gemma 4 E2B

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Open and Share not yet tapped on a device (Mac Catalyst opens Pages/Preview, Android the user's apps).
- [ ] Long content: the model writes it in its reply budget (1,024 tokens by default in `LiteRtLmChatClient`); for
      longer documents, ask it to write section by section.

## 20. Export or share a chat

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Optionally import it back.

## 21. Accessibility

- [ ] Screen-reader labels for the newer controls: image tiles, step lines, thinking headers, the settings switches and
      download states, photo thumbnails (`SemanticProperties`).
- [ ] Large text: check the chat, settings and sheets with the largest font sizes on Android and iOS.
- [ ] Contrast of the secondary text in light and dark mode.

## 22. Hands-free voice conversation — implemented; verified on a Pixel Fold and on Mac Catalyst

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Speech-to-text on Mac Catalyst and an iPhone; Windows has none (its recorder has no level for the voice chat
      either, but voice messages could use `Windows.Media.SpeechRecognition`).
- [ ] An iPhone with a real microphone, and any device with headphones (speaking over the reply).
- [ ] Headphone detection (`VoiceRecorder.HeadphonesConnected`: the audio route on iOS/Mac Catalyst, output devices
      on Android) is untested; so is recording while text-to-speech plays on iOS.
- [ ] Windows has no speech recognizer for this; an option to not keep the audio (only the words); the transcript isn't in the chat search.
- [ ] An automatic mode (Android 13+ `EXTRA_ENABLE_LANGUAGE_DETECTION` with the languages the user speaks); the language pack download isn't shown to the user
      (only the message to try again); the voice chat (hands-free) uses the same setting but has no way to change it on its full-screen view.

## 23. Task mode: multi-step tasks with a visible plan — implemented; verified on Mac Catalyst with Gemma 4 E2B

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [x] The plan card is saved with the chat (2026-10-27): kept with the reply as a saved file of kind `task_plan` (JSON: the steps with their state and result, and how it ended) when the card closes, and shown again, closed, when the chat is reopened (`TaskFeature.RestoreSavedFile`).
- [ ] The steps don't see the earlier chat (only the request): "plan the same trip but cheaper" has no "same trip" to go on.
- [ ] Proposed by the model for long requests (now only the chip); the planner's steps are sometimes redundant (a "review" step at the end).
- [ ] Try it on phones (E2B on a phone's 8k window with the tool definitions: the step prompts are ~1-1.5k tokens) and with a server model.

## 24. Agent tools: device actions, confirmed by the user — implemented; Android verified, iOS/Mac Catalyst untested

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] iOS / Mac Catalyst: EventKit events and reminders, Apple Maps (built, not run; the first use asks for access).
- [ ] Alarms more than 24 hours ahead (a calendar event is suggested instead); timers/alarms on iOS (no public API).

## 25. Goals and check-ins — goals implemented and verified on Android; check-ins pending

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Gemma 4 E4B.
- [ ] Check-ins: a reminder (device actions, item 24 - only iOS/Mac Catalyst have reminders) or a scheduled task
      (item 30) asks how it went; e.g. a local notification on the evening of a day a goal is behind.

## 26. Agent tool: clipboard (`read_clipboard`) — implemented

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Android and iOS: Android 12+ and iOS tell the user that the app read the clipboard (a toast / the paste banner); not tried on a phone.

## 27. More models: Gemini Nano and Apple's on-device model

System models, offered in the model picker only where the device has them (detected at run time):

- [ ] Gemini Nano through ML Kit GenAI's Prompt API (Android: Pixel 9/10, Galaxy Z Fold7 and other recent flagships;
      not the emulator, the Pixel Fold or the Z Fold3): a .NET for Android binding of `com.google.mlkit:genai-prompt`,
      wrapped as an `IChatClient`; check whether it supports tool calling.
- [ ] Apple's Foundation Models through `Microsoft.Maui.Essentials.AI` (an `IChatClient`), once the repo moves to
      .NET 11 (iOS / Mac Catalyst with Apple Intelligence).

## 28. MCP support — implemented (HTTP verified with DeepWiki on a Galaxy S10; stdio untested)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] stdio on Mac Catalyst and Windows is untested (a program such as `npx -y <server>`); OAuth for servers that need it (the SDK has
      a client for it) - only a static header now; resources and prompts of servers aren't used, only tools.
- [x] A reusable package: `Emgu.LiteRT.LM.Agents.Mcp` (the servers as agent tools) and `Emgu.LiteRT.LM.Maui.Mcp` (`McpFeature` for any `ChatPage`, phase C, 2026-10-26).

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

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Speed: ~205-225 s per picture in the simulator, about 10x the Mac's CPU with the caches in use; measure on an
      iPhone (if it is as slow, check which XNNPACK kernels the iOS build uses).
- [ ] Memory and storage on an iPhone: the text encoder maps 3.5 GB; ~21 GB with the caches. Consider the
      `com.apple.developer.kernel.increased-memory-limit` entitlement if the app is killed.

## 32. Live camera tools — first version implemented

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Real webcam check on Mac Catalyst (this Mac has no camera); iOS live capture is not enabled yet.
- [ ] Follow-ups: camera selection and YOLO detection/tracking. Continuous detector monitoring is covered by item 33;
      model image descriptions still capture only when asked. Neither feature records video.

## 33. Visual agents and event monitors — actions and snapshots implemented

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Additional adapters: YOLO, face detection, OCR and plate detection/reading; tracking individuals, recurring
      alerts, saved rules, richer conditions, and background monitoring remain future work.
- [ ] Mac Catalyst camera/audio hardware, the full Windows chat UI flow, and long-running thermal/battery tests.

## 34. Visual-alert context exhaustion and empty replies — fixed; long-session follow-ups remain

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

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

### YOLOX-Nano experiment — optional detector verified

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] YOLOX quantization, COCO validation and sustained camera/memory/thermal comparison (GPU alongside a loaded Gemma too);
      live camera plus Gemma selecting YOLOX in chat remains an end-to-end check.
      [Usage, provenance, reproduction and results](../../Emgu.LiteRT.Models/YOLOX_EVALUATION.md).

### D-FINE-N — converts and matches; a Mac Catalyst photo detector only

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Try the page in the Catalyst app (the sandbox build failed at code signing, so only the tests ran); find what blocks the
      GPU compile (GridSample/attention?) before offering it on phones.

### PicoDet-S — converts, matches, runs on the GPU; a "PicoDet S" photo page

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Try the page in the Catalyst app and on the phones' UI (only the detector and tests were run); a visual agent entry; GPU
      speed on the Fold looks no better than YOLOX's, so a visual agent is optional.
- [ ] Fold CPU timing was measured only after the first (locked-screen) run was discarded; re-measure if the numbers matter.

### MobileNet V5 evaluation — encoder verified; detector checkpoint needed

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Obtain/train a V5 detection head before attempting LiteRT export and a same-device V3/V5 detection comparison.

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

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] A real iPhone, and App Store validation of the frameworks (item 35).

## 37. Tool groups on demand — implemented; verified with Gemma 4 E2B on a Mac and a Galaxy S10

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Trimming the camera group's definitions (the action schema of `create_visual_monitor`, 1,600 characters): only
      with `FullDemoPlannerCatalogCreatesAlertAt4k` before and after, since they were tuned for E2B.
- [ ] Routing by meaning (EmbeddingGemma similarity to each group) in place of, or with, the keywords.
- [ ] Phones could go back to 4k for Gemma 4: on the S10's GPU, E2B at 4k answered "12 times 7", set and logged a goal
      and went through the alarm stub just as at 8k, in the same time (21-26 s vs 21-27 s). 8k only costs when a long
      chat is read again (a saved chat opened, after compacting) - slower on a phone's CPU (Pixel Fold 342 s vs 123 s),
      seconds on its GPU. Kept at 8k for now.

## 38. Agent tools: Gmail (read first, then drafts and sending)

"Do I have mail from the bank this week?", "Summarize the last email from Sam", "Reply that I'll be there at 6".
Uses the Gmail API with OAuth 2.0 (the IMAP/app-password route and a compose-only `mailto:` action were considered and left
out). Not started.

- [ ] Google Cloud project with the Gmail API enabled and OAuth clients for Android and Mac Catalyst (iOS later); the app stays
      in testing mode (up to 100 listed test users, no Google review). Publishing it needs Google's verification and, for the
      restricted read scope, a paid security assessment - decide only if this ships publicly. Check Google's current scope
      list before choosing scopes.
- [ ] Sign-in in the demo: `WebAuthenticator` with authorization code + PKCE, a custom redirect scheme per platform (Android
      intent filter, `Info.plist` URL scheme), refresh token in `SecureStorage`; Settings > "Connect Gmail" and "Disconnect"
      (revokes the token); off by default, with a note that the Gmail API call goes to Google although the model runs on the
      device.
- [ ] `Emgu.LiteRT.LM.Agents`: a small Gmail client (`HttpClient`, no Google SDK) and `AgentTools.Email(...)` with
      `search_email` (the user's words - sender, subject, dates, unread, label - turned into Gmail's `q=` syntax, like
      `PhotoRequest.Parse`; a short list of sender, subject, date) and `read_email` (HTML stripped with `HtmlText.Extract`, cut to
      about 1,500 characters so it fits the 8k window; threads: latest messages only).
- [ ] Routing: an "email" `ToolGroup` (keywords mail, email, inbox, gmail, message from...) so the tools cost nothing otherwise
      (`ToolRouter`); step lines "Searching your mail" / "Reading an email from ..."; the instructions say the model must not
      send or act on anything it didn't get from the user.
- [ ] Second phase: `draft_email` / `send_email` through the device-action Confirm / Cancel card (`CANCELLED - NOT done`
      when declined); the model never sends on its own; the send scope is added only then.
- [ ] Email bodies are untrusted text (prompt injection): label tool results as email content, no automatic forwarding or
      replying, and a test where a message tells the model to send mail.
- [ ] Tests: the query builder and message parsing model-free (a canned API response); a Gemma 4 E2B check on a test
      account (search, read, summary); the confirmation card on Android and Mac Catalyst.

## 39. The user's language: the thinking translated on request — implemented; verified on Mac Catalyst (Spanish, German, Chinese)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Translation quality isn't measured: the Spanish one was fine to read but put a stray "¿" before the quoted question ("¿Tengo 3 cajas...").
- [ ] Windows: no Latin-script detection (SearchPioneer.Lingua, Apache-2.0, 79 languages, accurate on short text, but ~71 MB of models and net8+
      only; or word lists for the main Latin-script languages) - no translation offer for Spanish or German there yet.
- [ ] Translate automatically after each reply (an option), if opening it often makes the wait a nuisance; a Settings switch "Think in my language"
      that asks for the thinking in the user's language instead - test whether the thinking gets worse (accuracy, tool calls, length) first.
- [ ] Try it on a phone (Android: the same code with ML Kit; the Catalyst run used Apple's recognizer for Spanish and German).

## 40. Hand gestures for the visual agents — prototype: MediaPipe's hand tracking on LiteRT; photo page only

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] The model files' own licence terms (the model cards) before they are bundled or hosted; now they are downloaded from Google.
- [ ] More gestures (OK, wave, rock) from the landmarks with rules, or a custom classifier on the gesture embedder.

## 41. Sound events for the visual agents — prototype: YAMNet on LiteRT; a "Sound events" page only

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] The model's licence beyond its metadata (Google has no licence file beside it), before bundling or hosting it.
- [ ] Android foreground service for listening in the background; Windows microphone source.

## 42. Sound monitors in the chat — "tell me when the doorbell rings"; YAMNet scores the microphone, no model per sound

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Tune thresholds per sound with real recordings (smoke alarm, baby, doorbell); try other phones' microphones and noisy rooms.
- [ ] Combined camera + sound conditions, a camera snapshot as a sound alert, and sound monitors listed beside the camera monitors.
- [ ] Windows microphone stream (`Emgu.LiteRT.LM.Maui`'s `MicrophoneSoundSource`, `IsSupported` false on Windows); listening in the background (Android foreground service, iOS background audio).
- [ ] Run with the camera loop and Gemma together on a phone (heat, battery) - nothing was timed together.
- [ ] Try on iOS (the `AVAudioEngine` tap and the microphone gate are built, not run).

## 43. Video clips as a visual-alert action — `record_clip` built for Android (Camera2); tested on a Galaxy S10 and a Pixel Fold

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: a real detection triggering a clip (nobody in front of the camera - the action was called directly), clips longer than 6 s, the cost next to
      Gemma generating, and a clip while the voice recorder or a sound monitor holds the microphone (the earlier test shows the microphone can be shared).
- [ ] The clip starts when the alert fires (about a second after the person appears, plus the 0.3 s reconfiguration) and the frames pause that long at
      each end. A pre-roll (the seconds before the alert) needs a ring buffer of encoded frames: an always-running MediaCodec encoder muxed with
      MediaMuxer instead of MediaRecorder; and a MediaRecorder prepared in advance would remove the start's pause.
- [ ] Other platforms: Mac Catalyst and Windows have no clip action (their cameras are OpenCV's), iOS has no live camera at all.
- [ ] A clip as the action of a sound monitor ("record a clip when the dog barks") - the sound monitors have chime and speech only.

## 44. Full-resolution photos as a visual-alert action — `take_photo` built for Android (Camera2); tested on a Galaxy S10 and a Pixel Fold

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: a real detection triggering a photo (the action was called with a made-up event), the quality of the photos themselves (not looked at: only their
      size and orientation were read), phones with other sensors, and the largest "high resolution" sizes some cameras list (e.g. 50 MP) - only the standard maximum
      (12 MP here) is used, since Camera2 only guarantees that beside a preview stream.
- [ ] A photo of the frame that triggered the alert (the photo is taken ~2 s after the alert fires, about a second after confirmation); a photo as a sound monitor's
      action; the same on Mac Catalyst and Windows (their cameras are OpenCV's).

## 45. The monitors say what they are for — the user's request on each card, and a live status on the camera preview

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: the look of a long request on a narrow screen (only the text was read, not a screenshot - the camera preview would be in it), Windows and Mac Catalyst
      (the code is shared and builds, the camera label there only changes for monitors), and the photo/snapshot status text in the label.
- [ ] The request isn't shown in the transcript's snapshot, clip and photo cards, which show the monitor's condition; the sound monitors' alerts could name their
      request too.

## 46. Clips play in the chat — `InlineVideoView` (Android's VideoView) in the clip card

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: that the picture is actually drawn and upright (no screenshot - the camera would be in it), scrolling a playing card in a long transcript (a `VideoView` is a surface
      and can draw over or behind other views while it scrolls), the camera panel overlapping a playing card, seeking by touch, and playing with sound from the clip while the
      phone's volume is low or in a call.
- [ ] A texture-based player (Media3/ExoPlayer, CommunityToolkit.Maui's MediaElement) if the surface misbehaves; seeking; the same for photos' full-screen view.

## 47. The hand-gesture visual agent — "take a picture when you see a thumb up" goes to MediaPipe's gestures

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: the hand models beside the COCO detector and Gemma on a phone, a monitor for a gesture on the S10, Mac Catalyst and Windows (the code is shared and builds), other hands, rooms
      and lighting (one person, one room), the back camera with a real hand, two hands.
- [ ] The fist and "I love you": log the classifier's raw scores for the frames it calls "none" (is the gesture second, at 0.3-0.5?) before deciding whether to take the second-best gesture when "none"
      isn't clearly ahead; MediaPipe's video mode (follow the hand from its last landmarks) so a hand that closes into a fist stays found.
- [ ] MediaPipe's video mode (the next frame's hand rect from the previous landmarks) to roughly halve the work; more gestures (OK, wave, rock) from the landmarks; two hands making one gesture
      together; the model files' licence (item 40).

## 48. The selfie camera on Android — ask for it in words, or tap Flip

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: a real face or hand in the selfie camera (only metadata was read - no frames were looked at), that the mirrored preview and the upright frames look right (no screenshot: the camera
      would be in it), phones with several front cameras or none, the camera switch while a clip or photo is being taken, and Mac Catalyst and Windows (one camera, no choice).
- [ ] An option to keep the last camera between chats; mirrored selfie photos and clips (Android's camera app mirrors selfies by default, ours are saved as the sensor sees them); asking for a
      specific lens (ultra-wide, telephoto).

## 49. A sound can take the photo or the clip — "when you hear the baby cry, take a video"

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] A pre-roll (the seconds before the cry) needs a rolling buffer of encoded frames; the clip starts after the sound is confirmed.

## 51. Body pose — a prototype: MediaPipe's pose landmarker on LiteRT; a photo page only (Emgu.LiteRT.Models/POSE.md)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] A `body-pose` visual agent and conditions (arms raised, sitting/standing, lying, hand above the head, a wave); tracking between frames; the Pixel Fold, M1 and GPU numbers; a real person on a live camera (nothing was tested on one). Not a fall detector: the model card rules out life-critical use.

## 52. A body-pose visual agent — "take a picture when you see me waving"

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not tried on a real person and live camera (no frames were looked at), the Pixel Fold, or Mac Catalyst/iOS/Windows; waves faster than ~2 Hz at 5 frames a second; tracking between frames to cut the cost; more postures.

## 53. Alert sounds — "play the lullaby when the baby cries" (`play_file`)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not done: stopping a repeated sound when the crying stops, fades, the music library or streaming, the Fold, Mac Catalyst/iOS/Windows (the same players as the chime, untried), and nobody has listened to the two built-in sounds (they were checked by their duration and levels only).

## 54. Monitoring in the background (Android foreground service)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not done: camera monitors (they still stop with the app: the live camera closes when the window stops), starting a service again after the process is killed, a battery estimate, Android 14+ (the S10 is Android 12; the service types and the start-from-foreground rule follow the documented 14 rules but weren't run on one - the Pixel Fold is Android 16, untested), and a notification with Stop/Resume for single monitors.

## 55. The demo on the library's `ChatPage` - one chat page for every app, with skills

`Emgu.LiteRT.LM.Maui`'s `ChatPage` (2026-10-26, phase A of the plan: the page, `ChatFeature`/`ChatSkill`, the built-in features, the `MauiChat` sample on it; see `ARCHITECTURE.md` and `docs/agents.md`) is what other MAUI apps drop in. The demo's `LiteRtLmChatPage` derives from it since phase B, and since phase C (2026-10-27) every feature an app would want is the library's or one of its packages'.

- [x] Phase B (done 2026-10-26, in the B1, B2.1, B2.2, B2.4, B2.5 and B2.6 commits below; the plan as written, the outcome in the lines after it): `LiteRtLmChatPage : ChatPage` - the model catalog as `ChatModelOption`s (`Choice.ToString()` as the id, so saved chats and the `model` setting carry over), `ChatStore` as the `IChatHistoryStore`, `AppSettings.Chat` behind `IChatPageSettings` (same keys), the server as `ConfigureClient`; delete `.Layout`, `.Settings`, `.ModelList`, `.Loading`, `.SavedChats`, `.Memories`, `.Private`, `.Agent`, `.Tools`, `.Keyboard` and `ChatAgentTools`; a `DemoChatHost : IChatHost` adapter over `IChatPageHost` and the demo's services (`ChatStore`, `SearchEmbedder`, camera, sound mute, workload, the background service) so `Chat/Features/*` compile unchanged, with thin `ChatFeature` wrappers for the chips (Plan, Voice), the extra settings sections, the context-window chips, CPU threads, storage and export. Check on Mac Catalyst (the side panel at 820 px, Enter to send) and the Fold: saved chats, memories and goals from the same `chats.db3`, private mode, server mode, MCP groups, the camera overlay, background recording.
- [x] Phase B1 (2026-10-26): the library grew what the demo has and any app wants - `ReloadModelAsync` with the chat kept, the GPU-to-CPU prompt, resumable downloads with Continue/Not now, the context chips, CPU threads, overlays, hands-free, remote models, and the virtuals a derived page needs (`docs/agents.md`); checked on the Fold.
- [x] Phase B2.1 (2026-10-26): the demo prepared with the old page still running - `UseLiteRtChat()`, `MenuSheet`, `ChatModelOptions.FromCatalog`, `ChatSettingsAdapter`.
- [x] Phase B2.2 (2026-10-26): the `ChatFeature` wrappers of the demo's partials, compiled but unused until the swap - `DemoFeature`, `IDemoHost`, `DemoChatHost`, `CameraFeature` (ten partials), `DocumentsFeature`, `McpFeature`, `VoiceModeFeature`, `ServerFeature`, `TranscriptFeature`, `ExportFeature`, `StorageFeature`, `ModelSettingsFeature`, `TaskChipFeature`, `DemoToolsFeature`, `WorkloadService`, `AlertAudioReplySpeaker` (`ARCHITECTURE.md`). Next: B2.3/B2.4, the swap (`LiteRtLmChatPage : ChatPage`, the template override removed, the partials deleted), then the shrink commits and the docs.
- [x] Phase B2.4 (2026-10-26): the swap - `LiteRtLmChatPage : ChatPage` in one file over the wrappers (`ARCHITECTURE.md`), the `ChatViewTemplate` and `ChatAttachmentViewTemplate` overrides gone from `ChatTemplates.xaml` (the transcript binds the item's `Transcript`), the demo's 39 page partials, `ChatAgentTools` and its `CpuThreadsView` deleted, the `Microsoft.Agents.AI` reference dropped; the app pins the light theme (the demo's sections are drawn in fixed light colours, invisible on the library's dark page). Checked on Mac Catalyst (driven by CGEvent clicks - `screencapture -o`, without the window shadow, is what maps to the window): Return sent a calculator question that Gemma 4 E2B answered on Metal with `calculate` and the exchange appeared in the chat list; a saved chat opened with its voice messages and step lines; the "+" menu attached the surfers sample and the model ran `analyze_visual_image` on it; the CPU row reloaded the model with the chat kept ("Reading the conversation so far...", then "3 surfboards" from the picture); private mode on and off. Conscious losses: the collapsible CPU/GPU model groups, the header Export button (a row action and a settings button now), "Writing down what you said..." under a voice message while it is transcribed, the CPU-threads reload of the search and image models (only the chat model reloads), the per-feature memory count line in the old place. Not yet: a voice message on the Mac (no microphone to drive). On the Pixel Fold (2026-10-26, `tools/deploy_android`, driven by uiautomator): `calculate` on the GPU (OpenCL, thought 28 s), the chats sheet with the Export actions and the server's chats named, a saved chat opened with its memory step line, the CPU row mid-chat ("Reading the conversation so far...", then the name spelled backwards), and "Open the camera" - which found a bug: the camera streamed but its panel never showed, because `CameraFeature` adds its panels in `OnAttached`, which the base constructor runs before the template is applied, so `AddOverlay` had no `PART_Overlay`; the library now queues overlays and layers added before the template (`FlushOverlays` in `OnApplyTemplate`), and the preview panel with Selfie camera / Close / Maximize showed on the retest.
- [x] Phase B2.5 (2026-10-26): the shrink - the ten feature classes that still took the old `IChatHost` (`TranslationFeature`, `DeviceActionsFeature`, `WrittenDocumentsFeature`, `GoalsFeature`, `PastChatsFeature`, `PhotoSearchFeature`, `MeetingFeature`, `ExerciseFeature`, `TaskFeature` with the Plan chip merged in, `ImageFeature`) are `DemoFeature`s with their own tool groups, sections, chips and hooks (`ARCHITECTURE.md`); `IChatHost`, `DemoChatHost`, `DemoToolsFeature`, `DemoSectionsFeature` and `TaskChipFeature` deleted, `IDemoHost` down to the store, the workload policy and the page's actions. Also fixed: the features' progress during a reply ("Step 2 of 3", "Writing the answer...") stayed on the page's status line after the reply, as the send cleared only the view's. Checked on Mac Catalyst: the settings sheet with every section in its place (the image section after the embedding model, then storage, export, goals, past chats, photos, conversation memory, developer mode), `calculate` on Metal, and a plan ("convert 5 miles to kilometres, then multiply by 3") reviewed, run - `convert_units` and `calculate` as step lines, the results in the card - and answered.
- [x] Phase B2.6 (2026-10-26): the docs - `ARCHITECTURE.md` (the page, its features, how to add one), `FEATURES.md` (the layout and agent sections, the features' file names), `README.md`, `docs/agents.md`, `CLAUDE.md`.
- [x] Phase C, goals (2026-10-26): the library's `GoalsFeature` gained Undo last (`IAgentGoalStore.DeleteLastProgressAsync`, on the SQLite and in-memory stores, tested) and registers itself as a service; `GoalsSkill.ExtraSummary` names the extra tools in the group's summary; the demo's `GoalsFeature` is deleted, `ExerciseFeature` wires its tools, words and summary into the skill in `OnAttached`, and `ChatToolGroups` keeps only the group's name. `UnitTestToolRouting` builds the group from the skill. Checked on Mac Catalyst with Gemma 4 E2B: "I want to read 20 pages a day, and I read 25 pages today" ran `add_goal` and `log_progress` through the library's group, the settings' goals sheet showed the goal, Undo last emptied its progress (the `GoalProgress` table went from 1 row to 0) and Delete removed it. It found a library bug: `ChatPage.Link(text, null)` added a tap handler that threw on the null action, silently, so the library's goals rows (Edit, Delete, and the new Undo last, which added their own handlers after it) did nothing when tapped - `Link` now skips the handler for a null action and the rows pass theirs to it.
- [x] Phase C, past chats (2026-10-26): the search_chats group is the library's `PastChatsFeature` over `Emgu.LiteRT.LM.Chat`'s new `ChatSearchIndex` and `IChatSearchStore` (which `ChatHistoryStore` implements - the `ChatEmbedding` table moved there, with the demo's `ChatStore` losing its copy) and a `SearchServices` (`SearchEmbedder` and `ISearchModelChoice` moved to the library, `IIndexingWorkload` with `DeviceIndexingWorkload`, the model's key, the deleted-model notice); the demo's `PastChatsFeature`, `SearchEmbedder` and Core's `ChatSearchIndex`, past-chats constants and chat-search instructions are gone, `WorkloadService` is the demo's `IIndexingWorkload`, and `ChatFeature` gained `OnAppResumed`/`OnAppStopped` (the page watches its window; `DemoFeature` dropped its own). Found on Catalyst: the page's workload was read through `_parts` while the base constructor built the settings, so the photo section threw - a `[ThreadStatic]` `_building` covers that window. Checked on Catalyst: the section (35 messages indexed), and a question about an earlier chat ran `search_chats` with its links card.
- [x] Phase C, documents (2026-10-26): the library's `DocumentsFeature`, in a new package `Emgu.LiteRT.LM.Maui.Documents` (so `Emgu.LiteRT.LM.Maui` doesn't depend on PdfPig; its csproj, shared items, `CMakeLists.txt`, `cmake/*.in`, the root CMake, the solution and the demo's reference), over `Emgu.LiteRT.LM.Chat`'s new `IChatDocumentStore` (which `ChatHistoryStore` implements - the `Document` and `DocumentItem` tables and the chat's `DocumentId` moved there from the demo's `ChatStore`, whose record-typed methods stay as the base's) and the `SearchServices`; `ChatFeature` gained `PrepareMessageAsync`/`RefreshMessageAsync` (`ChatMessageAddition`: a message's context, attachments and step line; the page's `PrepareRequestAsync`, `AddCardToNextReply`, `StepLine`) and `OnChatSaved`; the demo's `DocumentsFeature`, its document records and methods, constants and instructions are gone, and its `SendCoreAsync` runs the features' additions through `PrepareRequestAsync` (the image references go in the request's context first, so a refresh after compacting keeps them). Checked on Catalyst with Gemma 4 E2B: a text file attached through "+" > Document (the chip: "1 passage", "Ask about the document."), a question answered from its excerpt with the "Looked in orchard_notes.txt: 1 excerpt" step line, and the saved chat recorded the document.
- [x] Phase C, photo search (2026-10-26): `PhotoSearchFeature`, `PhotoIndex` and `PhotoLibrary` moved into `Emgu.LiteRT.LM.Maui` (as files, with their history), the place list `places.tsv.gz` as the library's embedded resource and the Android photo permissions declared by its assembly; `Emgu.LiteRT.LM.Chat` gained `IPhotoIndexStore`/`ChatPhotoEmbedding` and the `ChatPhotos` constants, `ChatHistoryStore` the `PhotoEmbedding` table (from the demo's `ChatStore`); `IIndexingWorkload` gained the `onlyWhileCharging` parameter the feature's "Index photos only while charging" setting drives, so the demo's `WorkloadService` is deleted and the page uses `DeviceIndexingWorkload` (the old charging key is read as the new one's default by `ChatSettingsAdapter`). Checked on Catalyst: the settings section with its switch, charging row and count; a photo search itself wasn't run (the user's photos).
- [x] Phase C, written documents and meetings (2026-10-26): `WrittenDocumentsFeature` and `MeetingFeature` moved into `Emgu.LiteRT.LM.Maui` as files; the write_document group's constants are `Emgu.LiteRT.LM.Chat`'s `ChatWrittenDocuments`; the meeting feature's two demo dependencies became hooks the page sets (`SyncBackgroundService` for the camera feature's foreground service, `TranscriptAttacher` for `DocumentsFeature.AttachDocument`). The library's own AutoSave path doesn't keep written documents (nor generated pictures) with the reply yet - a saved-decorations step for later; the demo's page saves and restores them as before.
- [x] Phase C, MCP (2026-10-26): `McpFeature` moved as a file into a new package `Emgu.LiteRT.LM.Maui.Mcp` (so `Emgu.LiteRT.LM.Maui` doesn't depend on the MCP SDK; the same wiring as the Documents package), its servers in the "mcp_servers" setting (the demo's key); the device actions' Confirm / Cancel card became the library's `ConfirmationCard`, which the demo's `DeviceActionsFeature` uses too. Checked on Catalyst: a reminder request's confirmation card (the demo offers no timers on a Mac), cancelled, and the MCP settings section.
- [x] Phase C, the camera (2026-10-26): the camera cluster moved as files into a new package `Emgu.LiteRT.LM.Maui.CV` (`CameraFeature`'s ten partials, `ExerciseFeature`, the camera actions, `AlertAudioReplySpeaker`, the Android `MonitoringForegroundService` with its permissions declared by the assembly, `PoseDrawing`, `SoundWindower`, the four alert sounds as embedded resources; `CvFeature` as their base, `VisualDetectors` for the app's detector factories, `CameraFeature.SnapshotSaver` for an app's Save As dialog), the pure parts into `Emgu.LiteRT.LM.Agents.CV` (`VisualHostCatalog`, the snapshot and play_file actions, the `ChatCamera` constants - from the demo's Core, with the tests following), `PictureGallery` into `Emgu.LiteRT.LM.Maui` (with `FolderName` and `SaveWasCancelled`, used by the demo's image feature too) and `ChatResources.ThemeColor`. The demo keeps `DetectorFactory` (its GPU-check pictures) and the vision pages; the camera-sound and count-aloud settings are the library's keys (the demo's, through its prefix). Checked on Catalyst: the camera group answered a question about its agents and actions from `list_visual_agents`/`list_visual_actions` without opening the camera, and the alert-sounds section; the camera itself and the monitors weren't run (the user's camera and microphone). CPU threads and the workload were already the library's.
- [x] Phase C, the server (2026-10-26): `ServerFeature` moved as a file into a new package `Emgu.LiteRT.LM.Maui.OpenAI` (so `Emgu.LiteRT.LM.Maui` doesn't depend on the OpenAI SDK; the same wiring as the MCP package), its address and model in the "server_url"/"server_model" settings (the demo's keys). The page's remote-model seams became generic: `Emgu.LiteRT.LM.Chat`'s `IRemoteChatModel` on `ChatModelOption.Remote` (connect, client, adjust/resend options, summary options, one-off questions), which the library's `ChatPage` uses by default in `CreateChatClient`, `ConnectRemoteAsync`, `ComposeOptionsAsync`, `ConfigureConversation` and `AskModelOnceAsync`; `ChatView.ResendRequested` and the page's `ShouldResend` for the message sent again after a refused request; `IChatPageHost.ResetRemote`/`ModelCapabilitiesChanged`/`RefreshModelList`/`StartNewChat`. The demo's page lost its five server overrides and `IDemoHost` its four model members. Checked on Catalyst against a mock OpenAI-compatible server (see `FEATURES.md`, "Your own server").
- [x] Phase C, export (2026-10-26; before storage, which waits for an export in progress): `ExportFeature` moved as a file into `Emgu.LiteRT.LM.Maui` (it takes the app's exporter delegate, so the MAUI package stays free of SQLite; `ExportFolder` is the ZIPs' cache folder the storage catalog clears) and `ChatExporter` into `Emgu.LiteRT.LM.Chat.Sqlite`, over `ChatHistoryStore` (`GetChatAsync`, `GetMessagesAsync<ChatMessageRecord>`, the attachments and the document records; `UnitTestChatExport` unchanged). Found on the way: `ChatSearchIndex` used `ToHashSet`, which netstandard2.0 lacks, so `Emgu.LiteRT.LM.Chat`'s netstandard target hadn't built since the past-chats move (the MAUI and test builds use net10) - fixed. Checked on Catalyst: the Export button on a chat row opened the share sheet with the ZIP.
- [x] Phase C, storage (2026-10-26): `StorageFeature` and `StoragePage` moved as files into `Emgu.LiteRT.LM.Maui` (the page styled with the chat's theme keys), `StorageCatalog` and `StorageFiles` into `Emgu.LiteRT.LM.Chat` (public; the catalog takes `StorageModel`s - a search model with its `Download` for the page's Download button - and `StorageFolder`s for the app's own folders, the image model's included, in place of the ImageModel kind; the store's part is the new `IChatStorageStore`, which `ChatHistoryStore` implements in `ChatHistoryStore.Storage.cs`, from the demo's `ChatStore.Storage.cs`). The feature's couplings became generic: `IStorageParticipant` (in `LM.Maui`; `PrepareStorageDeleteAsync` before a deletion - every participant at once, cancel first and wait after - and `StorageChanged` after one, after a download, or when the search model's availability changed), implemented by `PhotoSearchFeature`, `PastChatsFeature`, `DocumentsFeature` and the demo's `ImageFeature`, found through the new `ChatFeatureServices.GetAll<T>`; `SearchServices.Choice` (the `ISearchModelChoice`, for the model's name and download) and the deleted-model check and notice the storage feature adds to the services on attach (`search_deleted_<file>`, the demo's key); `IChatPageHost.RunReloadAsync`/`ReleaseModelAsync` (`ChatPage.RunReloadAsync`/`PendingReload`), so `IDemoHost` lost them. The searches start indexing again themselves when a private chat ends. Checked on Catalyst: the Storage section, the page with every entry and the free space, deleting the shared exports (the confirmation, the entry "Not on this device" after), Back.
- [x] Phase C, the saved decorations (2026-10-26): a reply's own files are `ChatMessageItem.SavedFiles` (`ChatSavedFile`, in `Emgu.LiteRT.LM.Chat`), which a feature keeps with the reply through `IChatPageHost.AddCardToReply(card, file)` (the written documents feature at once, the demo's image feature once the picture is drawn, with a null card); `ChatHistoryStore` saves them with the reply by default (so the library's AutoSave binding keeps them) and reads them back into the loaded reply, and the page's `RestoreSavedFiles` asks the features for the cards (`ChatFeature.RestoreSavedFile`) when a chat is opened - the demo's page calls it too, so its `Drawn()`/`Take()`/`ClearDrawn()` bookkeeping is gone. `WrittenDocumentsFeature` is on the sample's page now. Checked on Catalyst: the demo (a Markdown document written by Gemma 4 E2B on the GPU, New, reopened with its card) and the sample (the same through AutoSave, on the CPU - the sample has no Metal package, and the GPU-to-CPU prompt worked; `UnitTestChatHistoryStore.SavesAReplysOwnFilesWithoutBeingGivenThem`).
- [x] Phase C, device actions (2026-10-26; the first of the review): `DeviceActionsFeature` and `DeviceActions` (the platform code) moved as files into `Emgu.LiteRT.LM.Maui`, the group's constants into `Emgu.LiteRT.LM.Chat`'s `ChatDeviceActions` (from the demo's `ChatToolGroups`, with the routing test following). Checked on Catalyst: a reminder request's confirmation card, cancelled.
- [x] Phase C, task mode (2026-10-26): `TaskFeature` moved as a file into `Emgu.LiteRT.LM.Maui` (the card with the theme's fonts and colours, `ChatPage.Link` for Skip/Undo); the per-message client it needs is the new `ChatFeature.CreateMessageClient(request)` - the page's `CreateMessageClient` asks the features after preparing the message (the default `SendCoreAsync` and the demo's), `ChatView.DefaultSendAsync` takes the client. Checked on Catalyst: a planned two-step question with Gemma 4 E2B on the GPU - the plan card, Run, the steps' results, the answer.
- [x] Phase C, the voice language (2026-10-27): `VoiceLanguageFeature` moved as a file into `Emgu.LiteRT.LM.Maui` (the "voice_language" setting, the demo's key; the recognizer's language set in `OnAttached`). Checked on Catalyst: the picker in the settings.
- [x] Phase C, the voice chat (2026-10-27): `VoiceModeFeature` moved as a file into `Emgu.LiteRT.LM.Maui`; `IReplySpeaker` gained the `Speaking` event and `Completion` task it follows (`TextToSpeechReplySpeaker` implements them now, as the CV package's `AlertAudioReplySpeaker` did), `IChatPageHost` gained `ReplySpeaker`, `IsRecording`, `StopReply` and `SendVoiceMessageAsync`, so `IDemoHost` is down to the store, the workload and whether the page is shown. Found on the way: features are attached before the page's template is applied, so a host member that reads the view (`ReplySpeaker`, `IsRecording`) is null-safe and the feature follows the speaker again in `OnAppearing`. Checked on Catalyst: the chat page opens with the chip; the layer itself wasn't started (the microphone).
- [x] Phase C, the review (done 2026-10-27): the demo-only features were reviewed one by one for what an app would want - `DeviceActionsFeature`, `TaskFeature`, `VoiceLanguageFeature` and `VoiceModeFeature` moved (above); `TranslationFeature` and `TranscriptFeature` stay (they work on the demo's message templates - `StreamingBubble`, `UserMessageInfo` - and its own saving, which an app on AutoSave gets from the binding's transcript watch), as does `ModelSettingsFeature` (the demo's embedding-model choice); `ImageFeature` became a package of its own the next day (below). Phase C is complete: the demo's page is a `ChatPage` with the library's features plus its own three, `IDemoHost` holds the store, the workload and whether the page is shown. The plan is in `history/CHAT_PAGE_PLAN.md`.
- [x] The follow-ups (2026-10-27): `ImageFeature` moved as a file into a new package `Emgu.LiteRT.LM.Maui.Images` (depends on `Emgu.LiteRT.LM.Maui` and `Emgu.LiteRT.Models`; the same wiring as the other packages), with `ModelSelector` and `PictureSaver` hooks for the demo's model selector and Save As dialog, the "image_generation" setting (the demo's key), the workload from the constructor, and its PNG written by `Emgu.LiteRT.Models`' new `PngEncoder` (8-bit RGB, zlib over deflate; `UnitTestPngEncoder` decodes it with Emgu CV and round-trips `PngMetadata`) instead of Emgu CV; the group's constants and instructions are `Emgu.LiteRT.LM.Chat`'s `ChatImageGeneration`. And the Windows CI follow-up: `TestCocoSsdMobilenetV1` compared the CompiledModel and Interpreter detections exactly, and on Windows the CompiledModel found a surfboard at 0.57 the Interpreter didn't - the sure detections (0.6 and up) must match one to one now, weaker ones may differ. Checked on Catalyst: the Image generation section (the model isn't on this Mac).
- [x] A live reply through the sample's `ChatPage` on a phone: done on the Pixel Fold on 2026-10-26 (Qwen3; Gemma 4 E2B calling `roll_die`, `remember` and `get_weather`; the chats, memories and settings sheets; reopening a chat with a model switch) - two bugs fixed, see `docs/agents.md`.
- [x] The rerun-after-stub groups (`ChatView.RerunRequested`, `ChatSkillSet.TakeRerun`) and the thinking duration saved with a reply (`ChatMessageRecord.ThinkingSeconds`, so a reopened reply says "Thought for N s"): done 2026-10-26.
- [x] A live reply on Mac Catalyst: done 2026-10-26 (typed with System Events; Enter sent through `ChatPromptEditor`'s handler, Qwen3 thought and answered). It showed that a model without tools was still given the tool instructions and mused about "the remember tool": `ChatPageOptions.InstructionsWithoutTools` now.
- [x] The Core project's cleanup and the model catalog (2026-10-27): `ChatToolGroups` gone (the groups' names are the features'), `ChatSessionTools` a test fixture, `ChatModelCatalog` the library's (`Emgu.LiteRT.LM.Chat`, `ChatModelCatalog.For(ChatPlatform)`; `ChatDevicePlatform.Current` in `Emgu.LiteRT.LM.Maui`), which the demo and the `MauiChat` sample both use; `ChatSettings`/`AppSettings` gone (`ModelSettingsFeature` reads its keys through `Host.Settings`).
- [x] The thinking translation into the library (2026-10-27): `TranslationFeature` moved as a file into `Emgu.LiteRT.LM.Maui`, `LanguageText` into `Emgu.LiteRT.LM.Chat`. `ChatMessageItem` gained `Language`, `ThinkingTranslation`, `ShowThinkingTranslation`, `ThinkingDisplayText` and `ThinkingAccessory` (the library's and the demo's reply templates bind the last two to the thinking view), `ChatHistoryStore` the two columns (the demo's names, so old chats keep them) and `SaveMessageChangesAsync`, `IChatPageHost` `SaveMessageChangesAsync`. The `MauiChat` sample has it too. Checked on Catalyst (Spanish, Gemma 4 E2B on the GPU): the row, the translation, the chat reopened with it, back to the English.
- [x] The `Maui.Demo.Lite.Core` project removed (2026-10-27): its `ChatStore` only renamed `ChatHistoryStore`'s methods and its records were empty, and `ChatPlannerInstructions.Agent` was the library's `ChatSkillSet.DefaultInstructions` word for word. The page uses `ChatHistoryStore` with a `SqliteAgentMemoryStore` and a `SqliteAgentGoalStore`, the tests the library's names; the same database and tables, so saved chats open unchanged.
- [x] The voice message transcript into the library (2026-10-27): the demo's `TranscriptFeature` is gone; the base `ChatPage` keeps a voice message's words when the app saves the chats itself (`AutoSave` off), `SaveMessageChangesAsync` titles a "Voice message" chat, `SaveExchangeAsync` keeps words written down during the save, and `TitleOf` uses words written down before it. `IDemoHost` lost its last user and was removed. The demo's only feature of its own is `ModelSettingsFeature`. The store's side is unit-tested. Checked on the Pixel Fold the same day (a voice message spoken by the Mac's `say`, Gemma 4 E2B on the GPU): the words under the message, the chat titled with them, both back after reopening. There the words came before the save (Gemma thought for 31 s), so the page's watch for words that come after it is still only covered by the store's test.
- [x] A reopened voice message showed its saved file's name ("294_0.wav · 8 s") instead of "Voice message (0:08)": the store now keeps the name an attachment was shown with as a fourth part of the `Attachments` line (`ChatSavedAttachment.Name`; older rows read as before; `KeepsTheNameAnAttachmentWasShownWith`). Found on the Fold, 2026-10-27.
- [x] The embedding-model choice into the library (2026-10-27): the demo's `ModelSettingsFeature` became `Emgu.LiteRT.LM.Maui`'s `SearchModelFeature` (`SearchModelOption`s, the "search_model" setting, a GPU option's `CpuFallback`, `CreateServices`), with `ISearchModelParticipant` for the photo, past-chat and document features; the page keeps its Windows GPU options, moves the old "embedding_model"/"embedding_windows_gpu" settings over, and keeps the image-model selector. `DemoFeature` and `Chat/Features/` are gone. Checked on Mac Catalyst: the section, the saved choice, the list (Cancel). A real switch wasn't run: on this Mac it would download the 485 MB Multimodal model and clear the photo index; try it where both models are downloaded, and the Windows GPU options on Windows.
- [x] Photo search said "0 photos indexed" on the Pixel Fold with 200 saved (2026-10-27): the saved index was read only once a pass got going - after waiting for the device (there: "only while charging", unplugged) and loading the search model - so until then the count was 0. `PhotoSearchFeature` now reads it (`PhotoIndex.EnsureLoadedAsync`) when the page first appears, and an indexing error stays on screen with the count (it was replaced at once by the count). Checked on the Fold: "Photo indexing paused: waiting for a charger. 200 photos indexed." No place on any photo there: the app hasn't been granted ACCESS_MEDIA_LOCATION, so Android strips the GPS positions.
- [x] "Allow photo locations…" in Settings > Search my photos on Android (2026-10-27), while ACCESS_MEDIA_LOCATION is missing: the request, or the app's settings page; once allowed, the photos' details are read again for their places, embeddings kept. On the Fold the permission was granted without a dialog; the places pass waited for a charger (only while charging, unplugged) - check the place count once it has run.
- [ ] Still open: the Windows build of `ChatPromptEditor`.
