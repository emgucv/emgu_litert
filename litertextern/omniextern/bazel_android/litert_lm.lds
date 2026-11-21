/* Version script for liblitert-lm.so: the LiteRT-LM runtime is exported to libomniextern.so (see BUILD), except protobuf.
 * Android's system libraries (the GL drivers' libprotobuf-cpp-lite.so, found through libGLESv3.so/libEGL.so) bind their
 * own protobuf symbols to an exported copy of ours, and crash in its static initializers. libomniextern.so links its
 * own protobuf, hidden the same way. */
{
  global:
    *;
  local:
    _ZN6google8protobuf*;
    _ZTIN6google8protobuf*;
    _ZTSN6google8protobuf*;
    _ZTVN6google8protobuf*;
    _ZTTN6google8protobuf*;
};
