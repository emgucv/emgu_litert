#!/usr/bin/env python3
"""MediaPipe's holistic landmarker on the demo's sample person (a synthetic picture, HOLISTIC.md) and four copies: the reference for
UnitTestHolisticTrackerNative.

  python3 -m venv /tmp/mp-venv && /tmp/mp-venv/bin/pip install mediapipe==1.1.1 opencv-python-headless
  /tmp/mp-venv/bin/python Emgu.LiteRT.Models/tools/make_holistic_reference.py holistic_landmarker.task \\
      Emgu.LiteRT.Example/Maui/Resources/Raw/holistic_person.png Emgu.LiteRT.Test/Emgu.TF.Lite.Test/TestData/holistic_reference.json

holistic_landmarker.task is Google's bundle (HolisticModelFiles.Url, SHA-256 E2DAB611...). Each image gets a landmarker of its own: MediaPipe
keeps tracking state between detect() calls even in IMAGE mode, and HolisticTracker reads every frame on its own. The copies are made with the
same OpenCV calls as the C# test's HolisticVariant (UnitTestHolisticTracker.cs). Landmarks are rounded to 5 decimals, scores to 4.
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
        "big": cv2.resize(image, (w * 2, h * 2), interpolation=cv2.INTER_LINEAR),
        "far": cv2.resize(far, (w, h), interpolation=cv2.INTER_LINEAR),
        "tilt": tilt,
    }


def points(landmarks, visibility=False):
    if visibility:
        return [[round(p.x, 5), round(p.y, 5), round(p.z, 5), round(p.visibility, 4)] for p in landmarks]
    return [[round(p.x, 5), round(p.y, 5), round(p.z, 5)] for p in landmarks]


def main(task, image_path, out_path):
    image = cv2.imread(image_path, cv2.IMREAD_COLOR)
    result = {"mediapipe": mp.__version__, "images": {}}
    for name, img in variants(image).items():
        options = vision.HolisticLandmarkerOptions(base_options=BaseOptions(model_asset_path=task), output_face_blendshapes=True,
                                                   running_mode=vision.RunningMode.IMAGE)
        with vision.HolisticLandmarker.create_from_options(options) as landmarker:
            found = landmarker.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(img, cv2.COLOR_BGR2RGB)))
        entry = {"width": img.shape[1], "height": img.shape[0],
                 "pose": points(found.pose_landmarks, True), "poseWorld": points(found.pose_world_landmarks),
                 "leftHand": points(found.left_hand_landmarks), "leftHandWorld": points(found.left_hand_world_landmarks),
                 "rightHand": points(found.right_hand_landmarks), "rightHandWorld": points(found.right_hand_world_landmarks),
                 "face": points(found.face_landmarks), "blendshapes": [round(c.score, 4) for c in found.face_blendshapes]}
        result["images"][name] = entry
        print(f"{name:<9} {img.shape[1]}x{img.shape[0]}: pose {len(entry['pose'])}, left hand {len(entry['leftHand'])}, "
              f"right hand {len(entry['rightHand'])}, face {len(entry['face'])}, blendshapes {len(entry['blendshapes'])}")
    with open(out_path, "w") as f:
        json.dump(result, f, separators=(",", ":"))


if __name__ == "__main__":
    main(*sys.argv[1:4])
