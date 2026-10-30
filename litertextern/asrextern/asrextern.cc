//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#include "asrextern/asrextern.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <variant>
#include <vector>

#include "absl/cleanup/cleanup.h"  // from @com_google_absl
#include "absl/status/status.h"  // from @com_google_absl
#include "absl/status/statusor.h"  // from @com_google_absl
#include "absl/strings/match.h"  // from @com_google_absl
#include "omni/asr/asr_engine.h"
#include "omni/asr/asr_session.h"
#include "omni/asr/audio_source.h"
#include "omni/asr/model_metadata.h"
#include "omni/omni_session.h"

namespace {

using ::litert::omni::OmniSession;
using ::litert::omni::asr::AsrEngine;
using ::litert::omni::asr::AsrEngineConfig;
using ::litert::omni::asr::AsrSession;
using ::litert::omni::asr::AudioSource;

char* CopyString(const std::string& text) {
  char* copy = static_cast<char*>(std::malloc(text.size() + 1));
  if (copy != nullptr) {
    std::memcpy(copy, text.c_str(), text.size() + 1);
  }
  return copy;
}

void SetError(char** error, const std::string& message) {
  if (error != nullptr) {
    *error = CopyString(message);
  }
}

// An audio source fed by the app: it cuts the samples pushed to it into windows of samples_per_interval with
// consecutive windows overlapping (each window adds `step` new samples), the way omni/asr's FileAudioSource reads a
// file. After End(), the last window is padded with silence and the source closes.
class PushAudioSource : public AudioSource {
 public:
  PushAudioSource(int sample_rate_hz, int samples_per_interval, int step)
      : sample_rate_hz_(sample_rate_hz),
        samples_per_interval_(samples_per_interval),
        step_(step),
        window_(samples_per_interval, 0.0f) {}

  int GetSampleRateHz() const override { return sample_rate_hz_; }
  int GetNumChannels() const override { return 1; }

  void Push(const float* samples, int count) {
    std::lock_guard<std::mutex> lock(pending_mutex_);
    pending_.insert(pending_.end(), samples, samples + count);
  }

  void End() {
    std::lock_guard<std::mutex> lock(pending_mutex_);
    ended_ = true;
  }

  bool Ended() const {
    std::lock_guard<std::mutex> lock(pending_mutex_);
    return ended_;
  }

 protected:
  void ResetInternal() override {
    std::lock_guard<std::mutex> lock(pending_mutex_);
    pending_.clear();
    std::fill(window_.begin(), window_.end(), 0.0f);
    started_ = false;
    ended_ = false;
    closed_ = false;
  }

  bool NeedScheduleInternal() const override {
    std::lock_guard<std::mutex> lock(pending_mutex_);
    return !closed_ && (pending_.size() >= Needed() || (ended_ && !pending_.empty()));
  }

  absl::Status ScheduleInternal() override {
    absl::Cleanup cleanup = [this] { SetState(State::kIdle); };
    std::vector<float> chunk;
    {
      std::lock_guard<std::mutex> lock(pending_mutex_);
      if (closed_) {
        return absl::OutOfRangeError("End of audio stream reached.");
      }
      size_t needed = Needed();
      if (pending_.size() < needed && !ended_) {
        return absl::OutOfRangeError("More audio is needed.");
      }
      if (pending_.empty()) {
        closed_ = true;
        return absl::OutOfRangeError("End of audio stream reached.");
      }
      size_t taken = std::min(needed, pending_.size());
      if (started_) {
        // Keep the overlap, then the new samples, then silence for a short last window.
        std::move(window_.begin() + step_, window_.end(), window_.begin());
        auto tail = window_.begin() + (samples_per_interval_ - step_);
        std::copy_n(pending_.begin(), taken, tail);
        std::fill(tail + taken, window_.end(), 0.0f);
      } else {
        std::copy_n(pending_.begin(), taken, window_.begin());
        std::fill(window_.begin() + taken, window_.end(), 0.0f);
        started_ = true;
      }
      pending_.erase(pending_.begin(), pending_.begin() + taken);
      if (ended_ && pending_.empty()) {
        closed_ = true;
      }
      chunk = window_;
    }
    PushOutput(std::move(chunk));
    return absl::OkStatus();
  }

 private:
  // The new samples the next window takes. Must be called with pending_mutex_ held.
  size_t Needed() const { return started_ ? step_ : samples_per_interval_; }

  const int sample_rate_hz_;
  const int samples_per_interval_;
  const int step_;
  mutable std::mutex pending_mutex_;
  std::vector<float> pending_;
  std::vector<float> window_;
  bool started_ = false;
  bool ended_ = false;
  bool closed_ = false;
};

}  // namespace

struct AsrExternEngine {
  std::unique_ptr<AsrEngine> engine;
};

struct AsrExternSession {
  std::unique_ptr<AsrSession> session;
  PushAudioSource* audio = nullptr;  // owned by session
};

extern "C" {

AsrExternEngine* asrextern_engine_create(const char* model_name, const char* model_path, const char* tokenizer_path,
                                         const char* backend, int num_threads, float overlap_ratio,
                                         const char* text_merger, char** error) {
  if (model_name == nullptr || model_path == nullptr) {
    SetError(error, "model_name and model_path are required.");
    return nullptr;
  }
  AsrEngineConfig config;
  // The model's file is set first: its extension decides the decoder (.litertlm models use LiteRT-LM's).
  config.model_path = model_path;
  absl::Status status = ::litert::omni::asr::PopulateConfigFromMetadataJson(model_name, "", config);
  if (!status.ok()) {
    SetError(error, std::string(status.message()));
    return nullptr;
  }
  // The app downloads the files: nothing is fetched here.
  config.model_url.clear();
  config.tokenizer_url.clear();
  if (tokenizer_path != nullptr) {
    config.tokenizer_path = tokenizer_path;
  }
  std::string model_file(model_path);
  size_t slash = model_file.find_last_of("/\\");
  config.cache_dir = slash == std::string::npos ? std::string(".") : model_file.substr(0, slash);
  std::string backend_name = backend == nullptr ? "cpu" : backend;
  if (absl::EqualsIgnoreCase(backend_name, "gpu")) {
    config.backend = AsrEngineConfig::Backend::kGpu;
  } else if (absl::EqualsIgnoreCase(backend_name, "npu")) {
    config.backend = AsrEngineConfig::Backend::kNpu;
  } else {
    config.backend = AsrEngineConfig::Backend::kCpu;
  }
  if (num_threads > 0) {
    config.num_threads = num_threads;
  }
  if (overlap_ratio >= 0.0f && overlap_ratio < 1.0f) {
    config.overlap_ratio = overlap_ratio;
  }
  if (text_merger != nullptr && absl::EqualsIgnoreCase(text_merger, "levenshtein")) {
    config.text_merger_type = AsrEngineConfig::TextMergerType::kLevenshtein;
  } else if (text_merger != nullptr && absl::EqualsIgnoreCase(text_merger, "timestamp")) {
    config.text_merger_type = AsrEngineConfig::TextMergerType::kTimestamp;
  }
  auto engine = AsrEngine::Create(std::move(config));
  if (!engine.ok()) {
    SetError(error, std::string(engine.status().message()));
    return nullptr;
  }
  return new AsrExternEngine{std::move(*engine)};
}

void asrextern_engine_delete(AsrExternEngine* engine) { delete engine; }

int asrextern_engine_get_sample_rate(AsrExternEngine* engine) {
  return engine == nullptr ? 0 : engine->engine->config().sample_rate_hz;
}

AsrExternSession* asrextern_session_create(AsrExternEngine* engine, char** error) {
  if (engine == nullptr) {
    SetError(error, "engine is null.");
    return nullptr;
  }
  const AsrEngineConfig& config = engine->engine->config();
  int samples_per_interval =
      static_cast<int>(static_cast<long long>(config.sample_rate_hz) * config.input_milliseconds / 1000);
  int overlap = static_cast<int>(samples_per_interval * config.overlap_ratio);
  int step = samples_per_interval - overlap;
  if (samples_per_interval <= 0 || step <= 0) {
    SetError(error, "Invalid window or overlap.");
    return nullptr;
  }
  auto audio = std::make_unique<PushAudioSource>(config.sample_rate_hz, samples_per_interval, step);
  PushAudioSource* raw_audio = audio.get();
  auto session = engine->engine->CreateSession(std::move(audio));
  if (!session.ok()) {
    SetError(error, std::string(session.status().message()));
    return nullptr;
  }
  return new AsrExternSession{std::move(*session), raw_audio};
}

void asrextern_session_delete(AsrExternSession* session) { delete session; }

void asrextern_session_push_audio(AsrExternSession* session, const float* samples, int count) {
  if (session != nullptr && samples != nullptr && count > 0) {
    session->audio->Push(samples, count);
  }
}

void asrextern_session_end_audio(AsrExternSession* session) {
  if (session != nullptr) {
    session->audio->End();
  }
}

int asrextern_session_process(AsrExternSession* session, char** confirmed, char** unconfirmed, char** error) {
  if (confirmed != nullptr) *confirmed = nullptr;
  if (unconfirmed != nullptr) *unconfirmed = nullptr;
  if (session == nullptr) {
    SetError(error, "session is null.");
    return ASREXTERN_ERROR;
  }
  // Each ProcessNext takes one window through every stage; a window can end without new text (the merger keeps it
  // until the next window confirms it), so go on until there is text or no whole window left.
  while (true) {
    absl::StatusOr<OmniSession::Output> output = session->session->ProcessNext();
    if (output.ok()) {
      const auto* text = std::get_if<OmniSession::TextOutput>(&*output);
      if (text != nullptr) {
        if (confirmed != nullptr) *confirmed = CopyString(text->confirmed_text);
        if (unconfirmed != nullptr) *unconfirmed = CopyString(text->unconfirmed_text);
        return ASREXTERN_TEXT;
      }
      continue;
    }
    if (absl::IsNotFound(output.status())) {
      // The window was recognized but gave no text yet.
      continue;
    }
    if (absl::IsOutOfRange(output.status())) {
      if (!session->audio->Ended()) {
        return ASREXTERN_NEED_AUDIO;
      }
      absl::StatusOr<OmniSession::Output> last = session->session->Flush();
      std::string text_out;
      if (last.ok()) {
        if (const auto* text = std::get_if<OmniSession::TextOutput>(&*last)) {
          text_out = text->confirmed_text;
        }
      } else if (!absl::IsNotFound(last.status())) {
        SetError(error, std::string(last.status().message()));
        return ASREXTERN_ERROR;
      }
      if (confirmed != nullptr) *confirmed = CopyString(text_out);
      return ASREXTERN_END;
    }
    SetError(error, std::string(output.status().message()));
    return ASREXTERN_ERROR;
  }
}

void asrextern_session_reset(AsrExternSession* session) {
  if (session != nullptr) {
    session->session->Reset();
  }
}

void asrextern_free_string(char* text) { std::free(text); }

}  // extern "C"
