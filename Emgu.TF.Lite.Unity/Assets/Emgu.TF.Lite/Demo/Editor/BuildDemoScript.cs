#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections;

namespace Emgu.TF.Lite
{
    public class BuildDemoScript
    {
        private static readonly string[] Scenes =
        {
            "Assets/Emgu.TF.Lite/Demo/MobilenetScene.unity",
            "Assets/Emgu.TF.Lite/Demo/CocoSsdMobilenet.unity"
        };

        static void PerformOSXBuild()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            BuildPipeline.BuildPlayer(Scenes, "Builds/OSX/osx.app", BuildTarget.StandaloneOSX, BuildOptions.None);
        }


        static void PerformWin64Build()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);
            BuildPipeline.BuildPlayer(Scenes, "Builds/Win64/win64.exe", BuildTarget.StandaloneWindows64,
                BuildOptions.None);
        }

        // An APK for arm64-v8a and armeabi-v7a devices, with libtfliteextern.so and libLiteRt.so (Plugins/Android/libs, from
        // copy_unity_assets). IL2CPP: Mono can't build arm64.
        static void PerformAndroidBuild()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            EditorUserBuildSettings.buildAppBundle = false;
            BuildPipeline.BuildPlayer(Scenes, "Builds/Android/Emgu.TF.Lite.Unity.apk", BuildTarget.Android, BuildOptions.None);
        }

        // The Xcode project, in Builds/iOS: build it with Xcode, signed with your own team.
        // tfliteextern.xcframework and LiteRt.xcframework (Plugins/iOS, from copy_unity_assets) are linked into
        // UnityFramework and embedded in the app.
        static void PerformIOSBuild()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            BuildPipeline.BuildPlayer(Scenes, "Builds/iOS", BuildTarget.iOS, BuildOptions.None);
        }

    }
}
#endif
