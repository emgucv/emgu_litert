# D-FINE-N evaluation (a Mac Catalyst photo detector in the demo)

Evaluated 2026-10-19 on Mac Catalyst's host (Apple M1), a Galaxy S10 and a Pixel Fold, as an alternative to SSD MobileNet V3
and [YOLOX-Nano](YOLOX_EVALUATION.md). **Decision: not a phone or visual-agent detector; offered on Mac Catalyst only, for single photos.** It converts to LiteRT and matches the original, but
it is 1.4-2.7x slower than YOLOX-Nano on CPUs and has no phone-GPU path, where YOLOX runs in 16-18 ms. This note keeps the
recipe and the results. The model is not bundled: `DFineDetector` downloads the 640 file from Emgu's S3
(`emgu-public/dfine/`, hash checked) for the "D-FINE Nano" photo page, which exists on Mac Catalyst only, runs on the CPU, and has no
visual-agent entry. The classes are `Emgu.LiteRT.Models/DFine.cs`, `DFineDetector.cs` and `DFineImageInput.cs` in the demo;
tests are `UnitTestDFine` (decoding) and `UnitTestDFineNative` (parity with `dfine_reference.json`, made from the ONNX model).

## Why D-FINE-N

[D-FINE](https://github.com/Peterande/D-FINE) is a real-time DETR detector with no NMS step: the network returns 300 final
queries. COCO mAP50-95 of D-FINE-N is 42.8 (640 px, 4 M parameters, ~7 GFLOPs) against YOLOX-Nano's 25.8 (416 px) and SSD
MobileNet V3 small's ~15. Its code and COCO weights are Apache-2.0, so it fits Emgu's dual licence. The Ultralytics models
(YOLOv5, v8, v10, v11, YOLO26) are AGPL-3.0 and YOLOv9 is GPL-3.0, which don't fit Emgu's commercial licence.

## Provenance and licence

- Source: https://github.com/Peterande/D-FINE at commit `956d1709314c2c6a4df6f34de232054578a7449f`. Apache-2.0; copyright
  "The D-FINE Authors"; the decoder and encoder are "modified from RT-DETR" (lyuwenyu, Apache-2.0).
- Weights: `dfine_n_coco.pth` from the D-FINE release `dfinev1.0`
  (https://github.com/Peterande/storage/releases/download/dfinev1.0/dfine_n_coco.pth), 15,489,558 bytes, SHA-256
  `41973938d2784d38a9836990d805b8392855ebf611aba55f0f7add90e110744c`.
- D-FINE's README (August 2026) notes that checkpoints trained or pretrained on **Objects365** (`*_obj365`, `*_obj2coco`)
  "may be subject to the Objects365 dataset terms" and shouldn't be assumed commercially cleared. `dfine_n_coco` is trained
  on COCO alone and isn't affected - don't swap in an `obj2coco` checkpoint without checking.
- COCO's annotations are CC BY 4.0, which the licences page would credit.
- Converted models (what to host, like YOLOX's on Emgu's S3), reproduced bit for bit by `tools/prepare_dfine_n.py`:

| File | Bytes | SHA-256 |
|---|---:|---|
| `dfine_n_coco_640_float32.tflite` | 16,061,132 | `1f7c2fa937e414d4d728b007c4295addb106fd56c113ea188155193d2a6e80bc` |
| `dfine_n_coco_416_float32.tflite` | 15,924,240 | `e27d6fdd30175554adfa9298f1b51ff9d50665d6575af531d250d3802e5e8c1e` |

  Host them with a `dfine_license.txt` (the Apache-2.0 text) and a `dfine_notice.txt` (source, copyright, the official
  weights' hash, what was modified, the hashes above), as for YOLOX.

## Model contract

- Input: float32 NHWC `[1, S, S, 3]`, S = 640 (trained size) or another size the model was exported at (416 and 480 were
  converted too: the anchors are rebuilt for the size). The image is resized to S x S **without** keeping its aspect
  ratio, BGR to RGB, divided by 255. No mean/std.
- Outputs: `pred_logits` `[1, 300, 80]` (before the sigmoid; COCO's 80 contiguous classes, no background) and `pred_boxes`
  `[1, 300, 4]` (centre x, centre y, width, height, normalized to the input - multiply by the original image's size).
- Decode: sigmoid, take the best scores over the 300 queries x 80 classes, keep those at or above the threshold (0.5 in
  the checks), convert to corners. No NMS; a query may still duplicate another's object at a different class (a "car" box
  over a "truck" at 416 px).

## Conversion recipe

Needs Python 3.12 in an isolated environment and the pinned D-FINE checkout. The tested stack: torch 2.14.1, torchvision
0.29.1, onnx 1.20.1, onnxsim 0.6.5, onnx2tf 2.6.9, TensorFlow 2.21.0, tf_keras 2.21.0, keras 3.15.0, onnxruntime 1.26.0,
ai-edge-litert 2.1.2, NumPy 2.2.6, opencv-python 4.13.0.92, PyYAML 6.0.3, scipy 1.18.1, loguru 0.7.3, tensorboard 2.21.0,
faster-coco-eval 1.8.0, calflops 0.3.2, transformers 5.20.1. (D-FINE's `src.core` imports its training dependencies even
for export, hence the last five.)

```sh
python3.12 -m venv /private/tmp/emgu-dfine-venv
/private/tmp/emgu-dfine-venv/bin/python -m pip install torch==2.14.1 torchvision==0.29.1 PyYAML scipy loguru onnx==1.20.1 \
  onnxsim 'onnx2tf[tensorflow]==2.6.9' tensorflow==2.21.0 tf-keras==2.21.0 onnxruntime==1.26.0 ai-edge-litert==2.1.2 \
  opencv-python==4.13.0.92 tensorboard faster-coco-eval calflops transformers
git clone https://github.com/Peterande/D-FINE.git /private/tmp/emgu-dfine-upstream
git -C /private/tmp/emgu-dfine-upstream checkout 956d1709314c2c6a4df6f34de232054578a7449f
/private/tmp/emgu-dfine-venv/bin/python Emgu.LiteRT.Models/tools/prepare_dfine_n.py \
  --upstream /private/tmp/emgu-dfine-upstream --output /private/tmp/emgu-dfine-model --size 640
/private/tmp/emgu-dfine-venv/bin/python Emgu.LiteRT.Models/tools/check_dfine_conversion.py \
  --onnx /private/tmp/emgu-dfine-model/dfine_n_coco_640.onnx \
  --tflite /private/tmp/emgu-dfine-model/conversion_640/litert/dfine_n_640_float32.tflite --size 640
```

`prepare_dfine_n.py` verifies the upstream commit and the weights' SHA-256, exports the raw network to ONNX at the chosen
size, simplifies it with onnxsim, converts with `onnx2tf -ois images:1,3,S,S -tb tf_converter -rtpo Erf GeLU -osd -nuo`,
and fails unless the result has no dynamic tensors. It was run end to end for 640 and 416 on 2026-10-19 and produced the
files above bit for bit. `check_dfine_conversion.py` compares the model with the ONNX export on the demo's four sample
images and exits 1 if the decoded detections differ.

What failed on the way (and why the flags are what they are):

- **onnx2tf's default direct converter** (the path YOLOX used) gives a model that runs on the CPU with the right outputs,
  but with **137 tensors of dynamic shape** (`[-1,-1,-1,128]` through the encoder), even given fixed input shapes, and
  LiteRT's GPU compiler refuses them ("Attempting to use a delegate that only supports static-sized tensors").
- **The TensorFlow converter alone** gives static shapes but a `FlexErf` op (GELU), which LiteRT's interpreter doesn't
  have: `allocate_tensors` fails. `-rtpo Erf GeLU` replaces it by plain operations.
- **float16** (written by onnx2tf too) can't run on LiteRT's CPU: its convolutions take float16 inputs ("conv.cc ...
  input_type"). Use float32, as with YOLOX.
- The checkpoint's anchors and valid mask are built for 640. For another size the export sets `eval_spatial_size` and
  leaves those two buffers out of the state dict, so they're rebuilt.
- The TensorFlow-converter model at 480 px had a raw-output difference of 2.0 on one query of `space_shuttle.jpg` while every
  decoded detection was identical (not investigated); 416 and 640 match to ~0.001 in the logits and 0.0001 in the boxes.

## Results

**Parity** with ONNX Runtime (CPU), the four bundled images, score threshold 0.5: identical decoded detections at 640
and 416 (same labels, scores within 0.001 and boxes within 0.001 normalized), maximum logit difference 0.0003-0.004 and box
difference 0.0001-0.0004. At 640 it finds: surfers - 3 surfboards (0.92-0.95) and 3 people (0.83-0.91); dog/bike - dog 0.95,
bicycle 0.91, truck 0.80, tv 0.61, potted plant 0.56, a second bicycle 0.51; space shuttle - airplane 0.85; tulips -
"potted plant" 0.66. At 416 it loses the tv and plant in `dog416.png` and the tulips detection, adds a duplicate "car"
over the truck, and gives lower scores (surfers 0.53-0.85): it's visibly less accurate at 416. Not a COCO validation.

**Speed**, inference only (no preprocessing or decoding), median of 15 after 3 warmups, LiteRT `CompiledModel`/interpreter,
float32. YOLOX-Nano is the same measurement of `yolox_nano_float32.tflite` (the app's full pipeline adds preprocessing and
decoding: see YOLOX_EVALUATION.md).

| Device | Model | CPU 2 / 4 threads | GPU |
|---|---|---|---|
| Mac (M1) | YOLOX-Nano 416 | 13 / 9 ms | 9.6 ms in the app's pipeline (Metal) |
| | D-FINE-N 416 | 36 / 25 ms | compile fails |
| | D-FINE-N 480 | 43 / 30 ms | |
| | D-FINE-N 640 | 68 / 44 ms | compile fails; GPU+CPU 28 ms (4 CPU threads), not fully accelerated |
| Pixel Fold | YOLOX-Nano 416 | 33 / 23 ms | **16 ms**, fully accelerated |
| | D-FINE-N 416 | 46 / 41 ms | compile fails; GPU+CPU 73 ms |
| | D-FINE-N 640 | 95 / 85 ms | compile fails; GPU+CPU 125 ms |
| Galaxy S10 | YOLOX-Nano 416 | 61 / 57 ms (noisy, 51-78) | **18 ms**, fully accelerated |
| | D-FINE-N 416 | 77 / 53 ms | compile fails; GPU+CPU 86 ms |
| | D-FINE-N 640 | 161 / 115 ms | compile fails; GPU+CPU 121 ms |

- The GPU-only compile fails on Metal and OpenCL alike (`kLiteRtStatusErrorCompilation`); with CPU fallback the graph is
  split and ends up slower than the CPU alone on the phones. On the M1 GPU+CPU at 640 ran in 28 ms, but isn't fully
  accelerated and its output accuracy wasn't checked.
- Measured through `Emgu.LiteRT` (CompiledModel) and, for the Mac, also the Python interpreter, which agree within a few ms.
  The phones were awake and unlocked, unplugged or on USB, one run each: short samples, not sustained or thermal tests.
- On the same CPU the LiteRT model is about as fast as ONNX Runtime (68.3 vs 64.6 ms at 640, 2 threads).

## What integrating it took (and what is left)

The YOLOX pattern, Mac-only until the GPU graph gets fixed. Done:

- `Emgu.LiteRT.Models/DFine.cs` (an `IDisposable` over `CompiledModel`, like `YoloX.cs`) with the decode above and tests
  against the ONNX output (`dfine_reference.json`, like `yolox_reference.json`);
- `DFineDetector : ICocoDetector` downloading the model from Emgu's S3 with its hash, and the Mac Catalyst photo page (no visual
  agent: it's too slow for monitoring);
- the licences page entry (Apache-2.0 plus the COCO attribution) in `Emgu.LiteRT.Example/Maui/tools/make_licenses.py`;
- the input is resized to S x S without letterboxing, so box coordinates scale by the original width and height.

The open question for phones is the GPU: find which operations the Metal/OpenCL compilers reject (the graph has 1,200
operations: `GridSample`, the transformer's attention and layer norms are the likely ones) and whether the model can be
rewritten without them. Unexplored.
