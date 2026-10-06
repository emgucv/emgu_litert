# --------------------------------------------------------
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# 
# Root CMake file for Emgu TF
#
# From the off-tree build directory, invoke:
#   $ cmake <PATH_TO_EMGUTF_ROOT>
#
# --------------------------------------------------------

CMAKE_MINIMUM_REQUIRED(VERSION 3.16)

SET(EMGU_TF_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")

PROJECT(emgutf)

# ----------------------------------------------------------------------------
#  Autodetect if we are in a GIT repository
# ----------------------------------------------------------------------------
FIND_PROGRAM(GIT_PATH git)
FIND_PROGRAM(WC_PATH wc)
MARK_AS_ADVANCED(force GIT_PATH WC_PATH)
IF(GIT_PATH AND WC_PATH)
  MESSAGE(STATUS "Extracting Emgu TF git version, please wait...")
  EXECUTE_PROCESS(
    WORKING_DIRECTORY  ${CMAKE_CURRENT_SOURCE_DIR}
    COMMAND ${GIT_PATH} log --oneline 
    COMMAND ${WC_PATH} -l
    OUTPUT_VARIABLE GITVERSION_RESULT)
  #MESSAGE(STATUS "COMMAND: ${GIT_PATH} log --oneline | ${WC_PATH} -l")
  STRING(REGEX REPLACE "^([0123456789]+).*" "\\1" EMGUTF_GITVERSION "${GITVERSION_RESULT}")
  STRING(STRIP "${EMGUTF_GITVERSION}" EMGUTF_GITVERSION)
  IF(EMGUTF_GITVERSION MATCHES "^$" OR EMGUTF_GITVERSION MATCHES "^-")
    # This is NOT a git repository:
    SET(EMGUTF_GITVERSION "0")
  ENDIF()
  MESSAGE(STATUS "Emgu TF GIT VERSION: ${EMGUTF_GITVERSION}")
ELSE()
  # We don't have git or wc:
  SET(EMGUTF_GITVERSION "0")
ENDIF()
SET(LITERT_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/litert")
SET(CMAKE_MODULE_PATH ${CMAKE_MODULE_PATH} "${CMAKE_CURRENT_SOURCE_DIR}/cmake/modules")

SET(CSC_PREFERRED_VERSION "16.0")
SET(CSC_TARGET_FRAMEWORK "" CACHE STRING "C# target framework")

#SET(CSC_PREFERRED_VERSION "4.0")
#IF((NOT NETFX_CORE) AND MSVC) 
#  #SET CSC 3.5 as the prefered compiler for EMGU TF
#  SET(CSC_TARGET_FRAMEWORK "3.5" CACHE STRING "C#  target framework")
#ELSE()
#  #SET CSC 4.5 (in the .Net framework 4.0 folder) as the prefered compiler for EMGU TF
#  SET(CSC_TARGET_FRAMEWORK "" CACHE STRING "C# target framework")
#ENDIF()

FIND_PACKAGE(CSharp REQUIRED)
MESSAGE(STATUS "DOTNET_FOUND: ${DOTNET_FOUND}")
INCLUDE (CheckPlatform)
IF ( (IS_UBUNTU OR IS_DEBIAN) AND NOT (DOTNET_FOUND))
  MESSAGE(FATAL_ERROR "dotnet is required to build Emgu TF, please follow the instructions to install dotnet: https://dotnet.microsoft.com/download/dotnet")  
ENDIF()
#MESSAGE(STATUS "IS_UBUNTU: ${IS_UBUNTU}")
INCLUDE (CSharpMacros)
INCLUDE (LitertVersion)
MESSAGE(STATUS "LITERT_VERSION: ${LITERT_VERSION} ")

SET(LICENSE_FILE_NAME "LICENSE")
MACRO(CHECK_LICENSE_TYPE)
  MESSAGE(STATUS "LICENSE FILE: ${CMAKE_CURRENT_SOURCE_DIR}/${LICENSE_FILE_NAME}")
  IF (WIN32)
    EXECUTE_PROCESS(COMMAND "findstr" "gpl" "${LICENSE_FILE_NAME}" WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} OUTPUT_VARIABLE TMP)
  ELSE()	
    EXECUTE_PROCESS(COMMAND "grep" "gpl" "${LICENSE_FILE_NAME}" WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} OUTPUT_VARIABLE TMP)
  ENDIF()
  
  STRING(STRIP "${TMP}" TMP)
  STRING(LENGTH "${TMP}" GPL_LICENSE_LENGTH)
  SET (LICENSE_TYPE "Dual License")
  IF(${GPL_LICENSE_LENGTH} EQUAL 0)
    SET(LICENSE_TYPE "Commercial")
  ENDIF()
  
  MESSAGE(STATUS "LICENSE_TYPE: ${LICENSE_TYPE}")
ENDMACRO()

CHECK_LICENSE_TYPE()
SET(NUGET_EMGU_URL "https://www.emgu.com")
SET(EMGUTF_MAIN_WEB_URL "${NUGET_EMGU_URL}/wiki/index.php/Emgu_TF")


IF(MSVC)
  set(ENABLE_SOLUTION_FOLDERS ON CACHE BOOL "Solution folder in Visual Studio")
ENDIF()

set_property(GLOBAL PROPERTY USE_FOLDERS ${ENABLE_SOLUTION_FOLDERS})

IF(WIN32)
  INCLUDE (FindCodeSign)
ENDIF()

# ----------------------------------------------------------------------------
#  Check if 32bit or 64-bit
# ----------------------------------------------------------------------------
INCLUDE (CheckTargetArch)
CHECK_TARGET_ARCH()

IF ( TARGET_ARCH_64 )
  IF (TARGET_ARM)
    SET(NUGET_NATIVE_PLATFORM_ID_ARCH "arm64")
  ELSE()
    SET(NUGET_NATIVE_PLATFORM_ID_ARCH "x64")
  ENDIF()
ELSE()
  IF (TARGET_ARM)
    SET(NUGET_NATIVE_PLATFORM_ID_ARCH "arm")
  ELSE()
    SET(NUGET_NATIVE_PLATFORM_ID_ARCH "x86")
  ENDIF()
ENDIF()

IF ( IS_UBUNTU )
  #MESSAGE(STATUS "TARGET_ARCH_64: ${TARGET_ARCH_64}")
  SET(NUGET_NATIVE_PLATFORM_ID_UBUNTU "${LSB_RELEASE_ID_SHORT}.${LSB_RELEASE_RELEASE_SHORT}-${NUGET_NATIVE_PLATFORM_ID_ARCH}")
  STRING(TOLOWER "${NUGET_NATIVE_PLATFORM_ID_UBUNTU}" NUGET_NATIVE_PLATFORM_ID_UBUNTU)
  MESSAGE(STATUS "NUGET_NATIVE_PLATFORM_ID_UBUNTU: ${NUGET_NATIVE_PLATFORM_ID_UBUNTU}")
ELSE()
  SET(NUGET_NATIVE_PLATFORM_ID_UBUNTU "ubuntu.24.04-x64")
ENDIF()

IF (WIN32)
  MESSAGE(STATUS "CMAKE_SYSTEM_NAME: ${CMAKE_SYSTEM_NAME}")
  IF(TARGET_ARCH_64)
    SET(EMGUTF_ARCH win64)
  ELSE()
    MESSAGE(STATUS "CMAKE_GENERATOR: ${CMAKE_GENERATOR}")
    STRING(FIND "${CMAKE_GENERATOR}" "ARM" IS_ARM)
    IF(IS_ARM GREATER -1)
      SET(EMGUTF_ARCH arm)
    ELSE()
      SET(EMGUTF_ARCH win32)
    ENDIF()
  ENDIF()
ELSEIF(IS_UBUNTU)
  IF(TARGET_ARCH_64)
	IF(TARGET_ARM)
	  SET(EMGUTF_ARCH "ubuntu_arm64")
	ELSE()
	  SET(EMGUTF_ARCH "ubuntu_x64")
	ENDIF()
  ELSE()
    IF(TARGET_ARM)
	  SET(EMGUTF_ARCH "ubuntu_arm")
	ELSE()
      SET(EMGUTF_ARCH "ubuntu_x86")
	ENDIF()
  ENDIF()
ELSEIF(IS_DEBIAN)
  IF(TARGET_ARCH_64)
    SET(EMGUTF_ARCH "debian_arm64")
  ELSE()
    SET(EMGUTF_ARCH "debian_arm")
  ENDIF()
ELSE()
  SET(EMGUTF_ARCH ${CMAKE_SYSTEM_NAME})
ENDIF()

MESSAGE(STATUS "Building for ${EMGUTF_ARCH}")

SET(LIBRARY_OUTPUT_PATH ${CMAKE_SOURCE_DIR}/lib)
IF(WIN32)
  IF ("${EMGUTF_ARCH}" STREQUAL "win64")
      SET(UNMANAGED_LIBRARY_OUTPUT_SUBFOLDER "/runtimes/win-x64/native")
  ELSEIF("${EMGUTF_ARCH}" STREQUAL "arm")
	SET(UNMANAGED_LIBRARY_OUTPUT_SUBFOLDER "/arm")
  ELSEIF("${EMGUTF_ARCH}" STREQUAL "arm64")
	SET(UNMANAGED_LIBRARY_OUTPUT_SUBFOLDER "/arm64")
  ELSE()
    SET(UNMANAGED_LIBRARY_OUTPUT_SUBFOLDER "/x86")
  ENDIF()
ELSEIF()
  SET(UNMANAGED_LIBRARY_OUTPUT_SUBFOLDER "")
ENDIF()

SET(UNMANAGED_LIBRARY_OUTPUT_PATH "${LIBRARY_OUTPUT_PATH}${UNMANAGED_LIBRARY_OUTPUT_SUBFOLDER}")
FILE(MAKE_DIRECTORY ${UNMANAGED_LIBRARY_OUTPUT_PATH})

SET(UNMANAGED_DLL_EXTRA)

MACRO(CHECK_BINARY_EXIST folder patterns property_name second_check)
  SET(${property_name} ON)
  #MESSAGE(STATUS "PATTERNS: ${patterns}")
  #separate_arguments(patterns)
  foreach(pattern ${patterns})
    #MESSAGE(STATUS "PATTERN: ${pattern}")
    FILE(GLOB DLL_LIST "${CMAKE_CURRENT_SOURCE_DIR}/${folder}/${pattern}")
    LIST(LENGTH DLL_LIST DLL_LIST_LENGTH)
    IF(DLL_LIST_LENGTH GREATER 0)
      LIST(APPEND UNMANAGED_DLL_EXTRA ${DLL_LIST})
      MESSAGE(STATUS "Including the following files in package: ${DLL_LIST}")
      INSTALL(
        FILES
        ${DLL_LIST}
        DESTINATION ${folder}
        COMPONENT emgutf_binary
        )  
      #SET(${property_name} ON)
    ELSE()
      SET(${property_name} ${second_check})
    ENDIF()
    #ENDIF()
    #MESSAGE(STATUS "Property - ${property_name}: ${${property_name}}")
  endforeach()
  IF(${${property_name}})
    MESSAGE(STATUS "${property_name}: ${${property_name}}")
  ENDIF()
ENDMACRO()


CHECK_BINARY_EXIST("lib/ios" "libtfliteextern.a" HAVE_IOS_LITE OFF)

CHECK_BINARY_EXIST("lib/ios" "libtfliteextern_darwin_arm64.a" HAVE_MAC_CATALYST_LITE_ARM64 OFF)
CHECK_BINARY_EXIST("lib/ios" "libtfliteextern_darwin_x86_64.a" HAVE_MAC_CATALYST_LITE_X86_64 OFF)
IF (HAVE_MAC_CATALYST_LITE_ARM64 OR HAVE_MAC_CATALYST_LITE_X86_64)
  SET(HAVE_MAC_CATALYST_LITE ON)
  MESSAGE(STATUS "HAVE_MAC_CATALYST_LITE: ${HAVE_MAC_CATALYST_LITE}")
ELSE()
  SET(HAVE_MAC_CATALYST_LITE OFF)
ENDIF()
CHECK_BINARY_EXIST("lib/Build/Emgu.LiteRT.runtime.maui.macos/AnyCPU/Release/net${DOTNET_VERSION_MAJOR}.0-maccatalyst" "Emgu.LiteRT.runtime.maui.macos.dll" HAVE_MAUI_MAC_LITE ${HAVE_MAC_CATALYST_LITE})


CHECK_BINARY_EXIST("lib/runtimes/osx/native" "libtfliteextern.dylib" HAVE_OSX_LITE OFF)
CHECK_BINARY_EXIST("lib/runtimes/linux-x64/native" "libtfliteextern.so" HAVE_LINUX_X86_64_LITE OFF)
CHECK_BINARY_EXIST("lib/runtimes/ubuntu-x64/native" "libtfliteextern.so" HAVE_UBUNTU_X86_64_LITE OFF)
CHECK_BINARY_EXIST("lib/runtimes/ubuntu-arm64/native" "libtfliteextern.so" HAVE_UBUNTU_ARM64_LITE OFF)
CHECK_BINARY_EXIST("lib/runtimes/linux-arm/native" "libtfliteextern.so" HAVE_LINUX_ARM_LITE OFF)
CHECK_BINARY_EXIST("lib/runtimes/linux-arm64/native" "libtfliteextern.so" HAVE_LINUX_ARM64_LITE OFF)
CHECK_BINARY_EXIST("lib/android/arm64-v8a" "libtfliteextern.so" HAVE_ANDROID_ARM64_V8A_LITE OFF)
CHECK_BINARY_EXIST("lib/android/armeabi-v7a" "libtfliteextern.so" HAVE_ANDROID_ARMEABI_V7A_LITE OFF)
CHECK_BINARY_EXIST("lib/android/x86" "libtfliteextern.so" HAVE_ANDROID_X86_LITE OFF)
CHECK_BINARY_EXIST("lib/android/x86_64" "libtfliteextern.so" HAVE_ANDROID_X86_64_LITE OFF)

IF (IS_DEBIAN)
  SET(NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM "linux-arm")
  STRING(TOLOWER "${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM}" NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM)
  SET(NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64 "linux-arm64")
  STRING(TOLOWER "${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64}" NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64)
ELSE()
  IF(HAVE_LINUX_ARM_LITE)
    SET(NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM "linux-arm")
    STRING(TOLOWER "${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM}" NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM)
  ENDIF()
  IF(HAVE_LINUX_ARM64_LITE)
    SET(NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64 "linux-arm64")
    STRING(TOLOWER "${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64}" NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64)
  ENDIF()
ENDIF()

SET(HAVE_LINUX_LITE OFF)
IF(HAVE_LINUX_X86_64_LITE OR HAVE_LINUX_ARM_LITE OR HAVE_LINUX_ARM64_LITE)
  SET(HAVE_LINUX_LITE ON)
  MESSAGE(STATUS "HAVE_LINUX_LITE: ${HAVE_LINUX_LITE}")
ENDIF()

SET(HAVE_ANDROID_LITE OFF)
#IF(HAVE_ANDROID_ARM64_V8A_LITE OR HAVE_ANDROID_ARMEABI_LITE OR HAVE_ANDROID_ARMEABI_V7A_LITE OR HAVE_ANDROID_X86_LITE OR HAVE_ANDROID_X86_64_LITE)
IF(HAVE_ANDROID_ARM64_V8A_LITE OR HAVE_ANDROID_ARMEABI_V7A_LITE OR HAVE_ANDROID_X86_LITE OR HAVE_ANDROID_X86_64_LITE)
  SET(HAVE_ANDROID_LITE ON) 
ENDIF()
IF(HAVE_ANDROID_LITE)
  MESSAGE(STATUS "HAVE_ANDROID_LITE: ${HAVE_ANDROID_LITE}")
ENDIF()

CHECK_BINARY_EXIST("lib/runtimes/win-x86/native" "tfliteextern.dll" HAVE_WINDESKTOP_X86_LITE OFF)
CHECK_BINARY_EXIST("lib/runtimes/win-x64/native" "tfliteextern.dll" HAVE_WINDESKTOP_X64_LITE OFF)

SET(HAVE_WINDESKTOP_LITE OFF)

IF (HAVE_WINDESKTOP_X86_LITE OR HAVE_WINDESKTOP_X64_LITE)
  SET(HAVE_WINDESKTOP_LITE ON) 
  MESSAGE(STATUS "HAVE_WINDESKTOP_LITE: ${HAVE_WINDESKTOP_LITE}")  
  SET(WINDESKTOP_LITE_LABEL "-windesktop")
ELSEIF(HAVE_WINDESKTOP_X86_LITE)
    SET(WINDESKTOP_LITE_LABEL "-windesktop_x86")
ELSEIF(HAVE_WINDESKTOP_X64_LITE)	
    SET(WINDESKTOP_LITE_LABEL "-windesktop_x64")
ENDIF()   
  
# The Windows build scripts record the tflite_with_xnnpack define (TFLITE_ENABLE_XNNPACK for
# the CMake build) next to tfliteextern.dll. XNNPACK's sources are compiled either way, so the
# Bazel output folder can't tell whether it was enabled.
SET(TFLITE_WITH_XNNPACK_MARKER "${CMAKE_CURRENT_SOURCE_DIR}/lib/runtimes/win-x64/native/tflite_with_xnnpack.txt")
SET(EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK_DEFAULT OFF)
IF(HAVE_WINDESKTOP_X64_LITE AND EXISTS "${TFLITE_WITH_XNNPACK_MARKER}")
  FILE(READ "${TFLITE_WITH_XNNPACK_MARKER}" TFLITE_WITH_XNNPACK_VALUE)
  STRING(STRIP "${TFLITE_WITH_XNNPACK_VALUE}" TFLITE_WITH_XNNPACK_VALUE)
  STRING(TOLOWER "${TFLITE_WITH_XNNPACK_VALUE}" TFLITE_WITH_XNNPACK_VALUE)
  IF("${TFLITE_WITH_XNNPACK_VALUE}" STREQUAL "true")
    SET(EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK_DEFAULT ON)
  ENDIF()
ENDIF()
OPTION(EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK "The Windows x64 tfliteextern.dll is built with tflite_with_xnnpack=true" ${EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK_DEFAULT})
MESSAGE(STATUS "EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK: ${EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK}")

IF(HAVE_WINDESKTOP_X64_LITE AND EMGU_TF_LITE_WINDESKTOP_X64_XNNPACK)
  SET(HAVE_WINDESKTOP_X64_LITE_XNN ON)
  SET(WINDESKTOP_LITE_LABEL "${WINDESKTOP_LITE_LABEL}_xnn")
ENDIF()

# Same for Ubuntu: platforms/ubuntu/24.04/bazel_build_tflite records the tflite_with_xnnpack
# define next to libtfliteextern.so.
SET(UBUNTU_LITE_LABEL "")
SET(EMGU_TF_LITE_UBUNTU_XNNPACK_DEFAULT OFF)
IF(HAVE_UBUNTU_X86_64_LITE OR HAVE_UBUNTU_ARM64_LITE)
  SET(UBUNTU_LITE_LABEL "-ubuntu")
  FOREACH(UBUNTU_LITE_FOLDER "ubuntu-x64" "ubuntu-arm64")
    SET(UBUNTU_XNNPACK_MARKER "${CMAKE_CURRENT_SOURCE_DIR}/lib/runtimes/${UBUNTU_LITE_FOLDER}/native/tflite_with_xnnpack.txt")
    IF(EXISTS "${UBUNTU_XNNPACK_MARKER}")
      FILE(READ "${UBUNTU_XNNPACK_MARKER}" TFLITE_WITH_XNNPACK_VALUE)
      STRING(STRIP "${TFLITE_WITH_XNNPACK_VALUE}" TFLITE_WITH_XNNPACK_VALUE)
      STRING(TOLOWER "${TFLITE_WITH_XNNPACK_VALUE}" TFLITE_WITH_XNNPACK_VALUE)
      IF("${TFLITE_WITH_XNNPACK_VALUE}" STREQUAL "true")
        SET(EMGU_TF_LITE_UBUNTU_XNNPACK_DEFAULT ON)
      ENDIF()
    ENDIF()
  ENDFOREACH()
ENDIF()
OPTION(EMGU_TF_LITE_UBUNTU_XNNPACK "The Ubuntu libtfliteextern.so is built with tflite_with_xnnpack=true" ${EMGU_TF_LITE_UBUNTU_XNNPACK_DEFAULT})
MESSAGE(STATUS "EMGU_TF_LITE_UBUNTU_XNNPACK: ${EMGU_TF_LITE_UBUNTU_XNNPACK}")

IF((HAVE_UBUNTU_X86_64_LITE OR HAVE_UBUNTU_ARM64_LITE) AND EMGU_TF_LITE_UBUNTU_XNNPACK)
  SET(UBUNTU_LITE_LABEL "${UBUNTU_LITE_LABEL}_xnn")
ENDIF()

SET(IOS_LITE_LABEL "")
IF(HAVE_IOS_LITE)
  #MESSAGE(STATUS "HAVE_IOS_LITE: ${HAVE_IOS_LITE}")
  SET(IOS_LITE_LABEL "-ios")
ENDIF()

SET(OSX_LITE_LABEL "")
IF(HAVE_OSX_LITE)
  #MESSAGE(STATUS "HAVE_OSX_LITE: ${HAVE_OSX_LITE}")
  SET(OSX_LITE_LABEL "-macos")
ENDIF()

SET(ANDROID_LITE_LABEL "")
IF(HAVE_ANDROID_ARM64_V8A_LITE)
  SET(ANDROID_LITE_LABEL "${ANDROID_LITE_LABEL}-arm64-v8a")
ENDIF()
#IF(HAVE_ANDROID_ARMEABI_LITE)
#  SET(ANDROID_LITE_LABEL "${ANDROID_LITE_LABEL}-armeabi")
#ENDIF()
IF(HAVE_ANDROID_ARMEABI_V7A_LITE)	
  SET(ANDROID_LITE_LABEL "${ANDROID_LITE_LABEL}-armeabi-v7a")
ENDIF()
IF(HAVE_ANDROID_X86_LITE)
  SET(ANDROID_LITE_LABEL "${ANDROID_LITE_LABEL}-x86")
ENDIF()
IF(HAVE_ANDROID_X86_64_LITE)
  SET(ANDROID_LITE_LABEL "${ANDROID_LITE_LABEL}-x86_64")
ENDIF()

CHECK_BINARY_EXIST("bin" "Emgu.TF.Runtime.*" HAVE_PREBUILD_EMGU_TF_WORLD OFF)

#IF("${ANDROID_LITE_LABEL}" STREQUAL "-arm64-v8a-armeabi-armeabi-v7a-x86-x86_64")
IF("${ANDROID_LITE_LABEL}" STREQUAL "-arm64-v8a-armeabi-v7a-x86-x86_64")
  SET(ANDROID_LITE_LABEL "-android")
ELSEIF(NOT ("${ANDROID_LITE_LABEL}" STREQUAL ""))
  SET(ANDROID_LITE_LABEL "-android${ANDROID_LITE_LABEL}")
ENDIF()

SET(HAVE_LITE_BUILD OFF)
IF(HAVE_ANDROID_LITE OR HAVE_IOS_LITE OR HAVE_OSX_LITE OR HAVE_MAC_CATALYST_LITE OR HAVE_WINDESKTOP_LITE OR HAVE_LINUX_LITE OR HAVE_UBUNTU_X86_64_LITE OR HAVE_UBUNTU_ARM64_LITE)
  SET(HAVE_LITE_BUILD ON)
  MESSAGE(STATUS "Have Tensorflow Lite build: ${HAVE_LITE_BUILD}")
ENDIF()

if ("${LICENSE_TYPE}" STREQUAL "Commercial")
  SET(LICENSE_LABEL "-cr")
ELSE()
  SET(LICENSE_LABEL "")
ENDIF()
	
# ----------------------------------------------------------------------------
#  Setup Package information
# ----------------------------------------------------------------------------
SET(PACKAGE "${PROJECT_NAME}")
SET(GITHUB_REPO_URL "https://github.com/emgucv/emgutf")
SET(CPACK_PACKAGE_CONTACT "Emgu TF support <support@emgu.com>")
SET(PACKAGE_BUGREPORT "${CPACK_PACKAGE_CONTACT}")
SET(PACKAGE_NAME "${PROJECT_NAME}")

SET(CPACK_PACKAGE_DESCRIPTION_SUMMARY "Emgu TF Lite is a cross platform .Net wrapper to the Google Tensorflow Lite library.")
SET(LITE_BUILD_LABEL "")
IF(HAVE_LITE_BUILD)
  SET(LITE_BUILD_LABEL "${IOS_LITE_LABEL}${ANDROID_LITE_LABEL}${WINDESKTOP_LITE_LABEL}${OSX_LITE_LABEL}")
  IF(("${LITE_BUILD_LABEL}" STREQUAL "-ios-android-windesktop-macos") OR ("${LITE_BUILD_LABEL}" STREQUAL "-ios-android-windesktop_xnn-macos"))
    SET(LITE_BUILD_LABEL "-pro")
  ELSE()
    # The pro package also bundles the Ubuntu binary, so the Ubuntu label only goes on the others.
    SET(LITE_BUILD_LABEL "${LITE_BUILD_LABEL}${UBUNTU_LITE_LABEL}")
  ENDIF()
  SET(LITE_BUILD_LABEL "tflite${LITE_BUILD_LABEL}")
ENDIF()

SET(CPACK_PACKAGE_NAME "emgu${LITE_BUILD_LABEL}")

#SET(CPACK_PACKAGE_NAME "${PACKAGE_NAME}")

SET(CPACK_PACKAGE_VENDOR "Emgu Corporation")
SET(CPACK_PACKAGE_VERSION_MAJOR ${LITERT_VERSION_MAJOR})
SET(CPACK_PACKAGE_VERSION_MINOR ${LITERT_VERSION_MINOR})
SET(CPACK_PACKAGE_VERSION_PATCH ${LITERT_VERSION_PATCH})
SET(CPACK_PACKAGE_VERSION "${CPACK_PACKAGE_VERSION_MAJOR}.${CPACK_PACKAGE_VERSION_MINOR}.${CPACK_PACKAGE_VERSION_PATCH}.${EMGUTF_GITVERSION}")
#MESSAGE(STATUS "CPACK_PACKAGE_VERSION: ${CPACK_PACKAGE_VERSION}")

SET(CPACK_PACKAGE_FILE_NAME "lib${CPACK_PACKAGE_NAME}-${CPACK_PACKAGE_VERSION}${LICENSE_LABEL}")

MESSAGE(STATUS "CPACK_PACKAGE_FILE_NAME is ${CPACK_PACKAGE_FILE_NAME}")
FILE(WRITE "emgutf.version.txt" "${CPACK_PACKAGE_VERSION}${LICENSE_LABEL}")

#SET(CPACK_PACKAGE_DEPENDENCY_VERSION "${CPACK_PACKAGE_VERSION_MAJOR}.${CPACK_PACKAGE_VERSION_MINOR}.${CPACK_PACKAGE_VERSION_PATCH}.*")
SET(CPACK_PACKAGE_DEPENDENCY_VERSION "${CPACK_PACKAGE_VERSION}")

SET(CPACK_RESOURCE_FILE_LICENSE "${CMAKE_CURRENT_SOURCE_DIR}/${LICENSE_FILE_NAME}")

IF (WIN32 OR APPLE)
  SET(CPACK_PACKAGE_CLI_FOLDER bin)
ELSE ()
  SET(CPACK_PACKAGE_CLI_FOLDER lib/cli/${PACKAGE_NAME}-${CPACK_PACKAGE_VERSION_MAJOR}.${CPACK_PACKAGE_VERSION_MINOR})
ENDIF()


SET(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${LIBRARY_OUTPUT_PATH}" )
SET(CMAKE_RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")
SET(CMAKE_RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_RUNTIME_OUTPUT_DIRECTORY}")

SET(EMGU_TF_BUILD ON CACHE BOOL "BUILD Emgu TF")
SET(EMGU_TF_EXAMPLE_BUILD ON CACHE BOOL "Build Emgu TF Example")

# ----------------------------------------------------------------------------
#  Generate required common assembly file for Emgu TF project
# ----------------------------------------------------------------------------
INCLUDE(Today)
TODAY() #set the variable CURRENT_YEAR

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/CommonAssemblyInfo.cs.in ${CMAKE_CURRENT_SOURCE_DIR}/CommonAssemblyInfo.cs)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Directory.Build.props)

SET (EMGUTF_LICENSE_FILE "${LICENSE_FILE_NAME}.txt")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/${LICENSE_FILE_NAME} ${CMAKE_CURRENT_SOURCE_DIR}/${EMGUTF_LICENSE_FILE} COPYONLY)

# ----------------------------------------------------------------------------
#  Generate xamarin component configuration file
# ----------------------------------------------------------------------------
#CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/component.yaml.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/xamarin/emgutf_v1/component/component.yaml)

SET(EMGUTF_NUGET_FILE_LIST "")

SET(EMGU_WINDOWS_MSVCRT_X86_NUGET_ID "Emgu.runtime.windows.msvc.rt.x86")
SET(EMGU_WINDOWS_MSVCRT_X64_NUGET_ID "Emgu.runtime.windows.msvc.rt.x64")

#IF(EMGU_SIGN_FOUND AND WIN32)
#  file(TO_NATIVE_PATH ${EMGU_SIGN_EXECUTABLE} EMGU_SIGN_EXECUTABLE_NATIVE_PATH)
#  file(TO_NATIVE_PATH ${SIGNTOOL_EXECUTABLE} SIGNTOOL_EXECUTABLE_NATIVE_PATH)
#ENDIF()

#IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
#  file(TO_NATIVE_PATH ${EMGU_NUGET_SIGN_EXECUTABLE} EMGU_NUGET_SIGN_EXECUTABLE_NATIVE_PATH)
#  file(TO_NATIVE_PATH ${NUGET_EXECUTABLE} NUGET_EXECUTABLE_NATIVE_PATH)
#ENDIF()


SET(EMGUTF_MAUI_DOTNET_VERSION "net${DOTNET_VERSION_MAJOR}.0")
if ("${DOTNET_VERSION_MAJOR}" STREQUAL "10")
	SET(MAUI_VERSION_MSBUILD "<MauiVersion>10.0.11</MauiVersion>")
    #message(STATUS "Using .NET 10")
endif()

SET(EMGUTF_MAUI_ANDROID_SRC_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-android")
SET(EMGUTF_MAUI_IOS_SRC_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-ios")
#SET(EMGUTF_MAUI_MACOS_SRC_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-maccatalyst")
SET(EMGUTF_MAUI_WINDOWS_SRC_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-windows10.0.19041.0")

SET(EMGUTF_MAUI_ANDROID_DST_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-android34.0")
SET(EMGUTF_MAUI_IOS_DST_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-ios17.2")
#SET(EMGUTF_MAUI_MACOS_DST_FOLDER "${EMGUCV_MAUI_DOTNET_VERSION}-maccatalyst16.1")
SET(EMGUTF_MAUI_WINDOWS_DST_FOLDER "${EMGUTF_MAUI_DOTNET_VERSION}-windows10.0.19041.0")


IF(HAVE_LITE_BUILD)
  # Emgu.LiteRT.Util, Emgu.LiteRT.Tflite and Emgu.LiteRT each produce their own nuget package
  # (EMGUTF_LITE_NUGET_ID is still the root every other package id derives from, for Models and
  # the native runtime.* packages). Emgu.LiteRT.Tflite depends on Emgu.LiteRT.Util (its
  # UnmanagedObject-derived types need it to compile); Emgu.LiteRT also depends on
  # Emgu.LiteRT.Util for the same reason, but has no code dependency on Emgu.LiteRT.Tflite -
  # the two were only ever bundled together for packaging convenience, not because one needs
  # the other.
  SET(EMGUTF_LITE_NUGET_ID "Emgu.LiteRT")
  SET(EMGUTF_LITE_NUGET_TITLE "Emgu LiteRT")

  SET(EMGUTF_LITERT_NUGET_ID "${EMGUTF_LITE_NUGET_ID}")
  SET(EMGUTF_LITERT_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE}")

  # Emgu.LiteRT.LM/: the wrapper of LiteRT-LM's C API (liblitert-lm), its own managed package id,
  # depending on Emgu.LiteRT.Util only.
  SET(EMGUTF_LITERT_LM_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.LM")
  SET(EMGUTF_LITERT_LM_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE} LM")
  # Emgu.LiteRT.LM.Models/: ready-to-use language models (download + chat) on top of Emgu.LiteRT.LM.
  SET(EMGUTF_LITERT_LM_MODELS_NUGET_ID "${EMGUTF_LITERT_LM_NUGET_ID}.Models")
  SET(EMGUTF_LITERT_LM_MODELS_NUGET_TITLE "${EMGUTF_LITERT_LM_NUGET_TITLE} Models")

  SET(EMGUTF_LITERT_UTIL_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.Util")
  SET(EMGUTF_LITERT_UTIL_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE} Util")

  # Emgu.LiteRT.Tflite/ (renamed from Emgu.TF.Lite/) is the TensorFlow Lite Interpreter API
  # wrapper (Emgu.TF.Lite namespace); EMGUTF_LITE_TFLITE_NUGET_ID is its own managed package id,
  # distinct from EMGUTF_LITE_TFLITE_WINDOWS_NUGET_ID below (the native tfliteextern.dll runtime
  # package for Windows).
  SET(EMGUTF_LITE_TFLITE_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.Tflite")
  SET(EMGUTF_LITE_TFLITE_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE} Tflite")

  # Emgu.LiteRT.ImageIO/ (renamed from Emgu.Models/) - NativeImageIO/JpegData, its own managed
  # package id too, depending on Emgu.LiteRT.Util and Emgu.LiteRT.Tflite.
  SET(EMGUTF_LITE_IMAGEIO_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.ImageIO")
  SET(EMGUTF_LITE_IMAGEIO_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE} ImageIO")

  # Emgu.LiteRT.Models/ was renamed to Emgu.LiteRT.Tflite.Models/ - it's built on the TFLite
  # Interpreter API (Emgu.TF.Lite), not LiteRT's own CompiledModel API.
  SET(EMGUTF_LITE_MODEL_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.Tflite.Models")

  SET(EMGUTF_LITE_WINDOWS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.windows")  
  IF (HAVE_WINDESKTOP_X64_LITE_XNN)
    SET(EMGUTF_LITE_WINDOWS_NUGET_ID "${EMGUTF_LITE_WINDOWS_NUGET_ID}.xnn")  
  ENDIF()
  #IF ("${LICENSE_TYPE}" STREQUAL "Commercial")
  #  SET(EMGUTF_LITE_WINDOWS_NUGET_ID "${EMGUTF_LITE_WINDOWS_NUGET_ID}-CR")  
  #ENDIF()
  SET(EMGUTF_LITE_WINDOWS_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Windows")

  # tfliteextern.dll (the TensorFlow Lite Interpreter C API wrapper Emgu.TF.Lite/Emgu.LiteRT.Util
  # P/Invoke) is packaged separately from libLiteRt.dll (${EMGUTF_LITE_WINDOWS_NUGET_ID} above,
  # which Emgu.LiteRT's CompiledModel API P/Invokes directly), with a nuget dependency on it -
  # see EMGUTF_LITE_TFLITE_WINDOWS_NUGET_DEPENDENCY below.
  SET(EMGUTF_LITE_TFLITE_WINDOWS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.Tflite.runtime.windows")
  SET(EMGUTF_LITE_TFLITE_WINDOWS_NUGET_TITLE "Native runtime (tfliteextern) of ${EMGUTF_LITE_NUGET_TITLE} for Windows")

  SET(EMGUTF_LITE_MACOS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.macos")
  SET(EMGUTF_LITE_MACOS_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Mac OS console or asp.net application. (not for MAUI)")	
  
  SET(EMGUTF_LITE_UBUNTU_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.${NUGET_NATIVE_PLATFORM_ID_UBUNTU}") 
  #IF ("${LICENSE_TYPE}" STREQUAL "Commercial")
  #  SET(EMGUTF_LITE_UBUNTU_NUGET_ID "${EMGUTF_LITE_UBUNTU_NUGET_ID}-CR")  
  #ENDIF()  
  SET(EMGUTF_LITE_UBUNTU_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Ubuntu")	
  
  SET(EMGUTF_LITE_MAUI_ANDROID_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.maui.android")  
  SET(EMGUTF_LITE_MAUI_ANDROID_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Maui Android")	
  
#  SET(EMGUTF_LITE_XAMARIN_ANDROID_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.xamarin.android")  
#  SET(EMGUTF_LITE_XAMARIN_ANDROID_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Xamarin Android")	

  SET(EMGUTF_LITE_MAUI_IOS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.maui.ios")  
  SET(EMGUTF_LITE_MAUI_IOS_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Maui iOS")	
  
  SET(EMGUTF_LITE_MAUI_MACOS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.maui.macos")  
  SET(EMGUTF_LITE_MAUI_MACOS_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Maui MacOS")	
  
#  SET(EMGUTF_LITE_XAMARIN_IOS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.xamarin.ios")  
#  SET(EMGUTF_LITE_XAMARIN_IOS_NUGET_TITLE "Native runtime of ${EMGUTF_LITE_NUGET_TITLE} for Xamarin iOS")	

  SET(EMGUTF_LITE_MODEL_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE} Models")
  #MESSAGE(STATUS ">>> HAVE_WINDESKTOP_LITE: ${HAVE_WINDESKTOP_LITE}")
  
  IF(EMGU_SIGN_FOUND AND WIN32)
  
	  SET(EMGUTF_LITE_POST_PROCESS_BINARY "<Target Name=\"PostProcessBinary\" AfterTargets=\"PostBuildEvent\">")
      SET(EMGUTF_LITE_MODELS_POST_PROCESS_BINARY "<Target Name=\"PostProcessBinary\" AfterTargets=\"PostBuildEvent\">")
	  SET(EMGUTF_LITE_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}
    <Message Text=\"Signing $(OutputPath)$(AssemblyName).dll\" Importance=\"high\" />
	<MakeDir Directories=\"$(OutputPath)\\signed\"/>
    <Exec Command='${EMGU_SIGN_EXECUTABLE_NATIVE_PATH} \"$(OutputPath)$(AssemblyName).dll\" \"$(OutputPath)signed\" \"${SIGNTOOL_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(OutputPath)\" />
	</Target>")
	  SET(EMGUTF_LITE_MODELS_POST_PROCESS_BINARY "${EMGUTF_LITE_MODELS_POST_PROCESS_BINARY}
    <Message Text=\"Signing $(OutputPath)$(AssemblyName).dll\" Importance=\"high\" />
	<MakeDir Directories=\"$(OutputPath)\\signed\"/>
    <Exec Command='${EMGU_SIGN_EXECUTABLE_NATIVE_PATH} \"$(OutputPath)$(AssemblyName).dll\" \"$(OutputPath)signed\" \"${SIGNTOOL_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(OutputPath)\" />
	</Target>")
  ENDIF()
  IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
	  
	  SET(EMGUTF_LITE_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}
    <Target Name=\"PostProcessNuget\" AfterTargets=\"Pack\">
      <Message Text=\"Signing $(PackageOutputPath)\\$(PackageId).$(PackageVersion).nupkg\" Importance=\"high\" />
      <MakeDir Directories=\"$(PackageOutputPath)\\signed\"/>
      <Exec Command='${EMGU_NUGET_SIGN_EXECUTABLE_NATIVE_PATH} \"$(PackageOutputPath)\\$(PackageId).$(PackageVersion).nupkg\" \"$(PackageOutputPath)\\signed\" \"${NUGET_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(PackageOutputPath)\" />
    </Target>")
	  SET(EMGUTF_LITE_MODELS_POST_PROCESS_NUGET "${EMGUTF_LITE_MODELS_POST_PROCESS_NUGET}
    <Target Name=\"PostProcessNuget\" AfterTargets=\"Pack\">
      <Message Text=\"Signing $(PackageOutputPath)\\$(PackageId).$(PackageVersion).nupkg\" Importance=\"high\" />
      <MakeDir Directories=\"$(PackageOutputPath)\\signed\"/>
      <Exec Command='${EMGU_NUGET_SIGN_EXECUTABLE_NATIVE_PATH} \"$(PackageOutputPath)\\$(PackageId).$(PackageVersion).nupkg\" \"$(PackageOutputPath)\\signed\" \"${NUGET_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(PackageOutputPath)\" />
    </Target>")
	
  ENDIF()
  
  #IF (HAVE_ANDROID_LITE)
	CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.android.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Android/Lite/Directory.Build.props)
	IF(EMGU_SIGN_FOUND AND WIN32)
      CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Android/Lite/SignBinary.Targets)
    ENDIF()
    IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
      CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Android/Lite/SignPkg.Targets)
    ENDIF()
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.android.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Android/Lite/docs/README.md)
  #ENDIF()
  
  #IF (HAVE_IOS_LITE)	
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.ios.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/iOS/Directory.Build.props)
  IF(EMGU_SIGN_FOUND AND WIN32)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/iOS/SignBinary.Targets)
  ENDIF()
  IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/iOS/SignPkg.Targets)
  ENDIF()
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.ios.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/iOS/docs/README.md)
  #ENDIF()

  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.macos.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Mac/Directory.Build.props)
  IF(EMGU_SIGN_FOUND AND WIN32)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Mac/SignBinary.Targets)
  ENDIF()
  IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Mac/SignPkg.Targets)
  ENDIF()
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.macos.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/Mac/docs/README.md)
  
  SET(HAVE_MAUI_LITE OFF)
  IF(HAVE_IOS_LITE AND HAVE_ANDROID_LITE AND HAVE_WINDESKTOP_LITE)
    SET (HAVE_MAUI_LITE ON)
  ENDIF()
  MESSAGE(STATUS "HAVE_MAUI_LITE: ${HAVE_MAUI_LITE}")
      
ENDIF()

#IF(HAVE_MAUI_LITE)
  SET(EMGUTF_LITE_MAUI_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.maui${EMGUTF_LITE_RUNTIME_EXTRA_TAG}")
  SET(EMGUTF_LITE_MODELS_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.Tflite.Models")
  SET(EMGUTF_LITE_MAUI_NUGET_TITLE "${EMGUTF_LITE_NUGET_TITLE} for MAUI Native Runtime")
  
#  SET(EMGUTF_LITE_MAUI_NUGET_FILE_LIST "	
#    <file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />
#	
#	<file src=\"..\\..\\..\\lib\\Build\\Emgu.TF.Lite.Platform.Maui.UI\\AnyCPU\\Release\\${EMGUTF_MAUI_ANDROID_SRC_FOLDER}\\Emgu.TF.Lite.Platform.Maui.UI.dll\" target=\"lib\\${EMGUTF_MAUI_ANDROID_DST_FOLDER}\" />
#    <file src=\"..\\..\\..\\lib\\Build\\Emgu.TF.Lite.Platform.Maui.UI\\AnyCPU\\Release\\${EMGUTF_MAUI_ANDROID_SRC_FOLDER}\\Emgu.TF.Lite.Platform.Maui.UI.xml\" target=\"lib\\${EMGUTF_MAUI_ANDROID_DST_FOLDER}\" />
#	
#	<file src=\"..\\..\\..\\lib\\Build\\Emgu.TF.Lite.Platform.Maui.UI\\AnyCPU\\Release\\${EMGUTF_MAUI_IOS_SRC_FOLDER}\\Emgu.TF.Lite.Platform.Maui.UI.dll\" target=\"lib\\${EMGUTF_MAUI_IOS_DST_FOLDER}\" />
#    <file src=\"..\\..\\..\\lib\\Build\\Emgu.TF.Lite.Platform.Maui.UI\\AnyCPU\\Release\\${EMGUTF_MAUI_IOS_SRC_FOLDER}\\Emgu.TF.Lite.Platform.Maui.UI.xml\" target=\"lib\\${EMGUTF_MAUI_IOS_DST_FOLDER}\" />
#	
#	<file src=\"..\\..\\..\\lib\\Build\\Emgu.TF.Lite.Platform.Maui.UI\\AnyCPU\\Release\\${EMGUTF_MAUI_WINDOWS_SRC_FOLDER}\\Emgu.TF.Lite.Platform.Maui.UI.dll\" target=\"lib\\${EMGUTF_MAUI_WINDOWS_DST_FOLDER}\" />
#    <file src=\"..\\..\\..\\lib\\Build\\Emgu.TF.Lite.Platform.Maui.UI\\AnyCPU\\Release\\${EMGUTF_MAUI_WINDOWS_SRC_FOLDER}\\Emgu.TF.Lite.Platform.Maui.UI.xml\" target=\"lib\\${EMGUTF_MAUI_WINDOWS_DST_FOLDER}\" />
#	
#	<file src=\"README.md\" target=\"docs\\\" />
#    <file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />")

  # Depends on the Tflite package (tfliteextern.dll), not ${EMGUTF_LITE_WINDOWS_NUGET_ID} directly
  # (libLiteRt.dll only) - MAUI Windows needs both, and the Tflite package already depends on
  # ${EMGUTF_LITE_WINDOWS_NUGET_ID}, so this pulls in both transitively.
  SET(EMGUTF_LITE_MAUI_NUGET_DEPENDENCY "
  	<ItemGroup>
      <PackageReference Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'windows'\" Include=\"${EMGUTF_LITE_TFLITE_WINDOWS_NUGET_ID}\" Version=\"${CPACK_PACKAGE_DEPENDENCY_VERSION}\">
	  </PackageReference>
    </ItemGroup>")
   
  IF(EMGU_SIGN_FOUND AND WIN32)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/UI/Lite/SignBinary.Targets)
  ENDIF()
  IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/UI/Lite/SignPkg.Targets)
  ENDIF()
#ENDIF()

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/UI/Lite/docs/README.md)
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.maui.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Runtime/Maui/UI/Lite/Directory.Build.props)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Maui.Demo.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Example/Maui.Lite/Directory.Build.props)
IF (HAVE_WINDESKTOP_LITE)
    IF (HAVE_WINDESKTOP_X64_LITE)
      SET(EMGU_WINDOWS_MSVCRT_X64_NUGET_TITLE "MSVC x64 runtime for windows")
	  SET(EMGU_WINDOWS_MSVCRT_X64_NUGET_FILE_LIST "${EMGU_WINDOWS_MSVCRT_X64_NUGET_FILE_LIST} 
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x64\\native\\concrt*.dll\" target=\"runtimes\\win-x64\\native\" /> 
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x64\\native\\msvcp*.dll\" target=\"runtimes\\win-x64\\native\" /> 
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x64\\native\\vcruntime*.dll\" target=\"runtimes\\win-x64\\native\" />     
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x64\\native\\vccorlib*.dll\" target=\"runtimes\\win-x64\\native\" />     
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />
    <file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />")
    ENDIF()
	IF (HAVE_WINDESKTOP_X86_LITE)
      SET(EMGU_WINDOWS_MSVCRT_X86_NUGET_TITLE "MSVC x86 runtime for windows")
	  SET(EMGU_WINDOWS_MSVCRT_X86_NUGET_FILE_LIST "${EMGU_WINDOWS_MSVCRT_X86_NUGET_FILE_LIST} 
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x86\\native\\concrt*.dll\" target=\"runtimes\\win-x86\\native\" /> 
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x86\\native\\msvcp*.dll\" target=\"runtimes\\win-x86\\native\" /> 
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x86\\native\\vcruntime*.dll\" target=\"runtimes\\win-x86\\native\" />     
	<file src=\"..\\..\\..\\lib\\runtimes\\win-x86\\native\\vccorlib*.dll\" target=\"runtimes\\win-x86\\native\" />     
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />
    <file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />")
    ENDIF()
	IF(MSVC)
      SET(EMGU_WINDOWS_MSVCRT_VERSION ${CMAKE_CXX_COMPILER_VERSION})
	  string(REPLACE "." ";" EMGU_WINDOWS_MSVCRT_VERSION_LIST ${EMGU_WINDOWS_MSVCRT_VERSION})
	  list(GET EMGU_WINDOWS_MSVCRT_VERSION_LIST 0 EMGU_WINDOWS_MSVCRT_VERSION_MAJOR)
	  list(GET EMGU_WINDOWS_MSVCRT_VERSION_LIST 1 EMGU_WINDOWS_MSVCRT_VERSION_MINOR)
	  list(GET EMGU_WINDOWS_MSVCRT_VERSION_LIST 2 EMGU_WINDOWS_MSVCRT_VERSION_PATCH)
	  list(GET EMGU_WINDOWS_MSVCRT_VERSION_LIST 3 EMGU_WINDOWS_MSVCRT_VERSION_BUILD)
	  MESSAGE(STATUS "MSVCRT VERSION: ${EMGU_WINDOWS_MSVCRT_VERSION_MAJOR}.${EMGU_WINDOWS_MSVCRT_VERSION_MINOR}.${EMGU_WINDOWS_MSVCRT_VERSION_PATCH}.${EMGU_WINDOWS_MSVCRT_VERSION_BUILD}")
      SET(EMGU_WINDOWS_MSVCRT_NUGET_VERSION ${EMGU_WINDOWS_MSVCRT_VERSION})
	  IF ("${EMGU_WINDOWS_MSVCRT_VERSION_BUILD}" STREQUAL "0")
	    SET(EMGU_WINDOWS_MSVCRT_NUGET_VERSION "${EMGU_WINDOWS_MSVCRT_VERSION_MAJOR}.${EMGU_WINDOWS_MSVCRT_VERSION_MINOR}.${EMGU_WINDOWS_MSVCRT_VERSION_PATCH}")
	  ENDIF()
	  MESSAGE(STATUS "MSVCRT NUGET VERSION: ${EMGU_WINDOWS_MSVCRT_NUGET_VERSION}")
	  IF (HAVE_WINDESKTOP_X86_LITE)
	    SET(EMGUTF_LITE_WINDOWS_NUGET_DEPENDENCY "${EMGUTF_LITE_WINDOWS_NUGET_DEPENDENCY}
	  <dependency id=\"${EMGU_WINDOWS_MSVCRT_X86_NUGET_ID}\" version=\"${EMGU_WINDOWS_MSVCRT_VERSION}\" />
	  ")
	  ENDIF()
	  IF (HAVE_WINDESKTOP_X64_LITE)
	    SET(EMGUTF_LITE_WINDOWS_NUGET_DEPENDENCY "${EMGUTF_LITE_WINDOWS_NUGET_DEPENDENCY}
	  <dependency id=\"${EMGU_WINDOWS_MSVCRT_X64_NUGET_ID}\" version=\"${EMGU_WINDOWS_MSVCRT_VERSION}\" />
	  ")
	  ENDIF()
    ENDIF()
ENDIF()

IF( HAVE_WINDESKTOP_LITE )
  # ${EMGUTF_LITE_WINDOWS_NUGET_ID} contains only libLiteRt.dll (LiteRT's own runtime, which
  # Emgu.LiteRT's CompiledModel API P/Invokes directly). tfliteextern.dll (the TensorFlow Lite
  # Interpreter C API wrapper) is packaged separately, as ${EMGUTF_LITE_TFLITE_WINDOWS_NUGET_ID}
  # below, depending on this package rather than bundling both DLLs together.
  IF( HAVE_WINDESKTOP_X64_LITE )
  # libLiteRt.dll only comes from the Bazel build path (bazel_build_litert.ps1); the CMake path
  # (cmake_build_litert.ps1) only ever produces tfliteextern.dll. Guard on EXISTS, the same way
  # the macOS packaging below treats libLiteRt.dylib as optional, so a CMake-only build doesn't
  # fail to pack ${EMGUTF_LITE_WINDOWS_NUGET_ID} over a file that was never built.
  IF(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/lib/runtimes/win-x64/native/libLiteRt.dll")
  SET(EMGUTF_LITE_WINDOWS_NUGET_FILE_LIST "${EMGUTF_LITE_WINDOWS_NUGET_FILE_LIST}
    <file src=\"..\\..\\..\\lib\\runtimes\\win-x64\\native\\libLiteRt.dll\" target=\"runtimes\\win-x64\\native\" />
	")
  ENDIF()
  SET(EMGUTF_LITE_TFLITE_WINDOWS_NUGET_FILE_LIST "${EMGUTF_LITE_TFLITE_WINDOWS_NUGET_FILE_LIST}
    <file src=\"..\\..\\..\\lib\\runtimes\\win-x64\\native\\tfliteextern.dll\" target=\"runtimes\\win-x64\\native\" />
	")
  ENDIF()
  IF( HAVE_WINDESKTOP_X86_LITE )
  IF(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/lib/runtimes/win-x86/native/libLiteRt.dll")
  SET(EMGUTF_LITE_WINDOWS_NUGET_FILE_LIST "${EMGUTF_LITE_WINDOWS_NUGET_FILE_LIST}
    <file src=\"..\\..\\..\\lib\\runtimes\\win-x86\\native\\libLiteRt.dll\" target=\"runtimes\\win-x86\\native\" />
	")
  ENDIF()
  SET(EMGUTF_LITE_TFLITE_WINDOWS_NUGET_FILE_LIST "${EMGUTF_LITE_TFLITE_WINDOWS_NUGET_FILE_LIST}
    <file src=\"..\\..\\..\\lib\\runtimes\\win-x86\\native\\tfliteextern.dll\" target=\"runtimes\\win-x86\\native\" />
	")
  ENDIF()
  SET(EMGUTF_LITE_WINDOWS_NUGET_FILE_LIST "${EMGUTF_LITE_WINDOWS_NUGET_FILE_LIST}
	<file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />
	<file src=\"README.md\" target=\"docs\\\" />")
  SET(EMGUTF_LITE_WINDOWS_NUGET_DEPENDENCY "
    <dependencies>
	    ${EMGUTF_LITE_WINDOWS_NUGET_DEPENDENCY}
		<dependency id=\"${EMGUTF_LITE_NUGET_ID}\" version=\"${CPACK_PACKAGE_DEPENDENCY_VERSION}\" />
    </dependencies>")

  SET(EMGUTF_LITE_TFLITE_WINDOWS_NUGET_FILE_LIST "${EMGUTF_LITE_TFLITE_WINDOWS_NUGET_FILE_LIST}
	<file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />
	<file src=\"README.md\" target=\"docs\\\" />")
  SET(EMGUTF_LITE_TFLITE_WINDOWS_NUGET_DEPENDENCY "
    <dependencies>
		<dependency id=\"${EMGUTF_LITE_WINDOWS_NUGET_ID}\" version=\"${CPACK_PACKAGE_DEPENDENCY_VERSION}\" />
    </dependencies>")
ENDIF()

IF(HAVE_OSX_LITE) 
  SET(EMGUTF_LITE_MACOS_NUGET_FILE_LIST "${EMGUTF_LITE_MACOS_NUGET_FILE_LIST} 
    <file src=\"..\\..\\..\\lib\\runtimes\\osx\\native\\libtfliteextern.dylib\" target=\"runtimes\\osx\\native\" />")
  # LiteRT's own runtime (built by platforms/macos/bazel_build_litert_macos), P/Invoked directly by the
  # Emgu.LiteRT classes. Optional: the tfliteextern-based API works without it.
  IF(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/lib/runtimes/osx/native/libLiteRt.dylib")
    SET(EMGUTF_LITE_MACOS_NUGET_FILE_LIST "${EMGUTF_LITE_MACOS_NUGET_FILE_LIST}
    <file src=\"..\\..\\..\\lib\\runtimes\\osx\\native\\libLiteRt.dylib\" target=\"runtimes\\osx\\native\" />")
  ENDIF()
  # LiteRT-LM's C API (built by platforms/macos/bazel_build_tflite_macos, Apple Silicon only), P/Invoked directly by
  # the Emgu.LiteRT.LM classes, plus the prebuilt libGemmaModelConstraintProvider.dylib it depends on. Optional.
  IF(EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/lib/runtimes/osx/native/liblitert-lm.dylib")
    SET(EMGUTF_LITE_MACOS_NUGET_FILE_LIST "${EMGUTF_LITE_MACOS_NUGET_FILE_LIST}
    <file src=\"..\\..\\..\\lib\\runtimes\\osx\\native\\liblitert-lm.dylib\" target=\"runtimes\\osx\\native\" />
    <file src=\"..\\..\\..\\lib\\runtimes\\osx\\native\\libGemmaModelConstraintProvider.dylib\" target=\"runtimes\\osx\\native\" />")
  ENDIF()
  SET(EMGUTF_LITE_MACOS_NUGET_FILE_LIST "${EMGUTF_LITE_MACOS_NUGET_FILE_LIST}
	<file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />")
  SET(EMGUTF_LITE_MACOS_NUGET_DEPENDENCY "${EMGUTF_LITE_MACOS_NUGET_DEPENDENCY}
    <dependencies>
		<dependency id=\"${EMGUTF_LITE_NUGET_ID}\" version=\"${CPACK_PACKAGE_DEPENDENCY_VERSION}\" />
    </dependencies>")	
ENDIF()

IF (HAVE_UBUNTU_X86_64_LITE OR HAVE_UBUNTU_ARM64_LITE)
  IF (HAVE_UBUNTU_X86_64_LITE)
    SET(EMGUTF_LITE_UBUNTU_NUGET_FILE_LIST "${EMGUTF_LITE_UBUNTU_NUGET_FILE_LIST} 
      <file src=\"..\\..\\..\\lib\\runtimes\\ubuntu-x64\\native\\*.so\" target=\"runtimes\\runtimes\\ubuntu-x64\\native\" />")
  ENDIF()
  IF (HAVE_UBUNTU_ARM64_LITE)
    SET(EMGUTF_LITE_UBUNTU_NUGET_FILE_LIST "${EMGUTF_LITE_UBUNTU_NUGET_FILE_LIST} 
      <file src=\"..\\..\\..\\lib\\runtimes\\ubuntu-arm64\\native\\*.so\" target=\"runtimes\\runtimes\\ubuntu-arm64\\native\" />")
  ENDIF()
  SET(EMGUTF_LITE_UBUNTU_NUGET_FILE_LIST "${EMGUTF_LITE_UBUNTU_NUGET_FILE_LIST} 
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />
	<file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />")
	#<file src=\"Emgu.TF.Lite.runtime.ubuntu.targets\" target=\"build\\${EMGUTF_LITE_UBUNTU_NUGET_ID}.targets\" />")
  SET(EMGUTF_LITE_UBUNTU_NUGET_DEPENDENCY "${EMGUTF_LITE_UBUNTU_NUGET_DEPENDENCY}
    <dependencies>
		<dependency id=\"${EMGUTF_LITE_NUGET_ID}\" version=\"${CPACK_PACKAGE_DEPENDENCY_VERSION}\" />
    </dependencies>")	
ENDIF()

IF (HAVE_LINUX_ARM_LITE OR HAVE_LINUX_ARM64_LITE)
  SET(EMGUTF_LITE_DEBIAN_NUGET_DEPENDENCY "${EMGUTF_LITE_DEBIAN_NUGET_DEPENDENCY}
    <dependencies>
		<dependency id=\"${EMGUTF_LITE_NUGET_ID}\" version=\"${CPACK_PACKAGE_DEPENDENCY_VERSION}\" />
    </dependencies>")	
	
  IF(HAVE_LINUX_ARM_LITE)
    SET(EMGUTF_LITE_DEBIAN_ARM_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.debian-arm")
    SET(EMGUTF_LITE_DEBIAN_ARM_NUGET_TITLE "Native ARM runtime of Emgu TF Lite for Debian (including Raspberry Pi OS)")
    SET(EMGUTF_LITE_DEBIAN_ARM_NUGET_FILE_LIST "${EMGUTF_LITE_DEBIAN_ARM_NUGET_FILE_LIST} 
    <file src=\"..\\..\\..\\lib\\runtimes\\${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM}\\native\\*.so\" target=\"runtimes\\${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM}\\native\" />
	<file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />")
  ENDIF()

  IF(HAVE_LINUX_ARM64_LITE)
    SET(EMGUTF_LITE_DEBIAN_ARM64_NUGET_ID "${EMGUTF_LITE_NUGET_ID}.runtime.debian-arm64")
    SET(EMGUTF_LITE_DEBIAN_ARM64_NUGET_TITLE "Native ARM64 runtime of Emgu TF Lite for Debian (including Raspberry Pi OS)")  
    SET(EMGUTF_LITE_DEBIAN_ARM64_NUGET_FILE_LIST "${EMGUTF_LITE_DEBIAN_ARM64_NUGET_FILE_LIST} 
    <file src=\"..\\..\\..\\lib\\runtimes\\${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64}\\native\\*.so\" target=\"runtimes\\${NUGET_NATIVE_PLATFORM_ID_DEBIAN_ARM64}\\native\" />
	<file src=\"..\\..\\..\\${EMGUTF_LICENSE_FILE}\" target=\"\" />
	<file src=\"..\\..\\..\\miscellaneous\\nuget_icon.png\" target=\"icon.png\" />")
  ENDIF()  
ENDIF()

  #Sign Binary
  SET(EMGUTF_POST_PROCESS_BINARY "")
  IF(EMGU_SIGN_FOUND AND WIN32)
    file(TO_NATIVE_PATH ${EMGU_SIGN_EXECUTABLE} EMGU_SIGN_EXECUTABLE_NATIVE_PATH)
    file(TO_NATIVE_PATH ${SIGNTOOL_EXECUTABLE} SIGNTOOL_EXECUTABLE_NATIVE_PATH)
	  SET(EMGUTF_POST_PROCESS_BINARY "${EMGUTF_POST_PROCESS_BINARY}
	<Target Name=\"PostProcessBinary\" AfterTargets=\"PostBuildEvent\" Condition=\"Exists('${EMGU_SIGN_EXECUTABLE_NATIVE_PATH}')\">
    <Message Text=\"Signing $(OutputPath)$(AssemblyName).dll\" Importance=\"high\" />
	<MakeDir Directories=\"$(OutputPath)\\signed\"/>
    <Exec Command='${EMGU_SIGN_EXECUTABLE_NATIVE_PATH} \"$(OutputPath)$(AssemblyName).dll\" \"$(OutputPath)signed\" \"${SIGNTOOL_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(OutputPath)\" />
	</Target>")
	#  SET(EMGUTF_MODELS_POST_PROCESS_BINARY "<Target Name=\"PostProcessBinary\" AfterTargets=\"PostBuildEvent\">
    #<Message Text=\"Signing $(OutputPath)$(AssemblyName).dll\" Importance=\"high\" />
	#<MakeDir Directories=\"$(OutputPath)\\signed\"/>
    #<Exec Command='${EMGU_SIGN_EXECUTABLE_NATIVE_PATH} \"$(OutputPath)$(AssemblyName).dll\" \"$(OutputPath)signed\" \"${SIGNTOOL_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(OutputPath)\" />
	#</Target>")
	#  SET(EMGUTF_PROTOBUF_POST_PROCESS_BINARY "<Target Name=\"PostProcessBinary\" AfterTargets=\"PostBuildEvent\">
    #<Message Text=\"Signing $(OutputPath)$(AssemblyName).dll\" Importance=\"high\" />
	#<MakeDir Directories=\"$(OutputPath)\\signed\"/>
    #<Exec Command='${EMGU_SIGN_EXECUTABLE_NATIVE_PATH} \"$(OutputPath)$(AssemblyName).dll\" \"$(OutputPath)signed\" \"${SIGNTOOL_EXECUTABLE_NATIVE_PATH}\"' WorkingDirectory=\"$(OutputPath)\" />
	#</Target>")
  ENDIF()



IF (HAVE_WINDESKTOP_X64_LITE)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/${EMGU_WINDOWS_MSVCRT_X64_NUGET_ID}.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/${EMGU_WINDOWS_MSVCRT_X64_NUGET_ID}/Package.nuspec)
    #CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/${EMGU_WINDOWS_MSVCRT_X64_NUGET_ID}.targets.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/${EMGU_WINDOWS_MSVCRT_X64_NUGET_ID}/${EMGU_WINDOWS_MSVCRT_X64_NUGET_ID}.targets)
ENDIF()

IF (HAVE_WINDESKTOP_X86_LITE)
    CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/${EMGU_WINDOWS_MSVCRT_X86_NUGET_ID}.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/${EMGU_WINDOWS_MSVCRT_X86_NUGET_ID}/Package.nuspec)    
ENDIF()

# Emgu.LiteRT.Tflite: the TensorFlow Lite Interpreter API wrapper (Emgu.TF.Lite namespace), its
# own nuget package (depends on Emgu.LiteRT.Util - see its csproj's ProjectReference). The
# signing targets in EMGUTF_LITE_POST_PROCESS_* only refer to $(AssemblyName)/$(PackageId), so
# they are reused as-is for every self-packing project below.
SET(EMGUTF_LITE_TFLITE_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}")
SET(EMGUTF_LITE_TFLITE_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite/NetStandard/docs/README.md)

# Emgu.LiteRT.Util: the shared UnmanagedObject/Toolbox base both Emgu.LiteRT.Tflite and Emgu.LiteRT
# depend on. Its own nuget package too, with no managed dependencies of its own.
SET(EMGUTF_LITERT_UTIL_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}")
SET(EMGUTF_LITERT_UTIL_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.Util.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Util/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Util/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Util/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.Util.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Util/NetStandard/docs/README.md)

# Emgu.LiteRT: the C# wrapper of LiteRT's own runtime C API (libLiteRt). Its own nuget package
# depends on Emgu.LiteRT.Util (its UnmanagedObject-derived types need it to compile); it has no
# code dependency on Emgu.LiteRT.Tflite, which is a fully independent package.
SET(EMGUTF_LITERT_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}")
SET(EMGUTF_LITERT_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT/NetStandard/docs/README.md)

# Emgu.LiteRT.LM: the C# wrapper of LiteRT-LM's C API (liblitert-lm). Its own nuget package,
# depending on Emgu.LiteRT.Util only.
SET(EMGUTF_LITERT_LM_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}")
SET(EMGUTF_LITERT_LM_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.LM.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.LM.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM/NetStandard/docs/README.md)

# Emgu.LiteRT.LM.Models: ready-to-use language models (download + chat). Its own nuget package, depending on
# Emgu.LiteRT.LM and Emgu.LiteRT.Util.
SET(EMGUTF_LITERT_LM_MODELS_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}")
SET(EMGUTF_LITERT_LM_MODELS_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.LM.Models.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM.Models/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM.Models/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM.Models/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.LM.Models.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.LM.Models/NetStandard/docs/README.md)

# Emgu.LiteRT.ImageIO: NativeImageIO/JpegData (renamed from Emgu.Models). Its own nuget package,
# depending on Emgu.LiteRT.Util (Annotation) and Emgu.LiteRT.Tflite (Emgu.TF.Lite.Toolbox's
# native pixel-conversion P/Invokes).
SET(EMGUTF_LITE_IMAGEIO_POST_PROCESS_BINARY "${EMGUTF_LITE_POST_PROCESS_BINARY}")
SET(EMGUTF_LITE_IMAGEIO_POST_PROCESS_NUGET "${EMGUTF_LITE_POST_PROCESS_NUGET}")
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.ImageIO.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.ImageIO/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.ImageIO/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.ImageIO/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.ImageIO.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.ImageIO/NetStandard/docs/README.md)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Models.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite.Models/NetStandard/Directory.Build.props)
IF(EMGU_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignBinary.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite.Models/NetStandard/SignBinary.Targets)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND AND WIN32)
  CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/SignPkg.Targets.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite.Models/NetStandard/SignPkg.Targets)
ENDIF()
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Models.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Tflite.Models/NetStandard/docs/README.md)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.windows.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.runtime.windows/Package.nuspec)
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.windows.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.runtime.windows/README.md)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.Tflite.runtime.windows.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.Tflite.runtime.windows/Package.nuspec)
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.Tflite.runtime.windows.Package.README.md.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.Tflite.runtime.windows/README.md)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.macos.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.runtime.macos/Package.nuspec)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.ubuntu.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.runtime.ubuntu/Package.nuspec)
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.debian-arm.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.runtime.debian-arm/Package.nuspec)
CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.LiteRT.runtime.debian-arm64.Package.nuspec.in ${CMAKE_CURRENT_SOURCE_DIR}/platforms/nuget/Emgu.LiteRT.runtime.debian-arm64/Package.nuspec)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Test.Net.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.TF.Test/Emgu.TF.Lite.Test/Emgu.TF.Lite.Test.Net/Directory.Build.props)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.Inception.Console.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Example/Inception.Console.Lite/Directory.Build.props)

CONFIGURE_FILE(${CMAKE_CURRENT_SOURCE_DIR}/cmake/Emgu.TF.Lite.CVInterop.Directory.Build.props.in ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.Example/CVInterop.Lite/Directory.Build.props)

IF(TARGET_ARCH_64)
  SET(UNMANAGED_CODE_BITNESS "64")
ELSE()
  SET(UNMANAGED_CODE_BITNESS "32")
ENDIF()


# ----------------------------------------------------------------------------
#  Build the managed dlls of Emgu TF
# ----------------------------------------------------------------------------

ADD_SUBDIRECTORY(Emgu.LiteRT.Util)
IF(HAVE_LITE_BUILD)
  # Emgu.TF.Lite/ was renamed to Emgu.LiteRT.Tflite/ - the namespace/project/csproj name stays
  # Emgu.TF.Lite (it's the TFLite Interpreter API wrapper), only the folder moved.
  ADD_SUBDIRECTORY(Emgu.LiteRT.Tflite)
  ADD_SUBDIRECTORY(Emgu.LiteRT)
  ADD_SUBDIRECTORY(Emgu.LiteRT.LM)
  ADD_SUBDIRECTORY(Emgu.LiteRT.LM.Models)
  # Emgu.Models/ was renamed to Emgu.LiteRT.ImageIO/ and made its own project+package (it used
  # to be shared source imported directly into Emgu.LiteRT.Tflite.Models.csproj and
  # CVInterop.Lite.csproj). Depends on Emgu.LiteRT.Tflite (Emgu.TF.Lite.Toolbox), so must come
  # after it and stay inside this HAVE_LITE_BUILD block.
  ADD_SUBDIRECTORY(Emgu.LiteRT.ImageIO)
  ADD_SUBDIRECTORY(Emgu.LiteRT.Tflite.Models)
ENDIF()

IF (HAVE_LITE_BUILD)
  ADD_SUBDIRECTORY(platforms)
ENDIF()

#Add MAUI project
ADD_SUBDIRECTORY(Emgu.LiteRT.Runtime)

ADD_SUBDIRECTORY(Emgu.LiteRT.Example)

INSTALL(
  FILES
  "litert/LICENSE"
  DESTINATION "litert"
  COMPONENT emgutf_source
  )

IF(HAVE_ANDROID_LITE)
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}/Solution/Android
    DESTINATION Solution
    COMPONENT emgutf_source
    FILES_MATCHING 
    PATTERN "Emgu.TF.Lite.Android.sln"
    PATTERN ".git" EXCLUDE
    PATTERN "bin" EXCLUDE
    )
#  INSTALL(
#    DIRECTORY
#    ${CMAKE_CURRENT_SOURCE_DIR}/Solution/Android
#    DESTINATION Solution
#    COMPONENT emgutf_example_source
#    FILES_MATCHING 
#	PATTERN "Emgu.TF.Lite.Android.sln"
#    PATTERN ".git" EXCLUDE
#    PATTERN "bin" EXCLUDE
#    )
ENDIF()

IF(HAVE_IOS_LITE)
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}/Solution/iOS
    DESTINATION Solution
    COMPONENT emgutf_source
    FILES_MATCHING 
    PATTERN "Emgu.TF.Lite.iOS.sln"
    PATTERN ".git" EXCLUDE
    PATTERN "bin" EXCLUDE
    )
ENDIF()

IF(HAVE_OSX_LITE)
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}/Solution/macos
    DESTINATION Solution
    COMPONENT emgutf_source
    FILES_MATCHING 
    PATTERN "Emgu.TF.Lite.Mac.sln"
    PATTERN ".git" EXCLUDE
    PATTERN "bin" EXCLUDE
    )
ENDIF()

IF(HAVE_WINDESKTOP_LITE)
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}/Solution/Windows.Desktop
    DESTINATION Solution
    COMPONENT emgutf_source
    FILES_MATCHING 
    PATTERN "Emgu.TF.Lite.sln"
    PATTERN ".git" EXCLUDE
    PATTERN "bin" EXCLUDE
    )
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}/Solution/Windows.Desktop
    DESTINATION Solution
    COMPONENT emgutf_example_source
    FILES_MATCHING 
    PATTERN "Emgu.TF.Lite.Example.sln"
    PATTERN ".git" EXCLUDE
    PATTERN "bin" EXCLUDE
    )
ENDIF()

INSTALL(
  FILES
  "${CMAKE_CURRENT_SOURCE_DIR}/Emgu.TF.snk"
  "${CMAKE_CURRENT_SOURCE_DIR}/CommonAssemblyInfo.cs"
  "${CMAKE_CURRENT_SOURCE_DIR}/Directory.Build.props"
  "${CMAKE_CURRENT_SOURCE_DIR}/${LICENSE_FILE_NAME}" #LICENSE
  "${CMAKE_CURRENT_SOURCE_DIR}/${EMGUTF_LICENSE_FILE}" #LICENSE.txt
  DESTINATION .
  COMPONENT emgutf_source
  )

INSTALL(
  FILES 
  "${CMAKE_CURRENT_SOURCE_DIR}/miscellaneous/nuget_icon.png" 
  DESTINATION "miscellaneous/" 
  COMPONENT emgutf_source)


SET(CPACK_GENERATOR ZIP)
#IF(WIN32 AND (NOT NETFX_CORE))
#  SET(CPACK_GENERATOR NSIS;ZIP)
#ENDIF()
SET(CPACK_ARCHIVE_COMPONENT_INSTALL ON) #enable components install for zip
IF(CPACK_GENERATOR MATCHES "ZIP")
  SET(CPACK_COMPONENTS_ALL_IN_ONE_PACKAGE 1)
ENDIF()
  
# ----------------------------------------------------------------------------
#  The binary files
# ----------------------------------------------------------------------------
INSTALL(
  DIRECTORY
  ${CMAKE_CURRENT_SOURCE_DIR}/lib
  DESTINATION .
  COMPONENT emgutf_binary
  FILES_MATCHING 
  PATTERN "*.dll"
  PATTERN "*.dylib"
  PATTERN "*.so"
  PATTERN "*.so.*"
  PATTERN "*.txt"
  PATTERN "*.xml"
  PATTERN ".git" EXCLUDE
  PATTERN "obj" EXCLUDE
  PATTERN "CMake*" EXCLUDE
  PATTERN "Release" EXCLUDE
  PATTERN "Debug" EXCLUDE
  PATTERN "Build" EXCLUDE
  PATTERN "signed" EXCLUDE
  PATTERN "${PROJECT_NAME}.dir" EXCLUDE
  ) 

set(CPACK_COMPONENTS_ALL 
  emgutf_binary 
  emgutf_source
  emgutf_example_source
  )

IF (WIN32)
  #SET(CPACK_NSIS_MODIFY_PATH OFF)
  #SET(CPACK_NSIS_INSTALL_ROOT "C:\\\\Emgu")
  
  # ----------------------------------------------------------------------------
  #  Build the documents of Emgu TF
  # ----------------------------------------------------------------------------
  SET(EMGU_TF_DOCUMENTATION_BUILD OFF CACHE BOOL "Build Emgu TF Documentation")
  IF(EMGU_TF_DOCUMENTATION_BUILD)
    ADD_SUBDIRECTORY(miscellaneous)
  ENDIF()
  
  # ----------------------------------------------------------------------------
  #  Build the package
  # ----------------------------------------------------------------------------
    
  set(CPACK_PACKAGE_EXECUTABLES "" "") #http://public.kitware.com/Bug/view.php?id=7828
  
  #SET(CPACK_NSIS_CONTACT "support@emgu.com")
  
  # Define MUI_TEMP that will be used for uninstalling menulinks
  #SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n !insertmacro MUI_STARTMENU_GETFOLDER Application $MUI_TEMP")
  
  # ----------------------------------------------------------------------------
  #  Add menu link for documentations
  # ----------------------------------------------------------------------------
  
  
  IF(EMGU_TF_DOCUMENTATION_BUILD)
    #SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateDirectory \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\Documentation\\\" ")
    
    LIST(APPEND CPACK_COMPONENTS_ALL emgutf_document)
    set(CPACK_COMPONENT_EMGUTF_DOCUMENT_DISPLAY_NAME "Emgu TF Documentation")
    set(CPACK_COMPONENT_EMGUTF_DOCUMENT_DEPENDS emgutf_binary)
    #IF (HAVE_WINDESKTOP)
    #  SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateShortCut \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\Documentation\\\\Emgu TF Documentation.lnk\\\"  \\\"$INSTDIR\\\\Emgu.TF.Documentation.chm\\\" ")	
    #  SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n Delete \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\Documentation\\\\Emgu TF Documentation.lnk\\\" ")
    #ENDIF()
    #IF (HAVE_WINDESKTOP_LITE)
    #  SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateShortCut \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\Documentation\\\\Emgu TF Lite Documentation.lnk\\\"  \\\"$INSTDIR\\\\Emgu.TF.Lite.Documentation.chm\\\" ")	
    #  SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n Delete \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\Documentation\\\\Emgu TF Lite Documentation.lnk\\\" ")
    #ENDIF()
    
    #SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\nRMDir  \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\Documentation\\\" ")
  ENDIF()
  
  # ----------------------------------------------------------------------------
  #  Add menu link for web sites
  # ----------------------------------------------------------------------------
  #LIST(APPEND CPACK_NSIS_MENU_LINKS "${EMGUTF_MAIN_WEB_URL}" "Emgu TF wiki")
  #LIST(APPEND CPACK_NSIS_MENU_LINKS "https://github.com/emgucv/emgutf/discussions" "Discussions")
  #LIST(APPEND CPACK_NSIS_MENU_LINKS "https://github.com/emgucv/emgutf/issues" "Issues Tracking")
  
  # ----------------------------------------------------------------------------
  #  Add menu link for Visual Studio solutions 
  # ----------------------------------------------------------------------------
  #SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateDirectory \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\Visual Studio Solution\\\" ")
  #IF (HAVE_WINDESKTOP)
  #  SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateShortCut \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\Visual Studio Solution\\\\Emgu TF Visual Studio Examples.lnk\\\"  \\\"$INSTDIR\\\\Solution\\\\Windows.Desktop\\\\Emgu.TF.Example.sln\\\" ")	
  #  SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n Delete \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\Visual Studio Solution\\\\Emgu TF Visual Studio Examples.lnk\\\" ")
  #ENDIF()
  #IF (HAVE_WINDESKTOP_LITE)
  #  SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateShortCut \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\Visual Studio Solution\\\\Emgu TF Lite Visual Studio Examples.lnk\\\"  \\\"$INSTDIR\\\\Solution\\\\Windows.Desktop\\\\Emgu.TF.Lite.Example.sln\\\" ")	
  #  SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n Delete \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\Visual Studio Solution\\\\Emgu TF Lite Visual Studio Examples.lnk\\\" ")
  #ENDIF()
  
  #SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\nRMDir  \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\Visual Studio Solution\\\" ")
  
  # ----------------------------------------------------------------------------
  #  Add menu link for Licenses 
  # ----------------------------------------------------------------------------
  #SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateDirectory \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\License\\\" ")
  
  #SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateShortCut \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\License\\\\Emgu TF License.lnk\\\"  \\\"$INSTDIR\\\\${LICENSE_FILE_NAME}\\\" ")	
  #SET(CPACK_NSIS_EXTRA_INSTALL_COMMANDS "${CPACK_NSIS_EXTRA_INSTALL_COMMANDS}\nCreateShortCut \\\"$SMPROGRAMS\\\\$STARTMENU_FOLDER\\\\License\\\\Tensorflow License.lnk\\\"  \\\"$INSTDIR\\\\tensorflow\\\\LICENSE\\\" ")
  
  #SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n Delete \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\License\\\\Emgu TF License.lnk\\\" ")
  #SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\n Delete \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\License\\\\Tensorflow License.lnk\\\" ")
  
  #SET(CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS "${CPACK_NSIS_EXTRA_UNINSTALL_COMMANDS}\nRMDir  \\\"$SMPROGRAMS\\\\$MUI_TEMP\\\\License\\\" ")
  
  #IF(EMGU_TF_EXAMPLE_BUILD AND NOT NETFX_CORE)
  #  LIST(APPEND CPACK_COMPONENTS_ALL emgutf_example_binary)
  #  SET(CPACK_COMPONENT_EMGUTF_EXAMPLE_BINARY_DISPLAY_NAME "Emgu TF Examples (Binary)")
  #  SET(CPACK_COMPONENT_EMGUTF_EXAMPLE_BINARY_DEPENDS emgutf_binary)
  #ENDIF()
  
  set(CPACK_COMPONENT_MAIN_DISPLAY_NAME "Tensorflow Native Binary")
  set(CPACK_COMPONENT_MAIN_REQUIRED ON)
  set(CPACK_COMPONENT_EMGUTF_BINARY_DISPLAY_NAME "Emgu TF (Binary)")
  set(CPACK_COMPONENT_EMGUTF_BINARY_REQUIRED ON)
  #set(CPACK_COMPONENT_EMGUTF_BINARY_DEPENDS libs)
  set(CPACK_COMPONENT_EMGUTF_SOURCE_DISPLAY_NAME "Emgu TF (Source)")
  set(CPACK_COMPONENT_EMGUTF_SOURCE_DEPENDS emgutf_binary)
  set(CPACK_COMPONENT_EMGUTF_EXAMPLE_SOURCE_DISPLAY_NAME "Emgu TF Examples (Source)")
  set(CPACK_COMPONENT_EMGUTF_EXAMPLE_SOURCE_DEPENDS emgutf_source)
  
  IF (${CMAKE_VERSION} VERSION_GREATER "3.19.0")
    IF(EMGU_SIGN_FOUND AND WIN32)
	  #SET(NSIS_PACKAGE_FILE ${CMAKE_BINARY_DIR}/lib${CPACK_PACKAGE_NAME}-${CPACK_PACKAGE_VERSION}.exe)
      #MESSAGE(STATUS "CPACK_PACKAGE_FILES: ${NSIS_PACKAGE_FILE}")
	  STRING(REGEX REPLACE "/" "\\\\\\\\" WIN_CMAKE_COMMAND "${CMAKE_COMMAND}" )
	  STRING(REGEX REPLACE "/" "\\\\\\\\" WIN_EMGU_SIGN_EXECUTABLE "${EMGU_SIGN_EXECUTABLE}")
	  STRING(REGEX REPLACE "/" "\\\\\\\\" WIN_PACKAGE_FILE "${CMAKE_BINARY_DIR}/lib${CPACK_PACKAGE_NAME}-${CPACK_PACKAGE_VERSION}.exe")
	  STRING(REGEX REPLACE "/" "\\\\\\\\" WIN_PACKAGE_FOLDER "${CMAKE_BINARY_DIR}/signed")
	  STRING(REGEX REPLACE "/" "\\\\\\\\" WIN_SIGNTOOL_EXECUTABLE "${SIGNTOOL_EXECUTABLE}")
	  #FILE(WRITE "${CMAKE_BINARY_DIR}/sign_package.txt" "add_custom_command(OUTPUT ${WIN_PACKAGE_FOLDER}/lib${CPACK_PACKAGE_NAME}-${CPACK_PACKAGE_VERSION}.exe\n")
	  FILE(WRITE "${CMAKE_BINARY_DIR}/sign_package.cmake" "EXECUTE_PROCESS(COMMAND \"${WIN_CMAKE_COMMAND}\" -E make_directory \"${CMAKE_BINARY_DIR}/signed\")\n")
	  FILE(APPEND "${CMAKE_BINARY_DIR}/sign_package.cmake" "EXECUTE_PROCESS(COMMAND \"${WIN_EMGU_SIGN_EXECUTABLE}\" \"${WIN_PACKAGE_FILE}\" \"${WIN_PACKAGE_FOLDER}\" \"${WIN_SIGNTOOL_EXECUTABLE}\"  )")
      LIST(APPEND CPACK_POST_BUILD_SCRIPTS "${CMAKE_BINARY_DIR}/sign_package.cmake")
	  #LIST(APPEND CPACK_POST_BUILD_SCRIPTS "${WIN_EMGU_SIGN_EXECUTABLE} ${CMAKE_BINARY_DIR}/lib${CPACK_PACKAGE_NAME}-${CPACK_PACKAGE_VERSION}.exe ${CMAKE_BINARY_DIR}/signed")
	ENDIF()
  ENDIF()
ELSEIF (APPLE)
ELSE() #LINUX
ENDIF()  

MESSAGE(STATUS "CPACK_GENERATOR: ${CPACK_GENERATOR}")
INCLUDE (CPack) 
