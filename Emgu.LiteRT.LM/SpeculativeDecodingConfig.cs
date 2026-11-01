//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// How speculative decoding (e.g. MTP, for models that have it: LoadedFile.HasSpeculativeDecodingSupport) is used
    /// (LiteRtLmSpeculativeDecodingConfig): set on EngineSettings for every session, or on a SessionConfig for one. The
    /// config is copied when set, so it can be disposed afterwards.
    /// </summary>
    /// <remarks>The C API has no getter: Mode returns the value last set through this object.</remarks>
    public class SpeculativeDecodingConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private SpeculativeDecodingMode? _mode;

        /// <summary>
        /// Create a config that leaves every field to the engine's default until set.
        /// </summary>
        public SpeculativeDecodingConfig()
        {
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_speculative_decoding_config_create(out _ptr), "litert_lm_speculative_decoding_config_create");
        }

        /// <summary>
        /// Create a config with a mode.
        /// </summary>
        /// <param name="mode">When to speculate</param>
        public SpeculativeDecodingConfig(SpeculativeDecodingMode mode)
            : this()
        {
            Mode = mode;
        }

        /// <summary>
        /// When to speculate, or null if not set (the engine's default)
        /// </summary>
        public SpeculativeDecodingMode? Mode
        {
            get { return _mode; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value", "The mode can't be unset; create a new config instead.");
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_speculative_decoding_config_set_mode(_ptr, value.Value), "litert_lm_speculative_decoding_config_set_mode");
                _mode = value;
            }
        }

        /// <summary>
        /// Release the unmanaged config
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_speculative_decoding_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_speculative_decoding_config_create(out IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_speculative_decoding_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_speculative_decoding_config_set_mode(IntPtr config, SpeculativeDecodingMode mode);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_settings_set_speculative_decoding_config(IntPtr settings, IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_speculative_decoding_config(IntPtr config, IntPtr speculativeDecodingConfig);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_enable_speculative_decoding(
            IntPtr config, [MarshalAs(BoolMarshalType)] bool enableSpeculativeDecoding);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_settings_set_single_threaded_execution(
            IntPtr settings, [MarshalAs(BoolMarshalType)] bool singleThreadedExecution);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_engine_settings_create_from_raw_file_descriptor(
            int fd, byte[] backend, byte[] visionBackend, byte[] audioBackend, out IntPtr settings);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_conversation_wait_until_done(IntPtr conversation);
    }
}
