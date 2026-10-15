# Agent regression suite

Run this after a LiteRT-LM/native runtime update, a Gemma model change, or an edit to tool descriptions, schemas or
result wording. It uses the real Gemma 4 E2B model and the same streaming Microsoft Agent Framework API as the demo.
Every case starts a fresh client and session with all eight tools available; tool choice is automatic. Thinking is
off, inference uses four CPU threads, the context limit is 4096 tokens and each model response is capped at 160 tokens.
The function loop is capped at four requests and each scenario has a 90-second cancellation timeout.

The production tool names, descriptions and schemas pass through `DelegatingAIFunction`. External work is replaced
with fixed results: no weather/search HTTP requests, photo library, saved chats or image model is used. Memory tools
use an isolated in-memory store and their real result wording. A throwing HTTP handler also blocks accidental network calls in weather. The chat model may
download on its first use (about 2.6 GB); its expected SHA-256 is checked by the normal model loader. After caching
that model, the suite needs no network. `generate_image` success wording is shared with the demo through
`AgentTools.ImageGeneratedResult`, so its empty-reply regression is exercised when that text changes.

| Scenario | Expected tool | Argument checks |
| --- | --- | --- |
| What's 17% of 230? | `calculate` | Expression evaluates to 39.1 |
| Weather in Paris now | `get_weather` | Place contains Paris |
| Draw a cat | `generate_image` | Prompt contains cat and has at most 20 words |
| Show my beach photos | `find_photos` | Request preserves beach |
| Remember I'm vegetarian | `remember` | Fact preserves vegetarian |
| Convert 26.2 miles to kilometres | `convert_units` | Value and units convert to 42.1648128 km |
| Search saved Paris travel decisions | `search_chats` | Request preserves Paris |
| Draw a cat, with generation disabled | `generate_image` | Same prompt checks, with an unavailable result |

Every scenario must invoke the expected tool, avoid unexpected tools/repeated loops, and finish with a nonempty
assistant text message after tool execution. Interim text or a tool call alone does not count as a reply. All cases
run even when one fails, and every repetition must pass; a later success cannot hide an earlier failure. Sampling
options are the runtime defaults, so repeated runs measure observed stability rather than guaranteeing determinism.

From the repository root, on a machine with the matching native runtime:

```sh
dotnet test Emgu.LiteRT.Test/Emgu.TF.Lite.Test/Emgu.TF.Lite.Test.Net/Emgu.TF.Lite.Test.Net.csproj \
  -p:TestModels=true --filter FullyQualifiedName~UnitTestAgentRegression \
  --logger 'trx;LogFileName=agent-regression.trx' --results-directory TestResults/AgentRegression
```

Set `EMGU_AGENT_REGRESSION_REPEATS=3` (1–10 accepted) to repeat each scenario three times. In PowerShell set
`$env:EMGU_AGENT_REGRESSION_REPEATS = '3'` before the command. The default is one pass over all eight scenarios.
Without `-p:TestModels=true`, the real-model test is skipped and the three model-free harness tests still run.
An opted-in run fails if the native runtime/model cannot load; it does not silently skip that baseline.

The NUnit attachment `agent-regression-results.json` records the pass rate, each prompt, calls/arguments/results,
reply, errors and duration. It also records the model URL/hash, tool-contract hash, framework/platform, backend and
generation limits. Preserve it with the TRX results and record the source/runtime pins alongside each baseline.
The suite checks tool routing, argument intent and reply presence; it does not grade all answer quality or test real
external service integrations.

## Recorded runs

| Date | Runtime/model | Scenarios | Result |
| --- | --- | --- | --- |
| 2026-10-15, before memory-result fix | LiteRT-LM v0.18.0, Gemma 4 E2B CPU | 8 × 3 | 21/24 (87.5%); all selected tools correctly, but every `remember` reply was empty |
| 2026-10-15, with acknowledgement instruction | Same runtime/model | 8 × 3 | 24/24 (100%); final replies present, including all three memory acknowledgements |

Both runs used macOS 27.0.1 arm64, .NET 10.0.12, Microsoft Agent Framework 1.24.0 and constrained decoding. The
passing run took about 141 seconds for the 24 scenarios. All three model-free harness tests and the existing
`TestAgentMemoryTools` passed alongside it. These numbers describe this fixed corpus/backend, not a general model
accuracy score or results on other platforms.

Source pins: LiteRT-LM `b2f686e2`, LiteRT `26895c9fb`.

- Model SHA-256: `181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c`.
- Tool-contract SHA-256: `E7DE0C564147DD4C4D61F21868B973BC3597FD7105D3E1835205053A562ADD60`.
- Tested `liblitert-lm.dylib` SHA-256: `4186154175ca04bbcf8051dde022baf4a851b0982fa887348f836db7360798f1`.

The failure was corrected in the production `remember` result by asking the model to acknowledge the saved fact in
one short sentence. Memory storage and duplicate detection are unchanged; the result also carries that instruction
when the fact was already saved. The passing test uses that production result with its isolated in-memory store.
