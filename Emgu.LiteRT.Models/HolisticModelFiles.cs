// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.IO.Compression;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// MediaPipe's holistic landmarker models: Google's holistic_landmarker.task (float16, 13.7 MB, models dated 2023-10-24) is a zip of seven
    /// models - the pose detector and the lite pose landmark model, the hand re-crop model and the hand landmark model, BlazeFace (short range),
    /// Face Mesh V2 at 192 px and the blendshape model. Apache-2.0 per the BlazePose, Face Mesh V2 and hand tracking model cards (the re-crop
    /// model has no card of its own). The bundle is not copied to Emgu's server: it is downloaded from Google's, checked against its SHA-256 and
    /// unpacked. Every model has static shapes, so nothing is patched (HOLISTIC.md).
    /// </summary>
    public static class HolisticModelFiles
    {
        /// <summary>Where Google serves the bundle (version 1; "latest" serves the same file)</summary>
        public const string Url = "https://storage.googleapis.com/mediapipe-models/holistic_landmarker/holistic_landmarker/float16/1/holistic_landmarker.task";
        /// <summary>Its SHA-256 (upper case)</summary>
        public const string Sha256 = "E2DAB61191E2DCD0A15F943D8E3ED1DCE13C82DFA597B9DD39F562975A50C3F8";
        /// <summary>Its size in bytes</summary>
        public const long Size = 13683609;

        /// <summary>The names of the seven model files, in the order <see cref="HolisticTracker"/> takes them.</summary>
        public static readonly string[] ModelNames =
        {
            "pose_detector.tflite", "pose_landmarks_detector.tflite", "hand_roi_refinement.tflite", "hand_landmarks_detector.tflite",
            "face_detector.tflite", "face_landmarks_detector.tflite", "face_blendshapes.tflite"
        };

        /// <summary>Unpack the seven models of holistic_landmarker.task into a folder (files already there are kept) and return their paths.</summary>
        /// <param name="bundlePath">holistic_landmarker.task</param>
        /// <param name="folder">Where to put the .tflite files: a folder of their own, since other bundles use the same file names</param>
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
