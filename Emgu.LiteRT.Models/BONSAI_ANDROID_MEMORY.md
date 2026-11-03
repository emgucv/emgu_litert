# Bonsai Image 4B on Android: memory (Pixel Fold)

Why "Bonsai Image 4B · GPU" kills the MAUI demo on a Pixel Fold, what the CPU choice does instead, and what could be
changed. Measured on 2026-11-02 with the demo at dd6b7c49. Nothing has been changed in the code yet.

## The device

- Pixel Fold (Tensor G2, Mali-G710, Android 17): 11.7 GB of RAM seen by Android, and 5.9 GB of zRAM swap (compressed in RAM;
  its size is fixed by the firmware and can't be changed without root).
- The GPU has no memory of its own: it shares the RAM. Its buffers (dma-buf) are pinned, so they can't be swapped out or
  dropped.
- With the demo stopped, `MemAvailable` was 5.7-6.1 GB. Before the tests it had been 0.2-1.8 GB, with other apps and the
  demo's photo indexing running. `dumpsys meminfo` at 6.1 GB available broke the RAM down like this:

  | Where the RAM is | GB |
  |---|---|
  | Kernel | 2.0 |
  | Running processes | 4.4 |
  | Cached processes | 1.3 |
  | Kernel file cache | 3.2 |
  | Free | 2.2 |

## Bonsai on the GPU beside Gemma 4 E2B on the GPU: killed

- The demo's settings were Gemma 4 E2B · GPU, Bonsai Image 4B · GPU (`bonsai_gpu`) and Think on. The prompt was "Draw a
  red fox sitting in the snow".
- Gemma called `generate_image`, and the tile showed "Preparing the image model for the first time". About 40 s into
  Bonsai's load, Android's low-memory killer ended the demo while it was in front (`exit-info`: `LOW_MEMORY`, importance
  100). At the kill, lmkd recorded the demo with 2.7 GB in RAM, 2.0 GB in swap and **4.8 GB of dma-buf**. In the same
  second, lmkd also killed about twenty other apps.
- To the user this looks like a crash. Nothing is reported, so `OmniImageModel`'s GPU fallback (load error → CPU) never
  runs: Android's OpenCL doesn't fail an allocation, it gets the process killed.
- The same kill happened on 2026-11-01 at 14:39 (an OOM kill at 7.2 GB RSS). That first kill left
  `bonsai-flux2-cache/flux2_dit_<...>_mldrift_program_cache.bin` at **0 bytes**, and the DiT's GPU load has never
  finished on the Fold since. The text encoder's program cache (17 MB) and both weight caches were written in full:
  1.75 GB for the text encoder, 2.0 GB for the DiT.

## Bonsai on the CPU beside Gemma 4 E2B on the GPU: works, slowly

- Same prompt, same chat model, with `litert_lm_chat_image_model` set to `bonsai` (the CPU).
- The model loaded in about 35 s and drew the picture in **380 s**. The Galaxy S10 took 319 s. Gemma's reply followed,
  465 s for the whole turn. The fox was good, and nothing was killed.
- The draw is compute-bound: about 4 cores at 100%, with no I/O wait.
- Memory, sampled every 5 s from `/proc/<pid>/status`:

  | | Peak | Range during the draw |
  |---|---|---|
  | File-backed (`RssFile`) | 5.2 GB | 3.1-5.2 GB |
  | The app's own memory in RAM (`RssAnon`) | | 0.2-2.0 GB |
  | Swapped out (`VmSwap`) | 2.3 GB | |
  | Mali dma-buf (`/sys/class/misc/mali0/device/dma_buf_gpu_mem`) | | 0.7 GB, steady |

  Android swapped out the app's anonymous memory (mostly Gemma's CPU-side data) and kept the model's memory-mapped file
  pages, which it can drop and read again. That is why the CPU run survives where the GPU run is killed. It isn't using
  less memory.
- It ran close to the limit: system swap reached 5.1 of 5.7 GB, with 0.5 GB of RAM free.

## Why Bonsai is "small" elsewhere but not here

- PrismML's Bonsai Image 4B has ternary DiT weights. PrismML's own runtime has kernels that keep them packed, which is
  where the small memory figures come from.
- LiteRT has no ternary kernels. litert-community's `.litertlm` (4.3 GB) stores the DiT as int4 (`dit_gpu_int4b32`),
  plus the Qwen3 text encoder and the VAE decoder.
- On the Fold, the GPU weight caches (2.0 + 1.75 GB) are about the size of int4 weights, so the weights are already
  compact on the GPU.
- The M1's figures (2.1 GB on Metal, 4.8 GB on the CPU) are in `BonsaiImage4B`'s comment. How the Metal figure was
  measured may explain the difference; that hasn't been checked.

## Options (not done)

1. **Text encoder on the CPU, DiT and VAE on the GPU.**
   - LiteRT-LM already has per-stage backends: `Flux2ModelConfig::textenc_backend`, `dit_backend` and `vae_backend`
     (`omni/text2image/flux2/flux2_model_config.h:66`), read in `flux2_factory.cc:345`. Nothing sets them:
     `bonsai_flux2.cc` builds the config from the model's metadata only, and `text2image_runner`'s `--textenc_backend`,
     `--dit_backend` and `--vae_backend` flags are defined but never read.
   - This needs a patch to `bonsai_flux2.cc` and an argument on `omniextern_image_engine_create`. Windows already builds
     from patched LiteRT-LM sources (`platforms/windows/litert_lm_patches/`), and the Android build would need the same.
   - Expected: about 2-2.5 GB of GPU memory instead of about 4 GB. The text encoder runs once per picture.
2. **Unload the chat model from the GPU while drawing.** On a phone, when free memory is short:
   - the `generate_image` tool returns at once ("the picture follows this reply"), and Gemma finishes its reply;
   - the page unloads Gemma, loads Bonsai on the GPU, draws and adds the picture to the reply;
   - the page unloads Bonsai and loads Gemma again. `ChatPage.ReloadModelAsync` with `RereadNext` rereads the chat with
     the next message.

   This can't happen inside the tool call: `ReloadModelAsync` waits for the reply being written (`PendingSend`). The
   free-memory check has to allow for `MemAvailable` leaving out memory Android can reclaim by killing cached apps.
   Option 2 only helps if Bonsai fits on the GPU by itself, which is untested on the Fold. Option 1 makes that much
   more likely.
3. **Fall back to the CPU on phones.** If Bonsai doesn't fit on the GPU even alone, use the CPU automatically on phones
   with 12 GB of RAM or less (about 6 minutes a picture on the Fold).
4. **Delete a 0-byte `*_program_cache.bin`** before loading, so an interrupted first load isn't kept as a broken cache.

What won't help:
- FP16 on the GPU: noise, and only 0.25 GB saved (tested on Windows, see `MODELS.md`). LiteRT-LM forces FP32 for FLUX.2
  on the GPU.
- More swap: GPU buffers can't be swapped, zRAM lives in RAM, and its size is fixed.

## Next test, when this is picked up

On the Fold, with no chat model loaded, load Bonsai on the GPU (whole model, then with option 1's patch) and draw one
picture. Record the load time, the draw time, and the peak dma-buf and RSS. The demo's settings, the
`/proc/<pid>/status` sampling and lmkd's kill lines (`logcat`, tag `lowmemorykiller`) are enough to measure it.

## State left on the Fold

- The demo is at dd6b7c49, with the image model set to Bonsai Image 4B · CPU (`bonsai`). It was `bonsai_gpu`.
- `bonsai-flux2-cache/` still has the 0-byte DiT program cache.
- The 512 model (4.3 GB) and its cache (3.6 GB) are still there; phones don't offer that model.
- 1.3 GB of storage is free.
