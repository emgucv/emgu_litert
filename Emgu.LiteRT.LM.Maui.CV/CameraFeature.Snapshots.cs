// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.Models.CV.Maui;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// The take_snapshot alert action: a frame from the live camera shown in the transcript and the camera panel's strip (tap to see
// it large and save it). Session only unless kept in the chat (CameraFeature.CaptureStrip.cs); never given to the model.
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
            var capture = new CameraCapture(CameraCaptureKind.Snapshot, capturedAt, condition, data: jpeg, thumbnail: thumbnail);
            ChatMessageItem reply = CaptureReply();
            View card = SnapshotCard(capture, reply);
            Host.AddCardToReply(card);
            AddCapture(capture, new CaptureViews { Card = card, Reply = reply });
        });
    }

    // A snapshot's card: the picture (tap to see it large and save it), when and why, and Keep in chat (for a live one with a reply).
    private View SnapshotCard(CameraCapture capture, ChatMessageItem reply)
    {
        byte[] jpeg = capture.Data;
        var image = new Image { Source = CvViews.ToImageSource(jpeg), HeightRequest = 150, WidthRequest = 210, Aspect = Aspect.AspectFit };
        image.OnTap(() => ShowVisualSnapshotLarge(jpeg, capture.TakenAt));
        SemanticProperties.SetDescription(image, "Camera snapshot. Tap to open and save.");
        var caption = new Label
        {
            Text = "Camera snapshot · " + capture.TakenAt.ToLocalTime().ToString("HH:mm:ss") + (string.IsNullOrEmpty(capture.Condition) ? "" : "\n" + capture.Condition) +
                "\n" + CaptureKeptText(capture.Kept) + " · tap image to save",
            FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText")
        };
        var row = new VerticalStackLayout { Spacing = 4, Margin = new Thickness(6, 4), HorizontalOptions = LayoutOptions.Start,
            Children = { image, caption } };
        if (!capture.Kept && KeepInChatButton(capture, reply, caption) is Button keep)
            row.Children.Add(new HorizontalStackLayout { Children = { keep } });
        return row;
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
