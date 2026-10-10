# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

ADD_SUBDIRECTORY(Android)
ADD_SUBDIRECTORY(iOS)
ADD_SUBDIRECTORY(Mac)

# Only build MAUI projects on Windows or MacOS. MAUI compilation on Linux seems to be a problem.
# Also only build when there's an actual LiteRT-LM binary to reference for at least one platform
# (Windows' liblitert-lm.dll, since Android's liblitert-lm.so is Git LFS/build-dependent too, but
# gated at the MSBuild EXISTS level inside Emgu.LiteRT.LM.runtime.maui.android itself).
IF(HAVE_MAUI_LITE AND HAVE_WINDOWS_LITERT_LM AND (APPLE OR WIN32))
  PROJECT(Emgu.LiteRT.LM.runtime.maui)
  INSTALL(
    DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    DESTINATION Emgu.TF.Platform/Maui/LM
    COMPONENT emgulitert_source
    FILES_MATCHING
	PATTERN "*.cs"
    PATTERN "${PROJECT_NAME}.csproj"
    PATTERN ".svn" EXCLUDE
    PATTERN "obj" EXCLUDE
    PATTERN "CMakeFiles" EXCLUDE
    PATTERN "${PROJECT_NAME}.dir" EXCLUDE
    )
  SET(EXTRA_BUILD_FLAGS "/p:Platform=\"AnyCPU\"")

  BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/${PROJECT_NAME}.csproj" "${EXTRA_BUILD_FLAGS}" ALL)

  IF (TARGET Emgu.LiteRT.LM.runtime.maui.android)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.runtime.maui.android)
  ENDIF()

  IF (TARGET Emgu.LiteRT.LM.runtime.maui.ios)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.runtime.maui.ios)
  ENDIF()

  IF (TARGET Emgu.LiteRT.LM.runtime.maui.macos)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.runtime.maui.macos)
  ENDIF()

  IF (TARGET Emgu.LiteRT.LM.runtime.windows.nuget)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.runtime.windows.nuget)
  ENDIF()

  # Not a code dependency - Emgu.LiteRT.LM.runtime.maui.csproj has no ProjectReference on
  # Emgu.LiteRT.runtime.maui - but build after it so the two dotnet build/pack invocations
  # don't race on the shared platforms/nuget restore-source folder.
  IF (TARGET Emgu.LiteRT.runtime.maui)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.runtime.maui)
  ENDIF()

  INSTALL(
    DIRECTORY
    ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.runtime.maui
    DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
    COMPONENT emgulitert_binary)

ENDIF()
