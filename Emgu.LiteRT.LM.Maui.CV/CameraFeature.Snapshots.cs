// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.Models.CV.Maui;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// The take_snapshot alert action: a frame from the live camera, a thumbnail in the camera panel's strip and then in the chat's row
// for that opening (tap to see it large and save it; CameraFeature.CaptureStrip.cs). Session only unless kept in the chat; never
// given to the model.
public sealed partial class CameraFeature
{
    private async Task TakeVisualSnapshotAsync(VisualEvent occurrence, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!PageVisible || _cameraSuspended || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
                throw new InvalidOperationException("Snapshot needs the visible live camera.");
            return _camera;
        });
        // This is a fresh frame after the event, not the exact earlier frame used for detection. The shutter click plays with it, as with a
        // full-resolution photo: the planner answers "take a photo when..." with a snapshot, and a picture taken by an alert needs its sign.
#if ANDROID || (IOS && !MACCATALYST)
        Task click = PlayCameraClickAsync(token);
        byte[] jpeg;
        try { jpeg = await camera.CaptureAsync(token); }
        finally { await click; }
#else
        byte[] jpeg = await camera.CaptureAsync(token);
#endif
        DateTimeOffset capturedAt = DateTimeOffset.Now;
        byte[] thumbnail = SmallJpeg(jpeg);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_camera != camera || !PageVisible || _cameraSuspended || !_cameraPanel.IsVisible)
                throw new OperationCanceledException("The camera feed changed before the snapshot was shown.", token);
            string condition = _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition;
            AddCapture(new CameraCapture(CameraCaptureKind.Snapshot, capturedAt, condition, data: jpeg, thumbnail: thumbnail));
        });
    }
}
