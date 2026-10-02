//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT
{
    /// <summary>
    /// The LiteRT environment (LiteRtEnvironment). It holds the runtime-wide state, such as the
    /// registered hardware accelerators, and is required to load models and create compiled models.
    /// </summary>
    /// <remarks>
    /// This class shares its name with System.Environment; if both namespaces are imported, refer to it as
    /// Emgu.LiteRT.Environment or use an alias.
    /// </remarks>
    public class Environment : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Create a LiteRT environment with the default options.
        /// </summary>
        public Environment()
        {
            LiteRtInvoke.CheckStatus(LiteRtInvoke.LiteRtCreateEnvironment(0, IntPtr.Zero, out _ptr));
        }

        /// <summary>
        /// Release the unmanaged environment
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtInvoke.LiteRtDestroyEnvironment(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtInvoke
    {
        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern Status LiteRtCreateEnvironment(int numOptions, IntPtr options, out IntPtr environment);

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        internal static extern void LiteRtDestroyEnvironment(IntPtr environment);
    }
}
