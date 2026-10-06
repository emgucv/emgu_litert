//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// A LiteRT-LM session (LiteRtLmSession): prompt-level generation on an Engine, keeping its own context across
    /// calls.
    /// </summary>
    /// <remarks>Wait for (or cancel) a streaming call to complete before disposing the session.</remarks>
    public class Session : Emgu.LiteRT.Util.UnmanagedObject
    {
        // Keeps the engine alive while this session exists.
        private readonly Engine _engine;

        internal Session(Engine engine, SessionConfig config)
        {
            _engine = engine;
            if (config == null)
            {
                using (SessionConfig defaultConfig = new SessionConfig())
                    _ptr = LiteRtLmInvoke.litert_lm_engine_create_session(engine, defaultConfig);
            }
            else
            {
                _ptr = LiteRtLmInvoke.litert_lm_engine_create_session(engine, config);
            }
            LiteRtLmInvoke.CheckPtr(_ptr, "litert_lm_engine_create_session");
        }

        /// <summary>
        /// The engine this session was created from
        /// </summary>
        public Engine Engine
        {
            get { return _engine; }
        }

        /// <summary>
        /// Generate a response to the inputs, blocking until generation finishes.
        /// </summary>
        /// <param name="inputs">The prompt inputs</param>
        /// <returns>The responses</returns>
        public Responses GenerateContent(params InputData[] inputs)
        {
            IntPtr[] ptrs = InputData.ToPtrArray(inputs);
            IntPtr responses = LiteRtLmInvoke.CheckPtr(
                LiteRtLmInvoke.litert_lm_session_generate_content(_ptr, ptrs, new UIntPtr((uint)ptrs.Length)),
                "litert_lm_session_generate_content");
            GC.KeepAlive(inputs);
            return Responses.FromNative(responses);
        }

        /// <summary>
        /// Generate a response to a text prompt, blocking until generation finishes.
        /// </summary>
        /// <param name="prompt">The prompt</param>
        /// <returns>The text of the first response candidate</returns>
        public String GenerateContent(String prompt)
        {
            using (InputData input = new InputData(prompt))
                return GenerateContent(input).Text;
        }

        /// <summary>
        /// Generate a response to the inputs, streaming the text as it is generated. The inputs are copied, so they
        /// can be disposed once this method returns.
        /// </summary>
        /// <param name="inputs">The prompt inputs</param>
        /// <param name="onChunk">Called with each piece of generated text, on a LiteRT-LM background thread. If it
        /// throws, generation is cancelled and the task faults with that exception. May be null.</param>
        /// <param name="cancellationToken">Cancels the generation; the task is then cancelled (see Cancel)</param>
        /// <returns>A task that completes with the whole generated text</returns>
        public async Task<String> GenerateContentStreamAsync(
            InputData[] inputs,
            Action<String> onChunk = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            IntPtr[] ptrs = InputData.ToPtrArray(inputs);
            Task<String[]> task = StreamOperation.Start(
                this,
                (callback, callbackData) =>
                    LiteRtLmInvoke.litert_lm_session_generate_content_stream(
                        _ptr, ptrs, new UIntPtr((uint)ptrs.Length), callback, callbackData),
                "litert_lm_session_generate_content_stream",
                onChunk,
                Cancel,
                cancellationToken);
            GC.KeepAlive(inputs);
            return String.Concat(await task.ConfigureAwait(false));
        }

        /// <summary>
        /// Generate a response to a text prompt, streaming the text as it is generated.
        /// </summary>
        /// <param name="prompt">The prompt</param>
        /// <param name="onChunk">Called with each piece of generated text, on a LiteRT-LM background thread. May be
        /// null.</param>
        /// <param name="cancellationToken">Cancels the generation; the task is then cancelled (see Cancel)</param>
        /// <returns>A task that completes with the whole generated text</returns>
        public Task<String> GenerateContentStreamAsync(
            String prompt,
            Action<String> onChunk = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            using (InputData input = new InputData(prompt))
                return GenerateContentStreamAsync(new InputData[] { input }, onChunk, cancellationToken);
        }

        /// <summary>
        /// Add inputs to the session's context without generating (the first half of GenerateContent). Follow with
        /// RunDecode or RunDecodeAsync.
        /// </summary>
        /// <param name="inputs">The prompt inputs</param>
        public void RunPrefill(params InputData[] inputs)
        {
            IntPtr[] ptrs = InputData.ToPtrArray(inputs);
            LiteRtLmInvoke.CheckStatus(
                LiteRtLmInvoke.litert_lm_session_run_prefill(_ptr, ptrs, new UIntPtr((uint)ptrs.Length)),
                "litert_lm_session_run_prefill");
            GC.KeepAlive(inputs);
        }

        /// <summary>
        /// Generate a response to the inputs added by RunPrefill, blocking until generation finishes.
        /// </summary>
        /// <returns>The responses</returns>
        public Responses RunDecode()
        {
            return Responses.FromNative(
                LiteRtLmInvoke.CheckPtr(LiteRtLmInvoke.litert_lm_session_run_decode(_ptr), "litert_lm_session_run_decode"));
        }

        /// <summary>
        /// Generate a response to the inputs added by RunPrefill, streaming the text as it is generated.
        /// </summary>
        /// <param name="onChunk">Called with each piece of generated text, on a LiteRT-LM background thread. May be
        /// null.</param>
        /// <param name="cancellationToken">Cancels the generation; the task is then cancelled (see Cancel)</param>
        /// <returns>A task that completes with the whole generated text</returns>
        public async Task<String> RunDecodeAsync(
            Action<String> onChunk = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            String[] chunks = await StreamOperation.Start(
                this,
                (callback, callbackData) => LiteRtLmInvoke.litert_lm_session_run_decode_async(_ptr, callback, callbackData),
                "litert_lm_session_run_decode_async",
                onChunk,
                Cancel,
                cancellationToken).ConfigureAwait(false);
            return String.Concat(chunks);
        }

        /// <summary>
        /// Score how likely each target text is to follow the session's current context.
        /// </summary>
        /// <param name="targetTexts">The texts to score</param>
        /// <param name="storeTokenLengths">Whether to also return each text's number of tokens</param>
        /// <returns>One candidate per target text, with its score</returns>
        public Responses RunTextScoring(String[] targetTexts, bool storeTokenLengths = false)
        {
            if (targetTexts == null || targetTexts.Length == 0)
                throw new ArgumentException("At least one target text is required", "targetTexts");
            IntPtr[] texts = new IntPtr[targetTexts.Length];
            try
            {
                for (int i = 0; i < targetTexts.Length; i++)
                {
                    byte[] bytes = LiteRtLmInvoke.ToUtf8(targetTexts[i] ?? String.Empty);
                    texts[i] = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, texts[i], bytes.Length);
                }
                return Responses.FromNative(LiteRtLmInvoke.CheckPtr(
                    LiteRtLmInvoke.litert_lm_session_run_text_scoring(_ptr, texts, new UIntPtr((uint)texts.Length), storeTokenLengths),
                    "litert_lm_session_run_text_scoring"));
            }
            finally
            {
                foreach (IntPtr text in texts)
                    if (text != IntPtr.Zero)
                        Marshal.FreeHGlobal(text);
            }
        }

        /// <summary>
        /// Save the session's current state under a label, to return to with RewindToCheckpoint.
        /// </summary>
        /// <param name="label">The checkpoint label</param>
        public void SaveCheckpoint(String label)
        {
            LiteRtLmInvoke.CheckStatus(
                LiteRtLmInvoke.litert_lm_session_save_checkpoint(_ptr, LiteRtLmInvoke.ToUtf8(label)),
                "litert_lm_session_save_checkpoint");
        }

        /// <summary>
        /// Return the session to a state saved with SaveCheckpoint.
        /// </summary>
        /// <param name="label">The checkpoint label</param>
        public void RewindToCheckpoint(String label)
        {
            LiteRtLmInvoke.CheckStatus(
                LiteRtLmInvoke.litert_lm_session_rewind_to_checkpoint(_ptr, LiteRtLmInvoke.ToUtf8(label)),
                "litert_lm_session_rewind_to_checkpoint");
        }

        /// <summary>
        /// Return the session to an earlier step.
        /// </summary>
        /// <param name="step">The step to return to</param>
        public void RewindToStep(int step)
        {
            LiteRtLmInvoke.CheckStatus(
                LiteRtLmInvoke.litert_lm_session_rewind_to_step(_ptr, step),
                "litert_lm_session_rewind_to_step");
        }

        /// <summary>
        /// Cancel the generation in progress. LiteRT-LM doesn't support reusing a session after cancelling: create a new
        /// one for further generation.
        /// </summary>
        public void Cancel()
        {
            if (_ptr != IntPtr.Zero)
                LiteRtLmInvoke.litert_lm_session_cancel_process(_ptr);
        }

        /// <summary>
        /// Get the benchmark timings of the session. Requires EngineSettings.EnableBenchmark.
        /// </summary>
        /// <returns>The benchmark timings, or null if not available</returns>
        public BenchmarkInfo GetBenchmarkInfo()
        {
            IntPtr info = LiteRtLmInvoke.litert_lm_session_get_benchmark_info(_ptr);
            return info == IntPtr.Zero ? null : BenchmarkInfo.FromNative(info);
        }

        /// <summary>
        /// Release the unmanaged session
        /// </summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                LiteRtLmInvoke.litert_lm_session_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    public static partial class LiteRtLmInvoke
    {
        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_engine_create_session(IntPtr engine, IntPtr config);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_delete(IntPtr session);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern void litert_lm_session_cancel_process(IntPtr session);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_save_checkpoint(IntPtr session, byte[] label);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_rewind_to_checkpoint(IntPtr session, byte[] label);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_rewind_to_step(IntPtr session, int step);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_run_prefill(IntPtr session, IntPtr[] inputs, UIntPtr numInputs);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_session_run_decode(IntPtr session);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_session_run_text_scoring(
            IntPtr session,
            IntPtr[] targetText,
            UIntPtr numTargets,
            [MarshalAs(BoolMarshalType)] bool storeTokenLengths);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_session_generate_content(IntPtr session, IntPtr[] inputs, UIntPtr numInputs);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern IntPtr litert_lm_session_get_benchmark_info(IntPtr session);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_run_decode_async(
            IntPtr session,
            LiteRtLmStreamCallback callback,
            IntPtr callbackData);

        [DllImport(LiteRtLmLibrary, CallingConvention = LiteRtLmCallingConvention)]
        internal static extern int litert_lm_session_generate_content_stream(
            IntPtr session,
            IntPtr[] inputs,
            UIntPtr numInputs,
            LiteRtLmStreamCallback callback,
            IntPtr callbackData);
    }
}
