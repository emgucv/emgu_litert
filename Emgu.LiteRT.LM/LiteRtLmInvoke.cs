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
        /// On iOS it is LiteRtLm.framework (Emgu.LiteRT.LM.runtime.maui.ios, linked to LiteRt.framework), named by
        /// its path. Mac Catalyst (which also defines __IOS__) loads the macOS liblitert-lm.dylib from the app bundle
        /// by name instead: there is no Mac Catalyst build of LiteRT-LM, and a Catalyst app can load, but not link
        /// against, a macOS dylib.
        /// </summary>
#if (__IOS__ && !__MACCATALYST__) && (!UNITY_EDITOR)
        public const string LiteRtLmLibrary = "@rpath/LiteRtLm.framework/LiteRtLm";
#elif UNITY_IPHONE && (!UNITY_EDITOR)
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
            CheckError(litert_lm_set_min_log_level(level), "litert_lm_set_min_log_level");
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
        /// Throw a LiteRtLmException if a LiteRT-LM C API call didn't return Ok, with the status and the message
        /// LiteRT-LM recorded. As Emgu CV's CvInvoke.CheckError: called right after the call, on the same thread (the
        /// message is thread-local), so nothing is thrown while native frames are on the stack.
        /// </summary>
        /// <param name="status">The status returned by the call</param>
        /// <param name="functionName">The name of the C API function, for the exception</param>
        internal static void CheckError(LiteRtLmStatusCode status, String functionName)
        {
            if (status != LiteRtLmStatusCode.Ok)
                throw new LiteRtLmException(status, functionName, TakeLastError());
        }

        /// <summary>
        /// Whether a call that reports a missing value with NotFound found it: true for Ok, false for NotFound, else
        /// the exception CheckError throws.
        /// </summary>
        /// <param name="status">The status returned by the call</param>
        /// <param name="functionName">The name of the C API function, for the exception</param>
        /// <returns>True if the value was there</returns>
        internal static bool CheckFound(LiteRtLmStatusCode status, String functionName)
        {
            if (status == LiteRtLmStatusCode.NotFound)
            {
                DiscardLastError();
                return false;
            }
            CheckError(status, functionName);
            return true;
        }

        /// <summary>
        /// Drop the message recorded for a failure that is handled (e.g. a value that isn't there), so it isn't
        /// reported with a later failure.
        /// </summary>
        internal static void DiscardLastError()
        {
            litert_lm_clear_last_error();
        }

        // The loaded liblitert-lm's C API version: null until read; NoVersion if it is older than 1.0.0.
        private static Version _apiVersion;
        private static readonly Version NoVersion = new Version(0, 0, 0);

        /// <summary>
        /// The C API version of the loaded liblitert-lm (litert_lm_get_api_version, 1.0.0 for LiteRT-LM v0.19.0), or
        /// null if it is older than 1.0.0, which this wrapper can't use. Loads liblitert-lm.
        /// </summary>
        public static Version ApiVersion
        {
            get
            {
                if (_apiVersion == null)
                {
                    try
                    {
                        int major, minor, patch;
                        CheckError(litert_lm_get_api_version(out major, out minor, out patch), "litert_lm_get_api_version");
                        _apiVersion = new Version(major, minor, patch);
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _apiVersion = NoVersion;
                    }
                }
                return ReferenceEquals(_apiVersion, NoVersion) ? null : _apiVersion;
            }
        }

        private static bool? _isAvailable;

        /// <summary>
        /// True if liblitert-lm is in the app and loads: an app may be built without it, e.g. for iOS on an Intel Mac,
        /// where LiteRT-LM can't be built, and then offers only remote models. The first check loads it.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    try
                    {
                        int major, minor, patch;
                        litert_lm_get_api_version(out major, out minor, out patch);
                        _isAvailable = true;
                    }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
                    {
                        _isAvailable = false;
                    }
                }
                return _isAvailable.Value;
            }
        }

        /// <summary>
        /// True if the loaded liblitert-lm is LiteRT-LM v0.18.0 or later. Loads liblitert-lm.
        /// </summary>
        [Obsolete("Use ApiVersion: this wrapper needs C API 1.0.0 (LiteRT-LM v0.19.0) or later.")]
        public static bool IsV018OrLater
        {
            get { return ApiVersion != null; }
        }

        // The error message LiteRT-LM recorded for the last failed call on this thread, cleared once read so a later
        // failure that records none can't report it again. Must be called on the thread that made the failed call,
        // right after it (the error state is thread-local).
        private static String TakeLastError()
        {
            String message = PtrToStringUtf8(litert_lm_get_last_error_message());
            litert_lm_clear_last_error();
            return message;
        }

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        private static extern LiteRtLmStatusCode litert_lm_get_api_version(out int major, out int minor, out int patch);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        private static extern IntPtr litert_lm_get_last_error_message();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        private static extern void litert_lm_clear_last_error();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        private static extern LiteRtLmStatusCode litert_lm_set_min_log_level(LogSeverity level);
    }
}
