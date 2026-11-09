//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// P/Invoke entry points into libomniextern, Emgu's C API over LiteRT-LM's OmniEngine (omni/, which LiteRT-LM only
    /// has in C++): speech recognition, text to image and text to speech. It is built from litertextern/omniextern in LiteRT-LM's Bazel workspace
    /// and links against libLiteRt, like liblitert-lm. Its functions return a status (LiteRtLmStatusCode's values) and
    /// record a message for the calling thread, as LiteRT-LM's C API does; nothing is thrown across it.
    /// </summary>
    public static class OmniInvoke
    {
        /// <summary>
        /// The file name of the library: libomniextern.dylib (macOS, also loaded by Mac Catalyst), libomniextern.so
        /// (Linux / Android) or libomniextern.dll (Windows); omniextern.framework on iOS.
        /// </summary>
#if (__IOS__ && !__MACCATALYST__) && (!UNITY_EDITOR)
        public const string OmniLibrary = "@rpath/omniextern.framework/omniextern";
#elif UNITY_IPHONE && (!UNITY_EDITOR)
        public const string OmniLibrary = "__Internal";
#else
        public const string OmniLibrary = "libomniextern";
#endif

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        private static extern IntPtr omniextern_get_last_error_message();

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        private static extern void omniextern_clear_last_error();

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_engine_create(byte[] modelName, byte[] modelPath,
            byte[] tokenizerPath, byte[] backend, int numThreads, float overlapRatio, byte[] textMerger, out IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void omniextern_engine_delete(IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern int omniextern_engine_get_sample_rate(IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_session_create(IntPtr engine, out IntPtr session);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void omniextern_session_delete(IntPtr session);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_session_push_audio(IntPtr session, IntPtr samples, int count);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_session_end_audio(IntPtr session);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_session_process(IntPtr session, out SpeechToTextResult result,
            out IntPtr confirmed, out IntPtr unconfirmed);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_session_reset(IntPtr session);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void omniextern_free_string(IntPtr text);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_image_engine_create(byte[] modelName, byte[] modelPath,
            byte[] backend, int numThreads, int size, byte[] cacheDir, out IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void omniextern_image_engine_delete(IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_image_generate(IntPtr engine, byte[] prompt, int steps,
            ulong seed, out IntPtr rgb, out int width, out int height, out int channels);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void omniextern_free_buffer(IntPtr buffer);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_tts_engine_create(byte[] modelName, byte[] modelPath,
            byte[] backend, int numThreads, byte[] language, byte[] voice, byte[] cacheDir, out IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void omniextern_tts_engine_delete(IntPtr engine);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_tts_engine_get_voices(IntPtr engine, byte[] language, out IntPtr names);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_tts_engine_get_languages(IntPtr engine, out IntPtr names);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_tts_engine_get_default_voice(IntPtr engine, byte[] language, out IntPtr voice);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_tts_synthesize(IntPtr engine, byte[] text, out IntPtr pcm,
            out int count, out int sampleRate);

        [DllImport(OmniLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern LiteRtLmStatusCode omniextern_engine_settings_set_selected_signatures(IntPtr settings,
            IntPtr[] names, int count);

        private static bool? _isAvailable;

        /// <summary>
        /// True if libomniextern is in the app and loads (it isn't built for every platform yet: not for Windows).
        /// The first check loads it.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    try
                    {
                        omniextern_engine_get_sample_rate(IntPtr.Zero);
                        _isAvailable = true;
                    }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
                    {
                        _isAvailable = false;
                    }
                }
                return _isAvailable.Value;
            }
        }

        /// <summary>
        /// Throw a LiteRtLmException if an omniextern call didn't return Ok, with its status and the message it
        /// recorded; called right after the call, on the same thread (as LiteRtLmInvoke.CheckError).
        /// </summary>
        internal static void CheckError(LiteRtLmStatusCode status, String functionName)
        {
            if (status == LiteRtLmStatusCode.Ok)
                return;
            String message = LiteRtLmInvoke.PtrToStringUtf8(omniextern_get_last_error_message());
            omniextern_clear_last_error();
            throw new LiteRtLmException(status, functionName, message);
        }

        /// <summary>A string libomniextern returned, freed; null for a null pointer.</summary>
        internal static String TakeString(IntPtr text)
        {
            if (text == IntPtr.Zero)
                return null;
            try
            {
                return LiteRtLmInvoke.PtrToStringUtf8(text);
            }
            finally
            {
                omniextern_free_string(text);
            }
        }
    }

    /// <summary>
    /// LiteRT-LM's speech recognition (its OmniEngine) for one model, such as Parakeet-TDT-0.6B-v3, loaded from its
    /// local .litertlm. Sessions created from it turn audio into text.
    /// </summary>
    public class SpeechToTextEngine : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Load a speech recognition model.
        /// </summary>
        /// <param name="modelName">The model's name in LiteRT-LM's registry, e.g. "parakeet-tdt-0.6b-v3"</param>
        /// <param name="modelPath">The model file (.litertlm, which holds the tokenizer)</param>
        /// <param name="tokenizerPath">Ignored: kept for models that had a separate tokenizer before LiteRT-LM v0.19.0</param>
        /// <param name="backend">"cpu", "gpu" or "npu"</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="overlapRatio">How much consecutive windows (5 s for Parakeet) overlap, from 0 to 1; negative
        /// for LiteRT-LM's default (0.75)</param>
        /// <param name="textMerger">How the windows' texts are joined: "levenshtein" (aligns their words),
        /// "timestamp" (cuts at the words' times), or null for LiteRT-LM's default (timestamp)</param>
        public SpeechToTextEngine(String modelName, String modelPath, String tokenizerPath = null, String backend = "cpu",
            int numThreads = 0, float overlapRatio = 0.4f, String textMerger = null)
        {
            if (String.IsNullOrEmpty(modelName))
                throw new ArgumentNullException("modelName");
            if (String.IsNullOrEmpty(modelPath))
                throw new ArgumentNullException("modelPath");
            if (String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase))
                Emgu.LiteRT.Util.AcceleratorPlugins.Load();
            OmniInvoke.CheckError(OmniInvoke.omniextern_engine_create(LiteRtLmInvoke.ToUtf8(modelName), LiteRtLmInvoke.ToUtf8(modelPath),
                LiteRtLmInvoke.ToUtf8(tokenizerPath), LiteRtLmInvoke.ToUtf8(backend ?? "cpu"), numThreads, overlapRatio,
                LiteRtLmInvoke.ToUtf8(textMerger), out _ptr), "omniextern_engine_create");
            SampleRate = OmniInvoke.omniextern_engine_get_sample_rate(_ptr);
        }

        /// <summary>The sample rate sessions take, in Hz (16000; the session resamples for the model).</summary>
        public int SampleRate { get; private set; }

        /// <summary>Start recognizing new audio.</summary>
        public SpeechToTextSession CreateSession()
        {
            return new SpeechToTextSession(this);
        }

        /// <summary>Release the engine.</summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                OmniInvoke.omniextern_engine_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>What <see cref="SpeechToTextSession.Process"/> found.</summary>
    public enum SpeechToTextResult
    {
        /// <summary>A whole window hasn't been pushed yet: push more audio, or end it.</summary>
        NeedAudio = 0,

        /// <summary>A window was recognized: its confirmed text, and the text still subject to change.</summary>
        Text = 1,

        /// <summary>The audio ended: the rest of the text. Nothing more comes until the session is reset.</summary>
        End = 2,
    }

    /// <summary>
    /// Speech recognition of one stream of audio: push mono samples at the engine's sample rate, end the audio, and
    /// process until the end. The audio is recognized in overlapping windows; each window confirms some text, and the
    /// confirmed texts together are the transcript. PushAudio may be called from another thread than Process (e.g. a
    /// microphone's); otherwise use one thread at a time.
    /// </summary>
    public class SpeechToTextSession : Emgu.LiteRT.Util.UnmanagedObject
    {
        private readonly SpeechToTextEngine _engine;

        internal SpeechToTextSession(SpeechToTextEngine engine)
        {
            _engine = engine;
            OmniInvoke.CheckError(OmniInvoke.omniextern_session_create(engine.Ptr, out _ptr), "omniextern_session_create");
        }

        /// <summary>
        /// Add mono samples, in [-1, 1], at the engine's sample rate.
        /// </summary>
        /// <param name="samples">The samples</param>
        /// <param name="offset">The first sample to add</param>
        /// <param name="count">How many samples to add, or -1 for the rest</param>
        public void PushAudio(float[] samples, int offset = 0, int count = -1)
        {
            if (samples == null)
                throw new ArgumentNullException("samples");
            if (count < 0)
                count = samples.Length - offset;
            if (offset < 0 || count < 0 || offset + count > samples.Length)
                throw new ArgumentOutOfRangeException("count");
            if (count == 0)
                return;
            GCHandle handle = GCHandle.Alloc(samples, GCHandleType.Pinned);
            try
            {
                OmniInvoke.CheckError(
                    OmniInvoke.omniextern_session_push_audio(_ptr, handle.AddrOfPinnedObject() + offset * sizeof(float), count),
                    "omniextern_session_push_audio");
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Mark the end of the audio.</summary>
        public void EndAudio()
        {
            OmniInvoke.CheckError(OmniInvoke.omniextern_session_end_audio(_ptr), "omniextern_session_end_audio");
        }

        /// <summary>
        /// Recognize the next window of the audio pushed so far.
        /// </summary>
        /// <param name="confirmed">The text that window confirmed (it won't change), or the rest of the text at the end</param>
        /// <param name="unconfirmed">The text after it, which the next window may still change</param>
        /// <returns>Whether there was text, more audio is needed, or the audio ended</returns>
        public SpeechToTextResult Process(out String confirmed, out String unconfirmed)
        {
            SpeechToTextResult result;
            IntPtr confirmedPtr, unconfirmedPtr;
            LiteRtLmStatusCode status = OmniInvoke.omniextern_session_process(_ptr, out result, out confirmedPtr, out unconfirmedPtr);
            confirmed = OmniInvoke.TakeString(confirmedPtr) ?? String.Empty;
            unconfirmed = OmniInvoke.TakeString(unconfirmedPtr) ?? String.Empty;
            GC.KeepAlive(_engine);
            OmniInvoke.CheckError(status, "omniextern_session_process");
            return result;
        }

        /// <summary>Start again for new audio (audio pushed but not recognized yet is dropped).</summary>
        public void Reset()
        {
            OmniInvoke.CheckError(OmniInvoke.omniextern_session_reset(_ptr), "omniextern_session_reset");
        }

        /// <summary>Release the session.</summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                OmniInvoke.omniextern_session_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }
}
