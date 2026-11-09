//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

// A small C API over LiteRT-LM's OmniEngine (omni/, C++ only), for P/Invoke. It covers speech recognition
// (omni/speech2text): an engine loads a model (e.g. Parakeet-TDT-0.6B-v3 from its .litertlm), a session takes mono
// audio pushed in pieces and returns the text; text to image (omni/text2image): an image engine loads a model
// (Bonsai-Image-ternary-4B or FLUX.2-klein-4B from its .litertlm) and draws a picture for a prompt; and text to speech
// (omni/text2speech): a TTS engine loads a model (Kokoro 82M) and speaks text in one of its voices.
//
// Errors: every function that can fail returns a status code (0 on success; otherwise the same values as absl's status
// codes and LiteRT-LM's LiteRtLmStatusCode) and records a message for the calling thread, read with
// omniextern_get_last_error_message. Nothing is thrown across this API. Strings returned are malloc'd: free them with
// omniextern_free_string; pixels with omniextern_free_buffer.

#ifndef EMGU_OMNIEXTERN_H_
#define EMGU_OMNIEXTERN_H_

#if defined(_WIN32)
#define OMNIEXTERN_EXPORT __declspec(dllexport)
#else
#define OMNIEXTERN_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct OmniExternEngine OmniExternEngine;
typedef struct OmniExternSession OmniExternSession;
typedef struct OmniExternImageEngine OmniExternImageEngine;
typedef struct OmniExternTtsEngine OmniExternTtsEngine;

// The status codes (absl::StatusCode's values).
#define OMNIEXTERN_STATUS_OK 0
#define OMNIEXTERN_STATUS_INVALID_ARGUMENT 3
#define OMNIEXTERN_STATUS_INTERNAL 13

// What omniextern_session_process found.
#define OMNIEXTERN_NEED_AUDIO 0
#define OMNIEXTERN_TEXT 1
#define OMNIEXTERN_END 2

// The message recorded for the last failed call on this thread, or null; valid until the next failure on this thread.
OMNIEXTERN_EXPORT const char* omniextern_get_last_error_message(void);
OMNIEXTERN_EXPORT void omniextern_clear_last_error(void);

// Creates a speech recognition engine for a model LiteRT-LM registers (e.g. "parakeet-tdt-0.6b-v3") from its local
// .litertlm (which holds the tokenizer): nothing is downloaded. tokenizer_path is ignored (kept for the C# API).
// backend is "cpu", "gpu" or "npu"; num_threads <= 0 for the default; overlap_ratio (0-1, e.g. 0.4) is how much
// consecutive windows overlap, < 0 for LiteRT-LM's default; text_merger is how the windows' texts are joined:
// "levenshtein" (aligns their words) or "timestamp" (cuts at the words' times), null for LiteRT-LM's default.
OMNIEXTERN_EXPORT int omniextern_engine_create(const char* model_name, const char* model_path,
                                               const char* tokenizer_path, const char* backend, int num_threads,
                                               float overlap_ratio, const char* text_merger,
                                               OmniExternEngine** out_engine);
OMNIEXTERN_EXPORT void omniextern_engine_delete(OmniExternEngine* engine);

// The sample rate omniextern_session_push_audio expects (16000); the session resamples for the model.
OMNIEXTERN_EXPORT int omniextern_engine_get_sample_rate(OmniExternEngine* engine);

// Creates a session reading the audio pushed to it. The engine must outlive it.
OMNIEXTERN_EXPORT int omniextern_session_create(OmniExternEngine* engine, OmniExternSession** out_session);
OMNIEXTERN_EXPORT void omniextern_session_delete(OmniExternSession* session);

// Adds mono samples at the engine's sample rate, in [-1, 1]. Can be called from another thread than
// omniextern_session_process.
OMNIEXTERN_EXPORT int omniextern_session_push_audio(OmniExternSession* session, const float* samples, int count);

// Marks the end of the audio.
OMNIEXTERN_EXPORT int omniextern_session_end_audio(OmniExternSession* session);

// Recognizes the next window of the audio pushed so far. *out_result is OMNIEXTERN_TEXT with the text confirmed by
// that window (*confirmed) and the text still subject to change (*unconfirmed); OMNIEXTERN_NEED_AUDIO when a whole
// window hasn't been pushed yet; OMNIEXTERN_END after the end of the audio, with the rest of the text in *confirmed.
OMNIEXTERN_EXPORT int omniextern_session_process(OmniExternSession* session, int* out_result, char** confirmed,
                                                 char** unconfirmed);

// Starts again for new audio (audio pushed but not recognized yet is dropped).
OMNIEXTERN_EXPORT int omniextern_session_reset(OmniExternSession* session);

OMNIEXTERN_EXPORT void omniextern_free_string(char* text);

// Creates a text-to-image engine for a model LiteRT-LM registers ("bonsai-flux2", "flux2-klein") from its local
// .litertlm: nothing is downloaded. backend is "cpu", "gpu" or "npu"; num_threads <= 0 for the default; size is the
// side of the square pictures in pixels, a multiple of 16, <= 0 for the model's own (its graphs are made for one size:
// 256 for both models, which refuse any other). cache_dir is the folder for the compiled-model caches (about 3.5 GB,
// named after the graphs, "flux2_dit_<file time>_<file size>...", so each model wants a folder of its own), or null
// for the model file's folder.
OMNIEXTERN_EXPORT int omniextern_image_engine_create(const char* model_name, const char* model_path,
                                                     const char* backend, int num_threads, int size,
                                                     const char* cache_dir, OmniExternImageEngine** out_engine);
OMNIEXTERN_EXPORT void omniextern_image_engine_delete(OmniExternImageEngine* engine);

// Draws a picture for prompt (UTF-8): steps <= 0 for the model's default (4); seed > 0 picks the starting noise
// (the same seed and prompt give the same picture), 0 for the model's default. Blocks until the picture is done; it
// can't be cancelled. *out_rgb is the picture, interleaved RGB888 (*out_height rows of *out_width x *out_channels
// bytes), malloc'd: free it with omniextern_free_buffer. One picture at a time per engine.
OMNIEXTERN_EXPORT int omniextern_image_generate(OmniExternImageEngine* engine, const char* prompt, int steps,
                                                unsigned long long seed, unsigned char** out_rgb, int* out_width,
                                                int* out_height, int* out_channels);

OMNIEXTERN_EXPORT void omniextern_free_buffer(void* buffer);

// Creates a text-to-speech engine for a model LiteRT-LM registers ("kokoro") from its local .litertlm: nothing is
// downloaded. backend is "cpu", "gpu" or "npu" (Kokoro's GPU file runs its vocoder on the GPU); num_threads <= 0 for the
// default; language is BCP-47 ("en-US", "fr", "zh"...) and voice a voice of that language ("af_heart"), either null for
// the model's default; cache_dir is the folder for the compiled-model caches, null for the model file's folder. The
// language and voice are fixed for the engine (a voice of another language is INVALID_ARGUMENT).
OMNIEXTERN_EXPORT int omniextern_tts_engine_create(const char* model_name, const char* model_path, const char* backend,
                                                   int num_threads, const char* language, const char* voice,
                                                   const char* cache_dir, OmniExternTtsEngine** out_engine);
OMNIEXTERN_EXPORT void omniextern_tts_engine_delete(OmniExternTtsEngine* engine);

// The model's voices, one per line (*out_names, free with omniextern_free_string): those of language (BCP-47), or all of
// them for a null or empty language.
OMNIEXTERN_EXPORT int omniextern_tts_engine_get_voices(OmniExternTtsEngine* engine, const char* language,
                                                       char** out_names);

// The languages the model speaks, BCP-47, one per line (*out_names, free with omniextern_free_string).
OMNIEXTERN_EXPORT int omniextern_tts_engine_get_languages(OmniExternTtsEngine* engine, char** out_names);

// The model's default voice for language (BCP-47), in *out_voice (free with omniextern_free_string).
OMNIEXTERN_EXPORT int omniextern_tts_engine_get_default_voice(OmniExternTtsEngine* engine, const char* language,
                                                              char** out_voice);

// Speaks text (UTF-8) in the engine's language and voice. Blocks until the audio is done (longer text is cut at its
// sentences and clauses inside). *out_pcm is mono float samples in [-1, 1] (*out_count of them at *out_sample_rate Hz,
// 24000 for Kokoro), malloc'd: free it with omniextern_free_buffer. One call at a time per engine.
OMNIEXTERN_EXPORT int omniextern_tts_synthesize(OmniExternTtsEngine* engine, const char* text, float** out_pcm,
                                                int* out_count, int* out_sample_rate);

// LiteRT-LM's engine settings (c/engine.h), for the setting its C API doesn't have.
typedef struct LiteRtLmEngineSettings LiteRtLmEngineSettings;

// Limits the main model to these signatures (count names, e.g. "decode" and "prefill_128"); count 0 for all of them
// (the default). LiteRT-LM's LlmExecutorSettings::SetSelectedSignatures, its --selected_signatures flag: the list must
// include "decode" and a prefill signature, or the engine fails to load. Prefilling with a smaller signature only keeps
// its smaller buffers: Gemma 4 E2B on the GPU keeps ~740 MB more after any prompt that took its prefill_1024.
OMNIEXTERN_EXPORT int omniextern_engine_settings_set_selected_signatures(LiteRtLmEngineSettings* settings,
                                                                          const char* const* names, int count);

#ifdef __cplusplus
}
#endif

#endif  // EMGU_OMNIEXTERN_H_
