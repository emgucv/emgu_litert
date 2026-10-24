# Body pose (MediaPipe's pose landmarker on LiteRT)

A prototype of a body-pose detector for the visual agents (TODO item 51): `Emgu.LiteRT.Models/PoseTracker.cs` runs MediaPipe's pose
detector and pose landmark model (BlazePose) through LiteRT's `CompiledModel`, in C#, with no MediaPipe library. The demo has a "Body
pose" photo page (lite, full or heavy; CPU or GPU). Evaluated 2026-10-20 on a Galaxy S10; checked against MediaPipe's Python results
on an Apple M1. Nothing here is a visual-agent condition yet: this note is the foundation for one.

## What it does

For an image it returns, for the best person (one by default, up to as many as you ask for): 33 landmarks - x and y as fractions of the
image, z (relative to the hips, in the unit of x scaled to the region), a visibility score (not hidden) and a presence score (inside the
frame) - and 33 world landmarks in metres from the hips, turned upright. The names and the bones to draw are `PoseTracker.LandmarkNames` and
`Bones`: nose, eyes, ears, mouth, shoulders, elbows, wrists, pinky/index/thumb knuckles, hips, knees, ankles, heels, foot index.

## Provenance and licence

- MediaPipe: https://github.com/google-ai-edge/mediapipe, Apache-2.0 (code). The models are the two files in Google's
  `pose_landmarker_{lite,full,heavy}.task` (float16, `.../latest/...`; each a zip of `pose_detector.tflite` - 2.9 MB, the same in all three - and
  `pose_landmarks_detector.tflite`), with no metadata inside:

| Bundle | Bytes | SHA-256 |
|---|---:|---|
| `pose_landmarker_lite.task` | 5,777,746 | `59929e1d1ee95287735ddd833b19cf4ac46d29bc7afddbbf6753c459690d574a` |
| `pose_landmarker_full.task` | 9,398,198 | `4eaa5eb7a98365221087693fcc286334cf0858e2eb6e15b506aa4a7ecdcec4ad` |
| `pose_landmarker_heavy.task` | 30,664,242 | `64437af838a65d18e5ba7a0d39b465540069bc8aae8308de3e318aad31fcbc7b` |

- **Licence (checked 2026-10-20): Apache-2.0**, from the model card ("Model Card BlazePose GHUM 3D": "LICENSED UNDER Apache License, Version 2.0").
  The bundles are still downloaded from Google's storage and hash-checked, not copied to Emgu's S3 (`PoseModelFiles`); the licence would allow hosting them.
- The card's **out-of-scope** list matters for what we build on it: "Multiple people in an image", people further than about 4 m (14 feet), "Head is not
  visible", "Applications requiring metric accurate depth", and "Any form of surveillance or identity recognition is explicitly out of scope and
  not enabled by this technology". Its ethical notes: "not intended for human life-critical decisions. The primary intended application is
  entertainment", and it degrades with poor light, motion blur and occlusion. So: gesture-like triggers (a wave, arms raised, standing up) are
  what it is for; **a fall detector or any safety alarm is not** something to promise on it, and nothing here identifies anyone.
- The other MediaPipe models we might use, by their cards (2026-10-20): Face Mesh V2, Blendshape V2, Selfie segmentation, Multiclass segmentation and
  Hair segmentation all say Apache-2.0 and "not for human life-critical decisions"; Selfie and Multiclass segmentation also exclude surveillance and
  identity recognition, and Face Mesh and Blendshape V2 say instead that their output "do[es] not provide facial recognition or identification". The
  hand landmark card says Apache-2.0 too; the gesture classifier's card states no licence (HAND_GESTURES.md). The face detector's card ("MediaPipe BlazeFace Model Card (Short Range)", found 2026-10-23) says
  Apache-2.0 and excludes surveillance and identity recognition (FACE.md). No card was found for the image classifier/embedder or the object
  detector.
- The sample photo (`Resources/Raw/pose_standing.jpg`) is MediaPipe's test image `pose.jpg` (its Apache-2.0 repository's test data, from
  Google's `mediapipe-assets` storage).

## Model contract

| Model | Input | Outputs |
|---|---|---|
| `pose_detector` | `[1,224,224,3]` float32 RGB, the image letterboxed (black padding), scaled to **-1..1** | `[1,2254,12]` regressors (x, y, w, h and four keypoints of x, y, relative to the anchor, in input pixels) and `[1,2254,1]` score logits |
| `pose_landmarks_detector` | `[1,256,256,3]` float32 RGB, 0..1, the body's rotated square warped to 256 | `[1,195]` = 39 x (x, y, z in crop pixels, visibility and presence as **logits**); `[1,1]` presence (**already a probability**); `[1,256,256,1]` body mask; `[1,64,64,39]` heatmaps; `[1,117]` = 39 x (x, y, z) world landmarks |

The pipeline (MediaPipe's, ported and checked in Python first):

1. Letterbox into 224 x 224, run the detector. 2254 anchors: strides 8, 16, 32, 32, 32 with two anchors per cell, i.e. 28x28x2 + 14x14x2 + 7x7x6;
   centres only (fixed sizes). Decode `xc = raw/224 + anchor`, sigmoid on the logits, weighted NMS at IoU 0.3, remove the letterbox padding.
2. The region: centred on keypoint 0 (the middle of the hips), the box side is twice the distance from keypoint 0 to keypoint 1 (a point on the circle
   round the whole body) times 1.25, a square in pixels, rotated so the line from keypoint 0 to keypoint 1 points up (target angle 90 degrees).
3. Warp the region to 256 x 256, run the landmark model, keep the result if the presence output is at least 0.5.
4. Landmarks through the region's rotation to the image; z scaled by the region's width; world landmarks only rotated. The last 6 of the 39 are
   auxiliary (the last two are the next frame's alignment points, which would let a video mode skip the detector - not done).

What a first look found: the presence output is **not** a logit - run through a sigmoid it read exactly 0.731 for everything (the raw output was
0.9999 on a person, 0.03 on the space shuttle, 3e-6 on tulips).

## Results

**Parity with MediaPipe** (Python `PoseLandmarker`, mediapipe 1.1.1, image mode) on its `pose.jpg` (1000 x 667) and five copies - mirrored, turned 90
degrees, the person small (padded to double size and shrunk back), cropped, and tilted 20 degrees: the same person found every time, landmarks
within 3.6-7.6 px on average and 10-20 px at most (a 1000 px image; the crop is resampled slightly differently), visibility within 0.03 for lite
and full (heavy: up to 0.15), world landmarks within 0.07 m (z included), scale and centre the same (no offset); heavy was checked in Python too.
The tests (`UnitTestPoseTrackerNative`) cover lite and full, 6 images each, with a 30 px per-landmark and 12 px mean limit.

**Speed** (the whole frame: letterbox, detector, warp, landmark model, decode), CPU with 2 threads, median of 20 after warmup, the test photo:

| Device | Lite | Full |
|---|---:|---:|
| Galaxy S10 (CPU) | 26 ms | 36 ms |

The GPU doesn't compile either model fully on the S10's OpenCL (`IsFullyAccelerated` false), so the demo's "GPU" backend falls back to the CPU
there. Which operation stops it hasn't been looked for. Lite at 26 ms is about 38 frames a second - more than a camera monitor needs.
Not measured: the Pixel Fold, the M1 (the tests ran, without timing), the Metal GPU, the heavy model on a phone.

## The visual agent (TODO item 52)

`body-pose` (`PoseVisualAgent.cs`, `VisualHostCatalog.PoseDescriptor`; the geometry is `Emgu.LiteRT.Models/PoseGestures.cs`) makes six postures the
object classes of the camera monitors, the way `hand-gesture` does for hands, so "take a picture when you see me waving" is an ordinary monitor with
every action (chime, speech, snapshot, photo, clip). It runs the lite model on the CPU, analysing the camera's frames (about 5 a second) one person at a time.

| Class | Rule (pixels, camera upright, every landmark used visible at 0.5 or more) |
|---|---|
| `arms raised` | both wrists above their shoulders by more than 0.4 shoulder widths |
| `hand raised` | exactly one is |
| `waving` | a raised wrist swings sideways at least twice, each at least 0.25 shoulder widths, within 2 s (`PoseGestures.WaveDetector`) |
| `standing` | the spine within 35 degrees of upright and both knees more than 0.6 of the torso's length down the spine from the hips (ankles below the knees) |
| `sitting` | the same, both knees less than 0.3 of it down the spine (level with the hips) |
| `lying down` | the spine more than 60 degrees from upright (and then no raised hand) |

The score is the least visibility among the landmarks a reading used. Thighs are measured along the spine, not straight down the image, so a tilted
camera or a leaning body doesn't change them; and each leg on its own, so a lunge (the test photo: arms out sideways, one knee bent) is neither standing
nor sitting. Nothing maps to a fall ("falls", "fell", "collapse" are not classes): the card rules out life-critical use.

Gemma 4 E2B on a Galaxy S10 (GPU), the prompt and what it made: "Beep when you see me waving" - `waving` (it added a spoken "Beep" too, as it does for any
"beep"); "Beep when I stand up" - `standing`; "Say hello when someone raises their hand" - first the agent's own name as the label, then, with the
instructions changed to say raising a hand is not a hand gesture and the camera keywords widened, still `body-pose`; "Take a picture when the person is lying down" -
the `person` class. So the host now reads the request itself when the label is no posture and is a mistake - the person class, an agent's name, or
nothing that is a class of any agent (`VisualHostCatalog.PostureForMonitor` / `PoseFromRequest`: unmistakable phrases only, so "stands at the door" stays a
person and "the dog sits down" a dog); after that both gave `hand raised` and `lying down`. Frames fed from the test photo: the monitor for "lying
down" stayed quiet on an empty image and on the upright warrior pose, started on the photo turned a quarter and ended when it went away, taking its
snapshot. Not run: a real person on a live camera - so nothing says how well the rules hold up on real webcam views (legs cut off at the desk, a
person far away, a hand raised only to the shoulder) - and the wave detector has seen only made-up swings. The frames reach the agent at about 5 a second, so a
wave faster than about 2 Hz is sampled thinly; a faster feed for this agent would help.

## Counting jumping jacks

`JumpingJackCounter` counts jumping jacks frame by frame: closed (both wrists below their shoulders, feet at most 1.0 shoulder widths apart), open
(both wrists more than 0.4 shoulder widths above their shoulders - `PoseGestures`' arms raised - and feet at least 1.2 apart), closed again - it
counts when it closes. The feet are only checked when both ankles are visible, so a camera that sees the person from the waist up counts the arms;
frames in between change nothing, and losing the person drops the jumping jack under way but keeps the count. Checked on synthetic Z-Image pictures
(2026-10-24): a jumping jack read open (wrists 1.45 shoulder widths up, feet 5.5 apart), a man standing - cropped and enlarged, as the wide shot
left him too small to find - closed (wrists 1.4-1.5 down, feet 0.41), and the warrior pose (arms level) neither; Z-Image's "full body" prompts were
cut off at the chest. `UnitTestJumpingJacks` covers the rules on made-up landmarks.

`SquatCounter` and `HighKneesCounter` (`ExerciseCounters.cs`, with the `IExerciseCounter` all three implement) count the same way:
- Squats: the hip's height above the ankle over the knee's - about 2 standing, 1 with the thighs level, only heights, so the front and the side
  alike; standing at 1.75 or more, down at 1.45 or less (thighs ~30 degrees from level), counted on standing up; legs with hip, knee and ankle
  visible, averaged.
- High knees: each knee's drop below its hip in torso lengths (shoulders' middle to hips' middle) - about 0.8 standing; up at 0.2 or less and no more
  than 0.35 to the side of the hip, down at 0.5 or more; each leg counts on coming down. The sideways limit is needed: a jumping jack's spread legs
  bring the knees to 0.25-0.32 below the hips (0.62 to the side), the warrior pose's bent knee 0.14 (0.65 to the side), standing 0.77 (0.02).
- Checked only on those synthetic pictures (standing read 2.14 for squats, the jumping jack 2.15, the warrior pose 1.59 - in between) and on
  made-up landmarks in `UnitTestJumpingJacks`; no squat or high-knee picture, real person or Gemma 4 run yet.

In the demo's chat, Gemma 4 calls `count_exercise` (`Emgu.LiteRT.LM.Agents`' `ExerciseTools.cs`, in the goals tool group - a group of its own
added a stub to every message and went over the budget `UnitTestToolRouting` checks): the call lasts as long as the exercise, since the model is
far too slow to judge each movement. `LiteRtLmChatPage.Exercise.cs` opens the camera, runs its own pose detector (lite) on every frame it keeps up
with and shows a card with the count, the time and Done; it ends at the target or the time asked for, Done, 10 s without a jumping jack after the
first, 30 s without any, or 5 minutes. The count is then logged toward a goal that names jumping jacks or star jumps (`AgentTools.LogExerciseAsync`:
the count for "30 jumping jacks a day", 1 for "do jumping jacks every day", the minutes for "10 minutes of jumping jacks a day") and returned to
the model, told not to log it again. `GoalParser` reads "jumping jacks", "push ups" and hyphenated words such as "sit-ups" as one unit. Not yet
tried with a real person or with Gemma 4.

## What is left

- More conditions (hands on the head, leaning, crouching, a person turned away) and thresholds tuned on real people.
- Tracking: run the detector only when the last frame's landmarks are lost, using auxiliary landmarks 33 and 34 for the next region (as MediaPipe's
  video mode does) - it would roughly halve the time.
- The GPU: find the rejected operation; the Pixel Fold and the M1's numbers; real people in front of the camera (this was tested on one photo and
  copies of it, with no real person, and no frames from the phones' cameras were looked at).
