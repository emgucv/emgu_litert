// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>A face found by the face detector: a score, a box and six keypoints, as fractions of the image.</summary>
    public sealed class FaceDetection
    {
        /// <summary>The detector's score (0..1)</summary>
        public float Score { get; internal set; }
        /// <summary>left, top, right, bottom</summary>
        public float[] Box { get; internal set; }
        /// <summary>Six (x, y) keypoints: the eye on the image's left, the other eye, the nose tip, the mouth, and the two ear tragions</summary>
        public float[][] Keypoints { get; internal set; }
    }

    /// <summary>A face: 478 landmarks and, if asked for, 52 expression scores, as MediaPipe's face landmarker returns them.</summary>
    public sealed class Face
    {
        /// <summary>The detection it came from</summary>
        public FaceDetection Detection { get; internal set; }
        /// <summary>The region the landmark model was run on</summary>
        public HandRect Rect { get; internal set; }
        /// <summary>The landmark model's confidence that there is a face in the region (0..1)</summary>
        public float Presence { get; internal set; }
        /// <summary>478 x (x, y as fractions of the image, z in the same unit as x scaled to the region; smaller is nearer): the face mesh, the last 10 the irises</summary>
        public float[][] Landmarks { get; internal set; }
        /// <summary>52 expression scores (0..1) in the order of <see cref="FaceTracker.BlendshapeNames"/>, or null if not asked for</summary>
        public float[] Blendshapes { get; internal set; }

        /// <summary>An expression's score by its name (e.g. "mouthSmileLeft"), or 0 without blendshapes.</summary>
        public float Blendshape(string name)
        {
            int i = FaceTracker.IndexOfBlendshape(name);
            return Blendshapes != null && i >= 0 ? Blendshapes[i] : 0;
        }
    }

    /// <summary>
    /// MediaPipe's face landmarker on LiteRT's CompiledModel (FACE.md): the face detector (BlazeFace short range, 128 px, 896 SSD anchors,
    /// weighted NMS) finds faces, each box scaled by 1.5 and turned so the eyes are level gives the region, the landmark model (Face Mesh
    /// V2, 256 px) returns 478 landmarks and a presence score, and the blendshape model turns 146 of the landmarks into 52 expression
    /// scores (always on the CPU: its graph doesn't compile for the Metal GPU, and it takes about 1 ms). Like <see cref="PoseTracker"/>
    /// the host fills <see cref="DetectorInput"/> and <see cref="LandmarkInput"/> (images are cut and scaled with OpenCV or the platform,
    /// not here) and serializes calls. The models are made for faces near the camera (within about 2 m), facing it; per their model
    /// cards they read expressions and do not identify anyone, and are not for life-critical decisions. Every frame runs the detector:
    /// no tracking between frames yet.
    /// </summary>
    public sealed class FaceTracker : IDisposable
    {
        /// <summary>The detector's square input size</summary>
        public const int DetectorSize = 128;
        /// <summary>The landmark model's square input size</summary>
        public const int LandmarkSize = 256;
        /// <summary>Landmarks per face</summary>
        public const int LandmarkCount = 478;
        /// <summary>Expression scores per face</summary>
        public const int BlendshapeCount = 52;
        private const int AnchorCount = 896;

        /// <summary>The expression scores' names, in the blendshape model's order (ARKit's names; "_neutral" first).</summary>
        public static IReadOnlyList<string> BlendshapeNames { get; } = Array.AsReadOnly(new[]
        {
            "_neutral", "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight", "cheekPuff", "cheekSquintLeft",
            "cheekSquintRight", "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight",
            "eyeLookOutLeft", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight", "eyeWideLeft",
            "eyeWideRight", "jawForward", "jawLeft", "jawOpen", "jawRight", "mouthClose", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthFrownLeft", "mouthFrownRight", "mouthFunnel", "mouthLeft", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthPressLeft",
            "mouthPressRight", "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight", "mouthUpperUpLeft", "mouthUpperUpRight",
            "noseSneerLeft", "noseSneerRight"
        });

        /// <summary>The 146 landmarks the blendshape model takes, in its order (MediaPipe's face_blendshapes_graph.cc).</summary>
        public static IReadOnlyList<int> BlendshapeLandmarks { get; } = Array.AsReadOnly(new[]
        {
            0, 1, 4, 5, 6, 7, 8, 10, 13, 14, 17, 21, 33, 37, 39, 40, 46, 52, 53, 54, 55, 58, 61, 63, 65, 66, 67, 70, 78, 80, 81, 82, 84, 87, 88, 91,
            93, 95, 103, 105, 107, 109, 127, 132, 133, 136, 144, 145, 146, 148, 149, 150, 152, 153, 154, 155, 157, 158, 159, 160, 161, 162, 163,
            168, 172, 173, 176, 178, 181, 185, 191, 195, 197, 234, 246, 249, 251, 263, 267, 269, 270, 276, 282, 283, 284, 285, 288, 291, 293,
            295, 296, 297, 300, 308, 310, 311, 312, 314, 317, 318, 321, 323, 324, 332, 334, 336, 338, 356, 361, 362, 365, 373, 374, 375, 377,
            378, 379, 380, 381, 382, 384, 385, 386, 387, 388, 389, 390, 397, 398, 400, 402, 405, 409, 415, 454, 466, 468, 469, 470, 471, 472,
            473, 474, 475, 476, 477
        });

        /// <summary>
        /// Outlines to draw, as pairs of landmark indexes: the face oval, the eyes, the eyebrows and the lips (a subset of MediaPipe's mesh
        /// connections).
        /// </summary>
        public static readonly int[][] Contours = Pairs(
            // face oval
            new[] { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379, 378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109, 10 },
            // the eye on the image's left, and the other
            new[] { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246, 33 },
            new[] { 263, 249, 390, 373, 374, 380, 381, 382, 362, 398, 384, 385, 386, 387, 388, 466, 263 },
            // eyebrows: each a lower and an upper line
            new[] { 46, 53, 52, 65, 55 },
            new[] { 70, 63, 105, 66, 107 },
            new[] { 276, 283, 282, 295, 285 },
            new[] { 300, 293, 334, 296, 336 },
            // lips, outer and inner
            new[] { 61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185, 61 },
            new[] { 78, 95, 88, 178, 87, 14, 317, 402, 318, 324, 308, 415, 310, 311, 312, 13, 82, 81, 80, 191, 78 });

        private static int[][] Pairs(params int[][] paths) =>
            paths.SelectMany(p => Enumerable.Range(0, p.Length - 1).Select(i => new[] { p[i], p[i + 1] })).ToArray();

        /// <summary>The index of an expression in <see cref="BlendshapeNames"/>, or -1.</summary>
        public static int IndexOfBlendshape(string name)
        {
            for (int i = 0; i < BlendshapeNames.Count; i++)
                if (string.Equals(BlendshapeNames[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static float[][] _anchors;

        private readonly Environment _environment;
        private readonly Model _detectorModel, _landmarkModel, _blendshapeModel;
        private readonly CompiledModel _detector, _landmarks, _blendshapes;
        private readonly TensorBuffer[] _detectorIn, _detectorOut, _landmarkIn, _landmarkOut, _blendshapeIn, _blendshapeOut;
        private readonly int[] _detectorOrder, _landmarkOrder;
        private bool _disposed;

        /// <summary>Compile the models. Compilation should run off the UI thread.</summary>
        /// <param name="detectorPath">face_detector.tflite</param>
        /// <param name="landmarkPath">face_landmarks_detector.tflite with a static batch (<see cref="FaceModelFiles.Extract"/>)</param>
        /// <param name="blendshapePath">face_blendshapes.tflite, or null for no expression scores</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="accelerators">What the detector and the landmark model may use (the blendshape model runs on the CPU)</param>
        /// <param name="gpuPrecision">The GPU's precision (FP32 is the safe choice)</param>
        public FaceTracker(string detectorPath, string landmarkPath, string blendshapePath = null, int numThreads = 2,
            HwAccelerators accelerators = HwAccelerators.Cpu, GpuPrecision? gpuPrecision = null)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _detectorModel = new Model(_environment, detectorPath);
                _landmarkModel = new Model(_environment, landmarkPath);
                CheckShape(_detectorModel, new[] { 1, DetectorSize, DetectorSize, 3 }, 2, "face_detector");
                CheckShape(_landmarkModel, new[] { 1, LandmarkSize, LandmarkSize, 3 }, 3, "face_landmarks_detector (with a static batch)");
                // The detector's outputs: regressors [1,896,16] and logits [1,896,1] - by shape.
                var detectorSignature = _detectorModel.GetSignature(0);
                _detectorOrder = new int[2];
                for (int i = 0; i < 2; i++)
                    _detectorOrder[detectorSignature.GetOutputTensorType(i).Dimensions[2] == 16 ? 0 : 1] = i;
                // The landmark model's: landmarks [1,1,1,1434], the face's presence as a logit [1,1,1,1], and a third score (not used).
                _landmarkOrder = OrderByName(_landmarkModel.GetSignature(0).OutputNames, 3);
                _detector = Compile(_detectorModel, numThreads, accelerators, gpuPrecision);
                _landmarks = Compile(_landmarkModel, numThreads, accelerators, gpuPrecision);
                _detectorIn = _detector.CreateInputBuffers();
                _detectorOut = _detector.CreateOutputBuffers();
                _landmarkIn = _landmarks.CreateInputBuffers();
                _landmarkOut = _landmarks.CreateOutputBuffers();
                if (blendshapePath != null)
                {
                    _blendshapeModel = new Model(_environment, blendshapePath);
                    CheckShape(_blendshapeModel, new[] { 1, BlendshapeLandmarks.Count, 2 }, 1, "face_blendshapes");
                    _blendshapes = Compile(_blendshapeModel, numThreads, HwAccelerators.Cpu, null);
                    _blendshapeIn = _blendshapes.CreateInputBuffers();
                    _blendshapeOut = _blendshapes.CreateOutputBuffers();
                }
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
                throw new ArgumentException("Expected MediaPipe's " + name + " model (a float32 input of " + string.Join("x", input) +
                    " and " + outputs + " outputs).");
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

        /// <summary>Whether LiteRT reports the detector and the landmark model fully on the requested accelerator.</summary>
        public bool IsFullyAccelerated
        {
            get { CheckActive(); return _detector.IsFullyAccelerated && _landmarks.IsFullyAccelerated; }
        }

        /// <summary>True if the tracker computes expression scores.</summary>
        public bool HasBlendshapes => _blendshapes != null;

        /// <summary>The detector's input: the image letterboxed into 128 x 128 float32 RGB, scaled to -1..1, the padding 0.</summary>
        public TensorBuffer DetectorInput
        {
            get { CheckActive(); return _detectorIn[0]; }
        }

        /// <summary>The landmark model's input: the face's region warped into 256 x 256 float32 RGB, 0..1.</summary>
        public TensorBuffer LandmarkInput
        {
            get { CheckActive(); return _landmarkIn[0]; }
        }

        /// <summary>How an image of this size goes into the detector's square input: scaled to fit and centred.</summary>
        public static void Letterbox(int imageWidth, int imageHeight, out int width, out int height, out int left, out int top)
        {
            double scale = Math.Min((double)DetectorSize / imageWidth, (double)DetectorSize / imageHeight);
            width = Math.Max(1, (int)Math.Round(imageWidth * scale));
            height = Math.Max(1, (int)Math.Round(imageHeight * scale));
            left = (DetectorSize - width) / 2;
            top = (DetectorSize - height) / 2;
        }

        /// <summary>Run the detector on the filled <see cref="DetectorInput"/>.</summary>
        /// <param name="imageWidth">The width of the image that was letterboxed</param>
        /// <param name="imageHeight">The height of the image that was letterboxed</param>
        /// <param name="threshold">The least score</param>
        /// <param name="maxFaces">The most faces to return, best first</param>
        public FaceDetection[] Detect(int imageWidth, int imageHeight, float threshold = 0.5f, int maxFaces = 1)
        {
            CheckActive();
            _detector.Run(_detectorIn, _detectorOut);
            float[] regressors = _detectorOut[_detectorOrder[0]].Read<float>();
            float[] logits = _detectorOut[_detectorOrder[1]].Read<float>();
            return Decode(regressors, logits, imageWidth, imageHeight, threshold, 0.3f, maxFaces);
        }

        /// <summary>The 896 SSD anchor centres (x, y as fractions of the input) of the face detector.</summary>
        public static float[][] Anchors()
        {
            if (_anchors != null) return _anchors;
            // SsdAnchorsCalculator: strides 8, 16, 16, 16; layers of one stride share a grid, and each has two anchors (aspect ratio 1.0
            // and the interpolated scale) at every cell centre: 16x16x2 + 8x8x6 = 896. Their sizes are fixed.
            int[] strides = { 8, 16, 16, 16 };
            var anchors = new List<float[]>();
            for (int layer = 0; layer < strides.Length;)
            {
                int last = layer;
                while (last < strides.Length && strides[last] == strides[layer]) last++;
                int grid = (DetectorSize + strides[layer] - 1) / strides[layer];
                for (int y = 0; y < grid; y++)
                    for (int x = 0; x < grid; x++)
                        for (int k = 0; k < (last - layer) * 2; k++)
                            anchors.Add(new[] { (x + 0.5f) / grid, (y + 0.5f) / grid });
                layer = last;
            }
            return _anchors = anchors.ToArray();
        }

        /// <summary>
        /// Turn the detector's outputs into faces: logistic scores, boxes and keypoints from the anchors, weighted non-maximum suppression
        /// (IoU 0.3), and the letterbox padding taken out so everything is a fraction of the image.
        /// </summary>
        public static FaceDetection[] Decode(float[] regressors, float[] logits, int imageWidth, int imageHeight,
            float threshold = 0.5f, float nmsThreshold = 0.3f, int maxFaces = 1)
        {
            if (regressors == null || regressors.Length != AnchorCount * 16 || logits == null || logits.Length != AnchorCount)
                throw new ArgumentException("Expected the face detector's outputs [1,896,16] and [1,896,1].");
            if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            float[][] anchors = Anchors();
            var candidates = new List<FaceDetection>();
            for (int i = 0; i < AnchorCount; i++)
            {
                float logit = Math.Max(-100f, Math.Min(100f, logits[i]));
                float score = (float)(1 / (1 + Math.Exp(-logit)));
                if (!(score >= threshold)) continue;
                int o = i * 16;
                float ax = anchors[i][0], ay = anchors[i][1];
                float xc = regressors[o] / DetectorSize + ax, yc = regressors[o + 1] / DetectorSize + ay;
                float w = regressors[o + 2] / DetectorSize, h = regressors[o + 3] / DetectorSize;
                var keypoints = new float[6][];
                for (int k = 0; k < 6; k++)
                    keypoints[k] = new[] { regressors[o + 4 + 2 * k] / DetectorSize + ax, regressors[o + 5 + 2 * k] / DetectorSize + ay };
                candidates.Add(new FaceDetection { Score = score, Box = new[] { xc - w / 2, yc - h / 2, xc + w / 2, yc + h / 2 }, Keypoints = keypoints });
            }
            candidates = candidates.OrderByDescending(c => c.Score).ToList();
            Letterbox(imageWidth, imageHeight, out int width, out int height, out int left, out int top);
            float pl = (float)left / DetectorSize, pt = (float)top / DetectorSize;
            float sx = (float)width / DetectorSize, sy = (float)height / DetectorSize;
            var result = new List<FaceDetection>();
            while (candidates.Count > 0 && result.Count < maxFaces)
            {
                FaceDetection best = candidates[0];
                var group = new List<FaceDetection> { best };
                var rest = new List<FaceDetection>();
                foreach (FaceDetection c in candidates.Skip(1))
                    (Overlap(best.Box, c.Box) > nmsThreshold ? group : rest).Add(c);
                candidates = rest;
                float total = group.Sum(g => g.Score);
                float[] box = Enumerable.Range(0, 4).Select(k => group.Sum(g => g.Score * g.Box[k]) / total).ToArray();
                var keypoints = new float[6][];
                for (int j = 0; j < 6; j++)
                    keypoints[j] = new[]
                    {
                        (group.Sum(g => g.Score * g.Keypoints[j][0]) / total - pl) / sx,
                        (group.Sum(g => g.Score * g.Keypoints[j][1]) / total - pt) / sy
                    };
                result.Add(new FaceDetection
                {
                    Score = best.Score,
                    Box = new[] { (box[0] - pl) / sx, (box[1] - pt) / sy, (box[2] - pl) / sx, (box[3] - pt) / sy },
                    Keypoints = keypoints
                });
            }
            return result.ToArray();
        }

        private static float Overlap(float[] a, float[] b)
        {
            float ix = Math.Max(0, Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]));
            float iy = Math.Max(0, Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]));
            float inter = ix * iy;
            float union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter;
            return union > 0 ? inter / union : 0;
        }

        /// <summary>
        /// The region of a face: the detection's box scaled by 1.5 about its centre, turned so the line from the first eye keypoint to the
        /// second is level (MediaPipe's DetectionsToRects and RectTransformation in the face detector graph).
        /// </summary>
        public static HandRect RectOf(FaceDetection detection, int imageWidth, int imageHeight)
        {
            if (detection == null) throw new ArgumentNullException(nameof(detection));
            float[] b = detection.Box, k0 = detection.Keypoints[0], k1 = detection.Keypoints[1];
            double rotation = -Math.Atan2(-(k1[1] - k0[1]) * imageHeight, (k1[0] - k0[0]) * imageWidth);
            rotation -= 2 * Math.PI * Math.Floor((rotation + Math.PI) / (2 * Math.PI));
            return new HandRect
            {
                CenterX = (b[0] + b[2]) / 2,
                CenterY = (b[1] + b[3]) / 2,
                Width = (b[2] - b[0]) * 1.5f,
                Height = (b[3] - b[1]) * 1.5f,
                Rotation = (float)rotation
            };
        }

        /// <summary>
        /// Run the landmark model on the filled <see cref="LandmarkInput"/> (the region <paramref name="rect"/> warped to 256 x 256) and,
        /// with <see cref="HasBlendshapes"/>, the blendshape model. Null if the model sees no face there (presence under
        /// <paramref name="minPresence"/>).
        /// </summary>
        public Face Read(FaceDetection detection, HandRect rect, int imageWidth, int imageHeight, float minPresence = 0.5f)
        {
            CheckActive();
            _landmarks.Run(_landmarkIn, _landmarkOut);
            float[] raw = _landmarkOut[_landmarkOrder[0]].Read<float>();
            float presence = Sigmoid(_landmarkOut[_landmarkOrder[1]].Read<float>()[0]);
            if (!(presence >= minPresence)) return null;
            float[][] landmarks = ProjectLandmarks(raw, rect);
            return new Face
            {
                Detection = detection,
                Rect = rect,
                Presence = presence,
                Landmarks = landmarks,
                Blendshapes = HasBlendshapes ? Blendshapes(landmarks, imageWidth, imageHeight) : null
            };
        }

        /// <summary>The 52 expression scores of a face's landmarks (the 146 the model takes, as image pixels).</summary>
        public float[] Blendshapes(float[][] landmarks, int imageWidth, int imageHeight)
        {
            CheckActive();
            if (!HasBlendshapes) throw new InvalidOperationException("The tracker was made without the blendshape model.");
            if (landmarks == null || landmarks.Length != LandmarkCount) throw new ArgumentException("Expected 478 landmarks.", nameof(landmarks));
            var input = new float[BlendshapeLandmarks.Count * 2];
            for (int i = 0; i < BlendshapeLandmarks.Count; i++)
            {
                float[] p = landmarks[BlendshapeLandmarks[i]];
                input[2 * i] = p[0] * imageWidth;
                input[2 * i + 1] = p[1] * imageHeight;
            }
            _blendshapeIn[0].Write(input);
            _blendshapes.Run(_blendshapeIn, _blendshapeOut);
            return _blendshapeOut[0].Read<float>();
        }

        /// <summary>
        /// The landmark model's 478 x (x, y, z in 256-pixel units) as fractions of the image, through the region's rotation, with z scaled
        /// to the region (MediaPipe's LandmarkProjection).
        /// </summary>
        public static float[][] ProjectLandmarks(float[] raw, HandRect rect)
        {
            if (raw == null || raw.Length != LandmarkCount * 3) throw new ArgumentException("Expected 1434 landmark values.");
            double c = Math.Cos(rect.Rotation), s = Math.Sin(rect.Rotation);
            var result = new float[LandmarkCount][];
            for (int i = 0; i < LandmarkCount; i++)
            {
                double x = raw[3 * i] / LandmarkSize - 0.5, y = raw[3 * i + 1] / LandmarkSize - 0.5;
                result[i] = new[]
                {
                    (float)((c * x - s * y) * rect.Width + rect.CenterX),
                    (float)((s * x + c * y) * rect.Height + rect.CenterY),
                    (float)(raw[3 * i + 2] / LandmarkSize * rect.Width)
                };
            }
            return result;
        }

        private static float Sigmoid(float v) => (float)(1 / (1 + Math.Exp(-Math.Max(-100f, Math.Min(100f, v)))));

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FaceTracker));
        }

        /// <summary>Release the buffers, compiled models and environment.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var set in new[] { _detectorIn, _detectorOut, _landmarkIn, _landmarkOut, _blendshapeIn, _blendshapeOut })
                if (set != null) foreach (var b in set) b?.Dispose();
            _detector?.Dispose(); _landmarks?.Dispose(); _blendshapes?.Dispose();
            _detectorModel?.Dispose(); _landmarkModel?.Dispose(); _blendshapeModel?.Dispose();
            _environment?.Dispose();
        }
    }
}
