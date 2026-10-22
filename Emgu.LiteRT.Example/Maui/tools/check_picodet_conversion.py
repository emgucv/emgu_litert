#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Check a converted PicoDet-S LiteRT model against the official ONNX export (ONNX Runtime) on the demo's four images.

Preprocessing follows PaddleDetection's test reader: the image resized (not letterboxed) to 416 x 416 with cubic
interpolation, BGR to RGB, divided by 255, minus the ImageNet mean and divided by its standard deviation, NCHW for ONNX and
NHWC for LiteRT. The network returns, for strides 8, 16, 32 and 64, class scores (already sigmoid) [1,N,80] and box
distributions [1,N,32]. Decoding (PicoHeadV2): softmax over the 8 bins of each side, times the stride, distances from the
cell centre ((x + 0.5) * stride); class-aware NMS at 0.6 (pixel areas with +1, as PaddleDetection's multiclass NMS). The decoded detections (score > 0.5) must agree - the same count
and classes, scores within 0.01, boxes within 2 pixels - or the script exits 1. With --write-reference the ONNX
detections are written as JSON, the fixture of the C# test. Timings are inference only. Environment: PICODET_EVALUATION.md.
"""
import argparse
import json
import statistics
import sys
import time
from pathlib import Path

import cv2
import numpy as np
import onnxruntime as ort
from ai_edge_litert.interpreter import Interpreter

LABELS = ("person,bicycle,car,motorcycle,airplane,bus,train,truck,boat,traffic light,fire hydrant,stop sign,parking meter,"
          "bench,bird,cat,dog,horse,sheep,cow,elephant,bear,zebra,giraffe,backpack,umbrella,handbag,tie,suitcase,frisbee,skis,"
          "snowboard,sports ball,kite,baseball bat,baseball glove,skateboard,surfboard,tennis racket,bottle,wine glass,cup,fork,"
          "knife,spoon,bowl,banana,apple,sandwich,orange,broccoli,carrot,hot dog,pizza,donut,cake,chair,couch,potted plant,bed,"
          "dining table,toilet,tv,laptop,mouse,remote,keyboard,cell phone,microwave,oven,toaster,sink,refrigerator,book,clock,"
          "vase,scissors,teddy bear,hair drier,toothbrush").split(",")
DEFAULT_IMAGES = ["surfers.jpg", "dog416.png", "space_shuttle.jpg", "tulips.jpg"]
SIZE, STRIDES, BINS = 416, (8, 16, 32, 64), 8
MEAN = np.array([0.485, 0.456, 0.406], np.float32)
STD = np.array([0.229, 0.224, 0.225], np.float32)


def preprocess(path):
    bgr = cv2.imread(str(path))
    if bgr is None:
        raise RuntimeError("Can't read " + str(path))
    rgb = cv2.cvtColor(cv2.resize(bgr, (SIZE, SIZE), interpolation=cv2.INTER_CUBIC), cv2.COLOR_BGR2RGB)
    return bgr.shape[1], bgr.shape[0], (rgb.astype(np.float32) / 255 - MEAN) / STD


def overlap(a, b):
    # Pixel areas with +1, like PaddleDetection's multiclass NMS (normalized=False), in 416-pixel coordinates.
    ix = max(0, min(a[2], b[2]) - max(a[0], b[0]) + 1)
    iy = max(0, min(a[3], b[3]) - max(a[1], b[1]) + 1)
    inter = ix * iy
    return inter / ((a[2] - a[0] + 1) * (a[3] - a[1] + 1) + (b[2] - b[0] + 1) * (b[3] - b[1] + 1) - inter)


def decode(scores_by_level, regs_by_level, width, height, threshold=0.5, nms=0.6):
    boxes, scores = [], []
    for cls, reg, stride in zip(scores_by_level, regs_by_level, STRIDES):
        n = -(-SIZE // stride)  # the stride-64 grid is 7 x 7
        e = np.exp(reg[0].reshape(-1, 4, BINS) - reg[0].reshape(-1, 4, BINS).max(-1, keepdims=True))
        d = ((e / e.sum(-1, keepdims=True)) * np.arange(BINS)).sum(-1) * stride
        ys, xs = np.divmod(np.arange(n * n), n)
        cx, cy = (xs + 0.5) * stride, (ys + 0.5) * stride
        boxes.append(np.stack([cx - d[:, 0], cy - d[:, 1], cx + d[:, 2], cy + d[:, 3]], 1))
        scores.append(cls[0])
    boxes, scores = np.concatenate(boxes), np.concatenate(scores)
    result = []
    for c in range(80):
        candidates = np.where(scores[:, c] > threshold)[0]
        kept = []
        for i in candidates[np.argsort(-scores[candidates, c])]:
            if all(overlap(boxes[i], boxes[k]) <= nms for k in kept):
                kept.append(i)
        for i in kept:
            b = boxes[i] * [width / SIZE, height / SIZE, width / SIZE, height / SIZE]
            result.append({"label": LABELS[c], "class": c, "score": round(float(scores[i, c]), 4),
                           "box": [float(np.clip(b[0], 0, width)), float(np.clip(b[1], 0, height)),
                                   float(np.clip(b[2], 0, width)), float(np.clip(b[3], 0, height))]})
    return sorted(result, key=lambda d: -d["score"])


def levels(outputs):
    """Class scores and box distributions, largest grid (stride 8) first, whatever order the model lists them in."""
    scores = sorted([o for o in outputs if o.shape[-1] == 80], key=lambda o: -o.shape[1])
    regs = sorted([o for o in outputs if o.shape[-1] == 4 * BINS], key=lambda o: -o.shape[1])
    return scores, regs


def agree(a, b):
    return len(a) == len(b) and all(x["class"] == y["class"] and abs(x["score"] - y["score"]) <= 0.01
                                    and all(abs(p - q) <= 2 for p, q in zip(x["box"], y["box"])) for x, y in zip(a, b))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--onnx", required=True, type=Path, help="The official ONNX export (picodet_s_416_coco_lcnet.onnx)")
    parser.add_argument("--tflite", required=True, type=Path, help="The converted float32 LiteRT model")
    parser.add_argument("--threads", type=int, default=2)
    parser.add_argument("--write-reference", type=Path, help="Write the ONNX detections as the C# test's fixture")
    parser.add_argument("--images", nargs="*", type=Path,
                        default=[Path(__file__).resolve().parent.parent / "Resources" / "Raw" / n for n in DEFAULT_IMAGES])
    args = parser.parse_args()

    options = ort.SessionOptions()
    options.intra_op_num_threads = args.threads
    options.log_severity_level = 3
    session = ort.InferenceSession(str(args.onnx), options, providers=["CPUExecutionProvider"])
    interpreter = Interpreter(model_path=str(args.tflite), num_threads=args.threads)
    interpreter.allocate_tensors()
    inp = interpreter.get_input_details()[0]
    if list(inp["shape"]) != [1, SIZE, SIZE, 3]:
        raise RuntimeError(f"Expected NHWC input [1,{SIZE},{SIZE},3], got {list(inp['shape'])}")

    def run_tflite(x):
        interpreter.set_tensor(inp["index"], x[None].astype(inp["dtype"]))
        interpreter.invoke()
        return [interpreter.get_tensor(o["index"]) for o in interpreter.get_output_details()]

    report, failed, reference = {}, False, []
    for image in args.images:
        width, height, x = preprocess(image)
        onnx_outputs = session.run(None, {"image": np.transpose(x, (2, 0, 1))[None]})
        lite_outputs = run_tflite(x)
        expected, actual = decode(*levels(onnx_outputs), width, height), decode(*levels(lite_outputs), width, height)
        ok = agree(expected, actual)
        failed |= not ok
        raw = max(float(np.max(np.abs(a - b))) for a, b in zip(sum(levels(lite_outputs), []), sum(levels(onnx_outputs), [])))
        report[image.name] = {"agree": ok, "max_abs_raw": raw, "detections": [(d["label"], d["score"]) for d in actual]}
        reference.append({"image": image.name, "width": width, "height": height,
                          "detections_xyxy_score_class": [d["box"] + [d["score"], d["class"]] for d in expected]})

    _, _, x = preprocess(args.images[0])
    nchw = np.transpose(x, (2, 0, 1))[None]
    for _ in range(3):
        session.run(None, {"image": nchw})
        run_tflite(x)
    times = {"onnxruntime": [], "litert": []}
    for _ in range(15):
        start = time.perf_counter(); session.run(None, {"image": nchw}); times["onnxruntime"].append((time.perf_counter() - start) * 1000)
        start = time.perf_counter(); run_tflite(x); times["litert"].append((time.perf_counter() - start) * 1000)
    report[f"median_ms_{args.threads}_threads"] = {k: round(statistics.median(v), 1) for k, v in times.items()}
    print(json.dumps(report, indent=1))
    if args.write_reference:
        import hashlib
        digest = lambda p: hashlib.sha256(Path(p).read_bytes()).hexdigest()
        args.write_reference.write_text(json.dumps({"onnx_sha256": digest(args.onnx), "tflite_sha256": digest(args.tflite),
                                                    "size": SIZE, "samples": reference}, indent=1))
    if failed:
        print("PARITY FAILED: decoded detections differ from the ONNX model's", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
