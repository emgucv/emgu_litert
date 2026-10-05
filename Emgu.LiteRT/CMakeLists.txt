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
    PROJECT(Emgu.LiteRT)
    BUILD_DOTNET_PROJ(${PROJECT_NAME} "${CMAKE_CURRENT_SOURCE_DIR}/NetStandard/Emgu.LiteRT.csproj" "" ALL)
    # Emgu.LiteRT.csproj's UnmanagedObject-derived types need Emgu.LiteRT.Util to compile; it has
    # no dependency on Emgu.TF.Lite (Emgu.LiteRT.Tflite is a fully independent package).
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.Util)
  
    INSTALL(
      DIRECTORY
      ${LIBRARY_OUTPUT_PATH}/Build/Emgu.LiteRT
      DESTINATION ${CPACK_PACKAGE_CLI_FOLDER}
      COMPONENT emgutf_binary)
  ENDIF()
ENDIF()
