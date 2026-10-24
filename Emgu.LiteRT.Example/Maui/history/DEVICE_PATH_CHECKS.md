# Device checks for TODO item 3

Track actual runtime checks here. A successful build does not establish that a microphone, GPU, native PDF renderer,
photo library or network service works. Leave a check pending until its device-specific steps have run.

## Results — 2026-10-16–17

| Path | Result | Evidence / remaining work |
|---|---|---|
| Tavily search, Mac Catalyst | Passed | Called the production `TavilySearch` tool with “Emgu LiteRT GitHub”, using the app's saved key. Received 1,978 characters of results without an HTTP/tool exception. |
| Tavily extraction, Mac Catalyst | Passed | Called the production `TavilyReadPage` tool for `https://www.emgu.com/`. Received 1,503 characters of extracted content without an HTTP/tool exception. |
| Live microphone, Mac Catalyst | Pending permission | Recording checks reached the microphone permission request; no completed capture is claimed. |
| Live microphone, Android | Passed capture and model input | Pixel Fold recorded the device speaker's fixed test phrase twice through `AudioRecord`: 4.34 s and 3.02 s, PCM16 mono at 16 kHz, nonzero peak samples 16,740 / 18,274. Gemma 4 E2B GPU returned a nonempty 28-character transcription containing “fox”. Audio/transcript were not saved. This is a real microphone capture of synthesized speech; a person's spoken-message UI check remains useful. |
| Recording lifecycle, Android | Fixed and passed | Baseline kept recording when another page covered chat. After cancelling on page disappearance and window stop, native tests confirmed recording was false after navigation and after an actual Home/background transition. |
| Image generation, Android (CPU only) | Passed; slow | Pixel Fold generated a recognizable watercolor fox in snow at 256 × 256, eight steps, seed 12345. First run took 883.11 s (14 min 43 s), including cache preparation. Peak process RSS was 3,851,040 KiB (3.67 GiB). All 21 model files (10,563,077,982 bytes) passed SHA-256 against the existing Mac copy after the actual interrupted/resumed Hugging Face download. Gemma coexistence is checked separately below. |
| Image generation alongside Gemma GPU, Android | Passed with prepared caches | Actual private-chat `SendAsync` → agent → `generate_image` → image tile → assistant reply, with Gemma 4 E2B GPU, 4K context and thinking off. “Draw a red fox in snow, watercolor.” produced one inspected PNG, one tool step and a 57-character assistant reply in 1,040.71 s (17 min 21 s). Peak process RSS was 5,161,368 KiB (4.92 GiB). The real image-viewer action was invoked. No workload pause or process termination occurred. |
| Image download pause/resume and storage, Mac Catalyst | Passed | Cancelled a real Hugging Face download after 8,407,809 bytes; progress included those bytes, resume completed with a matching SHA-256, and the production storage catalog removed the isolated weights/cache. Original model remained complete. |
| Image settings and storage dialogs, Android | Passed | Actual native Download/Cancel, pause (1,168,340 bytes retained), resume to Ready, storage Cancel and Delete all passed. The prompt showed 51 MB then 49 MB remaining. Supporting weights were sparse, size-only placeholders in an isolated fixture; only its missing 50 MB graph was really downloaded. No inference used that fixture. Deletion removed fixture weights/cache, turned generation off and preserved the verified source model. |
| Windows generation, recording, PDF rendering and photos | Pending hardware | No Windows device or VM was available in this workspace. |
| Real phone photo indexing | Pending owner agreement | No new real-library indexing test was started. |

The image tests exposed and fixed `ZImageTurbo.DownloadedBytes` and resumed-progress accounting ignoring `.part`
files, and the settings dialog rounding small remaining downloads to “0.0 GB”. Small downloads now display MB;
the storage explanation distinguishes total model/cache use from remaining download size. Eight managed
regression checks passed (two new image-progress checks and six storage checks).
The Mac fixture used an existing model copy with one 50 MB graph omitted; this verifies a real graph download and
resume, not a fresh 10.6 GB installation or every safetensors/tokenizer download path.

Process RSS/HWM comes from `/proc/self/status`; it does not account for every GPU allocation. The standalone run
started without prepared caches; the chat run reused them. Different prompts and device conditions mean these two
runs are not a controlled measure of Gemma's speed overhead. First-run cache preparation with Gemma resident,
interactive PNG saving, a person's voice-message UI flow, and denied/re-enabled microphone permission remain
unverified. The Pixel Fold timings establish that this implementation works but is currently slow on this device.

API keys, search response text and microphone recordings are not included in the evidence logs. Temporary microphone
checks retain audio only in memory. Diagnostic app instances were closed after the run; their pending microphone
requests cannot start a late recording. Temporary startup code was removed from the clean builds. The Mac image
fixture used separate hard links for existing weights and its own download/cache files; Android used the separate
fixtures described above. Deletion never targeted the existing model directory.

## Live microphone

1. Use a device with an actual input microphone, allow the OS microphone permission, and select an audio-capable
   chat model such as Gemma 4 E2B.
2. Start recording, speak “This is a microphone test”, stop, and send. Confirm that the recorded attachment reaches
   the model and that the reply addresses the spoken message.
3. Repeat start/stop to check microphone reuse. Also stop or leave the page while recording and check that capture
   ends and another app can use the microphone.
4. Inspect only format/signal metadata if collecting diagnostics: RIFF/WAVE, 16 kHz, mono, 16-bit PCM, expected
   duration, nonzero samples. A generated WAV fed directly to the model does not test the live capture path.
5. Check denied permission and re-enabling it in system settings separately, restoring the owner's original setting.

## Android image generation

1. Confirm free space for approximately 10.6 GB of weights, 9 GB of prepared caches and additional OS headroom.
   Keep temporary test files in a separate folder. Verify every transferred file's final size; an ADB transport
   accepting bytes is not proof that the phone finished writing them.
2. In Settings, turn Image generation on: verify the remaining download size and storage explanation. Cancel and
   confirm that the switch returns off without starting a download.
3. Accept Download, observe increasing progress, turn the switch off partway through, and relaunch. Confirm retained
   `.part` bytes are reflected in the remaining size and progress. Resume and verify complete model readiness.
4. With the chat model unloaded, generate “A red fox in snow, watercolor” at the default eight steps. Record elapsed
   time, peak process memory, successful PNG display/open/save and whether the UI remains responsive.
5. Repeat with Gemma 4 E2B loaded on GPU and invoke generation from chat. Record memory, any process termination,
   and the final nonempty acknowledgement. Measure a second image after cache preparation separately.
6. In Storage, cancel deletion once, then confirm deletion of the **test** image model. Verify weights, partials and
   prepared caches are removed, generation turns off, and other models/chats remain usable.

## Tavily

Use a key already configured in Settings > Web search. Never put a key in a checked-in fixture or print it in logs.
Call both production tools: search for a harmless public topic and read a public URL. Confirm search returns page
titles, URLs and extracts, and extraction returns page text rather than the “Could not read” fallback. A fake tool or
keyless `read_web_page` call does not establish that Tavily extraction works. Separately test one chat prompt that
requires recent information and check that the model cites the returned sources.

## Phone photos and Windows

For a real-library test, first obtain the owner's agreement. Use an isolated index, avoid logging filenames, locations
or image contents, and keep embeddings on the device. Check a small agreed set with a known subject, date/place
filters, opening the original and cancelling/restarting indexing. Remove only the test index afterward; never delete
library originals. Test-only images can exercise mechanics but do not complete the real-library check.

On Windows, render a scanned PDF page through `Windows.Data.Pdf` and verify readable orientation and dimensions;
test both a local photo's `StorageFile` details and a cloud-only/inaccessible photo. Also run the recording and image
generation steps above. Mac/Android equivalents and cross-compilation do not complete these Windows checks.

## Interrupted-run cleanup

The phone reconnected and the original interrupted folder was removed. Follow-up checks used only
`files/item3_zimage` and `item3_*` diagnostic files. Those test weights, prepared caches, test store and outputs were
removed after completion; `files/LiteRT-LM`, downloaded chat models, saved chats and the phone's photo library were
preserved. The temporary transfer server and ADB reverse port were removed. Temporary diagnostic startup code was
removed and the normal app rebuilt and redeployed. Android and Mac Catalyst builds passed; the clean Android
assembly was checked for absence of the diagnostic classes and command marker.
