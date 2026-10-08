SET(LITERT_LM_VERSION_FILE "${LITERT_LM_SOURCE_DIR}/version.bzl")
file(STRINGS "${LITERT_LM_VERSION_FILE}" LITERT_LM_VERSION_PARTS REGEX "^VERSION =[ ]+" )

string(REGEX MATCH "\"([0-9]+\\.[0-9]+\\.[0-9]+)\"" _ "${LITERT_LM_VERSION_PARTS}")
set(LITERT_LM_VERSION "${CMAKE_MATCH_1}")
string(REGEX MATCH "^([0-9]+)\\.([0-9]+)\\.([0-9]+)" _ "${LITERT_LM_VERSION}")
SET(LITERT_LM_VERSION_MAJOR "${CMAKE_MATCH_1}")
SET(LITERT_LM_VERSION_MINOR "${CMAKE_MATCH_2}")
SET(LITERT_LM_VERSION_PATCH "${CMAKE_MATCH_3}")

MESSAGE(STATUS "LITERT_LM_VERSION: '${LITERT_LM_VERSION}' ")

# create a dependency on version file
# we never use output of the following command but cmake will rerun automatically if the version file changes
configure_file("${LITERT_LM_VERSION_FILE}" "${CMAKE_BINARY_DIR}/junk/litert_lm_version.junk" COPYONLY)
