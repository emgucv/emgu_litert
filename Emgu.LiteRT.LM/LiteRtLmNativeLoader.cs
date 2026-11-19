//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// Finds liblitert-lm and libomniextern on Windows when they aren't where .NET's DllImport looks. An app that gets
    /// the native DLLs as files copied to runtimes\win-&lt;arch&gt;\native (Emgu.LiteRT.Runtime/Windows's shared project,
    /// as the MAUI demo does) has them out of deps.json, so DllImport only searches the app's folder; they loaded only
    /// after Emgu.TF.Lite's loader had set that folder as the DLL directory. LiteRtLmInvoke.IsAvailable, checked when
    /// the chat page is made, came first and cached false: the demo offered no on-device models (2026-11-18).
    /// LiteRtLmInvoke and OmniInvoke call <see cref="EnsureLoaded"/> from their static constructors: it loads each DLL
    /// by its full path, and Windows then gives the already loaded module to the bare-name DllImport.
    /// </summary>
    public static class LiteRtLmNativeLoader
    {
        private static readonly object _lock = new object();
        private static bool _done;

        /// <summary>
        /// The full path of <paramref name="fileName"/> in <paramref name="baseDirectory"/> or, failing that, in its
        /// runtimes\win-&lt;arch&gt;\native folder; null if it is in neither.
        /// </summary>
        /// <param name="baseDirectory">The folder to look in, e.g. the app's folder</param>
        /// <param name="fileName">The file, e.g. "liblitert-lm.dll"</param>
        /// <param name="architecture">The process architecture, which names the runtimes folder</param>
        public static string FindNativeLibrary(string baseDirectory, string fileName, Architecture architecture)
        {
            if (String.IsNullOrEmpty(baseDirectory) || String.IsNullOrEmpty(fileName))
                return null;
            string inBase = Path.Combine(baseDirectory, fileName);
            if (File.Exists(inBase))
                return inBase;
            string rid = WindowsRid(architecture);
            if (rid == null)
                return null;
            string inRuntimes = Path.Combine(baseDirectory, "runtimes", rid, "native", fileName);
            return File.Exists(inRuntimes) ? inRuntimes : null;
        }

        private static string WindowsRid(Architecture architecture)
        {
            switch (architecture)
            {
                case Architecture.X64: return "win-x64";
                case Architecture.X86: return "win-x86";
                case Architecture.Arm64: return "win-arm64";
                case Architecture.Arm: return "win-arm";
                default: return null;
            }
        }

        /// <summary>
        /// On Windows, loads liblitert-lm.dll and libomniextern.dll by their full paths, from the app's folder or its
        /// runtimes\win-&lt;arch&gt;\native folder, once. Does nothing elsewhere, or when a DLL isn't found (DllImport
        /// then searches as before). Never throws.
        /// </summary>
        public static void EnsureLoaded()
        {
#if !((__IOS__ || UNITY_IPHONE) && !UNITY_EDITOR)
            lock (_lock)
            {
                if (_done)
                    return;
                _done = true;
                try
                {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        return;
                    // liblitert-lm first: libomniextern imports it.
                    Load(LiteRtLmInvoke.LiteRtLmLibrary + ".dll");
                    Load(OmniInvoke.OmniLibrary + ".dll");
                }
                catch (Exception e)
                {
                    Trace.WriteLine("LiteRtLmNativeLoader: " + e.Message);
                }
            }
#endif
        }

        private static void Load(string fileName)
        {
            string path = null;
            foreach (string directory in BaseDirectories())
            {
                path = FindNativeLibrary(directory, fileName, RuntimeInformation.ProcessArchitecture);
                if (path != null)
                    break;
            }
            if (path == null)
            {
                Trace.WriteLine("LiteRtLmNativeLoader: " + fileName + " not found; DllImport searches as usual.");
                return;
            }
            // A rooted path loads with LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR: libLiteRt.dll, libGemmaModelConstraintProvider.dll
            // and the MSVC runtime are found next to it.
            if (Emgu.LiteRT.Util.Toolbox.LoadLibrary(path) == IntPtr.Zero)
                Trace.WriteLine("LiteRtLmNativeLoader: could not load " + path);
        }

        private static string[] BaseDirectories()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string assemblyLocation = typeof(LiteRtLmNativeLoader).Assembly.Location;
            string assemblyDirectory = String.IsNullOrEmpty(assemblyLocation) ? null : Path.GetDirectoryName(assemblyLocation);
            return new[] { appDirectory, assemblyDirectory };
        }
    }
}
