/*
  Version script for the Android/Linux libLiteRt.so built by //tfliteextern:litert_runtime_so: LiteRT's own C API
  plus the TensorFlow Lite C API, which tfliteextern links against. Same as LiteRT's
  //litert/build_common:export_litert_tflite_runtime_linux.lds, which isn't visible outside //litert.
*/
VERS_1.0 {
  global:
    LiteRt*;
    kLiteRtRuntimeBuiltin;
    _TfLite*;
    TfLite*;
  local:
    *;
};
