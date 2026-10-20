// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.IO.Compression;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// MediaPipe's pose landmarker models: Google's pose_landmarker_{lite,full,heavy}.task is a zip of pose_detector.tflite (the same
    /// 2.9 MB detector in all three) and pose_landmarks_detector.tflite (2.8, 6.4 or 27.7 MB). The bundles are not copied to Emgu's server:
    /// each is downloaded from Google's, checked against its SHA-256 (Apache-2.0 per the model card) and unpacked.
    /// </summary>
    public static class PoseModelFiles
    {
        /// <summary>One of the three bundles</summary>
        public sealed class Variant
        {
            /// <summary>lite, full or heavy</summary>
            public string Name { get; internal set; }
            /// <summary>Where Google serves it</summary>
            public string Url { get; internal set; }
            /// <summary>Its SHA-256 (upper case)</summary>
            public string Sha256 { get; internal set; }
            /// <summary>Its size in bytes</summary>
            public long Size { get; internal set; }
        }

        private static Variant Make(string name, string sha256, long size) => new Variant
        {
            Name = name, Sha256 = sha256, Size = size,
            Url = "https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_" + name + "/float16/latest/pose_landmarker_" + name + ".task"
        };

        /// <summary>Lite: 5.8 MB, about 44 FPS on a Pixel 3's CPU (its model card)</summary>
        public static readonly Variant Lite = Make("lite", "59929E1D1EE95287735DDD833B19CF4AC46D29BC7AFDDBBF6753C459690D574A", 5777746);
        /// <summary>Full: 9.4 MB, more accurate</summary>
        public static readonly Variant Full = Make("full", "4EAA5EB7A98365221087693FCC286334CF0858E2EB6E15B506AA4A7ECDCEC4AD", 9398198);
        /// <summary>Heavy: 30.7 MB, the most accurate and the slowest</summary>
        public static readonly Variant Heavy = Make("heavy", "64437AF838A65D18E5BA7A0D39B465540069BC8AAE8308DE3E318AAD31FCBC7B", 30664242);

        /// <summary>The names of the two model files, in the order the tracker takes them.</summary>
        public static readonly string[] ModelNames = { "pose_detector.tflite", "pose_landmarks_detector.tflite" };

        /// <summary>Unpack the two models of a pose_landmarker .task into a folder (files already there are kept) and return their paths.</summary>
        /// <param name="bundlePath">pose_landmarker_lite.task and so on</param>
        /// <param name="folder">Where to put the .tflite files: one folder for each variant</param>
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
                    string temporary = paths[i] + ".part";
                    using (var target = File.Create(temporary))
                    using (var source = model.Open())
                        source.CopyTo(target);
                    if (File.Exists(paths[i])) File.Delete(paths[i]);
                    File.Move(temporary, paths[i]);
                }
            return paths;
        }
    }
}
