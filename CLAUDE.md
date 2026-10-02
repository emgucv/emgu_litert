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
       - `//tfliteextern:tfliteextern` (a `cc_binary`, `linkshared`) is the shared-library form used by Windows/macOS. `bazel_build_tflite_macos` outputs `lib/runtimes/osx/native/libtfliteextern-<cpu>.dylib` (fixing the install name to `@rpath/libtfliteextern.dylib` via `install_name_tool`) then `lipo`s the per-arch dylibs into the universal `libtfliteextern.dylib` that `Emgu.TF.Runtime/Mac` expects; `bazel_build_tflite_macos_fat` drives both `darwin` and `darwin_arm64` for that, then also runs `bazel_build_litert_macos`.
       - `platforms/macos/bazel_build_litert_macos` builds LiteRT's own unmodified runtime library, `//litert/c:litert_runtime_c_api_dylib`, into `lib/runtimes/osx/native/libLiteRt.dylib` — no Emgu code is staged into or linked into it. It's a rules_apple `macos_dylib`, so `--macos_cpus=arm64,x86_64` produces the universal binary in one invocation (no per-arch build + `lipo`), and its install name is already `@rpath/libLiteRt.dylib`. It exports only the `LiteRt*` C API (plus `kLiteRtRuntimeBuiltin`) — no `TfLite*`/`tflite::` symbols — and is independent of `libtfliteextern.dylib`; the `Emgu.TF.Lite.LiteRt` C# classes (`Emgu.TF.Lite/LiteRt/`) P/Invoke it directly via `DllImport("libLiteRt")`. GPU/NPU accelerators are optional plugin dylibs (`libLiteRtMetalAccelerator.dylib` etc.) it tries to load at runtime; without them it logs a warning and uses the built-in XNNPACK CPU accelerator. When `lib/runtimes/osx/native/libLiteRt.dylib` exists, the top-level `CMakeLists.txt` adds it to the `Emgu.TF.Lite.runtime.macos` nuspec next to `libtfliteextern.dylib`, and `Emgu.TF.Runtime/Mac`'s `.projitems` copies it to the build output; the CPack zip already picks it up via its `*.dylib` install pattern.
       - `//tfliteextern:tfliteextern_ios_framework` (a `cc_library` wrapped in `ios_static_framework`, no `hdrs` — the umbrella header rules_apple would generate collides with our own `tfliteextern.h`) is the static-library form for iOS device/simulator and Mac Catalyst, where `DllImport("tfliteextern")` resolves to `__Internal` and the tfe* symbols just need to be present in the final app binary. `bazel_build_tflite_ios <config>` (configs: `ios_arm64`, `ios_sim_arm64`, `ios_x86_64`, `ios_armv7`, `darwin_x86_64`/`darwin_arm64` for Catalyst) unzips the resulting `tfliteextern.framework`, extracts the raw archive, and names it `lib/ios/libtfliteextern_<config>.a`; `bazel_build_xcframework` (`ios_x86_64` + `ios_arm64` + `ios_sim_arm64`) and `bazel_build_mac_catalyst` (`darwin_x86_64` + `darwin_arm64`) drive multiple configs and `lipo`/`xcodebuild -create-xcframework` them together, matching what `Emgu.TF.Runtime/Maui/iOS`'s `.csproj` expects (`lib/ios/libtfliteextern_ios.xcframework` or `libtfliteextern.a`).
       - `platforms/android/bazel_build_tflite_android <arch>` (`x86`, `x86_64`, `arm`, `arm64`) stages the same `litertextern/` sources and builds the shared-library `//tfliteextern:tfliteextern` target (like Windows/macOS) with litert's `--config=android_<arch>` configs, copying the result to `lib/android/<abi>/libtfliteextern.so` (matching what `Emgu.TF.Runtime/Maui/Android`'s `.csproj` expects). Two things needed fixing beyond staging the sources:
         - `tfliteextern.cc`'s GPU delegate functions (`tfeGpuDelegateV2Create`/`Delete`) are compiled only under `#ifdef __ANDROID__`, so `litertextern/tfliteextern/bazel/BUILD`'s `_TFLITEEXTERN_DEPS` adds `//tflite/delegates/gpu:delegate` + `:delegate_options` behind a `select({"@org_tensorflow//tensorflow:android": [...], "//conditions:default": []})` — those targets need EGL and don't build on the other platforms.
         - Of litert's four `.bazelrc` `android_*` configs, only `android_arm64` sets `--incompatible_enable_cc_toolchain_resolution`/`--incompatible_enable_android_toolchain_resolution`; the other three fall back to a legacy `--crosstool_top=//external:android/crosstool` alias that isn't actually defined anywhere in this WORKSPACE (it silently resolves to the host `local_config_cc` toolchain instead, which doesn't have a toolchain for a non-host cpu, or produces `/bin/false`-stubbed compile actions). The build script passes both flags explicitly for every arch to force the same NDK-toolchain-via-platforms resolution arm64 already gets from its config.
       - `platforms/ubuntu/24.04/bazel_build_tflite` follows the same staging pattern, builds `//tfliteextern:tfliteextern` with `--config=linux` (`--config=linux_arm64` too on an aarch64 host), and copies the result to `lib/runtimes/ubuntu-x64/native/libtfliteextern.so` (or `ubuntu-arm64`), matching what `Emgu.TF.Runtime/Ubuntu`'s `.projitems` expects. No GPU-delegate select() or toolchain-resolution override needed here — Linux isn't `__ANDROID__` and already resolves its host toolchain correctly.
   - **Broken / not yet migrated**: the Debian (`platforms/debian/bookworm`, `platforms/debian/trixie`) ARM build scripts still `cd` into `tensorflow/tensorflow/tfliteextern` — a path that no longer exists in this repo. Fixing them means porting each to `litertextern/` the way Windows, macOS, iOS, Android, and Ubuntu's Bazel paths were.
2. **P/Invoke layer** — `TfLiteInvoke` (in `Emgu.TF.Lite/`) is a partial static class that exposes the native DLL entry points via `[DllImport]`.
3. **Managed wrappers** — `Interpreter`, `Tensor`, etc. inherit `UnmanagedObject` (from `Emgu.TF.Util/`) and wrap native handles with proper lifetime management.
4. **Models layer** — `Emgu.TF.Lite.Models/` provides high-level pre-built model helpers (MobileNet, COCO SSD, etc.) that download weights and run inference.
5. **Platform runtime packages** — `Emgu.TF.Runtime/` contains shared project items (`.shproj`/`.projitems`) that pull in the correct native binaries for each platform (Windows, macOS, Debian, Ubuntu, Maui).
6. **Unity integration** — `Emgu.TF.Lite.Unity/` is a Unity project that mirrors the managed API for use in game engines.

### Shared projects
`Emgu.TF.Lite.Shared.shproj`, `Emgu.TF.Util.Shared.shproj`, etc. share source files across multiple target frameworks (NetStandard, Android, iOS, Unity). The `.projitems` files define which `.cs` files are included.

## Building

### Prerequisites
- .NET SDK
- Visual Studio 2022 or VS 2026 (detected automatically by `vswhere.exe`)
- CMake 3.16+
- TF Lite native DLL already built and placed under `lib/runtimes/`

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

## Key Conventions

- **Extern library name**: `tfliteextern`. On iOS/Unity this resolves to `__Internal`.
- **`UnmanagedObject`** (in `Emgu.TF.Util/`): base class for all objects wrapping a native pointer; handles `Dispose`/finalizer pattern.
- **Calling convention**: `CallingConvention.Cdecl` throughout.
- **Boolean marshaling**: `UnmanagedType.U1` for `bool`, `UnmanagedType.Bool` for `int`-as-bool.
- **Error handling**: native errors are redirected via a callback delegate (`TfLiteErrorCallback`) and thrown as managed exceptions.
- There is no `tensorflow/` submodule — it was removed once the Windows CMake path was proven to work without it (LiteRT downloads the TF source it needs itself). The overall package version (`CPACK_PACKAGE_VERSION_*` in the top-level `CMakeLists.txt`, and `litertextern/tfliteextern/CMakeLists.txt`'s own version) is read from `litert/version.bzl` via `cmake/modules/LitertVersion.cmake`, not from a TF version file.
- The `litert/` directory is a submodule of `google-ai-edge/LiteRT`, pinned to the `v2.2.0` tag (not `main` — the bleeding-edge commit this was originally added at hit a WORKSPACE bug under Bazel; see `litertextern/tfliteextern/bazel/BUILD`'s comments). Do not modify files inside it directly.
- `litertextern/` holds the Emgu-authored native wrapper (`tfliteextern`, `imgproc`) that both Windows build paths use — this is the one place native wrapper source changes should be made for that platform going forward. `litertextern/tfliteextern/bazel/{BUILD,tfliteextern.def}` is the Bazel-specific counterpart to `litertextern/tfliteextern/CMakeLists.txt`, staged into `litert/tfliteextern/` at build time (see above) since it can't live there in git.
