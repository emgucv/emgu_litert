/* Version script for libomniextern.so: only Emgu's C API (omniextern.h) is exported; the LiteRT-LM runtime it uses is
 * liblitert-lm.so's (see BUILD). */
{
  global:
    omniextern_*;
  local:
    *;
};
