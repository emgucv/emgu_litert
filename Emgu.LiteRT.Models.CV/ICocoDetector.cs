// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.Models;

// Common host result contract; no model output tensors escape the adapter.
/// <summary>An object detector that takes Emgu CV images and returns COCO-style detections, for the demo's photo pages and camera monitors.</summary>
public interface ICocoDetector : IDisposable
{
    /// <summary>True once <see cref="PrepareAsync"/> has completed.</summary>
    bool Imported { get; }
    /// <summary>The backend in use ("CPU", "GPU" or "GPU + CPU"), or null before <see cref="PrepareAsync"/> has completed.</summary>
    string Backend { get; }
    /// <summary>Why the GPU was not used, or null if it was used or never tried.</summary>
    string GpuFailure { get; }
    /// <summary>Downloads the weights if needed and compiles the model, trying the GPU first when <paramref name="preferGpu"/> is set and the platform supports it; the CPU otherwise or on failure.</summary>
    Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token);
    /// <summary>Detects objects in an 8-bit BGR image. Rectangles are normalized to the image (0 to 1).</summary>
    CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr);
}
