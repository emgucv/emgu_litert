# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

IF(HAVE_LITE_BUILD)
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}
    DESTINATION .
    COMPONENT emgulitert_source
    FILES_MATCHING
    PATTERN "*.cs"
    PATTERN "*.xaml"
    PATTERN "*.shproj"
    PATTERN "*.csproj"
    PATTERN "*.projitems"
    PATTERN "README.md"
    PATTERN "Directory.Build.props"
    PATTERN ".svn" EXCLUDE
    PATTERN "obj" EXCLUDE
    PATTERN "bin" EXCLUDE
    PATTERN "CMakeFiles" EXCLUDE
    PATTERN "${PROJECT_NAME}.dir" EXCLUDE
    )

  # Only build MAUI projects on Windows or macOS, as for the MAUI runtime projects. It has no native runtime
  # dependency, so it doesn't need HAVE_MAUI_LITE (every platform's native build).
  IF (DOTNET_FOUND AND (APPLE OR WIN32))
    PROJECT(Emgu.LiteRT.LM.Maui)
    BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/Maui/Emgu.LiteRT.LM.Maui.csproj" "/p:Platform=\"AnyCPU\"" ALL)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.Chat)

    INSTALL(
      DIRECTORY
      ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.Maui
      DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
      COMPONENT emgulitert_binary)
  ENDIF()
ENDIF()
