# LiteRT-LM chat performance

Measurements behind the chat page's context window, compaction and GPU settings (`LiteRtLmChatPage.cs`), taken on
2026-10-11 with LiteRT-LM v0.18.0, on the CPU backend (4 threads, LiteRT-LM's default) unless marked GPU.

Devices:

- **Mac:** Apple M1, 16 GB RAM. Tests run with `dotnet test` (`Emgu.TF.Lite.Test.Net`) or in the Mac Catalyst
  demo.
- **Phone:** Google Pixel Fold (Tensor G2, 12 GB RAM, Android 17). Tests run inside the demo app (Debug build).

## Context window

The context window is how many tokens a conversation can hold: system message, history, the new message and the reply
(`LanguageModel.MaxNumTokens`).

| Model | What the model file allows | LiteRT-LM's default |
|---|---|---|
| Qwen3 0.6B, Qwen3.5 0.8B / 4B | 4,096 (fixed) | 4,096 |
| Gemma 4 E2B | up to ~32k (adjustable, 32,003) | 4,096 |
| Gemma 4 E4B | the file reports 0, but the model has the same 32,003 limit and accepted 8k and 16k | 4,096 |

A conversation longer than the window fails with LiteRT-LM's `Input token ids are too long. Exceeding the maximum
number of tokens allowed: 5136 >= 4096` (new conversation) or `Prefill input length exceeds available state entries`
(continued conversation). A reply that runs into the limit just stops.

### Re-reading a full conversation

Method: a history that fills the window minus ~1,200 tokens is loaded (`Chat.LoadHistory`). It starts with a fact
("the secret code is 7341") followed by filler text. The model is then asked for the fact (32 output tokens at most).
The time is for reading the whole history and answering. Each window size ran in a fresh process. The model recalled
the fact correctly in every run.

This is what opening a saved chat or compacting a chat costs, since LiteRT-LM has to read the whole conversation
again.

| Device | Model | 4k (~3,000 tokens) | 8k (~7,400) | 16k (~15,500) | 32k (~31,000) |
|---|---|---|---|---|---|
| Mac M1 | Gemma 4 E2B | 12 s | 27 s | 82 s | 262 s |
| Mac M1 | Gemma 4 E4B | 28 s | 61 s | 168 s | - |
| Pixel Fold | Gemma 4 E2B | 123 s | 342 s | - | - |

Memory (resident size of the process, including the memory-mapped model) with the window full:

| Device | Model | 4k | 8k | 16k | 32k |
|---|---|---|---|---|---|
| Mac M1 | Gemma 4 E2B | 2.9 GB | 4.1 GB | 4.6 GB | 5.2 GB |
| Mac M1 | Gemma 4 E4B | 5.0 GB | 5.5 GB | 6.9 GB | - |
| Pixel Fold | Gemma 4 E2B | 2.6 GB (2.0 GB of the phone's RAM left) | 4.9 GB (1.5 GB left) | - | - |

Neither 16k nor 32k was tried on the phone: with 1.5 GB left at 8k, Android would likely close the app, and a run
would take over 15 minutes.

### Normal messages

Within an open Gemma 4 conversation, LiteRT-LM only processes the new message. A short follow-up ("what is the code
plus one?") in a full conversation took **6.4 s at 4k and 7.6 s at 8k** on the Pixel Fold, so the window size hardly
affects normal back-and-forth.

### What the demo uses

| | Window | Backend |
|---|---|---|
| Gemma 4 E2B / E4B / 12B on Mac Catalyst | 8,192 (`ModelOption.DesktopContextTokens`) | GPU (Metal), fp32 (`ModelOption.GpuOnMac`, Settings → "Use the GPU") |
| Gemma 4 E2B / E4B on Windows | 8,192 | CPU |
| Gemma 4 E2B on Android | 4,096 | GPU (OpenCL), fp32 (`ModelOption.GpuOnAndroid`, Settings → "Use the GPU"), CPU if unavailable |
| Everything else | 4,096 | CPU |

Gemma 4 12B is only offered on Mac Catalyst: it needs about 8 GB of memory and is practical only on a GPU.

On a phone's CPU, even a 4k conversation takes about 2 minutes to read again (about 25 tokens per second). At 8k,
opening a long saved chat or compacting would take about 6 minutes. On Android, Gemma 4 E2B therefore runs on the GPU
(next section), where a full 4k conversation takes 8 s. Phones keep the 4k window anyway: at 8k it would still take
about 30 s.

## GPU (Android, Pixel Fold)

LiteRT runs models on an Android GPU through its OpenCL accelerator, `libLiteRtOpenClAccelerator.so`. This is a
LiteRT-LM prebuilt that our `libLiteRt.so` loads at run time, shipped in the optional
`Emgu.LiteRT.OpenCL.runtime.maui.android` package. The app must declare the device's `libOpenCL.so` with
`<uses-native-library>`. The Pixel Fold's Mali GPU (Tensor G2) has a public OpenCL driver.

### fp16 or fp32

LiteRT-LM's GPU backend computes activations in 16-bit floating point (fp16) by default. They can be set to 32-bit (fp32)
with `EngineSettings.ActivationDataType`. Gemma 4 E2B on the Pixel Fold, compared with its CPU:

| | CPU | GPU, fp16 (default) | GPU, fp32 |
|---|---|---|---|
| Re-reading a full 4k chat (~3,000 tokens) | 123 s | 5.3 s | 8.0 s |
| Re-reading a full 8k chat (~7,400 tokens) | 342 s | 16.4 s | 29.2 s |
| Re-reading a full 16k chat (~15,500 tokens) | - | 51 s | - |
| Short follow-up in a full 4k chat | 6.4 s | 0.7 s | 1.0 s |
| Short follow-up in a full 8k chat | 7.6 s | 0.9 s | 1.7 s |
| "What is the code plus one?" after a full 4k chat | 7342 ✓ | **73342 ✗** | 7342 ✓ |
| The same after a full 8k chat | 7342 ✓ | **73341 ✗** | 7342 ✓ |
| "What is the secret code?" after a full 16k chat | - | **73411 ✗** | - |
| Short chats (3 turns, no long history) | correct | correct | correct |
| App memory (resident), full 4k chat | 2.6 GB | 2.2–2.4 GB | 2.9 GB |
| App memory, full 8k chat | 4.9 GB | 2.4–2.6 GB | 3.3 GB |
| First load (builds the GPU cache) | under 3 s | 29.5 s | 26.9 s |
| Later loads | under 3 s | 2.6 s | 2.6 s |
| GPU cache next to the model | - | 0.75 GB | 0.75 GB |

The GPU cache files hold compiled GPU programs and converted weights (`<model>_..._mldrift_program_cache.bin` and
`_mldrift_weight_cache.bin`). They grow when the window size changes: probe runs at several sizes in a row left 2.35 GB
and 3.9 GB. The app always uses one size.

**Why fp32:** with fp16, Gemma 4 E2B gave wrong answers once a long conversation had been loaded: it doubled digits
("73342" for 7342, "73411" for 7341), at every window size tried. Short conversations were correct. The CPU, and the GPU
with fp32, answered every one of these correctly. The likely cause is fp16's limited precision over a long context: in
attention, each new token is weighed against thousands of earlier ones, and rounding errors add up. fp32 costs about 1.5×
the time and ~0.5–0.7 GB more memory. It is still 15× faster than the CPU at reading a conversation and about 6× faster
per reply. A model that is fast but misremembers numbers and codes from earlier in the chat is not useful, so the demo
uses fp32. The cache is the same size either way.

### Other findings

- LiteRT-LM's GPU top-k sampler (`libLiteRtTopKOpenClSampler.so`) added about 25 s to every model load (3 s → 30 s even
  with a warm cache). It didn't change speed or fix the wrong answers, so it isn't shipped: LiteRT-LM then picks the next
  token on the CPU.
- In the demo, with the GPU cache in place, Gemma 4 E2B loads in 2 s. Replies in a short chat took 1.1–1.8 s (the CPU
  takes ~6 s).
- Turning "Use the GPU" off reloads the model on the CPU in 1.3 s, continues the chat, and deletes the GPU cache.
  Turning it on again rebuilds the cache (31.5 s).
- Only Gemma 4 E2B was tested on the GPU. Gemma 4 E4B, the Qwen models, and images and audio (whose encoders stay on
  the CPU) were not, nor were other phones.

## GPU (Mac, Apple M1)

On macOS, LiteRT's GPU accelerator is the Metal one, `libLiteRtMetalAccelerator.dylib` (a LiteRT-LM prebuilt: arm64,
macOS 14+, only system frameworks), in the optional `Emgu.LiteRT.Metal.runtime.maui.macos` (Mac Catalyst) and
`Emgu.LiteRT.Metal.runtime.macos` (plain macOS apps) packages. Our `libLiteRt.dylib` loads it with dlopen and just its
file name, which on macOS only finds it on the library search path or in the current folder; a plain console app
started from another folder couldn't use the GPU, so `Emgu.LiteRT.LM` loads the plugin by its full path before
creating a GPU engine. Apple M1, 16 GB, 4k window, measured with `dotnet test`:

| Gemma 4 E2B | CPU | GPU, fp16 | GPU, fp32 |
|---|---|---|---|
| Re-reading a full 4k chat (~3,000 tokens) | 12 s | 2.7 s | 3.4 s |
| Writing a long answer | - | ~42 tokens/s | ~37 tokens/s |
| Short follow-up in the full chat | - | 0.2 s | 0.3 s |
| "What is the code plus one?" after the full chat | - | **732 ✗** (twice) | 7342 ✓ (twice) |
| Later loads (GPU cache in place) | - | 1.0 s | 1.0 s |

| Gemma 4 12B, GPU | fp16 | fp32 |
|---|---|---|
| Re-reading a full 4k chat | 37.5 s | 37.6 s |
| Writing a long answer | ~7 tokens/s | ~8.6 tokens/s |
| Short follow-up in the full chat | 2.2 s | 2.2 s |
| "What is the code plus one?" after the full chat | **7341 ✗** | 7342 ✓ |
| First load (download, hash check, GPU cache) | - | 231 s |
| Later loads | - | 3.3 s |
| GPU cache next to the model | - | 6.0 GB |

In the Mac Catalyst demo, with the model already downloaded, Gemma 4 12B's first load on the GPU (building the cache) took
41.7 s. Asked to write a C# palindrome check with "Think" on, it answered in 138 s with a two-pointer solution, an
explanation and examples; Gemma 4 E2B took 51 s and built a cleaned copy of the string with LINQ instead.

The Mac's GPU shows the same fp16 problem as the Pixel Fold's, and on the M1 fp32 costs little or nothing. So the
`Gemma4` classes themselves now use fp32 on the GPU (`Gemma4.CreateEngineSettings`), for every app, not just the demo.
Google's own numbers for Gemma 4 12B on a GPU are 15 tokens/s (M4 MacBook Air 16 GB) and 29 tokens/s (M4 Pro).

## GPU (Windows, RTX 3070)

Measured on 2026-10-17 with LiteRT-LM v0.18.0 and the source-built WebGPU bundle (`platforms/windows/README.windows-gpu.md`;
Direct3D 12), on a Windows 11 workstation with an Intel Xeon Silver 4210 (4 CPU threads used), 191 GB RAM and an NVIDIA
GeForce RTX 3070 (8 GB, driver 610.88). Gemma 4 E2B, thinking off, one fresh process per row
(`UnitTestLongChatBenchmark`, `EMGU_LONGCHAT=<cpu|gpu32|gpu16>:<window>`): the history (a fact, then filler) fills the
window less ~1,200 tokens, then the model is asked for the secret code, for the code plus one, and for a ~120-word story.

| Gemma 4 E2B | CPU 4k | CPU 8k | GPU fp32 4k | GPU fp32 8k | GPU fp16 4k | GPU fp16 8k |
|---|---|---|---|---|---|---|
| History read and recalled | 15.4 s | 46.6 s | 5.9 s | 4.1 s | 5.7 s | 3.2 s |
| "What is the secret code?" | 7341 ✓ | 7341 ✓ | 7341 ✓ | 7341 ✓ | 7341 ✓ | **734141** ✗ |
| "What is the code plus one?" | 7342 ✓ | 7342 ✓ | 7342 ✓ | 7342 ✓ | **73412** ✗ | **7341** ✗ |
| Short follow-up | 1.4 s | 1.6 s | 0.2 s | 0.2 s | 0.2 s | 0.2 s |
| Writing a long answer | 7.8 tokens/s | 7.1 tokens/s | 50 tokens/s | 48 tokens/s | 47 tokens/s | 44 tokens/s |
| Process memory at the peak (GPU memory not counted) | 2.8 GB | 5.6 GB | 1.1 GB | 1.2 GB | 1.1 GB | 1.4 GB |
| Load (GPU: cache already built for another size) | 0.5-0.7 s | 0.7 s | 11.4 s | 3.7 s | 10.7 s | 12.5 s |

- **fp16 has the same problem as on the Pixel Fold and the M1:** the GPU with 16-bit activations repeated or garbled the
  digits in both window sizes, while the CPU and the GPU with fp32 answered every question correctly. Windows therefore
  keeps `Gemma4`'s fp32 default (set by `Gemma4.CreateEngineSettings`).
- fp32 costs nothing measurable here: reading the history took 4-6 s against 3-6 s, and decode speed was 48-50 tokens/s
  against 44-47. The GPU reads a full 8k conversation 11 times faster than the CPU and writes 6-7 times faster.
- These are single runs on one machine, with the model and its caches already on disk; the loads include building the
  GPU cache the first time a window size is used. The CPU numbers use 4 threads, like the rest of this file.
- Not measured: Gemma 4 E4B and 12B (the 12B is CPU-only on Windows, see `platforms/windows/GPU-MODEL-COMPATIBILITY.md`),
  16k and 32k windows, images and audio, or other GPUs.

## Token costs

Method: a single message sent with the window set to 128 tokens, so that LiteRT-LM's error reports the message's exact
size.

| Input | Tokens |
|---|---|
| Text | `LanguageModel.CountTokens` (the model's tokenizer). For a ~5,100-token history it estimated 5,100 against LiteRT-LM's 5,136 |
| Chat template, per message | ~8 |
| Image, 1024×1024, Gemma 4 E2B | ~260 (269 with "Hi" and the template) |
| Image, 1024×1024, Qwen3.5 0.8B VL | ~260 (271 with "Hi" and the template) |
| Audio, Gemma 4 E2B | ~25 per second (`audio_sample.wav`, 5.8 s: 156 with "Hi" and the template) |

The chat page's estimate uses 270 per image, 25 per second of audio, and 8 per message. It also counts 450 per web
search step and, until the conversation is rebuilt, the reply's thinking. It sets aside 1,024 tokens for the reply,
plus 50 for the chat template or 600 for the agent's instructions and tools.

## Compaction (Mac Catalyst, Gemma 4 E2B, 4k window)

Two ~1,700-token messages after a message with three facts (name, cat, project codename) took the chat over the
limit:

- The model summarized the first exchange, and the second message was answered within about 10 s, including the
  summary.
- Asked afterwards, the model answered "Your cat is called Miso, and your project codename is BLUE HERON." After the
  saved chat was reopened, it answered "Your name is Kenji."
- Qwen3 0.6B wrote poor summaries: it copied the instructions' example and repeated itself. The small models drop
  earlier messages instead of summarizing them.

## Model loading (Pixel Fold, Gemma 4 E2B)

| | Time |
|---|---|
| First load after download | 11 s (9 s of it is a one-time SHA-256 check of the 2.6 GB file) |
| Later loads | under 3 s |
| Engine creation on the Mac (file already checked) | 0.2–1.6 s |
