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
    PROJECT(Emgu.TF.Lite.Document.Html)
    ADD_CUSTOM_TARGET(
       ${PROJECT_NAME}
       COMMAND ${MSBUILD_EXECUTABLE} /p:Configuration=Release ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.TF.Lite.html.shfbproj
       SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.TF.Lite.html.shfbproj
    )
    
	ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.TF.Lite Emgu.TF.Lite.Models)

    PROJECT(Emgu.TF.Lite.Documentation.chm)
    ADD_CUSTOM_TARGET(
       ${PROJECT_NAME} ALL
       # Emgu.TF.Lite.chm.shfbproj is a dedicated copy of Emgu.TF.Lite.html.shfbproj with
       # OutputPath/HelpFileFormat hardcoded to produce HTML Help 1 (.chm) output into a "chm"
       # subfolder, rather than overriding those two properties at build time: SHFB's BuildHelp
       # MSBuild task (SandcastleHelpFileBuilder.targets) never forwards a plain
       # /property:OutputPath=.../property:HelpFileFormat=... global override to the underlying
       # SandcastleProject, so such overrides are silently ignored and SHFB falls back to
       # whatever the project file itself says - which left this always producing a Website
       # build and never an actual .chm, so CPack's install of it below always failed.
       # (OutputPath also can't just be the project's own directory - SHFB's BE0034 check
       # refuses that as a "reserved folder", since CleanIntermediates would be free to delete
       # files there, including the project's own sources - hence the "chm" subfolder.)
       COMMAND ${MSBUILD_EXECUTABLE} /p:Configuration=Release ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.TF.Lite.chm.shfbproj
       SOURCES ${CMAKE_CURRENT_SOURCE_DIR}/Emgu.TF.Lite.chm.shfbproj
    )
    #Making sure documentation project is build one by one
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.TF.Lite.Document.Html)

    INSTALL(
      FILES
      "${CMAKE_CURRENT_SOURCE_DIR}/chm/Emgu.TF.Lite.Documentation.chm"
      DESTINATION .
      COMPONENT emgutf_document
      )
  ENDIF()
ENDIF()

