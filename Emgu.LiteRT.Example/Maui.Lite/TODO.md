# Chat page TODO

Planned work on the MAUI demo's LiteRT-LM chat page (`LiteRtLmChatPage*.cs`).

Sorted by priority: the download items first, since every model the chat offers depends on them (item 2 shows the
paused state item 1 makes possible); then the small layout fix; then GPU work - Windows (packaging and testing of a
plugin LiteRT-LM already ships) and the embedding models (waiting for a LiteRT-LM release); then new agent tools - the
two searches build on the saved document indexes and embedding model that exist, device actions need platform code
and a confirmation step, the clipboard is small but less useful; then the two largest: MCP, and serving the Mac's
models to phones.

## 1. Resume partial downloads

`FileDownloadManager` (`Emgu.LiteRT.Util`, used by every LiteRT-LM model) writes straight into the model file and deletes
it when the download is cancelled; a download cut off by the app closing or the phone sleeping leaves a partial file
under the model's name, which fails the hash check and is downloaded again from the start. A 2.6 GB Gemma 4 download
stopped at 90% starts over.

- [ ] Download into `<file>.part` and rename it when complete.
- [ ] Resume with an HTTP Range request for the missing bytes (Hugging Face supports it; `Emgu.LiteRT.Models`'
      `RangeDownloader` already does this for Z-Image-Turbo); start over if the server answers 200 instead of 206.
- [ ] Keep the `.part` file when the download is cancelled, fails or the app closes.
- [ ] Make sure the bytes come from the same file: pin the URLs to a revision, or keep the server's ETag with the
      `.part` file and send it as If-Range.
- [ ] Check the SHA-256 once complete, as now; on a mismatch delete the file and start over.
- [ ] Demo: a partly downloaded model shows "Download paused · 1.2 of 2.6 GB" with Continue (in its row, item 2, and
      in the download card).

## 2. Show which models are downloaded

Today every row of the model list (the side panel and the phone's model sheet, `BuildModelList`) reads the same way -
name, then "~2.6 GB download. ..." - whether the file is on the device or not; only selecting a model tells (it loads,
or a card offers the download).

- [ ] Mark each row: "Downloaded" (a check or device icon) versus "Download · 2.6 GB" (a cloud icon), using
      `IsDownloaded(option)`, which only looks at the file and never hashes it.
- [ ] Drop "~2.6 GB download." from the description of downloaded models; show the size on disk instead.
- [ ] A row for a model that is downloading shows its progress, and a partly downloaded one "Download paused"
      with its progress (item 1).
- [ ] Refresh the list when a download finishes, is cancelled or a model is deleted.
- [ ] Optionally: delete a downloaded model from its row (with its GPU caches, `*_cache.bin` next to it), asking first.
- [ ] The same for the image model (Settings > Image generation) and the search model used for documents and photos.

## 3. Collapsible "Model" section in settings

In wide windows (desktop, a foldable's inner screen) the side panel shows Chats, Model and Settings
(`_sidePanel` in `LiteRtLmChatPage.cs`); the model list takes a lot of room.

- [ ] Make the "Model" title a tappable header with a chevron that shows or hides `_sideModelList`.
- [ ] When collapsed, show the selected model on the header line (e.g. "Model · Gemma 4 E2B"), so it is still visible.
- [ ] Remember the state in Preferences.
- [ ] Phones are unaffected: there the model list opens in a sheet from the model chip.
- [ ] Check on Mac Catalyst and a foldable's inner screen.

## 4. GPU on Windows

On Windows LiteRT runs on the CPU only: there is no GPU accelerator next to our `libLiteRt.dll`, so Gemma 4 can't use the
GPU there as it does on Mac Catalyst (Metal) and Android (OpenCL). LiteRT-LM v0.18.0 ships the pieces in
`LiteRT-LM/prebuilt/windows_x86_64/`: the WebGPU accelerator `libLiteRtWebGpuAccelerator.dll` (LiteRT's GPU on
Direct3D 12 through Google's Dawn), Dawn itself (`libwebgpu_dawn.dll` / `webgpu_dawn.dll`) and the WebGPU top-k sampler.

- [ ] Check the plugin works with our `libLiteRt.dll` (built from LiteRT `26895c9f`): `dumpbin /imports` of the plugin
      against the exports in `litert_windows_exports.def` (add any it needs and is missing), then run MobileNet through
      `CompiledModel` with `HwAccelerators.Gpu` (a Windows `TestLiteRtMobilenetGpu`) and compare with the CPU.
- [ ] `bazel_build_litert.ps1` copies the plugin and Dawn to `lib/runtimes/win-x64/native/`, as the macOS and Android
      scripts do for Metal and OpenCL.
- [ ] Loading: libLiteRt loads plugins by file name; check Windows finds them next to the app (the DLL search path
      includes the app folder) or extend `Emgu.LiteRT.Util.AcceleratorPlugins.Load()` to load them by full path as on
      macOS.
- [ ] Package: an optional `Emgu.LiteRT.WebGpu.runtime.windows` NuGet package (like `Emgu.LiteRT.Metal.runtime.macos`),
      depending on `Emgu.LiteRT.runtime.windows`; the demo references it on Windows.
- [ ] LiteRT-LM: Gemma 4 with `backend: "gpu"` on Windows, with `ActivationDataType.Float32` (fp16 got digits wrong
      on Metal and OpenCL); the demo's "Use the GPU" setting and the GPU fallback (`_gpuUnavailable`) on Windows too;
      compare speed and answers with the CPU over a long chat, and record them in `Performance.md`.
- [ ] The GPU program/weight caches next to the model (as on Android and Mac); delete them when the GPU is turned off.
- [ ] Known limit: the embedding models stay on the CPU until item 5; Z-Image-Turbo is no faster on a GPU.
- [ ] Needs a Windows machine with a Direct3D 12 GPU (it can't be tested from the Mac).

## 5. GPU for the embedding models (document and photo search)

The embedding models (`EmbeddingGemma2*`, used for document questions and photo search) run on the CPU only:
`EmbeddingModel.Init` refuses "gpu", because they crash the process on every prebuilt GPU plugin (Metal on the Mac, OpenCL
on Android). Their weights are external buffers, which the plugin binds through `WeightLoader`, whose interface changed
in LiteRT `dd3db3123` ("Make the WeightLoader interface ABI-stable across the accelerator boundary"); LiteRT-LM v0.18.0's
prebuilt plugins were built after it, our `libLiteRt` (LiteRT `26895c9f`) before (details in CLAUDE.md). On the GPU,
photo indexing (~5.7 s per photo on a Pixel Fold's CPU, ~1 s on an M1) and document indexing would be much faster.

- [ ] Blocked until LiteRT-LM releases a version whose `LITERT_REF` contains `dd3db3123`. As of 2026-10-13 its `main`
      already pins LiteRT `ca21dc99`, which does; the latest release, v0.18.0, doesn't. Either wait for the next release
      or move both submodules to a `main` commit (riskier: untested LiteRT-LM, and every native build redone).
- [ ] Update `LiteRT-LM` and `litert` together (CLAUDE.md, "The `litert` submodule must match LiteRT-LM's LiteRT
      version"), confirm with `git -C litert merge-base --is-ancestor dd3db3123 HEAD`, and rebuild the natives for
      macOS, Mac Catalyst, iOS, Android and Windows.
- [ ] Remove the "gpu" check from `EmbeddingModel.Init`; run `EmbeddingGemma2Multimodal` with `backend: "gpu"` and
      `VisionBackend = "gpu"` on the Mac (Metal) and a phone (OpenCL): embeddings within cosine 0.99 of the CPU's, the
      same text, image and audio search results, and the speed per text / image.
- [ ] A GPU embedding test, skipped without the accelerator plugin (like `TestLiteRtMobilenetGpu`).
- [ ] Demo: use the GPU for the search model when the chat's "Use the GPU" is on, with a CPU fallback; check that the
      chat model and the search model can share the GPU's memory on a phone (Gemma 4 E2B already uses ~0.8 GB of GPU
      caches), and re-time photo and document indexing.
- [ ] Windows: the same with the WebGPU plugin of item 4.

## 6. Agent tool: search documents (`search_documents`)

A document only helps the chat it is attached to, and its excerpts go with every message whether they are needed or not.
Its index is saved in the chat database (`Document` / `DocumentItem` tables), so every document ever attached can be
searched.

- [ ] Tool in `Emgu.LiteRT.LM.Agents` (`AgentTools.SearchDocuments(search)`, the app provides the search, like
      `FindPhotos`): a query in the user's words, optionally a document name.
- [ ] Demo: search the saved indexes with the embedding model already used for documents and photos
      (`EmbeddingGemma2TextVision`, `_embedderGate`); return the best passages with the document name and page,
      within the excerpt budget the attached-document path uses.
- [ ] Keep attaching a document to a chat as it is; the tool adds "in any of my documents" and lets the model decide
      when to look.
- [ ] Settings: list the saved documents with their size, and delete them.

## 7. Agent tool: search past chats (`search_chats`)

"What did we decide about the trip last week?" Memories only keep the facts the model chose to save; the saved
messages have everything.

- [ ] Embed the saved messages (user and assistant, chunked) in the background with the same embedding model, like the
      photo index; a table of message embeddings, updated when exchanges are saved and pruned when chats are deleted.
- [ ] Tool (`AgentTools.SearchChats(search)`): returns matching snippets with the chat's title and date; the demo
      also shows them as tappable links that open the chat.
- [ ] Optional date filter ("last week") with the same parsing as `PhotoRequest`.
- [ ] Setting to turn it off (and delete its index), like photo search.

## 8. Agent tools: device actions, confirmed by the user

Tools that change something outside the app, so each shows a card with what it will do and Confirm / Cancel before
anything happens (the model can get a date or time wrong).

- [ ] `set_timer` / `set_alarm`: Android's `AlarmClock` intents; iOS / Mac Catalyst have no public timer API - a
      local notification instead, or leave them out there.
- [ ] `add_calendar_event` / `add_reminder`: Android's calendar insert intent (opens the calendar app filled in);
      EventKit on iOS and Mac Catalyst (calendar and reminders permissions); Windows: an .ics file or leave out.
- [ ] `open_directions`: open the maps app with directions to a place (pairs with location and weather).
- [ ] The confirmation card in the transcript; the tool returns what the user chose, so the model can say so.
- [ ] Permissions and manifest entries per platform; step lines and saved chats show what was done.

## 9. Agent tool: clipboard (`read_clipboard`)

"Summarize what I just copied", "translate the text I copied".

- [ ] Tool (`AgentTools.ReadClipboard(read)`, the app reads MAUI's `Clipboard`): text only, trimmed to the excerpt
      budget.
- [ ] Only when a message asks for it (the tool description says so), since a clipboard can hold passwords; show a step
      line "Reading the clipboard" so the user sees it happened.

## 10. MCP support

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

## 11. Serve the Mac's models to phones ("Server" option)

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
