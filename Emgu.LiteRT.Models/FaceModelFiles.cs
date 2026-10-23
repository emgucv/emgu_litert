// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.IO.Compression;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// MediaPipe's face landmarker models: Google's face_landmarker.task (float16, 3.8 MB) is a zip of face_detector.tflite (BlazeFace
    /// short range, 128 px), face_landmarks_detector.tflite (Face Mesh V2, 478 landmarks) and face_blendshapes.tflite (Blendshape V2, 52
    /// expression scores). Apache-2.0 per their model cards. The bundle is not copied to Emgu's server: it is downloaded from Google's,
    /// checked against its SHA-256 and unpacked, the landmark model with its dynamic batch dimension set to 1 (FACE.md).
    /// </summary>
    public static class FaceModelFiles
    {
        /// <summary>Where Google serves the bundle</summary>
        public const string Url = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task";
        /// <summary>Its SHA-256 (upper case)</summary>
        public const string Sha256 = "64184E229B263107BC2B804C6625DB1341FF2BB731874B0BCC2FE6544E0BC9FF";
        /// <summary>Its size in bytes</summary>
        public const long Size = 3758596;

        /// <summary>The names of the three model files, in the order the tracker takes them.</summary>
        public static readonly string[] ModelNames = { "face_detector.tflite", "face_landmarks_detector.tflite", "face_blendshapes.tflite" };

        /// <summary>
        /// Unpack the three models of face_landmarker.task into a folder (files already there are kept) and return their paths. The landmark
        /// model's dynamic batch dimension is set to 1 on the way (<see cref="WithStaticBatch"/>).
        /// </summary>
        /// <param name="bundlePath">face_landmarker.task</param>
        /// <param name="folder">Where to put the .tflite files</param>
        public static string[] Extract(string bundlePath, string folder)
        {
            Directory.CreateDirectory(folder);
            var paths = new string[ModelNames.Length];
            for (int i = 0; i < paths.Length; i++)
                paths[i] = Path.Combine(folder, ModelNames[i]);
            if (Array.TrueForAll(paths, File.Exists))
                return paths;
            using (var bundle = ZipFile.OpenRead(bundlePath))
                for (int i = 0; i < paths.Length; i++)
                {
                    ZipArchiveEntry model = bundle.GetEntry(ModelNames[i]) ?? throw new InvalidDataException(ModelNames[i] + " isn't in " + Path.GetFileName(bundlePath) + ".");
                    byte[] bytes;
                    using (var source = model.Open())
                    using (var memory = new MemoryStream())
                    {
                        source.CopyTo(memory);
                        bytes = memory.ToArray();
                    }
                    // The landmark model's tensors have a dynamic batch ([-1, 256, 256, 3] and so on), which CompiledModel can't size.
                    if (i == 1)
                        bytes = WithStaticBatch(bytes, out _);
                    string temporary = paths[i] + ".part";
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(paths[i])) File.Delete(paths[i]);
                    File.Move(temporary, paths[i]);
                }
            return paths;
        }

        /// <summary>
        /// A model with its dynamic batch dimension set to 1: in the tensors' shape signatures, each int vector of 2 to 5 elements that starts
        /// with -1 followed by sizes from 1 to 4096 gets 1 in place of the -1. (<see cref="HandModelFiles.WithStaticBatch"/> does the same
        /// for the gesture classifier's two-element shapes.) Nothing else changes: the patched landmark model's results equal the
        /// original's in the interpreter, bit for bit.
        /// </summary>
        /// <param name="model">The .tflite file's bytes</param>
        /// <param name="patched">How many vectors were changed (0 if the model has no dynamic batch)</param>
        public static byte[] WithStaticBatch(byte[] model, out int patched)
        {
            patched = 0;
            byte[] result = (byte[])model.Clone();
            int i = 0;
            while (i + 8 <= result.Length)
            {
                int length = BitConverter.ToInt32(result, i);
                if (length >= 2 && length <= 5 && i + 4 + 4 * length <= result.Length && BitConverter.ToInt32(result, i + 4) == -1)
                {
                    bool sizes = true;
                    for (int k = 1; k < length && sizes; k++)
                    {
                        int size = BitConverter.ToInt32(result, i + 4 + 4 * k);
                        sizes = size > 0 && size <= 4096;
                    }
                    if (sizes)
                    {
                        result[i + 4] = 1; result[i + 5] = 0; result[i + 6] = 0; result[i + 7] = 0;
                        patched++;
                        i += 4 + 4 * length;
                        continue;
                    }
                }
                // Flatbuffer vectors are 4-byte aligned.
                i += i % 4 == 0 ? 4 : 1;
            }
            return result;
        }
    }
}
