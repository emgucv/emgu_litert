//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

// A small C API over LiteRT-LM's speech recognition engine (omni/asr's AsrEngine, C++ only), for P/Invoke: an engine
// loads a model (e.g. Parakeet-TDT-0.6B-v3), a session takes 16 kHz mono audio pushed in pieces and returns the text.
// Strings returned are malloc'd: free them with asrextern_free_string. Errors are returned through `error` (malloc'd,
// may be null when not wanted).

#ifndef EMGU_ASREXTERN_H_
#define EMGU_ASREXTERN_H_

#if defined(_WIN32)
#define ASREXTERN_EXPORT __declspec(dllexport)
#else
#define ASREXTERN_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct AsrExternEngine AsrExternEngine;
typedef struct AsrExternSession AsrExternSession;

// What asrextern_session_process returned.
#define ASREXTERN_ERROR -1
#define ASREXTERN_NEED_AUDIO 0
#define ASREXTERN_TEXT 1
#define ASREXTERN_END 2

// Creates an engine for a model LiteRT-LM's metadata knows (e.g. "parakeet-tdt-0.6b-v3") from local files: nothing is
// downloaded. backend is "cpu", "gpu" or "npu"; num_threads <= 0 for the default; overlap_ratio (0-1, e.g. 0.4) is how
// much consecutive 5 s windows overlap; text_merger is how the windows' texts are joined: "levenshtein" (aligns their
// words) or "timestamp" (cuts at the words' times), null for the model's default. Returns null on error.
ASREXTERN_EXPORT AsrExternEngine* asrextern_engine_create(const char* model_name, const char* model_path,
                                                          const char* tokenizer_path, const char* backend,
                                                          int num_threads, float overlap_ratio,
                                                          const char* text_merger, char** error);
ASREXTERN_EXPORT void asrextern_engine_delete(AsrExternEngine* engine);

// The engine's input sample rate (16000 for Parakeet).
ASREXTERN_EXPORT int asrextern_engine_get_sample_rate(AsrExternEngine* engine);

// Creates a session reading the audio pushed to it. The engine must outlive it. Returns null on error.
ASREXTERN_EXPORT AsrExternSession* asrextern_session_create(AsrExternEngine* engine, char** error);
ASREXTERN_EXPORT void asrextern_session_delete(AsrExternSession* session);

// Adds mono samples at the engine's sample rate, in [-1, 1].
ASREXTERN_EXPORT void asrextern_session_push_audio(AsrExternSession* session, const float* samples, int count);

// Marks the end of the audio: the last window is padded with silence.
ASREXTERN_EXPORT void asrextern_session_end_audio(AsrExternSession* session);

// Recognizes the next window of the audio pushed so far. Returns ASREXTERN_TEXT with the text confirmed by that window
// (*confirmed) and the text still subject to change (*unconfirmed); ASREXTERN_NEED_AUDIO when a whole window hasn't
// been pushed yet; ASREXTERN_END after the end of the audio, with the last text in *confirmed; ASREXTERN_ERROR on error.
ASREXTERN_EXPORT int asrextern_session_process(AsrExternSession* session, char** confirmed, char** unconfirmed,
                                               char** error);

// Starts again for new audio.
ASREXTERN_EXPORT void asrextern_session_reset(AsrExternSession* session);

ASREXTERN_EXPORT void asrextern_free_string(char* text);

#ifdef __cplusplus
}
#endif

#endif  // EMGU_ASREXTERN_H_
