# Android: liblitert-lm.so and libomniextern.so from one LiteRT-LM runtime, the way the iOS build does it
# (litertextern/omniextern/bazel_ios). platforms/android/bazel_build_litert stages this package into the LiteRT-LM
# workspace as omniextern_android/, with bazel_ios's static_archives.bzl, next to omniextern/ (whose omniextern.cc and
# omniextern.h it compiles).
#
# Built as two linkshared cc_binaries (LiteRT-LM's //c:litert-lm and //omniextern:omniextern), each library carried its
# own static copy of the runtime: libomniextern.so duplicated most of liblitert-lm.so. Here liblitert-lm.so is a
# cc_shared_library of LiteRT-LM's C API (//c:engine, what //c:litert-lm is built from) that exports the runtime, and
# libomniextern.so links against it (dynamic_deps), so each library is linked once. Both keep their file names, so the
# Emgu.LiteRT.LM and Emgu.LiteRT.Runtime packages are unchanged. The other platforms (Linux, Windows, macOS) still build
# //omniextern:omniextern.

load(":static_archives.bzl", "static_archives")

package(default_visibility = ["//visibility:public"])

# Emgu's C API over the OmniEngine, with the models it can load (as //omniextern:omniextern).
cc_library(
    name = "omniextern_lib",
    srcs = [
        "//omniextern:omniextern.cc",
        "//omniextern:omniextern.h",
    ],
    copts = ["-fvisibility=hidden"],
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
    alwayslink = True,
)

# The Rust components the C API links, which cc_shared_library doesn't (see static_archives.bzl).
static_archives(
    name = "engine_archives",
    dep = "//c:engine",
    path_contains = [
        "/crate_index__",
        "/rustlib/",
        "/runtime/components/rust/",
        "/runtime/components/tool_use/rust/",
        "/runtime/components/tool_use/antlr/",
        "/tokenizers_cpp/",
        "/allocator_library/",
    ],
)

filegroup(
    name = "engine_archives_filelist",
    srcs = [":engine_archives"],
    output_group = "filelist",
)

cc_shared_library(
    name = "litert_lm",
    additional_linker_inputs = [
        ":engine_archives",
        ":engine_archives_filelist",
        ":litert_lm.lds",
    ],
    # What both libraries use is exported from here, so libomniextern.so doesn't link a second copy - except protobuf
    # (see litert_lm.lds), which each library links itself.
    exports_filter = [
        "//:__subpackages__",
        "@com_google_absl//:__subpackages__",
    ],
    shared_lib_name = "liblitert-lm.so",
    target_compatible_with = ["@platforms//os:android"],
    user_link_flags = [
        "-Wl,--gc-sections",
        "-Wl,-Bsymbolic",
        "-Wl,-rpath,$ORIGIN",
        "-Wl,--version-script=$(location :litert_lm.lds)",
        # The archives' paths, one per line (too many for $(locations)).
        "-Wl,@$(location :engine_archives_filelist)",
    ],
    deps = ["//c:engine"],
)

cc_shared_library(
    name = "omniextern",
    dynamic_deps = [":litert_lm"],
    additional_linker_inputs = [":omniextern.lds"],
    shared_lib_name = "libomniextern.so",
    target_compatible_with = ["@platforms//os:android"],
    user_link_flags = [
        "-Wl,--gc-sections",
        "-Wl,-Bsymbolic",
        # ($ORIGIN, not $$ORIGIN: user_link_flags aren't make-expanded.)
        "-Wl,-rpath,$ORIGIN",
        # Only its C API: the runtime it uses is liblitert-lm.so's.
        "-Wl,--version-script=$(location :omniextern.lds)",
    ],
    deps = [":omniextern_lib"],
)
