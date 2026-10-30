# Emgu's C API over LiteRT-LM's speech recognition engine (omni/asr, C++ only): libasrextern, which the
# Emgu.LiteRT.LM AsrEngine / AsrSession classes P/Invoke. LiteRT-LM is a pristine submodule, so this package can't live
# there in git: the build scripts copy it into LiteRT-LM/asrextern/ each run (with asrextern.cc/.h), like tfliteextern.
# Its labels are already LiteRT-LM's own, so nothing is rewritten.
#
# Built like LiteRT-LM's //c:litert-lm (a linkshared cc_binary, hidden visibility, only the ASREXTERN_EXPORT functions
# exported) and with the same --define=litert_runtime_link_mode=dynamic, so it links against our libLiteRt.

package(default_visibility = ["//visibility:public"])

cc_binary(
    name = "asrextern",
    srcs = [
        "asrextern.cc",
        "asrextern.h",
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
        "//omni:omni_session",
        "//omni/asr:asr_engine",
        "//omni/asr:asr_session",
        "//omni/asr:audio_source",
        "//omni/asr:model_metadata",
        "@com_google_absl//absl/cleanup",
        "@com_google_absl//absl/status",
        "@com_google_absl//absl/status:statusor",
        "@com_google_absl//absl/strings",
    ],
)
