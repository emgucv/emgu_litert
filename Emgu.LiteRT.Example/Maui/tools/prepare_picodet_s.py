#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Convert the official PP-PicoDet-S (416, COCO, LCNet) ONNX export to a float32 LiteRT model that runs on the GPU.

PicoDet: https://github.com/PaddlePaddle/PaddleDetection (Apache-2.0, code and weights). Baidu publishes the network
without post-processing as an ONNX file (configs/picodet/README_en.md of release/2.8.1); this script checks its
SHA-256, simplifies it, rewrites its two HardSigmoid nodes and converts it with onnx2tf. See PICODET_EVALUATION.md for
the environment, the decoding, parity checks, timings and licence notes.

Why the rewrite: onnx2tf turns HardSigmoid into a clip to 0..1, which the TFLite converter fuses into RELU_0_TO_1, an op
LiteRT's GPU delegates (Metal, OpenCL) don't support - the whole model then fails to compile for the GPU. The same
function as Mul, Add, Relu, Sub, Relu, Sub (min(relu(a*x+b), 1) = 1 - relu(1 - relu(a*x+b))) is fully accelerated. The
result must have no dynamic tensors and no RELU_0_TO_1; this script checks both.
"""
import argparse
import hashlib
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

ONNX_URL = "https://paddledet.bj.bcebos.com/deploy/third_engine/picodet_s_416_coco_lcnet.onnx"
ONNX_SHA256 = "d080f8d9931bf9164934a7863e989605c7b3aa7751e35be406e20aa6b68256a7"


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def rewrite_hard_sigmoid(source, target):
    import numpy as np
    import onnx
    from onnx import helper, numpy_helper
    model = onnx.load(str(source))
    graph = model.graph
    nodes, rewritten = [], 0

    def const(name, value):
        graph.initializer.append(numpy_helper.from_array(np.array(value, np.float32), name))
        return name

    for node in graph.node:
        if node.op_type != "HardSigmoid":
            nodes.append(node)
            continue
        alpha = next((a.f for a in node.attribute if a.name == "alpha"), 0.2)
        beta = next((a.f for a in node.attribute if a.name == "beta"), 0.5)
        p = node.name
        nodes += [helper.make_node("Mul", [node.input[0], const(p + "_a", alpha)], [p + "_m"], p + "_mul"),
                  helper.make_node("Add", [p + "_m", const(p + "_b", beta)], [p + "_s"], p + "_add"),
                  helper.make_node("Relu", [p + "_s"], [p + "_r"], p + "_relu"),
                  helper.make_node("Sub", [const(p + "_one", 1.0), p + "_r"], [p + "_d"], p + "_sub1"),
                  helper.make_node("Relu", [p + "_d"], [p + "_e"], p + "_relu2"),
                  helper.make_node("Sub", [p + "_one", p + "_e"], [node.output[0]], p + "_sub2")]
        rewritten += 1
    del graph.node[:]
    graph.node.extend(nodes)
    onnx.checker.check_model(model)
    onnx.save(model, str(target))
    print(f"HardSigmoid nodes rewritten: {rewritten}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--output", required=True, type=Path, help="Directory for the ONNX files and the LiteRT model")
    args = parser.parse_args()
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)

    official = args.output / "picodet_s_416_coco_lcnet.onnx"
    if not official.exists() or sha256(official) != ONNX_SHA256:
        temporary = official.with_suffix(".download")
        try:
            with urllib.request.urlopen(ONNX_URL, timeout=60) as response, temporary.open("wb") as target:
                shutil.copyfileobj(response, target)
            if sha256(temporary) != ONNX_SHA256:
                raise RuntimeError("Official ONNX SHA-256 mismatch")
            temporary.replace(official)
        finally:
            temporary.unlink(missing_ok=True)

    work = args.output / "conversion"
    work.mkdir(exist_ok=True)
    simplified = work / "picodet_s_416_sim.onnx"
    subprocess.run([sys.executable, "-m", "onnxsim", str(official), str(simplified)], check=True)
    # onnx2tf names its outputs after this file.
    gpu_onnx = work / "picodet_s_416_gpu.onnx"
    rewrite_hard_sigmoid(simplified, gpu_onnx)
    converted = work / "litert"
    subprocess.run([sys.executable, "-m", "onnx2tf", "-i", str(gpu_onnx), "-o", str(converted),
                    "-ois", "image:1,3,416,416", "-tb", "tf_converter", "-osd", "-nuo"], check=True)
    model = converted / "picodet_s_416_gpu_float32.tflite"

    from ai_edge_litert.interpreter import Interpreter, OpResolverType
    interpreter = Interpreter(model_path=str(model))
    interpreter.allocate_tensors()
    dynamic = [t["name"] for t in interpreter.get_tensor_details() if any(int(d) < 0 for d in t["shape_signature"])]
    if dynamic:
        raise RuntimeError(f"{len(dynamic)} dynamic tensors remain, e.g. {dynamic[0]}")
    # Without the default XNNPACK delegate, which would hide the ops it takes over.
    plain = Interpreter(model_path=str(model), experimental_op_resolver_type=OpResolverType.BUILTIN_WITHOUT_DEFAULT_DELEGATES)
    ops = {d["op_name"] for d in plain._get_ops_details()}
    if "RELU_0_TO_1" in ops:
        raise RuntimeError("RELU_0_TO_1 is still in the graph: LiteRT's GPU delegates don't support it")
    print(f"{model}\n  {model.stat().st_size} bytes, SHA-256 {sha256(model)}, input {list(interpreter.get_input_details()[0]['shape'])}, "
          f"{len(interpreter.get_tensor_details())} tensors, 0 dynamic, ops: {', '.join(sorted(ops))}")


if __name__ == "__main__":
    main()
