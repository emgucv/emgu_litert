// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>A person found in an image by the pose detector: a score, a box and four keypoints, as fractions of the image.</summary>
    public sealed class PoseDetection
    {
        /// <summary>The detector's score (0..1)</summary>
        public float Score { get; internal set; }
        /// <summary>left, top, right, bottom</summary>
        public float[] Box { get; internal set; }
        /// <summary>Four (x, y) keypoints: the middle of the hips, a point on the circle around the whole body, then two for the upper body</summary>
        public float[][] Keypoints { get; internal set; }
    }

    /// <summary>A body: 33 landmarks, as MediaPipe's pose landmarker returns them.</summary>
    public sealed class Pose
    {
        /// <summary>The detection it came from</summary>
        public PoseDetection Detection { get; internal set; }
        /// <summary>The region the landmark model was run on</summary>
        public HandRect Rect { get; internal set; }
        /// <summary>The landmark model's own confidence that there is a body in the region (0..1)</summary>
        public float Presence { get; internal set; }
        /// <summary>33 x (x, y as fractions of the image, z in the same unit as x scaled to the region, visibility, presence): see <see cref="PoseTracker.LandmarkNames"/></summary>
        public float[][] Landmarks { get; internal set; }
        /// <summary>33 x (x, y, z) in metres from the middle of the hips, turned upright by the region's rotation; z is only up to scale</summary>
        public float[][] WorldLandmarks { get; internal set; }
        /// <summary>The model's 6 auxiliary landmarks, as <see cref="Landmarks"/>: the first two (the hip centre and a point on the circle round the
        /// body) give the next frame's region when tracking (<see cref="PoseTracker.RectOf(float[], float[], int, int)"/>)</summary>
        public float[][] AuxiliaryLandmarks { get; internal set; }
    }

    /// <summary>
    /// MediaPipe's pose landmarker (BlazePose) on LiteRT's CompiledModel (TODO item 51, POSE.md): the pose detector (224 px, 2254 SSD
    /// anchors, weighted NMS) finds a person, its first two keypoints give the rotated square the body is in, and the landmark model
    /// (256 px) returns 33 landmarks plus 6 auxiliary ones, the visibility and presence of each, the world landmarks and a body mask
    /// (not used). Like <see cref="HandTracker"/> the host fills <see cref="DetectorInput"/> and <see cref="LandmarkInput"/> (images are
    /// cut and scaled with OpenCV or the platform, not here) and calls are serialized by the host. The model tracks one person, whose head
    /// is in view, within about 4 m (its model card), and every frame here runs the detector: no tracking between frames yet.
    /// </summary>
    public sealed class PoseTracker : IDisposable
    {
        /// <summary>The detector's square input size</summary>
        public const int DetectorSize = 224;
        /// <summary>The landmark model's square input size</summary>
        public const int LandmarkSize = 256;
        /// <summary>Landmarks per body that are returned</summary>
        public const int LandmarkCount = 33;
        private const int OutputLandmarks = 39;      // 33 + 6 auxiliary
        private const int AnchorCount = 2254;

        /// <summary>The landmarks' names, in the model's order</summary>
        public static IReadOnlyList<string> LandmarkNames { get; } = Array.AsReadOnly(new[]
        {
            "nose", "left_eye_inner", "left_eye", "left_eye_outer", "right_eye_inner", "right_eye", "right_eye_outer", "left_ear", "right_ear",
            "mouth_left", "mouth_right", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_wrist", "right_wrist",
            "left_pinky", "right_pinky", "left_index", "right_index", "left_thumb", "right_thumb", "left_hip", "right_hip",
            "left_knee", "right_knee", "left_ankle", "right_ankle", "left_heel", "right_heel", "left_foot_index", "right_foot_index"
        });

        /// <summary>The bones to draw, as pairs of landmark indexes</summary>
        public static readonly int[][] Bones =
        {
            new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 7 }, new[] { 0, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 8 }, new[] { 9, 10 },
            new[] { 11, 12 }, new[] { 11, 13 }, new[] { 13, 15 }, new[] { 15, 17 }, new[] { 15, 19 }, new[] { 15, 21 }, new[] { 17, 19 },
            new[] { 12, 14 }, new[] { 14, 16 }, new[] { 16, 18 }, new[] { 16, 20 }, new[] { 16, 22 }, new[] { 18, 20 },
            new[] { 11, 23 }, new[] { 12, 24 }, new[] { 23, 24 }, new[] { 23, 25 }, new[] { 25, 27 }, new[] { 27, 29 }, new[] { 29, 31 }, new[] { 27, 31 },
            new[] { 24, 26 }, new[] { 26, 28 }, new[] { 28, 30 }, new[] { 30, 32 }, new[] { 28, 32 }
        };

        private static float[][] _anchors;

        private readonly Environment _environment;
        private readonly Model _detectorModel, _landmarkModel;
        private readonly CompiledModel _detector, _landmarks;
        private readonly TensorBuffer[] _detectorIn, _detectorOut, _landmarkIn, _landmarkOut;
        private readonly int[] _detectorOrder, _landmarkOrder;
        private bool _disposed;

        /// <summary>Compile the two models. Compilation should run off the UI thread.</summary>
        /// <param name="detectorPath">pose_detector.tflite</param>
        /// <param name="landmarkPath">pose_landmarks_detector.tflite</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="accelerators">What the models may use</param>
        /// <param name="gpuPrecision">The GPU's precision (FP32 is the safe choice)</param>
        public PoseTracker(string detectorPath, string landmarkPath, int numThreads = 2,
            HwAccelerators accelerators = HwAccelerators.Cpu, GpuPrecision? gpuPrecision = null)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _detectorModel = new Model(_environment, detectorPath);
                _landmarkModel = new Model(_environment, landmarkPath);
                CheckShape(_detectorModel, DetectorSize, 2, "pose_detector");
                CheckShape(_landmarkModel, LandmarkSize, 5, "pose_landmarks_detector");
                // The detector's outputs: regressors [1,2254,12] and logits [1,2254,1] - by shape.
                var detectorSignature = _detectorModel.GetSignature(0);
                _detectorOrder = new int[2];
                for (int i = 0; i < 2; i++)
                    _detectorOrder[detectorSignature.GetOutputTensorType(i).Dimensions[2] == 12 ? 0 : 1] = i;
                // The landmark model's: landmarks [1,195], presence [1,1], mask [1,256,256,1], heatmap [1,64,64,39], world landmarks [1,117].
                _landmarkOrder = OrderByName(_landmarkModel.GetSignature(0).OutputNames, 5);
                _detector = Compile(_detectorModel, numThreads, accelerators, gpuPrecision);
                _landmarks = Compile(_landmarkModel, numThreads, accelerators, gpuPrecision);
                _detectorIn = _detector.CreateInputBuffers();
                _detectorOut = _detector.CreateOutputBuffers();
                _landmarkIn = _landmarks.CreateInputBuffers();
                _landmarkOut = _landmarks.CreateOutputBuffers();
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

        private static void CheckShape(Model model, int size, int outputs, string name)
        {
            var signature = model.GetSignature(0);
            if (signature.OutputCount != outputs || signature.GetInputTensorType(0).ElementType != ElementType.Float32 ||
                !signature.GetInputTensorType(0).Dimensions.SequenceEqual(new[] { 1, size, size, 3 }))
                throw new ArgumentException("Expected MediaPipe's " + name + " model (a float32 input of 1x" + size + "x" + size + "x3 and " + outputs + " outputs).");
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

        /// <summary>Whether LiteRT reports both models fully on the requested accelerator.</summary>
        public bool IsFullyAccelerated
        {
            get { CheckActive(); return _detector.IsFullyAccelerated && _landmarks.IsFullyAccelerated; }
        }

        /// <summary>The detector's input: the image letterboxed into 224 x 224 float32 RGB, scaled to -1..1.</summary>
        public TensorBuffer DetectorInput
        {
            get { CheckActive(); return _detectorIn[0]; }
        }

        /// <summary>The landmark model's input: the body's region warped into 256 x 256 float32 RGB, 0..1.</summary>
        public TensorBuffer LandmarkInput
        {
            get { CheckActive(); return _landmarkIn[0]; }
        }

        /// <summary>How an image of this size goes into the detector's square input: scaled to fit and centred (the padding is black).</summary>
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
        /// <param name="maxPoses">The most people to return, best first</param>
        public PoseDetection[] Detect(int imageWidth, int imageHeight, float threshold = 0.5f, int maxPoses = 1)
        {
            CheckActive();
            _detector.Run(_detectorIn, _detectorOut);
            float[] regressors = _detectorOut[_detectorOrder[0]].Read<float>();
            float[] logits = _detectorOut[_detectorOrder[1]].Read<float>();
            return Decode(regressors, logits, imageWidth, imageHeight, threshold, 0.3f, maxPoses);
        }

        /// <summary>The 2254 SSD anchor centres (x, y as fractions of the input) of the pose detector.</summary>
        public static float[][] Anchors()
        {
            if (_anchors != null) return _anchors;
            // SsdAnchorsCalculator: strides 8, 16, 32, 32, 32; layers of one stride share a grid, and each has two anchors (aspect
            // ratio 1.0 and the interpolated scale) at every cell centre: 28x28x2 + 14x14x2 + 7x7x6 = 2254. Their sizes are fixed.
            int[] strides = { 8, 16, 32, 32, 32 };
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
        /// Turn the detector's outputs into detections: logistic scores, boxes and keypoints from the anchors, weighted non-maximum
        /// suppression (IoU 0.3), and the letterbox padding taken out so everything is a fraction of the image.
        /// </summary>
        public static PoseDetection[] Decode(float[] regressors, float[] logits, int imageWidth, int imageHeight,
            float threshold = 0.5f, float nmsThreshold = 0.3f, int maxPoses = 1)
        {
            if (regressors == null || regressors.Length != AnchorCount * 12 || logits == null || logits.Length != AnchorCount)
                throw new ArgumentException("Expected the pose detector's outputs [1,2254,12] and [1,2254,1].");
            if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            float[][] anchors = Anchors();
            var candidates = new List<PoseDetection>();
            for (int i = 0; i < AnchorCount; i++)
            {
                float logit = Math.Max(-100f, Math.Min(100f, logits[i]));
                float score = (float)(1 / (1 + Math.Exp(-logit)));
                if (!(score >= threshold)) continue;
                int o = i * 12;
                float ax = anchors[i][0], ay = anchors[i][1];
                float xc = regressors[o] / DetectorSize + ax, yc = regressors[o + 1] / DetectorSize + ay;
                float w = regressors[o + 2] / DetectorSize, h = regressors[o + 3] / DetectorSize;
                var keypoints = new float[4][];
                for (int k = 0; k < 4; k++)
                    keypoints[k] = new[] { regressors[o + 4 + 2 * k] / DetectorSize + ax, regressors[o + 5 + 2 * k] / DetectorSize + ay };
                candidates.Add(new PoseDetection { Score = score, Box = new[] { xc - w / 2, yc - h / 2, xc + w / 2, yc + h / 2 }, Keypoints = keypoints });
            }
            candidates = candidates.OrderByDescending(c => c.Score).ToList();
            Letterbox(imageWidth, imageHeight, out _, out _, out int left, out int top);
            float pl = (float)left / DetectorSize, pt = (float)top / DetectorSize;
            float sx = 1 - 2 * pl, sy = 1 - 2 * pt;
            var result = new List<PoseDetection>();
            while (candidates.Count > 0 && result.Count < maxPoses)
            {
                PoseDetection best = candidates[0];
                var group = new List<PoseDetection> { best };
                var rest = new List<PoseDetection>();
                foreach (PoseDetection c in candidates.Skip(1))
                    (Overlap(best.Box, c.Box) > nmsThreshold ? group : rest).Add(c);
                candidates = rest;
                float total = group.Sum(g => g.Score);
                float[] box = Enumerable.Range(0, 4).Select(k => group.Sum(g => g.Score * g.Box[k]) / total).ToArray();
                var keypoints = new float[4][];
                for (int j = 0; j < 4; j++)
                    keypoints[j] = new[]
                    {
                        (group.Sum(g => g.Score * g.Keypoints[j][0]) / total - pl) / sx,
                        (group.Sum(g => g.Score * g.Keypoints[j][1]) / total - pt) / sy
                    };
                result.Add(new PoseDetection
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
        /// The region of a body: centred on the middle of the hips (keypoint 0), twice the distance to keypoint 1 across (as a square
        /// in pixels) and 1.25 times that, turned so the line from the first keypoint to the second points up.
        /// </summary>
        public static HandRect RectOf(PoseDetection detection, int imageWidth, int imageHeight)
        {
            if (detection == null) throw new ArgumentNullException(nameof(detection));
            return RectOf(detection.Keypoints[0], detection.Keypoints[1], imageWidth, imageHeight);
        }

        /// <summary>The region from the two alignment points (the hip centre and a point on the circle round the body), as fractions of the image.</summary>
        public static HandRect RectOf(float[] center, float[] scale, int imageWidth, int imageHeight)
        {
            double x0 = center[0] * imageWidth, y0 = center[1] * imageHeight;
            double x1 = scale[0] * imageWidth, y1 = scale[1] * imageHeight;
            double size = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 2 * 1.25;
            double rotation = Math.PI / 2 - Math.Atan2(-(y1 - y0), x1 - x0);
            rotation -= 2 * Math.PI * Math.Floor((rotation + Math.PI) / (2 * Math.PI));
            return new HandRect
            {
                CenterX = center[0],
                CenterY = center[1],
                Width = (float)(size / imageWidth),
                Height = (float)(size / imageHeight),
                Rotation = (float)rotation
            };
        }

        /// <summary>
        /// Run the landmark model on the filled <see cref="LandmarkInput"/> (the region <paramref name="rect"/> warped to 256 x 256).
        /// Null if the model sees no body there (presence under <paramref name="minPresence"/>).
        /// </summary>
        /// <param name="detection">The detection the region came from (kept in the result)</param>
        /// <param name="rect">The region</param>
        /// <param name="minPresence">The least presence</param>
        /// <param name="refineWithHeatmap">Move each landmark to the weighted centre of the model's heatmap around it, as MediaPipe's pose
        /// landmarker does (<see cref="RefineWithHeatmap"/>); off by default, which keeps this tracker's earlier results</param>
        public Pose Read(PoseDetection detection, HandRect rect, float minPresence = 0.5f, bool refineWithHeatmap = false)
        {
            CheckActive();
            _landmarks.Run(_landmarkIn, _landmarkOut);
            float[] raw = _landmarkOut[_landmarkOrder[0]].Read<float>();
            float presence = _landmarkOut[_landmarkOrder[1]].Read<float>()[0];   // already a probability: the model applies the sigmoid
            float[] world = _landmarkOut[_landmarkOrder[4]].Read<float>();
            if (!(presence >= minPresence)) return null;
            if (refineWithHeatmap)
                raw = RefineWithHeatmap(raw, _landmarkOut[_landmarkOrder[3]].Read<float>());
            return new Pose
            {
                Detection = detection,
                Rect = rect,
                Presence = presence,
                Landmarks = ProjectLandmarks(raw, rect, out float[][] auxiliary),
                WorldLandmarks = RotateWorld(world, rect.Rotation),
                AuxiliaryLandmarks = auxiliary
            };
        }

        /// <summary>
        /// The landmark model's 39 x (x, y, z in 256-pixel units, visibility and presence as logits) as 33 x (x, y, z, visibility,
        /// presence) with x and y as fractions of the image (through the region's rotation) and z scaled to the region, and the 6
        /// auxiliary landmarks (the first two are the hip centre and the scale point, the alignment points of the next frame's region: MediaPipe
        /// splits off landmarks 33-34 for tracking, and the holistic video mode's results match with them).
        /// </summary>
        public static float[][] ProjectLandmarks(float[] raw, HandRect rect, out float[][] auxiliary)
        {
            if (raw == null || raw.Length != OutputLandmarks * 5) throw new ArgumentException("Expected 195 landmark values.");
            double c = Math.Cos(rect.Rotation), s = Math.Sin(rect.Rotation);
            var all = new float[OutputLandmarks][];
            for (int i = 0; i < OutputLandmarks; i++)
            {
                double x = raw[5 * i] / LandmarkSize - 0.5, y = raw[5 * i + 1] / LandmarkSize - 0.5;
                all[i] = new[]
                {
                    (float)((c * x - s * y) * rect.Width + rect.CenterX),
                    (float)((s * x + c * y) * rect.Height + rect.CenterY),
                    (float)(raw[5 * i + 2] / LandmarkSize * rect.Width),
                    Sigmoid(raw[5 * i + 3]),
                    Sigmoid(raw[5 * i + 4])
                };
            }
            auxiliary = all.Skip(LandmarkCount).ToArray();
            return all.Take(LandmarkCount).ToArray();
        }

        private static float Sigmoid(float v) => (float)(1 / (1 + Math.Exp(-Math.Max(-100f, Math.Min(100f, v)))));

        /// <summary>
        /// MediaPipe's RefineLandmarksFromHeatmapCalculator with the pose landmarker's settings (kernel 7, least confidence 0.5): each of the
        /// 39 landmarks (x, y in the 256-pixel crop) moves to the confidence-weighted mean position of the heatmap's 7 x 7 window around it
        /// (64 x 64 cells, the sigmoid of the model's values, the window cut at the edges), if the window's highest confidence is at least
        /// 0.5. A landmark outside the heatmap is kept. Returns a new array; z, visibility and presence are unchanged.
        /// </summary>
        /// <param name="raw">The landmark model's 39 x 5 values</param>
        /// <param name="heatmap">Its heatmap, [1, 64, 64, 39] in HWC order</param>
        /// <param name="kernelSize">The window's size</param>
        /// <param name="minConfidence">The least highest confidence in the window for the landmark to move</param>
        public static float[] RefineWithHeatmap(float[] raw, float[] heatmap, int kernelSize = 7, float minConfidence = 0.5f)
        {
            const int cells = 64;
            if (raw == null || raw.Length != OutputLandmarks * 5) throw new ArgumentException("Expected 195 landmark values.");
            if (heatmap == null || heatmap.Length != cells * cells * OutputLandmarks) throw new ArgumentException("Expected a 64 x 64 x 39 heatmap.");
            float[] result = (float[])raw.Clone();
            int offset = (kernelSize - 1) / 2;
            for (int i = 0; i < OutputLandmarks; i++)
            {
                // C++'s int conversion truncates toward zero, as here: a point just left of or above the crop still counts as cell 0.
                int col = (int)(raw[5 * i] / LandmarkSize * cells), row = (int)(raw[5 * i + 1] / LandmarkSize * cells);
                if (col < 0 || col >= cells || row < 0 || row >= cells) continue;
                float sum = 0, weightedCol = 0, weightedRow = 0, max = 0;
                for (int r = Math.Max(0, row - offset); r < Math.Min(cells, row + offset + 1); r++)
                    for (int c = Math.Max(0, col - offset); c < Math.Min(cells, col + offset + 1); c++)
                    {
                        float confidence = Sigmoid(heatmap[(r * cells + c) * OutputLandmarks + i]);
                        sum += confidence;
                        max = Math.Max(max, confidence);
                        weightedCol += c * confidence;
                        weightedRow += r * confidence;
                    }
                if (max >= minConfidence && sum > 0)
                {
                    result[5 * i] = weightedCol / cells / sum * LandmarkSize;
                    result[5 * i + 1] = weightedRow / cells / sum * LandmarkSize;
                }
            }
            return result;
        }

        private static float[][] RotateWorld(float[] world, float rotation)
        {
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            var result = new float[LandmarkCount][];
            for (int i = 0; i < LandmarkCount; i++)
                result[i] = new[] { (float)(c * world[3 * i] - s * world[3 * i + 1]), (float)(s * world[3 * i] + c * world[3 * i + 1]), world[3 * i + 2] };
            return result;
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PoseTracker));
        }

        /// <summary>Release the buffers, compiled models and environment.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var set in new[] { _detectorIn, _detectorOut, _landmarkIn, _landmarkOut })
                if (set != null) foreach (var b in set) b?.Dispose();
            _detector?.Dispose(); _landmarks?.Dispose();
            _detectorModel?.Dispose(); _landmarkModel?.Dispose();
            _environment?.Dispose();
        }
    }
}
