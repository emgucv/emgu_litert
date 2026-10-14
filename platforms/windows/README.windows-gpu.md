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
| `licenses/*.txt` | LiteRT, ML Drift, Dawn, and DXC source license notices |

The script checks that every DLL is an x64 PE library before copying the bundle.
It exits on a failed download, patch, configuration, compile, or artifact check.
Step logs are under `.tmp/wgpu/logs`; a failure names the relevant log.

Deploy the runtime, accelerator, Dawn, and shader compiler DLLs together beside
the executable. The optional prebuilt GPU sampler is excluded: LiteRT-LM uses GPU
inference and CPU token sampling. An existing compatible `liblitert-lm.dll` and
`libGemmaModelConstraintProvider.dll` from the normal Windows native build are
still needed for chat. This script does not build managed code.

The script's default output is isolated; it does not replace
`lib/runtimes/win-x64/native`. Treat the bundle as the source-build candidate
documented in [INVESTIGATION.md](INVESTIGATION.md). Other model families still
need validation before enabling their Windows GPU backend.

## NuGet package

The validated six-DLL bundle is packaged as **Emgu.LiteRT.GPU.runtime.windows**,
along with its build manifest and targets that select the matching LiteRT core
when CPU runtime packages are also referenced. The package supports Windows x64
desktop and MAUI applications, depends on the managed `Emgu.LiteRT` package and
x64 MSVC runtime, and excludes the optional GPU sampler.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File platforms/windows/build_emgu_litert.ps1 -NugetFlag nuget
```

This produces `platforms/nuget/Emgu.LiteRT.GPU.runtime.windows.<version>.nupkg`
when `.tmp/wgpu/artifacts/gpu-build.json` exists. Use `-GpuBundleDirectory <path>`
for a custom source bundle. CMake validates source revisions and DLL checksums
at configure and before each package build. Without a GPU bundle, CPU packages
continue to build and the GPU package target is omitted.

The equivalent CMake option is `-DEMGU_WINDOWS_GPU_BUNDLE_DIR=<path>`; the target
is `Emgu.LiteRT.GPU.runtime.windows.nuget`.

## Windows MAUI chat

After generating project properties and native NuGet packages with
`build_emgu_litert.ps1 -NugetFlag nuget`, build the demo:

```powershell
dotnet build Emgu.LiteRT.Example/Maui.Lite/Maui.Demo.Lite.csproj -f net10.0-windows10.0.22621.0 -p:WindowsPackageType=None -p:RestoreAdditionalProjectSources="$PWD/platforms/nuget"
```

The demo automatically references the GPU NuGet package when the default
`.tmp/wgpu/artifacts/gpu-build.json` exists. `WindowsGpuRuntimeDirectory` can
select another bundle directory for that detection. Build the package first;
the generated `WindowsGpuRuntimePackageVersion` selects the current repository
version. The package replaces the normal LiteRT core with the matching GPU core,
and keeps GPU output in a separate `webgpu` subfolder to avoid stale DLLs when
alternating CPU and GPU builds. Use `-p:EnableWindowsGpu=false` for a CPU-only
build, or `-p:EnableWindowsGpu=true` to require the GPU package even when the
source bundle is not on this machine. The CPU-only build displays the GPU setting as unavailable. The Debug
GPU executable is `Emgu.LiteRT.Example/Maui.Lite/bin/Debug/webgpu/net10.0-windows10.0.22621.0/win-x64/Maui.Demo.Lite.exe`.

In Chat → Settings, enable **Use the GPU**, then select **Gemma 4 E2B**.
Windows defaults to CPU until this setting is enabled. Switching backend reloads
the model and retains the conversation. Qwen and Gemma 4 E4B currently use CPU on
Windows. GPU initialization failure falls back to CPU with a status message;
turn the switch off and on to retry. A Direct3D 12 GPU with enough memory is
required. The vision/audio encoders and token sampling continue to use CPU.

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

The Windows MAUI demo also passed Gemma 4 E2B GPU inference at its default 8k
context window, followed by switching to CPU with conversation history retained
and another successful reply. Removing the GPU plugin from the isolated test
output exercised CPU fallback successfully. GPU and CPU-only builds both passed;
an incompatible source revision in the bundle manifest was rejected. Temporary
app test code was removed from the final build.

The GPU NuGet package was restored together with the ordinary Windows and
LiteRT-LM runtime packages. Build and publish deployed the six matching DLLs;
the package and deployed DLL hashes matched the source manifest. The standalone
apphost and published app returned `Hello!` on GPU with no shader validation
errors, and a published CPU control also passed. The MAUI demo now restores this
package and its build passed with the same verified DLLs.
