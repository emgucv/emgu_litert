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

`HolisticDetector.Detect(Mat)` reads a picture on its own, as MediaPipe does a first image (MediaPipe carries state between calls even in image
mode, so the reference script makes a new landmarker for each picture). `Detect(Mat, TimeSpan)` reads frames of a video as MediaPipe's video mode
does: the body tracked from the last frame instead of the detector, each hand and the face kept in their last region while they move little, the
body smoothed ([Video mode](#video-mode-tracking-and-smoothing)). One person only, as in MediaPipe.

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

## Video mode: tracking and smoothing

`HolisticDetector.Detect(Mat, TimeSpan)` (with `HolisticImageInput`'s `HolisticTracking`, the static helpers in `HolisticTracker` and
`LandmarkSmoother`) ports `holistic_pose_tracking.cc`, `holistic_hand_tracking.cc` and `holistic_face_tracking.cc`:

- **Body:** after the first detection the pose detector is skipped. The next frame's region comes from the first two of the landmark model's six
  auxiliary landmarks (the hip centre and a point on the circle round the body), smoothed by a one-euro filter (min cutoff 0.01, beta 10), as
  `PoseTracker.RectOf` makes it from a detection. Only a frame without a body (the landmark model's presence under 0.5) sends the next frame
  back to the detector. The 33 landmarks are smoothed by one-euro filters in pixels (min cutoff 0.05, beta 80, derivative cutoff 1; speeds
  divided by the body's size, twice the distance between those two auxiliary landmarks), their visibility by a low-pass filter (0.1 of each new
  value), and the world landmarks by one-euro filters in metres (0.1, 40). The hands and face take their regions from the smoothed landmarks.
- **Each hand:** the region from the pose and the re-crop model as for a picture (the re-crop model still runs), then MediaPipe's
  `RoiTrackingCalculator`: the region of the hand's last landmarks is kept instead unless the two differ by more than 40 degrees, 0.2 of the size
  in position or 0.4 in size, or a last landmark is outside the re-crop region shrunk by 10%. The region of the last landmarks is the box of the
  12 palm landmarks (the wrist, the thumb's first three, each finger's first two) turned so the wrist is below the index, middle and ring
  knuckles, x 2, shifted by -0.1, square. The holistic source passes `HandLandmarksToRectCalculator` all 21 landmarks; with all 21 our hands were
  1.2-2 px from MediaPipe's on the test videos, with the palm's 12 they are its to 0.00 px (median) - MediaPipe 1.1.1 evidently uses the palm.
- **Face:** the detector still runs in the region from the pose; its region is kept as the box of the last 478 landmarks, level with landmarks 33
  and 263 (the outer eye corners), x 1.5 (not square), unless it differs by more than 15 degrees, 0.1 in position or 0.3 in size, or a last
  landmark is outside the detector's region shrunk by 20%.
- Nothing else carries over: a part not found in a frame starts again in the next, as MediaPipe's loopbacks hold only the previous frame. Ours
  also starts again when a timestamp goes back or comes more than a second after the last (`HolisticDetector.TrackingGap`; MediaPipe carries any
  gap), and the demo page's photos and the parity tests use the single-picture mode.

**Parity with MediaPipe's video mode** (mediapipe 1.1.1, `detect_for_video` at 30 frames a second) on four 3 s synthetic videos made from the test
pictures - the picture swaying side to side (2 s, 6% of its width), up and down, zooming 5% and turning 3 degrees, with noise (sigma 4). Median
error per frame, and jitter: the mean size of each landmark's frame-to-frame second difference, |p(t+1) - 2 p(t) + p(t-1)|, which on slow,
smooth motion is mostly noise. Pixels of the 256 px frames:

| Video | Body: error, jitter ours / MediaPipe | Hands: error, jitter ours / MediaPipe | Face: error, jitter |
|---|---|---|---|
| sample person | 0.91, 1.16 / 1.11 | 0.01-0.02, 0.53 / 0.53 and 0.43 / 0.43 | 0.32, 0.26 / 0.27 |
| covering the eyes | 0.92, 1.37 / 1.47 | 0.02-0.03, 0.67 / 0.66 and 0.64 / 0.58 | (no face, as MediaPipe) |
| hand over the mouth | 0.77, 1.99 / 1.89 | 0.03, 0.76 / 0.75 | 0.29, 0.54 / 0.50 |
| hand raised, smiling | 2.10, 9.22 / 9.00 | 0.01-0.10, 0.35 / 0.35 | 0.45, 0.34 / 0.34 |

The same frames read one by one (image mode) jitter far more: the body 4.3-13 px, a hand 0.7-7 px. A raised hand that the single pictures found in
39 of 90 frames (MediaPipe: 33) was found in 89 tracked (MediaPipe: 89). The body was detected once per video; the hands kept their region in 97-178
of 180 hand-frames and the face in 89 of 90.

**Speed** on the M1 (CPU, 2 threads, those videos): 22-23 ms a frame tracked against 27-29 ms one by one - the pose detector is skipped; the hands'
re-crop model and the face detector still run, as in MediaPipe. The phones weren't measured.

**One thing tracking makes worse, in MediaPipe too:** in the raised-hand video both MediaPipe's video mode and ours move the lowered arm's wrist up by
the face after about two seconds (from 26 px below the shoulder to 140 px above), and keep it there, since the detector never runs again while the
body is tracked; read one by one, the wrist stays below the shoulder. So "arms raised" was read in 29 of those 90 frames where one hand was up, and
the sway's turning made the raised hand read as waving in 15 more. On the covered-eyes video tracking helped: a stray "hand over mouth" in 1 frame
against 13. The agent and the live page use the video mode.

**Tests**: `UnitTestHolisticTracker` (model-free: the one-euro filter against a hand calculation, the smoother, the palm region and its turn, the
face region from landmarks, the keep rules) and `UnitTestHolisticTrackerNative.TheVideoModeMatchesMediaPipesOnAMovingPerson` (MediaPipe's video
mode on 20 frames of the sample moved by whole pixels with fixed noise - `tools/make_holistic_video_reference.py` makes
`TestData/holistic_video_reference.json`, and the test makes the same frames: the detector runs only on the first, both hands and the face keep
their region from then on, and the hands are within 0.1 px of MediaPipe's on average once tracked - 0.04-0.07 measured; the body within 1.5 / 3.5
px, the face 0.5 / 1.5). `TheDetectorFindsTheBodyHandsAndFace` checks a gap of more than a second, and a timestamp going back, start again.

## The visual agent: two things at once, hands on the face

The demo's `holistic` visual agent (`Chat/Visual/HolisticVisualAgent.cs`, CPU, 2 threads) makes what the body, hands and face show together classes of
the camera monitors (`HolisticGestures`, in this library):

- **Combinations**: every posture of the body-pose agent with every expression of the face-expression agent, posture first - "hand raised and smiling",
  "waving and smiling", "arms raised and mouth open"... (30 classes; the score is the lower of the two). So "take a picture when I raise my hand and smile"
  is an ordinary monitor. Gestures (thumb up...) aren't combined: the holistic bundle has no gesture classifier.
- **Interactions**: `covering eyes` (peekaboo), `hand over mouth`, `hand on face`.

The plain postures and expressions stay with the body-pose (26 ms a frame on a Galaxy S10) and face-expression (8 ms) agents, registered before this one
(50-72 ms), so a monitor for "smiling" still uses the face agent.

**The interactions** are geometry in eye distances (the pose's eyes, 2 and 5): both eyes within 0.25 of a bone of a hand (the segments between its
landmarks), the middle of the mouth within 0.18, or a hand's deepest landmark at least 0.05 inside the outline of the pose's head landmarks (0-10);
each score is 0.5 at its threshold. The pose's head landmarks are used, not the face's, because the face model found no face in any of the synthetic
pictures of covered eyes or two of three of covered mouths - the pose keeps its head. Distances to the hand's bones, not to its outline: a hand
cradling the chin has the mouth inside its outline, between thumb and fingers. Measured on synthetic Z-Image pictures (2026-10-24, three seeds each:
"a person covering both eyes with their hands", "...covering their mouth with one hand", "...resting their cheek on one hand", "a smiling person
raising one hand high above their head"), in MediaPipe's landmarks:

| Pictures | Eyes to the nearest bone | Mouth to the nearest bone | Hand inside the head outline |
|---|---|---|---|
| covering the eyes | 0.01-0.13 (both eyes) | 0.10-0.37 | 0.16-0.41 |
| covering the mouth | 0.55-1.07 | 0.02-0.13 | 0.43-0.50 |
| the sample: hands raised beside the face | 0.58-1.51 | 0.83-1.33 | 0.20-0.96 outside |
| cheek on a hand | one eye 0.03 (the other 0.42); 0.54; - | 0.26; 0.84; - | 0.35; 0.13 outside; no hand found |

Through this tracker (`UnitTestHolisticVisualAgentNative`, on JPEG copies in `TestData/holistic_*.jpg` and the sample): covering the eyes reads
`covering eyes` 1.00 and `hand on face` 0.84; the covered mouth `hand over mouth` 0.93 and `hand on face` 1.00; the raised hand with a smile `hand raised
and smiling` 0.73; the sample none of them. Of all twelve pictures: two of three covered-eyes pictures read `covering eyes` - in the third only one hand
was found -, all three covered mouths `hand over mouth` (0.60-0.99), all three raised hands with a smile their combination (0.69-0.87). What doesn't
work: **a hand resting at the side of the jaw** is outside the head outline (`hand on face` 0.32; the face model's outline would separate it from the
sample by only 0.20 against 0.12); **a hand cradling the chin**, its fingers by the mouth, can read as `hand over mouth` (0.68 on the JPEG copy) and was
found or not depending on JPEG or PNG; **both hands under the chin** weren't found at all. Hands on the face are where hand finding is least stable.

**Routing**: Gemma 4 E2B (Metal GPU, on an M1, with the demo's camera instructions) named the class for "Beep when I wave and smile at the camera"
(`waving and smiling`), but gave `person` for "Take a picture when I raise my hand and smile", `baby` for "Play a chime when the baby plays peekaboo",
`hand_gesture` for "Take a photo when I cover my mouth with my hand" and `face-expression` for "Say stop when I touch my face". So, as for the pose and
face agents, the host reads the request when the label is half of a combination or a mistake (`VisualHostCatalog.HolisticForMonitor`: a combination or
interaction the label names; else a posture and an expression the request names in so many words, when the label is one of them or a mistake; else an
interaction the request names), checked before the face and pose fallbacks, which would take only half of a combination. With it all five became the
right holistic class, and "Take a picture when I smile" / "Beep when I raise my hand" stayed `smiling` (face-expression) and `hand raised` (body-pose).
The planner's instructions moved to `Maui.Demo.Lite.Core` (`VisualHostCatalog.PlannerInstructions`) so the model check uses the app's text.

**Tests**: `UnitTestHolisticVisualAgent` (model-free: the hull and distances, the interactions and a combination on made-up landmarks, the classes,
the words, the requests, the routing and agent selection, the observations) and `UnitTestHolisticVisualAgentNative` (the synthetic pictures above).

## Not done yet

- Real people: everything here was checked on synthetic pictures, their copies and videos made from them - the live camera and the agent haven't had
  a person in front of them, on the Mac or a phone.
- The video mode's speed on the phones, and a way out of a wrong guess it keeps (such as re-running the detector now and then, which MediaPipe
  doesn't do).
