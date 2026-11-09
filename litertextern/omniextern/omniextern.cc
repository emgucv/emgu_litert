//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#include "omniextern/omniextern.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <utility>
#include <variant>
#include <vector>

#include "absl/status/status.h"  // from @com_google_absl
#include "absl/status/statusor.h"  // from @com_google_absl
#include "absl/strings/match.h"  // from @com_google_absl
#include "c/engine_internal.h"
#include "omni/omni_engine.h"
#include "omni/omni_registry.h"
#include "omni/omni_session.h"
#include "omni/text2speech/tts_session.h"
#include "runtime/proto/speech2text_session_config.pb.h"
#include "runtime/proto/text2image_session_config.pb.h"
#include "runtime/proto/text2speech_session_config.pb.h"

namespace {

using ::litert::omni::OmniEngine;
using ::litert::omni::OmniSession;
using ::litert::omni::PushInputSource;
using ::litert::lm::proto::Speech2TextSessionConfig;
using ::litert::lm::proto::Text2ImageSessionConfig;
using ::litert::lm::proto::Text2SpeechSessionConfig;

// The sample rate the app pushes; the session resamples to the model's.
constexpr int kInputSampleRateHz = 16000;

// The calling thread's last error, as LiteRT-LM's C API keeps it.
thread_local std::string last_error;
thread_local bool has_last_error = false;

int Fail(absl::StatusCode code, const std::string& message) {
  last_error = message;
  has_last_error = true;
  return static_cast<int>(code);
}

int Fail(const absl::Status& status) { return Fail(status.code(), std::string(status.message())); }

// Runs body, turning a C++ exception into a recorded error: nothing may unwind into the caller (P/Invoke).
template <typename Body>
int Guard(Body&& body) {
  try {
    return body();
  } catch (const std::exception& e) {
    return Fail(absl::StatusCode::kInternal, e.what());
  } catch (...) {
    return Fail(absl::StatusCode::kInternal, "Unknown exception.");
  }
}

char* CopyString(const std::string& text) {
  char* copy = static_cast<char*>(std::malloc(text.size() + 1));
  if (copy != nullptr) {
    std::memcpy(copy, text.c_str(), text.size() + 1);
  }
  return copy;
}

// The engine settings both kinds of model share: the compiled-model caches go next to the model, as LiteRT-LM's
// engine does.
OmniEngine::Settings MakeSettings(const char* model_path, const char* backend, int num_threads) {
  OmniEngine::Settings settings;
  settings.model_path = model_path;
  std::string model_file(model_path);
  size_t slash = model_file.find_last_of("/\\");
  settings.cache_dir = slash == std::string::npos ? std::string(".") : model_file.substr(0, slash);
  std::string backend_name = backend == nullptr ? "cpu" : backend;
  if (absl::EqualsIgnoreCase(backend_name, "gpu")) {
    settings.backend = OmniEngine::Settings::Backend::kGpu;
  } else if (absl::EqualsIgnoreCase(backend_name, "npu")) {
    settings.backend = OmniEngine::Settings::Backend::kNpu;
  } else {
    settings.backend = OmniEngine::Settings::Backend::kCpu;
  }
  if (num_threads > 0) {
    settings.num_threads = num_threads;
  }
  return settings;
}

}  // namespace

struct OmniExternEngine {
  std::unique_ptr<OmniEngine> engine;
};

struct OmniExternImageEngine {
  std::unique_ptr<OmniEngine> engine;
};

// The session factory itself rather than an OmniEngine (which hides it): it lists the voices and languages.
struct OmniExternTtsEngine {
  std::unique_ptr<litert::omni::OmniSessionFactory> factory;
  litert::omni::tts::TtsSessionFactory* tts = nullptr;  // the same object
};

struct OmniExternSession {
  std::unique_ptr<OmniSession> session;
  PushInputSource* input = nullptr;  // owned by session
  bool ended = false;
};

namespace {

absl::Status PushMetadata(OmniExternSession* session) {
  return session->input->PushInput(OmniSession::AudioInputMetadata{kInputSampleRateHz, 1});
}

// The text left after the end of the audio: Flush drains every stage and joins what they still hold.
int Finish(OmniExternSession* session, int* out_result, char** confirmed) {
  absl::StatusOr<OmniSession::Output> last = session->session->Flush();
  if (!last.ok()) {
    return Fail(last.status());
  }
  std::string text;
  if (const auto* output = std::get_if<OmniSession::TextOutput>(&*last)) {
    text = output->confirmed_text;
  }
  if (confirmed != nullptr) *confirmed = CopyString(text);
  *out_result = OMNIEXTERN_END;
  return OMNIEXTERN_STATUS_OK;
}

}  // namespace

extern "C" {

const char* omniextern_get_last_error_message(void) { return has_last_error ? last_error.c_str() : nullptr; }

void omniextern_clear_last_error(void) {
  last_error.clear();
  has_last_error = false;
}

int omniextern_engine_create(const char* model_name, const char* model_path, const char* /*tokenizer_path*/,
                             const char* backend, int num_threads, float overlap_ratio, const char* text_merger,
                             OmniExternEngine** out_engine) {
  if (out_engine != nullptr) *out_engine = nullptr;
  if (model_name == nullptr || model_path == nullptr || out_engine == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "model_name, model_path and out_engine are required.");
  }
  return Guard([&]() -> int {
    OmniEngine::Settings settings = MakeSettings(model_path, backend, num_threads);
    Speech2TextSessionConfig session_config;
    if (overlap_ratio >= 0.0f && overlap_ratio < 1.0f) {
      session_config.set_overlap_ratio(overlap_ratio);
    }
    if (text_merger != nullptr && absl::EqualsIgnoreCase(text_merger, "levenshtein")) {
      session_config.set_text_merger_type(Speech2TextSessionConfig::TEXT_MERGER_TYPE_LEVENSHTEIN);
    } else if (text_merger != nullptr && absl::EqualsIgnoreCase(text_merger, "timestamp")) {
      session_config.set_text_merger_type(Speech2TextSessionConfig::TEXT_MERGER_TYPE_TIMESTAMP);
    }
    auto engine = OmniEngine::Create(model_name, settings, &session_config);
    if (!engine.ok()) {
      return Fail(engine.status());
    }
    *out_engine = new OmniExternEngine{std::move(*engine)};
    return OMNIEXTERN_STATUS_OK;
  });
}

void omniextern_engine_delete(OmniExternEngine* engine) { delete engine; }

int omniextern_engine_get_sample_rate(OmniExternEngine* engine) { return engine == nullptr ? 0 : kInputSampleRateHz; }

int omniextern_session_create(OmniExternEngine* engine, OmniExternSession** out_session) {
  if (out_session != nullptr) *out_session = nullptr;
  if (engine == nullptr || out_session == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "engine and out_session are required.");
  }
  return Guard([&]() -> int {
    auto input = std::make_unique<PushInputSource>();
    PushInputSource* raw_input = input.get();
    auto session = engine->engine->CreateSession(std::move(input));
    if (!session.ok()) {
      return Fail(session.status());
    }
    auto result = std::make_unique<OmniExternSession>();
    result->session = std::move(*session);
    result->input = raw_input;
    absl::Status status = PushMetadata(result.get());
    if (!status.ok()) {
      return Fail(status);
    }
    *out_session = result.release();
    return OMNIEXTERN_STATUS_OK;
  });
}

void omniextern_session_delete(OmniExternSession* session) { delete session; }

int omniextern_session_push_audio(OmniExternSession* session, const float* samples, int count) {
  if (session == nullptr || (samples == nullptr && count > 0) || count < 0) {
    return Fail(absl::StatusCode::kInvalidArgument, "session and samples are required.");
  }
  if (count == 0) {
    return OMNIEXTERN_STATUS_OK;
  }
  return Guard([&]() -> int {
    absl::Status status =
        session->input->PushInput(OmniSession::AudioInput{std::vector<float>(samples, samples + count)});
    return status.ok() ? OMNIEXTERN_STATUS_OK : Fail(status);
  });
}

int omniextern_session_end_audio(OmniExternSession* session) {
  if (session == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "session is null.");
  }
  return Guard([&]() -> int {
    if (!session->ended) {
      session->input->Finish();
      session->ended = true;
    }
    return OMNIEXTERN_STATUS_OK;
  });
}

int omniextern_session_process(OmniExternSession* session, int* out_result, char** confirmed, char** unconfirmed) {
  if (confirmed != nullptr) *confirmed = nullptr;
  if (unconfirmed != nullptr) *unconfirmed = nullptr;
  if (session == nullptr || out_result == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "session and out_result are required.");
  }
  *out_result = OMNIEXTERN_NEED_AUDIO;
  return Guard([&]() -> int {
    absl::StatusOr<OmniSession::Output> output = session->session->ProcessNext();
    if (output.ok()) {
      if (const auto* text = std::get_if<OmniSession::TextOutput>(&*output)) {
        if (confirmed != nullptr) *confirmed = CopyString(text->confirmed_text);
        if (unconfirmed != nullptr) *unconfirmed = CopyString(text->unconfirmed_text);
        *out_result = OMNIEXTERN_TEXT;
        return OMNIEXTERN_STATUS_OK;
      }
      // Another kind of output (the end of the output): treated like no output.
    } else if (!absl::IsNotFound(output.status()) && !absl::IsOutOfRange(output.status())) {
      return Fail(output.status());
    }
    // NotFound: no whole window yet. OutOfRange: the input ended and everything was sent through.
    return session->ended ? Finish(session, out_result, confirmed) : OMNIEXTERN_STATUS_OK;
  });
}

int omniextern_session_reset(OmniExternSession* session) {
  if (session == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "session is null.");
  }
  return Guard([&]() -> int {
    session->session->Reset();
    session->ended = false;
    absl::Status status = PushMetadata(session);
    return status.ok() ? OMNIEXTERN_STATUS_OK : Fail(status);
  });
}

void omniextern_free_string(char* text) { std::free(text); }

int omniextern_image_engine_create(const char* model_name, const char* model_path, const char* backend,
                                   int num_threads, int size, const char* cache_dir,
                                   OmniExternImageEngine** out_engine) {
  if (out_engine != nullptr) *out_engine = nullptr;
  if (model_name == nullptr || model_path == nullptr || out_engine == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "model_name, model_path and out_engine are required.");
  }
  if (size > 0 && size % 16 != 0) {
    return Fail(absl::StatusCode::kInvalidArgument, "size must be a multiple of 16.");
  }
  return Guard([&]() -> int {
    OmniEngine::Settings settings = MakeSettings(model_path, backend, num_threads);
    if (cache_dir != nullptr && cache_dir[0] != '\0') {
      settings.cache_dir = cache_dir;
    }
    // The stages are built for one square size, taken from the session config (a prompt's own size is ignored).
    Text2ImageSessionConfig session_config;
    if (size > 0) {
      session_config.set_width(size);
      session_config.set_height(size);
    }
    auto engine = OmniEngine::Create(model_name, settings, &session_config);
    if (!engine.ok()) {
      return Fail(engine.status());
    }
    *out_engine = new OmniExternImageEngine{std::move(*engine)};
    return OMNIEXTERN_STATUS_OK;
  });
}

void omniextern_image_engine_delete(OmniExternImageEngine* engine) { delete engine; }

int omniextern_image_generate(OmniExternImageEngine* engine, const char* prompt, int steps, unsigned long long seed,
                              unsigned char** out_rgb, int* out_width, int* out_height, int* out_channels) {
  if (out_rgb != nullptr) *out_rgb = nullptr;
  if (engine == nullptr || prompt == nullptr || out_rgb == nullptr || out_width == nullptr || out_height == nullptr ||
      out_channels == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "engine, prompt and the out parameters are required.");
  }
  if (prompt[0] == '\0') {
    return Fail(absl::StatusCode::kInvalidArgument, "The prompt is empty.");
  }
  return Guard([&]() -> int {
    // A session per picture: its stages are made over the engine's loaded models, so this costs little, and the
    // engine keeps no state between pictures.
    auto input = std::make_unique<PushInputSource>();
    PushInputSource* raw_input = input.get();
    auto session = engine->engine->CreateSession(std::move(input));
    if (!session.ok()) {
      return Fail(session.status());
    }
    OmniSession::ImageGenInputMetadata metadata;
    metadata.num_inference_steps = steps;  // <= 0: the model's default
    metadata.seed = seed;                  // 0: the model's default
    absl::Status status = raw_input->PushInput(metadata);
    if (status.ok()) {
      status = raw_input->PushInput(OmniSession::TextInput{.text = prompt});
    }
    if (!status.ok()) {
      return Fail(status);
    }
    absl::StatusOr<OmniSession::Output> output = (*session)->ProcessNext();
    if (!output.ok()) {
      return Fail(output.status());
    }
    const auto* image = std::get_if<litert::omni::ImageOutput>(&*output);
    if (image == nullptr || image->rgb_data.empty()) {
      return Fail(absl::StatusCode::kInternal, "The model returned no picture.");
    }
    auto* pixels = static_cast<unsigned char*>(std::malloc(image->rgb_data.size()));
    if (pixels == nullptr) {
      return Fail(absl::StatusCode::kResourceExhausted, "Out of memory for the picture.");
    }
    std::memcpy(pixels, image->rgb_data.data(), image->rgb_data.size());
    *out_rgb = pixels;
    *out_width = image->width;
    *out_height = image->height;
    *out_channels = image->channels;
    return OMNIEXTERN_STATUS_OK;
  });
}

void omniextern_free_buffer(void* buffer) { std::free(buffer); }

int omniextern_tts_engine_create(const char* model_name, const char* model_path, const char* backend,
                                 int num_threads, const char* language, const char* voice, const char* cache_dir,
                                 OmniExternTtsEngine** out_engine) {
  if (out_engine != nullptr) *out_engine = nullptr;
  if (model_name == nullptr || model_path == nullptr || out_engine == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "model_name, model_path and out_engine are required.");
  }
  return Guard([&]() -> int {
    OmniEngine::Settings settings = MakeSettings(model_path, backend, num_threads);
    if (cache_dir != nullptr && cache_dir[0] != '\0') {
      settings.cache_dir = cache_dir;
    }
    Text2SpeechSessionConfig session_config;
    if (language != nullptr && language[0] != '\0') {
      session_config.set_language(language);
    }
    if (voice != nullptr && voice[0] != '\0') {
      session_config.set_voice(voice);
    }
    // Cut only at line breaks: every acoustic run costs about the same (Kokoro's frames are a fixed size, about 10 s of
    // speech), so a run per sentence - LiteRT-LM's default - spends a whole run on "Hello."; the caller groups short
    // sentences into one text, and Kokoro still splits a text too long for one run by itself.
    session_config.mutable_text_chunk_config()->add_delimiters("\n");
    auto factory = litert::omni::OmniRegistry::Get().CreateSessionFactory(model_name, settings, &session_config);
    if (!factory.ok()) {
      return Fail(factory.status());
    }
    auto* tts = dynamic_cast<litert::omni::tts::TtsSessionFactory*>(factory->get());
    if (tts == nullptr) {
      return Fail(absl::StatusCode::kInvalidArgument, std::string(model_name) + " isn't a text-to-speech model.");
    }
    // A voice of another language is only noticed when a session is made: check it now, with the message LiteRT-LM gives.
    auto check = (*factory)->Create(std::make_unique<PushInputSource>());
    if (!check.ok()) {
      return Fail(check.status());
    }
    *out_engine = new OmniExternTtsEngine{std::move(*factory), tts};
    return OMNIEXTERN_STATUS_OK;
  });
}

void omniextern_tts_engine_delete(OmniExternTtsEngine* engine) { delete engine; }

namespace {

char* JoinLines(const std::vector<std::string>& lines) {
  std::string text;
  for (const std::string& line : lines) {
    if (!text.empty()) text += '\n';
    text += line;
  }
  return CopyString(text);
}

}  // namespace

int omniextern_tts_engine_get_voices(OmniExternTtsEngine* engine, const char* language, char** out_names) {
  if (out_names != nullptr) *out_names = nullptr;
  if (engine == nullptr || out_names == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "engine and out_names are required.");
  }
  return Guard([&]() -> int {
    *out_names = JoinLines(language == nullptr || language[0] == '\0' ? engine->tts->GetAvailableVoices()
                                                                      : engine->tts->GetAvailableVoices(language));
    return OMNIEXTERN_STATUS_OK;
  });
}

int omniextern_tts_engine_get_languages(OmniExternTtsEngine* engine, char** out_names) {
  if (out_names != nullptr) *out_names = nullptr;
  if (engine == nullptr || out_names == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "engine and out_names are required.");
  }
  return Guard([&]() -> int {
    *out_names = JoinLines(engine->tts->GetAvailableLanguages());
    return OMNIEXTERN_STATUS_OK;
  });
}

int omniextern_tts_engine_get_default_voice(OmniExternTtsEngine* engine, const char* language, char** out_voice) {
  if (out_voice != nullptr) *out_voice = nullptr;
  if (engine == nullptr || language == nullptr || out_voice == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "engine, language and out_voice are required.");
  }
  return Guard([&]() -> int {
    *out_voice = CopyString(engine->tts->GetDefaultVoice(language));
    return OMNIEXTERN_STATUS_OK;
  });
}

int omniextern_tts_synthesize(OmniExternTtsEngine* engine, const char* text, float** out_pcm, int* out_count,
                              int* out_sample_rate) {
  if (out_pcm != nullptr) *out_pcm = nullptr;
  if (out_count != nullptr) *out_count = 0;
  if (engine == nullptr || text == nullptr || out_pcm == nullptr || out_count == nullptr || out_sample_rate == nullptr) {
    return Fail(absl::StatusCode::kInvalidArgument, "engine, text and the out parameters are required.");
  }
  return Guard([&]() -> int {
    auto input = std::make_unique<PushInputSource>();
    PushInputSource* raw_input = input.get();
    auto session = engine->factory->Create(std::move(input));
    if (!session.ok()) {
      return Fail(session.status());
    }
    absl::Status status = raw_input->PushInput(OmniSession::TextInput{.text = text});
    if (!status.ok()) {
      return Fail(status);
    }
    raw_input->Finish();
    // The sentences' audio as it is made, then what Flush still holds.
    std::vector<float> pcm;
    int sample_rate = 24000;
    auto append = [&](const OmniSession::Output& output) {
      if (const auto* audio = std::get_if<litert::omni::AudioOutput>(&output)) {
        pcm.insert(pcm.end(), audio->pcm_samples.begin(), audio->pcm_samples.end());
        if (audio->sample_rate_hz > 0) sample_rate = audio->sample_rate_hz;
      }
    };
    while (true) {
      absl::StatusOr<OmniSession::Output> output = (*session)->ProcessNext();
      if (!output.ok()) {
        if (absl::IsNotFound(output.status()) || absl::IsOutOfRange(output.status())) break;
        return Fail(output.status());
      }
      if (std::holds_alternative<litert::omni::EndOfOutput>(*output)) break;
      append(*output);
    }
    absl::StatusOr<OmniSession::Output> last = (*session)->Flush();
    if (!last.ok() && !absl::IsOutOfRange(last.status()) && !absl::IsNotFound(last.status())) {
      return Fail(last.status());
    }
    if (last.ok()) append(*last);
    auto* buffer = static_cast<float*>(std::malloc(std::max<size_t>(1, pcm.size()) * sizeof(float)));
    if (buffer == nullptr) {
      return Fail(absl::StatusCode::kResourceExhausted, "Out of memory for the audio.");
    }
    if (!pcm.empty()) std::memcpy(buffer, pcm.data(), pcm.size() * sizeof(float));
    *out_pcm = buffer;
    *out_count = static_cast<int>(pcm.size());
    *out_sample_rate = sample_rate;
    return OMNIEXTERN_STATUS_OK;
  });
}

int omniextern_engine_settings_set_selected_signatures(LiteRtLmEngineSettings* settings, const char* const* names,
                                                       int count) {
  return Guard([&]() -> int {
    if (settings == nullptr || settings->settings == nullptr || count < 0 || (count > 0 && names == nullptr)) {
      return Fail(absl::StatusCode::kInvalidArgument, "settings is null, or the names are missing.");
    }
    std::vector<std::string> selected;
    for (int i = 0; i < count; ++i) {
      if (names[i] == nullptr) return Fail(absl::StatusCode::kInvalidArgument, "A signature name is null.");
      selected.emplace_back(names[i]);
    }
    settings->settings->GetMutableMainExecutorSettings().SetSelectedSignatures(std::move(selected));
    return OMNIEXTERN_STATUS_OK;
  });
}

}  // extern "C"
