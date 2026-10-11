//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Emgu.LiteRT.LM
{
    public static partial class LiteRtLmInvoke
    {
        // LiteRT's GPU accelerator plugins, by platform, that may be installed next to libLiteRt.
        private static readonly string[] MacAcceleratorPlugins = { "libLiteRtMetalAccelerator.dylib" };

        private static int _acceleratorPluginsLoaded;

        /// <summary>
        /// Load LiteRT's GPU accelerator plugins that are installed next to libLiteRt, so a "gpu" engine can use them.
        /// Called before an engine with a GPU backend is created.
        /// </summary>
        /// <remarks>
        /// libLiteRt loads its accelerator plugins with dlopen and just the file name. On macOS that only finds a
        /// plugin on the library search path or in the current folder - not next to libLiteRt.dylib, where the
        /// Emgu.LiteRT.Metal.runtime.* packages put libLiteRtMetalAccelerator.dylib (runtimes/osx/native in a .NET app):
        /// a console app couldn't use the GPU unless it was started from that folder. Loading the plugin by its full
        /// path first makes LiteRT's dlopen return the library that is already loaded. (Mac Catalyst apps found it in
        /// their bundle either way.)
        /// </remarks>
        internal static void LoadAcceleratorPlugins()
        {
            if (Interlocked.Exchange(ref _acceleratorPluginsLoaded, 1) != 0)
                return;
#if (__IOS__ && !__MACCATALYST__) || UNITY_IPHONE
            // iOS links LiteRT-LM into the app (CLiteRTLM.framework); there are no plugin files to load.
#else
#if !__MACCATALYST__
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return;
#endif
            foreach (string folder in NativeLibraryFolders())
            {
                foreach (string plugin in MacAcceleratorPlugins)
                {
                    string path = Path.Combine(folder, plugin);
                    try
                    {
                        if (File.Exists(path))
                            dlopen(path, RtldNow);
                    }
                    catch (Exception)
                    {
                        // Without it, LiteRT reports that no GPU accelerator could be loaded.
                    }
                }
            }
#endif
        }

        // Where .NET puts an app's native libraries: the app folder (a RID-specific build, a Mac Catalyst app's
        // Contents/MonoBundle), or runtimes/<rid>/native under it (a portable build using the runtime packages).
        private static string[] NativeLibraryFolders()
        {
            string baseFolder = AppContext.BaseDirectory ?? String.Empty;
            return new[]
            {
                baseFolder,
                Path.Combine(baseFolder, "runtimes", "osx", "native"),
                Path.Combine(baseFolder, "runtimes", "osx-arm64", "native"),
                Path.Combine(baseFolder, "runtimes", "maccatalyst-arm64", "native")
            };
        }

        private const int RtldNow = 2;

        [DllImport("libSystem.dylib")]
        private static extern IntPtr dlopen(String path, int mode);
    }
}
