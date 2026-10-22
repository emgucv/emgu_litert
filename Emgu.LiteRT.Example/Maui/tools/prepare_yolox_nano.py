#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Convert official Megvii YOLOX-Nano ONNX weights to LiteRT. No Ultralytics dependency.

See YOLOX_EVALUATION.md for pinned dependencies, licensing, parity checks and limitations.
The converter may rewrite its input file, so it receives a disposable copy.
"""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import urllib.request
from pathlib import Path

URL = "https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx"
SOURCE_SHA256 = "c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d"
CONVERTED_SHA256 = "4be8f0e9e203e8150738db6b3e8486d49b3c3d16c46eba6ed3ccb70bb215fe00"


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="Directory for source, work copy and converted models")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    source = args.output / "official_yolox_nano.onnx"
    if not source.exists() or sha256(source) != SOURCE_SHA256:
        temporary = source.with_suffix(".download")
        try:
            with urllib.request.urlopen(URL, timeout=60) as response, temporary.open("wb") as target:
                shutil.copyfileobj(response, target)
            if sha256(temporary) != SOURCE_SHA256:
                raise RuntimeError("Official ONNX checkpoint SHA-256 mismatch")
            temporary.replace(source)
        finally:
            temporary.unlink(missing_ok=True)
    work = args.output / "conversion_input"
    work.mkdir(exist_ok=True)
    shutil.copyfile(source, work / "yolox_nano.onnx")
    converted = args.output / "litert"
    subprocess.run([sys.executable, "-m", "onnx2tf", "-i", str(work / "yolox_nano.onnx"),
                    "-o", str(converted), "-tb", "flatbuffer_direct", "-osd", "-nuo"], check=True)
    model = converted / "yolox_nano_float32.tflite"
    actual = sha256(model)
    if actual != CONVERTED_SHA256:
        raise RuntimeError("Converted artifact differs from the tested model. Recheck conversion/runtime parity before bundling it: " + actual)
    print(json.dumps({"source_url": URL, "source_sha256": SOURCE_SHA256,
                      "converted_sha256": actual, "model": str(model)}, indent=2))


if __name__ == "__main__":
    main()
