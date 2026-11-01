//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#include "omniextern/omniextern.h"

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
#include "omni/omni_engine.h"
#include "omni/omni_session.h"
#include "runtime/proto/speech2text_session_config.pb.h"

namespace {

using ::litert::omni::OmniEngine;
using ::litert::omni::OmniSession;
using ::litert::omni::PushInputSource;
using ::litert::lm::proto::Speech2TextSessionConfig;

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

}  // namespace

struct OmniExternEngine {
  std::unique_ptr<OmniEngine> engine;
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
    OmniEngine::Settings settings;
    settings.model_path = model_path;
    // The compiled-model caches go next to the model, as LiteRT-LM's engine does.
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

}  // extern "C"
