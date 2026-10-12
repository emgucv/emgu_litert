//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Inception (299x299, float), retrained to recognize flower species, run with LiteRT's CompiledModel API. The
    /// counterpart of Emgu.TF.Lite.Models.Inception.
    /// </summary>
    public class Inception : CompiledImageModel
    {
        /// <summary>
        /// Download the model if it isn't already, and compile it.
        /// </summary>
        /// <param name="modelFile">The .tflite model, or null for the flower-retrained Inception</param>
        /// <param name="labelFile">The labels, or null for its flower labels</param>
        /// <param name="accelerators">The hardware accelerators to compile the model for</param>
        /// <returns>Async task</returns>
        public Task Init(
            DownloadableFile modelFile = null,
            DownloadableFile labelFile = null,
            HwAccelerators accelerators = HwAccelerators.Cpu)
        {
            String defaultLocalSubfolder = "Inception";
            if (modelFile == null)
            {
                modelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/inception_flower_retrain/optimized_graph.tflite",
                    defaultLocalSubfolder,
                    "9E4F5BF63CC7975EB17ECA1398C3046AC39451F462976750258CE7072BB0ECCD");
            }
            if (labelFile == null)
            {
                labelFile = new DownloadableFile(
                    "https://github.com/emgucv/models/raw/master/inception_flower_retrain/output_labels.txt",
                    defaultLocalSubfolder,
                    "298454B11DBEE503F0303367F3714D449855071DF9ECAC16AB0A01A0A7377DB6");
            }
            return InitModel(modelFile, labelFile, accelerators);
        }

        /// <summary>
        /// Load the image file, run it through the model and return the recognition results.
        /// </summary>
        /// <param name="imageFile">The image file</param>
        /// <param name="width">The input width</param>
        /// <param name="height">The input height</param>
        /// <param name="mean">Subtracted from each pixel value</param>
        /// <param name="scale">Each pixel value (after subtracting the mean) is multiplied by this</param>
        /// <param name="flipUpsideDown">Whether to flip the image upside down</param>
        /// <param name="swapBR">Whether to swap the blue and red channels</param>
        /// <returns>The recognition results, the most likely first</returns>
        public RecognitionResult[] Recognize(String imageFile, int width = 299, int height = 299, float mean = 0.0f,
            float scale = 1.0f / 255.0f, bool flipUpsideDown = false, bool swapBR = true)
        {
            ReadImageFileToInput<float>(imageFile, height, width, mean, scale, flipUpsideDown, swapBR);
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
