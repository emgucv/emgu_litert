# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

IF(HAVE_LITE_BUILD)
  INSTALL(
    DIRECTORY
    ${CMAKE_CURRENT_SOURCE_DIR}
    DESTINATION .
    COMPONENT emgutf_source
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
    PROJECT(Emgu.LiteRT.LM.Models)
    BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/NetStandard/Emgu.LiteRT.LM.Models.csproj" "" ALL)
    # Emgu.LiteRT.LM.Models.csproj builds on Emgu.LiteRT.LM (the LiteRT-LM API) and Emgu.LiteRT.Util
    # (DownloadableFile, FileDownloadManager).
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.LM Emgu.LiteRT.Util)

    INSTALL(
      DIRECTORY
      ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT.LM.Models
      DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
      COMPONENT emgutf_binary)
  ENDIF()
ENDIF()
