# Emgu's C API over LiteRT-LM's OmniEngine (omni/, C++ only): libomniextern, which the Emgu.LiteRT.LM
# SpeechToTextEngine / SpeechToTextSession and TextToImageEngine classes P/Invoke. LiteRT-LM is a pristine submodule, so this package can't
# live there in git: the build scripts copy it into LiteRT-LM/omniextern/ each run (with omniextern.cc/.h), like
# tfliteextern. Its labels are already LiteRT-LM's own, so nothing is rewritten.
#
# Built like LiteRT-LM's //c:litert-lm (a linkshared cc_binary, hidden visibility, only the OMNIEXTERN_EXPORT functions
# exported) and with the same --define=litert_runtime_link_mode=dynamic, so it links against our libLiteRt. Each model
# it can load is an alwayslink dependency that registers itself with OmniRegistry: Parakeet TDT for speech, Bonsai-Image (bonsai-flux2) and FLUX.2-klein for images,
# Kokoro for text to speech.

package(default_visibility = ["//visibility:public"])

cc_binary(
    name = "omniextern",
    srcs = [
        "omniextern.cc",
        "omniextern.h",
    ],
    copts = ["-fvisibility=hidden"],
    features = ["-legacy_whole_archive"],
    linkopts = select({
        "@platforms//os:macos": [],
        "@platforms//os:ios": [],
        "@platforms//os:windows": [],
        "//conditions:default": [
            "-Wl,--no-undefined",
            "-Wl,--strip-all",
            "-Wl,--gc-sections",
            "-Wl,-Bsymbolic",
            "-Wl,-rpath,$$ORIGIN",
        ],
    }),
    linkshared = 1,
    deps = [
        "//omni:omni_engine",
        "//omni:omni_session",
        "//omni/speech2text:parakeet_tdt",
        "//omni/text2image:bonsai_flux2",
        "//omni/text2image:flux2_klein",
        "//omni/text2speech:kokoro",
        "//omni/text2speech:tts_session",
        "//omni:omni_registry",
        "//runtime/proto:speech2text_session_config_cc_proto",
        "//runtime/proto:text2image_session_config_cc_proto",
        "//runtime/proto:text2speech_session_config_cc_proto",
        "@com_google_absl//absl/status",
        "@com_google_absl//absl/status:statusor",
        "@com_google_absl//absl/strings",
    ],
)
