# ----------------------------------------------------------------------------
#
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#
# ----------------------------------------------------------------------------
#
# LiteRT-backed build of tfliteextern (Windows x64).
# Links against Google's google-ai-edge/LiteRT `tflite/` tree (litert/
# submodule). No tensorflow/ submodule is used or required: litert/tflite's
# own CMakeLists.txt downloads the TF source it additionally needs (e.g.
# MLIR/compiler files) itself via FetchContent when TENSORFLOW_SOURCE_DIR
# isn't set, which is the case here.

cmake_minimum_required(VERSION 3.16)

# Declared before anything that needs a compiler (the Mac Catalyst check below, litert's subdirectories), so C/C++
# are enabled up front instead of only once a subdirectory's own project() runs.
PROJECT(tfliteextern)

SET(CMAKE_CXX_STANDARD 20)

SET(CMAKE_MODULE_PATH ${CMAKE_MODULE_PATH} "${CMAKE_CURRENT_SOURCE_DIR}/../../cmake/modules")

GET_FILENAME_COMPONENT(LITERT_SOURCE_DIR_ABS "${CMAKE_CURRENT_SOURCE_DIR}/../../litert" ABSOLUTE)
SET(LITERT_SOURCE_DIR "${LITERT_SOURCE_DIR_ABS}")
SET(CPACK_PACKAGE_VENDOR "Emgu Corporation")
INCLUDE (LitertVersion)
SET(CPACK_PACKAGE_VERSION_MAJOR ${LITERT_VERSION_MAJOR})
SET(CPACK_PACKAGE_VERSION_MINOR ${LITERT_VERSION_MINOR})
SET(CPACK_PACKAGE_VERSION_PATCH ${LITERT_VERSION_PATCH})

# TFLITEEXTERN_LINK_LITERT: build LiteRT's own CMake project (litert/litert, which adds litert/tflite itself)
# and link tfliteextern dynamically against its libLiteRt shared library, instead of statically linking the
# TFLite runtime into tfliteextern - the same split the macOS/Android Bazel builds use. libLiteRt exports the
# TfLite* C API tfliteextern needs (on Apple, LiteRT's CMake links it with -all_load and no export list) as
# well as the LiteRt* C API Emgu.LiteRT P/Invokes directly, so one runtime serves both. On by default for
# macOS only; the Windows/Linux CMake builds keep the statically linked tfliteextern.
IF(APPLE AND NOT IOS)
  SET(TFLITEEXTERN_LINK_LITERT_DEFAULT ON)
ELSE()
  SET(TFLITEEXTERN_LINK_LITERT_DEFAULT OFF)
ENDIF()
OPTION(TFLITEEXTERN_LINK_LITERT "Link tfliteextern dynamically against LiteRT's libLiteRt shared library" ${TFLITEEXTERN_LINK_LITERT_DEFAULT})

# Mac Catalyst (clang's "-target <arch>-apple-ios<version>-macabi", see platforms/ios/cmake_build_tflite_catalyst)
# is CMAKE_SYSTEM_NAME Darwin to CMake, but TARGET_OS_IOS to the compiler. litert/tflite's
# profiling/platform_profiler.cc calls MaybeCreateSignpostProfiler() whenever TARGET_OS_IOS, while its
# CMakeLists.txt only compiles profiling/signpost_profiler.mm (which defines it) when CMAKE_SYSTEM_NAME is iOS,
# so a Catalyst link fails on that undefined symbol. litert is a pristine submodule we don't patch, so the file
# is added to tensorflow-lite below, with the same -fno-objc-arc it gets on iOS. Objective-C++ has to be
# enabled before litert/tflite's directory is added, or that directory lacks the OBJCXX compile rules.
IF(APPLE)
  INCLUDE(CheckCXXSourceCompiles)
  CHECK_CXX_SOURCE_COMPILES("
    #include <TargetConditionals.h>
    #if !TARGET_OS_MACCATALYST
    #error not Mac Catalyst
    #endif
    int main() { return 0; }" TFLITEEXTERN_TARGET_MACCATALYST)
  IF(TFLITEEXTERN_TARGET_MACCATALYST)
    enable_language(OBJCXX)
  ENDIF()
ENDIF()

# The TFLite version (TfLiteVersion(), TFLITE_VERSION_STRING) is TensorFlow's version. Bazel passes it to
# litert/tflite/version.h as TF_MAJOR_VERSION etc., but LiteRT's CMake doesn't, so version.h would fall back to its
# hardcoded (stale) default. Define them here, before LiteRT's directories are added so they inherit them, from the
# TensorFlow source LiteRT's CMake builds against: TENSORFLOW_SOURCE_DIR's tf_version.bzl if one is given, else the
# GIT_TAG litert/tflite/CMakeLists.txt downloads. tflite_version.txt below records it for the package versions.
IF(TENSORFLOW_SOURCE_DIR AND EXISTS "${TENSORFLOW_SOURCE_DIR}/tensorflow/tf_version.bzl")
  INCLUDE(TensorflowVersion)
  SET(TFLITEEXTERN_TF_VERSION_SUFFIX "")
ELSE()
  FILE(STRINGS "${LITERT_SOURCE_DIR_ABS}/tflite/CMakeLists.txt" TFLITEEXTERN_TF_GIT_TAG REGEX "GIT_TAG[ ]+v[0-9]+\\.[0-9]+\\.[0-9]+")
  STRING(REGEX MATCH "v([0-9]+)\\.([0-9]+)\\.([0-9]+)([-a-z0-9]*)" TFLITEEXTERN_TF_TAG_MATCH "${TFLITEEXTERN_TF_GIT_TAG}")
  SET(TENSORFLOW_VERSION_MAJOR "${CMAKE_MATCH_1}")
  SET(TENSORFLOW_VERSION_MINOR "${CMAKE_MATCH_2}")
  SET(TENSORFLOW_VERSION_PATCH "${CMAKE_MATCH_3}")
  SET(TFLITEEXTERN_TF_VERSION_SUFFIX "${CMAKE_MATCH_4}")
ENDIF()
IF(TENSORFLOW_VERSION_MAJOR STREQUAL "")
  MESSAGE(WARNING "Could not determine the TensorFlow version; TfLiteVersion() falls back to litert/tflite/version.h's default")
ELSE()
  SET(TFLITEEXTERN_TFLITE_VERSION "${TENSORFLOW_VERSION_MAJOR}.${TENSORFLOW_VERSION_MINOR}.${TENSORFLOW_VERSION_PATCH}")
  MESSAGE(STATUS "TFLite (TensorFlow) version: ${TFLITEEXTERN_TFLITE_VERSION}${TFLITEEXTERN_TF_VERSION_SUFFIX}")
  ADD_COMPILE_DEFINITIONS(
    TF_MAJOR_VERSION=${TENSORFLOW_VERSION_MAJOR}
    TF_MINOR_VERSION=${TENSORFLOW_VERSION_MINOR}
    TF_PATCH_VERSION=${TENSORFLOW_VERSION_PATCH}
    "TF_VERSION_SUFFIX=\"${TFLITEEXTERN_TF_VERSION_SUFFIX}\"")
ENDIF()

IF(TFLITEEXTERN_LINK_LITERT)
  ADD_SUBDIRECTORY(${LITERT_SOURCE_DIR_ABS}/litert ${CMAKE_CURRENT_BINARY_DIR}/litert)
ELSE()
  ADD_SUBDIRECTORY(${LITERT_SOURCE_DIR_ABS}/tflite ${CMAKE_CURRENT_BINARY_DIR}/tflite)
ENDIF()

IF(TFLITEEXTERN_TARGET_MACCATALYST AND TARGET tensorflow-lite)
  target_sources(tensorflow-lite PRIVATE ${LITERT_SOURCE_DIR_ABS}/tflite/profiling/signpost_profiler.mm)
  set_source_files_properties(${LITERT_SOURCE_DIR_ABS}/tflite/profiling/signpost_profiler.mm
    TARGET_DIRECTORY tensorflow-lite PROPERTIES COMPILE_FLAGS -fno-objc-arc)
ENDIF()



# litert/tflite/CMakeLists.txt unconditionally add_subdirectory()s profiling/proto and
# tools/benchmark/proto, whose add_custom_command()s pass protoc a
# --proto_path=${TENSORFLOW_SOURCE_DIR} - the root of the tensorflow source tree litert's
# own CMake FetchContent downloads for unrelated needs (see the file header above) - even
# though the .proto files they're compiling actually live under litert/tflite/, a different
# tree entirely. protoc requires --proto_path to be an exact prefix of the file being
# compiled, so this always fails: "File does not reside within any path specified using
# --proto_path". litert is a pristine submodule we don't patch upstream, and none of these
# targets are a dependency of tfliteextern (verified: `cmake --build . --target tfliteextern`
# succeeds without them), so exclude them from the default `all` target instead - that's
# what actually breaks a plain `cmake --build .` otherwise, even though our own target
# already builds fine either way.
FOREACH(BROKEN_PROTO_TARGET profiling_info_proto model_runtime_info_proto benchmark_result_proto)
  IF(TARGET ${BROKEN_PROTO_TARGET})
    SET_TARGET_PROPERTIES(${BROKEN_PROTO_TARGET} PROPERTIES EXCLUDE_FROM_ALL TRUE)
  ENDIF()
ENDFOREACH()

INCLUDE(Today)
TODAY() #set the variable CURRENT_YEAR

# ----------------------------------------------------------------------------
#  Autodetect if we are in a GIT repository
# ----------------------------------------------------------------------------
FIND_PROGRAM(GIT_PATH git)
FIND_PROGRAM(WC_PATH wc)
MARK_AS_ADVANCED(force GIT_PATH WC_PATH)
IF(GIT_PATH AND WC_PATH)
  MESSAGE(STATUS "Extracting Tensorflow git version, please wait...")
  EXECUTE_PROCESS(
    WORKING_DIRECTORY  ${CMAKE_CURRENT_SOURCE_DIR}
    COMMAND ${GIT_PATH} log --oneline
    COMMAND ${WC_PATH} -l
    OUTPUT_VARIABLE GITVERSION_RESULT)
  STRING(REGEX REPLACE "^([0123456789]+).*" "\\1" TF_GITVERSION "${GITVERSION_RESULT}")
  STRING(STRIP "${TF_GITVERSION}" TF_GITVERSION)
  IF(TF_GITVERSION MATCHES "^$" OR TF_GITVERSION MATCHES "^-")
    # This is NOT a git repository:
    SET(TF_GITVERSION "0")
  ENDIF()
  MESSAGE(STATUS "TF GIT VERSION: ${TF_GITVERSION}")
ELSE()
  # We don't have git or wc:
  SET(TF_GITVERSION "0")
ENDIF()

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/version.rc.in ${CMAKE_CURRENT_SOURCE_DIR}/version.rc)



INCLUDE_DIRECTORIES("${CMAKE_CURRENT_SOURCE_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}/../imgproc" "${LITERT_SOURCE_DIR_ABS}")

SET(TFLITEEXTERN_SOURCES tfliteextern.cc ${CMAKE_CURRENT_SOURCE_DIR}/../imgproc/imgproc.cc)
SET(TFLITEEXTERN_HDRS tfliteextern.h ${CMAKE_CURRENT_SOURCE_DIR}/../imgproc/imgproc.h)
IF (WIN32)
  SET(TFLITEEXTERN_VER_RC ${CMAKE_CURRENT_SOURCE_DIR}/version.rc)
  IF(MSVC)
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
      # 64 bits
      link_directories(${CMAKE_CURRENT_SOURCE_DIR}/link_directory/x64)
    elseif(CMAKE_SIZEOF_VOID_P EQUAL 4)
      # 32 bits
      link_directories(${CMAKE_CURRENT_SOURCE_DIR}/link_directory/x86)
    endif()
  ENDIF()
ENDIF()
add_library(tfliteextern SHARED ${TFLITEEXTERN_HDRS} ${TFLITEEXTERN_SOURCES} ${TFLITEEXTERN_VER_RC})


# TF_MAJOR_VERSION etc. are defined before LiteRT's directories are added, see above.

IF(TFLITEEXTERN_LINK_LITERT)
  # litert_runtime_c_api_shared_lib (libLiteRt) carries the TFLite/TensorFlow include directories as PUBLIC
  # properties but no link interface. tfliteextern.cc also includes the model schema header, which needs the
  # flatbuffers headers (header-only use, nothing from libflatbuffers is linked).
  target_link_libraries(tfliteextern litert_runtime_c_api_shared_lib)
  target_include_directories(tfliteextern PRIVATE $<TARGET_PROPERTY:flatbuffers,INTERFACE_INCLUDE_DIRECTORIES>)
ELSE()
  target_link_libraries(tfliteextern
    tensorflow-lite
  )
  ADD_DEPENDENCIES(tfliteextern tensorflow-lite)
ENDIF()

IF(TFLITE_ENABLE_XNNPACK)
ELSE()
  add_definitions(-DWITHOUT_XNNPACK)
ENDIF()

#Check if ARM specific compiler predefined marco exist
SET(IS_ARM)
check_symbol_exists("__arm__" "" IS_ARM)
IF(IS_ARM)
  SET(TARGET_ARM TRUE)
ENDIF()

#Check if ARM64 specific compiler predefined marco exist
SET(IS_ARM64)
check_symbol_exists("__aarch64__" "" IS_ARM64)
IF(IS_ARM64)
  SET(TARGET_ARM TRUE)
  SET(TARGET_ARCH_64 TRUE)
ENDIF()

IF(MSVC AND CMAKE_CL_64)
  SET(TARGET_ARCH_64 TRUE)
ELSE()
  IF((CMAKE_SIZEOF_VOID_P EQUAL 8) AND NOT(APPLE AND CMAKE_OSX_ARCHITECTURES MATCHES "i386"))
    SET(TARGET_ARCH_64 TRUE)
  ENDIF()
ENDIF()

IF(WIN32)
  IF (IS_ARM64)
    SET(BINARY_OUTPUT_SUBFOLDER "runtimes/win-arm64/native")
  ELSEIF (TARGET_ARCH_64)
    SET(BINARY_OUTPUT_SUBFOLDER "runtimes/win-x64/native")
  ELSE()
    SET(BINARY_OUTPUT_SUBFOLDER "runtimes/win-x86/native")
  ENDIF()
ELSEIF(APPLE AND NOT IOS)
  # EMGUCV_ARCH (Emgu.CV's own convention) is never set when building tfliteextern
  # standalone - unlike the WIN32/Linux branches above/below, this used to always fall
  # into the ELSE() below, mislabeling arm64 Macs as "x64". Use the same self-detected
  # IS_ARM64 (via check_symbol_exists("__aarch64__", ...) above) they already use instead.
  IF (IS_ARM64)
    SET(BINARY_OUTPUT_SUBFOLDER "/runtimes/osx/native/arm64")
  ELSE()
    SET(BINARY_OUTPUT_SUBFOLDER "/runtimes/osx/native/x64")
  ENDIF()
ELSEIF("${EMGUCV_ARCH}" STREQUAL "ubuntu_x64")
  SET(BINARY_OUTPUT_SUBFOLDER "/runtimes/ubuntu-x64/native")
ELSEIF("${EMGUCV_ARCH}" STREQUAL "ubuntu_x86")
  SET(BINARY_OUTPUT_SUBFOLDER "/runtimes/ubuntu-x86/native")
ELSEIF("${EMGUCV_ARCH}" STREQUAL "ubuntu_arm64")
  SET(BINARY_OUTPUT_SUBFOLDER "/runtimes/ubuntu-arm64/native")
ELSEIF("${EMGUCV_ARCH}" STREQUAL "ubuntu_arm")
  SET(BINARY_OUTPUT_SUBFOLDER "/runtimes/ubuntu-arm/native")
ELSEIF(IS_ARM64)
  SET(BINARY_OUTPUT_SUBFOLDER "runtimes/linux-arm64/native")
ELSEIF(IS_ARM)
  SET(BINARY_OUTPUT_SUBFOLDER "runtimes/linux-arm/native")
ELSEIF(TARGET_ARCH_64)
  SET(BINARY_OUTPUT_SUBFOLDER "runtimes/linux-x86_64/native")
ELSE()
  SET(BINARY_OUTPUT_SUBFOLDER "runtimes/linux-x86/native")
ENDIF()

IF(WIN32 AND MSVC AND (NOT NETFX_CORE))
  # Add install rules for required system runtimes such as MSVCRxx.dll
  SET (CMAKE_INSTALL_SYSTEM_RUNTIME_LIBS_SKIP ON)
  INCLUDE(InstallRequiredSystemLibraries)
  LIST(APPEND TFLITEEXTERN_DEPENDENCY_DLLS ${CMAKE_INSTALL_SYSTEM_RUNTIME_LIBS})
ENDIF()

# TFLITEEXTERN_OUTPUT_DIRECTORY can be set on the command line to override the per-platform lib/ folder - e.g.
# platforms/ios/cmake_build_tflite_catalyst, whose Mac Catalyst build would otherwise land in (and overwrite)
# the macOS folder, since CMake sees both as APPLE AND NOT IOS.
IF(NOT TFLITEEXTERN_OUTPUT_DIRECTORY)
  SET(TFLITEEXTERN_OUTPUT_DIRECTORY_RELATIVE ${CMAKE_CURRENT_SOURCE_DIR}/../../lib/${BINARY_OUTPUT_SUBFOLDER})
  GET_FILENAME_COMPONENT(TFLITEEXTERN_OUTPUT_DIRECTORY "${TFLITEEXTERN_OUTPUT_DIRECTORY_RELATIVE}" ABSOLUTE)
ENDIF()
set_target_properties( tfliteextern PROPERTIES ARCHIVE_OUTPUT_DIRECTORY ${TFLITEEXTERN_OUTPUT_DIRECTORY} )
set_target_properties( tfliteextern PROPERTIES LIBRARY_OUTPUT_DIRECTORY ${TFLITEEXTERN_OUTPUT_DIRECTORY} )
set_target_properties( tfliteextern PROPERTIES RUNTIME_OUTPUT_DIRECTORY ${TFLITEEXTERN_OUTPUT_DIRECTORY} )
set_target_properties( tfliteextern PROPERTIES RUNTIME_OUTPUT_DIRECTORY_DEBUG ${TFLITEEXTERN_OUTPUT_DIRECTORY} )
set_target_properties( tfliteextern PROPERTIES RUNTIME_OUTPUT_DIRECTORY_RELEASE ${TFLITEEXTERN_OUTPUT_DIRECTORY} )

MESSAGE(STATUS "TFLITEEXTERN_OUTPUT_DIRECTORY ${TFLITEEXTERN_OUTPUT_DIRECTORY}")

IF(TFLITEEXTERN_LINK_LITERT)
  # Deploy libLiteRt next to libtfliteextern, which finds it there via an @loader_path rpath (matching the
  # Bazel macOS build's @rpath install names), instead of the absolute build-tree rpath CMake uses by default.
  set_target_properties(litert_runtime_c_api_shared_lib PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY ${TFLITEEXTERN_OUTPUT_DIRECTORY}
    RUNTIME_OUTPUT_DIRECTORY ${TFLITEEXTERN_OUTPUT_DIRECTORY}
    INSTALL_NAME_DIR "@rpath"
    BUILD_WITH_INSTALL_NAME_DIR ON)
  set_target_properties(tfliteextern PROPERTIES
    INSTALL_NAME_DIR "@rpath"
    BUILD_WITH_INSTALL_NAME_DIR ON
    INSTALL_RPATH "@loader_path"
    BUILD_WITH_INSTALL_RPATH ON)
ENDIF()

# Same marker the Bazel build scripts write: the TFLite version, read by the top level CMakeLists.txt to version the
# Emgu.TF.Lite/Emgu.LiteRT.Tflite.* nuget packages.
IF(TFLITEEXTERN_TFLITE_VERSION)
  FILE(WRITE "${TFLITEEXTERN_OUTPUT_DIRECTORY}/tflite_version.txt" "${TFLITEEXTERN_TFLITE_VERSION}\n")
ENDIF()

IF(WIN32)
  # Same marker bazel_build_litert.ps1 writes from the tflite_with_xnnpack define,
  # read by the top level CMakeLists.txt to set EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK.
  IF(TFLITE_ENABLE_XNNPACK)
    FILE(WRITE "${TFLITEEXTERN_OUTPUT_DIRECTORY}/tflite_with_xnnpack.txt" "true\n")
  ELSE()
    FILE(WRITE "${TFLITEEXTERN_OUTPUT_DIRECTORY}/tflite_with_xnnpack.txt" "false\n")
  ENDIF()
ENDIF()

IF (MSVC)

  IF(DEFINED TFLITEEXTERN_DEPENDENCY_DLLS)
  FOREACH(TFLITEEXTERN_DEPENDENCY_DLL ${TFLITEEXTERN_DEPENDENCY_DLLS})
    LIST(APPEND TFLITEEXTERN_DEPENDENCY_DLL_DEPLOY_COMMAND COMMAND ${CMAKE_COMMAND} -E copy "${TFLITEEXTERN_DEPENDENCY_DLL}" "${TFLITEEXTERN_OUTPUT_DIRECTORY}")
  ENDFOREACH()

  ADD_CUSTOM_COMMAND(
    TARGET tfliteextern
    POST_BUILD
    ${TFLITEEXTERN_DEPENDENCY_DLL_DEPLOY_COMMAND}
    COMMENT "Copying ${TFLITEEXTERN_DEPENDENCY_DLLS} to ${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")
ENDIF()

ENDIF()
