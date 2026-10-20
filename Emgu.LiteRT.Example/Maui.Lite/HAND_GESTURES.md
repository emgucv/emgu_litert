# Hand gestures (MediaPipe's hand tracking on LiteRT)

A prototype of a hand-gesture detector for the visual agents: `Emgu.LiteRT.Models/HandTracker.cs` runs MediaPipe's palm detector, hand
landmark model, gesture embedder and gesture classifier through LiteRT's `CompiledModel`, in C#, with no MediaPipe library. The demo has a
"Hand gestures" photo page. Evaluated 2026-10-20 on an Apple M1 (Metal), a Galaxy S10 and a Pixel Fold.

## What it does

For an image it returns, for up to two hands: the palm score, 21 landmarks (x and y as fractions of the image, z), 21 world landmarks
(metres), "Left"/"Right", and one of MediaPipe's eight gestures: `Closed_Fist`, `Open_Palm`, `Pointing_Up`, `Thumb_Down`, `Thumb_Up`,
`Victory`, `ILoveYou`, `None`. A hand held sideways gives `None` (MediaPipe's classifier takes the hand as it is in the image).

## Provenance and licence

- MediaPipe: https://github.com/google-ai-edge/mediapipe, Apache-2.0 (code). The models are the ones in Google's
  `gesture_recognizer.task` (float16, version 1, unchanged since April 2023):
  `https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task`, 8,373,440 bytes,
  SHA-256 `97952348cf6a6a4915c2ea1496b4b37ebabc50cbbf80571435643c455f2b0482`. It is a zip with `hand_landmarker.task`
  (`hand_detector.tflite`, `hand_landmarks_detector.tflite`) and `hand_gesture_recognizer.task` (`gesture_embedder.tflite`,
  `canned_gesture_classifier.tflite`), each a zip too.
- **The model files' own licence terms haven't been confirmed.** Google's documentation pages say the page content is CC BY 4.0 and the code
  samples Apache-2.0; the model cards (linked from the MediaPipe docs) are the place to check. So the bundle is **not** copied to Emgu's S3:
  the demo and the test download it from Google's storage, check the hash and unpack it (`HandModelFiles`). Decide before hosting or bundling it.
- The sample photos (`Resources/Raw/hand_*.jpg`) are MediaPipe's test images (its Apache-2.0 repository's test data, from Google's
  `mediapipe-assets` storage), used as in MediaPipe's own tests.

## Model contract

| Model | Input | Outputs |
|---|---|---|
| `hand_detector` | float32 `[1,192,192,3]`, RGB 0..1, the image **letterboxed** (scaled to fit, centred, black padding) | regressors `[1,2016,18]` (cx, cy, w, h and 7 keypoints, in 192-pixel units from the anchor), logits `[1,2016,1]` |
| `hand_landmarks_detector` | float32 `[1,224,224,3]`, RGB 0..1, the **rotated square** around the hand | `Identity` landmarks `[1,63]` (x, y, z in 224-pixel units), `Identity_1` hand presence, `Identity_2` handedness (P(right)), `Identity_3` world landmarks `[1,63]` |
| `gesture_embedder` | `hand` `[1,21,3]`, `handedness` `[1,1]`, `world_hand` `[1,21,3]` | `[1,128]` |
| `canned_gesture_classifier` | `[-1,128]` (dynamic batch) | `[-1,8]` scores |

Pipeline (from MediaPipe's `hand_detector_graph`, `hand_landmarks_detector_graph` and `hand_gesture_recognizer_graph`, checked against its
Python Tasks API):

1. **Palms:** 2016 SSD anchors (strides 8, 16, 16, 16; layers of one stride share a grid, two anchors per cell; fixed size, so only the centres);
   sigmoid scores >= 0.5; boxes and keypoints = regressor / 192 + anchor centre; weighted NMS (IoU 0.3); the letterbox padding removed.
2. **Hand rect:** rotation = 90 degrees - atan2(-(dy), dx) of the wrist (keypoint 0) to the middle finger base (keypoint 2); the box centre shifted
   -0.5 box heights along the hand's axis; made square in pixels (the longer side) and scaled by 2.6.
3. **Landmarks:** the rotated rect warped to 224 x 224 (an affine warp; OpenCV needs its half-pixel offsets), the 63 values / 224, projected back
   through the rotation, x and y by the rect's width and height, z by the width; presence >= 0.5.
4. **Gesture:** the landmarks with the image's aspect ratio taken out (x and y scaled by size / the larger side around 0.5), moved so the wrist is
   the origin and divided by max(x extent, y extent) + 1e-5; the same for the world landmarks without the aspect step; handedness = P(right).
   **Not rotated:** MediaPipe feeds them in the image's orientation (rotating them by the hand's rect rotation made `Thumb_Up` come out as `None`).

Pitfalls found on the way:

- **The classifier's batch dimension is dynamic**, which `CompiledModel` can't run or resize (no buffer can be made for `-1`). `HandModelFiles`
  sets it to 1 in the unpacked file: each two-element int vector `[-1, n]` (the tensors' shape signatures) becomes `[1, n]` - five vectors; the
  results are identical (compared with the interpreter, difference 0.0).
- The detector wants 0..1, not -1..1 (-1..1 gave 31 palms at score 1.0 and no hand).
- The gesture embedder's `handedness` input is the probability of a *right* hand; all five test photos report "Right".
  A selfie camera shows a mirrored image - then left and right swap (the gesture isn't affected much, but the feature is).
- The z of landmarks and the world landmarks are as MediaPipe's: relative to the wrist.

## Results

**Parity** with MediaPipe's own Python Gesture Recognizer (1.1.1) on its five test images (`hand_reference.json`): the gesture and the handedness
are the same on all five - `Closed_Fist`, `Pointing_Up`, `None` (the sideways hand), `Thumb_Up`, `Victory` - scores within 0.06, and every landmark within
14 pixels of MediaPipe's (the mean offset is 0-2 pixels, scale 1.00, rotation 0 degrees; the largest single errors were 6-10 pixels on the wrist
and thumb: the crop is resampled slightly differently). An image without a hand (`space_shuttle.jpg`) has no hands. `UnitTestHandTracker` runs this
on the CPU, and (explicitly) on the Metal GPU, where both vision models compile fully and give the same results.

**Speed**, the vision models alone through `CompiledModel` (inference, float16 weights, median of 15, random input) on the M1:

| Model | CPU 2 / 4 threads | GPU |
|---|---|---|
| Palm detector 192 | 7.8 / 9.0 ms | **2.2 ms**, fully accelerated |
| Hand landmarks 224 | 6.0 / 5.0 ms | **2.0 ms**, fully accelerated |

**A whole frame** (`HandImageInput.Track`: resize and letterbox, detect, warp, landmarks, gesture; one hand in a ~350 x 350 photo), median of 20:

| Device | CPU 2 / 4 threads | GPU + CPU |
|---|---|---|
| Galaxy S10 | 28.3 / 19.3 ms | 27.6 ms |
| Pixel Fold | 21.0 / 15.9 ms (14.2 in a later run) | 26.3 ms (31.8) |

Both phones gave the right gesture for all five photos on the CPU and the GPU. **On the phones the GPU is not faster for a whole frame**:
the two models are small, the data goes to the GPU and back twice a frame, and the OpenCV preprocessing and the CPU-side gesture models are the
same. The page therefore defaults to the CPU (4 threads: 15-20 ms, 50-65 frames a second for one hand); the GPU is a choice, validated against
the CPU's result on a photo. (The speed check the other detectors use, which alternates CPU and GPU runs, made the CPU look 2-3 times slower than it
is alone - 50 ms against 14 ms on the Fold - so it isn't used here.)

## What is left for a visual agent

- The live camera path: `HandTracker.DetectPalms` on every frame is 8 ms on a CPU; MediaPipe's video mode tracks the hand from the previous frame's
  landmarks (the next rect comes from them) and runs the palm detector only when the hand is lost - about half the work. Not done.
- A `hand-gesture` visual agent next to `coco-ssd-mobilenet-v3`, `coco-yolox-nano` and the others (`VisualAgents.cs`, `CocoVisualAgent.cs`): its classes
  are the gestures, so the planner can create monitors such as "when I show a thumbs up, take a picture", with a hold time so a flicker isn't an event.
- More gestures (OK, wave, rock): from the 21 landmarks with rules, or MediaPipe's custom classifier training (`gesture_embedder` + a new classifier).
- The model licence question above, before the models are bundled or hosted.
