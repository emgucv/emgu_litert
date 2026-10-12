//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Base class of the COCO SSD MobileNet object detectors (quantized, uint8 input), run with LiteRT's CompiledModel
    /// API: CocoSsdMobilenetV1 and CocoSsdMobilenetV3. The counterpart of Emgu.TF.Lite.Models.CocoSsdMobilenet.
    /// </summary>
    public abstract class CocoSsdMobilenet : CompiledImageModel
    {
        /// <summary>
        /// Download the model if it isn't already, and compile it.
        /// </summary>
        /// <param name="modelFile">The .tflite model, or null for the version's default</param>
        /// <param name="labelFile">The labels, or null for the version's default</param>
        /// <param name="accelerators">The hardware accelerators to compile the model for</param>
        /// <returns>Async task</returns>
        public abstract Task Init(
            DownloadableFile modelFile = null,
            DownloadableFile labelFile = null,
            HwAccelerators accelerators = HwAccelerators.Cpu);

        /// <summary>
        /// Download and compile the given model and labels.
        /// </summary>
        protected Task InitCocoSsd(DownloadableFile modelFile, DownloadableFile labelFile, HwAccelerators accelerators)
        {
            return InitModel(modelFile, labelFile, accelerators);
        }

        // The input's height and width, from its shape [1, height, width, 3].
        private int InputHeight
        {
            get { return InputTensorType.Dimensions[1]; }
        }

        private int InputWidth
        {
            get { return InputTensorType.Dimensions[2]; }
        }

        /// <summary>
        /// Detect objects in an image file.
        /// </summary>
        /// <param name="imageFile">The image file</param>
        /// <param name="scoreThreshold">If positive, only the objects with a higher score are returned</param>
        /// <returns>The objects detected</returns>
        public RecognitionResult[] Recognize(String imageFile, float scoreThreshold = 0.0f)
        {
            ReadImageFileToInput<byte>(imageFile, InputHeight, InputWidth, 0.0f, 1.0f);
            Run();
            return GetResults(scoreThreshold);
        }

#if __IOS__
        /// <summary>
        /// Detect objects in an image.
        /// </summary>
        /// <param name="image">The image</param>
        /// <param name="scoreThreshold">If positive, only the objects with a higher score are returned</param>
        /// <returns>The objects detected</returns>
        public RecognitionResult[] Recognize(UIKit.UIImage image, float scoreThreshold = 0.0f)
        {
            ReadImageToInput<byte>(image, InputHeight, InputWidth, 0.0f, 1.0f);
            Run();
            return GetResults(scoreThreshold);
        }
#endif

        /// <summary>
        /// The objects detected in the last run.
        /// </summary>
        /// <param name="scoreThreshold">If positive, only the objects with a higher score are returned</param>
        /// <returns>The objects detected</returns>
        public RecognitionResult[] GetResults(float scoreThreshold)
        {
            // Outputs: locations [1, N, 4] (y0, x0, y1, x1, normalized), classes [1, N], scores [1, N], count [1].
            float[] locations = ReadOutput<float>(0);
            float[] classes = ReadOutput<float>(1);
            float[] scores = ReadOutput<float>(2);
            int numDetections = Math.Min((int)Math.Round(ReadOutput<float>(3)[0]), scores.Length);

            // The label file starts with a background class, the model's classes with the first object class.
            const int labelOffset = 1;
            List<RecognitionResult> results = new List<RecognitionResult>();
            for (int i = 0; i < numDetections; i++)
            {
                if (scoreThreshold > 0.0f && scores[i] < scoreThreshold)
                    continue;
                RecognitionResult r = new RecognitionResult();
                r.Class = (int)Math.Round(classes[i]);
                r.Label = Labels[r.Class + labelOffset];
                r.Score = scores[i];
                float y0 = locations[i * 4], x0 = locations[i * 4 + 1], y1 = locations[i * 4 + 2], x1 = locations[i * 4 + 3];
                r.Rectangle = new float[] { x0, y0, x1, y1 };
                results.Add(r);
            }
            return results.ToArray();
        }

        /// <summary>
        /// An object detected
        /// </summary>
        public class RecognitionResult
        {
            /// <summary>
            /// The bounding box (x0, y0, x1, y1), normalized to [0, 1] of the image's width and height
            /// </summary>
            public float[] Rectangle;

            /// <summary>
            /// The object label
            /// </summary>
            public String Label;

            /// <summary>
            /// The score
            /// </summary>
            public double Score;

            /// <summary>
            /// The class index
            /// </summary>
            public int Class;
        }
    }
}
