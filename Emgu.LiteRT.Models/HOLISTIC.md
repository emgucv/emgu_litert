# Holistic landmarks (MediaPipe's holistic landmarker on LiteRT)

`Emgu.LiteRT.Models/HolisticTracker.cs` runs the seven models of MediaPipe's holistic landmarker through LiteRT's `CompiledModel`, in C#, with
no MediaPipe library: one person's body, both hands and face in a single call. `Emgu.LiteRT.Models.CV`'s `HolisticDetector` (with
`HolisticImageInput`) does it for Emgu CV images, and the demo has a "Holistic" page (photo or live camera; CPU, or the GPU for the hands and
face). Ported 2026-10-24, checked against MediaPipe's Python results on an Apple M1 and measured on two phones.

## What it does

For an image it returns a `HolisticResult`, or null when no body is found:

- `Pose`: 33 landmarks (x, y as fractions of the image, z, visibility, presence) and world landmarks in metres, as `PoseTracker` gives them.
- `LeftHand` / `RightHand` (each may be null): 21 landmarks and world landmarks, as `HandTracker` gives them. Left and right are **the person's own**
  hands, taken from the pose (MediaPipe's holistic graph doesn't use the hand model's handedness output); `Hand.Handedness` says the same. A hand's
  world landmarks are moved so its wrist is the pose's world wrist, so the hands and the body share one coordinate frame. There is no gesture.
- `Face` (may be null): 478 landmarks and 52 expression scores, as `FaceTracker` gives them.

It reads every frame on its own: no tracking or smoothing between frames. MediaPipe carries state between calls even in image mode (the
reference script therefore makes a new landmarker for each picture), and its video mode skips the detectors using the previous frame's landmarks;
both are left for later. One person only, as in MediaPipe.

## Provenance and licence

- MediaPipe: https://github.com/google-ai-edge/mediapipe, Apache-2.0 (code). The pipeline is from `tasks/cc/vision/holistic_landmarker/`
  (`holistic_pose_tracking.cc`, `holistic_hand_tracking.cc`, `holistic_face_tracking.cc`) and the calculators they use, read at commit 32cc4544.
- The models are the seven in Google's `holistic_landmarker.task` (float16, version 1,
  `.../holistic_landmarker/holistic_landmarker/float16/1/holistic_landmarker.task`; 13,683,609 bytes, SHA-256
  `e2dab61191e2dcd0a15f943d8e3ed1dce13c82dfa597b9dd39f562975a50c3f8`; the models are dated 2023-10-24): `pose_detector` (2.9 MB),
  `pose_landmarks_detector` (2.8 MB, BlazePose lite), `hand_roi_refinement` (88 KB), `hand_landmarks_detector` (5.5 MB), `face_detector` (230 KB,
  BlazeFace short range), `face_landmarks_detector` (1.2 MB, Face Mesh V2 at **192** px, not the face landmarker's 256) and `face_blendshapes`
  (0.96 MB, the face landmarker's own). Downloaded from Google's storage and hash-checked (`HolisticModelFiles`), not copied to Emgu's S3. Every
  model has static shapes, so nothing is patched.
- **Licence (checked 2026-10-24): Apache-2.0**, from the BlazePose, hand tracking and Face Mesh V2 model cards (the face detector's and
  blendshapes' cards are in [FACE.md](FACE.md)). The hand re-crop model has no card of its own.
- **What the cards rule out**: surveillance and identity recognition, human-life-critical decisions; BlazePose covers one person within ~4 m with
  the head in view. As with the face and pose trackers, nothing here identifies anyone.
- The sample (`Emgu.LiteRT.Example/Maui/Resources/Raw/holistic_person.png`, 256 x 256) is **synthetic**: no real person. It was drawn on
  2026-10-24 by `Emgu.LiteRT.Models`' `ZImageTurbo` from the prompt "waist-up studio photo of a smiling man raising both open hands beside his
  face, plain grey background", seed 2, 8 steps, and kept as PNG so the C# test and the Python reference decode the same pixels. Of seeds 1-3
  MediaPipe found the body, both hands and the face in all three; seed 2 was picked. MediaPipe's pose test photo (a real person) isn't used.

## Model contract

| Model | Input | Outputs |
|---|---|---|
| `pose_detector`, `pose_landmarks_detector` | as in [POSE.md](POSE.md) (224 px letterboxed, -1..1; 256 px region, 0..1) | as in POSE.md |
| `hand_roi_refinement` | `[1,256,256,3]` float32 RGB, 0..1, the hand's region from the pose, edges repeated | `[1,1,1,4]`: two points (x0, y0, x1, y1) in crop pixels |
| `hand_landmarks_detector` | `[1,224,224,3]` float32 RGB, 0..1, edges repeated | landmarks `[1,63]` in crop pixels, presence `[1,1]` (a probability), handedness `[1,1]` (unused), world landmarks `[1,63]` |
| `face_detector` | `[1,128,128,3]` float32 RGB, **-1..1**, the face's region from the pose, **black** outside the image (-1) | `[1,896,16]` regressors, `[1,896,1]` logits |
| `face_landmarks_detector` | `[1,192,192,3]` float32 RGB, 0..1, edges repeated | `[1434]` = 478 x (x, y, z in crop pixels), presence as a **logit** |
| `face_blendshapes` | `[1,146,2]` landmarks in image pixels | `[52]` scores |

## The pipeline

1. **Pose:** `PoseTracker`'s detector and landmark model, with MediaPipe's heatmap refinement (`PoseTracker.RefineWithHeatmap`: each landmark
   moved to the confidence-weighted centre of the 7 x 7 heatmap cells around it, if the highest is at least 0.5).
2. **Each hand**, if the pose's wrist visibility is above 0.1: a region from the wrist, pinky (17/18) and index (19/20) pose landmarks - centred
   at (2 x index + pinky) / 3, twice its distance from the wrist across, turned so the wrist is below, then x 2.7, shifted 0.1 of its size away
   from the wrist and made square. The re-crop model reads that region at 256 px and returns two points; the hand's region is centred on the
   first, twice its distance to the second across, turned so the second is below, shifted by -0.1 and made square. The landmark model reads it at
   224 px; the hand is kept if its presence is above 0.5.
3. **Face:** a region from pose landmarks 0-10 (their box, level with the eyes, square, x 3); BlazeFace finds the face in it (128 px, the best
   detection, IoU 0.3); the face's region is the box of its six keypoints, level with the eyes, square, x 2. The 192 px landmark model reads it
   (kept if the sigmoid of the presence is above 0.5; z scaled by the region's width in the image), then the blendshape model.

All regions follow MediaPipe's `RectTransformationCalculator` (shift along the region's own axes, then square on the longer side in pixels, then
scale): `HolisticTracker.Transform`.

## Two mistakes this found in the other trackers

The first comparison put the pose 12 px (of 256) away from MediaPipe's, while the hands and face were within 0.5 px. MediaPipe's own pose
landmarker gave exactly the holistic pose on this picture, so the error was in `PoseImageInput`, which the pose and holistic trackers share:

- **The border.** MediaPipe's image preprocessing leaves the border mode at `ImageToTensorCalculator`'s default, `BORDER_REPLICATE`, for every
  landmark model; `PoseImageInput` and `HandImageInput` padded the region with black. On MediaPipe's own pose photo the body's region stays mostly
  inside the image, so it hardly showed; on a waist-up picture the region reaches far below the image. 12 -> 3 px.
- **The corners.** MediaPipe's OpenCV converter (`image_to_tensor_converter_opencv.cc`) maps the region's corners straight to the input's corners,
  0 and the input's size; the trackers had moved both by half a pixel ("OpenCV's pixel centres"), which scales the crop slightly about a
  different point. 3 -> 1 px.

Both are fixed in all four `*ImageInput` crops (pose, hand, face, holistic), and `PoseImageInput.Track` now refines with the heatmap as MediaPipe
does. The existing parity tests before and after (the pose's "before" is POSE.md's figure, without the heatmap refinement; the
holistic row's is with the border already fixed):

| Test | Before (mean landmark error) | After |
|---|---|---|
| pose, MediaPipe's 1000 px photo and five copies | 3.6-7.6 px | lite 1.4-3.0 px, full 1.0-2.1 px |
| face, the 256 px sample and five copies | 0.13-0.78 px | 0.13-0.42 px |
| hand, MediaPipe's five photos | 1.5-5.7 px | 1.5-5.6 px |
| holistic hands / face | 0.3-0.7 / 0.1-0.6 px | 0.1-0.4 / 0.05-0.13 px |

(The hand landmarker's remaining offset comes from elsewhere - its palm detector or region - and wasn't pursued.)

## Results

**Parity with MediaPipe** (Python `HolisticLandmarker`, mediapipe 1.1.1, image mode, blendshapes on, a new landmarker per picture) on the sample
and four copies - mirrored, doubled in size, the person small (padded to double size and shrunk back), tilted 20 degrees;
`tools/make_holistic_reference.py` makes `TestData/holistic_reference.json`. Errors in pixels of the 256 px picture; the pose maximum counts the
landmarks MediaPipe sees (visibility 0.5 or more), since it guesses the feet of a waist-up picture far outside it, where they move 5 px with any
change of the crop:

| Copy | Pose (mean / max) | Left hand | Right hand | Face | Expressions (max difference) |
|---|---:|---:|---:|---:|---:|
| original | 1.02 / 0.84 | 0.27 / 0.70 | 0.29 / 0.62 | 0.08 / 0.23 | 0.030 |
| mirrored | 0.26 / 0.29 | 0.11 / 0.20 | 0.31 / 0.65 | 0.07 / 0.25 | 0.059 |
| doubled | 1.03 / 1.88 | 0.36 / 0.85 | 0.28 / 0.96 | 0.13 / 0.35 | 0.031 |
| small | 0.24 / 0.37 | 0.13 / 0.35 | 0.16 / 0.32 | 0.05 / 0.15 | 0.049 |
| tilted | 0.88 / 2.89 | 0.30 / 0.67 | 0.18 / 0.35 | 0.13 / 0.27 | 0.035 |

World landmarks: the pose's shape within 1-6 mm on average and its position within 1-9 mm; the hands' shape within 0.5-6 mm, their position within
2-15 mm (they carry the pose wrist's own error). Handedness as MediaPipe's everywhere.

**Speed**, the whole frame (the warps, the pose, both hands with their re-crops, the face and its expressions), the sample picture, median of 20
after a first run (the demo's first photo takes 2-3 times as long). The hands cost the most: two re-crops and two landmark models.

| Device | Backend, threads | Frame | Body / hands / face |
|---|---|---:|---|
| M1 (Mac Catalyst) | CPU, 2 | 30.5 ms | 12 / 15 / 3.5 ms |
| M1 (Mac Catalyst) | GPU for hands and face (Metal), 2 | 24 ms | 12 / 9 / 3 ms |
| Galaxy S10 | CPU, 2 | 72 ms | 30 / 33 / 9 ms |
| Galaxy S10 | CPU, 4 | 50-59 ms | 21-25 / 22-25 / 6-8 ms |
| Pixel Fold | CPU, 2 | 53-62 ms | 20-25 / 24-28 / 6-9 ms |
| Pixel Fold | CPU, 4 | 67-90 ms | 38-45 / 18-39 / 9-10 ms |

On the Pixel Fold 4 threads are slower than 2 (the pose model most of all). A 480 x 480 frame costs about the same as the 256 px sample (the
models' inputs are fixed; only the warps grow).

**The GPU**: the pose models don't compile for it (`UnitTestPoseTrackerNative`'s GPU case fails with `ErrorCompilation` on the Metal GPU, and
POSE.md found the same on the S10's OpenCL), so `HolisticTracker` always runs them on the CPU, and the blendshape model too; `accelerators` applies
to the hand and face models. These compile fully for the M1's Metal GPU and give the CPU's landmarks (the parity test's GPU case: the same
figures to 0.01 px), and the frame drops from 30.5 to 23-24 ms. On the phones the GPU (OpenCL) is slower: Galaxy S10 78 vs 77 ms (2 threads), 64
vs 55 ms (4); Pixel Fold 126 vs 61 ms, 149 vs 95 ms. So `HolisticDetector` with `preferGpu` uses the GPU only if it compiles fully, matches the
CPU's hand and face landmarks within 1% of the picture and is faster on the bundled sample (the median of five frames each); otherwise it
says why in `GpuFailure` and uses the CPU - verified: the Mac Catalyst app chose the GPU, both phones the CPU.

**The demo page** (`Vision/HolisticPage.cs`): the body (without its head and finger points where the face and hand models found them), the hand
skeletons, the face's outline, a row with each part's time, which hands were found and the face's three strongest expressions. Checked on the
Pixel Fold with the sample; the live camera wasn't tried with a person in front of it.

**Tests**: `UnitTestHolisticTracker` (model-free: the region transform, the hand regions from the pose and from the re-crop points, the face
region, the heatmap refinement) and `UnitTestHolisticTrackerNative` (the parity above with limits of 1.5 / 3 px for the pose, 0.75 / 2 px for the
hands, 0.5 / 1.5 px for the face, 0.1 for the expressions, 10 mm for world shapes - on the CPU, and explicitly with the hands and face on the GPU;
no one on the space shuttle; the seven models unpacked; `HolisticDetector` downloading and running them).

## Not done yet

- A visual agent for the camera monitors (stage 3).
- Tracking between frames (MediaPipe's video mode), and smoothing.
- Real people: this was checked on one synthetic picture and its copies.
- A real person in front of the live camera.
