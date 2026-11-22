//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>The platform this build runs on, for <see cref="ChatModelCatalog.For"/>.</summary>
    public static class ChatDevicePlatform
    {
        /// <summary>The platform this build runs on.</summary>
        public static ChatPlatform Current =>
#if ANDROID
            ChatPlatform.Android;
#elif MACCATALYST
            ChatPlatform.MacCatalyst;
#elif IOS
            ChatPlatform.iOS;
#else
            ChatPlatform.Windows;
#endif

        /// <summary>
        /// True on a device that allows an app little memory: an iPhone or iPad with less than 5 GB (an iPhone 12's 4 GB
        /// allow ~2.2 GB, with the memory entitlements). Android phones weren't measured.
        /// </summary>
        public static bool LowMemory =>
#if IOS && !MACCATALYST
            Foundation.NSProcessInfo.ProcessInfo.PhysicalMemory < 5_000_000_000UL;
#else
            false;
#endif

        /// <summary>True on an iPad with 12 GB or more of memory (an iPad Air or Pro with an M4): it can hold Gemma 4 12B.</summary>
        public static bool HighMemory =>
#if IOS && !MACCATALYST
            Foundation.NSProcessInfo.ProcessInfo.PhysicalMemory >= 11_000_000_000UL;
#else
            false;
#endif

        /// <summary>
        /// Whether the app has a GPU accelerator for the language models. On Windows, only with the WebGPU bundle
        /// (<c>platforms/windows/bazel_build_windows_gpu.ps1</c>, the <c>Emgu.LiteRT.GPU.runtime.windows</c> package):
        /// <c>libLiteRtWebGpuAccelerator.dll</c> and the Windows SDK's <c>dxcompiler.dll</c>, in the app's folder or its
        /// <c>runtimes\win-&lt;arch&gt;\native</c>. Without the accelerator a model on the GPU fails as its engine is made
        /// ("litert_lm_engine_create failed (Unknown)"). The accelerator alone isn't enough to go by: a CPU build that copies
        /// <c>lib\runtimes\win-x64\native\*.dll</c> takes it along after a GPU native build, without the shader compilers Dawn
        /// needs, which come only with the bundle. Elsewhere true: the accelerators ship with LiteRT-LM (OpenCL on Android,
        /// Metal on iOS and Mac Catalyst).
        /// </summary>
        public static bool GpuAvailable =>
#if WINDOWS
            HasNativeLibrary("libLiteRtWebGpuAccelerator.dll") && HasNativeLibrary("dxcompiler.dll");

        private static bool HasNativeLibrary(string fileName) =>
            LiteRtLmNativeLoader.FindNativeLibrary(System.AppContext.BaseDirectory, fileName,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture) != null;
#else
            true;
#endif

        /// <summary>
        /// The catalog's models (<see cref="ChatModelCatalog.For"/>) this app can run: none when LiteRT-LM isn't in the app
        /// (<see cref="LiteRtLmInvoke.IsAvailable"/>; e.g. an iOS build from an Intel Mac, where LiteRT-LM can't be built), so
        /// the app offers only its remote models; on the CPU only without a GPU accelerator (<see cref="GpuAvailable"/>). On a
        /// <see cref="LowMemory"/> device, Gemma 4 prefills in small pieces.
        /// </summary>
        public static IList<ChatModelOption> AvailableModels() =>
            LiteRtLmInvoke.IsAvailable ? ChatModelCatalog.For(Current, LowMemory, HighMemory, GpuAvailable) : new List<ChatModelOption>();
    }
}
