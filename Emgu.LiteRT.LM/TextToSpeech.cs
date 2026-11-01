//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;

namespace Emgu.LiteRT.LM
{
    /// <summary>
    /// LiteRT-LM's text to speech (its OmniEngine, omni/text2speech) for one model, such as Kokoro 82M, loaded from its
    /// local .litertlm, in one language and voice chosen when it is created (another voice needs another engine; loading
    /// one takes well under a second once the caches are written). Use one thread at a time.
    /// </summary>
    public class TextToSpeechEngine : Emgu.LiteRT.Util.UnmanagedObject
    {
        /// <summary>
        /// Load a text-to-speech model.
        /// </summary>
        /// <param name="modelName">The model's name in LiteRT-LM's registry, e.g. "kokoro"</param>
        /// <param name="modelPath">The model file (.litertlm)</param>
        /// <param name="backend">"cpu", "gpu" or "npu" (Kokoro's GPU file runs its vocoder on the GPU)</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="language">The language, BCP-47 ("en-US", "fr", "zh"...), or null for the voice's or the model's default</param>
        /// <param name="voice">A voice of that language ("af_heart"), or null for the language's default</param>
        /// <param name="cacheDir">The folder for the compiled-model caches, or null for the model file's folder</param>
        public TextToSpeechEngine(String modelName, String modelPath, String backend = "cpu", int numThreads = 0,
            String language = null, String voice = null, String cacheDir = null)
        {
            if (String.IsNullOrEmpty(modelName))
                throw new ArgumentNullException("modelName");
            if (String.IsNullOrEmpty(modelPath))
                throw new ArgumentNullException("modelPath");
            if (String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase))
                Emgu.LiteRT.Util.AcceleratorPlugins.Load();
            OmniInvoke.CheckError(OmniInvoke.omniextern_tts_engine_create(LiteRtLmInvoke.ToUtf8(modelName),
                LiteRtLmInvoke.ToUtf8(modelPath), LiteRtLmInvoke.ToUtf8(backend ?? "cpu"), numThreads,
                LiteRtLmInvoke.ToUtf8(language), LiteRtLmInvoke.ToUtf8(voice), LiteRtLmInvoke.ToUtf8(cacheDir), out _ptr),
                "omniextern_tts_engine_create");
        }

        /// <summary>The model's voices: those of a language (BCP-47), or all of them for null.</summary>
        public String[] GetVoices(String language = null)
        {
            IntPtr names;
            OmniInvoke.CheckError(OmniInvoke.omniextern_tts_engine_get_voices(_ptr, LiteRtLmInvoke.ToUtf8(language), out names),
                "omniextern_tts_engine_get_voices");
            return Lines(OmniInvoke.TakeString(names));
        }

        /// <summary>The languages the model speaks, BCP-47.</summary>
        public String[] Languages
        {
            get
            {
                IntPtr names;
                OmniInvoke.CheckError(OmniInvoke.omniextern_tts_engine_get_languages(_ptr, out names),
                    "omniextern_tts_engine_get_languages");
                return Lines(OmniInvoke.TakeString(names));
            }
        }

        /// <summary>The model's default voice for a language (BCP-47).</summary>
        public String GetDefaultVoice(String language)
        {
            if (String.IsNullOrEmpty(language))
                throw new ArgumentNullException("language");
            IntPtr voice;
            OmniInvoke.CheckError(OmniInvoke.omniextern_tts_engine_get_default_voice(_ptr, LiteRtLmInvoke.ToUtf8(language), out voice),
                "omniextern_tts_engine_get_default_voice");
            return OmniInvoke.TakeString(voice);
        }

        /// <summary>
        /// Speak a text in the engine's language and voice. Blocks until the audio is done; longer text is cut at its
        /// sentences and clauses inside.
        /// </summary>
        /// <param name="text">What to say</param>
        /// <param name="sampleRate">The audio's sample rate in Hz (24000 for Kokoro)</param>
        /// <returns>Mono samples in [-1, 1]</returns>
        public float[] Synthesize(String text, out int sampleRate)
        {
            if (String.IsNullOrEmpty(text))
                throw new ArgumentNullException("text");
            IntPtr pcm;
            int count;
            LiteRtLmStatusCode status = OmniInvoke.omniextern_tts_synthesize(_ptr, LiteRtLmInvoke.ToUtf8(text), out pcm, out count, out sampleRate);
            OmniInvoke.CheckError(status, "omniextern_tts_synthesize");
            try
            {
                float[] samples = new float[count];
                if (count > 0)
                    Marshal.Copy(pcm, samples, 0, count);
                return samples;
            }
            finally
            {
                OmniInvoke.omniextern_free_buffer(pcm);
            }
        }

        private static String[] Lines(String text)
        {
            return String.IsNullOrEmpty(text) ? new String[0] : text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Release the engine.</summary>
        protected override void DisposeObject()
        {
            if (_ptr != IntPtr.Zero)
            {
                OmniInvoke.omniextern_tts_engine_delete(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }
}
