# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

IF(HAVE_WINDESKTOP_LITE)

PROJECT(Emgu.LiteRT.Tflite.runtime.windows.nuget)

  get_filename_component(NUGET_OUTPUT_DIR ${CMAKE_CURRENT_SOURCE_DIR} DIRECTORY)
  BUILD_NUGET_PACKAGE(
    ${PROJECT_NAME}
	"${CMAKE_CURRENT_SOURCE_DIR}/../../../Emgu.LiteRT/NetStandard/Emgu.LiteRT.csproj"  #csproj_file
	"${CMAKE_CURRENT_SOURCE_DIR}/Package.nuspec" #nuspec_file
	"${NUGET_OUTPUT_DIR}" #output_dir
	"${CMAKE_CURRENT_SOURCE_DIR}" #working_dir
	)

  IF (EMGU_NUGET_SIGN_FOUND)
    EMGU_SIGN_NUGET(${PROJECT_NAME} "${NUGET_OUTPUT_DIR}/${EMGUTF_LITE_TFLITE_WINDOWS_NUGET_ID}.${CPACK_PACKAGE_VERSION}${TENSORFLOW_VERSION_SUFFIX}.nupkg")
  ENDIF()

  # tfliteextern.dll's own dependency, Emgu.LiteRT.runtime.windows (libLiteRt.dll), is expressed
  # in Package.nuspec's <dependencies> for the published nuget package; this is just the CMake
  # build-order dependency so its nupkg exists on disk first.
  IF (TARGET Emgu.LiteRT.runtime.windows.nuget)
  	ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.runtime.windows.nuget)
  ENDIF()

if(ENABLE_SOLUTION_FOLDERS)
  set_target_properties(${PROJECT_NAME} PROPERTIES FOLDER "NUGET")
endif()

ENDIF()
