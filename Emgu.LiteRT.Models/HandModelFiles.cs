// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.IO.Compression;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// The four models of MediaPipe's gesture recognizer, and where they come from: Google's gesture_recognizer.task is a zip with
    /// hand_landmarker.task (hand_detector.tflite and hand_landmarks_detector.tflite) and hand_gesture_recognizer.task
    /// (gesture_embedder.tflite and canned_gesture_classifier.tflite), each itself a zip. The bundle is not copied to Emgu's own
    /// server: it is downloaded from Google's, checked against its SHA-256, and unpacked.
    /// </summary>
    public static class HandModelFiles
    {
        /// <summary>Google's gesture_recognizer.task, float16, version 1 (unchanged since April 2023)</summary>
        public const string BundleUrl = "https://storage.googleapis.com/mediapipe-models/gesture_recognizer/gesture_recognizer/float16/1/gesture_recognizer.task";
        /// <summary>The bundle's SHA-256 (upper case)</summary>
        public const string BundleSha256 = "97952348CF6A6A4915C2EA1496B4B37EBABC50CBBF80571435643C455F2B0482";
        /// <summary>The bundle's size in bytes</summary>
        public const long BundleSize = 8373440;

        /// <summary>The names of the four model files, in the order the tracker takes them.</summary>
        public static readonly string[] ModelNames =
        {
            "hand_detector.tflite", "hand_landmarks_detector.tflite", "gesture_embedder.tflite", "canned_gesture_classifier.tflite"
        };

        /// <summary>
        /// Unpack the four models of a gesture_recognizer.task into a folder (files already there are kept) and return their paths
        /// in the order of <see cref="ModelNames"/>.
        /// </summary>
        /// <param name="bundlePath">gesture_recognizer.task</param>
        /// <param name="folder">Where to put the .tflite files</param>
        public static string[] Extract(string bundlePath, string folder)
        {
            Directory.CreateDirectory(folder);
            var paths = new string[ModelNames.Length];
            for (int i = 0; i < ModelNames.Length; i++)
                paths[i] = Path.Combine(folder, ModelNames[i]);
            if (Array.TrueForAll(paths, File.Exists))
                return paths;
            using (var bundle = ZipFile.OpenRead(bundlePath))
            {
                for (int part = 0; part < 2; part++)
                {
                    string taskName = part == 0 ? "hand_landmarker.task" : "hand_gesture_recognizer.task";
                    ZipArchiveEntry task = bundle.GetEntry(taskName) ?? throw new InvalidDataException(taskName + " isn't in " + Path.GetFileName(bundlePath) + ".");
                    using (var stream = task.Open())
                    using (var inner = new ZipArchive(stream, ZipArchiveMode.Read))
                        for (int k = 0; k < 2; k++)
                        {
                            int index = part * 2 + k;
                            ZipArchiveEntry model = inner.GetEntry(ModelNames[index]) ?? throw new InvalidDataException(ModelNames[index] + " isn't in " + taskName + ".");
                            string temporary = paths[index] + ".part";
                            using (var target = File.Create(temporary))
                            using (var source = model.Open())
                                source.CopyTo(target);
                            // The classifier's batch dimension is dynamic, which LiteRT's CompiledModel can't run: make it 1.
                            if (index == 3)
                                File.WriteAllBytes(temporary, WithStaticBatch(File.ReadAllBytes(temporary), out _));
                            if (File.Exists(paths[index])) File.Delete(paths[index]);
                            File.Move(temporary, paths[index]);
                        }
                }
            }
            return paths;
        }

        /// <summary>
        /// The canned gesture classifier with its dynamic batch dimension set to 1: its input is [-1, 128] and its output
        /// [-1, 8] (in the tensors' shape signatures), which LiteRT's CompiledModel has no way to resize. Each two-element
        /// int vector of -1 followed by a small positive number gets 1 in place of the -1; nothing else changes, and the
        /// results are the same (compared with the interpreter).
        /// </summary>
        /// <param name="model">The .tflite file's bytes</param>
        /// <param name="patched">How many vectors were changed (0 if the model has no dynamic batch)</param>
        public static byte[] WithStaticBatch(byte[] model, out int patched)
        {
            patched = 0;
            byte[] result = (byte[])model.Clone();
            for (int i = 0; i + 12 <= result.Length; i++)
            {
                // int32 length 2, int32 -1, int32 n with 0 < n <= 4096 (little endian)
                if (result[i] == 2 && result[i + 1] == 0 && result[i + 2] == 0 && result[i + 3] == 0 &&
                    result[i + 4] == 0xFF && result[i + 5] == 0xFF && result[i + 6] == 0xFF && result[i + 7] == 0xFF &&
                    result[i + 10] == 0 && result[i + 11] == 0 && (result[i + 8] != 0 || result[i + 9] != 0) && result[i + 9] <= 0x10)
                {
                    result[i + 4] = 1; result[i + 5] = 0; result[i + 6] = 0; result[i + 7] = 0;
                    patched++;
                    i += 11;
                }
            }
            return result;
        }
    }
}
