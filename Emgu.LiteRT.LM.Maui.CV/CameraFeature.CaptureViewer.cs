// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;

namespace Emgu.LiteRT.LM.Maui.CV;

// A capture's viewer, from its thumbnail in the camera panel's strip or in a chat row: the picture large or the clip playing, when
// and why, Open or Full screen, Share, Save to gallery, and Keep in chat (with a reply, not in a private chat). One sheet for all three
// kinds; the clip stops when it closes.
public sealed partial class CameraFeature
{
    private async Task ShowCaptureAsync(CameraCapture capture)
    {
        Action stop = null;
        string title;
        View content;
        switch (capture.Kind)
        {
            case CameraCaptureKind.Snapshot:
                title = "Camera snapshot";
                content = SnapshotViewer(capture);
                break;
#if ANDROID || (IOS && !MACCATALYST)
            case CameraCaptureKind.Photo:
                title = "Camera photo";
                content = PhotoViewer(capture);
                break;
            case CameraCaptureKind.Clip:
                title = "Camera clip";
                content = ClipViewer(capture, out stop);
                break;
#endif
            default:
                return;
        }
        try { await Host.ShowSheetAsync(title, content, "Close"); }
        finally { stop?.Invoke(); }
    }

    // The viewer's picture or clip box: as large as leaves the caption and the buttons on the sheet.
    private double CaptureViewerHeight
    {
        get
        {
            Size page = Host.PageSize;
            return Math.Clamp(page.Height * 0.42, 180, 480);
        }
    }

    // What the viewer says under the picture: the kind's own details, the cause, and whether it is kept.
    private static Label CaptureCaption(CameraCapture capture, string details) => new()
    {
        Text = details + (string.IsNullOrEmpty(capture.Condition) ? "" : "\n" + capture.Condition) + "\n" + CaptureKeptText(capture.Kept),
        FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText")
    };

    private static string CaptureKeptText(bool kept) => kept ? "Kept in this chat" : "Session only";

    // The viewer's buttons, wrapping to the next line rather than squeezed: a squeezed "Full screen" was cut to "Full screer" on an iPad.
    private static FlexLayout CaptureButtons(params Button[] buttons)
    {
        var layout = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, HorizontalOptions = LayoutOptions.Start };
        foreach (Button button in buttons)
        {
            if (button == null) continue;
            button.Margin = new Thickness(0, 0, 6, 4);
            FlexLayout.SetShrink(button, 0);
            layout.Children.Add(button);
        }
        return layout;
    }

    // "Keep in chat" in a viewer: saves the capture with its row's reply (or, from the strip, the latest reply); none for a kept one,
    // in a private chat or without a reply.
    private Button KeepInChatButton(CameraCapture capture, Label caption)
    {
        ChatMessageItem reply = (capture.Tag as CaptureViews)?.Reply ?? CaptureReply();
        if (capture.Kept || !CanKeepCaptures(reply)) return null;
        var keep = MonitorButton("Keep in chat");
        SemanticProperties.SetDescription(keep, "Save this with the chat, so it is here when the chat is opened again");
        keep.Clicked += async (_, _) =>
        {
            if (capture.Kept || !keep.IsEnabled) return;
            keep.IsEnabled = false;
            try
            {
                await KeepCaptureAsync(capture, reply);
                keep.Text = "Kept in chat ✓";
                caption.Text = caption.Text.Replace(CaptureKeptText(false), CaptureKeptText(true));
            }
            catch (Exception ex)
            {
                keep.IsEnabled = true;
                await Host.AskAsync("Couldn't keep it in the chat", ex.Message, "OK");
            }
        };
        return keep;
    }

    private View SnapshotViewer(CameraCapture capture)
    {
        byte[] jpeg = capture.Data;
        var image = new Image { Source = CvViews.ToImageSource(jpeg), Aspect = Aspect.AspectFit, HeightRequest = CaptureViewerHeight };
        SemanticProperties.SetDescription(image, "Camera snapshot");
        Label caption = CaptureCaption(capture, "Camera snapshot · " + capture.TakenAt.ToString("HH:mm:ss"));
        var status = new Label { FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText"), IsVisible = false };
        var save = MonitorButton("Save to gallery");
        SemanticProperties.SetDescription(save, "Save this camera snapshot as a JPEG file");
        save.Clicked += async (_, _) =>
        {
            if (!save.IsEnabled) return;
            save.IsEnabled = false; status.IsVisible = false;
            try
            {
                string fileName = "camera-snapshot-" + capture.TakenAt.ToString("yyyyMMdd-HHmmss-fff") + ".jpg";
                string saved = await (SnapshotSaver ?? PictureGallery.SaveAsync)(jpeg, fileName);
                if (saved != null) { status.Text = saved; status.IsVisible = true; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await Host.AskAsync("Couldn't save snapshot", ex.Message, "OK"); }
            finally { save.IsEnabled = true; }
        };
        return new VerticalStackLayout { Spacing = 8, Children = { image, caption, CaptureButtons(save, KeepInChatButton(capture, caption)), status } };
    }
}
