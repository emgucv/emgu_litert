//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// LiteRT-LM's experimental C API (c/experimental.h): functions LiteRT-LM may change or remove in any release.
    /// </summary>
    public static class LiteRtLmExperimental
    {
        /// <summary>
        /// Whether the loaded liblitert-lm was built with the debugger's tracing backend (LITERT_LM_DEBUGGER_ENABLED=1),
        /// which writes the sessions' intermediate activations under their debug capture folder.
        /// </summary>
        public static bool IsDebuggerEnabled
        {
            get
            {
                bool enabled;
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_experimental_is_debugger_enabled(out enabled), "litert_lm_experimental_is_debugger_enabled");
                return enabled;
            }
        }

        /// <summary>
        /// Turn the Metal residency set (keeping the model's GPU buffers resident) on or off for a running engine on
        /// Apple GPUs; see EngineSettings.GpuEnableMetalResidencySet for the setting at creation.
        /// </summary>
        /// <param name="engine">The engine</param>
        /// <param name="enable">Whether to use the residency set</param>
        public static void UpdateGpuEnableMetalResidencySet(Engine engine, bool enable)
        {
            if (engine == null)
                throw new ArgumentNullException("engine");
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_experimental_engine_update_gpu_enable_metal_residency_set(engine, enable),
                "litert_lm_experimental_engine_update_gpu_enable_metal_residency_set");
        }

        /// <summary>
        /// The folder, relative to the working directory (e.g. "litert_lm_debugger/0"), where the debugger writes a
        /// session's intermediate activations, or null if there is none (the debugger isn't enabled).
        /// </summary>
        /// <param name="session">The session</param>
        /// <returns>The folder, or null</returns>
        public static String GetDebugCaptureDir(Session session)
        {
            if (session == null)
                throw new ArgumentNullException("session");
            IntPtr debugInfo;
            if (!LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_experimental_session_get_debug_info(session, out debugInfo), "litert_lm_experimental_session_get_debug_info"))
                return null;
            return ReadCaptureDir(debugInfo);
        }

        /// <summary>
        /// The folder where the debugger writes a conversation's session's intermediate activations, or null.
        /// </summary>
        /// <param name="conversation">The conversation</param>
        /// <returns>The folder, or null</returns>
        public static String GetDebugCaptureDir(Conversation conversation)
        {
            if (conversation == null)
                throw new ArgumentNullException("conversation");
            IntPtr debugInfo;
            if (!LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_experimental_conversation_get_session_debug_info(conversation, out debugInfo), "litert_lm_experimental_conversation_get_session_debug_info"))
                return null;
            return ReadCaptureDir(debugInfo);
        }

        // The capture folder of a LiteRtLmSessionDebugInfo, which is then deleted.
        private static String ReadCaptureDir(IntPtr debugInfo)
        {
            if (debugInfo == IntPtr.Zero)
                return null;
            try
            {
                IntPtr dir;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_experimental_session_debug_info_get_capture_dir(debugInfo, out dir), "litert_lm_experimental_session_debug_info_get_capture_dir")
                    ? LiteRtLmInvoke.PtrToStringUtf8(dir) : null;
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_experimental_session_debug_info_delete(debugInfo);
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_experimental_is_debugger_enabled([MarshalAs(BoolMarshalType)] out bool enabled);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_experimental_engine_update_gpu_enable_metal_residency_set(
            IntPtr engine, [MarshalAs(BoolMarshalType)] bool enableMetalResidencySet);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_experimental_session_get_debug_info(IntPtr session, out IntPtr debugInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_experimental_conversation_get_session_debug_info(IntPtr conversation, out IntPtr debugInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_experimental_session_debug_info_delete(IntPtr debugInfo);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_experimental_session_debug_info_get_capture_dir(IntPtr debugInfo, out IntPtr captureDir);
    }
}
