// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>One person's body, hands and face, as MediaPipe's holistic landmarker finds them.</summary>
    public sealed class HolisticResult
    {
        /// <summary>A result</summary>
        /// <param name="pose">The body</param>
        /// <param name="leftHand">The person's left hand, or null</param>
        /// <param name="rightHand">The person's right hand, or null</param>
        /// <param name="face">The face, or null</param>
        public HolisticResult(Pose pose, Hand leftHand, Hand rightHand, Face face)
        {
            Pose = pose;
            LeftHand = leftHand;
            RightHand = rightHand;
            Face = face;
        }

        /// <summary>The body: 33 landmarks and world landmarks</summary>
        public Pose Pose { get; }
        /// <summary>The person's left hand (on the right of the image when they face the camera), or null</summary>
        public Hand LeftHand { get; }
        /// <summary>The person's right hand, or null</summary>
        public Hand RightHand { get; }
        /// <summary>The face: 478 landmarks and, with the blendshape model, 52 expression scores; or null</summary>
        public Face Face { get; }
    }

    /// <summary>
    /// MediaPipe's holistic landmarker on LiteRT's CompiledModel (HOLISTIC.md): one person's pose, then each hand and the face from the pose
    /// instead of their own detectors. The pose detector and the lite landmark model find the body (as <see cref="PoseTracker"/>, with the
    /// heatmap refinement); each hand's region comes from the pose's wrist, pinky and index finger, is corrected by the hand re-crop model and
    /// read by the hand landmark model; the face's region comes from the pose's face points, BlazeFace finds the face in it, and Face Mesh V2
    /// (192 px) reads it, then the blendshape model. The host fills each input (images are cut and scaled with OpenCV or the platform, not here)
    /// in the order of the steps and serializes calls; <c>Emgu.LiteRT.Models.CV</c>'s <c>HolisticDetector</c> does it for Emgu CV images. Every
    /// call reads one image on its own, which is what MediaPipe's landmarker does for the first image: MediaPipe carries tracking and smoothing
    /// from one call to the next, even for still images, and this doesn't (yet).
    /// </summary>
    public sealed class HolisticTracker : IDisposable
    {
        /// <summary>The hand re-crop model's square input size</summary>
        public const int RefinementSize = 256;
        /// <summary>The hand landmark model's square input size</summary>
        public const int HandLandmarkSize = 224;
        /// <summary>The face detector's square input size</summary>
        public const int FaceDetectorSize = 128;
        /// <summary>The face landmark model's square input size (Face Mesh V2 at 192 px; the face landmarker's is 256)</summary>
        public const int FaceLandmarkSize = 192;

        /// <summary>The pose landmarks a hand's region comes from (wrist, pinky, index finger): the person's left hand, and right</summary>
        public static readonly int[] LeftHandPose = { 15, 17, 19 }, RightHandPose = { 16, 18, 20 };

        // MediaPipe's holistic_hand_tracking.cc and holistic_face_tracking.cc.
        private const float MinWristVisibility = 0.1f;
        private const float HandFromPoseScale = 2.7f, HandShiftY = -0.1f;
        private const float FaceFromPoseScale = 3.0f, FaceFromDetectionScale = 2.0f;
        private const float FaceThreshold = 0.5f, FaceSuppression = 0.3f;

        private readonly PoseTracker _pose;
        private readonly Environment _environment;
        private readonly Model _refinementModel, _handModel, _faceDetectorModel, _faceModel, _blendshapeModel;
        private readonly CompiledModel _refinement, _hand, _faceDetector, _face, _blendshapes;
        private readonly TensorBuffer[] _refinementIn, _refinementOut, _handIn, _handOut, _faceDetectorIn, _faceDetectorOut, _faceIn, _faceOut,
            _blendshapeIn, _blendshapeOut;
        private readonly int[] _handOrder, _faceDetectorOrder, _faceOrder;
        private bool _disposed;

        /// <summary>Compile the seven models. Compilation should run off the UI thread.</summary>
        /// <param name="paths">The model files in <see cref="HolisticModelFiles.ModelNames"/>' order (<see cref="HolisticModelFiles.Extract"/>)</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="accelerators">What the hand and face models may use (the pose models and the blendshape model always run on the CPU:
        /// they don't compile for the GPU)</param>
        /// <param name="gpuPrecision">The GPU's precision (FP32 is the safe choice)</param>
        public HolisticTracker(string[] paths, int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu, GpuPrecision? gpuPrecision = null)
        {
            if (paths == null || paths.Length != HolisticModelFiles.ModelNames.Length)
                throw new ArgumentException("Expected the seven model files of holistic_landmarker.task.", nameof(paths));
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _pose = new PoseTracker(paths[0], paths[1], numThreads, HwAccelerators.Cpu, null);
                _environment = new Environment();
                _refinementModel = new Model(_environment, paths[2]);
                _handModel = new Model(_environment, paths[3]);
                _faceDetectorModel = new Model(_environment, paths[4]);
                _faceModel = new Model(_environment, paths[5]);
                _blendshapeModel = new Model(_environment, paths[6]);
                CheckShape(_refinementModel, new[] { 1, RefinementSize, RefinementSize, 3 }, 1, "hand_roi_refinement");
                CheckShape(_handModel, new[] { 1, HandLandmarkSize, HandLandmarkSize, 3 }, 4, "hand_landmarks_detector");
                CheckShape(_faceDetectorModel, new[] { 1, FaceDetectorSize, FaceDetectorSize, 3 }, 2, "face_detector");
                CheckShape(_faceModel, new[] { 1, FaceLandmarkSize, FaceLandmarkSize, 3 }, 2, "face_landmarks_detector (192 px)");
                CheckShape(_blendshapeModel, new[] { 1, FaceTracker.BlendshapeLandmarks.Count, 2 }, 1, "face_blendshapes");
                // The hand landmark model's outputs: landmarks [1,63], presence [1,1], handedness [1,1] (not used: the pose says which hand
                // it is), world landmarks [1,63].
                _handOrder = OrderByName(_handModel.GetSignature(0).OutputNames, 4);
                // The face detector's: regressors [1,896,16] and logits [1,896,1]; the face landmark model's: landmarks [1,1,1,1434] and the
                // face's presence as a logit [1,1,1,1] - by size.
                _faceDetectorOrder = OrderBySize(_faceDetectorModel, 896 * 16);
                _faceOrder = OrderBySize(_faceModel, FaceTracker.LandmarkCount * 3);
                _refinement = Compile(_refinementModel, numThreads, accelerators, gpuPrecision);
                _hand = Compile(_handModel, numThreads, accelerators, gpuPrecision);
                _faceDetector = Compile(_faceDetectorModel, numThreads, accelerators, gpuPrecision);
                _face = Compile(_faceModel, numThreads, accelerators, gpuPrecision);
                _blendshapes = Compile(_blendshapeModel, numThreads, HwAccelerators.Cpu, null);
                _refinementIn = _refinement.CreateInputBuffers(); _refinementOut = _refinement.CreateOutputBuffers();
                _handIn = _hand.CreateInputBuffers(); _handOut = _hand.CreateOutputBuffers();
                _faceDetectorIn = _faceDetector.CreateInputBuffers(); _faceDetectorOut = _faceDetector.CreateOutputBuffers();
                _faceIn = _face.CreateInputBuffers(); _faceOut = _face.CreateOutputBuffers();
                _blendshapeIn = _blendshapes.CreateInputBuffers(); _blendshapeOut = _blendshapes.CreateOutputBuffers();
            }
            catch { Dispose(); throw; }
        }

        private CompiledModel Compile(Model model, int threads, HwAccelerators accelerators, GpuPrecision? precision)
        {
            using (var options = new Options(accelerators))
            {
                options.AddCpuOptions(new CpuOptions { NumThreads = threads });
                if ((accelerators & HwAccelerators.Gpu) != 0 && precision.HasValue)
                    options.AddGpuOptions(new GpuOptions { Precision = precision.Value });
                return new CompiledModel(_environment, model, options);
            }
        }

        private static void CheckShape(Model model, int[] input, int outputs, string name)
        {
            var signature = model.GetSignature(0);
            if (signature.OutputCount != outputs || signature.GetInputTensorType(0).ElementType != ElementType.Float32 ||
                !signature.GetInputTensorType(0).Dimensions.SequenceEqual(input))
                throw new ArgumentException("Expected MediaPipe's " + name + " model (a float32 input of " + string.Join("x", input) + " and " +
                    outputs + " outputs).");
        }

        private static int[] OrderByName(string[] names, int count)
        {
            var order = Enumerable.Range(0, count).ToArray();
            if (names == null || names.Length != count) return order;
            for (int i = 0; i < count; i++)
            {
                string expected = i == 0 ? "Identity" : "Identity_" + i;
                int found = Array.FindIndex(names, n => n == expected || (n != null && n.EndsWith(":" + expected, StringComparison.Ordinal)));
                if (found < 0) return Enumerable.Range(0, count).ToArray();
                order[i] = found;
            }
            return order;
        }

        // Two outputs: the one with this many values first.
        private static int[] OrderBySize(Model model, int size)
        {
            var signature = model.GetSignature(0);
            int Count(int i) => signature.GetOutputTensorType(i).Dimensions.Aggregate(1, (a, b) => a * b);
            return Count(0) == size ? new[] { 0, 1 } : new[] { 1, 0 };
        }

        /// <summary>The pose part: its <see cref="PoseTracker.DetectorInput"/> and <see cref="PoseTracker.LandmarkInput"/> are the first two inputs to fill.</summary>
        public PoseTracker Pose
        {
            get { CheckActive(); return _pose; }
        }

        /// <summary>Whether LiteRT reports the hand and face models fully on the requested accelerator.</summary>
        public bool IsFullyAccelerated
        {
            get
            {
                CheckActive();
                return _refinement.IsFullyAccelerated && _hand.IsFullyAccelerated && _faceDetector.IsFullyAccelerated &&
                    _face.IsFullyAccelerated;
            }
        }

        /// <summary>The hand re-crop model's input: a hand's region from the pose warped into 256 x 256 float32 RGB, 0..1, edges repeated.</summary>
        public TensorBuffer RefinementInput
        {
            get { CheckActive(); return _refinementIn[0]; }
        }

        /// <summary>The hand landmark model's input: the corrected region warped into 224 x 224 float32 RGB, 0..1, edges repeated.</summary>
        public TensorBuffer HandLandmarkInput
        {
            get { CheckActive(); return _handIn[0]; }
        }

        /// <summary>The face detector's input: the face's region from the pose warped into 128 x 128 float32 RGB, -1..1, black outside the image.</summary>
        public TensorBuffer FaceDetectorInput
        {
            get { CheckActive(); return _faceDetectorIn[0]; }
        }

        /// <summary>The face landmark model's input: the face's region warped (stretched if it isn't square) into 192 x 192 float32 RGB, 0..1, edges repeated.</summary>
        public TensorBuffer FaceLandmarkInput
        {
            get { CheckActive(); return _faceIn[0]; }
        }

        // ---- The pose ----

        /// <summary>Run the pose detector on the filled <see cref="PoseTracker.DetectorInput"/>: the best person, or null.</summary>
        public PoseDetection DetectPose(int imageWidth, int imageHeight, float threshold = 0.5f)
        {
            CheckActive();
            return _pose.Detect(imageWidth, imageHeight, threshold, 1).FirstOrDefault();
        }

        /// <summary>Run the pose landmark model on the filled <see cref="PoseTracker.LandmarkInput"/> (with the heatmap refinement): the body, or null.</summary>
        public Pose ReadPose(PoseDetection detection, HandRect rect, float minPresence = 0.5f)
        {
            CheckActive();
            return _pose.Read(detection, rect, minPresence, refineWithHeatmap: true);
        }

        // ---- The hands ----

        /// <summary>
        /// A hand's region from the pose (MediaPipe's HandDetectionsFromPoseToRectsCalculator, then x 2.7 with a shift of -0.1 along the hand,
        /// made square): centred two thirds of the way from the pinky to the index finger, twice its distance from the wrist across, turned so
        /// the wrist is below. Null if the wrist's visibility is 0.1 or less.
        /// </summary>
        /// <param name="pose">The 33 pose landmarks (x, y, z, visibility, presence)</param>
        /// <param name="left">The person's left hand, or right</param>
        /// <param name="imageWidth">The image's width in pixels</param>
        /// <param name="imageHeight">The image's height in pixels</param>
        public static HandRect HandRectFromPose(float[][] pose, bool left, int imageWidth, int imageHeight)
        {
            if (pose == null || pose.Length != PoseTracker.LandmarkCount) throw new ArgumentException("Expected 33 pose landmarks.", nameof(pose));
            int[] points = left ? LeftHandPose : RightHandPose;
            float[] wrist = pose[points[0]], pinky = pose[points[1]], index = pose[points[2]];
            if (!(wrist[3] > MinWristVisibility)) return null;
            double xw = wrist[0] * imageWidth, yw = wrist[1] * imageHeight;
            double xm = (2.0 * index[0] * imageWidth + pinky[0] * imageWidth) / 3, ym = (2.0 * index[1] * imageHeight + pinky[1] * imageHeight) / 3;
            double box = Math.Sqrt((xm - xw) * (xm - xw) + (ym - yw) * (ym - yw)) * 2;
            var rect = new HandRect
            {
                CenterX = (float)(xm / imageWidth),
                CenterY = (float)(ym / imageHeight),
                Width = (float)(box / imageWidth),
                Height = (float)(box / imageHeight),
                Rotation = (float)Normalize(Math.PI / 2 - Math.Atan2(-(ym - yw), xm - xw))
            };
            return Transform(rect, imageWidth, imageHeight, HandFromPoseScale, 0, HandShiftY, true);
        }

        /// <summary>
        /// Run the hand re-crop model on the filled <see cref="RefinementInput"/> (the region <paramref name="crop"/> warped to 256 x 256) and return
        /// the corrected region (<see cref="HandRectFromRecrop"/>).
        /// </summary>
        public HandRect RefineHand(HandRect crop, int imageWidth, int imageHeight)
        {
            CheckActive();
            _refinement.Run(_refinementIn, _refinementOut);
            float[] output = _refinementOut[0].Read<float>();
            float[] center = Project(output[0] / RefinementSize, output[1] / RefinementSize, crop);
            float[] guide = Project(output[2] / RefinementSize, output[3] / RefinementSize, crop);
            return HandRectFromRecrop(center, guide, imageWidth, imageHeight);
        }

        /// <summary>
        /// The hand's region from the re-crop model's two points, as fractions of the image (MediaPipe's hand_roi_refinement_graph): centred on the
        /// first, twice its distance to the second across, turned so the second is below it, then shifted by -0.1 along the hand.
        /// </summary>
        public static HandRect HandRectFromRecrop(float[] center, float[] guide, int imageWidth, int imageHeight)
        {
            double x0 = center[0] * imageWidth, y0 = center[1] * imageHeight, x1 = guide[0] * imageWidth, y1 = guide[1] * imageHeight;
            double box = 2 * Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            var rect = new HandRect
            {
                CenterX = center[0],
                CenterY = center[1],
                Width = (float)(box / imageWidth),
                Height = (float)(box / imageHeight),
                Rotation = (float)Normalize(-Math.PI / 2 - Math.Atan2(-(y1 - y0), x1 - x0))
            };
            return Transform(rect, imageWidth, imageHeight, 1, 0, HandShiftY, true);
        }

        /// <summary>
        /// Run the hand landmark model on the filled <see cref="HandLandmarkInput"/> (the corrected region warped to 224 x 224). Null if the model
        /// sees no hand there (presence <paramref name="minPresence"/> or less). Which hand it is comes from the pose; the world landmarks are
        /// moved so the wrist is the pose's wrist.
        /// </summary>
        /// <param name="rect">The corrected region</param>
        /// <param name="left">The person's left hand, or right</param>
        /// <param name="pose">The body (for the world wrist), or null</param>
        /// <param name="minPresence">The least presence</param>
        public Hand ReadHand(HandRect rect, bool left, Pose pose, float minPresence = 0.5f)
        {
            CheckActive();
            _hand.Run(_handIn, _handOut);
            float[] raw = _handOut[_handOrder[0]].Read<float>();
            float presence = _handOut[_handOrder[1]].Read<float>()[0];
            float[] world = _handOut[_handOrder[3]].Read<float>();
            if (!(presence > minPresence)) return null;
            float[][] worldLandmarks = Rotate(world, rect.Rotation, HandTracker.LandmarkCount);
            float[] poseWrist = pose?.WorldLandmarks?[(left ? LeftHandPose : RightHandPose)[0]];
            if (poseWrist != null)
            {
                float dx = poseWrist[0] - worldLandmarks[0][0], dy = poseWrist[1] - worldLandmarks[0][1], dz = poseWrist[2] - worldLandmarks[0][2];
                foreach (float[] p in worldLandmarks) { p[0] += dx; p[1] += dy; p[2] += dz; }
            }
            return new Hand
            {
                Rect = rect,
                Presence = presence,
                // From the pose: Handedness names the person's own hand.
                RightHandScore = left ? 0 : 1,
                Landmarks = HandTracker.ProjectLandmarks(raw, rect),
                WorldLandmarks = worldLandmarks
            };
        }

        // ---- The face ----

        /// <summary>
        /// The face's region from the pose (MediaPipe's holistic face tracking): the box of pose landmarks 0-10 (nose, eyes, ears, mouth), level
        /// with the line from the right eye to the left eye, made square and x 3.
        /// </summary>
        public static HandRect FaceRectFromPose(float[][] pose, int imageWidth, int imageHeight)
        {
            if (pose == null || pose.Length != PoseTracker.LandmarkCount) throw new ArgumentException("Expected 33 pose landmarks.", nameof(pose));
            var points = pose.Take(11).ToArray();
            float minX = points.Min(p => p[0]), maxX = points.Max(p => p[0]), minY = points.Min(p => p[1]), maxY = points.Max(p => p[1]);
            var rect = new HandRect
            {
                CenterX = (minX + maxX) / 2,
                CenterY = (minY + maxY) / 2,
                Width = maxX - minX,
                Height = maxY - minY,
                Rotation = Angle(pose[5], pose[2], 0, imageWidth, imageHeight)
            };
            return Transform(rect, imageWidth, imageHeight, FaceFromPoseScale, 0, 0, true);
        }

        /// <summary>
        /// Run the face detector on the filled <see cref="FaceDetectorInput"/> (the region <paramref name="crop"/> warped to 128 x 128): the best
        /// face, with its box and six keypoints as fractions of the image, or null.
        /// </summary>
        public FaceDetection DetectFace(HandRect crop, float threshold = FaceThreshold)
        {
            CheckActive();
            _faceDetector.Run(_faceDetectorIn, _faceDetectorOut);
            float[] regressors = _faceDetectorOut[_faceDetectorOrder[0]].Read<float>();
            float[] logits = _faceDetectorOut[_faceDetectorOrder[1]].Read<float>();
            // In the square crop, as fractions of it (no letterbox), then into the image.
            FaceDetection found = FaceTracker.Decode(regressors, logits, FaceDetectorSize, FaceDetectorSize, threshold, FaceSuppression, 1).FirstOrDefault();
            return found == null ? null : ProjectDetection(found, crop);
        }

        /// <summary>The face detection's keypoints and box (as fractions of the crop) as fractions of the image.</summary>
        public static FaceDetection ProjectDetection(FaceDetection detection, HandRect crop)
        {
            float[] b = detection.Box;
            float[][] corners = { Project(b[0], b[1], crop), Project(b[2], b[1], crop), Project(b[2], b[3], crop), Project(b[0], b[3], crop) };
            return new FaceDetection
            {
                Score = detection.Score,
                Box = new[] { corners.Min(c => c[0]), corners.Min(c => c[1]), corners.Max(c => c[0]), corners.Max(c => c[1]) },
                Keypoints = detection.Keypoints.Select(k => Project(k[0], k[1], crop)).ToArray()
            };
        }

        /// <summary>
        /// The face's region from its detection (MediaPipe's holistic face tracking): the box of the six keypoints, level with the line from the
        /// first keypoint (the right eye) to the second, made square and x 2.
        /// </summary>
        public static HandRect FaceRectFromDetection(FaceDetection detection, int imageWidth, int imageHeight)
        {
            if (detection == null) throw new ArgumentNullException(nameof(detection));
            float[][] k = detection.Keypoints;
            float minX = k.Min(p => p[0]), maxX = k.Max(p => p[0]), minY = k.Min(p => p[1]), maxY = k.Max(p => p[1]);
            var rect = new HandRect
            {
                CenterX = (minX + maxX) / 2,
                CenterY = (minY + maxY) / 2,
                Width = maxX - minX,
                Height = maxY - minY,
                Rotation = Angle(k[0], k[1], 0, imageWidth, imageHeight)
            };
            return Transform(rect, imageWidth, imageHeight, FaceFromDetectionScale, 0, 0, true);
        }

        /// <summary>
        /// Run the face landmark model on the filled <see cref="FaceLandmarkInput"/> (the region <paramref name="rect"/> warped to 192 x 192) and
        /// the blendshape model. Null if the model sees no face there (presence <paramref name="minPresence"/> or less).
        /// </summary>
        public Face ReadFace(FaceDetection detection, HandRect rect, int imageWidth, int imageHeight, float minPresence = 0.5f)
        {
            CheckActive();
            _face.Run(_faceIn, _faceOut);
            float[] raw = _faceOut[_faceOrder[0]].Read<float>();
            float presence = Sigmoid(_faceOut[_faceOrder[1]].Read<float>()[0]);
            if (!(presence > minPresence)) return null;
            float[][] landmarks = ProjectFaceLandmarks(raw, rect, imageWidth, imageHeight);
            return new Face
            {
                Detection = detection,
                Rect = rect,
                Presence = presence,
                Landmarks = landmarks,
                Blendshapes = Blendshapes(landmarks, imageWidth, imageHeight)
            };
        }

        /// <summary>
        /// The 192 px face landmark model's 478 x (x, y, z in crop pixels) as fractions of the image, through the region's rotation; z is scaled
        /// by the length of the region's width in the image (MediaPipe's LandmarkProjection with the image's size).
        /// </summary>
        public static float[][] ProjectFaceLandmarks(float[] raw, HandRect rect, int imageWidth, int imageHeight)
        {
            if (raw == null || raw.Length != FaceTracker.LandmarkCount * 3) throw new ArgumentException("Expected 1434 landmark values.");
            double c = Math.Cos(rect.Rotation), s = Math.Sin(rect.Rotation);
            double zScale = Math.Sqrt(Math.Pow(rect.Width * c, 2) + Math.Pow(rect.Width * imageWidth / imageHeight * s, 2));
            var result = new float[FaceTracker.LandmarkCount][];
            for (int i = 0; i < result.Length; i++)
            {
                float[] p = Project(raw[3 * i] / FaceLandmarkSize, raw[3 * i + 1] / FaceLandmarkSize, rect);
                result[i] = new[] { p[0], p[1], (float)(raw[3 * i + 2] / FaceLandmarkSize * zScale) };
            }
            return result;
        }

        /// <summary>The 52 expression scores of a face's landmarks (the 146 the model takes, as image pixels).</summary>
        public float[] Blendshapes(float[][] landmarks, int imageWidth, int imageHeight)
        {
            CheckActive();
            if (landmarks == null || landmarks.Length != FaceTracker.LandmarkCount) throw new ArgumentException("Expected 478 landmarks.", nameof(landmarks));
            var input = new float[FaceTracker.BlendshapeLandmarks.Count * 2];
            for (int i = 0; i < FaceTracker.BlendshapeLandmarks.Count; i++)
            {
                float[] p = landmarks[FaceTracker.BlendshapeLandmarks[i]];
                input[2 * i] = p[0] * imageWidth;
                input[2 * i + 1] = p[1] * imageHeight;
            }
            _blendshapeIn[0].Write(input);
            _blendshapes.Run(_blendshapeIn, _blendshapeOut);
            return _blendshapeOut[0].Read<float>();
        }

        // ---- Geometry (MediaPipe's calculators; regions as fractions of the image, angles from pixels) ----

        /// <summary>
        /// MediaPipe's RectTransformationCalculator: shift along the region's own axes by a fraction of its size, then (if asked) make it square on
        /// its longer side in pixels, then scale it.
        /// </summary>
        public static HandRect Transform(HandRect rect, int imageWidth, int imageHeight, float scale, float shiftX, float shiftY, bool squareLong)
        {
            double w = rect.Width, h = rect.Height, r = rect.Rotation, x = rect.CenterX, y = rect.CenterY;
            if (r == 0)
            {
                x += w * shiftX;
                y += h * shiftY;
            }
            else
            {
                x += (imageWidth * w * shiftX * Math.Cos(r) - imageHeight * h * shiftY * Math.Sin(r)) / imageWidth;
                y += (imageWidth * w * shiftX * Math.Sin(r) + imageHeight * h * shiftY * Math.Cos(r)) / imageHeight;
            }
            if (squareLong)
            {
                double side = Math.Max(w * imageWidth, h * imageHeight);
                w = side / imageWidth;
                h = side / imageHeight;
            }
            return new HandRect { CenterX = (float)x, CenterY = (float)y, Width = (float)(w * scale), Height = (float)(h * scale), Rotation = rect.Rotation };
        }

        // A point given as a fraction of a region (0..1 across and down it) as a fraction of the image.
        private static float[] Project(float u, float v, HandRect rect)
        {
            double c = Math.Cos(rect.Rotation), s = Math.Sin(rect.Rotation);
            double x = u - 0.5, y = v - 0.5;
            return new[] { (float)((c * x - s * y) * rect.Width + rect.CenterX), (float)((s * x + c * y) * rect.Height + rect.CenterY) };
        }

        // The rotation that makes the line from one point to another point at the target angle (degrees), from the image's pixels.
        private static float Angle(float[] from, float[] to, double targetDegrees, int imageWidth, int imageHeight)
        {
            double dx = (to[0] - from[0]) * imageWidth, dy = (to[1] - from[1]) * imageHeight;
            return (float)Normalize(targetDegrees * Math.PI / 180 - Math.Atan2(-dy, dx));
        }

        private static double Normalize(double angle) => angle - 2 * Math.PI * Math.Floor((angle + Math.PI) / (2 * Math.PI));

        private static float Sigmoid(float v) => (float)(1 / (1 + Math.Exp(-Math.Max(-100f, Math.Min(100f, v)))));

        private static float[][] Rotate(float[] values, float rotation, int count)
        {
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            var result = new float[count][];
            for (int i = 0; i < count; i++)
                result[i] = new[] { (float)(c * values[3 * i] - s * values[3 * i + 1]), (float)(s * values[3 * i] + c * values[3 * i + 1]), values[3 * i + 2] };
            return result;
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HolisticTracker));
        }

        /// <summary>Release the buffers, compiled models and environments.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var set in new[] { _refinementIn, _refinementOut, _handIn, _handOut, _faceDetectorIn, _faceDetectorOut, _faceIn, _faceOut, _blendshapeIn, _blendshapeOut })
                if (set != null) foreach (var b in set) b?.Dispose();
            _refinement?.Dispose(); _hand?.Dispose(); _faceDetector?.Dispose(); _face?.Dispose(); _blendshapes?.Dispose();
            _refinementModel?.Dispose(); _handModel?.Dispose(); _faceDetectorModel?.Dispose(); _faceModel?.Dispose(); _blendshapeModel?.Dispose();
            _environment?.Dispose();
            _pose?.Dispose();
        }
    }
}
