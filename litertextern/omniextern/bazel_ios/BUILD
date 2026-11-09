# iOS: LiteRtLm.framework and omniextern.framework from one LiteRT-LM runtime. platforms/ios/bazel_build_litert_lm_ios
# stages this package into the LiteRT-LM workspace as omniextern_ios/, with static_archives.bzl, next to omniextern/ (whose
# omniextern.cc and omniextern.h it compiles).
#
# Built as two linkshared cc_binaries (LiteRT-LM's //c:litert-lm and //omniextern:omniextern), each framework carried
# its own static copy of the runtime: omniextern duplicated most of LiteRtLm (22.5 MB against 18.6 MB), and both
# registered miniaudio's Objective-C class ma_ios_notification_handler, which iOS warns about at launch ("may cause
# spurious casting failures and mysterious crashes"). Here LiteRtLm is a cc_shared_library of LiteRT-LM's C API
# (//c:engine, what //c:litert-lm is built from) that exports the runtime, and omniextern links against it
# (dynamic_deps), so each library is linked once. The other platforms still build //omniextern:omniextern.

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

# miniaudio, an objc_library on Apple platforms, which cc_shared_library doesn't link (see static_archives.bzl): linked
# into LiteRtLm whole, which exports it to omniextern.
filegroup(
    name = "miniaudio_archive",
    srcs = ["@miniaudio//:miniaudio_objc"],
)

# The Rust components and TFLite's signpost profiler the C API links, which cc_shared_library doesn't.
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
        "/libsignpost_profiler.a",
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
        ":miniaudio_archive",
    ],
    # Everything both libraries use is exported from here, so omniextern doesn't link a second copy.
    exports_filter = [
        "//:__subpackages__",
        "@com_google_absl//:__subpackages__",
        "@com_google_protobuf//:__subpackages__",
    ],
    shared_lib_name = "libLiteRtLm.dylib",
    target_compatible_with = ["@platforms//os:ios"],
    user_link_flags = [
        "-Wl,-install_name,@rpath/LiteRtLm.framework/LiteRtLm",
        "-Wl,-force_load,$(location :miniaudio_archive)",
        "-Wl,-filelist,$(location :engine_archives_filelist)",
        "-Wl,-framework,AudioToolbox",
        "-Wl,-framework,AVFoundation",
    ],
    deps = ["//c:engine"],
)

cc_shared_library(
    name = "omniextern",
    dynamic_deps = [":litert_lm"],
    shared_lib_name = "libomniextern.dylib",
    target_compatible_with = ["@platforms//os:ios"],
    user_link_flags = [
        "-Wl,-install_name,@rpath/omniextern.framework/omniextern",
        # Only its C API: the runtime it uses is LiteRtLm's.
        "-Wl,-exported_symbol,_omniextern_*",
    ],
    deps = [":omniextern_lib"],
)
