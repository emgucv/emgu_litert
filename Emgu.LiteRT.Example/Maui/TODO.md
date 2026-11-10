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
- 5. A new app icon — completed (the Windows check done 2026-10-29)
- 6. Run the model-free tests in CI — completed
- 7. An agent regression test — implemented
- 8. A Storage page in settings
- 9. Update to the next LiteRT-LM release — completed (v0.19.0 on every platform)
- 10. GPU on Windows — core support completed; follow-ups remain
- 15. Agent tool: search documents (`search_documents`)
- 16. Agent tool: search past chats (`search_chats`)
- 19. A private chat
- 21. Accessibility — completed (labels, large text and contrast, 2026-11-05)
- 36. Split the iOS natives into frameworks — completed (the simulator, an iPad Air and an iPhone 12)
- 43. (Notes from the feasibility checks, before it was built)
- 50. A shutter click for photos taken by an alert

## 3. Test the paths that were never run — partially verified

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Windows: the photo library (`StorageFile` details; its thumbnails now use `StorageFile.GetThumbnailAsync` - the library's
      Windows build had broken on Emgu CV and an ambiguous `Path`, fixed 2026-10-29).
- [ ] Photo search on a real phone's library (only the emulator's test photos so far), on a phone whose owner agrees
      to have their photos indexed for the test.
- [ ] Real-library photo indexing awaits owner agreement; Windows runtime checks await a Windows device/VM.

## 4. Collapsible "Model" section in settings — implemented; foldable UI check pending

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Foldable inner-screen UI check (Z Fold3 installed; initially locked, then wireless debugging disconnected).

## 11. GPU for the embedding models — Windows, Mac (Metal), a Galaxy S10 and the Pixel Fold (OpenCL) checked; the demo offers it on Windows, Mac Catalyst and Android

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] The same on the Pixel Fold (it was off adb).

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

- [ ] Long content: the model writes it in its reply budget (1,024 tokens by default in `LiteRtLmChatClient`); for
      longer documents, ask it to write section by section.

## 20. Export or share a chat

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Optionally import it back.

## 22. Hands-free voice conversation — implemented; verified on a Pixel Fold and on Mac Catalyst

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Speech-to-text on an iPhone; Windows has none (its recorder has no level for the voice chat either, but voice
      messages could use `Windows.Media.SpeechRecognition`).
- [ ] An iPhone with a real microphone, and any device with headphones (speaking over the reply).
- [ ] Headphone detection (`VoiceRecorder.HeadphonesConnected`: the audio route on iOS/Mac Catalyst, output devices
      on Android) is untested; so is recording while text-to-speech plays on iOS.
- [ ] Windows has no speech recognizer for this; an option to not keep the audio (only the words); the transcript isn't in the chat search.
- [ ] An automatic mode (Android 13+ `EXTRA_ENABLE_LANGUAGE_DETECTION` with the languages the user speaks); the language pack download isn't shown to the user
      (only the message to try again); the voice chat (hands-free) uses the same setting but has no way to change it on its full-screen view.

## 23. Task mode: multi-step tasks with a visible plan — implemented; verified on Mac Catalyst with Gemma 4 E2B

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] On a phone (the iPad ran it on 2026-11-05: an 8k window, 5-6 steps in 196-222 s), and with a server model.

## 24. Agent tools: device actions, confirmed by the user — implemented; Android, iOS and Mac Catalyst verified

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Alarms more than 24 hours ahead (a calendar event is suggested instead); timers/alarms on iOS (no public API).

## 25. Goals and check-ins — goals, reminders and end dates implemented; verified on Android

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number (the reminders and the end dates since
2026-10-29). Still open:

- [ ] Reminders on a real iPhone, Snooze (Android only: Apple's have no Snooze action), and after a phone restart (the
      Pixel Fold's first reminder and the Mac's were seen).
- [ ] A "Done" on the reminder itself: the goal database is the app's, out of a notification receiver's reach; for
      now tapping it opens the app.

## 26. Agent tool: clipboard (`read_clipboard`) — implemented

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Android 12+'s "pasted from your clipboard" toast not seen: the S10 has Settings > Privacy > "Show clipboard access" off
      (`clipboard_show_access_notifications` 0); a phone with it on.

## 27. More models: Gemini Nano and Apple's on-device model

System models, offered in the model picker only where the device has them (detected at run time):

- [ ] Gemini Nano through ML Kit GenAI's Prompt API (Android: Pixel 9/10, Galaxy Z Fold7 and other recent flagships;
      not the emulator, the Pixel Fold or the Z Fold3): a .NET for Android binding of `com.google.mlkit:genai-prompt`,
      wrapped as an `IChatClient`; check whether it supports tool calling.
- [ ] Apple's Foundation Models through `Microsoft.Maui.Essentials.AI` (an `IChatClient`), once the repo moves to
      .NET 11 (iOS / Mac Catalyst with Apple Intelligence).

## 28. MCP support — implemented (HTTP verified with DeepWiki on a Galaxy S10; stdio on Mac Catalyst)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] stdio on Windows is untested (verified on Mac Catalyst); OAuth for servers that need it (the SDK has a client for it) - only
      a static header now; resources and prompts of servers aren't used, only tools.

## 29. Serve the Mac's models to phones ("Server" option) — chat served and paired; verified with a Galaxy S10

The Mac Catalyst app serves the model it runs over the local network, and phone apps use it as if it were a local model.
How it works and what was verified: FEATURES.md, "Serving the Mac's model". Done so far: see
[history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Photo and video search through the Mac (the Mac's search model is text only for phones: the phone would send each
      picture or frame); past chats and documents use it since 2026-11-10.
- [ ] `POST /v1/images/generations` (Z-Image-Turbo, FLUX, Bonsai), so a phone gets pictures without the models.
- [ ] Try: an image and a voice message from a phone, Gemma 4 12B, the iPad (iOS's Local Network prompt), a phone on
      Tailscale (the address typed by hand), the Mac's address changing (Find a Mac again by name), several phones at once.
- [ ] A Mac asleep: the phone's message waits for a timeout; say "the Mac is asleep or unreachable" sooner, and show it in
      the model list.
- [ ] A sandboxed Mac build would need `com.apple.security.network.server`; HTTPS (a self-signed certificate) for use away
      from home without Tailscale.

## 30. Background tasks and monitoring — scheduled tasks implemented on Android; verified on a Galaxy S10 and a Z Fold3

"Every morning, summarise the weather and my reminders", "tell me when this page mentions a new release": tasks the
agent runs on a schedule, with a notification when there is something to say. How it works and what was measured:
FEATURES.md, "Scheduled tasks". Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

Shared:
- [ ] An "Add task" form in Settings > Scheduled tasks (now made only through the chat).
- [ ] Mac Catalyst: runs while the app is open (or a login item); the model is already loaded or loads for the run.

Android:
- [ ] A run with the app's process gone and the model loaded by the service on the Z Fold3 (the S10's was, 7.6 s + 46.7 s);
      the CPU's times and the memory; a run skipped when hot or low on battery (implemented, not seen).
- [ ] The S10 didn't re-arm the alarms after a debug reinstall (no `MY_PACKAGE_REPLACED` seen); the Fold3 did. Check
      after a restart, and with a Release install.

iOS: no run at a time the app picks.
- [ ] `BGAppRefreshTask` gives ~30 s (too short); `BGProcessingTask` gives minutes when iOS chooses, usually overnight
      while charging and idle: enough for "a summary ready by morning", not "at 7:00".
- [ ] No GPU in the background (iOS refuses Metal work from a background app), so the CPU; and iOS kills background
      apps first, so a 2.6 GB model is at risk even with the increased-memory entitlement - measure on the iPad.
- [ ] Otherwise run the task on a paired Mac (item 29) and notify the phone.

## 31. Image generation on iOS — implemented; untested on an iPhone

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] An 8 GB iPhone (the model cards' floor; Bonsai 512 is hidden there): the iPad has more memory.

## 32. Live camera tools — first version implemented

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Real webcam check on Mac Catalyst (this Mac has no camera). iOS live capture: item 59 (2026-11-04).
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

## 35. Privacy manifests for an App Store release (iOS, Mac Catalyst) — the demo's written; validation remains

Apple asks App Store apps, and SDKs that use "required-reason" APIs, to declare those APIs and the reasons for using
them in a `PrivacyInfo.xcprivacy`. The demo's, for iOS and Mac Catalyst, are in since 2026-11-05 (the scan and the reasons:
[history/TODO-done.md](history/TODO-done.md), same number). Still open:

- [ ] Confirm by uploading a Release build to App Store Connect (or `xcrun altool --validate-app`) and fixing what
      validation reports - also the embedded frameworks' `Info.plist` and `MinimumOSVersion` (the Gemma library needs
      iOS 26.4).

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

## 43. Video clips as a visual-alert action — `record_clip` built for Android (Camera2); tested on a Galaxy S10 and a Pixel Fold

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: a real detection triggering a clip (nobody in front of the camera - the action was called directly), clips longer than 6 s, the cost next to
      Gemma generating, and a clip while the voice recorder or a sound monitor holds the microphone (the earlier test shows the microphone can be shared).
- [ ] The clip starts when the alert fires (about a second after the person appears, plus the 0.3 s reconfiguration) and the frames pause that long at
      each end. A pre-roll (the seconds before the alert) needs a ring buffer of encoded frames: an always-running MediaCodec encoder muxed with
      MediaMuxer instead of MediaRecorder; and a MediaRecorder prepared in advance would remove the start's pause.
- [ ] Other platforms: Mac Catalyst and Windows have no clip action (their cameras are OpenCV's). iOS has it since 2026-11-04 (item 59).
- [ ] A clip as the action of a sound monitor ("record a clip when the dog barks") - the sound monitors have chime and speech only.

## 44. Full-resolution photos as a visual-alert action — `take_photo` built for Android (Camera2); tested on a Galaxy S10 and a Pixel Fold

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not run: a real detection triggering a photo (the action was called with a made-up event), the quality of the photos themselves (not looked at: only their
      size and orientation were read), phones with other sensors, and the largest "high resolution" sizes some cameras list (e.g. 50 MP) - only the standard maximum
      (12 MP here) is used, since Camera2 only guarantees that beside a preview stream.
- [ ] A photo of the frame that triggered the alert (the photo is taken ~2 s after the alert fires, about a second after confirmation); a photo as a sound monitor's
      action; the same on Mac Catalyst and Windows (their cameras are OpenCV's). iOS has it since 2026-11-04 (item 59).

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
      iOS has the switch since 2026-11-04 (item 59; `LiveCamera.HasFrontCamera` on both platforms).
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

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Places search (`search_places`, 2026-10-29; see `docs/agents.md`): built and unit-tested, checked on Windows with OpenStreetMap and a named town. Still to check: the device-position path ("nearest ..." with Share my location on) on the Fold and an iPhone, Google Places with a real key (the request is written from Google's documentation, never sent), Apple Maps on Mac Catalyst (`AppleMapsPlaceSearch`; on iOS it ran on an iPad Air on 2026-11-04: 25 libraries near a fixed point in 0.9 s, the nearest not first).
- [ ] Video moments (`VideoMomentsFeature`, 2026-10-29; see FEATURES.md, "Video moments"). It was checked in the iOS simulator, on Mac Catalyst, the Galaxy S10 and the Galaxy Z Fold 3 with a synthetic 30 s video, and on an iPad Air M4 (2026-11-04: watched with sound in 7.8 s, the simulator 19.6 s; every query's first moment its scene, the spoken phrase at 00:20-00:22; searches ~50 ms). Still to check:
  - the Pixel Fold;
  - a search waited about 7 s behind photo indexing (Z Fold 3): searches now go first (`SearchFirstGate`, 2026-10-30; 1.8 s during video indexing on the S10) - measure again on the Z Fold 3;
  - the S10 takes 25-40 min for a 5-minute video: a shorter limit or fewer frames on slow phones?
  - Windows (its `VideoSource` and `InlineVideoView` code hasn't been compiled);
  - picking from the photo library;
  - a real video of a few minutes, with real sounds;
  - reopening a chat with its cards;
  - a search model switched while watching.

  Later:
  - saving a moment as a clip (a frame-accurate cut);
  - videos longer than 5 minutes;
  - the GPU (the embedding GPU crash);
  - several videos in one chat;
  - telling "no moment matches" apart from a weak best match (photo search's lift against a baseline).
- [ ] Speech recognition with Parakeet (Settings > Speech recognition, `SpeechEngineFeature`, 2026-10-31; see FEATURES.md, "Speech recognition with Parakeet"). Voice messages, the voice chat and meetings use it when chosen, or where the device has no recognizer (the Galaxy S10's Android 12); checked on Mac Catalyst, the S10 and the iOS simulator with synthetic speech, and on the S10 beside Gemma 4 E2B with a meeting through the card and the Storage page's delete (2026-11-01). Still to do:
  - noise: fixed by LiteRT-LM v0.19.0's merger (item 9): 1.3% of words wrong on the noisy turns (Apple's recognizer 5.7%, Google's 4.4%); with that, decide whether Parakeet should be the default where it is downloaded;
  - a real microphone, the Pixel Fold and Z Fold 3;
  - Windows and Ubuntu builds of `libomniextern` (then Parakeet is Windows' only recognizer; the code is there);
  - text while speaking: `SpeechToTextSession` fed from the microphone as it records, instead of after the recording;
  - the languages Parakeet doesn't have (Chinese, Japanese...) go to the device's recognizer: Qwen3-ASR later.
- [ ] Search my videos (`VideoSearchFeature`, 2026-10-30; see FEATURES.md, "Search my videos"). Checked in the iOS simulator, on a Galaxy S10 and on the Pixel Fold with four synthetic clips. Still to check:
  - a real library: the thresholds (lift 0.02 over "a video") come from four synthetic clips; videos with talking, music, dark or busy scenes;
  - Mac Catalyst (with the user's OK: it reads the Mac's Photos library), Windows (not compiled here), the Z Fold 3;
  - many videos: the in-memory index (60 windows x 768 floats per video), the first pass's time, and the "Videos to index" defaults;
  - iCloud-only videos (skipped) and edited ones.

  Later:
  - past the first 10 minutes of a long video;
  - the photo cards saved with replies too, like the video cards.

## 56. Text to image through LiteRT-LM's omni engine (Bonsai Image 4B, FLUX.2-klein 4B)

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Pixel Fold and Z Fold 3 (12 GB): Bonsai and FLUX.2-klein on the GPU (OpenCL) - does a 12 GB phone's GPU fit the FP32 graphs? - and FLUX.2-klein on the CPU.
- [ ] Mac Catalyst through the UI (choose a model, draw, Stop during a picture, delete a model in Storage); not driven (no clicks on this Mac).
- [ ] Bonsai on the Windows GPU, open: the first 512 load without the caches and the CPU 512 weren't timed; memory is ~5 GB (not beside a chat model on an 8 GB card): (the engine now goes after 3 idle minutes, `ImageFeature.GpuIdleRelease`, 2026-11-05: watched with `nvidia-smi` on Z-Image GPU; Bonsai itself not) or run one stage on the CPU (needs a patch exposing `Flux2ModelConfig`'s per-stage backends; measure the per-stage split first). FP16 was tried: noise (see `MODELS.md`).
- [ ] Bonsai 512 on phones: on the Pixel Fold's GPU (2026-11-01) it loads in 159 s and draws in 201 s, but at 7.0 GB PSS (about 5 GB of GPU memory) Android closed the other apps and then killed the demo (in the background) before a second picture. To offer it on 12 GB phones: try with the demo in front, release the chat model while drawing, and limit it to phones with 12 GB or more.
- [ ] Maybe: prune caches of an older copy of the model file (their names carry the file's time and size) when it is downloaded again.

## 57. Kokoro text to speech through LiteRT-LM's omni engine

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] On a phone: a hands-free voice-chat turn with Kokoro (the microphone pausing and resuming around its playback), a camera alert's `speak_text`, and memory beside Gemma 4 E2B on the GPU.
- [ ] Shorter pauses: play the pieces gaplessly (AudioTrack, AVAudioEngine, AudioGraph) instead of a WAV file and a player each; start the first sound sooner by cutting the first sentence at a comma.
- [ ] Mac Catalyst through the UI (the section, "Try it", a reply read aloud).

## 58. Qwen3-ASR speech recognition through LiteRT-LM's omni engine

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Maybe: fewer windows' overlap on the CPU.
- [ ] Mac Catalyst and iOS: the Catalyst builds again since the demo and MauiChat need Mac Catalyst 17.0 (the new SDK
  pack's lowest, 2026-11-03), not run with Qwen3-ASR yet. The iOS simulator build failed to link
  (`_xamarin_initialize_dynamic_registrar` undefined) after the iOS SDK pack moved to 27.1: `obj/.../nativelibraries`
  kept the old pack's `libxamarin-dotnet-debug.dylib`, which the link searches first; it builds again with the
  platform's `bin`/`obj` deleted (2026-11-03, the demo and MauiChat).
- [ ] Maybe: pass the language Qwen3-ASR detects back (its decoder skips it; a LiteRT-LM change), and the 1.7B model on
  computers.
- [ ] Next omni models: maybe Moonshine tiny (English, 52 MB) or Whisper tiny for older phones.

## 59. The live camera on iOS — AVFoundation, at parity with Android's Camera2; measured on an iPad Air M4

Done so far: see [history/TODO-done.md](history/TODO-done.md), same number. Still open:

- [ ] Not done: a real detection triggering a photo or a clip, Mac Catalyst on this path (it keeps OpenCV's capture).
- [ ] The iPad's USB tunnel reset four times in 35 minutes on 2026-11-05, each time soon after the camera or the
  microphone started (it ended WebDriverAgent's runner each time; the app went on). The cable, or the camera's draw?

## 60. Watch and listen — Gemma 4 describes the live camera and microphone, every few seconds; Mac measured, Galaxy S10 run

"Tell me what you see and hear", on a page of its own on the home screen's LiteRT-LM tab (`Emgu.LiteRT.LM.Maui.CV`'s
`WatchAndListenPage` over `Emgu.LiteRT.LM.Agents`' `SceneNarrator`; how it works in [FEATURES.md](FEATURES.md),
"Watch and listen"). Done: the loop and its model-free tests, the shared `ChatModelLoader` (scheduled tasks use it
too), the page, one round measured on the Mac (4.3-5.3 s with Gemma 4 E2B's vision encoder on the GPU, 9.5-10.6 s
on the CPU), and a run on a Galaxy S10. Still open:

- [x] Galaxy S10: the scene described, 17.6-19.9 s a round with the vision encoder on the GPU, Stop, and leaving the
  page frees the model (2026-11-06).
- [ ] Speech heard on a phone (the S10's room was quiet: every round silent), the battery and heat over the 10-minute
  limit, the Z Fold3 and the iPad Air (the iOS build passes; not run on the iPad yet).
- [ ] Windows: a streaming `MicrophoneSoundSource` (WinRT `AudioGraph`); the page is hidden there until then.
- [ ] Reading the descriptions aloud (the microphone would hear them: mute it while speaking).
- [ ] Saving a session as a chat or a note; opening the page from the chat ("tell me what you see and hear"); running
  it through the Mac's server (item 29).

## 61. The camera's captures: the strip under the live view, one chat row per opening, and Keep in chat

The pictures and clips the camera actions take appear under the live preview, in one row with the newest on the
left, and nothing goes into the chat while the camera is open; when it closes, they become one row in the chat, and
"Keep in chat" or "Keep all" saves them with the chat (how it works in [FEATURES.md](FEATURES.md), "Camera and sound
monitors"). Done: `CameraCaptures` and its tests, the strip, the rows, the viewer, Keep and restoring kept captures as
one row (`ChatFeature.RestoreSavedFiles`), checked on the iPad Air (2026-11-08). The viewer's cut-off "Save snapshot…"
button seen on the iPad the same day is fixed (the picture leaves room for the buttons). Still open:

- [ ] A device run of a cap pushing out the oldest photo (unit-tested), a camera switch (no row), and a private chat
  (no Keep, no Keep all).
- [ ] The Galaxy S10 (photos and clips through Camera2) and Mac Catalyst (snapshots with OpenCV's camera).
