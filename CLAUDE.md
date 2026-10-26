# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository. It is the short version: the details - how each native build works, packaging, LiteRT-LM facts, the models, the MAUI demo's features - are in the documents listed under [Where the details are](#where-the-details-are). Put new details there, not here, and keep this file short.

## Project Overview

Emgu TF Lite is a cross-platform .NET wrapper for the Google TensorFlow Lite library, enabling TF Lite functions to be called from C#, VB, VC++, and IronPython. It loads and runs TensorFlow Lite models on Windows, macOS, Linux, Android, and iOS.

The native C++ layer is exposed through P/Invoke via the `tfliteextern` extern library.

## Repository map

- `litertextern/` - the Emgu-authored native wrapper (`tfliteextern`, `imgproc`), built with CMake (`litertextern/tfliteextern/CMakeLists.txt`) or Bazel (`litertextern/tfliteextern/bazel/BUILD`, staged into the LiteRT-LM workspace by each build script). Builds land in `lib/runtimes/<rid>/native/`, `lib/android/<abi>/` and `lib/ios/`. See `docs/native-builds.md`.
- `litert/` and `LiteRT-LM/` - pristine submodules of google-ai-edge/LiteRT and LiteRT-LM; never edit them (see the rules below).
- `Emgu.TF.Lite/` - the TFLite Interpreter API over `tfliteextern` (`TfLiteInvoke`, `Interpreter`, `Tensor`...); `Emgu.LiteRT.Util/` - `UnmanagedObject`, downloads and shared helpers; `Emgu.LiteRT/` - LiteRT's `CompiledModel` API over `libLiteRt` directly.
- `Emgu.TF.Lite.Models/`, `Emgu.TF.Lite.ImageIO/` - ready-made models on the Interpreter API and image input; `Emgu.LiteRT.Models/` - models on `CompiledModel` (MobileNet, COCO SSD, YOLOX, PicoDet, D-FINE, MediaPipe hands/pose/face/holistic, YAMNet, Z-Image-Turbo); `Emgu.LiteRT.Models.CV/` - the vision models that take Emgu CV `Mat`s; `Emgu.LiteRT.Models.CV.Maui/` - the live camera for them in a MAUI app (`LiveCamera`, `LiveCameraPreview`). See `Emgu.LiteRT.Models/MODELS.md`.
- `Emgu.LiteRT.LM/` - LiteRT-LM's C API (engine, sessions, conversations, embeddings); `Emgu.LiteRT.LM.Models/` - ready-to-use language and embedding models (Qwen3, Qwen3.5, Gemma 4, EmbeddingGemma 2); `Emgu.LiteRT.LM.Extensions.AI/` - `IChatClient` over them. See `docs/litert-lm.md`.
- `Emgu.LiteRT.LM.Agents/` - the chat agent's reusable tools (calculator, units, weather, goals, device actions, meeting notes...); `Emgu.LiteRT.LM.Agents.CV/` - the camera monitors' visual agents on the `Emgu.LiteRT.Models.CV` detectors; `Emgu.LiteRT.LM.Agents.Mcp/` - MCP tool servers as agent tools; `Emgu.LiteRT.LM.Agents.Documents/` - reading PDF/Word/text files and searching saved passages; `Emgu.LiteRT.LM.Chat/`, `.Chat.Sqlite/`, `.Maui/` - the reusable chat (conversation, saved chats, `ChatView`, and `ChatPage`: the whole page with its `ChatFeature` skills, which other apps drop in). See `docs/agents.md`.
- `Emgu.LiteRT.Runtime/` - the native runtime packages per platform, including the optional add-ons (LiteRT-LM, OpenCL, Metal). See `docs/packaging.md`.
- `Emgu.LiteRT.Example/Maui/` - the MAUI demo (chat and TF Lite pages); `Emgu.LiteRT.Example/Maui.Demo.Lite.Core/` - its code without UI, which the tests use; `Emgu.LiteRT.Example/MauiChat/` - a minimal chat sample. See `Emgu.LiteRT.Example/Maui/README.md`, `ARCHITECTURE.md` and `FEATURES.md`.
- `Emgu.LiteRT.Test/` - the tests; `Emgu.TF.Lite.Unity/` - Unity; `platforms/` - the build scripts and generated NuGet folders; `cmake/` - templates CMake configures (`Directory.Build.props`, nuspecs, package READMEs); `Solution/` - Visual Studio solutions.

### Layers
1. **Native layer** — C/C++ wrapper DLL (`tfliteextern`) built with CMake or Bazel; must be present in `lib/runtimes/<rid>/native/` before building .NET code.
2. **P/Invoke layer** — `TfLiteInvoke` (in `Emgu.TF.Lite/`, namespace `Emgu.TF.Lite`) is a partial static class that exposes the native DLL entry points via `[DllImport]`.
3. **Managed wrappers** — `Interpreter`, `Tensor`, etc. inherit `UnmanagedObject` (from `Emgu.LiteRT.Util/`, namespace `Emgu.LiteRT.Util`, built into its own `Emgu.LiteRT.Util.dll`) and wrap native handles with proper lifetime management.
   - `Emgu.LiteRT/` is a separate managed wrapper (own `Emgu.LiteRT.dll`, namespace `Emgu.LiteRT`) over LiteRT's own `libLiteRt` C API — `Environment`, `Model`, `CompiledModel`, `TensorBuffer`, etc. — with no Emgu native layer.
4. **Models layer** — ready-made models: `Emgu.LiteRT.Models` and `Emgu.LiteRT.Models.CV` on LiteRT's CompiledModel API, `Emgu.TF.Lite.Models` on the Interpreter (see `Emgu.LiteRT.Models/MODELS.md`).
5. **Platform runtime packages** — `Emgu.LiteRT.Runtime/` contains shared project items (`.shproj`/`.projitems`) that pull in the correct native binaries for each platform (Windows, macOS, Debian, Ubuntu, Maui).
6. **Unity integration** — `Emgu.TF.Lite.Unity/` is a Unity project that mirrors the managed API for use in game engines.

### Shared projects
`Emgu.TF.Lite.Shared.shproj`, `Emgu.LiteRT.Util.Shared.shproj`, etc. share source files across multiple target frameworks (NetStandard, Android, iOS, Unity). The `.projitems` files define which `.cs` files are included.

## Building

Native libraries first, then the .NET projects: packages only include the native binaries already in `lib/`. Every command, prerequisite and platform detail is in `docs/native-builds.md`.

| Platform | Native libraries | .NET projects and packages |
|---|---|---|
| Windows x64 | `platforms/windows/cmake_build_litert.ps1 64 xnn` or `bazel_build_litert.ps1 64` | `platforms/windows/build_emgu_litert.ps1` (`nuget`, `package`, `doc`) |
| macOS | `platforms/macos/bazel_build_tflite_macos_fat` (universal; LiteRT-LM arm64 only) | `platforms/macos/build_emgutf` |
| iOS | `platforms/ios/bazel_build_tflite_ios_frameworks`, then `bazel_build_litert_lm_ios` | (the MAUI projects) |
| Mac Catalyst | `platforms/ios/cmake_build_tflite_catalyst arm64` (plus the macOS build for LiteRT-LM) | (the MAUI projects) |
| Android | `platforms/android/bazel_build_tflite_android <arm64\|x86_64\|arm\|x86>` with `ANDROID_HOME` set | (the MAUI projects) |
| Ubuntu / Debian ARM | `platforms/ubuntu/24.04/bazel_build_tflite`, `platforms/debian/{bookworm,trixie}/bazel_build_tflite` | CMake |

The MAUI demo: `dotnet build Emgu.LiteRT.Example/Maui/Maui.Demo.Lite.csproj -f net10.0-<android|ios|maccatalyst|windows10.0.22621.0>`; `Emgu.LiteRT.Example/Maui/tools/deploy_android <serial>` installs and starts it on Android devices (with the models the Mac has), `tools/run_catalyst` runs the Catalyst build ad hoc. Step by step in `Emgu.LiteRT.Example/Maui/README.md`.

Pitfalls:
- Re-run CMake (`build_emgutf`, `build_emgu_litert.ps1`, or `cmake .` in `build/`) after pulling a new project or switching .NET SDKs: it generates each project's `Directory.Build.props` (the target framework). Without it: NU1201 "does not support any target frameworks".
- `git -C LiteRT-LM lfs pull` before the macOS, iOS and Android builds (LiteRT-LM's prebuilt libraries are Git LFS files).
- The Bazel scripts keep Bazel's output in `bazel_output/` (`--output_user_root`); run `bazel` by hand with the same startup flag, or it starts again with a cold cache.
- `-p:ValidateXcodeVersion=false` when the .NET Apple SDK pack doesn't match the installed Xcode; `-p:RuntimeIdentifier=iossimulator-arm64` / `android-arm64` to build one architecture; `-p:AdbTarget="-s <serial>"` with several adb devices.
- Delete the platform's `bin`/`obj` after editing an `Info.plist`, or after changing native libraries for Android (an incremental build can leave them out of the APK).

## Running Tests

Tests use either NUnit or MSTest (selected at compile-time via the `VS_TEST` preprocessor symbol).

### TF Lite — run all tests
```bash
dotnet test Emgu.LiteRT.Test/Emgu.TF.Lite.Test/Emgu.TF.Lite.Test.Net/Emgu.TF.Lite.Test.Net.csproj
```

### Model-free tests in CI
`.github/workflows/tests.yml` runs the suite on Ubuntu without models or this repo's native libraries: `cmake -S . -B build` (generates the `Directory.Build.props`), then `dotnet test ... --filter "TestCategory!=Native"` (111 tests, ~4 s). Test classes that load `tfliteextern`/`libLiteRt` are in the `Native` category (`[Category("Native")]`, `[TestCategory]` under `VS_TEST`) - put new native-dependent test classes there too, or CI fails with `DllNotFoundException`.

### Run a single test
```bash
dotnet test <project.csproj> --filter "FullyQualifiedName~TestGetVersion"
```

Test assets (e.g., `grace_hopper.jpg`) must be present in the working directory when tests run.

The LiteRT-LM tests (`UnitTestLiteRtLm.cs`) download large models on the first run (Qwen3-0.6B ~500 MB, Gemma 4 E2B ~2.6 GB, E4B ~3.7 GB), so they are ignored by default, the same way as Emgu CV's model tests: opt in with `dotnet test <project.csproj> -p:TestModels=true`, which defines `TEST_MODELS` (each test has an `#if !TEST_MODELS` `[Ignore]` block).

Like Emgu CV, the app and test projects target the .NET version of the `dotnet` CMake runs: `cmake/modules/FindCSharp.cmake` sets `DOTNET_VERSION_MAJOR` from `dotnet --version`, and per-project `Directory.Build.props` generated from `cmake/*.Directory.Build.props.in` set `<TargetFramework>net${DOTNET_VERSION_MAJOR}.0</TargetFramework>` (`-windows` for `CVInterop.Lite`, `-android`/`-ios`/`-maccatalyst` for the MAUI projects); the csproj files name no version (a `TargetFramework` in a csproj would override the generated props, which are imported first). Re-run CMake (`build_emgutf`, or `cmake .` in `build/`) after switching .NET SDKs. So `Emgu.TF.Lite.Test.Net` needs the CMake-generated `Directory.Build.props` files (run the platform's `build_emgutf` once), and on macOS it copies `libtfliteextern.dylib`/`libLiteRt.dylib` from `lib/runtimes/osx/native/` via `Emgu.LiteRT.Runtime/Mac`'s `.projitems`.

On Linux the tests use the Ubuntu natives in `lib/runtimes/ubuntu-x64/native/` and Emgu CV's Ubuntu 26.04 runtime, and switch sqlite-net to the system SQLite (`SystemSqlite.cs`) - see `docs/native-builds.md`, "Running the .NET tests on Linux".

## Key Conventions

- **Extern library name**: `tfliteextern`. On iOS it is `@rpath/tfliteextern.framework/tfliteextern`; on Mac Catalyst and Unity iOS (static libraries) `__Internal`.
- **`UnmanagedObject`** (in `Emgu.LiteRT.Util/`): base class for all objects wrapping a native pointer; handles `Dispose`/finalizer pattern.
- **Calling convention**: `CallingConvention.Cdecl` throughout.
- **Boolean marshaling**: `UnmanagedType.U1` for `bool`, `UnmanagedType.Bool` for `int`-as-bool.
- **Error handling**: native errors are redirected via a callback delegate (`TfLiteErrorCallback`) and thrown as managed exceptions.
- The `litert/` directory is a submodule of `google-ai-edge/LiteRT`, pinned to commit `26895c9f` (`v2.2.0-643`): the LiteRT commit the `LiteRT-LM` submodule pins as its `@litert` repository (`LITERT_REF` in `LiteRT-LM/WORKSPACE`; LiteRT-LM v0.18.0). Do not modify files inside it directly.
  - **The `litert` submodule must match LiteRT-LM's LiteRT version.** The Bazel builds use LiteRT-LM's `@litert` (downloaded by Bazel), while the CMake builds (`litertextern/tfliteextern/CMakeLists.txt`) use this submodule; keeping them on the same commit means every `libLiteRt` is built from the same LiteRT source, so it is compatible with `liblitert-lm` (which links against it) and all platforms ship the same TFLite/LiteRT version. When updating the `LiteRT-LM` submodule, check its `LiteRT-LM/WORKSPACE` `LITERT_REF` and move `litert` to that commit too (`git -C litert checkout <LITERT_REF>`, then `git -C litert submodule update` for its nested `third_party/tensorflow`), and re-check the CMake builds. Then check whether the new `LITERT_REF` contains LiteRT commit `dd3db3123` ("Make the WeightLoader interface ABI-stable across the accelerator boundary"; `git -C litert merge-base --is-ancestor dd3db3123 HEAD`): if it does, the embedding models' GPU crash should be gone (see `Emgu.LiteRT.LM.Models`' `EmbeddingModel` above) - after rebuilding the natives, remove the "gpu" check from `EmbeddingModel.Init`, run `EmbeddingGemma2Multimodal` with `backend: "gpu"` and `VisionBackend = "gpu"` on the Mac (Metal) and the Pixel Fold (OpenCL), compare its embeddings with the CPU's (cosine > 0.99) and the text/image/audio searches, add a GPU test (skipped without the accelerator plugin, like `TestLiteRtMobilenetGpu`) and record the results. (Before following LiteRT-LM, `litert` was pinned to `v2.2.0`, not `main`: the bleeding-edge commit this was originally added at hit a WORKSPACE bug under Bazel; see `litertextern/tfliteextern/bazel/BUILD`'s comments.)
- `litertextern/` holds the Emgu-authored native wrapper (`tfliteextern`, `imgproc`) that both Windows build paths use — this is the one place native wrapper source changes should be made for that platform going forward. `litertextern/tfliteextern/bazel/{BUILD,tfliteextern.def}` is the Bazel-specific counterpart to `litertextern/tfliteextern/CMakeLists.txt`, staged into `LiteRT-LM/tfliteextern/` at build time (see above) since it can't live there in git.
- **Structure of the MAUI demo**: new testable logic goes into `Maui.Demo.Lite.Core` (no MAUI); the chat page is `Emgu.LiteRT.LM.Maui`'s `ChatPage` with the demo's features, and a new chat feature is a `ChatFeature` (on `DemoFeature`) in `Chat/Features/` that reaches the page through `IChatPageHost` and `IDemoHost` (see `Emgu.LiteRT.Example/Maui/ARCHITECTURE.md`).

## Where the details are

| Document | Read it when |
|---|---|
| `docs/native-builds.md` | building or changing the native libraries: CMake and Bazel, staging into the LiteRT-LM workspace, the C API wrapper, each platform's dynamic split (macOS, iOS, Android, Linux), Mac Catalyst, Windows exports, all build commands, CI |
| `docs/packaging.md` | changing NuGet packages, runtime packages or versions: package layout, the LiteRT-LM / OpenCL / Metal add-ons, versioning |
| `docs/litert-lm.md` | working on `Emgu.LiteRT.LM`, `.LM.Models` or `.LM.Extensions.AI`: C API facts, streaming, embeddings and their GPU crash, model quirks, tool calls and constrained decoding, LiteRT-LM bugs worked around |
| `docs/agents.md` | working on `Emgu.LiteRT.LM.Agents` or the reusable chat (`Emgu.LiteRT.LM.Chat`, `.Chat.Sqlite`, `.Maui`) |
| `Emgu.LiteRT.Models/MODELS.md` | working on `Emgu.LiteRT.Models`, `.Models.CV`, `.Models.CV.Maui`, `Emgu.TF.Lite.Models` or `.ImageIO`: Z-Image-Turbo, the detectors, MediaPipe trackers, YAMNet (the per-model evaluations are the other `*.md` files beside it) |
| `Emgu.LiteRT.Example/Maui/ARCHITECTURE.md` | changing the MAUI demo's structure: folders, the chat page as a `ChatPage` and its `DemoFeature`s, how to add a feature, a model or a setting, `SearchEmbedder`, `Maui.Demo.Lite.Core` |
| `Emgu.LiteRT.Example/Maui/FEATURES.md` | working on a chat feature: how each works and what was measured or verified on which device (compaction, GPU, documents, photos, images, server, voice, device actions, MCP, camera and sound monitors, exercise, meeting notes) |
| `Emgu.LiteRT.Example/Maui/README.md` | building, running, deploying and debugging the demo |
| `Emgu.LiteRT.Example/Maui/TODO.md` | looking for open work on the demo (finished items, and the completed plans, are in `Emgu.LiteRT.Example/Maui/history/`, under the same item numbers) |
