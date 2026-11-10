Emgu TF Lite for Unity
======================

The Unity project of Emgu TF Lite, on Unity 6 (6000.5). It uses the same C# sources as the .NET packages
(`Emgu.LiteRT.Util`, `Emgu.TF.Lite`, `Emgu.TF.Lite.ImageIO`, `Emgu.TF.Lite.Models`), copied in, and the native libraries
built by the `platforms/` scripts.

Setting up the project:

* Build the native libraries for the platforms you need (`docs/native-builds.md`). For iOS: `platforms/ios/bazel_build_xcframework`.
* Run `copy_unity_assets` (`copy_unity_assets.bat` on Windows, through WSL). It copies:
  * the C# sources into `Assets/Emgu.TF.Lite/Assets/Scripts/`;
  * the TensorFlow licence into `Assets/Emgu.TF.Lite/Documentation/`;
  * for iOS, `lib/ios/tfliteextern.xcframework` and `lib/ios/LiteRt.xcframework` into `Assets/Emgu.TF.Lite/Plugins/iOS/`.
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

The Windows, Android and macOS plug-in folders (`Plugins/x86`, `x86_64`, `Android`, `tfliteextern.bundle`) only hold `.meta`
files from earlier releases: their native libraries are copied in by hand and were not checked with Unity 6.
