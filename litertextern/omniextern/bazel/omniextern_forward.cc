// libomniextern.dll on Windows (//omniextern:libomniextern) has no code of its own: omniextern_forward.def re-exports the
// omniextern_* functions it imports from liblitert-lm.dll, which holds omniextern and LiteRT-LM's runtime
// (//omniextern:liblitert-lm). A DLL needs at least one object file to link, so this is it.
