//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// P/Invoke entry points into LiteRT-LM's C API library (liblitert-lm), which exposes the litert_lm_* functions
    /// declared in LiteRT-LM's c/*.h headers. There is no Emgu native wrapper in between: liblitert-lm is built
    /// unmodified from the LiteRT-LM repository.
    /// </summary>
    public static partial class LiteRtLmInvoke
    {
        /// <summary>
        /// The file name of the LiteRT-LM C API library. .NET appends the platform suffix, resolving to
        /// liblitert-lm.dylib (macOS), liblitert-lm.so (Linux / Android) or liblitert-lm.dll (Windows).
        /// </summary>
#if (__IOS__ || UNITY_IPHONE) && (!UNITY_EDITOR)
        public const string LiteRtLmLibrary = "__Internal";
#else
        public const string LiteRtLmLibrary = "liblitert-lm";
#endif

        /// <summary>
        /// The LiteRT-LM native api calling convention
        /// </summary>
        public const CallingConvention LiteRtLmCallingConvention = CallingConvention.Cdecl;

        /// <summary>
        /// Represent a bool value in C
        /// </summary>
        public const UnmanagedType BoolMarshalType = UnmanagedType.U1;

        /// <summary>
        /// Set the minimum severity of the messages LiteRT-LM writes to its native log.
        /// </summary>
        /// <param name="level">The minimum severity to log</param>
        public static void SetMinLogLevel(LogSeverity level)
        {
            litert_lm_set_min_log_level(level);
        }

        /// <summary>
        /// Convert a string to a null terminated UTF-8 byte array, for a const char* parameter. LiteRT-LM expects
        /// UTF-8 (prompts, JSON messages, paths), while UnmanagedType.LPStr would use the ANSI code page on Windows.
        /// </summary>
        /// <param name="value">The string to convert, may be null</param>
        /// <returns>The null terminated UTF-8 bytes, or null if the string is null</returns>
        internal static byte[] ToUtf8(String value)
        {
            if (value == null)
                return null;
            int count = Encoding.UTF8.GetByteCount(value);
            byte[] bytes = new byte[count + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
            return bytes;
        }

        /// <summary>
        /// Read a null terminated UTF-8 string from native memory.
        /// </summary>
        /// <param name="ptr">The pointer to the string</param>
        /// <returns>The string, or null if the pointer is null</returns>
        internal static String PtrToStringUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;
            int length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
                length++;
            if (length == 0)
                return String.Empty;
            byte[] bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Throw a LiteRtLmException if a LiteRT-LM C API call returned NULL. The C API reports no error
        /// message; the details are written to LiteRT-LM's native log.
        /// </summary>
        /// <param name="ptr">The pointer returned by the call</param>
        /// <param name="functionName">The name of the C API function, for the exception message</param>
        /// <returns>The pointer</returns>
        internal static IntPtr CheckPtr(IntPtr ptr, String functionName)
        {
            if (ptr == IntPtr.Zero)
                throw new LiteRtLmException(String.Format("{0} failed; see the LiteRT-LM log for details", functionName));
            return ptr;
        }

        /// <summary>
        /// Throw a LiteRtLmException if a LiteRT-LM C API call returned a non-zero status.
        /// </summary>
        /// <param name="status">The status returned by the call, 0 on success</param>
        /// <param name="functionName">The name of the C API function, for the exception message</param>
        internal static void CheckStatus(int status, String functionName)
        {
            if (status != 0)
                throw new LiteRtLmException(String.Format("{0} failed with status {1}; see the LiteRT-LM log for details", functionName, status));
        }

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        private static extern void litert_lm_set_min_log_level(LogSeverity level);
    }
}
