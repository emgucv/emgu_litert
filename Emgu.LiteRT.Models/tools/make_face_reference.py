#!/usr/bin/env python3
"""MediaPipe's face landmarker on the demo's sample face (a synthetic portrait, FACE.md) and five copies: the reference for
UnitTestFaceTrackerNative.

  python3 -m venv /tmp/mp-venv && /tmp/mp-venv/bin/pip install mediapipe==1.1.1 opencv-python-headless
  /tmp/mp-venv/bin/python Emgu.LiteRT.Models/tools/make_face_reference.py face_landmarker.task \\
      Emgu.LiteRT.Example/Maui/Resources/Raw/face_portrait.png Emgu.LiteRT.Test/Emgu.TF.Lite.Test/TestData/face_reference.json

face_landmarker.task is Google's bundle (FaceModelFiles.Url, SHA-256 64184E22...). The copies are made with the same OpenCV calls as the
C# test's FaceVariant (UnitTestFaceTracker.cs). Landmarks are rounded to 5 decimals, blendshape scores to 4.
"""
import json
import sys

import cv2
import mediapipe as mp
from mediapipe.tasks.python import BaseOptions, vision


def variants(image):
    h, w = image.shape[:2]
    grey = (128, 128, 128)
    far = cv2.copyMakeBorder(image, h // 2, h // 2, w // 2, w // 2, cv2.BORDER_CONSTANT, value=grey)
    tilt = cv2.warpAffine(image, cv2.getRotationMatrix2D((w / 2, h / 2), 20, 1.0), (w, h), flags=cv2.INTER_LINEAR,
                          borderMode=cv2.BORDER_CONSTANT, borderValue=grey)
    return {
        "original": image,
        "flip": cv2.flip(image, 1),
        "rot90": cv2.rotate(image, cv2.ROTATE_90_CLOCKWISE),
        "crop": image[h // 8: h - h // 8, w // 8: w - w // 8].copy(),
        "far": cv2.resize(far, (w, h), interpolation=cv2.INTER_LINEAR),
        "tilt": tilt,
    }


def main(task, image_path, out_path):
    image = cv2.imread(image_path, cv2.IMREAD_COLOR)
    options = vision.FaceLandmarkerOptions(base_options=BaseOptions(model_asset_path=task), output_face_blendshapes=True,
                                           num_faces=2, running_mode=vision.RunningMode.IMAGE)
    result = {"mediapipe": mp.__version__, "images": {}}
    with vision.FaceLandmarker.create_from_options(options) as landmarker:
        for name, img in variants(image).items():
            found = landmarker.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(img, cv2.COLOR_BGR2RGB)))
            faces = [{"landmarks": [[round(p.x, 5), round(p.y, 5), round(p.z, 5)] for p in marks],
                      "blendshapes": [round(c.score, 4) for c in found.face_blendshapes[i]]}
                     for i, marks in enumerate(found.face_landmarks)]
            result["images"][name] = {"width": img.shape[1], "height": img.shape[0], "faces": faces}
            print(f"{name:<9} {img.shape[1]}x{img.shape[0]}: {len(faces)} face(s)")
    with open(out_path, "w") as f:
        json.dump(result, f, separators=(",", ":"))


if __name__ == "__main__":
    main(*sys.argv[1:4])
