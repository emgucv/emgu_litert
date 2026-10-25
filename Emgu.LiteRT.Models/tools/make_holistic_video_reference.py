#!/usr/bin/env python3
"""MediaPipe's holistic landmarker in VIDEO mode on 20 frames made from the demo's sample person (a synthetic picture, HOLISTIC.md): the
reference for UnitTestHolisticTrackerNative's video test.

  python3 -m venv /tmp/mp-venv && /tmp/mp-venv/bin/pip install mediapipe==1.1.1 opencv-python-headless
  /tmp/mp-venv/bin/python Emgu.LiteRT.Models/tools/make_holistic_video_reference.py holistic_landmarker.task \\
      Emgu.LiteRT.Example/Maui/Resources/Raw/holistic_person.png Emgu.LiteRT.Test/Emgu.TF.Lite.Test/TestData/holistic_video_reference.json

The frames are exact in any OpenCV: the picture moved by whole pixels (edges repeated) and a fixed integer pattern of noise added, as the C# test's
HolisticVideoFrame (UnitTestHolisticTracker.cs) makes them; 30 frames a second. Pose landmarks keep their visibility; the face is every 8th
landmark. Landmarks are rounded to 5 decimals.
"""
import json
import math
import sys

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import BaseOptions, vision

FRAMES = 20


def frame(image, t):
    h, w = image.shape[:2]
    dx, dy = round(8 * math.sin(2 * math.pi * t / 20)), round(3 * math.sin(2 * math.pi * t / 15))
    moved = cv2.warpAffine(image, np.float32([[1, 0, dx], [0, 1, dy]]), (w, h), flags=cv2.INTER_NEAREST, borderMode=cv2.BORDER_REPLICATE)
    y, x, c = np.mgrid[0:h, 0:w, 0:3]
    noise = (x * 73 + y * 151 + c * 31 + t * 197) % 9 - 4
    return np.clip(moved.astype(np.int32) + noise, 0, 255).astype(np.uint8)


def points(landmarks, visibility=False, step=1):
    if visibility:
        return [[round(p.x, 5), round(p.y, 5), round(p.visibility, 4)] for p in landmarks[::step]]
    return [[round(p.x, 5), round(p.y, 5)] for p in landmarks[::step]]


def main(task, image_path, out_path):
    image = cv2.imread(image_path, cv2.IMREAD_COLOR)
    options = vision.HolisticLandmarkerOptions(base_options=BaseOptions(model_asset_path=task), running_mode=vision.RunningMode.VIDEO)
    frames = []
    with vision.HolisticLandmarker.create_from_options(options) as landmarker:
        for t in range(FRAMES):
            img = frame(image, t)
            found = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(img, cv2.COLOR_BGR2RGB)),
                                                t * 1000 // 30)
            frames.append({"pose": points(found.pose_landmarks, True), "leftHand": points(found.left_hand_landmarks),
                           "rightHand": points(found.right_hand_landmarks), "face": points(found.face_landmarks, step=8)})
    print(f"{FRAMES} frames: pose {sum(1 for f in frames if f['pose'])}, left hand {sum(1 for f in frames if f['leftHand'])}, "
          f"right hand {sum(1 for f in frames if f['rightHand'])}, face {sum(1 for f in frames if f['face'])}")
    with open(out_path, "w") as f:
        json.dump({"mediapipe": mp.__version__, "frames": frames}, f, separators=(",", ":"))


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
