SET(LITERT_VERSION_FILE "${LITERT_SOURCE_DIR}/litert/version.bzl")
file(STRINGS "${LITERT_VERSION_FILE}" LITERT_VERSION_PARTS REGEX "LITERT_EXPERIMENTAL_VERSION =[ ]+" )

string(REGEX MATCH "\"([0-9]+\\.[0-9]+\\.[0-9]+)\"" _ "${LITERT_VERSION_PARTS}")
set(LITERT_VERSION "${CMAKE_MATCH_1}")
string(REGEX MATCH "^([0-9]+)\\.([0-9]+)\\.([0-9]+)" _ "${LITERT_VERSION}")
SET(LITERT_VERSION_MAJOR "${CMAKE_MATCH_1}")
SET(LITERT_VERSION_MINOR "${CMAKE_MATCH_2}")
SET(LITERT_VERSION_PATCH "${CMAKE_MATCH_3}")

MESSAGE(STATUS "LITERT_VERSION: '${LITERT_VERSION}' ")

# create a dependency on version file
# we never use output of the following command but cmake will rerun automatically if the version file changes
configure_file("${LITERT_VERSION_FILE}" "${CMAKE_BINARY_DIR}/junk/litert_version.junk" COPYONLY)
