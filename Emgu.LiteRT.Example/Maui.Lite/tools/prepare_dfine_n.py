#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Convert the official D-FINE-N (COCO) checkpoint to a float32 LiteRT model with static shapes.

D-FINE: https://github.com/Peterande/D-FINE (Apache-2.0, code and COCO weights). See DFINE_EVALUATION.md for the
pinned environment, the conversion pitfalls, parity checks, timings and licence notes. Needs the pinned source checkout:

    git clone https://github.com/Peterande/D-FINE.git <upstream>
    git -C <upstream> checkout 956d1709314c2c6a4df6f34de232054578a7449f

Steps: ONNX export of the raw network (no post-processor: pred_logits [1,300,80] before the sigmoid and pred_boxes
[1,300,4] as normalized cx,cy,w,h), onnxsim, then onnx2tf with its TensorFlow converter and Erf replaced by plain ops.
The converter's default direct path leaves 137 tensors dynamic (LiteRT's GPU compiler rejects those), and the
TensorFlow converter alone needs a Flex Erf op LiteRT doesn't have. The result must have no dynamic tensors and no Flex
ops; this script checks both. A float16 file is also written by onnx2tf but can't run on LiteRT's CPU: use float32.
"""
import argparse
import hashlib
import os
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

UPSTREAM_COMMIT = "956d1709314c2c6a4df6f34de232054578a7449f"
WEIGHTS_URL = "https://github.com/Peterande/storage/releases/download/dfinev1.0/dfine_n_coco.pth"
WEIGHTS_SHA256 = "41973938d2784d38a9836990d805b8392855ebf611aba55f0f7add90e110744c"


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def export_onnx(upstream, weights, output, size):
    """Raw D-FINE-N at size x size, with the anchors and valid mask rebuilt for that size (trained at 640)."""
    import torch
    import torch.nn as nn

    sys.path.insert(0, str(upstream))
    os.chdir(upstream)
    from src.core import YAMLConfig

    cfg = YAMLConfig("configs/dfine/dfine_hgnetv2_n_coco.yml", resume=str(weights))
    cfg.yaml_cfg["HGNetv2"]["pretrained"] = False
    cfg.yaml_cfg["eval_spatial_size"] = [size, size]
    checkpoint = torch.load(weights, map_location="cpu")
    state = checkpoint["ema"]["module"] if "ema" in checkpoint else checkpoint["model"]
    own = cfg.model.state_dict()
    skipped = [k for k, v in state.items() if k in own and own[k].shape != v.shape]
    print("size-dependent buffers rebuilt:", skipped)
    cfg.model.load_state_dict({k: v for k, v in state.items() if k not in skipped}, strict=False)

    class Raw(nn.Module):
        def __init__(self):
            super().__init__()
            self.model = cfg.model.deploy()

        def forward(self, images):
            out = self.model(images)
            return out["pred_logits"], out["pred_boxes"]

    model = Raw().eval()
    data = torch.rand(1, 3, size, size)
    torch.onnx.export(model, (data,), str(output), input_names=["images"], output_names=["pred_logits", "pred_boxes"],
                      opset_version=17, do_constant_folding=True, dynamo=False)
    import onnx
    onnx.checker.check_model(onnx.load(str(output)))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--upstream", required=True, type=Path, help="D-FINE checkout at the pinned commit")
    parser.add_argument("--output", required=True, type=Path, help="Directory for the weights, ONNX files and LiteRT model")
    parser.add_argument("--size", type=int, default=640, help="Square input size (640 is what the model was trained at)")
    args = parser.parse_args()
    args.upstream = args.upstream.resolve()
    args.output = args.output.resolve()
    revision = subprocess.check_output(["git", "-C", str(args.upstream), "rev-parse", "HEAD"], text=True).strip()
    if revision != UPSTREAM_COMMIT:
        raise RuntimeError("Use the pinned D-FINE revision " + UPSTREAM_COMMIT + ", not " + revision)
    args.output.mkdir(parents=True, exist_ok=True)

    weights = args.output / "dfine_n_coco.pth"
    if not weights.exists() or sha256(weights) != WEIGHTS_SHA256:
        temporary = weights.with_suffix(".download")
        try:
            with urllib.request.urlopen(WEIGHTS_URL, timeout=60) as response, temporary.open("wb") as target:
                shutil.copyfileobj(response, target)
            if sha256(temporary) != WEIGHTS_SHA256:
                raise RuntimeError("Official checkpoint SHA-256 mismatch")
            temporary.replace(weights)
        finally:
            temporary.unlink(missing_ok=True)

    onnx_raw = args.output / f"dfine_n_coco_{args.size}.onnx"
    export_onnx(args.upstream, weights, onnx_raw, args.size)
    work = args.output / f"conversion_{args.size}"
    work.mkdir(exist_ok=True)
    source = work / f"dfine_n_{args.size}.onnx"  # onnx2tf names its outputs after this file.
    subprocess.run([sys.executable, "-m", "onnxsim", str(onnx_raw), str(source)], check=True)
    converted = work / "litert"
    subprocess.run([sys.executable, "-m", "onnx2tf", "-i", str(source), "-o", str(converted),
                    "-ois", f"images:1,3,{args.size},{args.size}", "-tb", "tf_converter", "-rtpo", "Erf", "GeLU",
                    "-osd", "-nuo"], check=True)
    model = converted / f"dfine_n_{args.size}_float32.tflite"

    # No dynamic tensors, and no Select-TF (Flex) ops: allocate_tensors fails on those in LiteRT's Python interpreter.
    from ai_edge_litert.interpreter import Interpreter
    interpreter = Interpreter(model_path=str(model))
    interpreter.allocate_tensors()
    dynamic = [t["name"] for t in interpreter.get_tensor_details() if any(int(d) < 0 for d in t["shape_signature"])]
    if dynamic:
        raise RuntimeError(f"{len(dynamic)} dynamic tensors remain (the GPU can't compile those), e.g. {dynamic[0]}")
    print(f"{model}\n  {model.stat().st_size} bytes, SHA-256 {sha256(model)}, input {list(interpreter.get_input_details()[0]['shape'])}, "
          f"{len(interpreter.get_tensor_details())} tensors, 0 dynamic")


if __name__ == "__main__":
    main()
