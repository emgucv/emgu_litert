#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Check LiteRT outputs against official YOLOX ONNX and its own preprocessing/NMS.

Requires the environment and pinned source checkout in YOLOX_EVALUATION.md.
Uses only the demo's four bundled images. Prints reference detections and short
inference-only timings as JSON; does not establish COCO validation accuracy.
"""
import argparse
import ast
import importlib.util
import json
import statistics
import subprocess
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort
from ai_edge_litert.interpreter import Interpreter
from prepare_yolox_nano import SOURCE_SHA256, CONVERTED_SHA256, sha256

SOURCE_REVISION = "6ddff4824372906469a7fae2dc3206c7aa4bbaee"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--models", required=True, type=Path)
    parser.add_argument("--upstream", required=True, type=Path)
    args = parser.parse_args()
    revision = subprocess.check_output(["git", "-C", str(args.upstream), "rev-parse", "HEAD"], text=True).strip()
    if revision != SOURCE_REVISION:
        raise RuntimeError("Use the pinned YOLOX source revision: " + SOURCE_REVISION)
    source = args.models / "official_yolox_nano.onnx"
    converted = args.models / "litert/yolox_nano_float32.tflite"
    if sha256(source) != SOURCE_SHA256 or sha256(converted) != CONVERTED_SHA256:
        raise RuntimeError("Model SHA-256 mismatch")
    spec = importlib.util.spec_from_file_location("official_demo", args.upstream / "yolox/utils/demo_utils.py")
    official = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(official)
    # Execute the original preproc function without importing YOLOX's training dependencies.
    tree = ast.parse((args.upstream / "yolox/data/data_augment.py").read_text())
    preproc = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "preproc")
    scope = {"np": np, "cv2": cv2}
    exec(compile(ast.Module(body=[preproc], type_ignores=[]), "official_preproc", "exec"), scope)
    options = ort.SessionOptions()
    options.intra_op_num_threads = 2
    options.inter_op_num_threads = 1
    onnx = ort.InferenceSession(str(source), sess_options=options, providers=["CPUExecutionProvider"])
    lite = Interpreter(model_path=str(converted), num_threads=2)
    lite.allocate_tensors()
    input_info, = lite.get_input_details()
    output_info, = lite.get_output_details()
    images = Path(__file__).resolve().parents[2] / "Emgu.LiteRT.Example/Maui/Resources/Raw"
    samples = []
    for name in ("surfers.jpg", "dog416.png", "space_shuttle.jpg", "tulips.jpg"):
        image = cv2.imread(str(images / name))
        pixels, ratio = scope["preproc"](image, (416, 416))
        expected = onnx.run(None, {"images": pixels[None]})[0]
        lite.set_tensor(input_info["index"], pixels.transpose(1, 2, 0)[None])
        lite.invoke()
        actual = lite.get_tensor(output_info["index"]).transpose(0, 2, 1)
        np.testing.assert_allclose(expected, actual, rtol=1e-3, atol=5e-4)

        def decode(raw):
            prediction = official.demo_postprocess(raw.copy(), (416, 416))[0]
            boxes = np.empty_like(prediction[:, :4])
            boxes[:, :2] = prediction[:, :2] - prediction[:, 2:4] / 2
            boxes[:, 2:] = prediction[:, :2] + prediction[:, 2:4] / 2
            detections = official.multiclass_nms(boxes / ratio, prediction[:, 4:5] * prediction[:, 5:],
                                               nms_thr=.45, score_thr=.25)
            return [] if detections is None else detections.tolist()

        detections = decode(expected)
        np.testing.assert_allclose(np.array(detections), np.array(decode(actual)), rtol=1e-3, atol=.01)
        for _ in range(3):
            lite.invoke()
        times = []
        for _ in range(12):
            start = time.perf_counter()
            lite.invoke()
            times.append((time.perf_counter() - start) * 1000)
        samples.append({"image": name, "width": image.shape[1], "height": image.shape[0],
                        "max_raw_difference": float(np.max(np.abs(expected - actual))),
                        "litert_median_inference_ms": statistics.median(times),
                        "detections_xyxy_score_class": detections})
    print(json.dumps({"onnx_sha256": SOURCE_SHA256, "tflite_sha256": CONVERTED_SHA256, "samples": samples}, indent=2))


if __name__ == "__main__":
    main()
