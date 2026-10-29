# Depth Anything V2 Small: evaluation

Measured on 2026-10-28 with this repo's LiteRT (the `litert` submodule at LiteRT-LM v0.18.0's `LITERT_REF`), on an M1 Mac
(macOS, CPU 4 threads and Metal) and a Pixel Fold (Android, Tensor G2, CPU 4 threads and OpenCL), on four or five of the
test images (surfers, pose_standing, tulips, space_shuttle, grace_hopper).

## The model

- Source: [litert-community/depth-anything-v2-small](https://huggingface.co/litert-community/depth-anything-v2-small)
  (Apache 2.0), Google's LiteRT export of `depth-anything/Depth-Anything-V2-Small-hf` at revision `5426e4f0`, with the
  whole DPT decoder in the graph. Both files matched the repository's `SHA256SUMS`.
- `serving_default`: input `args_0` float32 `[1,3,518,686]` (normalized RGB, NCHW), output `output_0` float32 `[1,518,686]`
  (relative depth, larger nearer). Fixed shapes.
- `depth_anything_v2_small.tflite` (float32, 99.5 MB) and `depth_anything_v2_small_wi8_afp32.tflite` (dynamic int8
  weights, float32 activations, 27.7 MB). `DepthAnythingV2.ModelFile` is the int8-weight file.
- Other exports looked at: `litert-community/Depth-Anything-3-Small` (a GPU FP16 file, 55 MB), Arm's Depth Anything 3
  Small (needs ExecuTorch pre/post-processing), Qualcomm AI Hub's (download links only). Not evaluated.

## Results

Per photo: writing the input, running the model and reading the output (on the GPU the wait is in the read). The GPU
graph was fully accelerated in every case; compiling for the GPU took 0.5-1.9 s.

| | M1 CPU | M1 GPU FP32 | M1 GPU FP16 | Fold CPU | Fold GPU FP32 | Fold GPU FP16 |
|---|---|---|---|---|---|---|
| float32 file | 690-870 ms | 181 ms | 119 ms | 1.5-1.6 s | 0.82-0.95 s | 0.45-0.49 s |
| int8-weight file | 390 ms | 181 ms | 119 ms | 1.9-2.7 s | 0.87-1.1 s | 0.49-0.58 s |

GPU against CPU (Pearson correlation of the depth maps): FP32 with the float32 file identical (1.00000, max difference
0); FP16 0.9998-0.99998 (max difference 3-12% of the range, at a few pixels); the int8-weight file 0.9998-0.99997 on
either precision. No visible difference in the depth images.

The depth maps were checked by eye: the surfers and their boards in front of the sea, the yoga pose against the sky,
nearer tulips brighter than farther ones, the Fold's map the same as the Mac's.

## Choices

- The int8-weight file at GPU FP16: as fast as the float32 file on the GPU, a quarter of the download. Its CPU fallback
  is faster than the float32 file's on the M1 but slower on the Fold (dynamic int8 kernels).
- Fast enough for photos (under half a second on the Fold's GPU) and for a camera monitor that checks once or twice a
  second; too slow for a smooth live depth view. A smaller input would need a new conversion (the export's shape is fixed).
