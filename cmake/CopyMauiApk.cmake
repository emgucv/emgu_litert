# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------
# Copies the built MAUI demo's Android .apk from SRC_DIR into DEST_DIR (created if
# it doesn't exist yet). Run as a POST_BUILD step rather than a configure-time
# FILE(GLOB) in the caller, since the .apk doesn't exist until the dotnet build of
# the demo actually finishes.
#
# The build writes <application id>-Signed.apk (the one that installs) next to an
# unsigned <application id>.apk; only the signed one is copied. Without it, every
# .apk is.

FILE(GLOB EMGU_MAUI_APK_FILES "${SRC_DIR}/*-Signed.apk")
IF(NOT EMGU_MAUI_APK_FILES)
  FILE(GLOB EMGU_MAUI_APK_FILES "${SRC_DIR}/*.apk")
ENDIF()
IF(EMGU_MAUI_APK_FILES)
  FILE(MAKE_DIRECTORY "${DEST_DIR}")
  FILE(COPY ${EMGU_MAUI_APK_FILES} DESTINATION "${DEST_DIR}")
ELSE()
  MESSAGE(WARNING "No .apk found in ${SRC_DIR}")
ENDIF()
