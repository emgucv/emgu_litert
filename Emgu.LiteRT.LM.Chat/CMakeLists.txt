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
    PROJECT(Emgu.LiteRT.LM.Chat)
    BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/NetStandard/Emgu.LiteRT.LM.Chat.csproj" "" ALL)
    # Emgu.LiteRT.LM.Chat.csproj depends on Emgu.LiteRT.LM.Extensions.AI and Emgu.LiteRT.LM.Models.
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM.Extensions.AI)

    INSTALL(
      DIRECTORY
      ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.Chat
      DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
      COMPONENT emgulitert_binary)
  ENDIF()
ENDIF()
