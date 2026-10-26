//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// One CPU thread count for every model an app loads, kept in the app's preferences: at most the number of cores (physical
    /// cores on Windows, where SMT doubles the logical processors), 4 or fewer by default. An app shows a setting for it and passes
    /// <see cref="Count"/> to each model it loads; a change applies when the models are loaded again.
    /// </summary>
    public static class CpuThreadSettings
    {
        private const string PreferenceKey = "model_cpu_threads";

        /// <summary>The number of cores: the most threads <see cref="Count"/> allows.</summary>
        public static int Maximum { get; } = GetCoreCount();

        /// <summary>
        /// Raised by <see cref="RequestReload"/>: a page holding a model it loaded with the old count compares this with the value it
        /// saw then, and reloads it when they differ.
        /// </summary>
        public static int ReloadVersion { get; private set; }

        /// <summary>Tell pages that hold models to load them again with the new <see cref="Count"/> (raises <see cref="ReloadVersion"/>).</summary>
        public static void RequestReload() => ReloadVersion++;

        /// <summary>The threads models use, 1 to <see cref="Maximum"/>; saved in the app's preferences.</summary>
        public static int Count
        {
            get => Math.Clamp(Preferences.Default.Get(PreferenceKey, Math.Min(4, Maximum)), 1, Maximum);
            set => Preferences.Default.Set(PreferenceKey, Math.Clamp(value, 1, Maximum));
        }

        private static int GetCoreCount()
        {
#if WINDOWS
            // Count physical cores across all sockets, rather than SMT logical processors.
            uint length = 0;
            GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
            if (length > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
                try
                {
                    if (GetLogicalProcessorInformationEx(0, buffer, ref length))
                    {
                        int count = 0;
                        for (int offset = 0; offset + 8 <= length;)
                        {
                            IntPtr entry = IntPtr.Add(buffer, offset);
                            int size = Marshal.ReadInt32(entry, 4);
                            if (size < 8 || size > length - offset)
                                break;
                            if (Marshal.ReadInt32(entry) == 0)
                                count++;
                            offset += size;
                        }
                        if (count > 0)
                            return count;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
#endif
            return Math.Max(1, Environment.ProcessorCount);
        }

#if WINDOWS
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
#endif
    }
}
