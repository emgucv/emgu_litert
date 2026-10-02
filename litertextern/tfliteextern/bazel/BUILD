# Bazel target for building tfliteextern against LiteRT's Bazel path.
# Wired into platforms/windows/bazel_build_litert.ps1 (Windows),
# platforms/macos/bazel_build_tflite_macos (macOS),
# platforms/ubuntu/24.04/bazel_build_tflite (Ubuntu), and
# platforms/debian/{bookworm,trixie}/bazel_build_tflite (Debian arm/arm64).
#
# litert is a pristine third-party submodule (we don't own/patch it
# upstream), so this package can't live there in git - the build scripts
# stage this BUILD file plus ../tfliteextern.cc/.h and ../imgproc.cc/.h into
# a `litert/tfliteextern/` directory inside the litert submodule checkout on
# every run (Bazel needs everything in one workspace).
#
#   git checkout v2.2.0            # NOT the bleeding-edge main branch commit
#                                   # this submodule is pinned to - that commit
#                                   # (5bb05f75b at time of writing) hits a
#                                   # WORKSPACE cycle: "Failed to load Starlark
#                                   # extension '@@rules_ml_toolchain//py:...'"
#                                   # before any of our code is even touched.
#   bazel build --config=windows //tfliteextern:tfliteextern       # Windows
#   bazel build --config=macos_arm64 //tfliteextern:tfliteextern   # macOS arm64
#
# Do NOT set USE_LOCAL_TF/TF_LOCAL_SOURCE_PATH to reuse the emgutf tensorflow/
# submodule here - v2.2.0's PATCH.flatbuffers_windows_no_bash patch does not
# cleanly apply against that submodule's (newer/different) TF snapshot. Let
# Bazel fetch its own pinned TF archive instead (the default) - it works, at
# the cost of a second, separate TF source download alongside the submodule.
#
# The additional_linker_inputs/linkopts /DEF: trick on Windows is required:
# without an explicit .def file, dumpbin shows some exports (e.g.
# tfeGpuDelegateV2Create, tfeTensorGetType) aliased to unrelated internal
# symbol names. That's actually harmless - LiteRT's .bazelrc enables
# /OPT:ICF (identical COMDAT folding), which merges our several trivial
# "return 0" stub functions (XNNPACK/GPU/NNAPI delegate creation compiles to
# that when disabled/non-Android) with unrelated functions that happen to
# compile to identical bytes; the export table is still correct and callable
# either way (verified via a passing test run against the resulting DLL) -
# the explicit .def just keeps dumpbin's display from being confusing. Other
# platforms export every non-static symbol from a shared object by default,
# so no .def/version-script equivalent is needed there.
#
# Verified (Windows x64): built cleanly (2121 actions, ~3 min incremental)
# and passed the full Emgu.TF.Lite.Test.Net suite (including real MobileNet
# inference) against the resulting tfliteextern.dll.

package(default_visibility = ["//visibility:public"])

load(
    "//tflite:build_def.bzl",
    "tflite_copts",
)
load(
    # tflite/ios/ has no BUILD file of its own (only BUILD.apple) so it isn't a package -
    # ios.bzl is addressed as a file within the parent //tflite package instead.
    "//tflite:ios/ios.bzl",
    "TFL_MINIMUM_OS_VERSION",
)
load(
    "@build_bazel_rules_apple//apple:ios.bzl",
    "ios_static_framework",
)

_TFLITEEXTERN_SRCS = [
    "tfliteextern.cc",
    "tfliteextern.h",
    "imgproc.cc",
    "imgproc.h",
]

# tfliteextern only uses the TensorFlow Lite C API (plus the model schema header, for model introspection).
# //tflite/c:c_api includes the builtin op resolver.
_TFLITEEXTERN_CAPI_DEPS = [
    "//tflite/c:c_api",
    "//tflite/c:c_api_experimental",
    "//tflite/c:common",
    "//tflite/schema:schema_fbs",
]

_TFLITEEXTERN_DEPS = _TFLITEEXTERN_CAPI_DEPS + select({
    # tfliteextern.cc only includes the NNAPI and GPU delegate headers under #ifdef __ANDROID__ - both
    # delegates are Android-only (the GPU delegate needs EGL; see the "supports_gpu_delegate" comment in
    # tflite/delegates/gpu/BUILD), so these deps are too.
    "@org_tensorflow//tensorflow:android": [
        "//tflite/nnapi:nnapi_lib",
        "//tflite/delegates/nnapi:nnapi_delegate",
        "//tflite/delegates/gpu:delegate",
        "//tflite/delegates/gpu:delegate_options",
    ],
    "//conditions:default": [],
})

cc_binary(
    name = "tfliteextern",
    srcs = _TFLITEEXTERN_SRCS,
    copts = tflite_copts() + ["-DWITHOUT_XNNPACK"],
    additional_linker_inputs = select({
        "@bazel_tools//src/conditions:windows": ["tfliteextern.def"],
        "//conditions:default": [],
    }),
    linkopts = select({
        "@bazel_tools//src/conditions:windows": ["/DEF:$(location tfliteextern.def)"],
        "//conditions:default": [],
    }),
    linkshared = 1,
    linkstatic = 1,
    deps = _TFLITEEXTERN_DEPS,
)

# Static-library counterpart of the ":tfliteextern" shared library above, for platforms
# (iOS device/simulator, Mac Catalyst) where the C# side statically links tfliteextern
# into the app instead of loading a shared object - DllImport("tfliteextern") resolves to
# "__Internal" there (see Emgu.TF.Runtime/Maui/iOS's NativeReference), so the exported tfe*
# symbols just need to be present in the final linked binary, not in a standalone .dylib/.so.
cc_library(
    name = "tfliteextern_lib",
    srcs = _TFLITEEXTERN_SRCS,
    copts = tflite_copts() + ["-DWITHOUT_XNNPACK"],
    deps = _TFLITEEXTERN_DEPS,
)

# ---------------------------------------------------------------------------------------------------------
# macOS and Android: tfliteextern linked dynamically against LiteRT's own runtime library.
# (Android: ":litert_runtime_so"/":tfliteextern_so", producing libLiteRt.so/libtfliteextern.so, same idea as the
# macOS targets described below.)
#
# ":litert_runtime" builds libLiteRt.dylib from LiteRT's //litert/c:litert_tflite_runtime_c_api_so_shim (the
# same deps as LiteRT's own //litert/c:litert_tflite_runtime_c_api_so), exporting LiteRT's C API plus the
# TensorFlow Lite C API. It is defined here rather than using that target directly because the upstream one
# (a) sets no macOS install name and (b) doesn't link Metal: LiteRT's .bazelrc builds macOS with
# -undefined dynamic_lookup, so its MTLCreateSystemDefaultDevice reference would otherwise be left to
# resolve at runtime only if something else happened to load Metal.
#
# ":tfliteextern_dylib" builds libtfliteextern.dylib from the same sources as ":tfliteextern", but via
# dynamic_deps it links against ":litert_runtime" instead of statically including the TFLite runtime: every
# library tfliteextern needs is already linked into libLiteRt.dylib (exports_filter tells Bazel they may be
# shared from there), so libtfliteextern.dylib only contains the wrapper itself. XNNPACK is part of
# libLiteRt, so unlike the static builds it isn't compiled out (no -DWITHOUT_XNNPACK).
#
# bazel build --config=macos --config=macos_arm64 //tfliteextern:litert_runtime //tfliteextern:tfliteextern_dylib
# ---------------------------------------------------------------------------------------------------------
cc_shared_library(
    name = "litert_runtime",
    additional_linker_inputs = ["litert_exported_symbols_darwin.lds"],
    exports_filter = [
        "//tflite:__subpackages__",
        "@flatbuffers//:__subpackages__",
        "@XNNPACK//:__subpackages__",
    ],
    shared_lib_name = "libLiteRt.dylib",
    target_compatible_with = ["@platforms//os:macos"],
    user_link_flags = [
        "-Wl,-exported_symbols_list,$(location litert_exported_symbols_darwin.lds)",
        "-Wl,-install_name,@rpath/libLiteRt.dylib",
        "-Wl,-framework,Metal",
    ],
    deps = ["//litert/c:litert_tflite_runtime_c_api_so_shim"],
)

cc_shared_library(
    name = "litert_runtime_so",
    additional_linker_inputs = ["litert_exported_symbols_linux.lds"],
    exports_filter = [
        "//tflite:__subpackages__",
        "@flatbuffers//:__subpackages__",
        "@XNNPACK//:__subpackages__",
    ],
    shared_lib_name = "libLiteRt.so",
    target_compatible_with = ["@platforms//os:android"],
    user_link_flags = [
        "-Wl,--version-script,$(location litert_exported_symbols_linux.lds)",
        # LiteRT's own litert_tflite_runtime_c_api_so sets no SONAME, so libtfliteextern.so would record the
        # Bazel output path of libLiteRt.so as its DT_NEEDED entry instead of the plain file name.
        "-Wl,-soname,libLiteRt.so",
        "-landroid",
    ],
    deps = ["//litert/c:litert_tflite_runtime_c_api_so_shim"],
)

# The C API wrapper shared by the dynamically linked macOS and Android builds. On Android, NNAPI comes from its C
# API in libLiteRt.so, and the TFLite GPU delegate is compiled out (WITHOUT_GPU_DELEGATE): libLiteRt.so doesn't
# contain it, and it needs TFLite internals libLiteRt.so doesn't export.
cc_library(
    name = "tfliteextern_capi_lib",
    srcs = _TFLITEEXTERN_SRCS,
    copts = tflite_copts() + select({
        "@org_tensorflow//tensorflow:android": ["-DWITHOUT_GPU_DELEGATE"],
        "//conditions:default": [],
    }),
    target_compatible_with = select({
        "@platforms//os:macos": [],
        "@platforms//os:android": [],
        "//conditions:default": ["@platforms//:incompatible"],
    }),
    deps = _TFLITEEXTERN_CAPI_DEPS + ["//tflite/delegates/xnnpack:xnnpack_delegate"] + select({
        "@org_tensorflow//tensorflow:android": ["//tflite/delegates/nnapi:nnapi_delegate"],
        "//conditions:default": [],
    }),
)

cc_shared_library(
    name = "tfliteextern_dylib",
    dynamic_deps = [":litert_runtime"],
    shared_lib_name = "libtfliteextern.dylib",
    target_compatible_with = ["@platforms//os:macos"],
    user_link_flags = [
        "-Wl,-install_name,@rpath/libtfliteextern.dylib",
        # libLiteRt.dylib is deployed next to libtfliteextern.dylib.
        "-Wl,-rpath,@loader_path",
    ],
    deps = [":tfliteextern_capi_lib"],
)

cc_shared_library(
    name = "tfliteextern_so",
    dynamic_deps = [":litert_runtime_so"],
    shared_lib_name = "libtfliteextern.so",
    target_compatible_with = ["@platforms//os:android"],
    user_link_flags = ["-Wl,-soname,libtfliteextern.so"],
    deps = [":tfliteextern_capi_lib"],
)

# bazel build --config=ios_arm64 //tfliteextern:tfliteextern_ios_framework      # device
# bazel build --config=ios_sim_arm64 //tfliteextern:tfliteextern_ios_framework  # simulator (Apple Silicon)
# bazel build --apple_platform_type=macos --cpu=darwin_arm64 //tfliteextern:tfliteextern_ios_framework  # Mac Catalyst
#
# No symbol-hiding/allowlist step (unlike litert's own TensorFlowLiteC_framework, which
# wraps this in extract_object_files/hide_symbols_with_allowlist) - the tfe* symbols must
# stay visible since they're exactly what TfLiteInvoke's DllImport resolves against.
#
# No hdrs either: the C# side only ever needs the raw static binary's exported tfe*
# symbols (via DllImport("tfliteextern") -> "__Internal"), never an Objective-C-visible
# public header - and ios_static_framework's umbrella header (named "<bundle_name>.h",
# i.e. "tfliteextern.h") collides with our own tfliteextern.h if it's listed as a header.
ios_static_framework(
    name = "tfliteextern_ios_framework",
    bundle_name = "tfliteextern",
    minimum_os_version = TFL_MINIMUM_OS_VERSION,
    deps = [":tfliteextern_lib"],
)
