//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// Repetition, presence and frequency penalties (LiteRtLmRepetitionPenaltyConfig), applied through
    /// ConversationOptionalArgs.
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object
    /// (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class RepetitionPenaltyConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private float _repetitionPenalty;
        private float _presencePenalty;
        private float _frequencyPenalty;
        private int _windowSize;

        /// <summary>
        /// Create a repetition penalty configuration.
        /// </summary>
        public RepetitionPenaltyConfig()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_repetition_penalty_config_create(), "litert_lm_repetition_penalty_config_create");
        }

        /// <summary>
        /// The repetition penalty
        /// </summary>
        public float RepetitionPenalty
        {
            get { return _repetitionPenalty; }
            set { LiteRtLmInvoke.litert_lm_repetition_penalty_config_set_repetition_penalty(_ptr, value); _repetitionPenalty = value; }
        }

        /// <summary>
        /// The presence penalty
        /// </summary>
        public float PresencePenalty
        {
            get { return _presencePenalty; }
            set { LiteRtLmInvoke.litert_lm_repetition_penalty_config_set_presence_penalty(_ptr, value); _presencePenalty = value; }
        }

        /// <summary>
        /// The frequency penalty
        /// </summary>
        public float FrequencyPenalty
        {
            get { return _frequencyPenalty; }
            set { LiteRtLmInvoke.litert_lm_repetition_penalty_config_set_frequency_penalty(_ptr, value); _frequencyPenalty = value; }
        }

        /// <summary>
        /// The number of most recent tokens the penalties look at
        /// </summary>
        public int WindowSize
        {
            get { return _windowSize; }
            set { LiteRtLmInvoke.litert_lm_repetition_penalty_config_set_window_size(_ptr, value); _windowSize = value; }
        }

        /// <summary>
        /// Release the unmanaged configuration
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_repetition_penalty_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Prevents repeating n-grams (LiteRtLmNoRepeatNgramConfig), applied through ConversationOptionalArgs.
    /// </summary>
    /// <remarks>The C API has no getters, so the properties return the values last set through this object
    /// (the type's default value before that, not LiteRT-LM's own default).</remarks>
    public class NoRepeatNgramConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private int _noRepeatNgramSize;
        private int _windowSize;

        /// <summary>
        /// Create a no-repeat n-gram configuration.
        /// </summary>
        public NoRepeatNgramConfig()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_no_repeat_ngram_config_create(), "litert_lm_no_repeat_ngram_config_create");
        }

        /// <summary>
        /// The size of the n-grams that may not repeat
        /// </summary>
        public int NoRepeatNgramSize
        {
            get { return _noRepeatNgramSize; }
            set { LiteRtLmInvoke.litert_lm_no_repeat_ngram_config_set_no_repeat_ngram_size(_ptr, value); _noRepeatNgramSize = value; }
        }

        /// <summary>
        /// The number of most recent tokens to check for repeats
        /// </summary>
        public int WindowSize
        {
            get { return _windowSize; }
            set { LiteRtLmInvoke.litert_lm_no_repeat_ngram_config_set_window_size(_ptr, value); _windowSize = value; }
        }

        /// <summary>
        /// Release the unmanaged configuration
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_no_repeat_ngram_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Tokens that may never be generated (LiteRtLmSuppressTokensConfig), applied through ConversationOptionalArgs.
    /// </summary>
    public class SuppressTokensConfig : Emgu.LiteRT.Util.UnmanagedObject
    {
        private int[] _suppressTokens;

        /// <summary>
        /// Create a suppress-tokens configuration.
        /// </summary>
        public SuppressTokensConfig()
        {
            _ptr = LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_suppress_tokens_config_create(), "litert_lm_suppress_tokens_config_create");
        }

        /// <summary>
        /// The ids of the tokens to suppress
        /// </summary>
        public int[] SuppressTokens
        {
            get { return _suppressTokens; }
            set
            {
                int[] tokens = value ?? new int[0];
                LiteRtLmInvoke.litert_lm_suppress_tokens_config_set_suppress_tokens(_ptr, tokens, new UIntPtr((uint)tokens.Length));
                _suppressTokens = value;
            }
        }

        /// <summary>
        /// Release the unmanaged configuration
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_suppress_tokens_config_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_repetition_penalty_config_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_repetition_penalty_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_repetition_penalty_config_set_repetition_penalty(IntPtr config, float repetitionPenalty);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_repetition_penalty_config_set_presence_penalty(IntPtr config, float presencePenalty);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_repetition_penalty_config_set_frequency_penalty(IntPtr config, float frequencyPenalty);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_repetition_penalty_config_set_window_size(IntPtr config, int windowSize);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_no_repeat_ngram_config_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_no_repeat_ngram_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_no_repeat_ngram_config_set_no_repeat_ngram_size(IntPtr config, int noRepeatNgramSize);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_no_repeat_ngram_config_set_window_size(IntPtr config, int windowSize);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_suppress_tokens_config_create();

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_suppress_tokens_config_delete(IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_suppress_tokens_config_set_suppress_tokens(IntPtr config, int[] suppressTokens, UIntPtr numTokens);
    }
}
