# Emgu's C API over LiteRT-LM's OmniEngine (omni/, C++ only): libomniextern, which the Emgu.LiteRT.LM
# SpeechToTextEngine / SpeechToTextSession and TextToImageEngine classes P/Invoke. LiteRT-LM is a pristine submodule, so this package can't
# live there in git: the build scripts copy it into LiteRT-LM/omniextern/ each run (with omniextern.cc/.h), like
# tfliteextern. Its labels are already LiteRT-LM's own, so nothing is rewritten.
#
# Built like LiteRT-LM's //c:litert-lm (a linkshared cc_binary, hidden visibility, only the OMNIEXTERN_EXPORT functions
# exported) and with the same --define=litert_runtime_link_mode=dynamic, so it links against our libLiteRt. Each model
# it can load is an alwayslink dependency that registers itself with OmniRegistry: Parakeet TDT and Qwen3-ASR for speech,
# Bonsai-Image (bonsai-flux2) and FLUX.2-klein for images, Kokoro for text to speech.

package(default_visibility = ["//visibility:public"])

# Also compiled by //omniextern_ios (litertextern/omniextern/bazel_ios), the iOS build.
exports_files([
    "omniextern.cc",
    "omniextern.h",
])

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
        "//omni/speech2text:qwen3_asr",
        "//omni/text2image:bonsai_flux2",
        "//omni/text2image:flux2_klein",
        "//omni/text2speech:kokoro",
        "//omni/text2speech:tts_session",
        "//omni:omni_registry",
        "//c:engine_internal",
        "//runtime/proto:speech2text_session_config_cc_proto",
        "//runtime/proto:text2image_session_config_cc_proto",
        "//runtime/proto:text2speech_session_config_cc_proto",
        "@com_google_absl//absl/status",
        "@com_google_absl//absl/status:statusor",
        "@com_google_absl//absl/strings",
    ],
)

# Windows: one LiteRT-LM runtime for both C APIs. Built as above, liblitert-lm.dll (//c:litert-lm) and omniextern.dll each
# link their own static copy of the runtime (18.7 MB and 12.6 MB). The iOS way - liblitert-lm exporting the C++ runtime and
# omniextern linking against it (litertextern/omniextern/bazel_ios) - doesn't work here: a DLL exports only what is
# __declspec(dllexport)-ed or in a .def, LiteRT-LM's C++ isn't (and the submodule isn't edited), its global data (protobuf's
# default instances, absl, the OmniRegistry models register in) would need __declspec(dllimport) on the importing side, and
# cc_shared_library with dynamic_deps isn't supported on Windows here. So this one DLL holds LiteRT-LM's C API (//c:engine,
# what //c:litert-lm is built from: the same sources and deps) and omniextern; both APIs are __declspec(dllexport)-ed
# (LITERT_LM_C_API_EXPORT, OMNIEXTERN_EXPORT), so it exports both. Named liblitert-lm (not litert-lm, as //c:litert-lm), so
# the DLL is liblitert-lm.dll - DllImport("liblitert-lm") - and so is the name libomniextern.dll imports it by.
cc_binary(
    name = "liblitert-lm",
    srcs = [
        "omniextern.cc",
        "omniextern.h",
    ],
    features = ["-legacy_whole_archive"],
    linkshared = 1,
    target_compatible_with = ["@platforms//os:windows"],
    deps = [
        "//c:engine",
        "//omni:omni_engine",
        "//omni:omni_session",
        "//omni/speech2text:parakeet_tdt",
        "//omni/speech2text:qwen3_asr",
        "//omni/text2image:bonsai_flux2",
        "//omni/text2image:flux2_klein",
        "//omni/text2speech:kokoro",
        "//omni/text2speech:tts_session",
        "//omni:omni_registry",
        "//c:engine_internal",
        "//runtime/proto:speech2text_session_config_cc_proto",
        "//runtime/proto:text2image_session_config_cc_proto",
        "//runtime/proto:text2speech_session_config_cc_proto",
        "@com_google_absl//absl/status",
        "@com_google_absl//absl/status:statusor",
        "@com_google_absl//absl/strings",
    ],
)

filegroup(
    name = "liblitert-lm_implib",
    srcs = [":liblitert-lm"],
    output_group = "interface_library",
    target_compatible_with = ["@platforms//os:windows"],
)

cc_import(
    name = "liblitert-lm_import",
    interface_library = ":liblitert-lm_implib",
    shared_library = ":liblitert-lm",
    target_compatible_with = ["@platforms//os:windows"],
)

# Windows: libomniextern.dll, with no code of its own, so Emgu.LiteRT.LM's DllImport("libomniextern") (one name on every
# platform) still resolves. It links liblitert-lm.dll's import library and re-exports the omniextern_* functions it imports
# from there: each export is the linker's jump stub through the import table, into liblitert-lm.dll. (A .def forwarder,
# "f=liblitert-lm.f", would be a true forwarder, but MSVC 14.52's link.exe doesn't make one - LNK2001 for f, even for
# "f1=kernel32.Sleep" - 2026-11-18.) omniextern_forward.def is written by bazel_build_litert.ps1 when it stages this package:
# "EXPORTS" and every OMNIEXTERN_EXPORT function of omniextern.h.
cc_binary(
    name = "libomniextern",
    srcs = ["omniextern_forward.cc"],
    linkshared = 1,
    target_compatible_with = ["@platforms//os:windows"],
    win_def_file = "omniextern_forward.def",
    deps = [":liblitert-lm_import"],
)
