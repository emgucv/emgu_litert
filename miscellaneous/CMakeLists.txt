# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

#IF(HAVE_WINDESKTOP OR HAVE_WINDESKTOP_LITE)
#  #MESSAGE(STATUS "MSBUILD_EXECUTABLE_40: ${MSBUILD_EXECUTABLE_40}")
#  IF(NOT ("${MSBUILD_EXECUTABLE_150}" STREQUAL ""))
#    SET(MSBUILD_EXECUTABLE ${MSBUILD_EXECUTABLE_150})
#  ELSE()
#    MESSAGE(STATUS "Not able to find MSBUILD 150, Documentation build may not produce the right output.")
#  ENDIF()
#ENDIF()

IF("${MSBUILD_EXECUTABLE}" STREQUAL "")
  MESSAGE(STATUS "MSBUILD_EXECUTABLE NOT FOUND, skipping documentation projects.")
ELSE()
  IF(HAVE_WINDESKTOP_LITE)
    PROJECT(Emgu.TF.Lite.Document)

    # Emgu.LiteRT.html.shfbproj's own HelpFileFormat is "HtmlHelp1, Website", so this first
    # pass produces both the .chm and the website in one build. Emgu.LiteRT.OpenXml.shfbproj is
    # a separate, dedicated copy of it with PresentationStyle/HelpFileFormat hardcoded to OpenXml
    # for the second pass, rather than overriding those two properties at build time: SHFB's
    # BuildHelp MSBuild task (SandcastleHelpFileBuilder.targets) never forwards a plain
    # /property:PresentationStyle=.../property:HelpFileFormat=... global override to the
    # underlying SandcastleProject, so such an override is silently ignored and SHFB just
    # rebuilds the project's own default format again - meaning that never actually produced
    # OpenXml (Word) output. A single pass can't build HtmlHelp1/Website and OpenXml together
    # either way: SHFB ties HelpFileFormat to PresentationStyle, and no style supports all three
    # (confirmed via SHFB error BE0074 - VS2013 supports HtmlHelp1/MSHelpViewer/Website; OpenXml
    # supports only OpenXml), so a second, separate project file is required.
    ADD_CUSTOM_TARGET(
       ${PROJECT_NAME} ALL
       COMMAND ${MSBUILD_EXECUTABLE} /p:Configuration=Release ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.html.shfbproj
       COMMAND ${MSBUILD_EXECUTABLE} /p:Configuration=Release ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.OpenXml.shfbproj
       SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.html.shfbproj ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.LiteRT.OpenXml.shfbproj
    )
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.TF.Lite Emgu.TF.Lite.Models)

    INSTALL(
      FILES
      "${CMAKE_CURRENT_SOURCE_DIR}/../Help/Emgu.TF.Lite.Documentation.chm"
      DESTINATION .
      COMPONENT emgulitert_document
      )
  ENDIF()
ENDIF()

