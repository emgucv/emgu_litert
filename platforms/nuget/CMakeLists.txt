# --------------------------------------------------------
#  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
# --------------------------------------------------------

IF (HAVE_WINDESKTOP_X64_LITE)
  ADD_SUBDIRECTORY(Emgu.runtime.windows.msvc.rt.x64)
ENDIF()

IF (HAVE_WINDESKTOP_X86_LITE)
  ADD_SUBDIRECTORY(Emgu.runtime.windows.msvc.rt.x86)
ENDIF()

IF (HAVE_WINDESKTOP_LITE)
  add_subdirectory(Emgu.LiteRT.runtime.windows)
  add_subdirectory(Emgu.LiteRT.Tflite.runtime.windows)
ENDIF()


IF(HAVE_OSX_LITE)
  add_subdirectory(Emgu.LiteRT.runtime.macos)
ENDIF()

#IF ( HAVE_ANDROID_LITE )
#  add_subdirectory(Emgu.LiteRT.runtime.xamarin.android)
#ENDIF()

#IF ( HAVE_IOS_LITE )
#  add_subdirectory(Emgu.LiteRT.runtime.xamarin.ios)
#ENDIF()

add_subdirectory(Emgu.LiteRT.runtime.ubuntu)
add_subdirectory(Emgu.LiteRT.LM.runtime.ubuntu)

add_subdirectory(Emgu.LiteRT.runtime.debian-arm)
add_subdirectory(Emgu.LiteRT.runtime.debian-arm64)

#add_subdirectory(Emgu.LiteRT.runtime.maui)

#add_subdirectory(Emgu.TF.runtime.maui)