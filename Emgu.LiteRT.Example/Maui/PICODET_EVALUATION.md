# PP-PicoDet-S evaluation (a "PicoDet S" detector in the demo)

Evaluated 2026-10-19/20 on an Apple M1, a Galaxy S10 and a Pixel Fold, as an alternative to SSD MobileNet V3,
[YOLOX-Nano](YOLOX_EVALUATION.md) and [D-FINE-N](DFINE_EVALUATION.md). **Result: converts, matches the original, runs on
the GPU after one graph fix, is about 6 AP more accurate than YOLOX-Nano and faster than it on phone CPUs.** It's offered as
the "PicoDet S" photo page on every platform (GPU first with a CPU fallback, like YOLOX); it has no visual-agent entry.

## Provenance and licence

- Source: https://github.com/PaddlePaddle/PaddleDetection (`configs/picodet`, release/2.8.1). Apache-2.0 code and weights;
  copyright "PaddlePaddle Authors". COCO's annotations are CC BY 4.0.
- Model: PicoDet-S, 416 x 416, LCNet x0.75 backbone, LCPAN neck, PicoHeadV2 (1.18 M parameters, 1.65 GFLOPs, COCO mAP50-95
  **32.5**, the newer LCNet version of the net; the older ESNet one is 30.7). Baidu publishes it without post-processing as an
  ONNX file, which the recipe uses unchanged apart from the rewrite below:
  https://paddledet.bj.bcebos.com/deploy/third_engine/picodet_s_416_coco_lcnet.onnx, 4,781,221 bytes, SHA-256
  `d080f8d9931bf9164934a7863e989605c7b3aa7751e35be406e20aa6b68256a7`.
- Converted model (hosted on Emgu's S3 beside YOLOX's and D-FINE's, with `picodet_license.txt` and `picodet_notice.txt`), reproduced
  bit for bit by `tools/prepare_picodet_s.py`: `picodet_s_416_coco_float32.tflite`, 4,809,280 bytes, SHA-256
  `68870e3d997e53fe6a08297b653f9188543d1d8ba06d22d35b25877b5ae6e64e`.

## Model contract

- Input: float32 NHWC `[1, 416, 416, 3]`: the image resized to 416 x 416 **without** keeping its aspect ratio (cubic
  interpolation, `interp: 2` in PaddleDetection's reader), BGR to RGB, divided by 255, minus the ImageNet mean
  (0.485, 0.456, 0.406) and divided by its standard deviation (0.229, 0.224, 0.225).
- Outputs, eight tensors, for the strides 8, 16, 32 and 64 (grids of 52, 26, 13 and 7 cells a side, so 2704, 676, 169 and 49
  cells): class scores `[1, N, 80]` (already sigmoid; COCO's 80 contiguous classes) and box distributions `[1, N, 32]` (8 bins for each
  of left, top, right and bottom).
- Decode (PicoHeadV2): softmax over each side's 8 bins, expected bin times the stride, then the box is the cell centre
  `((x + 0.5) * stride, (y + 0.5) * stride)` moved by the four distances; scale to the image; class-aware NMS at 0.6 with pixel
  areas (+1), like PaddleDetection's multiclass NMS (its score threshold is 0.025, keep top 100). `PicoDet.Decode` in
  `Emgu.LiteRT.Models` does this on the host.

## Conversion recipe

Same environment as D-FINE's (DFINE_EVALUATION.md: Python 3.12, onnx2tf 2.6.9, TensorFlow 2.21.0, tf_keras 2.21.0,
onnxruntime 1.26.0, ai-edge-litert 2.1.2, onnxsim 0.6.5, opencv-python 4.13.0.92); no Paddle or PaddleDetection install is needed.

```sh
python3.12 -m venv /private/tmp/emgu-picodet-venv
/private/tmp/emgu-picodet-venv/bin/python -m pip install onnx onnxruntime onnxsim 'onnx2tf[tensorflow]==2.6.9' tensorflow==2.21.0 \
  tf-keras==2.21.0 ai-edge-litert==2.1.2 opencv-python numpy sng4onnx onnx_graphsurgeon psutil
/private/tmp/emgu-picodet-venv/bin/python Emgu.LiteRT.Example/Maui/tools/prepare_picodet_s.py --output /private/tmp/emgu-picodet-model
/private/tmp/emgu-picodet-venv/bin/python Emgu.LiteRT.Example/Maui/tools/check_picodet_conversion.py \
  --onnx /private/tmp/emgu-picodet-model/picodet_s_416_coco_lcnet.onnx \
  --tflite /private/tmp/emgu-picodet-model/conversion/litert/picodet_s_416_gpu_float32.tflite
```

`prepare_picodet_s.py` checks the official file's SHA-256, simplifies it with onnxsim, rewrites the two `HardSigmoid` nodes, converts
with `onnx2tf -ois image:1,3,416,416 -tb tf_converter -osd -nuo` and fails if any tensor is dynamic or `RELU_0_TO_1` is left.
It ran from scratch twice and produced the same file both times. `check_picodet_conversion.py` compares the model with
the ONNX file (ONNX Runtime) on the demo's four sample images and exits 1 if the decoded detections differ; with
`--write-reference` it writes the fixture `picodet_reference.json` that `UnitTestPicoDetNative` uses.

**The GPU fix.** The model converted to LiteRT at the first attempt, with static shapes, and matched ONNX - but the GPU
compile failed on Metal and OpenCL alike. LiteRT's log names the cause: `RELU_0_TO_1: Not supported op` (240 of 346 operations
were GPU-capable). PP-LCNet's squeeze-and-excitation blocks use `HardSigmoid`; onnx2tf turns it into a clip to 0..1 and the TFLite converter
fuses that into `RELU_0_TO_1`. Rewriting each `HardSigmoid` in the ONNX graph as Mul, Add, Relu, Sub, Relu, Sub
(min(relu(a*x+b), 1) = 1 - relu(1 - relu(a*x+b))) gives a model with identical results (raw output differences about 3e-4, as before) that
compiles fully accelerated. D-FINE's GPU failure is a different, unexplored problem. The list of ops of the final graph is
ADD, CONCATENATION, CONV_2D, DEPTHWISE_CONV_2D, LOGISTIC, MEAN, MUL, PAD, RESHAPE, RESIZE_NEAREST_NEIGHBOR, SQRT, SUB, TRANSPOSE.

## Results

**Parity** with ONNX Runtime (CPU), the four bundled images, score threshold 0.5: identical detections (same classes, scores
within 0.001, boxes within 2 pixels). Raw output difference at most 0.0003. It finds: surfers - 3 surfboards (0.82-0.90) and 3
people (0.67-0.87); dog/bike - dog 0.94, bicycle 0.88, truck 0.64, car 0.56; space shuttle - airplane 0.71; tulips - nothing
above 0.5 (D-FINE-N found a "potted plant" at 0.66). Not a COCO validation. On the GPU (explicit FP32): the same detections on the
M1's Metal GPU (`UnitTestPicoDetNative(Gpu)`, fully accelerated), and the S10's OpenCL GPU gave the same detections through the
demo's `PicoDetDetector`, with raw output differences of 1e-4 (S10) and 2e-4 (Fold) against its CPU.

**Speed**, inference only (no preprocessing or decoding) with the output read back, median of 15 after 3 warmups, LiteRT
`CompiledModel`, float32, all measured the same way in one run for the two models. The first Fold run was with the screen locked, which
made every number about ten times slower; these are from the unlocked phone.

| Device | Model | CPU 2 / 4 threads | GPU |
|---|---|---|---|
| Mac (M1) | PicoDet-S 416 | 20.8 / 16.5 ms | **3.9 ms** (fully accelerated) |
| | YOLOX-Nano 416 | 18.9 / 16.6 ms | 3.0 ms |
| Galaxy S10 | PicoDet-S 416 | **38.7 / 26.8 ms** | 17.8 ms |
| | YOLOX-Nano 416 | 55.1 / 46.5 ms | 16.7 ms |
| Pixel Fold | PicoDet-S 416 | **23.3 / 20.7 ms** | 16.7 ms |
| | YOLOX-Nano 416 | 33.7 / 25.8 ms | 20.2 ms (GPU+CPU 16.6 ms) |

- On a phone's CPU PicoDet-S is 25-45% faster than YOLOX-Nano, with about 6 points more AP; on the GPUs the two are within a few
  milliseconds of each other. The Mac's CPU shows no difference. The first S10 and Fold runs of the unfixed model were: CPU the
  same as above, GPU compile failure, GPU+CPU (RELU_0_TO_1 on the CPU) 26.5 ms (S10) and 37.1 ms (Fold).
- The whole pipeline in the demo (cubic resize, normalization, decoding) took 34-47 ms per image on the S10's GPU.
  The preprocessing is host-side OpenCV on a 416 x 416 float image; moving the mean/std into the graph or using a cheaper
  interpolation would save some of it (not measured).
- Python's `ai_edge_litert` interpreter (2 threads, M1): PicoDet-S 18.0 ms against ONNX Runtime's 25.2 ms.
- Phones were awake and unlocked, one run each: short samples, not sustained or thermal tests.

## In the demo

`Emgu.LiteRT.Models/PicoDet.cs` (the `CompiledModel`, `Decode`), the demo's `PicoDetDetector` (downloads the model from S3 with its hash;
GPU first with explicit FP32, kept only if its detections match the CPU's on `surfers.jpg` and it is faster, otherwise the CPU)
and `PicoDetImageInput`, the "PicoDet S" page (`CocoSsdMobilenetPage(usePicoDet: true)`), tests `UnitTestPicoDet` (decoding, no
model) and `UnitTestPicoDetNative` (parity with `picodet_reference.json`; set `PICODET_MODEL` to a local file to skip the download) and
the licences entry in `tools/make_licenses.py`. No visual agent; adding one would follow `CocoVisualAgent`'s YOLOX pattern.
