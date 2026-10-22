#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Run the published V5 encoder; this is not an object detector or a MAUI backend.

Requires torch, torchvision and timm (see MOBILENET_V5_EVALUATION.md).
Downloads about 1.18 GB of safetensors into --cache; never loads pickle weights.
"""

import argparse
import hashlib
import json
import platform
import statistics
import time
from pathlib import Path

import timm
import torch
from huggingface_hub import hf_hub_download
from PIL import Image
from safetensors.torch import load_model


REPOSITORY = "timm/mobilenetv5_300m.gemma3n"
REVISION = "f3a689df9847019f95abea17423a680b3d4512ac"
WEIGHTS_SHA256 = "affbbd57927c4138b783564d98603ef54530eb02ddf25cbabf79a4b2ce0fb321"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", type=Path, default=Path(__file__).resolve().parents[2] / "Emgu.LiteRT.Example/Maui/Resources/Raw/surfers.jpg")
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--size", type=int, choices=(256, 512, 768), default=256)
    parser.add_argument("--threads", type=int, default=4)
    args = parser.parse_args()
    if args.threads < 1:
        parser.error("--threads must be positive")
    torch.set_num_threads(args.threads)
    config_path = hf_hub_download(REPOSITORY, "config.json", revision=REVISION, cache_dir=args.cache, token=False)
    config = json.loads(Path(config_path).read_text())
    if config["num_classes"] != 0:
        raise RuntimeError("The pinned checkpoint's encoder contract has changed")
    checkpoint = hf_hub_download(REPOSITORY, "model.safetensors", revision=REVISION, cache_dir=args.cache, token=False)
    with open(checkpoint, "rb") as weights:
        if hashlib.file_digest(weights, "sha256").hexdigest() != WEIGHTS_SHA256:
            raise RuntimeError("Checkpoint SHA-256 mismatch")
    model = timm.create_model("mobilenetv5_300m.gemma3n", pretrained=False, num_classes=0).eval().cpu()
    load_model(model, checkpoint, strict=True)
    data_config = timm.data.resolve_model_data_config(model)
    data_config["input_size"] = (3, args.size, args.size)
    transform = timm.data.create_transform(**data_config, is_training=False)
    with Image.open(args.image) as image:
        pixels = transform(image.convert("RGB")).unsqueeze(0)
    with torch.inference_mode():
        features = model.forward_features(pixels)  # Warmup and inspect the spatial encoder output.
        if not torch.isfinite(features).all():
            raise RuntimeError("Non-finite encoder features")
        feature_shape = list(features.shape)
        del features
        durations = []
        for _ in range(3):
            start = time.perf_counter()
            output = model(pixels)
            durations.append((time.perf_counter() - start) * 1000)
            if not torch.isfinite(output).all():
                raise RuntimeError("Non-finite model output")
    print(json.dumps({
        "repository": REPOSITORY,
        "revision": REVISION,
        "weights_sha256": WEIGHTS_SHA256,
        "platform": platform.platform(),
        "torch": torch.__version__,
        "timm": timm.__version__,
        "backend": "PyTorch CPU FP32; not LiteRT",
        "threads": args.threads,
        "parameters": sum(p.numel() for p in model.parameters()),
        "num_classes": model.num_classes,
        "input_shape": list(pixels.shape),
        "feature_shape": feature_shape,
        "output_shape": list(output.shape),
        "finite_outputs": True,
        "warmed_inference_ms": durations,
        "median_inference_ms": statistics.median(durations),
        "detection_head": False,
        "result": "Encoder features only; no COCO classes, confidence scores, boxes or detection count",
    }, indent=2))


if __name__ == "__main__":
    main()
