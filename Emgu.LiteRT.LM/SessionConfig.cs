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

        /// <summary>
        /// Create a session configuration with the default values.
        /// </summary>
        public SessionConfig()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_session_config_create(), "litert_lm_session_config_create");
        }

        /// <summary>
        /// The maximum number of tokens to generate
        /// </summary>
        public int MaxOutputTokens
        {
            get { return _maxOutputTokens; }
            set { LiteRtLmInvoke.litert_lm_session_config_set_max_output_tokens(_ptr, value); _maxOutputTokens = value; }
        }

        /// <summary>
        /// Whether to apply the model's prompt template to the input
        /// </summary>
        public bool ApplyPromptTemplate
        {
            get { return _applyPromptTemplate; }
            set { LiteRtLmInvoke.litert_lm_session_config_set_apply_prompt_template(_ptr, value); _applyPromptTemplate = value; }
        }

        /// <summary>
        /// The sampler parameters. The values are copied when set.
        /// </summary>
        public SamplerParams SamplerParams
        {
            get { return _samplerParams; }
            set { LiteRtLmInvoke.litert_lm_session_config_set_sampler_params(_ptr, value); _samplerParams = value; }
        }

        /// <summary>
        /// The path of the LoRA weights to use
        /// </summary>
        public String LoraPath
        {
            get { return _loraPath; }
            set
            {
                LiteRtLmInvoke.CheckStatus(
                    LiteRtLmInvoke.litert_lm_session_config_set_lora_path(_ptr, LiteRtLmInvoke.ToUtf8(value)),
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
                LiteRtLmInvoke.CheckStatus(
                    LiteRtLmInvoke.litert_lm_session_config_set_audio_lora_path(_ptr, LiteRtLmInvoke.ToUtf8(value)),
                    "litert_lm_session_config_set_audio_lora_path");
                _audioLoraPath = value;
            }
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
        internal static extern IntPtr litert_lm_session_config_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_config_set_max_output_tokens(IntPtr config, int maxOutputTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_config_set_apply_prompt_template(
            IntPtr config,
            [MarshalAs(BoolMarshalType)] bool applyPromptTemplate);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_config_set_sampler_params(IntPtr config, IntPtr samplerParams);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_config_set_lora_path(IntPtr config, byte[] loraPath);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_config_set_audio_lora_path(IntPtr config, byte[] audioLoraPath);
    }
}
