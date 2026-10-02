//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT
{
    /// <summary>
    /// The compilation options used to create a CompiledModel (LiteRtOptions).
    /// </summary>
    public class Options : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Create compilation options with the default settings.
        /// </summary>
        public Options()
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateOptions(out _ptr));
        }

        /// <summary>
        /// Create compilation options targeting the specific hardware accelerators.
        /// </summary>
        /// <param name="hardwareAccelerators">The hardware accelerators to use</param>
        public Options(HwAccelerators hardwareAccelerators)
            : this()
        {
            HardwareAccelerators = hardwareAccelerators;
        }

        /// <summary>
        /// The hardware accelerators the model should be compiled for.
        /// </summary>
        public HwAccelerators HardwareAccelerators
        {
            get
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtGetOptionsHardwareAccelerators(_ptr, out int accelerators));
                return (HwAccelerators)accelerators;
            }
            set
            {
                LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtSetOptionsHardwareAccelerators(_ptr, (int)value));
            }
        }

        /// <summary>
        /// Release the unmanaged options
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtInvoke.LiteRtDestroyOptions(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtInvoke
    {
        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateOptions(out IntPtr options);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyOptions(IntPtr options);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtSetOptionsHardwareAccelerators(IntPtr options, int hardwareAccelerators);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtGetOptionsHardwareAccelerators(IntPtr options, out int hardwareAccelerators);
    }
}
