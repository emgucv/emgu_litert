# Building Windows GPU support

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File platforms/windows/bazel_build_windows_gpu.ps1
```

The script prepares pinned LiteRT, LiteRT-LM, ML Drift, and Dawn source copies,
applies the checked-in [GPU source patches](gpu_source_patches/), builds Dawn with
Direct3D 12 and DXC support, and builds the LiteRT runtime and WebGPU accelerator
with the same Bazel configuration. Source submodules are not modified.

The default build directory is `.tmp/wgpu`. Re-running the command reuses source
downloads, Dawn's CMake build, and Bazel's build cache. An interrupted preparation
can be retried. If the source pins or patch contents change, the script stops;
use a fresh build directory after updating and validating the patches.

## Prerequisites

- Windows x64.
- Git for Windows, including Bash.
- Bazel **7.6.1** or Bazelisk, which selects the workspace's pinned version.
- Python **3.11 or later**, available as `python.exe` or supplied explicitly.
- Visual Studio **2022 or 2026** with Desktop development with C++.
- CMake, on PATH or installed with Visual Studio.
- Windows SDK with x64 `dxcompiler.dll`, `dxil.dll`, and `d3dcompiler_47.dll`.
  SDK **10.0.28000.0** was used for the validated source experiment.
- Network access and space for the source trees, dependency downloads, and native builds.

Initialize `litert` and `LiteRT-LM` at the repository's checked-in pins before
building. The patches currently support LiteRT `26895c9f` and LiteRT-LM
`b2f686e2` (v0.18.0); the script checks both revisions and their agreement.

## Output

The default output folder is `.tmp/wgpu/artifacts`:

| File | Purpose |
|---|---|
| `libLiteRt.dll` | Matching LiteRT runtime with XNNPACK CPU support |
| `libLiteRtWebGpuAccelerator.dll` | Source-built GPU accelerator |
| `webgpu_dawn.dll` | Source-built Dawn WebGPU / Direct3D 12 implementation |
| `dxcompiler.dll`, `dxil.dll`, `d3dcompiler_47.dll` | Windows SDK shader compilers |
| `gpu-build.json` | Source/tool versions, patch hashes, and output DLL hashes |

The script checks that every DLL is an x64 PE library before copying the bundle.
It exits on a failed download, patch, configuration, compile, or artifact check.
Step logs are under `.tmp/wgpu/logs`; a failure names the relevant log.

Deploy the runtime, accelerator, Dawn, and shader compiler DLLs together beside
the executable. The optional prebuilt GPU sampler is excluded: LiteRT-LM uses GPU
inference and CPU token sampling. An existing compatible `liblitert-lm.dll` and
`libGemmaModelConstraintProvider.dll` from the normal Windows native build are
still needed for chat. This script does not build managed code or enable GPU
selection in the MAUI demo.

The script's default output is isolated; it does not replace
`lib/runtimes/win-x64/native`. Treat the bundle as the source-build candidate
documented in [INVESTIGATION.md](INVESTIGATION.md), pending application integration
and validation of the other model families.

## Options

```powershell
# Limit compilation parallelism.
powershell -NoProfile -ExecutionPolicy Bypass -File platforms/windows/bazel_build_windows_gpu.ps1 -Jobs 8

# Use another isolated build folder and export the DLL bundle elsewhere.
powershell -NoProfile -ExecutionPolicy Bypass -File platforms/windows/bazel_build_windows_gpu.ps1 -BuildRoot E:\repo\emgu_litert\.tmp\wgpu-next -OutputDirectory E:\gpu-dlls

# Override Python or the SDK version when several installations are present.
powershell -NoProfile -ExecutionPolicy Bypass -File platforms/windows/bazel_build_windows_gpu.ps1 -PythonExecutable C:\Python312\python.exe -WindowsSdkVersion 10.0.28000.0
```

`BuildRoot` must be a subdirectory of this repository's `.tmp` directory. Keeping
its path short helps avoid Windows native-build path limits. `CMakeExecutable`
can also be set explicitly to a CMake executable path.

## Validation

On October 14, 2026, a clean script build completed on Visual Studio 2026 with
Windows SDK 10.0.28000.0. Its six-DLL bundle passed Gemma 4 E2B CPU and RTX 3070
Direct3D 12 GPU chat controls: both returned `Hello!`, exited 0, and the GPU log
contained no shader validation errors. The GPU sampler was excluded.

A subsequent `-Jobs 24` run reused the prepared sources and native build caches;
Bazel completed with one internal action and no native compilation. Logs and
the build manifest are retained under `.tmp/wgpu`. The source-submodule build
folder guard and PowerShell syntax/help were also checked.
