#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Check a converted D-FINE-N LiteRT model against its ONNX export (ONNX Runtime) on the demo's four bundled images.

Preprocessing follows D-FINE's: the image resized (not letterboxed) to size x size, BGR to RGB, divided by 255, NCHW for
ONNX and NHWC for the LiteRT model. Decoding: sigmoid over the 300 queries x 80 classes, the best scores first, boxes
cx,cy,w,h normalized to the input and scaled to the original image. There is no NMS. The decoded detections (score >=
0.5) must agree - the same count and labels, scores within 0.01, boxes within 2 pixels - or the script exits 1. Raw
outputs are reported too (they can differ in queries nobody keeps). Timings are inference only, so don't compare them with
the app's full-pipeline numbers. Requires the environment in DFINE_EVALUATION.md.
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


def preprocess(path, size):
    bgr = cv2.imread(str(path))
    if bgr is None:
        raise RuntimeError("Can't read " + str(path))
    rgb = cv2.cvtColor(cv2.resize(bgr, (size, size), interpolation=cv2.INTER_LINEAR), cv2.COLOR_BGR2RGB)
    return bgr.shape[1], bgr.shape[0], rgb.astype(np.float32) / 255.0


def decode(logits, boxes, width, height, threshold=0.5):
    scores = 1 / (1 + np.exp(-logits[0]))
    flat = scores.reshape(-1)
    result = []
    for i in np.argsort(-flat)[:300]:
        score = float(flat[i])
        if score < threshold:
            break
        query, cls = divmod(int(i), scores.shape[1])
        cx, cy, w, h = boxes[0, query]
        result.append({"label": LABELS[cls], "score": round(score, 4),
                       "box": [float((cx - w / 2) * width), float((cy - h / 2) * height),
                               float((cx + w / 2) * width), float((cy + h / 2) * height)]})
    return result


def agree(a, b):
    if len(a) != len(b):
        return False
    remaining = list(b)
    for x in a:
        match = next((y for y in remaining if y["label"] == x["label"] and abs(y["score"] - x["score"]) <= 0.01
                      and all(abs(p - q) <= 2 for p, q in zip(x["box"], y["box"]))), None)
        if match is None:
            return False
        remaining.remove(match)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--onnx", required=True, type=Path, help="The raw ONNX export (dfine_n_coco_<size>.onnx)")
    parser.add_argument("--tflite", required=True, type=Path, help="The converted float32 LiteRT model")
    parser.add_argument("--size", type=int, default=640)
    parser.add_argument("--threads", type=int, default=2)
    parser.add_argument("--images", nargs="*", type=Path,
                        default=[Path(__file__).resolve().parents[2] / "Emgu.LiteRT.Example" / "Maui" / "Resources" / "Raw" / n for n in DEFAULT_IMAGES])
    args = parser.parse_args()

    options = ort.SessionOptions()
    options.intra_op_num_threads = args.threads
    session = ort.InferenceSession(str(args.onnx), options, providers=["CPUExecutionProvider"])
    interpreter = Interpreter(model_path=str(args.tflite), num_threads=args.threads)
    interpreter.allocate_tensors()
    inp = interpreter.get_input_details()[0]
    if list(inp["shape"]) != [1, args.size, args.size, 3]:
        raise RuntimeError(f"Expected NHWC input [1,{args.size},{args.size},3], got {list(inp['shape'])}")

    def run_tflite(x):
        interpreter.set_tensor(inp["index"], x[None].astype(inp["dtype"]))
        interpreter.invoke()
        outputs = [interpreter.get_tensor(o["index"]) for o in interpreter.get_output_details()]
        return next(o for o in outputs if o.shape[-1] == 80), next(o for o in outputs if o.shape[-1] == 4)

    report, failed = {}, False
    for image in args.images:
        width, height, x = preprocess(image, args.size)
        ref_logits, ref_boxes = session.run(["pred_logits", "pred_boxes"], {"images": np.transpose(x, (2, 0, 1))[None]})
        logits, boxes = run_tflite(x)
        expected, actual = decode(ref_logits, ref_boxes, width, height), decode(logits, boxes, width, height)
        ok = agree(expected, actual)
        failed |= not ok
        report[image.name] = {"agree": ok, "max_abs_logits": float(np.max(np.abs(logits - ref_logits))),
                              "max_abs_boxes": float(np.max(np.abs(boxes - ref_boxes))),
                              "detections": [(d["label"], d["score"]) for d in actual]}

    _, _, x = preprocess(args.images[0], args.size)
    nchw = np.transpose(x, (2, 0, 1))[None]
    for _ in range(3):
        session.run(None, {"images": nchw}); run_tflite(x)
    times = {"onnxruntime": [], "litert": []}
    for _ in range(15):
        start = time.perf_counter(); session.run(None, {"images": nchw}); times["onnxruntime"].append((time.perf_counter() - start) * 1000)
        start = time.perf_counter(); run_tflite(x); times["litert"].append((time.perf_counter() - start) * 1000)
    report[f"median_ms_{args.threads}_threads"] = {k: round(statistics.median(v), 1) for k, v in times.items()}
    print(json.dumps(report, indent=1))
    if failed:
        print("PARITY FAILED: decoded detections differ from the ONNX model's", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()
