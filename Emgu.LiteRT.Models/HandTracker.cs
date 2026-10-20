// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// The rotated square around a hand, found from a palm detection: where the landmark model is run. Coordinates are
    /// fractions of the image; <see cref="Rotation"/> is in radians (0 for a hand with its fingers up).
    /// </summary>
    public sealed class HandRect
    {
        /// <summary>Centre x, as a fraction of the image width</summary>
        public float CenterX { get; set; }
        /// <summary>Centre y, as a fraction of the image height</summary>
        public float CenterY { get; set; }
        /// <summary>Width, as a fraction of the image width</summary>
        public float Width { get; set; }
        /// <summary>Height, as a fraction of the image height (the same length as the width in pixels)</summary>
        public float Height { get; set; }
        /// <summary>The rotation in radians</summary>
        public float Rotation { get; set; }

        /// <summary>
        /// Three corners of the rectangle in image pixel coordinates - top left, top right, bottom right - where the 224 x 224
        /// landmark input's top left, top right and bottom right corners come from: an affine warp's source points.
        /// </summary>
        /// <param name="imageWidth">The image's width in pixels</param>
        /// <param name="imageHeight">The image's height in pixels</param>
        /// <returns>x0, y0, x1, y1, x2, y2</returns>
        public float[] SourceCorners(int imageWidth, int imageHeight)
        {
            double c = Math.Cos(Rotation), s = Math.Sin(Rotation);
            double pw = Width * imageWidth, ph = Height * imageHeight;
            double cx = CenterX * imageWidth, cy = CenterY * imageHeight;
            float[] Corner(double u, double v) => new[]
            {
                (float)(cx + c * u * pw - s * v * ph),
                (float)(cy + s * u * pw + c * v * ph)
            };
            return Corner(-0.5, -0.5).Concat(Corner(0.5, -0.5)).Concat(Corner(0.5, 0.5)).ToArray();
        }
    }

    /// <summary>A palm found in an image.</summary>
    public sealed class Palm
    {
        /// <summary>The detector's score, 0..1</summary>
        public float Score { get; internal set; }
        /// <summary>Normalized x0, y0, x1, y1 in the image</summary>
        public float[] Box { get; internal set; }
        /// <summary>The detector's 7 keypoints (wrist, finger bases and tips), normalized x, y in the image</summary>
        public float[][] Keypoints { get; internal set; }
    }

    /// <summary>A hand: its 21 landmarks and what the gesture recognizer made of them.</summary>
    public sealed class Hand
    {
        /// <summary>The palm it was found from</summary>
        public Palm Palm { get; internal set; }
        /// <summary>The region the landmarks were read from</summary>
        public HandRect Rect { get; internal set; }
        /// <summary>The landmark model's confidence that there is a hand, 0..1</summary>
        public float Presence { get; internal set; }
        /// <summary>The model's probability that it is a right hand (for an image that isn't mirrored)</summary>
        public float RightHandScore { get; internal set; }
        /// <summary>"Right" or "Left"</summary>
        public string Handedness => RightHandScore >= 0.5f ? "Right" : "Left";
        /// <summary>The 21 landmarks: x and y as fractions of the image, z in the same scale as x (smaller: nearer the camera)</summary>
        public float[][] Landmarks { get; internal set; }
        /// <summary>The 21 landmarks in metres from the hand's centre, x and y in the image's orientation</summary>
        public float[][] WorldLandmarks { get; internal set; }
        /// <summary>The gesture: one of <see cref="HandTracker.GestureLabels"/></summary>
        public string Gesture { get; internal set; }
        /// <summary>The gesture's score, 0..1</summary>
        public float GestureScore { get; internal set; }
    }

    /// <summary>
    /// MediaPipe's hand tracking on LiteRT: the palm detector (192 x 192) finds hands, the landmark model (224 x 224) reads 21
    /// landmarks from the rotated square around each, and MediaPipe's gesture embedder and classifier name the gesture (closed
    /// fist, open palm, pointing up, thumb down, thumb up, victory, "I love you", or none). The models are the ones in
    /// MediaPipe's gesture_recognizer.task (Apache-2.0). The pipeline follows MediaPipe's hand_landmarker and
    /// gesture_recognizer graphs: SSD anchors and weighted NMS for the palms, the rect made from the wrist-to-middle-finger
    /// direction, the landmarks projected back, and the gesture features not rotated (MediaPipe's classifier takes the hand as it
    /// is in the image: a sideways hand is "none"). The host fills <see cref="DetectorInput"/> and <see cref="LandmarkInput"/>
    /// (the images are cut and scaled with OpenCV or the platform, not here). Calls are serialized by the host.
    /// </summary>
    public sealed class HandTracker : IDisposable
    {
        /// <summary>The palm detector's square input size</summary>
        public const int DetectorSize = 192;
        /// <summary>The landmark model's square input size</summary>
        public const int LandmarkSize = 224;
        /// <summary>Landmarks per hand</summary>
        public const int LandmarkCount = 21;
        private const int AnchorCount = 2016;

        /// <summary>The gestures, in the classifier's order</summary>
        public static IReadOnlyList<string> GestureLabels { get; } = Array.AsReadOnly(
            new[] { "None", "Closed_Fist", "Open_Palm", "Pointing_Up", "Thumb_Down", "Thumb_Up", "Victory", "ILoveYou" });

        private static float[][] _anchors;

        private readonly Environment _environment;
        private readonly Model _detectorModel, _landmarkModel, _embedderModel, _classifierModel;
        private readonly CompiledModel _detector, _landmarks, _embedder, _classifier;
        private readonly TensorBuffer[] _detectorIn, _detectorOut, _landmarkIn, _landmarkOut, _embedderIn, _embedderOut, _classifierIn, _classifierOut;
        private readonly int[] _landmarkOrder, _embedderOrder, _detectorOrder;
        private bool _disposed;

        /// <summary>Compile the four models. Compilation should run off the UI thread.</summary>
        /// <param name="detectorPath">hand_detector.tflite</param>
        /// <param name="landmarkPath">hand_landmarks_detector.tflite</param>
        /// <param name="embedderPath">gesture_embedder.tflite</param>
        /// <param name="classifierPath">canned_gesture_classifier.tflite</param>
        /// <param name="numThreads">CPU threads</param>
        /// <param name="accelerators">What the detector and the landmark model may use (the two small gesture models always use the CPU)</param>
        /// <param name="gpuPrecision">The GPU's precision (FP32 is the safe choice)</param>
        public HandTracker(string detectorPath, string landmarkPath, string embedderPath, string classifierPath,
            int numThreads = 2, HwAccelerators accelerators = HwAccelerators.Cpu, GpuPrecision? gpuPrecision = null)
        {
            if (numThreads < 1) throw new ArgumentOutOfRangeException(nameof(numThreads));
            try
            {
                _environment = new Environment();
                _detectorModel = new Model(_environment, detectorPath);
                _landmarkModel = new Model(_environment, landmarkPath);
                _embedderModel = new Model(_environment, embedderPath);
                _classifierModel = new Model(_environment, classifierPath);
                CheckShape(_detectorModel, new[] { 1, DetectorSize, DetectorSize, 3 }, 2, "hand_detector");
                CheckShape(_landmarkModel, new[] { 1, LandmarkSize, LandmarkSize, 3 }, 4, "hand_landmarks_detector");
                CheckShape(_embedderModel, new[] { 1, LandmarkCount, 3 }, 1, "gesture_embedder");
                CheckShape(_classifierModel, new[] { 1, 128 }, 1, "canned_gesture_classifier");
                // The detector's outputs: regressors [1,2016,18] and logits [1,2016,1] - by shape.
                var detectorSignature = _detectorModel.GetSignature(0);
                _detectorOrder = new int[2];
                for (int i = 0; i < 2; i++)
                    _detectorOrder[detectorSignature.GetOutputTensorType(i).Dimensions[2] == 18 ? 0 : 1] = i;
                // The landmark model's: landmarks, presence, handedness and world landmarks, as Identity .. Identity_3.
                _landmarkOrder = OrderByName(_landmarkModel.GetSignature(0).OutputNames, 4);
                // The embedder's inputs: hand, handedness, world_hand.
                _embedderOrder = EmbedderInputOrder(_embedderModel.GetSignature(0).InputNames);
                _detector = Compile(_detectorModel, numThreads, accelerators, gpuPrecision);
                _landmarks = Compile(_landmarkModel, numThreads, accelerators, gpuPrecision);
                _embedder = Compile(_embedderModel, 1, HwAccelerators.Cpu, null);
                _classifier = Compile(_classifierModel, 1, HwAccelerators.Cpu, null);
                _detectorIn = _detector.CreateInputBuffers();
                _detectorOut = _detector.CreateOutputBuffers();
                _landmarkIn = _landmarks.CreateInputBuffers();
                _landmarkOut = _landmarks.CreateOutputBuffers();
                _embedderIn = _embedder.CreateInputBuffers();
                _embedderOut = _embedder.CreateOutputBuffers();
                _classifierIn = _classifier.CreateInputBuffers();
                _classifierOut = _classifier.CreateOutputBuffers();
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

        // The index of output Identity, Identity_1, ... in the signature (by name where the names say it, else in order).
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

        // The index of the embedder's inputs hand [0], handedness [1] and world_hand [2].
        private static int[] EmbedderInputOrder(string[] names)
        {
            var order = new[] { 0, 1, 2 };
            if (names == null || names.Length != 3) return order;
            int world = Array.FindIndex(names, n => n != null && n.IndexOf("world", StringComparison.OrdinalIgnoreCase) >= 0);
            int handed = Array.FindIndex(names, n => n != null && n.IndexOf("handed", StringComparison.OrdinalIgnoreCase) >= 0);
            int hand = Array.FindIndex(names, n => n != null && n.Equals("hand", StringComparison.OrdinalIgnoreCase));
            return world >= 0 && handed >= 0 && hand >= 0 ? new[] { hand, handed, world } : order;
        }

        /// <summary>Whether LiteRT reports both vision models fully on the requested accelerator.</summary>
        public bool IsFullyAccelerated
        {
            get { CheckActive(); return _detector.IsFullyAccelerated && _landmarks.IsFullyAccelerated; }
        }

        /// <summary>The palm detector's input: the image letterboxed into 192 x 192 float32 RGB, 0..1.</summary>
        public TensorBuffer DetectorInput
        {
            get { CheckActive(); return _detectorIn[0]; }
        }

        /// <summary>The landmark model's input: the hand's region warped into 224 x 224 float32 RGB, 0..1.</summary>
        public TensorBuffer LandmarkInput
        {
            get { CheckActive(); return _landmarkIn[0]; }
        }

        /// <summary>
        /// How an image of this size goes into the detector's square input: scaled to fit and centred (the padding is black).
        /// </summary>
        public static void Letterbox(int imageWidth, int imageHeight, out int width, out int height, out int left, out int top)
        {
            double scale = Math.Min((double)DetectorSize / imageWidth, (double)DetectorSize / imageHeight);
            width = Math.Max(1, (int)Math.Round(imageWidth * scale));
            height = Math.Max(1, (int)Math.Round(imageHeight * scale));
            left = (DetectorSize - width) / 2;
            top = (DetectorSize - height) / 2;
        }

        /// <summary>Run the palm detector on the filled <see cref="DetectorInput"/>.</summary>
        /// <param name="imageWidth">The width of the image that was letterboxed</param>
        /// <param name="imageHeight">The height of the image that was letterboxed</param>
        /// <param name="threshold">The least score</param>
        /// <param name="maxHands">The most palms to return, best first</param>
        public Palm[] DetectPalms(int imageWidth, int imageHeight, float threshold = 0.5f, int maxHands = 2)
        {
            CheckActive();
            _detector.Run(_detectorIn, _detectorOut);
            float[] regressors = _detectorOut[_detectorOrder[0]].Read<float>();
            float[] logits = _detectorOut[_detectorOrder[1]].Read<float>();
            return DecodePalms(regressors, logits, imageWidth, imageHeight, threshold, 0.3f, maxHands);
        }

        /// <summary>The 2016 SSD anchor centres (x, y as fractions of the input) of the palm detector.</summary>
        public static float[][] Anchors()
        {
            if (_anchors != null) return _anchors;
            // MediaPipe's SsdAnchorsCalculator: strides 8, 16, 16, 16; layers of one stride share a grid, and each has two
            // anchors (aspect ratio 1.0 and the interpolated scale) at every cell centre. Their sizes are fixed, so only the
            // centres matter.
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
        /// Turn the detector's outputs into palms: logistic scores, boxes and keypoints from the anchors, weighted
        /// non-maximum suppression (IoU 0.3), and the letterbox padding taken out so everything is a fraction of the image.
        /// </summary>
        public static Palm[] DecodePalms(float[] regressors, float[] logits, int imageWidth, int imageHeight,
            float threshold = 0.5f, float nmsThreshold = 0.3f, int maxHands = 2)
        {
            if (regressors == null || regressors.Length != AnchorCount * 18 || logits == null || logits.Length != AnchorCount)
                throw new ArgumentException("Expected the palm detector's outputs [1,2016,18] and [1,2016,1].");
            if (imageWidth < 1 || imageHeight < 1) throw new ArgumentOutOfRangeException(nameof(imageWidth));
            float[][] anchors = Anchors();
            var candidates = new List<Palm>();
            for (int i = 0; i < AnchorCount; i++)
            {
                float logit = Math.Max(-100f, Math.Min(100f, logits[i]));
                float score = (float)(1 / (1 + Math.Exp(-logit)));
                if (!(score >= threshold)) continue;
                int o = i * 18;
                float ax = anchors[i][0], ay = anchors[i][1];
                float xc = regressors[o] / DetectorSize + ax, yc = regressors[o + 1] / DetectorSize + ay;
                float w = regressors[o + 2] / DetectorSize, h = regressors[o + 3] / DetectorSize;
                var keypoints = new float[7][];
                for (int k = 0; k < 7; k++)
                    keypoints[k] = new[] { regressors[o + 4 + 2 * k] / DetectorSize + ax, regressors[o + 5 + 2 * k] / DetectorSize + ay };
                candidates.Add(new Palm { Score = score, Box = new[] { xc - w / 2, yc - h / 2, xc + w / 2, yc + h / 2 }, Keypoints = keypoints });
            }
            candidates = candidates.OrderByDescending(c => c.Score).ToList();
            Letterbox(imageWidth, imageHeight, out int lw, out int lh, out int left, out int top);
            float pl = (float)left / DetectorSize, pt = (float)top / DetectorSize;
            float sx = 1 - 2 * pl, sy = 1 - 2 * pt;
            var result = new List<Palm>();
            while (candidates.Count > 0 && result.Count < maxHands)
            {
                Palm best = candidates[0];
                var group = new List<Palm> { best };
                var rest = new List<Palm>();
                foreach (Palm c in candidates.Skip(1))
                    (Overlap(best.Box, c.Box) > nmsThreshold ? group : rest).Add(c);
                candidates = rest;
                float total = group.Sum(g => g.Score);
                float[] box = Enumerable.Range(0, 4).Select(k => group.Sum(g => g.Score * g.Box[k]) / total).ToArray();
                var keypoints = new float[7][];
                for (int j = 0; j < 7; j++)
                    keypoints[j] = new[]
                    {
                        (group.Sum(g => g.Score * g.Keypoints[j][0]) / total - pl) / sx,
                        (group.Sum(g => g.Score * g.Keypoints[j][1]) / total - pt) / sy
                    };
                result.Add(new Palm
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
        /// The region of a hand: turned so the wrist-to-middle-finger direction points up, shifted along it, made square (in
        /// pixels) and 2.6 times the palm's size, as MediaPipe does.
        /// </summary>
        public static HandRect RectOf(Palm palm, int imageWidth, int imageHeight)
        {
            if (palm == null) throw new ArgumentNullException(nameof(palm));
            double x0 = palm.Keypoints[0][0] * imageWidth, y0 = palm.Keypoints[0][1] * imageHeight;
            double x1 = palm.Keypoints[2][0] * imageWidth, y1 = palm.Keypoints[2][1] * imageHeight;
            double rotation = Math.PI / 2 - Math.Atan2(-(y1 - y0), x1 - x0);
            rotation -= 2 * Math.PI * Math.Floor((rotation + Math.PI) / (2 * Math.PI));
            double bw = palm.Box[2] - palm.Box[0], bh = palm.Box[3] - palm.Box[1];
            double xc = (palm.Box[0] + palm.Box[2]) / 2, yc = (palm.Box[1] + palm.Box[3]) / 2;
            const double shiftX = 0.0, shiftY = -0.5;
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            xc += (imageWidth * bw * shiftX * c - imageHeight * bh * shiftY * s) / imageWidth;
            yc += (imageWidth * bw * shiftX * s + imageHeight * bh * shiftY * c) / imageHeight;
            double longSide = Math.Max(bw * imageWidth, bh * imageHeight);
            return new HandRect
            {
                CenterX = (float)xc,
                CenterY = (float)yc,
                Width = (float)(longSide / imageWidth * 2.6),
                Height = (float)(longSide / imageHeight * 2.6),
                Rotation = (float)rotation
            };
        }

        /// <summary>
        /// Run the landmark model on the filled <see cref="LandmarkInput"/> (the region <paramref name="rect"/> warped to 224 x
        /// 224) and name the gesture. Null if the model sees no hand there (presence under <paramref name="minPresence"/>).
        /// </summary>
        public Hand ReadHand(Palm palm, HandRect rect, int imageWidth, int imageHeight, float minPresence = 0.5f)
        {
            CheckActive();
            _landmarks.Run(_landmarkIn, _landmarkOut);
            float[] raw = _landmarkOut[_landmarkOrder[0]].Read<float>();
            float presence = _landmarkOut[_landmarkOrder[1]].Read<float>()[0];
            float right = _landmarkOut[_landmarkOrder[2]].Read<float>()[0];
            float[] world = _landmarkOut[_landmarkOrder[3]].Read<float>();
            if (!(presence >= minPresence)) return null;
            var hand = new Hand { Palm = palm, Rect = rect, Presence = presence, RightHandScore = right };
            hand.Landmarks = ProjectLandmarks(raw, rect);
            hand.WorldLandmarks = RotateWorld(world, rect.Rotation);
            Classify(hand, imageWidth, imageHeight);
            return hand;
        }

        /// <summary>The landmark model's 21 x (x, y, z in 224-pixel units) as fractions of the image, through the region's rotation.</summary>
        public static float[][] ProjectLandmarks(float[] raw, HandRect rect)
        {
            if (raw == null || raw.Length != LandmarkCount * 3) throw new ArgumentException("Expected 63 landmark values.");
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

        private static float[][] RotateWorld(float[] world, float rotation)
        {
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            var result = new float[LandmarkCount][];
            for (int i = 0; i < LandmarkCount; i++)
                result[i] = new[] { (float)(c * world[3 * i] - s * world[3 * i + 1]), (float)(s * world[3 * i] + c * world[3 * i + 1]), world[3 * i + 2] };
            return result;
        }

        /// <summary>
        /// The gesture embedder's features of a hand: the landmarks with the image's aspect ratio taken out, moved so the wrist
        /// is the origin and scaled by the larger extent in x or y. (MediaPipe's gesture recognizer doesn't rotate them.)
        /// </summary>
        public static float[] GestureFeatures(float[][] landmarks, int imageWidth, int imageHeight, bool world = false)
        {
            if (landmarks == null || landmarks.Length != LandmarkCount) throw new ArgumentException("Expected 21 landmarks.");
            double max = Math.Max(imageWidth, imageHeight);
            var points = new double[LandmarkCount][];
            for (int i = 0; i < LandmarkCount; i++)
                points[i] = world
                    ? new double[] { landmarks[i][0], landmarks[i][1], landmarks[i][2] }
                    : new[] { (landmarks[i][0] - 0.5) * (imageWidth / max) + 0.5, (landmarks[i][1] - 0.5) * (imageHeight / max) + 0.5, landmarks[i][2] };
            double[] origin = (double[])points[0].Clone();   // a copy: points[0] itself is moved with the others
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (double[] p in points)
            {
                for (int k = 0; k < 3; k++) p[k] -= origin[k];
                minX = Math.Min(minX, p[0]); maxX = Math.Max(maxX, p[0]);
                minY = Math.Min(minY, p[1]); maxY = Math.Max(maxY, p[1]);
            }
            double scale = Math.Max(maxX - minX, maxY - minY) + 1e-5;
            var result = new float[LandmarkCount * 3];
            for (int i = 0; i < LandmarkCount; i++)
                for (int k = 0; k < 3; k++)
                    result[3 * i + k] = (float)(points[i][k] / scale);
            return result;
        }

        private void Classify(Hand hand, int imageWidth, int imageHeight)
        {
            _embedderIn[_embedderOrder[0]].Write(GestureFeatures(hand.Landmarks, imageWidth, imageHeight));
            _embedderIn[_embedderOrder[1]].Write(new[] { hand.RightHandScore });
            _embedderIn[_embedderOrder[2]].Write(GestureFeatures(hand.WorldLandmarks, imageWidth, imageHeight, true));
            _embedder.Run(_embedderIn, _embedderOut);
            _classifierIn[0].Write(_embedderOut[0].Read<float>());
            _classifier.Run(_classifierIn, _classifierOut);
            float[] scores = _classifierOut[0].Read<float>();
            int best = 0;
            for (int i = 1; i < scores.Length; i++)
                if (scores[i] > scores[best]) best = i;
            hand.Gesture = best < GestureLabels.Count ? GestureLabels[best] : "None";
            hand.GestureScore = scores[best];
        }

        private void CheckActive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HandTracker));
        }

        /// <summary>Release the buffers, compiled models, graphs and environment.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var set in new[] { _detectorIn, _detectorOut, _landmarkIn, _landmarkOut, _embedderIn, _embedderOut, _classifierIn, _classifierOut })
                if (set != null) foreach (var b in set) b?.Dispose();
            _detector?.Dispose(); _landmarks?.Dispose(); _embedder?.Dispose(); _classifier?.Dispose();
            _detectorModel?.Dispose(); _landmarkModel?.Dispose(); _embedderModel?.Dispose(); _classifierModel?.Dispose();
            _environment?.Dispose();
        }
    }
}
