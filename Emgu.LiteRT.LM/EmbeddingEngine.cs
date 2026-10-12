//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// What to do with an input longer than the model's longest input signature (LiteRtLmInputOverflowStrategy)
    /// </summary>
    public enum InputOverflowStrategy
    {
        /// <summary>
        /// Split the input into chunks, embed each and return their mean
        /// </summary>
        ChunkAndAverage = 0,
        /// <summary>
        /// Cut the input to the longest signature's length
        /// </summary>
        Truncate = 1,
        /// <summary>
        /// Fail
        /// </summary>
        Error = 2
    }

    /// <summary>
    /// Settings of an embedding engine (LiteRtLmEmbeddingEngineSettings): the model file and its backends.
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object.</remarks>
    public class EmbeddingEngineSettings : Emgu.LiteRT.Util.UnmanagedObject
    {
        private int _numThreads;
        private String _cacheDir;
        private int _maxInputLength;
        private int _minInputLength;
        private ActivationDataType _activationDataType;

        /// <summary>
        /// Create the settings for an embedding model file.
        /// </summary>
        /// <param name="modelPath">The path of the .litertlm embedding model, e.g. EmbeddingGemma 2</param>
        /// <param name="backend">The main backend, e.g. "cpu" or "gpu"</param>
        /// <param name="visionBackend">The vision backend, for models that embed images; or null</param>
        /// <param name="audioBackend">The audio backend, for models that embed audio; or null</param>
        public EmbeddingEngineSettings(String modelPath, String backend = "cpu", String visionBackend = null, String audioBackend = null)
        {
            if (modelPath == null)
                throw new ArgumentNullException("modelPath");
            ModelPath = modelPath;
            Backend = backend;
            VisionBackend = visionBackend;
            AudioBackend = audioBackend;
            _ptr = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_embedding_engine_settings_create(
                    LiteRtLmInvoke.ToUtf8(modelPath),
                    LiteRtLmInvoke.ToUtf8(backend),
                    LiteRtLmInvoke.ToUtf8(visionBackend),
                    LiteRtLmInvoke.ToUtf8(audioBackend)),
                "litert_lm_embedding_engine_settings_create");
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
        /// The vision backend, or null
        /// </summary>
        public String VisionBackend { get; }

        /// <summary>
        /// The audio backend, or null
        /// </summary>
        public String AudioBackend { get; }

        /// <summary>
        /// The number of CPU threads
        /// </summary>
        public int NumThreads
        {
            get { return _numThreads; }
            set { LiteRtLmInvoke.litert_lm_embedding_engine_settings_set_num_threads(_ptr, value); _numThreads = value; }
        }

        /// <summary>
        /// The folder for compiled-model caches
        /// </summary>
        public String CacheDir
        {
            get { return _cacheDir; }
            set { LiteRtLmInvoke.litert_lm_embedding_engine_settings_set_cache_dir(_ptr, LiteRtLmInvoke.ToUtf8(value)); _cacheDir = value; }
        }

        /// <summary>
        /// The longest input signature to use, in tokens
        /// </summary>
        public int MaxInputLength
        {
            get { return _maxInputLength; }
            set { LiteRtLmInvoke.litert_lm_embedding_engine_settings_set_max_input_length(_ptr, value); _maxInputLength = value; }
        }

        /// <summary>
        /// The shortest input signature to use, in tokens
        /// </summary>
        public int MinInputLength
        {
            get { return _minInputLength; }
            set { LiteRtLmInvoke.litert_lm_embedding_engine_settings_set_min_input_length(_ptr, value); _minInputLength = value; }
        }

        /// <summary>
        /// The activation data type
        /// </summary>
        public ActivationDataType ActivationDataType
        {
            get { return _activationDataType; }
            set { LiteRtLmInvoke.litert_lm_embedding_engine_settings_set_activation_data_type(_ptr, value); _activationDataType = value; }
        }

        /// <summary>
        /// Release the unmanaged settings
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_embedding_engine_settings_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Options of an embedding computation (LiteRtLmEmbeddingOptions)
    /// </summary>
    public class EmbeddingOptions : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Create the default options
        /// </summary>
        public EmbeddingOptions()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_embedding_options_create(), "litert_lm_embedding_options_create");
        }

        /// <summary>
        /// Whether the embedding is scaled to unit length, so the dot product of two embeddings is their cosine
        /// similarity
        /// </summary>
        public bool Normalize
        {
            get { return LiteRtLmInvoke.litert_lm_embedding_options_get_normalize(_ptr); }
            set { LiteRtLmInvoke.litert_lm_embedding_options_set_normalize(_ptr, value); }
        }

        /// <summary>
        /// Whether the tokenizer's special tokens (e.g. beginning of sequence) are added to text inputs
        /// </summary>
        public bool InsertSpecialTokens
        {
            get { return LiteRtLmInvoke.litert_lm_embedding_options_get_insert_special_tokens(_ptr); }
            set { LiteRtLmInvoke.litert_lm_embedding_options_set_insert_special_tokens(_ptr, value); }
        }

        /// <summary>
        /// What to do with an input longer than the model's longest input signature
        /// </summary>
        public InputOverflowStrategy InputOverflowStrategy
        {
            get { return LiteRtLmInvoke.litert_lm_embedding_options_get_input_overflow_strategy(_ptr); }
            set { LiteRtLmInvoke.litert_lm_embedding_options_set_input_overflow_strategy(_ptr, value); }
        }

        /// <summary>
        /// The number of values in the embedding, for models trained to allow shorter embeddings (Matryoshka, e.g.
        /// EmbeddingGemma: 768, 512, 256 or 128); 0 for the model's full size
        /// </summary>
        public int OutputSize
        {
            get { return LiteRtLmInvoke.litert_lm_embedding_options_get_output_size(_ptr); }
            set { LiteRtLmInvoke.litert_lm_embedding_options_set_output_size(_ptr, value); }
        }

        /// <summary>
        /// Release the unmanaged options
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_embedding_options_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// A LiteRT-LM embedding engine (LiteRtLmEmbeddingEngine): a loaded embedding model, which turns text (and, for
    /// multimodal models, images or audio) into a vector of numbers, so that inputs with similar meaning get similar
    /// vectors - for semantic search, clustering or classification.
    /// </summary>
    public class EmbeddingEngine : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Create an embedding engine, loading the model described by the settings. The settings are copied, so they
        /// can be disposed afterwards.
        /// </summary>
        /// <param name="settings">The settings</param>
        public EmbeddingEngine(EmbeddingEngineSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (String.Equals(settings.Backend, "gpu", StringComparison.OrdinalIgnoreCase)
                || String.Equals(settings.VisionBackend, "gpu", StringComparison.OrdinalIgnoreCase)
                || String.Equals(settings.AudioBackend, "gpu", StringComparison.OrdinalIgnoreCase))
                Emgu.LiteRT.Util.AcceleratorPlugins.Load();
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_embedding_engine_create(settings), "litert_lm_embedding_engine_create");
        }

        /// <summary>
        /// Compute the embedding of a text.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="options">The options, or null for the defaults</param>
        /// <returns>The embedding</returns>
        public float[] ComputeEmbedding(String text, EmbeddingOptions options = null)
        {
            using (InputData input = new InputData(text ?? String.Empty))
                return ComputeEmbedding(new[] { input }, options);
        }

        /// <summary>
        /// Compute the embedding of one request made of one or more inputs (text, image, audio).
        /// </summary>
        /// <param name="inputs">The inputs of the request</param>
        /// <param name="options">The options, or null for the defaults</param>
        /// <returns>The embedding</returns>
        public float[] ComputeEmbedding(InputData[] inputs, EmbeddingOptions options = null)
        {
            if (inputs == null || inputs.Length == 0)
                throw new ArgumentException("No input", "inputs");
            IntPtr[] pointers = new IntPtr[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
                pointers[i] = inputs[i].Ptr;
            IntPtr response = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_embedding_engine_compute_embedding(
                    _ptr, pointers, new UIntPtr((uint)pointers.Length), options == null ? IntPtr.Zero : options.Ptr),
                "litert_lm_embedding_engine_compute_embedding");
            try
            {
                return ReadResponse(response);
            }
            finally
            {
                LiteRtLmInvoke.litert_lm_embedding_response_delete(response);
                GC.KeepAlive(inputs);
                GC.KeepAlive(options);
            }
        }

        /// <summary>
        /// Compute the embeddings of several texts in one batch.
        /// </summary>
        /// <param name="texts">The texts</param>
        /// <param name="options">The options, or null for the defaults</param>
        /// <returns>The embeddings, in the order of the texts</returns>
        public float[][] ComputeEmbeddings(String[] texts, EmbeddingOptions options = null)
        {
            if (texts == null || texts.Length == 0)
                return new float[0][];
            InputData[] inputs = new InputData[texts.Length];
            // One input per request: an array of one LiteRtLmInputData* each, and the array of those arrays.
            IntPtr[] requests = new IntPtr[texts.Length];
            IntPtr batch = IntPtr.Zero;
            try
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    inputs[i] = new InputData(texts[i] ?? String.Empty);
                    requests[i] = Marshal.AllocHGlobal(IntPtr.Size);
                    Marshal.WriteIntPtr(requests[i], inputs[i].Ptr);
                }
                batch = Marshal.AllocHGlobal(IntPtr.Size * requests.Length);
                for (int i = 0; i < requests.Length; i++)
                    Marshal.WriteIntPtr(batch, i * IntPtr.Size, requests[i]);
                UIntPtr[] counts = new UIntPtr[texts.Length];
                for (int i = 0; i < counts.Length; i++)
                    counts[i] = new UIntPtr(1);

                IntPtr responses = LiteRtLmInvoke.CheckPtr(
                    LiteRtLmInvoke.litert_lm_embedding_engine_compute_embedding_batch(
                        _ptr, batch, counts, new UIntPtr((uint)texts.Length), options == null ? IntPtr.Zero : options.Ptr),
                    "litert_lm_embedding_engine_compute_embedding_batch");
                try
                {
                    int count = (int)LiteRtLmInvoke.litert_lm_embedding_responses_get_size(responses).ToUInt32();
                    float[][] embeddings = new float[count][];
                    for (int i = 0; i < count; i++)
                        embeddings[i] = ReadResponse(LiteRtLmInvoke.litert_lm_embedding_responses_get_at(responses, new UIntPtr((uint)i)));
                    return embeddings;
                }
                finally
                {
                    LiteRtLmInvoke.litert_lm_embedding_responses_delete(responses);
                }
            }
            finally
            {
                if (batch != IntPtr.Zero)
                    Marshal.FreeHGlobal(batch);
                foreach (IntPtr request in requests)
                    if (request != IntPtr.Zero)
                        Marshal.FreeHGlobal(request);
                foreach (InputData input in inputs)
                    if (input != null)
                        input.Dispose();
                GC.KeepAlive(options);
            }
        }

        private static float[] ReadResponse(IntPtr response)
        {
            int size = (int)LiteRtLmInvoke.litert_lm_embedding_response_get_size(response).ToUInt32();
            float[] values = new float[size];
            if (size > 0)
                Marshal.Copy(LiteRtLmInvoke.litert_lm_embedding_response_get_values(response), values, 0, size);
            return values;
        }

        /// <summary>
        /// The cosine similarity of two embeddings: 1 for the same direction, 0 for unrelated. For normalized
        /// embeddings it is their dot product.
        /// </summary>
        /// <param name="a">An embedding</param>
        /// <param name="b">Another embedding of the same size</param>
        /// <returns>The cosine similarity</returns>
        public static float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                throw new ArgumentException("The embeddings must have the same size");
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                na += a[i] * a[i];
                nb += b[i] * b[i];
            }
            return na == 0 || nb == 0 ? 0 : (float)(dot / Math.Sqrt(na * nb));
        }

        /// <summary>
        /// Release the engine
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_embedding_engine_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_engine_settings_create(byte[] modelPath, byte[] backend, byte[] visionBackend, byte[] audioBackend);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_settings_delete(IntPtr settings);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_settings_set_num_threads(IntPtr settings, int numThreads);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_settings_set_cache_dir(IntPtr settings, byte[] cacheDir);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_settings_set_max_input_length(IntPtr settings, int maxInputLength);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_settings_set_min_input_length(IntPtr settings, int minInputLength);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_settings_set_activation_data_type(IntPtr settings, ActivationDataType activationDataType);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_options_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_options_delete(IntPtr options);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_options_set_normalize(IntPtr options, [MarshalAs(BoolMarshalType)] bool normalize);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_embedding_options_get_normalize(IntPtr options);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_options_set_insert_special_tokens(IntPtr options, [MarshalAs(BoolMarshalType)] bool insertSpecialTokens);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        [return: MarshalAs(BoolMarshalType)]
        internal static extern bool litert_lm_embedding_options_get_insert_special_tokens(IntPtr options);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_options_set_input_overflow_strategy(IntPtr options, InputOverflowStrategy strategy);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern InputOverflowStrategy litert_lm_embedding_options_get_input_overflow_strategy(IntPtr options);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_options_set_output_size(IntPtr options, int outputSize);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_embedding_options_get_output_size(IntPtr options);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_response_delete(IntPtr response);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern UIntPtr litert_lm_embedding_response_get_size(IntPtr response);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_response_get_values(IntPtr response);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_responses_delete(IntPtr responses);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern UIntPtr litert_lm_embedding_responses_get_size(IntPtr responses);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_responses_get_at(IntPtr responses, UIntPtr index);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_engine_create(IntPtr settings);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_embedding_engine_delete(IntPtr engine);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_engine_compute_embedding(IntPtr engine, IntPtr[] inputs, UIntPtr numInputs, IntPtr options);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_embedding_engine_compute_embedding_batch(IntPtr engine, IntPtr inputsBatch, UIntPtr[] numInputsPerBatch, UIntPtr batchSize, IntPtr options);
    }
}
