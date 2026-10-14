PROJECT(Emgu.LiteRT.GPU.runtime.windows.nuget)
get_filename_component(NUGET_OUTPUT_DIR ${CMAKE_CURRENT_SOURCE_DIR} DIRECTORY)

ADD_CUSTOM_TARGET(Emgu.LiteRT.GPU.runtime.windows.validate
  COMMAND powershell -NoProfile -ExecutionPolicy Bypass -File
    "${CMAKE_CURRENT_SOURCE_DIR}/../../windows/validate_windows_gpu.ps1"
    -BundleDirectory "${EMGU_WINDOWS_GPU_BUNDLE_DIR}"
  VERBATIM)
BUILD_NUGET_PACKAGE(${PROJECT_NAME}
  "${CMAKE_CURRENT_SOURCE_DIR}/../../../Emgu.LiteRT/NetStandard/Emgu.LiteRT.csproj"
  "${CMAKE_CURRENT_SOURCE_DIR}/Package.nuspec"
  "${NUGET_OUTPUT_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}")
ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.LiteRT.GPU.runtime.windows.validate)
IF(TARGET Emgu.runtime.windows.msvc.rt.x64.nuget)
  ADD_DEPENDENCIES(${PROJECT_NAME} Emgu.runtime.windows.msvc.rt.x64.nuget)
ENDIF()
IF(EMGU_NUGET_SIGN_FOUND)
  EMGU_SIGN_NUGET(${PROJECT_NAME} "${NUGET_OUTPUT_DIR}/${EMGU_LITERT_GPU_WINDOWS_NUGET_ID}.${CPACK_PACKAGE_VERSION}${TENSORFLOW_VERSION_SUFFIX}.nupkg")
ENDIF()
IF(ENABLE_SOLUTION_FOLDERS)
  SET_TARGET_PROPERTIES(${PROJECT_NAME} PROPERTIES FOLDER "NUGET")
ENDIF()
