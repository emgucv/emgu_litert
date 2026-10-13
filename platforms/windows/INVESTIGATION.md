# Windows WebGPU investigation

## Result

The prebuilt Windows WebGPU accelerator is incompatible with our source-built LiteRT runtime at the private TFLite C++ object boundary. GPU hardware discovery succeeds; model creation crashes before generating a response. Replacing only the test runtime with the matching upstream prebuilt runtime makes Gemma 4 E2B chat work on the RTX 3070 through WebGPU / Direct3D 12. CPU chat also works with that bundled runtime.

No production files, installed native DLLs, or checked-out submodule pins were changed. All experimental DLL replacements and runtime code redirects were confined to the test executable under `.tmp/windows-gpu-smoke`. The redirect was disabled for the successful paired-runtime tests.

## Versions tested

- LiteRT source pin: `26895c9fbcc25c43faa8c1a98cd1fd28951602c3`.
- LiteRT-LM source pin: `b2f686e2ed4718fb84ec398a61dd59ca0f0aff27` (v0.18.0).
- Experimental LiteRT source: `dd3db312368c66a299d7df5f57c6659658671556`, the October 8 WeightLoader ABI fix. Checked out only in `.tmp/litert-abi-test`.
- WebGPU plugin SHA256: `2012B4E0225BA9E6F2D5027177FE207F8020409DD5A074F976EAD0D58DE3F810`.
- Upstream prebuilt runtime SHA256: `D027BCEB59DB9402ABE3B20F61E3E6EAB329B4F7C5665E7228B64871E7164C8A`.
- Model: existing `gemma-4-E2B-it.litertlm`; CPU/GPU model, 1,024-token context, float32 activations, thinking disabled, 24-token output limit.

## Native evidence

The first access violation occurs at `libLiteRtWebGpuAccelerator.dll + 0xAA7A31`, while its instruction reads a pointer from `this + 0x248`. The pointer value contains `0xBAADF00DFFFFFFFF`, consistent with an integer -1 followed by padding being interpreted as a pointer.

The disassembled function and its StableHLO-composite caller match `tflite::Subgraph::MarkSubgraphAsDelegationSkippable`. The source GPU model builder explicitly casts `TfLiteContext::impl_` to `tflite::Subgraph*` and calls that private C++ method. The prebuilt plugin has no matching PDB available; function identification uses the source/caller/instruction match, not an exact debug-symbol name.

Compiler layout output for the experimental source-built runtime:

| Field | Runtime offset | Accelerator instruction offset |
|---|---:|---:|
| `context_` | `0x28` | `0x28` |
| `subgraphs_` | `0x238` | `0x248` |
| `is_delegation_skippable_` | `0x2C8` | `0x2E0` |

The runtime field at `0x248` is actually `dynamic_tensor_index_`, followed by four padding bytes. Thus the accelerator's pointer read accesses the wrong member.

A process-local diagnostic redirected this one function to the host runtime's exported `TfLiteOpaqueContextMarkSubgraphAsDelegationSkippable` C API. The first three calls succeeded with status 0. Model creation then crashed at another accelerator address (`+0xA6FEE0`). This occurred with both the original runtime and the experimental ABI-fixed runtime. The mismatch is broader than one function, and neither a single redirect nor the WeightLoader source update is sufficient.

## Controlled tests

| Runtime | Accelerator | Result |
|---|---|---|
| Existing source-built runtime | Upstream prebuilt WebGPU | Native access violation during model creation |
| Source-built runtime at WeightLoader fix | Same WebGPU binary | Same initial access violation |
| Either source-built runtime, first call redirected | Same WebGPU binary | Initial calls succeed; another accelerator access violation follows |
| Upstream paired prebuilt runtime | Same WebGPU binary, no redirects | GPU chat returns `Hello!`, exit 0 |
| Upstream paired prebuilt runtime | CPU backend, no redirects | CPU chat returns `Hello!`, exit 0 |

Successful GPU discovery names `NVIDIA GeForce RTX 3070`, `backend=Direct3D 12`. These short smoke tests establish functionality, not comparative performance or large-context VRAM suitability.

## Source-build issues

Building the accelerator directly in LiteRT-LM's workspace fails because `@ml_drift` is undeclared and `generate_build_identifier_py` specifies an unsupported `strict_deps` attribute. The pinned LiteRT WORKSPACE's `ml_drift` archive also supplies no URL, so a complete, reproducible matching accelerator source build needs additional upstream dependency metadata.

The newer runtime itself builds after removing `LiteRtStaticLinkedAcceleratorCpuDef` from the temporary staged export list; the symbol was removed upstream. The original staged list was restored. Updating only this runtime still does not fix the GPU crash.

## Integration paths

1. Use the complete upstream Windows runtime/accelerator bundle as a matched unit: `libLiteRt.dll`, `libLiteRtWebGpuAccelerator.dll`, `webgpu_dawn.dll`, optional `libLiteRtTopKWebGpuSampler.dll`, and DirectX Shader Compiler DLLs. This is the experimentally working route. It requires resolving the repository's uniform source/version policy before deployment, rather than silently replacing the source-built runtime.
2. Rebuild runtime and accelerator together from one complete source/dependency/toolchain set. The accelerator's direct private TFLite C++ accesses also need compatible layouts; a shared LiteRT version label or a WeightLoader fix alone does not guarantee this.
3. Upstream can remove private object accesses across the DLL boundary, using runtime-owned C APIs/callbacks instead. The diagnostic redirect proves that the existing C API can perform the first failing operation without reading the accelerator's assumed object layout, but other accesses remain.

Export inspection: all 42 imported LiteRT functions needed by our source-built `liblitert-lm.dll` exist in the bundled runtime. Of 39 managed Emgu.LiteRT P/Invoke functions, 38 exist; `LiteRtGetStatusString` is missing and needs an error-description fallback if that runtime is adopted. Runtime availability is not full application validation: embedding, image generation, long conversations, GPU OOM fallback, and legacy TF Lite demos still need tests before shipping.

## Evidence files

The local investigation artifacts below are in `.tmp/windows-gpu-smoke/` and are not included in this commit.

- `native-trace.txt`, `native-trace-abi-fixed.txt`: initial crash traces.
- `subgraph-layout-build.log`: compiler class-layout report.
- `abi-fixed-runtime-build.log`, `abi-fixed-gpu-test.log`: experimental source runtime build and crash.
- `native-trace-with-bridge.txt`, `native-trace-original-with-bridge.txt`: diagnostic redirect traces.
- `paired-runtime-gpu-confirmation.log`, `paired-runtime-cpu-confirmation.log`: working GPU/CPU chat controls.
- `accelerator-build.log`: source accelerator build failures.
- `GpuSmoke.csproj`, `Program.cs`: reproducible isolated test harness.

The optional `GpuAbiProbe` is strictly diagnostic, uses version-specific code offsets, and must not be integrated into the application or shipped.
