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

        static void PerformAndroidBuild()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            BuildPipeline.BuildPlayer(Scenes, "Builds/Android", BuildTarget.Android, BuildOptions.None);
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
