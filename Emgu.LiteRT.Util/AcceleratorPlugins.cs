//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Emgu.LiteRT.Util
{
    /// <summary>
    /// Loads LiteRT's GPU accelerator plugins that are installed next to libLiteRt, before LiteRT looks for them.
    /// </summary>
    /// <remarks>
    /// libLiteRt loads its accelerator plugins with dlopen and just the file name. On macOS that only finds a plugin
    /// on the library search path or in the current folder - not next to libLiteRt.dylib, where the
    /// Emgu.LiteRT.Metal.runtime.* packages put libLiteRtMetalAccelerator.dylib (runtimes/osx/native in a .NET app): a
    /// console app couldn't use the GPU unless it was started from that folder. Loading the plugin by its full path
    /// first makes LiteRT's dlopen return the library that is already loaded. (Mac Catalyst apps found it in their
    /// bundle either way; Android finds plugins in the app's library folder.) Emgu.LiteRT's Environment and
    /// Emgu.LiteRT.LM's Engine call Load before LiteRT registers its accelerators.
    /// </remarks>
    public static class AcceleratorPlugins
    {
        // LiteRT's GPU accelerator plugins for macOS that may be installed next to libLiteRt.dylib.
        private static readonly string[] MacPlugins = { "libLiteRtMetalAccelerator.dylib" };

        private static int _loaded;

        /// <summary>
        /// Load the accelerator plugins found where .NET puts the app's native libraries (once per process; does
        /// nothing on other platforms or if none is installed).
        /// </summary>
        public static void Load()
        {
            if (Interlocked.Exchange(ref _loaded, 1) != 0)
                return;
#if (__IOS__ && !__MACCATALYST__) || UNITY_IPHONE
            // iOS links LiteRT into the app; there are no plugin files to load.
#else
#if !__MACCATALYST__
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return;
#endif
            foreach (string folder in NativeLibraryFolders())
            {
                foreach (string plugin in MacPlugins)
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
