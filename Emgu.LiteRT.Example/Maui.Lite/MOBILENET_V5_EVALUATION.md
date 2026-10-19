# MobileNet V5 replacement evaluation

Checked on 2026-10-19. The published V5 encoder runs, but it cannot replace the demo's SSD MobileNet V3
detector with the available weights. A trained object-detection head is missing. No detector switch was made.

## Published model and actual run

The [timm V5 documentation](https://huggingface.co/docs/timm/main/models/mobilenetv5) lists one pretrained
checkpoint, `mobilenetv5_300m.gemma3n`, without a classifier. Its
[implementation](https://github.com/huggingface/pytorch-image-models/blob/main/timm/models/mobilenetv5.py)
defines an encoder and an untrained classification variant, rather than a COCO SSD detector.
[Google describes V5 as Gemma 3n's vision encoder](https://developers.googleblog.com/en/introducing-gemma-3n-developer-guide/).
I did not find a published, trained COCO V5 detector in those sources; this is not proof that no other project has one.

Ran the real pretrained safetensors checkpoint on the bundled `Resources/Raw/surfers.jpg`:

| Item | Observed result |
|---|---|
| Machine | Apple M1, macOS 27.0.1 arm64 |
| Runtime | PyTorch 2.14.1, torchvision 0.29.1, timm 1.0.30; CPU FP32, four threads |
| Repository | `timm/mobilenetv5_300m.gemma3n` |
| Pinned revision | `f3a689df9847019f95abea17423a680b3d4512ac` |
| Checkpoint | 1,176,564,736 bytes, 294,126,912 parameters |
| SHA-256 | `affbbd57927c4138b783564d98603ef54530eb02ddf25cbabf79a4b2ce0fb321` |
| Input | Float RGB `[1, 3, 256, 256]`, timm evaluation transform, values scaled to 0–1 |
| Spatial features | `[1, 2048, 16, 16]`, finite values |
| Default pooled output | `[1, 2048]`, finite values; `num_classes = 0` |
| Warmed inference | 761.40 / 777.63 / 766.10 ms; median **766.10 ms** |

The hash-verified run reused the downloaded weights with networking disabled. Timing excludes downloading,
hashing, model loading and image preprocessing. One spatial-feature warmup preceded three timed model forwards.
An earlier run had a 780.54 ms median. These short CPU samples establish successful encoder execution; they do not
measure detection accuracy, sustained camera FPS, Android, Metal/OpenCL, LiteRT conversion or MAUI integration.
The published checkpoint's default preprocessing size is 768; this experiment used the supported smaller 256 size.

## Why it does not fit the detector

`CocoDetector.cs` writes byte RGB pixels in NHWC layout to LiteRT buffers. Its `CocoSsdMobilenetV3` model
returns four outputs, which `CocoSsdMobilenet.GetResults` decodes:

- Normalized boxes `[1, N, 4]` in ymin/xmin/ymax/xmax order.
- COCO class IDs `[1, N]`.
- Confidence scores `[1, N]`.
- Detection count `[1]`.

Those results drive the bounding boxes and the visual agent's person/other-object events. V5's features supply none
of these predictions. Changing the model download URL or converting the encoder to `.tflite` cannot add the missing
trained predictions. The published checkpoint also cannot replace the standalone ImageNet classification demo
without a trained classification head.

The M1/PyTorch V5 timing is not comparable to the existing Pixel Fold/LiteRT V3 timings in `TODO.md`: the devices,
runtimes, numerical precision and tasks differ. No V5 speed, accuracy or memory improvement is established.

## Reproduce the encoder probe

Use Python 3.12 and an isolated environment. The initial run downloads about 1.18 GB of public weights into the
specified cache. It uses safetensors, a pinned checkpoint revision and an expected SHA-256; no remote model code or
pickle weights are loaded. Run from the repository root:

```sh
python3.12 -m venv /private/tmp/emgu-mnv5-venv
/private/tmp/emgu-mnv5-venv/bin/python -m pip install torch==2.14.1 torchvision==0.29.1 timm==1.0.30
HF_HOME=/private/tmp/emgu-mnv5-cache /private/tmp/emgu-mnv5-venv/bin/python \
  Emgu.LiteRT.Example/Maui.Lite/tools/probe_mobilenet_v5.py \
  --cache /private/tmp/emgu-mnv5-cache/hub
```

Add `HF_HUB_OFFLINE=1` to repeat using the cached files. The script accepts `--image`, `--threads` and
`--size 256|512|768`; larger sizes have not been measured here. Results are printed as JSON.
The environment/cache are disposable and separate from the app's model storage.

## What a replacement experiment requires

1. Obtain a trained V5 COCO detector, or choose a detection head and train/fine-tune it with annotated data.
   An encoder-only timing test does not complete this step.
2. Establish input normalization/layout, label mapping, box decoding, score calibration and NMS from that model's
   reference implementation. Export the whole trained detector to LiteRT with supported operations.
3. Add a separate detector adapter. Compare its LiteRT CPU results with the original detector's reference results
   on known frames before attempting GPU; accept GPU only after numerical checks and a measured speed win.
4. Compare against V3 on the same device/images: COCO validation accuracy, person-alert misses/false positives,
   complete frame latency, startup time, memory alongside Gemma GPU, sustained camera rate, battery and thermals.
   Different models need task-quality comparisons, not identical V3 score/box assertions.
5. Integrate it as an optional visual agent for testing. Promote it to the default only after it meets the agreed
   quality/resource targets. A backbone version number is not evidence of a better mobile detector.
