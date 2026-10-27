// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// The detectors live in Emgu.LiteRT.Models.CV and know nothing about MAUI: this gives them the app's cache folder for the
// downloaded weights and the bundled images (Resources/Raw) their GPU accuracy check compares against the CPU.
internal static class DetectorFactory
{
    // The bundled GPU check pictures (Resources/Raw), for the library's VisualDetectors too.
    public static Task<Mat> ValidationImage(string name) => LoadAsync(name);

    private static async Task<Mat> LoadAsync(string name) => ImageUtil.Decode(await ImageUtil.ReadAppFileAsync(name).ConfigureAwait(false));

    public static CocoDetector Coco() => new(FileSystem.CacheDirectory, ValidationImage);
    public static YoloXDetector YoloX() => new(FileSystem.CacheDirectory, ValidationImage);
    public static PicoDetDetector PicoDet() => new(FileSystem.CacheDirectory, ValidationImage);
    public static DFineDetector DFine() => new(FileSystem.CacheDirectory, ValidationImage);
    public static HandGestureDetector Hand() => new(FileSystem.CacheDirectory, ValidationImage);
    public static PoseDetector Pose() => new(FileSystem.CacheDirectory, ValidationImage);
    public static FaceDetector Face() => new(FileSystem.CacheDirectory, ValidationImage);
    public static HolisticDetector Holistic() => new(FileSystem.CacheDirectory, ValidationImage);
}
