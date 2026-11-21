# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------


IF(HAVE_ANDROID_LITE OR HAVE_IOS_LITE OR HAVE_WINDESKTOP_LITE)
  INSTALL(
    DIRECTORY
    "${CMAKE_CURRENT_SOURCE_DIR}/Maui"
    DESTINATION Emgu.LiteRT.Example/Maui
    COMPONENT emgulitert_example_source
    FILES_MATCHING 
    PATTERN "*.cs"
    PATTERN "*.csproj"
    PATTERN "*.config"
    PATTERN "*.shproj"
    PATTERN "*.projitems"
    PATTERN "*.axml"
    PATTERN "*.xaml"
    PATTERN "*.xml"
    PATTERN "*.txt"
    PATTERN "*.png"
    PATTERN "*.dll"
    PATTERN "*.json"
    PATTERN ".git" EXCLUDE
    PATTERN "Debug" EXCLUDE
    PATTERN "Release" EXCLUDE  
    )
ENDIF()
  
# The MAUI demo's Android .apk, built with the other projects (as Emgu CV does for its MAUI demo) and copied to package/.
# One dotnet build takes one target framework, so the other platforms' demos are not built here: the MAUI projects of
# the platform's own build (README.md) do those. The native libraries (lib/android/<abi>/, built by
# platforms/android/bazel_build_litert) must be there first: HAVE_ANDROID_LITE. Needs the Android SDK; turn it off with
# -DEMGU_LITERT_BUILD_MAUI_DEMO_ANDROID=OFF.
OPTION(EMGU_LITERT_BUILD_MAUI_DEMO_ANDROID "Build the MAUI demo's Android .apk" ON)
IF(EMGU_LITERT_BUILD_MAUI_DEMO_ANDROID AND HAVE_ANDROID_LITE AND DOTNET_FOUND AND (WIN32 OR APPLE))
  PROJECT(Example.Maui.Demo.Android)
  BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/Maui/Maui.Demo.Lite.csproj" "/p:TargetFrameworks=net${DOTNET_VERSION_MAJOR}.0-android" ALL)
  SET_TARGET_PROPERTIES(${PROJECT_NAME} PROPERTIES FOLDER "Emgu.LiteRT.Examples")
  # The native libraries and the packages the demo's runtime references come first.
  FOREACH(_dep Emgu.LiteRT.runtime.maui.android Emgu.LiteRT.LM.runtime.maui.android Emgu.LiteRT.OpenCL.runtime.maui.android
      Emgu.LiteRT.LM.Maui Emgu.LiteRT.LM.Maui.CV)
    IF(TARGET ${_dep})
      ADD_DEPENDENCIES(${PROJECT_NAME} ${_dep})
    ENDIF()
  ENDFOREACH()
  ADD_CUSTOM_COMMAND(
    TARGET ${PROJECT_NAME}
    POST_BUILD
    COMMAND ${CMAKE_COMMAND}
      -DSRC_DIR=${CMAKE_CURRENT_SOURCE_DIR}/Maui/bin/${DEFAULT_CS_CONFIG}/net${DOTNET_VERSION_MAJOR}.0-android
      -DDEST_DIR=${CMAKE_SOURCE_DIR}/package
      -P ${CMAKE_SOURCE_DIR}/cmake/CopyMauiApk.cmake
    COMMENT "Copying the MAUI Android demo .apk to ${CMAKE_SOURCE_DIR}/package")
ENDIF()

add_subdirectory(Inception.Console.Lite)

add_subdirectory(CVInterop.Lite)

