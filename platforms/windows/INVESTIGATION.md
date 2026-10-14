# Windows WebGPU investigation

## Result

**The matched source-build route now works in an isolated Windows chat test.** The source-built LiteRT runtime, WebGPU accelerator, and Dawn DLL create Gemma 4 E2B and return `Hello!` on the NVIDIA RTX 3070 through Direct3D 12, without the diagnostic ABI redirect or shader validation errors. The CPU control also returns `Hello!`; both processes exit 0. GPU inference uses CPU token sampling because the optional prebuilt GPU sampler was deliberately excluded.

The native build blockers are resolved in experimental source copies. The normal Windows build script and MAUI GPU settings have not been changed, and other models have not yet been validated with this stack.

The prebuilt Windows WebGPU accelerator is incompatible with our source-built LiteRT runtime at the private TFLite C++ object boundary. GPU hardware discovery succeeds; model creation crashes before generating a response. Replacing only the test runtime with the matching upstream prebuilt runtime makes Gemma 4 E2B chat work on the RTX 3070 through WebGPU / Direct3D 12. CPU chat also works with that bundled runtime.

No production files, installed native DLLs, or checked-out submodule pins were changed. All experimental DLL replacements and runtime code redirects were confined to the test executable under `.tmp/windows-gpu-smoke`. The redirect was disabled for the successful paired-runtime tests.

## Versions tested

- LiteRT source pin: `26895c9fbcc25c43faa8c1a98cd1fd28951602c3`.
- LiteRT-LM source pin: `b2f686e2ed4718fb84ec398a61dd59ca0f0aff27` (v0.18.0).
- Experimental LiteRT source: `dd3db312368c66a299d7df5f57c6659658671556`, the October 8 WeightLoader ABI fix. Checked out only in `.tmp/litert-abi-test`.
- WebGPU plugin SHA256: `2012B4E0225BA9E6F2D5027177FE207F8020409DD5A074F976EAD0D58DE3F810`.
- Upstream prebuilt runtime SHA256: `D027BCEB59DB9402ABE3B20F61E3E6EAB329B4F7C5665E7228B64871E7164C8A`.
- Model: existing `gemma-4-E2B-it.litertlm`; CPU/GPU model, 1,024-token context, float32 activations, thinking disabled, 24-token output limit.

## Successful source build

The reusable [bazel_build_windows_gpu.ps1](bazel_build_windows_gpu.ps1) now automates source staging, patch application, Dawn and Bazel builds, and bundle verification. See [README.windows-gpu.md](README.windows-gpu.md) for prerequisites, options, and output locations. It produces an isolated DLL bundle and does not enable GPU selection in the MAUI demo.

On October 14, a clean invocation prepared fresh sources under `.tmp/wgpu`, built all six DLLs, and passed CPU/GPU chat controls with `Hello!`, exit 0, and zero GPU shader validation errors. An incremental `-Jobs 24` invocation also passed with no native recompilation. The new build uses the checksum-verified Dawn archive directly; its C++20 build does not require the earlier dependency rule's C++17 template adaptations. This confirms the script does not depend on the prototype's prepared source folders. New build metadata is in `.tmp/wgpu/artifacts/gpu-build.json`, with cold-build, incremental-build, and smoke-test logs under `.tmp/wgpu/logs`.

The successful runtime and accelerator use the original LiteRT pin (`26895c9f`), the LiteRT-LM v0.18.0 workspace, Bazel 7.6.1, and Visual Studio 2026 Professional / MSVC 14.51.36231. The experimental WeightLoader update is not required for this test.

The GPU engine source is publicly available at [google-ai-edge/ml-drift](https://github.com/google-ai-edge/ml-drift). The experiment uses revision `615fe9213315590e871e8474d7565d6a9c20a8e3`, the September 28 move of the experts-remap kernel into the public `kernels/special` directory. Relative to the earlier September 25 revision, this adds that kernel without unrelated engine changes. The original `litert/` and `LiteRT-LM/` checkouts and pins remain unchanged.

Build adaptations are preserved in [gpu_source_patches](gpu_source_patches/):

- [litert.patch](gpu_source_patches/litert.patch): remove the unsupported Python `strict_deps` attribute, use the public experts-remap kernel path, and allow the GPU IR builder to depend on TFLite's stable C++ target.
- [ml-drift.patch](gpu_source_patches/ml-drift.patch): correct LiteRT repository labels for custom parsers and source-file visibility, map the FP16 target name, and explicitly convert four unsigned sentinel initializers for MSVC.
- [dawn-external-dxc.patch](gpu_source_patches/dawn-external-dxc.patch): enable Dawn's DXC runtime path while using externally deployed Windows SDK shader-compiler DLLs, avoiding an LLVM/DXC source build. With this option, `copy_dxil_dll` is only a dependency placeholder; the caller must deploy the SDK DLLs.

All patches apply to staged experimental copies. The final Bazel accelerator build passes with normal visibility checks enabled.

### Dawn and shader compilers

Dawn is built from `v20260720.160313`, the version in the pinned LiteRT dependency declaration. It was initially fetched through LiteRT's `third_party/dawn/workspace.bzl`, including its C++ compatibility template patches, then copied into `.tmp/dawn-source` for CMake. The Direct3D 12 monolithic shared library is built with Visual Studio 2026, C++20, `/MP12 /Zc:__cplusplus`, and these options:

```text
DAWN_FETCH_DEPENDENCIES=ON
DAWN_BUILD_MONOLITHIC_LIBRARY=SHARED
DAWN_ENABLE_D3D12=ON
DAWN_USE_BUILT_DXC=ON
DAWN_USE_EXTERNAL_DXC=ON
DAWN_ENABLE_D3D11=OFF
DAWN_ENABLE_VULKAN=OFF
DAWN_ENABLE_DESKTOP_GL=OFF
DAWN_ENABLE_OPENGLES=OFF
DAWN_ENABLE_NULL=OFF
DAWN_USE_GLFW=OFF
DAWN_BUILD_TESTS=OFF
DAWN_BUILD_SAMPLES=OFF
DAWN_BUILD_PROTOBUF=OFF
TINT_BUILD_TESTS=OFF
TINT_BUILD_CMD_TOOLS=OFF
```

`/Zc:__cplusplus` is necessary: without it, the generated header selects its C++17 span fallback while the standard library already exposes C++20 `std::span`.

Deploy `dxcompiler.dll`, `dxil.dll`, and `d3dcompiler_47.dll` from Windows SDK `10.0.28000.0/bin/x64` beside the executable. Omitting FXC first caused device creation to fail. Building Dawn without DXC then created the model but rejected `enable f16` shaders and produced an empty response, despite exit code 0. Those runs are failures. Enabling DXC produces a nonempty answer with no shader validation errors.

### Bazel workspace wiring

The isolated workspace is `.tmp/gpu-workspace`, staged from LiteRT-LM's root workspace files, `third_party`, and the Emgu `tfliteextern` package. It declares `ml_drift` as a local repository with `@fp16` mapped to `@FP16`. LiteRT is overridden with `.tmp/litert-gpu-source`, which contains the original pin plus `litert.patch`.

`.tmp/dawn-bundle` contains Dawn's source and generated public headers, the source-built `webgpu_dawn.dll`, and its import library. Its Bazel package exposes `webgpu_dawn` through `cc_import` and aliases `webgpu_headers`, `dawn_headers`, and `libdawn_proc` to that library. This replaces the pinned Dawn Bazel rules, which provide headers but no implementation and still reference internal Python/Abseil paths.

Both targets are built in this workspace using the same configuration:

```text
--config=windows --cpu=x64_windows --define=tflite_with_xnnpack=true -c opt
--override_repository=litert=<repo>/.tmp/litert-gpu-source
```

Targets: `//tfliteextern:libLiteRt` and `@litert//litert/runtime/accelerators/gpu:libLiteRtWebGpuAccelerator`. The local experimental runner is `.tmp/windows-gpu-smoke/build-source-gpu.ps1`.

### Verified artifacts

| DLL | Bytes | SHA256 |
|---|---:|---|
| `libLiteRt.dll` | 6,273,536 | `C1DE7EE4B2B311CAE67B4DC8E78ACE8E51BD99701555CBF510BBF010ADEC9901` |
| `libLiteRtWebGpuAccelerator.dll` | 9,970,176 | `14EDA35C750B1717975B4DE2DC687EF8D5C103070F94B0E6D649A66E7113A057` |
| `webgpu_dawn.dll` | 6,672,896 | `87472916F32D6044DBD4997172E5DBCCB8171BA8E49212355A64B2DF3ED93631` |

These DLLs are deployed only in `.tmp/windows-gpu-smoke/bin/Debug/net10.0`. The application runtime under `lib/runtimes/win-x64/native` still has its original SHA256 `CE290A6C192678C66AF9BFEB67055161547E7BEEA25BE48818C3E976BC92BB22`.

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
| Original-pin source-built runtime | Matching source-built accelerator and DXC-enabled Dawn; no redirects or GPU sampler | GPU chat returns `Hello!`, no shader validation errors, exit 0 |
| Same source-built stack | CPU backend | CPU chat returns `Hello!`, exit 0 |

Successful GPU discovery names `NVIDIA GeForce RTX 3070`, `backend=Direct3D 12`. These short smoke tests establish functionality, not comparative performance or large-context VRAM suitability.

## Initial source-build issues

Initially, building the accelerator directly in LiteRT-LM's workspace failed because `@ml_drift` was undeclared and `generate_build_identifier_py` specified an unsupported `strict_deps` attribute. The pinned LiteRT WORKSPACE's `ml_drift` archive also supplied no URL. The successful source-build section above records the missing public source and build adaptations.

The newer runtime itself builds after removing `LiteRtStaticLinkedAcceleratorCpuDef` from the temporary staged export list; the symbol was removed upstream. The original staged list was restored. Updating only this runtime still does not fix the GPU crash.

## Integration paths

1. Use the complete upstream Windows runtime/accelerator bundle as a matched unit: `libLiteRt.dll`, `libLiteRtWebGpuAccelerator.dll`, `webgpu_dawn.dll`, optional `libLiteRtTopKWebGpuSampler.dll`, and DirectX Shader Compiler DLLs. This is the experimentally working route. It requires resolving the repository's uniform source/version policy before deployment, rather than silently replacing the source-built runtime.
2. Rebuild runtime and accelerator together from one complete source/dependency/toolchain set. This route now passes the isolated chat test and is the recommended integration path. The accelerator's direct private TFLite C++ accesses still need compatible layouts; a shared LiteRT version label or a WeightLoader fix alone does not guarantee this. Integration still needs automated source staging/patch application in the normal Windows build, DLL packaging and loading, MAUI GPU selection, and tests of the other model families.
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
- `source-runtime-build.log`, `source-accelerator-build.log`: successful matching runtime and accelerator builds.
- `dawn-dxc-configure.log`, `dawn-dxc-build.log`: DXC-enabled Dawn source build.
- `source-paired-gpu-test.log`, `source-paired-cpu-test.log`: successful source-built GPU and CPU controls.
- `source-fxc-gpu-test.log`: failed FXC-only shader validation control; exit 0 did not imply correct inference.

The optional `GpuAbiProbe` is strictly diagnostic, uses version-specific code offsets, and must not be integrated into the application or shipped.
