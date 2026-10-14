# Windows model GPU verification

Verified October 14, 2026 on an NVIDIA GeForce RTX 3070 (8 GB), using the
source-built WebGPU / Dawn Direct3D 12 bundle and Windows runtime packages
2.3.0.1964. This covers the **five chat models offered by the Windows picker**,
the document embedding model, and GPU compilation checks for image generation.
Gemma 4 12B is Mac-only; other embedding variants and TF Lite demo models are
outside this verification.

## Results

| Model | Tested context | GPU result | Additional inputs | GPU activations |
|---|---:|---|---|---|
| Qwen3 0.6B, dynamic int4 | 4,096 | Passed | Text | Default |
| Qwen3.5 0.8B, VL int8 | 4,096 | Passed after checksum correction | Image | Default |
| Qwen3.5 4B, mixed int4 | 4,096 | Passed after precision correction | Text | Float32 required in this build |
| Gemma 4 E2B | 8,192 | Passed | Image and audio | Float32, existing wrapper default |
| Gemma 4 E4B | 8,192 | Passed | Image and audio | Float32, existing wrapper default |

Each passing run created an engine with `backend: "gpu"`, returned a greeting,
then answered `2 + 3` with `5` in the same conversation. Logs confirmed WebGPU
execution, and the passing runs had **zero Dawn validation errors**. Thinking
was disabled and output was capped at 96 tokens. Multimodal checks used the
demo's `space_shuttle.jpg` and LiteRT-LM's `audio_sample.wav`, in fresh chats.

Image/audio encoders and token sampling remained on CPU, as in the demo. These
are runtime smoke tests, not answer-accuracy benchmarks. E2B's image description
was imperfect on both CPU and GPU; its audio transcript matched the CPU control.
Long chats, thinking, tools, larger contexts, and other GPU vendors were not
exhaustively tested.

## Fixes found

### Auxiliary models: document embedding and image generation

| Model / component | Windows GPU result | Application behavior |
|---|---|---|
| EmbeddingGemma 2 Text Vision 440M, document embedding | Passed text and image checks, including GPU vision encoder | Still CPU; managed wrapper currently refuses GPU |
| Z-Image-Turbo, complete GPU pipeline | Not compatible with the tested bundle: required text encoder fails compilation | Still CPU |
| Z-Image `z_embx` input graph | GPU FP32 compiled and ran with finite outputs | Direct graph test only |
| Z-Image `zc_main0` denoising graph | GPU FP32 compiled and ran with finite outputs | Direct graph test only |

Embedding used the actual demo model, SHA256
`92dcbea108899e5d6e30d919b0744f90d9967e80c67a4ab5503ac16d54f62eb0`.
An isolated harness called `EmbeddingEngine` directly, bypassing
`EmbeddingModel.Init`'s historical GPU refusal only in the test. Three document
passages, a search query, and `space_shuttle.jpg` produced finite, normalized
768-value vectors on CPU/CPU, GPU/CPU, and GPU/GPU (main/vision backends).
CPU-versus-GPU cosine similarity was **0.9971–0.9991**, above the 0.99 criterion.
The capital-of-France query ranked the Paris passage first; the shuttle image
ranked the space-shuttle passage first, with the same rankings on all backends.
The passing GPU runs had no Dawn validation errors.

The matched source-built Windows core and accelerator avoid the external-weight
ABI mismatch described in `EmbeddingModel.cs`. This result does **not** establish
compatibility with the newer prebuilt Metal/OpenCL accelerators on other
platforms. The cross-platform guard and the demo's CPU default remain unchanged;
enabling embedding GPU needs a suitably scoped follow-up. The text-only 270M
and audio-capable 740M embedding variants were not tested here.

Z-Image used the cached graphs for LiteRT model revision
`09ea3ae2ef44d04d0ad1591de36d1c13504e1521`. Its required `qwen_enc.tflite`
failed `CompiledModel` construction with `ErrorCompilation` even with explicit
FP32 GPU options. The delegate reported unsupported `RESHAPE` operations
("Tensor dimensions must be less than 5") and partitioned only **76 operations
onto GPU**, leaving **2,794 on CPU**, before compilation failed. The same encoder
compiled and ran on CPU, returning finite outputs. Thus the cached encoder is
usable, but requesting GPU does not work with this Windows bundle.

The two passing Z-Image graph checks used synthetic float inputs; they establish
compilation/execution only, not generated-image quality or speed. No complete
GPU image was generated. A hybrid CPU text encoder / GPU denoiser may be possible,
but the remaining graphs, real pipeline values, image quality, and memory use
must be verified before exposing that option. The failure observed here is a
compilation restriction, not evidence that GPU memory was exhausted.

Local reproduction: `.tmp/wgpu/aux-check/Aux.csproj` and `Program.cs`, with
per-process logs in that folder. Run the apphost executable with arguments
`embedding cpu cpu`, `embedding gpu cpu`, `embedding gpu gpu`,
`z_embx gpu fp32`, `zc_main0 gpu fp32`, `qwen_enc gpu fp32`, or `qwen_enc cpu`.
Embedding vectors are retained as JSON alongside the logs. These local harness
files are ignored build artifacts.

### Qwen3.5 0.8B vision-model download checksum

The definition expected SHA256 `e3360b65...`, but its URL now serves the October
12 re-quantized model. The downloaded file matched the provider's LFS metadata
at size 1,303,966,384 bytes and SHA256:

```
5e6d69e55f92d21799fe0a2552da979c95a2533b64e4f0ed73fe87dd9d759ab9
```

`Qwen35_0_8B_VL.ModelFile` now uses that hash. The
[provider's model update](https://huggingface.co/litert-community/Qwen3.5-0.8B/commit/f742da846a44d3788a5aee27e48681656c409de8)
identifies the replacement. Testing with the corrected repository definition
also passed. Other tested files matched their existing download definitions.

### Qwen3.5 4B GPU precision

The default 16-bit run loaded and returned a greeting, but its second reply was
empty. Dawn reported a buffer used as both read-only and read-write within the
same synchronization scope, followed by invalid command-buffer submissions.
The same conversation passed on CPU.

The Float32 GPU control passed with zero validation errors. The model wrapper
now selects Float32 on GPU, and the subsequent default-wrapper run also passed.
Raw-engine callers should set `EngineSettings.ActivationDataType` to
`ActivationDataType.Float32` for this model on GPU. The workaround is verified;
the underlying buffer-aliasing cause has not been fully diagnosed.

## Loading and memory limits

- Qwen3 initialized in about 5 seconds.
- Qwen3.5 0.8B's first GPU initialization took about 129 seconds.
- Qwen3.5 4B took about 238 seconds with 16-bit activations, 211 seconds for
  the Float32 control, and 206 seconds on the subsequent default-wrapper run.
  Recoverable deserialization warnings preceded recompilation. Repeated loads
  can still take minutes with this bundle.
- Repeated Gemma E2B/E4B checks initialized in about 5/7 seconds. These models
  had been loaded in earlier tests; these figures are not cold-load benchmarks.
- Total GPU memory use approached 7 GB during the large Qwen run, including
  desktop usage. This does not guarantee every 8 GB GPU or context setting fits.

The demo's Windows GPU setting now covers all five verified chat choices.
Windows defaults to CPU and falls back to CPU on GPU initialization failure.
Loading text warns that GPU preparation can take several minutes.

## Reproduction

The isolated harness and logs are untracked artifacts under
`.tmp/wgpu/model-gpu-check`. It uses the actual model wrappers and native
packages, has no GPU-to-CPU fallback, and runs GPU models in separate processes.
Qwen3.5 files were downloaded in ranged chunks after whole-file transfers
stalled, then SHA256-verified before testing.

```powershell
dotnet build .tmp/wgpu/model-gpu-check/Check.csproj
.tmp/wgpu/model-gpu-check/bin/Debug/net10.0/win-x64/Check.exe qwen3 gpu
.tmp/wgpu/model-gpu-check/bin/Debug/net10.0/win-x64/Check.exe qwen35vl gpu 4096 image
.tmp/wgpu/model-gpu-check/bin/Debug/net10.0/win-x64/Check.exe qwen35_4b gpu 4096
.tmp/wgpu/model-gpu-check/bin/Debug/net10.0/win-x64/Check.exe gemmae2b gpu 8192 image audio
.tmp/wgpu/model-gpu-check/bin/Debug/net10.0/win-x64/Check.exe gemmae4b gpu 8192 image audio
```

Native revisions: LiteRT `26895c9f`, LiteRT-LM `b2f686e2` (v0.18.0), ML Drift
`615fe921`, Dawn `v20260720.160313`, with the checked-in Windows GPU patches.
Exact DLL hashes and tool versions are in `.tmp/wgpu/artifacts/gpu-build.json`.
