Unity on Windows: TODO
======================

iOS, macOS and Android were brought up to date for Unity 6 (6000.5) and checked on devices on 2026-11-09/10 (`README.md`).
Windows wasn't: `Plugins/x86` and `Plugins/x86_64` hold only `.meta` files from earlier releases, for a statically linked
`tfliteextern.dll` and the Visual C++ runtime DLLs copied in by hand. This list is the Windows work still to do, on a
Windows machine with Unity 6000.5.

## What has changed since those `.meta` files

* The x64 Bazel build (`platforms/windows/bazel_build_litert.ps1 64`, since c8d57163) makes `tfliteextern.dll` a thin
  wrapper (47 KB) that imports the TFLite C API from `libLiteRt.dll`; both land in `lib/runtimes/win-x64/native/`. Unity
  needs both DLLs, as on macOS and Android. (`-StaticTfliteextern`, and `cmake_build_litert.ps1`, still give the old
  self-contained `tfliteextern.dll`.)
* The x86 and ARM64 builds still link the runtime into `tfliteextern.dll`.
* The build script also copies the Visual C++ runtime DLLs into `lib/runtimes/win-x64/native/`
  (`docs/native-builds.md`, Windows).

## To do

1. **Native libraries.** Build x64 with `bazel_build_litert.ps1 64` (thin `tfliteextern.dll` plus `libLiteRt.dll`).
   Decide whether 32-bit x86 is still worth supporting. If it isn't, remove `Plugins/x86` and its `.meta` files. If it
   is, check that the x86 build still works.
2. **`copy_unity_assets`.** Copy `../lib/runtimes/win-x64/native/{tfliteextern,libLiteRt}.dll` into
   `Assets/Emgu.TF.Lite/Plugins/x86_64/` (and the x86 ones into `Plugins/x86`, if kept). Print the same "missing" hint as for
   the other platforms. Add the DLLs to `.gitignore`, as for the `.dylib` and `.so` files. Check that
   `copy_unity_assets.bat`, which runs the script through WSL, still works.
3. **Visual C++ runtime.** Decide whether to bundle `msvcp140*.dll`, `vcruntime140*.dll`, `concrt140.dll` and
   `vccorlib140.dll`, or to require the Visual C++ Redistributable. Check what `tfliteextern.dll` and `libLiteRt.dll`
   actually import (`dumpbin /dependents`); the thin wrapper may need fewer of them than the old DLL. Remove the `.meta`
   files of any that aren't shipped.
4. **Plug-in `.meta` files.** Set them from an editor script with `PluginImporter`, as was done for macOS, so they are
   Unity 6's own:
   * `Plugins/x86_64/tfliteextern.dll.meta` (keep its GUID) and a new `libLiteRt.dll.meta`: Standalone Win64, and the
     editor with OS Windows and CPU x86_64. Every other platform excluded.
   * The x86 `tfliteextern.dll.meta` currently has `Exclude OSXUniversal: 0`. Fix this if x86 is kept.
   * The Windows editor settings must not clash with the macOS dylibs' (editor OS OSX), so the project opens on both.
5. **Loading `libLiteRt.dll`.** `tfliteextern.dll` imports it by name, so Windows has to find it when Unity loads
   `tfliteextern.dll` from the plug-in folder. Check this in the player (`<name>_Data/Plugins/x86_64/`) and in the editor
   (`Assets/.../Plugins/x86_64/`). If the editor doesn't find it, find a fix that doesn't change the shared C# sources (for
   example, preloading `libLiteRt.dll` from a Unity-only script), and record which one works.
6. **`BuildDemoScript.PerformWin64Build`.** It already builds `Scenes` into `Builds/Win64/win64.exe`. Try it with Mono and
   IL2CPP (IL2CPP needs Visual Studio's C++ tools), and pick one for the demo.
7. **Check on Windows.** Run both of these and record the results in `README.md`, as for the other platforms:
   * Play mode in the editor, with the Mobilenet and COCO SSD scenes.
   * The built player: TF Lite version 2.23.0, and grace_hopper labelled "military uniform" with its confidence and
     time.

   Don't run with `-nographics`: the demo's texture then has no pixels, so the label is wrong.
8. **ARM64 (optional).** Unity 6 builds Windows ARM64 players. Check whether a `win-arm64` build exists and is worth a
   `Plugins/ARM64` folder.
9. **Docs.** Replace the Windows paragraph at the end of `README.md` with a "Windows" section like the others. Add a line
   to `docs/native-builds.md` saying Unity uses the same `tfliteextern.dll` and `libLiteRt.dll`.

## Notes

* Opening the project on a Mac, where the Windows DLLs are missing, makes Unity delete their `.meta` files. Restore them
  with `git checkout` before committing (`README.md`). Making the Windows `.meta` files is therefore a job for the Windows
  machine.
* The Unity project uses only `Emgu.TF.Lite`'s Interpreter API (`DllImport("tfliteextern")`), not `Emgu.LiteRT`'s, so
  `libLiteRt.dll` is needed only because `tfliteextern.dll` depends on it. LiteRT-LM isn't part of the Unity project.
