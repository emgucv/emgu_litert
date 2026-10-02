//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.TF.Lite.LiteRt
{
    /// <summary>
    /// P/Invoke entry points into LiteRT's own runtime library (libLiteRt), which exposes the LiteRt* C API.
    /// Unlike TfLiteInvoke, there is no Emgu native wrapper in between: libLiteRt is built unmodified from
    /// the LiteRT repository.
    /// </summary>
    public static partial class LiteRtInvoke
    {
        /// <summary>
        /// The file name of the LiteRT runtime library. .NET appends the platform suffix, resolving to
        /// libLiteRt.dylib (macOS), libLiteRt.so (Linux / Android) or libLiteRt.dll (Windows).
        /// </summary>
        public const string LiteRtLibrary = "libLiteRt";

        /// <summary>
        /// The LiteRT native api calling convention
        /// </summary>
        public const CallingConvention LiteRtCallingConvention = CallingConvention.Cdecl;

        /// <summary>
        /// Represent a bool value in C
        /// </summary>
        public const UnmanagedType BoolMarshalType = UnmanagedType.U1;

        /// <summary>
        /// The string marshal type
        /// </summary>
        public const UnmanagedType StringMarshalType = UnmanagedType.LPStr;

        /// <summary>
        /// Get the description of a status code.
        /// </summary>
        /// <param name="status">The status code</param>
        /// <returns>The description of the status code</returns>
        public static String GetStatusString(Status status)
        {
            return Marshal.PtrToStringAnsi(LiteRtGetStatusString(status));
        }

        /// <summary>
        /// Throw a LiteRtException if the status is not Ok.
        /// </summary>
        /// <param name="status">The status returned by a LiteRt C API call</param>
        internal static void CheckStatus(Status status)
        {
            if (status != Status.Ok)
                throw new LiteRtException(status);
        }

        [DllImport(LiteRtLibrary, CallingConvention = LiteRtCallingConvention)]
        private static extern IntPtr LiteRtGetStatusString(Status status);
    }
}
