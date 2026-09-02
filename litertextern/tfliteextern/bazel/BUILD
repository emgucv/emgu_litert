# Bazel target for building tfliteextern against LiteRT's Bazel path.
# Wired into platforms/windows/bazel_build_tflite_x86_64.bat (Windows) and
# platforms/macos/bazel_build_tflite_macos (macOS). Not wired into a Linux
# build script yet.
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

cc_binary(
    name = "tfliteextern",
    srcs = [
        "tfliteextern.cc",
        "tfliteextern.h",
        "imgproc.cc",
        "imgproc.h",
    ],
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
    deps = [
        "//tflite:framework",
        "//tflite:context",
        "//tflite:string",
        "//tflite:string_util",
        "//tflite:schema_fbs_version",
        "//tflite/kernels:builtin_ops",
        "//tflite/nnapi:nnapi_lib",
        "//tflite/delegates/nnapi:nnapi_delegate",
    ],
)
