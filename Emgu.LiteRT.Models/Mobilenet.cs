//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// MobileNet (v1, 224x224, float) for image classification (1001 ImageNet classes), run with LiteRT's
    /// CompiledModel API. The counterpart of Emgu.TF.Lite.Models.Mobilenet.
    /// </summary>
    public class Mobilenet : CompiledImageModel
    {
        /// <summary>
        /// Download the model if it isn't already, and compile it.
        /// </summary>
        /// <param name="modelFile">The .tflite model, or null for MobileNet v1 1.0 224 (float)</param>
        /// <param name="labelFile">The labels, or null for its ImageNet labels</param>
        /// <param name="accelerators">The hardware accelerators to compile the model for</param>
        /// <returns>Async task</returns>
        public Task Init(
            DownloadableFile modelFile = null,
            DownloadableFile labelFile = null,
            HwAccelerators accelerators = HwAccelerators.Cpu)
        {
            String defaultLocalSubfolder = "Mobilenet";
            if (modelFile == null)
            {
                modelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/mobilenet_v1_1.0_224_float_2017_11_08/mobilenet_v1_1.0_224.tflite",
                    defaultLocalSubfolder,
                    "FDACE547B17907FA22821F4898F2AF49DCE7787FE688AD7C5D8D0220F3781C65");
            }
            if (labelFile == null)
            {
                labelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/mobilenet_v1_1.0_224_float_2017_11_08/labels.txt",
                    defaultLocalSubfolder,
                    "536FEACC519DE3D418DE26B2EFFB4D75694A8C4C0063E36499A46FA8061E2DA9");
            }
            return InitModel(modelFile, labelFile, accelerators);
        }

        /// <summary>
        /// Load the image file, run it through MobileNet and return the recognition results.
        /// </summary>
        /// <param name="imageFile">The image file</param>
        /// <returns>The recognition results, the most likely first</returns>
        public RecognitionResult[] Recognize(String imageFile)
        {
            ReadImageFileToInput<float>(imageFile, 224, 224, 128.0f, 1.0f / 128.0f);
            return Invoke();
        }

        /// <summary>
        /// Run the model on what is in the input buffer and return the recognition results.
        /// </summary>
        /// <returns>The recognition results, the most likely first</returns>
        public RecognitionResult[] Invoke()
        {
            Run();
            return SortResults(ReadOutput<float>(0));
        }

        /// <summary>
        /// Sort the results from the most likely to the least likely.
        /// </summary>
        /// <param name="probabilities">The probabilities of the classes (the output)</param>
        /// <returns>The recognition results, sorted by likelihood</returns>
        public RecognitionResult[] SortResults(float[] probabilities)
        {
            if (probabilities == null)
                return null;
            RecognitionResult[] results = new RecognitionResult[probabilities.Length];
            for (int i = 0; i < probabilities.Length; i++)
                results[i] = new RecognitionResult(Labels[i], probabilities[i]);
            Array.Sort(results, (a, b) => -a.Probability.CompareTo(b.Probability));
            return results;
        }

        /// <summary>
        /// A class and its probability
        /// </summary>
        public class RecognitionResult
        {
            /// <summary>
            /// Create a recognition result
            /// </summary>
            /// <param name="label">The label</param>
            /// <param name="probability">The probability</param>
            public RecognitionResult(String label, double probability)
            {
                Label = label;
                Probability = probability;
            }

            /// <summary>
            /// The label
            /// </summary>
            public String Label;

            /// <summary>
            /// The probability
            /// </summary>
            public double Probability;
        }
    }
}
