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
    PATTERN "*.resx"
    PATTERN "README.md"
    PATTERN "Directory.Build.props"
    PATTERN ".svn" EXCLUDE
    PATTERN "obj" EXCLUDE
    PATTERN "CMakeFiles" EXCLUDE
    PATTERN "${PROJECT_NAME}.dir" EXCLUDE
    )

  IF (DOTNET_FOUND)
    PROJECT(Emgu.LiteRT.LM.Agents.CV)
    BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/NetStandard/Emgu.LiteRT.LM.Agents.CV.csproj" "" ALL)
    # Emgu.LiteRT.LM.Agents.CV.csproj depends on Emgu.LiteRT.LM.Agents and Emgu.LiteRT.Models.CV.
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.Agents Emgu.LiteRT.Models.CV)

    INSTALL(
      DIRECTORY
      ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.Agents.CV
      DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
      COMPONENT emgulitert_binary)
  ENDIF()
ENDIF()
