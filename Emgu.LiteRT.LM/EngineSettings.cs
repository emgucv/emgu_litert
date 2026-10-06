//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// The settings an Engine is created from (LiteRtLmEngineSettings): the model file, the backends and the
    /// runtime options.
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object
    /// (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class EngineSettings : Emgu.LiteRT.Util.UnmanagedObject
    {
        private int _maxNumTokens;
        private int _numThreads;
        private int _audioNumThreads;
        private bool _parallelFileSectionLoading;
        private int _maxNumImages;
        private int _maxVisionTokensPerImage;
        private String _cacheDir;
        private String _litertDispatchLibDir;
        private ActivationDataType _activationDataType;
        private int _prefillChunkSize;
        private bool _enableYnnpack;
        private bool _benchmarkEnabled;
        private int _numPrefillTokens;
        private int _numDecodeTokens;
        private bool _enableSpeculativeDecoding;
        private int _gpuDecodeStepsPerSync;
        private bool _gpuWaitForWeightUploads;
        private bool _useRingbuffersLocalAttention;
        private int _loraRank;
        private int[] _supportedLoraRanks;
        private int _audioLoraRank;
        private int[] _supportedAudioLoraRanks;
        private bool _gpuEnableMetalResidencySet;

        /// <summary>
        /// Create the engine settings for a model file.
        /// </summary>
        /// <param name="modelPath">The path of the .litertlm model file</param>
        /// <param name="backend">The main backend, e.g. "cpu", "gpu" or "npu"</param>
        /// <param name="visionBackend">The vision backend, or null if not used</param>
        /// <param name="audioBackend">The audio backend, or null if not used</param>
        public EngineSettings(String modelPath, String backend = "cpu", String visionBackend = null, String audioBackend = null)
        {
            if (modelPath == null)
                throw new ArgumentNullException("modelPath");
            ModelPath = modelPath;
            Backend = backend;
            VisionBackend = visionBackend;
            AudioBackend = audioBackend;
            _ptr = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_engine_settings_create(
                    LiteRtLmInvoke.ToUtf8(modelPath),
                    LiteRtLmInvoke.ToUtf8(backend),
                    LiteRtLmInvoke.ToUtf8(visionBackend),
                    LiteRtLmInvoke.ToUtf8(audioBackend)),
                "litert_lm_engine_settings_create");
        }

        /// <summary>
        /// The path of the model file
        /// </summary>
        public String ModelPath { get; }

        /// <summary>
        /// The main backend
        /// </summary>
        public String Backend { get; }

        /// <summary>
        /// The vision backend, or null if not used
        /// </summary>
        public String VisionBackend { get; }

        /// <summary>
        /// The audio backend, or null if not used
        /// </summary>
        public String AudioBackend { get; }

        /// <summary>
        /// The maximum number of tokens (the context size)
        /// </summary>
        public int MaxNumTokens
        {
            get { return _maxNumTokens; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_max_num_tokens(_ptr, value); _maxNumTokens = value; }
        }

        /// <summary>
        /// The number of CPU threads
        /// </summary>
        public int NumThreads
        {
            get { return _numThreads; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_num_threads(_ptr, value); _numThreads = value; }
        }

        /// <summary>
        /// The number of CPU threads of the audio executor
        /// </summary>
        public int AudioNumThreads
        {
            get { return _audioNumThreads; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_audio_num_threads(_ptr, value); _audioNumThreads = value; }
        }

        /// <summary>
        /// Whether to load the sections of the model file in parallel
        /// </summary>
        public bool ParallelFileSectionLoading
        {
            get { return _parallelFileSectionLoading; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_parallel_file_section_loading(_ptr, value); _parallelFileSectionLoading = value; }
        }

        /// <summary>
        /// The maximum number of images per prompt
        /// </summary>
        public int MaxNumImages
        {
            get { return _maxNumImages; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_max_num_images(_ptr, value); _maxNumImages = value; }
        }

        /// <summary>
        /// The maximum number of vision tokens per image
        /// </summary>
        public int MaxVisionTokensPerImage
        {
            get { return _maxVisionTokensPerImage; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_max_vision_tokens_per_image(_ptr, value); _maxVisionTokensPerImage = value; }
        }

        /// <summary>
        /// The folder where compiled model caches are stored
        /// </summary>
        public String CacheDir
        {
            get { return _cacheDir; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_cache_dir(_ptr, LiteRtLmInvoke.ToUtf8(value)); _cacheDir = value; }
        }

        /// <summary>
        /// The folder of the LiteRT dispatch (NPU) libraries
        /// </summary>
        public String LitertDispatchLibDir
        {
            get { return _litertDispatchLibDir; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_litert_dispatch_lib_dir(_ptr, LiteRtLmInvoke.ToUtf8(value)); _litertDispatchLibDir = value; }
        }

        /// <summary>
        /// The activation data type
        /// </summary>
        public ActivationDataType ActivationDataType
        {
            get { return _activationDataType; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_activation_data_type(_ptr, value); _activationDataType = value; }
        }

        /// <summary>
        /// The number of tokens prefilled in one chunk
        /// </summary>
        public int PrefillChunkSize
        {
            get { return _prefillChunkSize; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_prefill_chunk_size(_ptr, value); _prefillChunkSize = value; }
        }

        /// <summary>
        /// Whether to use the YNNPACK CPU backend
        /// </summary>
        public bool EnableYnnpack
        {
            get { return _enableYnnpack; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_enable_ynnpack(_ptr, value); _enableYnnpack = value; }
        }

        /// <summary>
        /// Whether benchmarking is enabled (see Session.GetBenchmarkInfo)
        /// </summary>
        public bool BenchmarkEnabled
        {
            get { return _benchmarkEnabled; }
        }

        /// <summary>
        /// Enable benchmarking, so Session.GetBenchmarkInfo and Conversation.GetBenchmarkInfo return timings.
        /// </summary>
        public void EnableBenchmark()
        {
            LiteRtLmInvoke.litert_lm_engine_settings_enable_benchmark(_ptr);
            _benchmarkEnabled = true;
        }

        /// <summary>
        /// The number of prefill tokens used when benchmarking
        /// </summary>
        public int NumPrefillTokens
        {
            get { return _numPrefillTokens; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_num_prefill_tokens(_ptr, value); _numPrefillTokens = value; }
        }

        /// <summary>
        /// The number of decode tokens used when benchmarking
        /// </summary>
        public int NumDecodeTokens
        {
            get { return _numDecodeTokens; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_num_decode_tokens(_ptr, value); _numDecodeTokens = value; }
        }

        /// <summary>
        /// Whether to use speculative decoding, if the model supports it
        /// </summary>
        public bool EnableSpeculativeDecoding
        {
            get { return _enableSpeculativeDecoding; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_enable_speculative_decoding(_ptr, value); _enableSpeculativeDecoding = value; }
        }

        /// <summary>
        /// The number of GPU decode steps between synchronizations
        /// </summary>
        public int GpuDecodeStepsPerSync
        {
            get { return _gpuDecodeStepsPerSync; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_gpu_decode_steps_per_sync(_ptr, value); _gpuDecodeStepsPerSync = value; }
        }

        /// <summary>
        /// Whether to wait for the GPU weight uploads to finish when creating the engine
        /// </summary>
        public bool GpuWaitForWeightUploads
        {
            get { return _gpuWaitForWeightUploads; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_gpu_wait_for_weight_uploads(_ptr, value); _gpuWaitForWeightUploads = value; }
        }

        /// <summary>
        /// Whether to use ring buffers for local attention
        /// </summary>
        public bool UseRingbuffersLocalAttention
        {
            get { return _useRingbuffersLocalAttention; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_use_ringbuffers_local_attention(_ptr, value); _useRingbuffersLocalAttention = value; }
        }

        /// <summary>
        /// The LoRA rank
        /// </summary>
        public int LoraRank
        {
            get { return _loraRank; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_lora_rank(_ptr, value); _loraRank = value; }
        }

        /// <summary>
        /// The supported LoRA ranks
        /// </summary>
        public int[] SupportedLoraRanks
        {
            get { return _supportedLoraRanks; }
            set
            {
                int[] ranks = value ?? new int[0];
                LiteRtLmInvoke.CheckStatus(
                    LiteRtLmInvoke.litert_lm_engine_settings_set_supported_lora_ranks(_ptr, ranks, new UIntPtr((uint)ranks.Length)),
                    "litert_lm_engine_settings_set_supported_lora_ranks");
                _supportedLoraRanks = value;
            }
        }

        /// <summary>
        /// The audio LoRA rank
        /// </summary>
        public int AudioLoraRank
        {
            get { return _audioLoraRank; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_audio_lora_rank(_ptr, value); _audioLoraRank = value; }
        }

        /// <summary>
        /// The supported audio LoRA ranks
        /// </summary>
        public int[] SupportedAudioLoraRanks
        {
            get { return _supportedAudioLoraRanks; }
            set
            {
                int[] ranks = value ?? new int[0];
                LiteRtLmInvoke.CheckStatus(
                    LiteRtLmInvoke.litert_lm_engine_settings_set_supported_audio_lora_ranks(_ptr, ranks, new UIntPtr((uint)ranks.Length)),
                    "litert_lm_engine_settings_set_supported_audio_lora_ranks");
                _supportedAudioLoraRanks = value;
            }
        }

        /// <summary>
        /// Whether to use a Metal residency set for the GPU weights (Apple platforms)
        /// </summary>
        public bool GpuEnableMetalResidencySet
        {
            get { return _gpuEnableMetalResidencySet; }
            set { LiteRtLmInvoke.litert_lm_engine_settings_set_gpu_enable_metal_residency_set(_ptr, value); _gpuEnableMetalResidencySet = value; }
        }

        /// <summary>
        /// Release the unmanaged engine settings
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_engine_settings_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_settings_create(byte[] modelPath, byte[] backend, byte[] visionBackend, byte[] audioBackend);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_delete(IntPtr settings);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_max_num_tokens(IntPtr settings, int maxNumTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_num_threads(IntPtr settings, int numThreads);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_audio_num_threads(IntPtr settings, int numThreads);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_parallel_file_section_loading(
            IntPtr settings,
            [MarshalAs(BoolMarshalType)] bool parallelFileSectionLoading);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_max_num_images(IntPtr settings, int maxNumImages);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_max_vision_tokens_per_image(IntPtr settings, int maxVisionTokensPerImage);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_cache_dir(IntPtr settings, byte[] cacheDir);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_litert_dispatch_lib_dir(IntPtr settings, byte[] libDir);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_activation_data_type(IntPtr settings, ActivationDataType activationDataType);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_prefill_chunk_size(IntPtr settings, int prefillChunkSize);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_enable_ynnpack(
            IntPtr settings,
            [MarshalAs(BoolMarshalType)] bool enableYnnpack);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_enable_benchmark(IntPtr settings);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_num_prefill_tokens(IntPtr settings, int numPrefillTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_num_decode_tokens(IntPtr settings, int numDecodeTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_enable_speculative_decoding(
            IntPtr settings,
            [MarshalAs(BoolMarshalType)] bool enableSpeculativeDecoding);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_gpu_decode_steps_per_sync(IntPtr settings, int numDecodeStepsPerSync);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_gpu_wait_for_weight_uploads(
            IntPtr settings,
            [MarshalAs(BoolMarshalType)] bool waitForWeightUploads);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_use_ringbuffers_local_attention(
            IntPtr settings,
            [MarshalAs(BoolMarshalType)] bool useRingbuffersLocalAttention);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_lora_rank(IntPtr settings, int loraRank);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_engine_settings_set_supported_lora_ranks(IntPtr settings, int[] loraRanks, UIntPtr numRanks);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_audio_lora_rank(IntPtr settings, int loraRank);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_engine_settings_set_supported_audio_lora_ranks(IntPtr settings, int[] loraRanks, UIntPtr numRanks);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_engine_settings_set_gpu_enable_metal_residency_set(
            IntPtr settings,
            [MarshalAs(BoolMarshalType)] bool enableMetalResidencySet);
    }
}
