# Face landmarks and expressions (MediaPipe's face landmarker on LiteRT)

`Emgu.LiteRT.Models/FaceTracker.cs` runs MediaPipe's face detector, face landmark model and blendshape model through LiteRT's `CompiledModel`, in
C#, with no MediaPipe library. `Emgu.LiteRT.Models.CV`'s `FaceDetector` (with `FaceImageInput`) does it for Emgu CV images, and the demo has a
"Face" page (CPU or GPU) with a live camera. Ported 2026-10-23 and checked against MediaPipe's Python results on an Apple M1. Nothing here is a
visual-agent condition yet.

## What it does

For an image it returns up to as many faces as asked for (one by default): 478 landmarks - x and y as fractions of the image, z in the unit of x
scaled to the face's region (smaller is nearer) - with the last 10 the irises (468 and 473 their centres), a presence score, and 52 expression
scores from 0 to 1 named as ARKit's blendshapes (`FaceTracker.BlendshapeNames`: `mouthSmileLeft`, `eyeBlinkRight`, `jawOpen`, `browInnerUp`...;
`Face.Blendshape(name)`). `FaceTracker.Contours` are the outline, eyes, brows and lips to draw.

## Provenance and licence

- MediaPipe: https://github.com/google-ai-edge/mediapipe, Apache-2.0 (code). The pipeline's constants - the anchors, the decoding, the region,
  the 146 landmarks the blendshape model takes and the 52 names - are from its `face_detector_graph.cc`, `face_landmarks_detector_graph.cc` and
  `face_blendshapes_graph.cc`.
- The models are the three in Google's `face_landmarker.task` (float16, `.../face_landmarker/face_landmarker/float16/latest/`; 3,758,596 bytes,
  SHA-256 `64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff`): `face_detector.tflite` (230 KB, BlazeFace short range),
  `face_landmarks_detector.tflite` (2.5 MB, Face Mesh V2) and `face_blendshapes.tflite` (0.96 MB, Blendshape V2). A fourth file,
  `geometry_pipeline_metadata_landmarks.binarypb`, is for MediaPipe's head-pose matrix and isn't used. Like the pose bundles, it is downloaded from
  Google's storage and hash-checked (`FaceModelFiles`), not copied to Emgu's S3.
- **Licence (checked 2026-10-23): Apache-2.0**, from the three model cards ("MediaPipe BlazeFace Model Card (Short Range)", "Model Card MediaPipe
  Face Mesh V2", "Model Card Blendshape V2": "LICENSED UNDER Apache License, Version 2.0").
- **What the cards rule out**: the detector's, "Any form of surveillance or identity recognition", counting people in a crowd, faces looking away
  or more than 2 m away; Face Mesh's and Blendshape's, "human life-critical decisions" - their output "do[es] not provide facial recognition or
  identification" - and faces turned more than 80 degrees or less than half visible. They are made for selfie-style video, "AR entertainment".
  So expression triggers for fun (take a photo when you smile) are what this is for; **a drowsiness or attention alarm is not**, and nothing here
  identifies anyone.
- The sample photo (`Resources/Raw/face_portrait.png`, 256 x 256) is **synthetic**: no real person. It was drawn on 2026-10-23 by
  `Emgu.LiteRT.Models`' `ZImageTurbo` (Z-Image-Turbo, Apache-2.0) from the prompt "close-up studio portrait photo of a smiling woman looking at
  the camera, plain grey background", seed 22, 8 steps, and kept as PNG so the C# test and the Python reference decode the same pixels.
  MediaPipe's own test image `portrait.jpg` was used first, but it is a photograph of a real, well-known person, which a face-expression demo
  shouldn't open with.

## Model contract

| Model | Input | Outputs |
|---|---|---|
| `face_detector` | `[1,128,128,3]` float32 RGB, the image letterboxed, scaled to **-1..1**, the padding **0** (MediaPipe's `BORDER_ZERO` in the tensor's range: mid grey) | `[1,896,16]` regressors (x, y, w, h and six keypoints of x, y, relative to the anchor, in input pixels) and `[1,896,1]` score logits |
| `face_landmarks_detector` | `[1,256,256,3]` float32 RGB, 0..1, the face's rotated region warped to 256, its edges repeated (`BORDER_REPLICATE`) | `[1,1,1,1434]` = 478 x (x, y, z in crop pixels); `[1,1,1,1]` the face's presence as a **logit**; `[1,1]` a score MediaPipe doesn't use |
| `face_blendshapes` | `[1,146,2]` float32: 146 of the landmarks' x and y **in image pixels** | `[52]` scores, already 0..1 |

The landmark model's tensors have a **dynamic batch** (`[-1,256,256,3]`, and 221 shape signatures in all), which `CompiledModel` can't size (its
input buffer fails with "Failed to get num packed bytes"). `FaceModelFiles.WithStaticBatch` gives each shape vector of 2 to 5 elements that starts
with -1 a 1 there - the gesture classifier's patch (`HandModelFiles`), for longer shapes - on unpacking; the patched model's results equal the
original's in the interpreter bit for bit.

The pipeline (MediaPipe's, ported and checked in Python first):

1. Letterbox into 128 x 128, run the detector. 896 anchors: strides 8, 16, 16, 16 with two anchors per cell, i.e. 16x16x2 + 8x8x6; centres only
   (fixed sizes). Decode `xc = raw/128 + anchor`, sigmoid on the logits (clipped at 100), keep 0.5 and above, weighted NMS at IoU 0.3, remove the
   letterbox.
2. The region: the box scaled by 1.5 about its centre, turned so the line from keypoint 0 to keypoint 1 (the two eyes) is level.
3. Warp the region to 256 x 256, run the landmark model, keep the face if the sigmoid of the presence logit is at least 0.5. Landmarks through the
   region's rotation to the image (x by the region's width, y by its height, z by its width: MediaPipe's `LandmarkProjection`).
4. The 146 landmarks (`FaceTracker.BlendshapeLandmarks`) as image pixels into the blendshape model.

What a first look found: the detector's input is -1..1 (0..1 gave landmarks about 0.1-0.3 px further from MediaPipe's on average, and the
expressions up to twice as far); on the 90-degree copy of MediaPipe's own test portrait MediaPipe reported the same face twice, 4 px apart, where all
of its top detections overlap well above IoU 0.3 - the weighted NMS here merges them into one (the synthetic sample doesn't do this).

## Results

**Parity with MediaPipe** (Python `FaceLandmarker`, mediapipe 1.1.1, image mode, blendshapes on) on the sample face and five copies
(mirrored, turned 90 degrees, the face small, cropped, tilted 20 degrees; `tools/make_face_reference.py` makes `TestData/face_reference.json`),
the CPU and the Metal GPU the same to 0.01 px:

| Copy | Landmarks (mean / max, px of 256) | Expressions (max difference) |
|---|---:|---:|
| original | 0.23 / 0.61 | 0.039 |
| mirrored | 0.14 / 0.34 | 0.028 |
| turned 90 degrees | 0.78 / 1.52 | 0.037 |
| the face small | 0.35 / 0.67 | 0.046 |
| cropped | 0.13 / 0.43 | 0.020 |
| tilted 20 degrees | 0.26 / 0.51 | 0.028 |

With MediaPipe's own 820 x 1024 test portrait the upright copies were within 0.5-1 px on average and 1.3-3.9 px at most, expressions within
0.04-0.13, and the face shrunk to half was found by neither.

The tests (`UnitTestFaceTracker`, model-free: the anchors, decoding through the letterbox, the batch patch, the tables;
`UnitTestFaceTrackerNative`: the parity above with a 1 px mean, 4 px maximum and 0.1 expression limit, a smile on the sample, no face on the space
shuttle; the GPU case is explicit).

**Speed**, per model on an M1 (`CompiledModel`, 4 threads, the mean of 20 runs): detector 0.9 ms CPU / 0.7 ms Metal, landmarks 4.4 / 1.7 ms,
blendshapes 1.0 ms CPU (the Metal GPU can't compile it: a half/float mismatch in one of LiteRT's generated shaders, so it always runs on the CPU).
A whole frame on a Galaxy S10's CPU (the demo's live camera, 480 x 640): 8 ms, 30 fps; an 820 x 1024 photo 30 ms.

## Not done yet

- A `face` visual agent for the camera monitors (smiling, eyes closed, mouth open as object classes, like `PoseVisualAgent`'s postures).
- Tracking between frames (MediaPipe's video mode skips the detector using the previous frame's landmarks).
- The head-pose matrix (the geometry pipeline file).
