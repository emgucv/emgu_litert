# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Emgu TF Lite is a cross-platform .NET wrapper for the Google TensorFlow Lite library, enabling TF Lite functions to be called from C#, VB, VC++, and IronPython. It loads and runs TensorFlow Lite models on Windows, macOS, Linux, Android, and iOS.

The native C++ layer is exposed through P/Invoke via the `tfliteextern` extern library.

## Architecture

### Layer structure
1. **Native layer** — C/C++ wrapper DLL (`tfliteextern`) built with CMake or Bazel; must be present in `lib/runtimes/<rid>/native/` before building .NET code.
   - **Windows x64 (both CMake and Bazel)**, **macOS (Bazel)**, **iOS/Mac Catalyst (Bazel)**, **Android (Bazel)**, and **Ubuntu 24.04 (Bazel)** build against `litert/tflite` (the `google-ai-edge/LiteRT` submodule). There is no `tensorflow/` submodule in this repo.
     - CMake (Windows and macOS) builds directly from `litertextern/tfliteextern/`; `litert/tflite`'s own CMake project downloads the extra TF source it needs (e.g. MLIR/compiler files) itself via `FetchContent` at configure time. `litertextern/tfliteextern/CMakeLists.txt`'s `APPLE` branch picks its output subfolder (`runtimes/osx/native/arm64` or `.../x64`) via the same self-detected `IS_ARM64` (`check_symbol_exists("__aarch64__", ...)`) the `WIN32`/Linux branches already used — it used to check `EMGUCV_ARCH`, an Emgu.CV-only convention nothing in this repo ever sets, so it silently always fell into the x64 branch. `litert/tflite`'s own `profiling/proto`, `tools/benchmark/proto` (and transitively `model_runtime_info_proto`) CMake targets pass `protoc` a `--proto_path=${TENSORFLOW_SOURCE_DIR}` that doesn't encompass their actual `.proto` files (which live under `litert/tflite/`, not the separate FetchContent-downloaded TF tree `TENSORFLOW_SOURCE_DIR` points at) — breaking a plain `cmake --build .`, even though `tfliteextern` itself doesn't depend on any of them and builds fine alone. Since litert is a pristine submodule we don't patch upstream, `litertextern/tfliteextern/CMakeLists.txt` marks `profiling_info_proto`/`model_runtime_info_proto`/`benchmark_result_proto` `EXCLUDE_FROM_ALL` after the `ADD_SUBDIRECTORY` call instead, fixing `cmake --build .` generally; `platforms/macos/cmake_build_tflite` still targets `tfliteextern` specifically anyway, to skip litert's unrelated benchmark/example targets entirely. Only single-arch (host) builds are supported from CMake — setting `CMAKE_OSX_ARCHITECTURES` to build a universal arm64+x86_64 binary in one pass breaks that same `check_symbol_exists()` detection, since it only compiles for one architecture at a time; a fat binary would need separate per-arch builds `lipo`'d together, as the Bazel path already does.
     - Bazel needs the wrapper package to live *inside* the `litert` workspace (a pristine submodule we don't patch upstream), so `bazel_build_tflite_x86_64.bat` (Windows), `bazel_build_tflite_macos` (macOS), and `bazel_build_tflite_ios` (iOS/Mac Catalyst) each copy `litertextern/tfliteextern/bazel/{BUILD,tfliteextern.def}` plus the shared `litertextern/tfliteextern/*.cc/.h` and `litertextern/imgproc/*.cc/.h` sources into `litert/tfliteextern/` fresh on every run, then build from there. Bazel always fetches its own pinned TF archive (no local-submodule-reuse option — see the copied `BUILD`'s comments for why). The `BUILD` file's `.def`/`/DEF:` export-list trick is Windows-only (via `select()`); other platforms export every non-static symbol by default, verified by inspecting each build's symbol table.
       - `tfliteextern.cc` uses only the TensorFlow Lite **C API** (`tflite/c/c_api.h`, `c_api_experimental.h`, `common.h`, plus `tflite/schema/schema_generated.h` for model introspection). The `tfe*` entry points the C# side P/Invokes are unchanged, but their opaque handles are now tfliteextern's own small wrapper structs (`TfeModel`, `TfeInterpreter`, ...) around the C API objects. Gaps vs. the old C++-API wrapper: `TensorSize`/`NodeSize` come from the model's primary subgraph (excluding tensors/nodes delegates add); the C API fixes the thread count at creation, so `SetNumThreads` before `AllocateTensors` re-creates the interpreter (re-applying delegates) and after it is ignored with a stderr warning; `DynamicBuffer` (string tensors) is reimplemented on top of the exported `TfLiteTensorReset`. Errors reported through `tfeRedirectError`'s callback are thrown as exceptions by the C# handler, which .NET can't unwind through native frames on macOS/Linux — so new non-fatal diagnostics go to stderr instead. The NNAPI/GPU delegates stay C++ and Android-only.
       - `//tfliteextern:tfliteextern` (a `cc_binary`, `linkshared`) is the shared-library form used by Windows (and the static-link fallback elsewhere), statically linking the TFLite C API. **macOS** instead builds `//tfliteextern:tfliteextern_dylib`, a `cc_shared_library` with `dynamic_deps = [":litert_runtime"]`: `:litert_runtime` builds `libLiteRt.dylib` from LiteRT's `//litert/c:litert_tflite_runtime_c_api_so_shim` (LiteRT's runtime, exporting both the `LiteRt*` and `TfLite*` C APIs via the staged `litert_exported_symbols_darwin.lds`), with the install name set and `-framework Metal` linked — LiteRT's own `litert_tflite_runtime_c_api_so` sets neither, and its `.bazelrc` builds macOS with `-undefined dynamic_lookup`, so its `MTLCreateSystemDefaultDevice` reference would otherwise only resolve if something else loaded Metal. `exports_filter` lets `tfliteextern_dylib` take every TFLite library from `libLiteRt.dylib`, so `libtfliteextern.dylib` is only the wrapper (~80 KB per arch vs ~7–9 MB when statically linked) and depends on `@rpath/libLiteRt.dylib` with an `@loader_path` rpath. `bazel_build_tflite_macos <cpu>` builds both targets, copies them to `lib/runtimes/osx/native/{libtfliteextern,libLiteRt}-<cpu>.dylib` (dropping Bazel's extra `_solib`/runfiles rpaths, then re-signing ad hoc since `install_name_tool` invalidates the arm64 signature), and `lipo`s each into the universal `libtfliteextern.dylib`/`libLiteRt.dylib`; `bazel_build_tflite_macos_fat` drives `darwin_arm64` and `darwin`.
       - `libLiteRt.dylib` is also what the `Emgu.LiteRT` C# classes (`Emgu.LiteRT/`, built by `Emgu.LiteRT/NetStandard/Emgu.LiteRT.csproj` into its own `Emgu.LiteRT.dll`, configured from `cmake/Emgu.LiteRT.*.in`) P/Invoke directly via `DllImport("libLiteRt")` — one runtime serves both the `Emgu.TF.Lite` (Interpreter) and `Emgu.LiteRT` (CompiledModel) APIs. GPU/NPU accelerators are optional plugin dylibs (`libLiteRtMetalAccelerator.dylib` etc.) it tries to load at runtime; without them it logs a warning and uses the built-in XNNPACK CPU accelerator. XNNPACK is part of `libLiteRt`, so on macOS the Interpreter API also uses it by default (the static builds still compile tfliteextern with `-DWITHOUT_XNNPACK`). When `lib/runtimes/osx/native/libLiteRt.dylib` exists, the top-level `CMakeLists.txt` adds it to the `Emgu.LiteRT.runtime.macos` nuspec next to `libtfliteextern.dylib`, and `Emgu.TF.Runtime/Mac`'s `.projitems` copies it to the build output; the CPack zip already picks it up via its `*.dylib` install pattern.
       - `//tfliteextern:tfliteextern_ios_framework` (a `cc_library` wrapped in `ios_static_framework`, no `hdrs` — the umbrella header rules_apple would generate collides with our own `tfliteextern.h`) is the static-library form for iOS device/simulator and Mac Catalyst, where `DllImport("tfliteextern")` resolves to `__Internal` and the tfe* symbols just need to be present in the final app binary. `bazel_build_tflite_ios <config>` (configs: `ios_arm64`, `ios_sim_arm64`, `ios_x86_64`, `ios_armv7`, `darwin_x86_64`/`darwin_arm64` for Catalyst) unzips the resulting `tfliteextern.framework`, extracts the raw archive, and names it `lib/ios/libtfliteextern_<config>.a`; `bazel_build_xcframework` (`ios_x86_64` + `ios_arm64` + `ios_sim_arm64`) and `bazel_build_mac_catalyst` (`darwin_x86_64` + `darwin_arm64`) drive multiple configs and `lipo`/`xcodebuild -create-xcframework` them together, matching what `Emgu.TF.Runtime/Maui/iOS`'s `.csproj` expects (`lib/ios/libtfliteextern_ios.xcframework` or `libtfliteextern.a`).
       - `platforms/android/bazel_build_tflite_android <arch>` (`x86`, `x86_64`, `arm`, `arm64`) stages the same `litertextern/` sources and builds the shared-library `//tfliteextern:tfliteextern` target (like Windows/macOS) with litert's `--config=android_<arch>` configs, copying the result to `lib/android/<abi>/libtfliteextern.so` (matching what `Emgu.TF.Runtime/Maui/Android`'s `.csproj` expects). Two things needed fixing beyond staging the sources:
         - `tfliteextern.cc`'s GPU delegate functions (`tfeGpuDelegateV2Create`/`Delete`) are compiled only under `#ifdef __ANDROID__`, so `litertextern/tfliteextern/bazel/BUILD`'s `_TFLITEEXTERN_DEPS` adds `//tflite/delegates/gpu:delegate` + `:delegate_options` behind a `select({"@org_tensorflow//tensorflow:android": [...], "//conditions:default": []})` — those targets need EGL and don't build on the other platforms.
         - Of litert's four `.bazelrc` `android_*` configs, only `android_arm64` sets `--incompatible_enable_cc_toolchain_resolution`/`--incompatible_enable_android_toolchain_resolution`; the other three fall back to a legacy `--crosstool_top=//external:android/crosstool` alias that isn't actually defined anywhere in this WORKSPACE (it silently resolves to the host `local_config_cc` toolchain instead, which doesn't have a toolchain for a non-host cpu, or produces `/bin/false`-stubbed compile actions). The build script passes both flags explicitly for every arch to force the same NDK-toolchain-via-platforms resolution arm64 already gets from its config.
       - `platforms/ubuntu/24.04/bazel_build_tflite` follows the same staging pattern, builds `//tfliteextern:tfliteextern` with `--config=linux` (`--config=linux_arm64` too on an aarch64 host), and copies the result to `lib/runtimes/ubuntu-x64/native/libtfliteextern.so` (or `ubuntu-arm64`), matching what `Emgu.TF.Runtime/Ubuntu`'s `.projitems` expects. No GPU-delegate select() or toolchain-resolution override needed here — Linux isn't `__ANDROID__` and already resolves its host toolchain correctly.
   - **Broken / not yet migrated**: the Debian (`platforms/debian/bookworm`, `platforms/debian/trixie`) ARM build scripts still `cd` into `tensorflow/tensorflow/tfliteextern` — a path that no longer exists in this repo. Fixing them means porting each to `litertextern/` the way Windows, macOS, iOS, Android, and Ubuntu's Bazel paths were.
2. **P/Invoke layer** — `TfLiteInvoke` (in `Emgu.TF.Lite/`) is a partial static class that exposes the native DLL entry points via `[DllImport]`.
3. **Managed wrappers** — `Interpreter`, `Tensor`, etc. inherit `UnmanagedObject` (from `Emgu.LiteRT.Util/`, namespace `Emgu.LiteRT.Util`, built into its own `Emgu.LiteRT.Util.dll`) and wrap native handles with proper lifetime management.
   - `Emgu.LiteRT/` is a separate managed wrapper (own `Emgu.LiteRT.dll`, namespace `Emgu.LiteRT`) over LiteRT's own `libLiteRt` C API — `Environment`, `Model`, `CompiledModel`, `TensorBuffer`, etc. — with no Emgu native layer.
   - **Packaging**: `Emgu.LiteRT` is the single managed NuGet package. `Emgu.LiteRT/NetStandard/Emgu.LiteRT.csproj` references `Emgu.TF.Lite.csproj` and `Emgu.LiteRT.Util.csproj` with `PrivateAssets="all"` and packs their dlls/xml docs into its own `lib/<tfm>` via an `IncludeReferencedProjectsInPackage` target; `Emgu.TF.Lite` and `Emgu.LiteRT.Util` are `IsPackable=False` (set in their CMake-generated `Directory.Build.props`). Every other package ID derives from `EMGUTF_LITE_NUGET_ID` (`Emgu.LiteRT`) in the top-level `CMakeLists.txt` — `Emgu.LiteRT.Models`, `Emgu.LiteRT.runtime.{macos,windows,ubuntu,debian-*,maui.*}` — even though their folders/projects keep the `Emgu.TF.Lite.*` names. Projects that produce packages and use `Emgu.TF.Lite`/`Emgu.LiteRT.Util` types (`Emgu.TF.Lite.Models`, the MAUI runtime projects) reference `Emgu.LiteRT.csproj` normally (→ package dependency) and those two projects with `PrivateAssets="all"` (compile only). `Emgu.TF.Lite/Toolbox.cs`'s tfliteextern image helpers (`Pixel32ToPixelFloat` etc.) are in `Emgu.TF.Lite.Toolbox`, not `Emgu.LiteRT.Util.Toolbox` — a partial class can't span the two assemblies.
4. **Models layer** — `Emgu.LiteRT.Models/` provides high-level pre-built model helpers (MobileNet, COCO SSD, etc.) that download weights and run inference.
5. **Platform runtime packages** — `Emgu.TF.Runtime/` contains shared project items (`.shproj`/`.projitems`) that pull in the correct native binaries for each platform (Windows, macOS, Debian, Ubuntu, Maui).
6. **Unity integration** — `Emgu.TF.Lite.Unity/` is a Unity project that mirrors the managed API for use in game engines.

### Shared projects
`Emgu.TF.Lite.Shared.shproj`, `Emgu.LiteRT.Util.Shared.shproj`, etc. share source files across multiple target frameworks (NetStandard, Android, iOS, Unity). The `.projitems` files define which `.cs` files are included.

## Building

### Prerequisites
- .NET SDK
- Visual Studio 2022 or VS 2026 (detected automatically by `vswhere.exe`) — Windows
- CMake 3.16+
- Bazel (for the Bazel native builds; the version is pinned by `litert/.bazelversion`). The macOS and Android Bazel scripts set `HERMETIC_PYTHON_VERSION=3.12` when `python3.12` is on the `PATH`.
  - The macOS, iOS and Android Bazel scripts run `bazel --output_user_root=<repo>/bazel_output build ...`, so Bazel's output tree, install base and cache (several GB per platform) live in `bazel_output/` at the repo root — gitignored, outside the `litert/` workspace — instead of the default `/private/var/tmp/_bazel_$USER` (macOS) or `~/.cache/bazel` (Linux). Running `bazel` by hand in `litert/` needs the same startup flag (e.g. `bazel --output_user_root="$(cd .. && pwd)/bazel_output" build ...` — use an absolute path), otherwise Bazel starts a separate server with a cold cache in the default location. Deleting `bazel_output/` forces a full rebuild.
- Xcode — macOS/iOS. The macOS script handles an Xcode installed outside `/Applications` (e.g. on another volume) by passing `DEVELOPER_DIR` into Bazel's sandbox.
- Android SDK + NDK, and a JDK — Android (see below)
- TF Lite native library already built and placed under `lib/runtimes/` (or `lib/android/`, `lib/ios/`)

### Build native TF Lite (Windows x64)
```bat
cd platforms/windows
cmake_build_tflite_x86_64.bat
```
Or via Bazel (also produces `lib/runtimes/win-x64/native/tfliteextern.dll`):
```bat
cd platforms/windows
bazel_build_tflite_x86_64.bat 64 xnn
```

### Build the .NET solution (Windows)
```bat
cd platforms/windows
build_emgutf.bat
```
Optional args: `doc` (build docs), `nuget` (build NuGet packages), `package` (build zip packages).

### Build native TF Lite (macOS)
```bash
platforms/macos/bazel_build_tflite_macos_fat
```
Builds `arm64` and `x86_64` and writes the universal `lib/runtimes/osx/native/libtfliteextern.dylib` and `libLiteRt.dylib` (plus the per-arch `*-darwin_arm64.dylib`/`*-darwin.dylib` they are `lipo`'d from). To build one architecture only (the universal files then contain just that one):
```bash
platforms/macos/bazel_build_tflite_macos darwin_arm64   # or: darwin (x86_64, cross-compiled on Apple Silicon)
```
Alternative CMake build — host architecture only, statically linked, no `libLiteRt.dylib`; output goes to `lib/runtimes/osx/native/<arm64|x64>/libtfliteextern.dylib`:
```bash
platforms/macos/cmake_build_tflite
```

### Build the .NET solution and packages (macOS)
```bash
platforms/macos/build_emgutf
```
Configures CMake in `build/` (Release) and runs `make package`: builds the managed projects, writes the NuGet packages (`Emgu.LiteRT`, `Emgu.LiteRT.Models`, `Emgu.LiteRT.runtime.*`) to `platforms/nuget/`, and the CPack zip to `build/libemgutflite-ios-macos-<version>.zip`. The version's last component is derived from the git commit count, so it changes with every commit. Run the native build first: packages only include the native binaries already in `lib/`.

### Build native TF Lite (Android, from Linux or macOS)
The Android binaries can be built on either a Linux or a macOS host, with the same script:
```bash
platforms/android/bazel_build_tflite_android arm64   # or: x86_64, arm, x86
```
Writes `lib/android/<abi>/libtfliteextern.so` (`arm64-v8a`, `x86_64`, `armeabi-v7a`, `x86`), 16 KB page-aligned, with the Android-only NNAPI and GPU delegates linked in. Run once per ABI; extra arguments are passed to `bazel build`. The script reads the SDK from `ANDROID_HOME` and the NDK from `ANDROID_NDK_HOME`, else the newest version under `$ANDROID_HOME/ndk`:
- **Linux**: `ANDROID_HOME` defaults to `/usr/lib/android-sdk` (the build image layout) when unset.
- **macOS**: set `ANDROID_HOME` to the Android Studio SDK, usually `~/Library/Android/sdk` (no default — the Linux path doesn't exist there). Verified with NDK 28.0.12916984 for all four ABIs.

### Build with CMake (cross-platform)
```bash
mkdir build && cd build
cmake <PATH_TO_EMGUTF_ROOT>
cmake --build . --config Release
```

### Visual Studio solutions
Platform-specific `.sln` files live under `Solution/`:
- `Solution/Windows.Desktop/` — `Emgu.TF.Lite.sln`, plus test/example solutions
- `Solution/Android/`, `Solution/iOS/`, `Solution/CrossPlatform/`, `Solution/macos/`

## Running Tests

Tests use either NUnit or MSTest (selected at compile-time via the `VS_TEST` preprocessor symbol).

### TF Lite — run all tests
```bash
dotnet test Emgu.TF.Test/Emgu.TF.Lite.Test/Emgu.TF.Lite.Test.Net/Emgu.TF.Lite.Test.Net.csproj
```

### Run a single test
```bash
dotnet test <project.csproj> --filter "FullyQualifiedName~TestGetVersion"
```

Test assets (e.g., `grace_hopper.jpg`) must be present in the working directory when tests run.

`Emgu.TF.Lite.Test.Net` targets `net9.0`; on a machine with only a newer .NET runtime installed (e.g. .NET 10), run it with `DOTNET_ROLL_FORWARD=Major dotnet test ...`. The project also needs the CMake-generated `Directory.Build.props` files (run the platform's `build_emgutf` once), and on macOS it copies `libtfliteextern.dylib`/`libLiteRt.dylib` from `lib/runtimes/osx/native/` via `Emgu.TF.Runtime/Mac`'s `.projitems`.

## Key Conventions

- **Extern library name**: `tfliteextern`. On iOS/Unity this resolves to `__Internal`.
- **`UnmanagedObject`** (in `Emgu.LiteRT.Util/`): base class for all objects wrapping a native pointer; handles `Dispose`/finalizer pattern.
- **Calling convention**: `CallingConvention.Cdecl` throughout.
- **Boolean marshaling**: `UnmanagedType.U1` for `bool`, `UnmanagedType.Bool` for `int`-as-bool.
- **Error handling**: native errors are redirected via a callback delegate (`TfLiteErrorCallback`) and thrown as managed exceptions.
- There is no `tensorflow/` submodule — it was removed once the Windows CMake path was proven to work without it (LiteRT downloads the TF source it needs itself). The overall package version (`CPACK_PACKAGE_VERSION_*` in the top-level `CMakeLists.txt`, and `litertextern/tfliteextern/CMakeLists.txt`'s own version) is read from `litert/version.bzl` via `cmake/modules/LitertVersion.cmake`, not from a TF version file.
- The `litert/` directory is a submodule of `google-ai-edge/LiteRT`, pinned to the `v2.2.0` tag (not `main` — the bleeding-edge commit this was originally added at hit a WORKSPACE bug under Bazel; see `litertextern/tfliteextern/bazel/BUILD`'s comments). Do not modify files inside it directly.
- `litertextern/` holds the Emgu-authored native wrapper (`tfliteextern`, `imgproc`) that both Windows build paths use — this is the one place native wrapper source changes should be made for that platform going forward. `litertextern/tfliteextern/bazel/{BUILD,tfliteextern.def}` is the Bazel-specific counterpart to `litertextern/tfliteextern/CMakeLists.txt`, staged into `litert/tfliteextern/` at build time (see above) since it can't live there in git.
