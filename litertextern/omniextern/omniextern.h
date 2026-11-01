//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

// A small C API over LiteRT-LM's OmniEngine (omni/, C++ only), for P/Invoke. For now it covers speech recognition
// (omni/speech2text): an engine loads a model (e.g. Parakeet-TDT-0.6B-v3 from its .litertlm), a session takes mono
// audio pushed in pieces and returns the text.
//
// Errors: every function that can fail returns a status code (0 on success; otherwise the same values as absl's status
// codes and LiteRT-LM's LiteRtLmStatusCode) and records a message for the calling thread, read with
// omniextern_get_last_error_message. Nothing is thrown across this API. Strings returned are malloc'd: free them with
// omniextern_free_string.

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

#ifdef __cplusplus
}
#endif

#endif  // EMGU_OMNIEXTERN_H_
