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

  # A MAUI project, built on Windows or macOS only, as Emgu.LiteRT.LM.Maui.
  IF (DOTNET_FOUND AND (APPLE OR WIN32))
    PROJECT(Emgu.LiteRT.LM.Maui.Images)
    BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/Maui/Emgu.LiteRT.LM.Maui.Images.csproj" "/p:Platform=\"AnyCPU\"" ALL)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.Maui Emgu.LiteRT.Models)

    INSTALL(
      DIRECTORY
      ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.Maui.Images
      DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
      COMPONENT emgulitert_binary)
  ENDIF()
ENDIF()
