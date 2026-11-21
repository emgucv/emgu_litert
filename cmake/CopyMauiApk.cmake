# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------
# Copies the built MAUI demo's Android .apk from SRC_DIR into DEST_DIR (created if
# it doesn't exist yet). Run as a POST_BUILD step rather than a configure-time
# FILE(GLOB) in the caller, since the .apk doesn't exist until the dotnet build of
# the demo actually finishes.
#
# The demo's csproj copies the signed package to Emgu.LiteRT.Maui.Demo.android.<version>.apk;
# when that file is there, only it is copied. Otherwise the <application id>-Signed.apk
# the build writes (the one that installs) is, and without that, every .apk.

FILE(GLOB EMGU_MAUI_APK_FILES "${SRC_DIR}/Emgu.LiteRT.Maui.Demo.android.*.apk")
IF(NOT EMGU_MAUI_APK_FILES)
  FILE(GLOB EMGU_MAUI_APK_FILES "${SRC_DIR}/*-Signed.apk")
ENDIF()
IF(NOT EMGU_MAUI_APK_FILES)
  FILE(GLOB EMGU_MAUI_APK_FILES "${SRC_DIR}/*.apk")
ENDIF()
IF(EMGU_MAUI_APK_FILES)
  FILE(MAKE_DIRECTORY "${DEST_DIR}")
  FILE(COPY ${EMGU_MAUI_APK_FILES} DESTINATION "${DEST_DIR}")
ELSE()
  MESSAGE(WARNING "No .apk found in ${SRC_DIR}")
ENDIF()
