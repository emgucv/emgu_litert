# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

IF(HAVE_ANDROID_LITE AND (WIN32 OR APPLE))
  PROJECT(Emgu.LiteRT.LM.runtime.maui.android)
  INSTALL(
    DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    DESTINATION Emgu.LiteRT.Runtime/Maui/LM/Android
    COMPONENT emgulitert_source
    FILES_MATCHING
    PATTERN "*.cs"
    PATTERN "Directory.Build.props"
    PATTERN "${PROJECT_NAME}.csproj"
    PATTERN ".svn" EXCLUDE
    PATTERN "obj" EXCLUDE
    PATTERN "CMakeFiles" EXCLUDE
    PATTERN "${PROJECT_NAME}.dir" EXCLUDE
    )
  SET(EXTRA_BUILD_FLAGS "/p:Platform=\"AnyCPU\"")

  BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/${PROJECT_NAME}.csproj" "${EXTRA_BUILD_FLAGS}" ALL)

  ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.runtime.maui.android)

  INSTALL(
    DIRECTORY
    ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.runtime.maui.android
    DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
    COMPONENT emgulitert_binary)
ENDIF()
