#!/usr/bin/env python3
# Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
"""Builds Resources/Raw/licenses.json.gz, the MAUI demo's open-source licence list (LicensesPage).

Every entry is something the app ships, downloads or shows: the .NET packages the demo restores for Android, iOS and
Mac Catalyst (from `dotnet list package --include-transitive`, licences read from the NuGet cache), what the native
libraries are built from (LiteRT, LiteRT-LM and the Bazel repositories they depend on, with the licence files Bazel
downloaded), OpenCV and the libraries it bundles in Emgu CV's runtime, and a hand-kept list of models, fonts, data and
web services. Licence texts that aren't on this machine are downloaded from the projects' repositories at pinned
versions and cached in tools/.license_cache (not committed).

Run it from the demo folder after changing packages, native dependencies, models or assets, with the natives built (so
bazel_output has the external repositories) and the demo restored:

    python3 tools/make_licenses.py

Requires network access the first time (for the cached texts) and `dotnet` on the PATH.
"""

import gzip
import hashlib
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEMO = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(DEMO))
CACHE = os.path.join(HERE, ".license_cache")
NUGET = os.path.expanduser("~/.nuget/packages")
OUTPUT = os.path.join(DEMO, "Resources", "Raw", "licenses.json.gz")
FRAMEWORKS = ["net10.0-android", "net10.0-ios", "net10.0-maccatalyst"]

texts = {}
entries = []


def text_key(text):
    text = text.replace("\r\n", "\n").strip() + "\n"
    key = hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]
    texts[key] = text
    return key


def fetch(url):
    """A licence text from the web, cached."""
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, hashlib.sha256(url.encode()).hexdigest()[:24] + ".txt")
    if not os.path.exists(path):
        # curl: Python's own HTTPS client had its connections reset by S3 on this Mac.
        result = subprocess.run(["curl", "-sSfL", "--retry", "3", "-m", "60", "-A", "Emgu LiteRT licence generator", url],
                                capture_output=True)
        if result.returncode != 0:
            sys.exit(f"Couldn't download {url}: {result.stderr.decode(errors='replace').strip()}")
        data = result.stdout.decode("utf-8", "replace")
        with open(path, "w", encoding="utf-8") as f:
            f.write(data)
    with open(path, encoding="utf-8") as f:
        return f.read()


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def add(category, name, license, text, version=None, copyright=None, url=None, used_for=None, packages=None, notice=None):
    entry = {"category": category, "name": name, "license": license}
    if version:
        entry["version"] = version
    if copyright:
        entry["copyright"] = copyright
    if url:
        entry["url"] = url
    if used_for:
        entry["usedFor"] = used_for
    if packages:
        entry["packages"] = sorted(packages, key=str.lower)
    if notice:
        entry["notice"] = notice
    entry["text"] = text_key(text)
    entries.append(entry)


APACHE = None  # The Apache License 2.0's text, read from TensorFlow's LICENSE below.

MIT_TEMPLATE = """MIT License

Copyright (c) {copyright}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""

BSD3_TEMPLATE = """Copyright (c) {copyright}

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following
   disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
   disclaimer in the documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote
   products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
"""


# ---------------------------------------------------------------- This app

def this_app():
    add("This app", "Emgu LiteRT", "GPL-3.0 or Emgu commercial licence", read(os.path.join(REPO, "LICENSE")),
        copyright="Copyright (C) 2004-2026 EMGU Corporation", url="https://www.emgu.com",
        used_for="This demo and its Emgu.LiteRT, Emgu.TF.Lite and Emgu.LiteRT.LM libraries")


# ---------------------------------------------------------------- Models, data, fonts, services

def curated():
    model = "Models (downloaded when first used)"
    hf = "https://huggingface.co/"
    for name, repo, base, by in [
        ("Gemma 4 E2B, E4B and 12B", "litert-community/gemma-4-E2B-it-litert-lm", "google/gemma-4-E2B-it", "Google LLC"),
        ("EmbeddingGemma 2", "litert-community/embeddinggemma-2-text-vision-440m-litert-lm", "google/embeddinggemma-2", "Google LLC"),
        ("Qwen3 0.6B", "litert-community/Qwen3-0.6B", "Qwen/Qwen3-0.6B", "Alibaba Cloud (Qwen team)"),
        ("Qwen3.5 0.8B, 0.8B VL and 4B", "litert-community/Qwen3.5-0.8B", "Qwen/Qwen3.5-0.8B", "Alibaba Cloud (Qwen team)"),
        ("Z-Image-Turbo", "litert-community/Z-Image-Turbo-LiteRT", "Tongyi-MAI/Z-Image-Turbo", "Alibaba Tongyi-MAI"),
    ]:
        add(model, name, "Apache-2.0", APACHE, copyright=by, url=hf + base,
            used_for="LiteRT conversion: " + hf + repo)
    add(model, "YOLOX-Nano", "Apache-2.0", fetch("https://emgu-public.s3.amazonaws.com/yolox/yolox_notice.txt") + "\n\n" +
        fetch("https://emgu-public.s3.amazonaws.com/yolox/yolox_license.txt"),
        copyright="Megvii, Inc. and its affiliates", url="https://github.com/Megvii-BaseDetection/YOLOX",
        used_for="Optional object detector; converted by Emgu to LiteRT")
    add(model, "MobileNet, Inception and COCO SSD MobileNet", "Apache-2.0", APACHE, copyright="The TensorFlow Authors",
        url="https://github.com/tensorflow/models", used_for="Image classification and object detection demos")

    fonts = "Fonts"
    add(fonts, "Inter", "OFL-1.1", fetch("https://raw.githubusercontent.com/rsms/inter/v4.1/LICENSE.txt"),
        version="4.001", copyright="Copyright 2016 The Inter Project Authors", url="https://rsms.me/inter/")
    add(fonts, "Open Sans", "Apache-2.0", APACHE, version="1.10",
        copyright="Digitized data copyright © 2010-2011, Google Corporation", url="https://fonts.google.com/specimen/Open+Sans")
    add(fonts, "Material Symbols", "Apache-2.0", APACHE, copyright="Copyright 2026 Google LLC",
        url="https://github.com/google/material-design-icons")

    data = "Data and web services"
    add(data, "GeoNames", "CC BY 4.0",
        "Place names and positions (Resources/Raw/places.tsv.gz: towns of 5,000 or more people) are from GeoNames,\n"
        "https://www.geonames.org, licensed under the Creative Commons Attribution 4.0 License:\n"
        "https://creativecommons.org/licenses/by/4.0/\n\nThe data was reduced to towns of 5,000+ people and reformatted.",
        url="https://www.geonames.org", used_for="Place names for photo search (offline)")
    add(data, "Open-Meteo", "CC BY 4.0",
        "Weather data by Open-Meteo.com (https://open-meteo.com), licensed under the Creative Commons Attribution 4.0\n"
        "License: https://creativecommons.org/licenses/by/4.0/\n\nUsed by the chat's get_weather tool when Web search is on.",
        url="https://open-meteo.com", used_for="Weather forecasts (get_weather)")
    add(data, "Wikipedia", "CC BY-SA 4.0",
        "Text from Wikipedia (https://www.wikipedia.org) is available under the Creative Commons Attribution-ShareAlike\n"
        "4.0 License (https://creativecommons.org/licenses/by-sa/4.0/); the chat's search_wikipedia tool gives the model\n"
        "excerpts of articles and names the article it used.",
        url="https://www.wikipedia.org", used_for="Wikipedia search (search_wikipedia)")
    add(data, "Frankfurter", "Free API (ECB reference rates)",
        "Currency conversion uses the Frankfurter API (https://frankfurter.dev), which publishes the European Central\n"
        "Bank's euro foreign exchange reference rates.", url="https://frankfurter.dev", used_for="convert_currency")
    add(data, "Tavily", "Tavily terms of service",
        "With a Tavily API key of the user's own, web search and page reading go through Tavily (https://tavily.com),\n"
        "under Tavily's terms of service.", url="https://tavily.com", used_for="search_web and read_web_page (optional)")

    media = "Sample media"
    # Matched by content (Git blob hash) with OpenCV's repositories.
    add(media, "Sample photo: space shuttle (space_shuttle.jpg)", "Apache-2.0", APACHE, copyright="OpenCV team",
        url="https://github.com/opencv/opencv/blob/4.x/doc/tutorials/dnn/images/space_shuttle.jpg",
        used_for="Sample image; the same file is in OpenCV's DNN tutorial images")
    # Matched by content with TensorFlow's repository and its flower_photos dataset.
    add(media, "Sample photo: surfers (surfers.jpg)", "Apache-2.0", APACHE, copyright="The TensorFlow Authors",
        url="https://github.com/tensorflow/tensorflow/blob/master/tensorflow/examples/multibox_detector/data/surfers.jpg",
        used_for="Sample image for object detection")
    add(media, "Sample photo: tulips (tulips.jpg)", "CC BY 2.0",
        "Photo by Dan Kamminga, https://www.flickr.com/photos/dankamminga/11746276/\n\n"
        "Licensed under the Creative Commons Attribution 2.0 License: https://creativecommons.org/licenses/by/2.0/\n\n"
        "From TensorFlow's flower_photos dataset (tulips/11746276_de3dec8201.jpg),\n"
        "https://storage.googleapis.com/download.tensorflow.org/example_images/flower_photos.tgz; unchanged.",
        copyright="Dan Kamminga", url="https://www.flickr.com/photos/dankamminga/11746276/",
        used_for="Sample image for image classification and photo search")
    add(media, "Sample photo: dog and bike (dog416.png)", "OpenCV test data",
        "Sample image from OpenCV's test data: https://github.com/opencv/opencv_extra/blob/4.x/testdata/dnn/dog416.png\n\n"
        "The opencv_extra repository states no licence of its own; OpenCV itself is licensed under the Apache License 2.0.",
        url="https://github.com/opencv/opencv_extra", used_for="Sample image for object detection")
    add(media, "LiteRT-LM test audio (audio_sample.wav)", "Apache-2.0", APACHE, copyright="Google LLC",
        url="https://github.com/google-ai-edge/LiteRT-LM", used_for="Sample audio attachment")


# ---------------------------------------------------------------- .NET packages

def nuget_license(package, version):
    folder = os.path.join(NUGET, package.lower(), version.lower())
    spec = next((os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".nuspec")), None) if os.path.isdir(folder) else None
    if spec is None:
        return None
    s = read(spec)
    m = re.search(r'<license type="(\w+)"[^>]*>([^<]+)</license>', s)
    copyright = (re.search(r"<copyright>([^<]+)</copyright>", s) or [None, ""])[1].strip()
    url = (re.search(r"<projectUrl>([^<]+)</projectUrl>", s) or [None, ""])[1].strip()
    if m and m.group(1) == "file":
        return {"license": "See licence file", "text": read(os.path.join(folder, m.group(2))), "copyright": copyright, "url": url}
    expression = m.group(2) if m else "See project"
    return {"license": expression, "copyright": copyright, "url": url}


def dotnet_packages():
    packages = {}
    for framework in FRAMEWORKS:
        out = subprocess.run(["dotnet", "list", os.path.join(DEMO, "Maui.Demo.Lite.csproj"), "package", "--include-transitive",
                              "--framework", framework], capture_output=True, text=True).stdout
        for line in out.splitlines():
            m = re.match(r"\s*>\s+(\S+)\s+(?:\S+\s+)?(\S+)\s*$", line)
            if m:
                packages[m.group(1)] = m.group(2)
    if not packages:
        sys.exit("dotnet list package found no packages: restore the demo first.")

    groups = {}  # Packages under one licence and copyright are listed together.
    for package, version in sorted(packages.items(), key=lambda p: p[0].lower()):
        info = nuget_license(package, version)
        if info is None:
            print("warning: no nuspec for", package, version)
            continue
        if package.startswith("Emgu."):
            continue  # Emgu CV: below, with OpenCV.
        if package.startswith(("Xamarin.", "GoogleGson")) or info["license"] == "MIT AND Apache-2.0":
            group = "androidx-glide" if "Glide" in package else "androidx"
        elif info["license"] == "MIT" and "Microsoft" in info["copyright"]:
            group = "microsoft"
        elif package.startswith("SQLitePCLRaw"):
            group = "sqlitepclraw"
        elif package.startswith("SourceGear"):
            group = "sqlite"
        else:
            group = package
        groups.setdefault(group, []).append((package, version, info))

    category = ".NET packages"
    for group, items in groups.items():
        names = [p for p, _, _ in items]
        package, version, info = items[0]
        if group == "microsoft":
            add(category, ".NET MAUI, Microsoft.Extensions.AI, Microsoft Agent Framework and other Microsoft packages", "MIT",
                MIT_TEMPLATE.format(copyright=".NET Foundation and Contributors / Microsoft Corporation"),
                copyright="© Microsoft Corporation", url="https://github.com/dotnet", packages=names)
        elif group == "androidx":
            add(category, "AndroidX, Google and Kotlin libraries for .NET for Android", "MIT (bindings) and Apache-2.0 (libraries)",
                MIT_TEMPLATE.format(copyright=".NET Foundation and Contributors") + "\n\n" + APACHE,
                copyright="Bindings © Microsoft Corporation; libraries © Google LLC, The Android Open Source Project and JetBrains s.r.o.",
                url="https://github.com/dotnet/android-libraries", packages=names)
        elif group == "sqlitepclraw":
            add(category, "SQLitePCLRaw", "Apache-2.0", APACHE, version=version, copyright=info["copyright"],
                url="https://github.com/ericsink/SQLitePCL.raw", packages=names)
        elif group == "sqlite":
            add(category, "SQLite", "Public domain", "SQLite is in the public domain: https://sqlite.org/copyright.html\n\n"
                "Built and packaged for .NET by SourceGear, LLC (SourceGear.sqlite3):\n\n" + info.get("text", ""),
                version=version, url="https://sqlite.org", packages=names)
        elif group == "androidx-glide":
            add(category, "Glide", "BSD-2-Clause, MIT and Apache-2.0",
                fetch("https://raw.githubusercontent.com/bumptech/glide/v4.16.0/LICENSE"),
                version=version, copyright="Copyright 2014 Google, Inc. (Glide); bindings © Microsoft Corporation",
                url="https://github.com/bumptech/glide", packages=names)
        else:
            license = info["license"]
            if "text" in info:
                text = info["text"]
            elif license == "MIT":
                text = MIT_TEMPLATE.format(copyright=info["copyright"] or package)
            elif license == "Apache-2.0":
                text = APACHE
            elif license == "BSD-3-Clause":
                text = BSD3_TEMPLATE.format(copyright=info["copyright"] or package)
            else:
                text = "Licence: " + license + "\n" + info["url"]
            if package == "PdfPig":
                text = fetch("https://raw.githubusercontent.com/UglyToad/PdfPig/v0.1.16/LICENSE")
            if package == "sqlite-net-pcl":
                license = "MIT"
            add(category, package, license, text, version=version, copyright=info["copyright"], url=info["url"],
                packages=names if len(names) > 1 else None)


# ---------------------------------------------------------------- Native: LiteRT, LiteRT-LM and their dependencies

# Bazel repositories of `deps(//c:litert-lm) + deps(//tfliteextern:litert_runtime_so) + deps(//tfliteextern:tfliteextern_so)`
# that are build tooling, not linked code.
BUILD_ONLY = {"bazel_tools", "build_bazel_apple_support", "platforms", "rules_cc", "rules_rust", "crate_index", "skia_user_config"}
# Rust crates in the dependency graph for other targets (Windows, WebAssembly).
OTHER_TARGETS = re.compile(r"^(windows|wasm-bindgen|js-sys|wasi)")

NATIVE_NAMES = {
    "org_tensorflow": ("TensorFlow Lite", "https://github.com/tensorflow/tensorflow"),
    "litert": ("LiteRT", "https://github.com/google-ai-edge/LiteRT"),
    "xla": ("XLA", "https://github.com/openxla/xla"), "tsl": ("TSL (TensorFlow support library)", "https://github.com/openxla/xla"),
    "XNNPACK": ("XNNPACK", "https://github.com/google/XNNPACK"), "KleidiAI": ("KleidiAI", "https://gitlab.arm.com/kleidi/kleidiai"),
    "com_google_absl": ("Abseil", "https://abseil.io"), "com_google_protobuf": ("Protocol Buffers", "https://protobuf.dev"),
    "com_googlesource_code_re2": ("RE2", "https://github.com/google/re2"), "flatbuffers": ("FlatBuffers", "https://github.com/google/flatbuffers"),
    "eigen_archive": ("Eigen", "https://eigen.tuxfamily.org"), "farmhash_archive": ("FarmHash", "https://github.com/google/farmhash"),
    "fft2d": ("Ooura FFT (fft2d)", "https://www.kurims.kyoto-u.ac.jp/~ooura/fft.html"), "gemmlowp": ("gemmlowp", "https://github.com/google/gemmlowp"),
    "ruy": ("ruy", "https://github.com/google/ruy"), "cpuinfo": ("cpuinfo", "https://github.com/pytorch/cpuinfo"),
    "pthreadpool": ("pthreadpool", "https://github.com/google/pthreadpool"), "FP16": ("FP16", "https://github.com/Maratyszcza/FP16"),
    "FXdiv": ("FXdiv", "https://github.com/Maratyszcza/FXdiv"), "arm_neon_2_x86_sse": ("ARM_NEON_2_x86_SSE", "https://github.com/intel/ARM_NEON_2_x86_SSE"),
    "kissfft": ("KISS FFT", "https://github.com/mborgerding/kissfft"), "sentencepiece": ("SentencePiece", "https://github.com/google/sentencepiece"),
    "tokenizers_cpp": ("tokenizers-cpp", "https://github.com/mlc-ai/tokenizers-cpp"), "nlohmann_json": ("JSON for Modern C++", "https://github.com/nlohmann/json"),
    "stb": ("stb", "https://github.com/nothings/stb"), "miniaudio": ("miniaudio", "https://miniaud.io"),
    "libjpeg_turbo": ("libjpeg-turbo", "https://libjpeg-turbo.org"), "png": ("libpng", "http://www.libpng.org"),
    "zlib": ("zlib", "https://zlib.net"), "minizip": ("minizip", "https://zlib.net"), "skia": ("Skia", "https://skia.org"),
    "slinky": ("Slinky", "https://github.com/dsharlet/slinky"), "opencl_headers": ("OpenCL headers", "https://github.com/KhronosGroup/OpenCL-Headers"),
    "vulkan_headers": ("Vulkan headers", "https://github.com/KhronosGroup/Vulkan-Headers"),
}

# Licence names where the text alone is ambiguous ("BSD") or lists several (Eigen); and which files to show.
NATIVE_LICENSES = {
    "arm_neon_2_x86_sse": "BSD-2-Clause", "com_google_protobuf": "BSD-3-Clause", "com_googlesource_code_re2": "BSD-3-Clause",
    "cpuinfo": "BSD-2-Clause", "pthreadpool": "BSD-2-Clause", "skia": "BSD-3-Clause", "XNNPACK": "BSD-3-Clause",
    "libjpeg_turbo": "IJG, BSD-3-Clause and zlib", "png": "libpng-2.0", "fft2d": "Ooura FFT licence (free use with notice)",
    "kissfft": "BSD-3-Clause", "KleidiAI": "Apache-2.0 and BSD-3-Clause", "farmhash_archive": "MIT", "FP16": "MIT", "FXdiv": "MIT",
    "eigen_archive": "MPL-2.0 (some files BSD-3-Clause, Apache-2.0 or MINPACK)", "miniaudio": "Public domain (Unlicense) or MIT-0",
    "stb": "MIT or public domain", "nlohmann_json": "MIT", "slinky": "MIT", "zlib": "zlib", "minizip": "zlib",
}
# Eigen also ships the GPL/LGPL texts of modules this build doesn't use.
NATIVE_FILES = {"eigen_archive": ["COPYING.README", "COPYING.MPL2", "COPYING.BSD", "COPYING.APACHE", "COPYING.MINPACK"]}

LICENSE_FILE = re.compile(r"^(license|licence|copying|copyright|notice|unlicense)", re.IGNORECASE)


def license_files(folder):
    names = sorted(f for f in os.listdir(folder) if LICENSE_FILE.match(f) and os.path.isfile(os.path.join(folder, f)))
    paths = [os.path.join(folder, f) for f in names]
    # REUSE-style projects (KleidiAI, KISS FFT) keep the licence texts in LICENSES/.
    if os.path.isdir(os.path.join(folder, "LICENSES")):
        paths += [os.path.join(folder, "LICENSES", f) for f in sorted(os.listdir(os.path.join(folder, "LICENSES")))]
    # Some crates' licence files are links to a parent folder the crate doesn't include ("../LICENCE-MIT").
    texts = [read(p) for p in paths if os.path.exists(p)]
    return "\n\n".join(t for t in texts if len(t.strip()) > 100)


def guess_license(text):
    t = text[:4000]
    for pattern, name in [(r"Apache License,?\s+Version 2\.0", "Apache-2.0"), (r"Mozilla Public License,? v(ersion)? ?2\.0", "MPL-2.0"),
                          (r"Permission is hereby granted, free of charge", "MIT"), (r"Redistribution and use in source and binary forms", "BSD"),
                          (r"This software is provided 'as-is'", "zlib"), (r"public domain", "Public domain")]:
        if re.search(pattern, t, re.IGNORECASE):
            return name
    return "See licence text"


def bazel_externals():
    roots = [os.path.join(REPO, "bazel_output", d, "external") for d in os.listdir(os.path.join(REPO, "bazel_output"))
             if os.path.isdir(os.path.join(REPO, "bazel_output", d, "external", "litert"))]
    if not roots:
        sys.exit("No LiteRT-LM Bazel output found: build the natives first (bazel_output/*/external).")
    return roots[0]


def bazel_repositories():
    """The external repositories the native libraries are built from, by `bazel query` in the LiteRT-LM workspace."""
    cached = os.path.join(CACHE, "bazel_repos.txt")
    if not os.path.exists(cached):
        query = "deps(//c:litert-lm) union deps(//tfliteextern:litert_runtime_so) union deps(//tfliteextern:tfliteextern_so)"
        out = subprocess.run(["bazel", "--output_user_root=" + os.path.join(REPO, "bazel_output"), "query", query, "--output=package",
                              "--noimplicit_deps", "--notool_deps"], cwd=os.path.join(REPO, "LiteRT-LM"), capture_output=True, text=True).stdout
        repos = sorted({line.split("//")[0][1:] for line in out.splitlines() if line.startswith("@")})
        if not repos:
            sys.exit("bazel query found no repositories (stage tfliteextern into LiteRT-LM by building the natives first).")
        os.makedirs(CACHE, exist_ok=True)
        with open(cached, "w") as f:
            f.write("\n".join(repos))
    return [r for r in read(cached).split() if r]


def native():
    external = bazel_externals()
    category = "Native libraries (LiteRT and LiteRT-LM)"
    add(category, "LiteRT-LM", "Apache-2.0", read(os.path.join(REPO, "LiteRT-LM", "LICENSE")), copyright="Google LLC",
        url="https://github.com/google-ai-edge/LiteRT-LM",
        notice="Includes LiteRT-LM's prebuilt GPU accelerators (OpenCL, Metal) and Gemma model constraint provider.")
    add(category, "libc++ (LLVM)", "Apache-2.0 WITH LLVM-exception",
        fetch("https://raw.githubusercontent.com/llvm/llvm-project/llvmorg-19.1.0/libcxx/LICENSE.TXT"),
        url="https://libcxx.llvm.org", used_for="C++ standard library linked into the native libraries")

    crates = []
    for repo in bazel_repositories():
        if repo in BUILD_ONLY:
            continue
        folder = os.path.join(external, repo)
        if repo.startswith("crate_index__"):
            m = re.match(r"crate_index__(.+)-(\d[\w.+-]*)$", repo)
            name, version = m.group(1), m.group(2)
            if OTHER_TARGETS.match(name):
                continue
            cargo = read(os.path.join(folder, "Cargo.toml")) if os.path.exists(os.path.join(folder, "Cargo.toml")) else ""
            license = (re.search(r'^license\s*=\s*"([^"]+)"', cargo, re.MULTILINE) or [None, "See licence text"])[1]
            authors = (re.search(r"^authors\s*=\s*\[([^\]]*)\]", cargo, re.MULTILINE) or [None, ""])[1]
            authors = ", ".join(a.strip().strip('"') for a in authors.split(",") if a.strip())
            text = license_files(folder) if os.path.isdir(folder) else ""
            if not text:
                # The licences the crate declares, in their standard texts.
                parts = []
                if "MIT" in license:
                    parts.append(MIT_TEMPLATE.format(copyright=authors or name + " authors"))
                if "Apache" in license:
                    parts.append(APACHE)
                text = "\n\n".join(parts) or "Licence: " + license + "\nhttps://crates.io/crates/" + name
            crates.append((name, version, license, authors, text))
            continue
        if repo == "fft2d":
            text = read(os.path.join(folder, "readme.txt"))
        elif repo == "minizip":
            text = "minizip is part of zlib (contrib/minizip) and is distributed under the zlib licence:\n\n" + \
                   license_files(os.path.join(external, "zlib"))
        elif repo in NATIVE_FILES:
            text = "\n\n".join(read(os.path.join(folder, f)) for f in NATIVE_FILES[repo])
        else:
            text = license_files(folder) if os.path.isdir(folder) else ""
        if not text:
            print("warning: no licence text for", repo)
            continue
        name, url = NATIVE_NAMES.get(repo, (repo, None))
        add(category, name, NATIVE_LICENSES.get(repo) or guess_license(text), text, url=url)

    for name, version, license, authors, text in sorted(crates, key=lambda c: c[0].lower()):
        add("Rust crates (in LiteRT-LM's tokenizers and constrained decoding)", name, license, text, version=version,
            copyright=authors or None, url="https://crates.io/crates/" + name)


# ---------------------------------------------------------------- Emgu CV and OpenCV

def opencv():
    category = "Emgu CV and OpenCV"
    emgu = os.path.join(NUGET, "emgu.cv", "4.12.0.5764", "LICENSE.txt")
    add(category, "Emgu CV", "GPL-3.0 or Emgu commercial licence", read(emgu), version="4.12.0",
        copyright="Copyright (C) Emgu Corporation", url="https://www.emgu.com", used_for="Image decoding and drawing in the demos")
    cv = "https://raw.githubusercontent.com/opencv/opencv/4.12.0/"
    add(category, "OpenCV", "Apache-2.0", fetch(cv + "LICENSE") + "\n\n" + fetch(cv + "COPYRIGHT"), version="4.12.0",
        url="https://opencv.org")
    # The libraries OpenCV builds into Emgu CV's runtime ("3rdparty dependencies" in its build information).
    for name, version, license, path, url in [
        ("libjpeg-turbo", "3.1.0", "IJG, BSD-3-Clause and zlib", "3rdparty/libjpeg-turbo/LICENSE.md", "https://libjpeg-turbo.org"),
        ("libpng", "1.6.43", "libpng-2.0", "3rdparty/libpng/LICENSE", "http://www.libpng.org"),
        ("libtiff", "4.6.0", "libtiff", "3rdparty/libtiff/COPYRIGHT", "https://libtiff.gitlab.io/libtiff/"),
        ("libwebp", None, "BSD-3-Clause", "3rdparty/libwebp/COPYING", "https://developers.google.com/speed/webp"),
        ("OpenJPEG", "2.5.3", "BSD-2-Clause", "3rdparty/openjpeg/LICENSE", "https://www.openjpeg.org"),
        ("OpenEXR", "2.3.0", "BSD-3-Clause", "3rdparty/openexr/LICENSE", "https://openexr.com"),
        ("cpufeatures", None, "BSD-2-Clause", "3rdparty/cpufeatures/LICENSE", "https://github.com/opencv/opencv/tree/4.x/3rdparty/cpufeatures"),
        ("Protocol Buffers (in OpenCV)", "3.19.1", "BSD-3-Clause", "3rdparty/protobuf/LICENSE", "https://protobuf.dev"),
        ("FlatBuffers (in OpenCV)", "23.5.9", "Apache-2.0", "3rdparty/flatbuffers/LICENSE.txt", "https://github.com/google/flatbuffers"),
        ("zlib (in OpenCV)", "1.3.0.1", "zlib", "3rdparty/zlib/LICENSE", "https://zlib.net"),
    ]:
        add(category, name, license, fetch(cv + path), version=version, url=url)
    # Carotene has no licence file of its own: its sources carry it.
    header = fetch(cv + "hal/carotene/src/add.cpp").split("*/")[0]
    add(category, "Carotene", "BSD-3-Clause", re.sub(r"^\s*/?\*+ ?", "", header, flags=re.MULTILINE),
        copyright="Copyright (C) 2014, NVIDIA Corporation", url="https://github.com/opencv/opencv/tree/4.12.0/hal/carotene")
    add(category, "KleidiCV", "Apache-2.0", APACHE, version="0.5.0", copyright="Arm Limited",
        url="https://gitlab.arm.com/kleidi/kleidicv")
    add(category, "Tesseract OCR", "Apache-2.0", fetch("https://raw.githubusercontent.com/tesseract-ocr/tesseract/5.5.0/LICENSE"),
        url="https://github.com/tesseract-ocr/tesseract")
    add(category, "Leptonica", "BSD-2-Clause", fetch("https://raw.githubusercontent.com/DanBloomberg/leptonica/1.85.0/leptonica-license.txt"),
        url="http://www.leptonica.org")


def main():
    global APACHE
    external = bazel_externals()
    APACHE = read(os.path.join(REPO, "LiteRT-LM", "LICENSE"))
    if "Apache License" not in APACHE:
        sys.exit("LiteRT-LM's LICENSE isn't the Apache License: check the submodule.")
    this_app()
    curated()
    native()
    opencv()
    dotnet_packages()
    data = json.dumps({"entries": entries, "texts": texts}, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.open(OUTPUT, "wb", 9) as f:
        f.write(data)
    print(f"{len(entries)} entries, {len(texts)} licence texts, {len(data) / 1024:.0f} KB -> "
          f"{os.path.getsize(OUTPUT) / 1024:.0f} KB {os.path.relpath(OUTPUT, DEMO)}")


if __name__ == "__main__":
    main()
