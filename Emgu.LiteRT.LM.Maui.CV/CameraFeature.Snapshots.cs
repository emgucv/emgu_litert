// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.Models.CV.Maui;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// The take_snapshot alert action: a frame from the live camera shown in the transcript (tap to see it large and save
// it). Session only, never given to the model.
public sealed partial class CameraFeature
{
    // Bound image memory independently of transcript/chat history. Never attach these to a model request.
    private readonly Queue<View> _visualSnapshotViews = new();
    private const int MaximumVisualSnapshots = 20;

    private async Task TakeVisualSnapshotAsync(VisualEvent occurrence, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!PageVisible || _cameraSuspended || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
                throw new InvalidOperationException("Snapshot needs the visible live camera.");
            return _camera;
        });
        // This is a fresh frame after the event, not the exact earlier frame used for detection.
        byte[] jpeg = await camera.CaptureAsync(token);
        DateTimeOffset capturedAt = DateTimeOffset.Now;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_camera != camera || !PageVisible || _cameraSuspended || !_cameraPanel.IsVisible)
                throw new OperationCanceledException("The camera feed changed before the snapshot was shown.", token);
            string condition = _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition;
            var image = new Image { Source = CvViews.ToImageSource(jpeg), HeightRequest = 150, WidthRequest = 210, Aspect = Aspect.AspectFit };
            image.OnTap(() => ShowVisualSnapshotLarge(jpeg, capturedAt));
            SemanticProperties.SetDescription(image, "Alert snapshot. Tap to open and save.");
            var caption = new Label { Text = "Camera snapshot · " + capturedAt.ToString("HH:mm:ss") + "\n" + condition + "\nSession only · tap image to save",
                FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText") };
            var row = new VerticalStackLayout { Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
                Children = { image, caption } };
            Host.AddCardToReply(row);
            _visualSnapshotViews.Enqueue(row);
            if (_visualSnapshotViews.Count > MaximumVisualSnapshots) Host.RemoveCard(_visualSnapshotViews.Dequeue());
        });
    }

    private void ClearVisualSnapshots()
    {
        while (_visualSnapshotViews.Count > 0) Host.RemoveCard(_visualSnapshotViews.Dequeue());
    }

    private async void ShowVisualSnapshotLarge(byte[] jpeg, DateTimeOffset capturedAt)
    {
        Size page = Host.PageSize;
        var image = new Image { Source = CvViews.ToImageSource(jpeg), Aspect = Aspect.AspectFit,
            HeightRequest = Math.Max(240, Math.Min(page.Width, page.Height) * .65) };
        var save = new Button { Text = "Save snapshot…" };
        var status = new Label { FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText"), IsVisible = false };
        SemanticProperties.SetDescription(save, "Save this camera snapshot as a JPEG file");
        save.Clicked += async (_, _) =>
        {
            if (!save.IsEnabled) return;
            save.IsEnabled = false; status.IsVisible = false;
            try
            {
                string fileName = "camera-snapshot-" + capturedAt.ToString("yyyyMMdd-HHmmss-fff") + ".jpg";
                string saved = await (SnapshotSaver ?? PictureGallery.SaveAsync)(jpeg, fileName);
                if (saved != null) { status.Text = saved; status.IsVisible = true; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await Host.AskAsync("Couldn't save snapshot", ex.Message, "OK"); }
            finally { save.IsEnabled = true; }
        };
        await Host.ShowSheetAsync("Camera snapshot", new VerticalStackLayout { Spacing = 10, Children = { image, save, status } }, "Close");
    }
}
