// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite;

// Common host result contract; no model output tensors escape the adapter.
internal interface ICocoDetector : IDisposable
{
    bool Imported { get; }
    string Backend { get; }
    string GpuFailure { get; }
    Task PrepareAsync(bool preferGpu, int numThreads, Action<string> progress, CancellationToken token);
    CocoSsdMobilenet.RecognitionResult[] Detect(Mat bgr);
}
