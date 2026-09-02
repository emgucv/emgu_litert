# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Emgu TF Lite is a cross-platform .NET wrapper for the Google TensorFlow Lite library, enabling TF Lite functions to be called from C#, VB, VC++, and IronPython. It loads and runs TensorFlow Lite models on Windows, macOS, Linux, Android, and iOS.

The native C++ layer is exposed through P/Invoke via the `tfliteextern` extern library.

## Architecture

### Layer structure
1. **Native layer** — C/C++ wrapper DLL (`tfliteextern`) built with CMake or Bazel; must be present in `lib/runtimes/<rid>/native/` before building .NET code.
   - **Windows x64 (both CMake and Bazel)** build against `litert/tflite` (the `google-ai-edge/LiteRT` submodule). There is no `tensorflow/` submodule in this repo.
     - CMake builds directly from `litertextern/tfliteextern/`; `litert/tflite`'s own CMake project downloads the extra TF source it needs (e.g. MLIR/compiler files) itself via `FetchContent` at configure time.
     - Bazel needs the wrapper package to live *inside* the `litert` workspace (a pristine submodule we don't patch upstream), so `bazel_build_tflite_x86_64.bat` copies `litertextern/tfliteextern/bazel/{BUILD,tfliteextern.def}` plus the shared `litertextern/tfliteextern/*.cc/.h` and `litertextern/imgproc/*.cc/.h` sources into `litert/tfliteextern/` fresh on every run, then builds `//tfliteextern:tfliteextern` from there. Bazel always fetches its own pinned TF archive (no local-submodule-reuse option — see the copied `BUILD`'s comments for why).
   - **Broken / not yet migrated**: macOS, Linux, Android, and iOS all still `cd` into `tensorflow/tensorflow/tfliteextern` to build — a path that no longer exists in this repo. Fixing these means porting each to `litertextern/` the way both Windows paths were.
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
