# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

IF(NOT "${EMGU_LITERT_LM_DEBIAN_ARM64_NUGET_FILE_LIST}" STREQUAL "")

  PROJECT(Emgu.LiteRT.LM.runtime.debian-arm64.nuget)

  get_filename_component(NUGET_OUTPUT_DIR ${CMAKE_CURRENT_SOURCE_DIR} DIRECTORY)
  BUILD_NUGET_PACKAGE(
    ${PROJECT_NAME}
	"${CMAKE_CURRENT_SOURCE_DIR}/../../../Emgu.LiteRT/NetStandard/Emgu.LiteRT.csproj"  #csproj_file
	"${CMAKE_CURRENT_SOURCE_DIR}/Package.nuspec" #nuspec_file
	"${NUGET_OUTPUT_DIR}" #output_dir
	"${CMAKE_CURRENT_SOURCE_DIR}" #working_dir
	)
  IF (EMGU_NUGET_SIGN_FOUND)
    EMGU_SIGN_NUGET(${PROJECT_NAME} "${NUGET_OUTPUT_DIR}/${EMGU_LITERT_LM_DEBIAN_ARM64_NUGET_ID}.${EMGU_LITERT_LM_PACKAGE_VERSION}${TENSORFLOW_VERSION_SUFFIX}.nupkg")
  ENDIF()

  ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT)

  # Depends on Emgu.LiteRT.runtime.debian-arm64.nuget (libLiteRt.so) only for CMake build order - the actual
  # nuget dependency is expressed in Package.nuspec's <dependencies>.
  IF (TARGET Emgu.LiteRT.runtime.debian-arm64.nuget)
    ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.runtime.debian-arm64.nuget)
  ENDIF()

  if(ENABLE_SOLUTION_FOLDERS)
    set_target_properties(${PROJECT_NAME} PROPERTIES FOLDER "NUGET")
  endif()

ENDIF()
