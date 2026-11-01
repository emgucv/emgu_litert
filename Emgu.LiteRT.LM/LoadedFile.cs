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
    /// <remarks>A value the file doesn't define is reported as false, 0 / -1 / Unspecified for the older properties
    /// (as before C API 1.0.0), and null for the others.</remarks>
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
            LiteRtLmInvoke.CheckError(LiteRtLmInvoke.litert_lm_loaded_file_create(LiteRtLmInvoke.ToUtf8(litertlmPath), out _ptr), "litert_lm_loaded_file_create");
        }

        /// <summary>
        /// Whether the model supports speculative decoding
        /// </summary>
        public bool HasSpeculativeDecodingSupport
        {
            get
            {
                bool supported;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_has_speculative_decoding_support(_ptr, out supported), "litert_lm_loaded_file_has_speculative_decoding_support") && supported;
            }
        }

        /// <summary>
        /// Whether the model supports thinking (see ThinkingConfig)
        /// </summary>
        public bool SupportsThinking
        {
            get
            {
                bool supported;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_supports_thinking(_ptr, out supported), "litert_lm_loaded_file_supports_thinking") && supported;
            }
        }

        /// <summary>
        /// Whether the model supports function (tool) calling
        /// </summary>
        public bool SupportsFunctionCalling
        {
            get
            {
                bool supported;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_supports_function_calling(_ptr, out supported), "litert_lm_loaded_file_supports_function_calling") && supported;
            }
        }

        /// <summary>
        /// Whether the model accepts the given input modality.
        /// </summary>
        /// <param name="modality">The input modality</param>
        /// <returns>True if the model accepts the input modality</returns>
        public bool SupportsInputModality(Modality modality)
        {
            bool supported;
            return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_supports_input_modality(_ptr, modality, out supported), "litert_lm_loaded_file_supports_input_modality") && supported;
        }

        /// <summary>
        /// The model's default sampler type (Unspecified if the file has no language model metadata)
        /// </summary>
        public SamplerType SamplerType
        {
            get
            {
                SamplerType type;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_sampler_type(_ptr, out type), "litert_lm_loaded_file_sampler_type") ? type : SamplerType.Unspecified;
            }
        }

        /// <summary>
        /// The model's default sampling temperature (0 if not defined)
        /// </summary>
        public float SamplerTemperature
        {
            get
            {
                float temperature;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_sampler_temperature(_ptr, out temperature), "litert_lm_loaded_file_sampler_temperature") ? temperature : 0;
            }
        }

        /// <summary>
        /// The model's default top-k value (0 if not defined)
        /// </summary>
        public int SamplerTopK
        {
            get
            {
                int topK;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_sampler_top_k(_ptr, out topK), "litert_lm_loaded_file_sampler_top_k") ? topK : 0;
            }
        }

        /// <summary>
        /// The model's default top-p value (0 if not defined)
        /// </summary>
        public float SamplerTopP
        {
            get
            {
                float topP;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_sampler_top_p(_ptr, out topP), "litert_lm_loaded_file_sampler_top_p") ? topP : 0;
            }
        }

        /// <summary>
        /// The model's maximum number of vision tokens (-1 if not defined)
        /// </summary>
        public int MaxVisionTokenBudget
        {
            get
            {
                int budget;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_max_vision_token_budget(_ptr, out budget), "litert_lm_loaded_file_max_vision_token_budget") ? budget : -1;
            }
        }

        /// <summary>
        /// The model's maximum context, in tokens, or null if the file doesn't say
        /// </summary>
        public uint? MaxContextTokens
        {
            get
            {
                uint tokens;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_max_context_tokens(_ptr, out tokens), "litert_lm_loaded_file_max_context_tokens") ? tokens : (uint?)null;
            }
        }

        /// <summary>
        /// Whether the model's context length is dynamic (chosen when the engine is created), or null if the file
        /// doesn't say
        /// </summary>
        public bool? IsDynamicContext
        {
            get
            {
                bool dynamic;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_is_dynamic_context(_ptr, out dynamic), "litert_lm_loaded_file_is_dynamic_context") ? dynamic : (bool?)null;
            }
        }

        /// <summary>
        /// The vision input lengths (signatures) the model supports, or null if it has no vision or doesn't define them
        /// </summary>
        public int[] VisionSignatureSelection
        {
            get { return ReadLengths(LiteRtLmInvoke.litert_lm_loaded_file_vision_signature_selection, "litert_lm_loaded_file_vision_signature_selection"); }
        }

        /// <summary>
        /// The input lengths (signatures) an embedding model supports, or null if it doesn't define them
        /// </summary>
        public int[] EmbeddingSignatureSelection
        {
            get { return ReadLengths(LiteRtLmInvoke.litert_lm_loaded_file_embedding_signature_selection, "litert_lm_loaded_file_embedding_signature_selection"); }
        }

        /// <summary>
        /// The backends the model's part for a modality can run on, or null if the file doesn't say.
        /// </summary>
        /// <param name="modality">The modality</param>
        /// <returns>The backends, or null</returns>
        public BackendType[] GetSupportedBackends(Modality modality)
        {
            UIntPtr count;
            if (!LiteRtLmInvoke.CheckFound(
                    LiteRtLmInvoke.litert_lm_loaded_file_modality_supported_backends(_ptr, modality, null, UIntPtr.Zero, out count),
                    "litert_lm_loaded_file_modality_supported_backends"))
                return null;
            BackendType[] backends = new BackendType[checked((int)count.ToUInt64())];
            if (backends.Length > 0)
                LiteRtLmInvoke.CheckError(
                    LiteRtLmInvoke.litert_lm_loaded_file_modality_supported_backends(_ptr, modality, backends, new UIntPtr((uint)backends.Length), out count),
                    "litert_lm_loaded_file_modality_supported_backends");
            return backends;
        }

        /// <summary>
        /// The NPU the model's part for a modality was compiled for, or null if the file doesn't say.
        /// </summary>
        /// <param name="modality">The modality</param>
        /// <returns>The NPU brand, or null</returns>
        public NpuBrand? GetNpuBrand(Modality modality)
        {
            NpuBrand brand;
            return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_modality_npu_brand(_ptr, modality, out brand), "litert_lm_loaded_file_modality_npu_brand") ? brand : (NpuBrand?)null;
        }

        /// <summary>
        /// The system on chip the model's part for a modality was compiled for, or null if the file doesn't say.
        /// </summary>
        /// <param name="modality">The modality</param>
        /// <returns>The SoC name, or null</returns>
        public String GetSocName(Modality modality)
        {
            IntPtr name;
            return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_modality_soc_name(_ptr, modality, out name), "litert_lm_loaded_file_modality_soc_name") ? LiteRtLmInvoke.PtrToStringUtf8(name) : null;
        }

        /// <summary>
        /// The oldest LiteRT-LM runtime version that can run the file, or null if the file doesn't say
        /// </summary>
        public String MinRuntimeVersion
        {
            get
            {
                IntPtr version;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_min_runtime_version(_ptr, out version), "litert_lm_loaded_file_min_runtime_version") ? LiteRtLmInvoke.PtrToStringUtf8(version) : null;
            }
        }

        /// <summary>
        /// What kind of model the file holds
        /// </summary>
        public ModelType ModelType
        {
            get
            {
                ModelType type;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_model_type(_ptr, out type), "litert_lm_loaded_file_model_type") ? type : ModelType.Unknown;
            }
        }

        /// <summary>
        /// The size of an embedding model's embeddings, or null if it isn't an embedding model or doesn't say
        /// </summary>
        public int? EmbeddingDimension
        {
            get
            {
                int dimension;
                return LiteRtLmInvoke.CheckFound(LiteRtLmInvoke.litert_lm_loaded_file_embedding_dimension(_ptr, out dimension), "litert_lm_loaded_file_embedding_dimension") ? dimension : (int?)null;
            }
        }

        private delegate LiteRtLmStatusCode LengthsFunction(IntPtr loadedFile, int[] lengths, UIntPtr maxSize, out UIntPtr count);

        // The count first, then the lengths.
        private int[] ReadLengths(LengthsFunction function, String functionName)
        {
            UIntPtr count;
            if (!LiteRtLmInvoke.CheckFound(function(_ptr, null, UIntPtr.Zero, out count), functionName))
                return null;
            int[] lengths = new int[checked((int)count.ToUInt64())];
            if (lengths.Length > 0)
                LiteRtLmInvoke.CheckError(function(_ptr, lengths, new UIntPtr((uint)lengths.Length), out count), functionName);
            return lengths;
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
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_create(byte[] litertlmPath, out IntPtr outResult);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_loaded_file_delete(IntPtr loadedFile);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_has_speculative_decoding_support(
            IntPtr loadedFile, [MarshalAs(BoolMarshalType)] out bool supported);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_supports_thinking(
            IntPtr loadedFile, [MarshalAs(BoolMarshalType)] out bool supported);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_supports_function_calling(
            IntPtr loadedFile, [MarshalAs(BoolMarshalType)] out bool supported);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_sampler_type(IntPtr loadedFile, out SamplerType type);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_sampler_temperature(IntPtr loadedFile, out float temperature);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_sampler_top_k(IntPtr loadedFile, out int topK);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_sampler_top_p(IntPtr loadedFile, out float topP);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_supports_input_modality(
            IntPtr loadedFile, Modality modality, [MarshalAs(BoolMarshalType)] out bool supported);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_max_vision_token_budget(IntPtr loadedFile, out int budget);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_max_context_tokens(IntPtr loadedFile, out uint maxContextTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_is_dynamic_context(
            IntPtr loadedFile, [MarshalAs(BoolMarshalType)] out bool isDynamic);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_vision_signature_selection(
            IntPtr loadedFile, [Out] int[] lengths, UIntPtr maxSize, out UIntPtr count);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_modality_supported_backends(
            IntPtr loadedFile, Modality modality, [Out] BackendType[] backends, UIntPtr maxSize, out UIntPtr count);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_modality_npu_brand(IntPtr loadedFile, Modality modality, out NpuBrand npuBrand);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_modality_soc_name(IntPtr loadedFile, Modality modality, out IntPtr socName);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_min_runtime_version(IntPtr loadedFile, out IntPtr version);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_model_type(IntPtr loadedFile, out ModelType modelType);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_embedding_dimension(IntPtr loadedFile, out int dimension);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode litert_lm_loaded_file_embedding_signature_selection(
            IntPtr loadedFile, [Out] int[] lengths, UIntPtr maxSize, out UIntPtr count);
    }
}
