# LiteRT-LM chat performance

Measurements behind the chat page's context window and compaction settings (`LiteRtLmChatPage.cs`), taken on
2026-10-11 with LiteRT-LM v0.18.0 on the CPU backend (4 threads, LiteRT-LM's default).

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

| | Window |
|---|---|
| Gemma 4 E2B / E4B on Mac Catalyst and Windows | 8,192 (`ModelOption.DesktopContextTokens`) |
| Everything else, and all models on phones | 4,096 |

On a phone, even a 4k conversation takes about 2 minutes to read again (about 25 tokens per second). At 8k, opening a
long saved chat or compacting would take about 6 minutes. Running on the GPU would be the real fix (LiteRT-LM supports
it, but its Android GPU plugin isn't built yet).

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
