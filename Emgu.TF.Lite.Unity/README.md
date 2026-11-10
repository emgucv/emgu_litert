Emgu TF Lite for Unity
======================

The Unity project of Emgu TF Lite, on Unity 6 (6000.5). It uses the same C# sources as the .NET packages
(`Emgu.LiteRT.Util`, `Emgu.TF.Lite`, `Emgu.TF.Lite.ImageIO`, `Emgu.TF.Lite.Models`), copied in, and the native libraries
built by the `platforms/` scripts.

Setting up the project:

* Build the native libraries for the platforms you need (`docs/native-builds.md`). For iOS: `platforms/ios/bazel_build_xcframework`;
  for macOS: `platforms/macos/bazel_build_macos_fat`.
* Run `copy_unity_assets` (`copy_unity_assets.bat` on Windows, through WSL). It copies:
  * the C# sources into `Assets/Emgu.TF.Lite/Assets/Scripts/`;
  * the TensorFlow licence into `Assets/Emgu.TF.Lite/Documentation/`;
  * for iOS, `lib/ios/tfliteextern.xcframework` and `lib/ios/LiteRt.xcframework` into `Assets/Emgu.TF.Lite/Plugins/iOS/`;
  * for macOS, `lib/runtimes/osx/native/libtfliteextern.dylib` and `libLiteRt.dylib` into `Assets/Emgu.TF.Lite/Plugins/macOS/`.
* Run `copy_demo_assets` for the demo's images.
* `sync_unity_assets` copies edited sources back to the repository's projects.

The copied files are not tracked (see `.gitignore`); the plug-ins' `.meta` files are.

iOS:

* `tfliteextern.xcframework` (the TF Lite Interpreter wrapper) and `LiteRt.xcframework` (TensorFlow Lite itself, which it
  links) are dynamic frameworks. Their `.meta` files make them iOS-only plug-ins with "Add to Embedded Binaries": Unity links them
  into `UnityFramework`, where `Emgu.TF.Lite`'s `DllImport("__Internal")` calls resolve, and embeds them in the app.
* For the GPU, add `LiteRtMetalAccelerator.xcframework` from `lib/ios/` the same way.
* `Emgu.TF.Lite.BuildDemoScript.PerformIOSBuild` (`Assets/Emgu.TF.Lite/Demo/Editor/BuildDemoScript.cs`) writes the demo's
  Xcode project to `Builds/iOS`, for the device SDK; build it in Xcode with your own team:

  ```
  Unity -batchmode -quit -buildTarget iOS -projectPath Emgu.TF.Lite.Unity -executeMethod Emgu.TF.Lite.BuildDemoScript.PerformIOSBuild
  ```

* Checked on 2026-11-09 with Unity 6000.5.10f1 and Xcode 27 on an iPad Air (M4): a test scene loaded TF Lite 2.23.0, and
  the Mobilenet demo labels grace_hopper "military uniform" (72%, 92 ms).
* Unity 6 rather than 2021.3/2022.3: only Unity 6 takes `.xcframework` plug-ins.

macOS:

* `libtfliteextern.dylib` and `libLiteRt.dylib` are universal (arm64 and x86_64); `libtfliteextern.dylib` finds `libLiteRt.dylib`
  beside it (`@loader_path`). Their `.meta` files make them plug-ins for the macOS player and the editor on macOS; Unity puts
  them in the app's `Contents/PlugIns/`, where `DllImport("tfliteextern")` finds them.
* `Emgu.TF.Lite.BuildDemoScript.PerformOSXBuild` writes the demo to `Builds/OSX/osx.app`.
* Checked on 2026-11-09 with Unity 6000.5.10f1: a player built this way loaded TF Lite 2.23.0 and labelled grace_hopper
  "military uniform" (76%), both natively on Apple Silicon and as x86_64 under Rosetta. Run with `-batchmode -nographics`, it
  got a wrong label: without a graphics device the texture it reads has no pixels.

The Windows and Android plug-in folders (`Plugins/x86`, `x86_64`, `Android`) only hold `.meta` files from earlier releases:
their native libraries are copied in by hand and were not checked with Unity 6. Opening the project without them makes Unity
delete those `.meta` files; restore them with git before committing.
