//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// A .litertlm model file opened for capability queries (LiteRtLmLoadedFile), without creating an Engine.
    /// </summary>
    public class LoadedFile : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Open a .litertlm model file.
        /// </summary>
        /// <param name="litertlmPath">The path of the model file</param>
        public LoadedFile(String litertlmPath)
        {
            if (litertlmPath == null)
                throw new ArgumentNullException("litertlmPath");
            _ptr = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_loaded_file_create(LiteRtLmInvoke.ToUtf8(litertlmPath)),
                "litert_lm_loaded_file_create");
        }

        /// <summary>
        /// Whether the model supports speculative decoding
        /// </summary>
        public bool HasSpeculativeDecodingSupport
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_has_speculative_decoding_support(_ptr); }
        }

        /// <summary>
        /// Whether the model supports thinking (see ThinkingConfig)
        /// </summary>
        public bool SupportsThinking
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_supports_thinking(_ptr); }
        }

        /// <summary>
        /// Whether the model supports function (tool) calling
        /// </summary>
        public bool SupportsFunctionCalling
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_supports_function_calling(_ptr); }
        }

        /// <summary>
        /// Whether the model accepts the given input modality.
        /// </summary>
        /// <param name="modality">The input modality</param>
        /// <returns>True if the model accepts the input modality</returns>
        public bool SupportsInputModality(Modality modality)
        {
            return LiteRtLmInvoke.litert_lm_loaded_file_supports_input_modality(_ptr, modality);
        }

        /// <summary>
        /// The model's default sampler type
        /// </summary>
        public SamplerType SamplerType
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_sampler_type(_ptr); }
        }

        /// <summary>
        /// The model's default sampling temperature
        /// </summary>
        public float SamplerTemperature
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_sampler_temperature(_ptr); }
        }

        /// <summary>
        /// The model's default top-k value
        /// </summary>
        public int SamplerTopK
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_sampler_top_k(_ptr); }
        }

        /// <summary>
        /// The model's default top-p value
        /// </summary>
        public float SamplerTopP
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_sampler_top_p(_ptr); }
        }

        /// <summary>
        /// The model's maximum number of vision tokens
        /// </summary>
        public int MaxVisionTokenBudget
        {
            get { return LiteRtLmInvoke.litert_lm_loaded_file_max_vision_token_budget(_ptr); }
        }

        /// <summary>
        /// Release the unmanaged file
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_loaded_file_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_loaded_file_create(byte[] litertlmPath);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_loaded_file_delete(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_loaded_file_has_speculative_decoding_support(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_loaded_file_supports_thinking(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_loaded_file_supports_function_calling(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern SamplerType litert_lm_loaded_file_sampler_type(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern float litert_lm_loaded_file_sampler_temperature(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_loaded_file_sampler_top_k(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern float litert_lm_loaded_file_sampler_top_p(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_loaded_file_supports_input_modality(IntPtr loadedFile, Modality modality);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_loaded_file_max_vision_token_budget(IntPtr loadedFile);
    }
}
