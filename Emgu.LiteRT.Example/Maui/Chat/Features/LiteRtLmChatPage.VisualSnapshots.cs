// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID || MACCATALYST || WINDOWS || IOS
using Emgu.LiteRT.LM.Agents.Events;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // Bound image memory independently of transcript/chat history. Never attach these to a model request.
    private readonly Queue<View> _visualSnapshotViews = new();
    private const int MaximumVisualSnapshots = 20;

    private async Task TakeVisualSnapshotAsync(VisualEvent occurrence, CancellationToken token)
    {
        LiveCamera camera = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!_pageVisible || _cameraSuspended || !_cameraPanel.IsVisible || _camera?.IsRunning != true)
                throw new InvalidOperationException("Snapshot needs the visible live camera.");
            return _camera;
        });
        // This is a fresh frame after the event, not the exact earlier frame used for detection.
        byte[] jpeg = await camera.CaptureAsync(token);
        DateTimeOffset capturedAt = DateTimeOffset.Now;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_camera != camera || !_pageVisible || _cameraSuspended || !_cameraPanel.IsVisible)
                throw new OperationCanceledException("The camera feed changed before the snapshot was shown.", token);
            string condition = _visualRuntime.Snapshot().FirstOrDefault(m => m.Id == occurrence.MonitorId)?.Condition;
            var image = new Image { Source = ImageUtil.ToImageSource(jpeg), HeightRequest = 150, WidthRequest = 210, Aspect = Aspect.AspectFit };
            image.OnTap(() => ShowVisualSnapshotLarge(jpeg, capturedAt));
            SemanticProperties.SetDescription(image, "Alert snapshot. Tap to open and save.");
            var caption = new Label { Text = "Camera snapshot · " + capturedAt.ToString("HH:mm:ss") + "\n" + condition + "\nSession only · tap image to save",
                FontSize = 12, FontFamily = Theme.BodyFont, TextColor = Theme.SecondaryText };
            var row = new VerticalStackLayout { Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
                Children = { image, caption } };
            AddCard(_agentBubble, row);
            _visualSnapshotViews.Enqueue(row);
            if (_visualSnapshotViews.Count > MaximumVisualSnapshots) RemoveCard(_visualSnapshotViews.Dequeue());
        });
    }

    private void ClearVisualSnapshots()
    {
        while (_visualSnapshotViews.Count > 0) RemoveCard(_visualSnapshotViews.Dequeue());
    }

    private async void ShowVisualSnapshotLarge(byte[] jpeg, DateTimeOffset capturedAt)
    {
        var image = new Image { Source = ImageUtil.ToImageSource(jpeg), Aspect = Aspect.AspectFit,
            HeightRequest = Math.Max(240, Math.Min(Width, Height) * .65) };
        var save = new Button { Text = "Save snapshot…", FontFamily = Theme.TitleFont };
        var status = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false };
        SemanticProperties.SetDescription(save, "Save this camera snapshot as a JPEG file");
        save.Clicked += async (_, _) =>
        {
            if (!save.IsEnabled) return;
            save.IsEnabled = false; status.IsVisible = false;
            try
            {
                using var stream = new MemoryStream(jpeg, writable: false);
                var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(
                    "camera-snapshot-" + capturedAt.ToString("yyyyMMdd-HHmmss-fff") + ".jpg", stream);
                if (result.IsSuccessful) { status.Text = "Saved to " + result.FilePath; status.IsVisible = true; }
                else if (!ImageFeature.SaveWasCancelled(result.Exception))
                    throw result.Exception ?? new IOException("The snapshot could not be saved.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await DisplayAlertAsync("Couldn't save snapshot", ex.Message, "OK"); }
            finally { save.IsEnabled = true; }
        };
        await _sheet.ShowViewAsync("Camera snapshot", new VerticalStackLayout { Spacing = 10, Children = { image, save, status } }, "Close");
    }
}
#endif
