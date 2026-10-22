# Official YOLOX-Nano in the MAUI demo

Verified 2026-10-19 on Mac Catalyst (Apple M1) and Pixel Fold. YOLOX-Nano is an optional detector (GPU where it
qualifies, else CPU);
the existing SSD MobileNet V3 demo and visual agent remain available. No Ultralytics models, packages or export
tools were used. This experiment does not establish a better default detector.

## Use it

- Home → TF Lite → **YOLOX Nano** runs the COCO detector on a sample or a selected photo.
- Chat can discover the second visual agent, `coco-yolox-nano`. For example:
  **“Use YOLOX to watch for a person and play a sound.”** It uses the existing camera, observations, monitors and
  actions; it does not open a second camera. First use downloads the model (3.69 MB); later use reuses the verified cache.

The chat instructions prefer V3 for ordinary object monitors and select YOLOX when explicitly requested.

`YoloXDetector` tries the **GPU** first (Metal on Mac Catalyst, OpenCL on Android; explicit FP32, GPU+CPU) and keeps it
only if its detections on the bundled `surfers.jpg` match the CPU model's (same count and classes, score within 0.01,
normalized box within 0.005) and it is faster over a short alternating benchmark; otherwise - or if compiling fails -
it falls back to the CPU and `GpuFailure` says why, as `CocoDetector` does for V3. The photo page's backend picker and the
visual agent (`CocoVisualAgent`) both use it. Verified with a temporary check (`YoloXDetector.PrepareAsync(preferGpu:
true)` on `surfers.jpg`, full pipeline including preprocessing and decoding, median of 15 after 3 warmups, 2 CPU
threads): M1 Metal 9.6 ms vs CPU 23.6 ms; Galaxy S10 (OpenCL) 31.4 vs 62.6 ms; Pixel Fold (OpenCL) 36.2 vs 42.6 ms
- all three accepted the GPU, reported "GPU" (fully accelerated) and returned the same six detections as the CPU. A
GPU that fails either check is not used. The earlier V3 qualification is unchanged. This run checked the actual
visual adapter on a padded-stride test frame; live camera monitoring and model-driven YOLOX selection in chat were
not exercised end to end.

## Provenance and license

- Source: [Megvii-BaseDetection/YOLOX](https://github.com/Megvii-BaseDetection/YOLOX), inspected at commit
  `6ddff4824372906469a7fae2dc3206c7aa4bbaee`.
- Weights: the official [YOLOX-Nano ONNX release](https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx),
  linked by upstream's [ONNXRuntime demo](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/demo/ONNXRuntime/README.md).
  3,659,407 bytes; SHA-256 `c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d`.
- Converted float32 model: `yolox_nano_float32.tflite`, 3,694,008 bytes;
  SHA-256 `4be8f0e9e203e8150738db6b3e8486d49b3c3d16c46eba6ed3ccb70bb215fe00`.
- The [Apache-2.0 license](https://emgu-public.s3.amazonaws.com/yolox/yolox_license.txt) and
  [source/conversion attribution](https://emgu-public.s3.amazonaws.com/yolox/yolox_notice.txt) are hosted beside
  the weights in S3. Both public copies were verified byte-for-byte against the original local files.
  The demo does not bundle these text files.

The app downloads the converted checkpoint on demand from
[Emgu public S3](https://emgu-public.s3.amazonaws.com/yolox/yolox_nano_float32.tflite).
The public HTTPS object was downloaded and matched the size/hash above on 2026-10-19. The app excludes the
`.tflite` weights and the license/notice files from its package. The model binary is not committed to Git.
Native regressions download the same checkpoint into `LocalApplicationData/Emgu.LiteRT.Tests/YoloX`, validate
its SHA-256 against the reference fixture, and reuse the verified copy on later runs. First use needs internet access.

The shared `FileDownloadManager` reports progress, supports cancellation/resume and validates SHA-256 before
publishing the downloaded file. The model stays at `FileSystem.CacheDirectory/yolox_nano_float32.tflite`, so
previous versions' verified cached copies are reused without downloading. Clearing the cache requires an internet
connection on next use; after a successful download the detector works offline. Download/checksum failures stop
preparation and allow retry. Verified with the shared downloader: fresh S3 download with progress and hash
validation, cache reuse without a second download, and replacement of a corrupted cached file. The original official ONNX is preserved during conversion: onnx2tf may rewrite its
input, so the conversion script passes it a disposable copy.

## Input and output contract

`YoloXImageInput` follows upstream preprocessing: BGR bytes, aspect-preserving resize with integer truncation,
top-left placement in a 416×416 canvas padded with 114, then float32 in 0–255. It does not swap RGB or divide by 255.
The original ONNX input is NCHW; the LiteRT input is NHWC `[1,416,416,3]`.

The converted output is `[1,85,3549]`. `Emgu.LiteRT.Models.YoloX` restores prediction-major layout, decodes stride
8/16/32 grids and exponential widths/heights, and combines objectness with the highest class probability. Labels
use COCO's contiguous 80-class order, without SSD's background entry. Decode uses the official ONNX demo's
class-agnostic NMS and inclusive pixel areas, threshold 0.45. Observations use scores above 0.25; the photo page
shows scores at least 0.5. Boxes are mapped back to the original image and clipped after NMS; invisible boxes are
discarded. Output is limited to 100 detections. Non-finite/invalid predictions are filtered.

V3 and YOLOX scores are model-specific and should not be compared as calibrated probabilities.

## What passed

- Python: converted LiteRT output versus the pristine upstream ONNX on `surfers.jpg`, `dog416.png`,
  `space_shuttle.jpg`, `tulips.jpg`. Every raw output passed `rtol=1e-3, atol=5e-4`; selected reference boxes/scores
  passed `rtol=1e-3, atol=0.01`. Maximum raw absolute differences were approximately 0.000316, 0.000049,
  0.000084 and 0.000245 respectively. Reference decoding uses upstream's actual preprocessing/grid/NMS functions.
- Native Mac Catalyst and Android: all four images matched the reference detection counts, classes, scores within
  0.01 and normalized box coordinates within 0.005. The visual adapter handled a padded-stride frame and returned
  three people over 0.5. The real photo page produced six rows/annotated boxes for the surfers image.
- Retained regressions: decoder grids, letterbox restoration, objectness, cross-class NMS, invalid predictions,
  limits and host label mapping; native LiteRT parity calls the production preprocessing. Together with existing
  visual-agent/backend timing tests: **47 passed, one opt-in Gemma test skipped**.
- Mac Catalyst and Android builds passed. Temporary startup probes and their reference asset were removed before
  the final normal builds. The reference fixture remains in the test project; diagnostics retain no personal photos.

On the surfers example, YOLOX recognized three people at about 0.869/0.844/0.830, plus surfboards. This is a useful
qualitative result, not a COCO validation accuracy measurement. There were also low-confidence spurious labels in
the tulips image, illustrating why four handpicked images cannot establish overall quality.

## Short same-device CPU comparison

Two CPU threads for both models, three warmups, twelve timed calls per model, alternating execution order.
Medians include preprocessing, buffer transfer, inference, decoding and NMS; exclude compilation and image decode.
The Mac table uses a repeat without a concurrent build. The phone was awake and unlocked. These are short samples,
not sustained camera/thermal tests or measurements alongside a loaded Gemma engine.

| Image | M1 YOLOX | M1 V3 | Pixel Fold YOLOX | Pixel Fold V3 |
|---|---:|---:|---:|---:|
| Surfers | 23.57 ms | 4.73 ms | 43.88 ms | 12.70 ms |
| Dog/bike | 23.12 ms | 4.76 ms | 49.54 ms | 16.79 ms |
| Space shuttle | 23.23 ms | 4.60 ms | 46.60 ms | 11.81 ms |
| Tulips | 23.14 ms | 4.66 ms | 49.89 ms | 16.21 ms |

This float32 YOLOX export is slower than the quantized V3 on both tested CPUs. Keep it optional. GPU qualification,
quantization with representative calibration data, full COCO validation, memory alongside Gemma, sustained frame
rate, battery and thermals are follow-ups before considering a default change.

## Reproduce conversion and independent parity

Use Python 3.12 in an isolated environment. The tested conversion stack was onnx2tf 2.6.9, TensorFlow/tf-keras 2.21.0,
Keras 3.15.0, ONNX 1.20.1, ONNXRuntime 1.26.0, ai-edge-litert 2.1.2, NumPy 2.2.6 and OpenCV 4.13.0.92.
The pinned converter supplies its core dependencies; pin the optional TensorFlow/runtime packages too:

```sh
python3.12 -m venv /private/tmp/emgu-yolox-venv
/private/tmp/emgu-yolox-venv/bin/python -m pip install \
  'onnx2tf[tensorflow]==2.6.9' tensorflow==2.21.0 tf-keras==2.21.0 \
  onnxruntime==1.26.0 ai-edge-litert==2.1.2 opencv-python==4.13.0.92
git clone https://github.com/Megvii-BaseDetection/YOLOX.git /private/tmp/emgu-yolox-upstream
git -C /private/tmp/emgu-yolox-upstream checkout 6ddff4824372906469a7fae2dc3206c7aa4bbaee
/private/tmp/emgu-yolox-venv/bin/python Emgu.LiteRT.Models/tools/prepare_yolox_nano.py \
  --output /private/tmp/emgu-yolox-model
/private/tmp/emgu-yolox-venv/bin/python Emgu.LiteRT.Models/tools/check_yolox_conversion.py \
  --models /private/tmp/emgu-yolox-model --upstream /private/tmp/emgu-yolox-upstream
```

Both scripts were run successfully. The first verifies source and converted SHA-256; the second independently
compares the raw outputs and upstream-decoded detections and prints JSON. Its Python inference-only timings are not
the MAUI full-frame timings in the table. The app needs neither Python nor these conversion packages at runtime.

## GPU experiment (2026-10-19)

`YoloX` now accepts accelerator flags and an optional GPU precision, and exposes LiteRT's
`IsFullyAccelerated` result. Existing callers still default to CPU. No automatic backend selection was added.

The development probe tests GPU-only and GPU+CPU with default precision and explicit FP32. Each accepted
image must match the independent upstream ONNX fixture: identical detection count/classes, score error <0.01,
normalized box error <0.005. Timings include the production preprocessing, host/device transfers, synchronous
inference, output readback/transposition, decoding and NMS. Five warmups, thirty alternating timed calls per
backend/image, CPU threads=2; reported values are medians. Compilation and image decode are excluded.
No camera, personal images or loaded Gemma engine are involved.

On M1 Metal, default precision failed the strict shuttle box/score check, although surfers/dog passed.
Explicit FP32 passed all four images, with maximum score error about 0.000002 and box error below 0.000001.
LiteRT reported fully accelerated for GPU-only and GPU+CPU. GPU-only FP32 compilation was about 761 ms
in the recorded run; CPU compilation about 5 ms. Compilation timing is sensitive to driver caches.

| Image | M1 CPU | M1 GPU FP32 | Speedup |
|---|---:|---:|---:|
| Surfers | 23.35 ms | 9.67 ms | 2.41× |
| Dog/bike | 24.34 ms | 9.81 ms | 2.48× |
| Space shuttle | 24.30 ms | 9.90 ms | 2.45× |
| Tulips | 24.70 ms | 9.89 ms | 2.50× |

GPU+CPU FP32 was also fully accelerated and had similar times (9.79–9.95 ms).

On Pixel Fold OpenCL, default precision failed the strict surfers box/score check. Explicit FP32 passed all four
images: maximum score error <0.000003, normalized box error <0.000001; GPU-only was fully accelerated.
CPU compilation was 46.59 ms, GPU-only FP32 504.22 ms (after earlier default-precision compilations).

| Image | Pixel Fold CPU | Pixel Fold GPU FP32 | Speedup |
|---|---:|---:|---:|
| Surfers | 40.66 ms | 32.19 ms | 1.26× |
| Dog/bike | 54.97 ms | 35.47 ms | 1.55× |
| Space shuttle | 58.22 ms | 36.08 ms | 1.61× |
| Tulips | 58.72 ms | 35.21 ms | 1.67× |

The phone was unplugged, battery 80%, battery temperature initially 28.6°C (28.3°C after testing; battery 79%). Variation between images/runs
shows the limits of a short mobile benchmark. These are YOLOX-versus-YOLOX numbers; the earlier quantized
V3 CPU run was faster than these YOLOX GPU times, but was not repeated in this experiment.
GPU+CPU FP32 also passed all four images, was fully accelerated, and measured 30.92–34.88 ms on the GPU
versus 54.97–58.09 ms on CPU in its later pass.

The explicit native GPU regression passed on the Mac:

```sh
dotnet test Emgu.LiteRT.Test/Emgu.TF.Lite.Test/Emgu.TF.Lite.Test.Net/Emgu.TF.Lite.Test.Net.csproj \
  --filter 'Name~Gpu&FullyQualifiedName~UnitTestYoloXNative'
```

### Reproduce the MAUI benchmark

The retained opt-in probe is `tools/YoloXGpuProbe.cs.txt`; it is excluded from ordinary compilation.
For a diagnostic build only, copy it to `YoloXGpuProbe.cs` in the demo root (`Emgu.LiteRT.Example/Maui/`) and copy
`Emgu.LiteRT.Test/Emgu.TF.Lite.Test/TestData/yolox_reference.json` to
`Resources/Raw/yolox_gpu_reference.json`. Build the app normally.

- Mac Catalyst: launch the app executable with `EMGU_YOLOX_GPU_TEST=1`. Results go to
  `/private/tmp/yolox_gpu_probe.log`.
- Android: install with `adb -s <Pixel-serial> install --no-incremental -r <apk>`, force-stop the app,
  create `files/yolox_gpu_probe_command` with `adb ... shell run-as com.emgu.litert touch ...`, then
  launch the activity. Read `files/yolox_gpu_probe.log` through `run-as`. Wake/unlock the phone first.

The command is consumed once; the probe uses only bundled images and releases its models after completion.
Remove both temporary source/asset files, delete the private command/log and any stale copy of the reference
asset in the Mac app bundle. Clean the demo's Android build intermediates before rebuilding:
incremental packaging can retain removed assets. Verify the resulting APK excludes the deleted assets.
Rebuild and reinstall the normal app
after testing. (The standard adapter now qualifies the GPU itself - see above.) These short measurements do not
establish sustained performance, battery use or latency alongside Gemma.
