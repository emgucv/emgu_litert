//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>
/// The detectors the camera's visual agents and the exercise counter run on (Emgu.LiteRT.Models.CV), made when a feature
/// needs them. By default each keeps its downloaded weights in the app's cache folder and skips the GPU accuracy check;
/// an app with the check's pictures gives a loader for them, or replaces any factory.
/// </summary>
public sealed class VisualDetectors
{
    /// <summary>Create the set.</summary>
    /// <param name="cacheFolder">Where the detectors keep their downloaded weights; null for the app's cache folder</param>
    /// <param name="validationImage">Loads a bundled picture by name for a detector's GPU accuracy check; null to skip the check</param>
    public VisualDetectors(string cacheFolder = null, Func<string, Task<Mat>> validationImage = null)
    {
        string folder = cacheFolder ?? FileSystem.CacheDirectory;
        Func<string, Task<Mat>> load = validationImage ?? (name => Task.FromResult<Mat>(null));
        Coco = () => new CocoDetector(folder, load);
        YoloX = () => new YoloXDetector(folder, load);
        Hand = () => new HandGestureDetector(folder, load);
        Pose = () => new PoseDetector(folder, load);
        Face = () => new FaceDetector(folder, load);
        Holistic = () => new HolisticDetector(folder, load);
    }

    /// <summary>The COCO SSD MobileNet detector (objects).</summary>
    public Func<CocoDetector> Coco { get; set; }

    /// <summary>The YOLOX detector (objects, experimental).</summary>
    public Func<YoloXDetector> YoloX { get; set; }

    /// <summary>The hand gesture detector.</summary>
    public Func<HandGestureDetector> Hand { get; set; }

    /// <summary>The body pose detector.</summary>
    public Func<PoseDetector> Pose { get; set; }

    /// <summary>The face expression detector.</summary>
    public Func<FaceDetector> Face { get; set; }

    /// <summary>The holistic detector (pose, face and hands).</summary>
    public Func<HolisticDetector> Holistic { get; set; }
}
