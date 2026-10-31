//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// P/Invoke entry points into libasrextern, Emgu's C API over LiteRT-LM's speech recognition engine (omni/asr,
    /// which LiteRT-LM only has in C++). It is built from litertextern/asrextern in LiteRT-LM's Bazel workspace and
    /// links against libLiteRt, like liblitert-lm.
    /// </summary>
    public static class AsrInvoke
    {
        /// <summary>
        /// The file name of the speech recognition library: libasrextern.dylib (macOS, also loaded by Mac Catalyst),
        /// libasrextern.so (Linux / Android) or libasrextern.dll (Windows); asrextern.framework on iOS.
        /// </summary>
#if (__IOS__ && !__MACCATALYST__) && (!UNITY_EDITOR)
        public const string AsrLibrary = "@rpath/asrextern.framework/asrextern";
#elif UNITY_IPHONE && (!UNITY_EDITOR)
        public const string AsrLibrary = "__Internal";
#else
        public const string AsrLibrary = "libasrextern";
#endif

        internal const int Error = -1;
        internal const int NeedAudio = 0;
        internal const int Text = 1;
        internal const int End = 2;

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern IntPtr asrextern_engine_create(byte[] modelName, byte[] modelPath, byte[] tokenizerPath,
            byte[] backend, int numThreads, float overlapRatio, byte[] textMerger, out IntPtr error);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void asrextern_engine_delete(IntPtr engine);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern int asrextern_engine_get_sample_rate(IntPtr engine);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern IntPtr asrextern_session_create(IntPtr engine, out IntPtr error);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void asrextern_session_delete(IntPtr session);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void asrextern_session_push_audio(IntPtr session, IntPtr samples, int count);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void asrextern_session_end_audio(IntPtr session);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern int asrextern_session_process(IntPtr session, out IntPtr confirmed, out IntPtr unconfirmed, out IntPtr error);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void asrextern_session_reset(IntPtr session);

        [DllImport(AsrLibrary, CallingConvention = LiteRtLmInvoke.LiteRtLmCallingConvention)]
        internal static extern void asrextern_free_string(IntPtr text);

        private static bool? _isAvailable;

        /// <summary>
        /// True if libasrextern is in the app and loads (it isn't built for every platform yet: not for Windows, nor
        /// Android's x86_64 emulator). The first check loads it.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable == null)
                {
                    try
                    {
                        asrextern_engine_get_sample_rate(IntPtr.Zero);
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

        /// <summary>A string libasrextern returned, freed; null for a null pointer.</summary>
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
                asrextern_free_string(text);
            }
        }
    }

    /// <summary>
    /// LiteRT-LM's speech recognition engine for one model, such as Parakeet-TDT-0.6B-v3, loaded from local files.
    /// Sessions created from it turn audio into text.
    /// </summary>
    public class AsrEngine : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Load a speech recognition model.
        /// </summary>
        /// <param name="modelName">The model's name in LiteRT-LM's ASR metadata, e.g. "parakeet-tdt-0.6b-v3"</param>
        /// <param name="modelPath">The model file (.tflite or .litertlm)</param>
        /// <param name="tokenizerPath">The tokenizer (tokenizer.json) for a .tflite model, or null</param>
        /// <param name="backend">"cpu", "gpu" or "npu"</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="overlapRatio">How much consecutive windows (5 s for Parakeet) overlap, from 0 to 1</param>
        /// <param name="textMerger">How the windows' texts are joined: "levenshtein" (aligns their words),
        /// "timestamp" (cuts at the words' times), or null for the model's default</param>
        public AsrEngine(String modelName, String modelPath, String tokenizerPath = null, String backend = "cpu",
            int numThreads = 0, float overlapRatio = 0.4f, String textMerger = null)
        {
            if (String.IsNullOrEmpty(modelName))
                throw new ArgumentNullException("modelName");
            if (String.IsNullOrEmpty(modelPath))
                throw new ArgumentNullException("modelPath");
            if (String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase))
                Emgu.LiteRT.Util.AcceleratorPlugins.Load();
            IntPtr error;
            _ptr = AsrInvoke.asrextern_engine_create(LiteRtLmInvoke.ToUtf8(modelName), LiteRtLmInvoke.ToUtf8(modelPath),
                LiteRtLmInvoke.ToUtf8(tokenizerPath), LiteRtLmInvoke.ToUtf8(backend ?? "cpu"), numThreads, overlapRatio,
                LiteRtLmInvoke.ToUtf8(textMerger), out error);
            String message = AsrInvoke.TakeString(error);
            if (_ptr == IntPtr.Zero)
                throw new LiteRtLmException("asrextern_engine_create failed: " + message);
            SampleRate = AsrInvoke.asrextern_engine_get_sample_rate(_ptr);
        }

        /// <summary>The sample rate the model takes, in Hz (16000 for Parakeet).</summary>
        public int SampleRate { get; private set; }

        /// <summary>Start recognizing new audio.</summary>
        public AsrSession CreateSession()
        {
            return new AsrSession(this);
        }

        /// <summary>Release the engine.</summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                AsrInvoke.asrextern_engine_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>What <see cref="AsrSession.Process"/> found.</summary>
    public enum AsrResult
    {
        /// <summary>A whole window hasn't been pushed yet: push more audio, or end it.</summary>
        NeedAudio = 0,

        /// <summary>A window was recognized: its confirmed text, and the text still subject to change.</summary>
        Text = 1,

        /// <summary>The audio ended: the last confirmed text. Nothing more comes until the session is reset.</summary>
        End = 2,
    }

    /// <summary>
    /// Speech recognition of one stream of audio: push mono samples at the engine's sample rate, end the audio, and
    /// process until the end. The audio is recognized in overlapping windows; each window confirms some text, and the
    /// confirmed texts together are the transcript. Not thread-safe: use one thread at a time.
    /// </summary>
    public class AsrSession : Emgu.LiteRT.Util.UnmanagedObject
    {
        private readonly AsrEngine _engine;

        internal AsrSession(AsrEngine engine)
        {
            _engine = engine;
            IntPtr error;
            _ptr = AsrInvoke.asrextern_session_create(engine.Ptr, out error);
            String message = AsrInvoke.TakeString(error);
            if (_ptr == IntPtr.Zero)
                throw new LiteRtLmException("asrextern_session_create failed: " + message);
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
                AsrInvoke.asrextern_session_push_audio(_ptr, handle.AddrOfPinnedObject() + offset * sizeof(float), count);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Mark the end of the audio: the last window is padded with silence.</summary>
        public void EndAudio()
        {
            AsrInvoke.asrextern_session_end_audio(_ptr);
        }

        /// <summary>
        /// Recognize the next window of the audio pushed so far.
        /// </summary>
        /// <param name="confirmed">The text that window confirmed (it won't change), or the last text at the end</param>
        /// <param name="unconfirmed">The text after it, which the next window may still change</param>
        /// <returns>Whether there was text, more audio is needed, or the audio ended</returns>
        public AsrResult Process(out String confirmed, out String unconfirmed)
        {
            IntPtr confirmedPtr, unconfirmedPtr, error;
            int result = AsrInvoke.asrextern_session_process(_ptr, out confirmedPtr, out unconfirmedPtr, out error);
            confirmed = AsrInvoke.TakeString(confirmedPtr) ?? String.Empty;
            unconfirmed = AsrInvoke.TakeString(unconfirmedPtr) ?? String.Empty;
            String message = AsrInvoke.TakeString(error);
            GC.KeepAlive(_engine);
            if (result == AsrInvoke.Error)
                throw new LiteRtLmException("asrextern_session_process failed: " + message);
            return (AsrResult)result;
        }

        /// <summary>Start again for new audio.</summary>
        public void Reset()
        {
            AsrInvoke.asrextern_session_reset(_ptr);
        }

        /// <summary>Release the session.</summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                AsrInvoke.asrextern_session_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }
}
