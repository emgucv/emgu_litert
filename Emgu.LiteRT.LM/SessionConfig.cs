//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The configuration of a session (LiteRtLmSessionConfig).
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object
    /// (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class SessionConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private int _maxOutputTokens;
        private bool _applyPromptTemplate;
        private SamplerParams _samplerParams;
        private String _loraPath;
        private String _audioLoraPath;
        private bool _enableSpeculativeDecoding;
        private SpeculativeDecodingMode? _speculativeDecoding;

        /// <summary>
        /// Create a session configuration with the default values.
        /// </summary>
        public SessionConfig()
        {
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_create(out _ptr), "litert_lm_session_config_create");
        }

        /// <summary>
        /// The maximum number of tokens to generate
        /// </summary>
        public int MaxOutputTokens
        {
            get { return _maxOutputTokens; }
            set { LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_max_output_tokens(_ptr, value), "litert_lm_session_config_set_max_output_tokens"); _maxOutputTokens = value; }
        }

        /// <summary>
        /// Whether to apply the model's prompt template to the input
        /// </summary>
        public bool ApplyPromptTemplate
        {
            get { return _applyPromptTemplate; }
            set { LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_apply_prompt_template(_ptr, value), "litert_lm_session_config_set_apply_prompt_template"); _applyPromptTemplate = value; }
        }

        /// <summary>
        /// The sampler parameters. The values are copied when set.
        /// </summary>
        public SamplerParams SamplerParams
        {
            get { return _samplerParams; }
            set { LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_sampler_params(_ptr, value), "litert_lm_session_config_set_sampler_params"); _samplerParams = value; }
        }

        /// <summary>
        /// The path of the LoRA weights to use
        /// </summary>
        public String LoraPath
        {
            get { return _loraPath; }
            set
            {
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_lora_path(_ptr, LiteRtLmInvoke.ToUtf8(value)),
                    "litert_lm_session_config_set_lora_path");
                _loraPath = value;
            }
        }

        /// <summary>
        /// The path of the audio LoRA weights to use
        /// </summary>
        public String AudioLoraPath
        {
            get { return _audioLoraPath; }
            set
            {
                LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_audio_lora_path(_ptr, LiteRtLmInvoke.ToUtf8(value)),
                    "litert_lm_session_config_set_audio_lora_path");
                _audioLoraPath = value;
            }
        }

        /// <summary>
        /// How this session uses speculative decoding (e.g. MTP), overriding EngineSettings.SpeculativeDecoding; null
        /// until set (the engine's setting)
        /// </summary>
        public SpeculativeDecodingMode? SpeculativeDecoding
        {
            get { return _speculativeDecoding; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException("value", "Speculative decoding can't be unset; create a new config instead.");
                using (SpeculativeDecodingConfig config = new SpeculativeDecodingConfig(value.Value))
                    LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_speculative_decoding_config(_ptr, config), "litert_lm_session_config_set_speculative_decoding_config");
                _speculativeDecoding = value;
            }
        }

        /// <summary>
        /// Whether this session uses speculative decoding, if the model supports it
        /// </summary>
        [Obsolete("Use SpeculativeDecoding (litert_lm_session_config_set_enable_speculative_decoding is deprecated in LiteRT-LM's C API 1.0.0).")]
        public bool EnableSpeculativeDecoding
        {
            get { return _enableSpeculativeDecoding; }
            set { LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_session_config_set_enable_speculative_decoding(_ptr, value), "litert_lm_session_config_set_enable_speculative_decoding"); _enableSpeculativeDecoding = value; }
        }

        /// <summary>
        /// Release the unmanaged session configuration
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_session_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_create(out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_max_output_tokens(IntPtr config, int maxOutputTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_apply_prompt_template(
            IntPtr config,
            [MarshalAs(BoolMarshalType)] bool applyPromptTemplate);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_sampler_params(IntPtr config, IntPtr samplerParams);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_lora_path(IntPtr config, byte[] loraPath);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_session_config_set_audio_lora_path(IntPtr config, byte[] audioLoraPath);
    }
}
