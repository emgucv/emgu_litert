// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.CV;

// The session's snapshots, photos and clips (CameraCaptures): each is a card in the chat and a thumbnail in the camera panel's
// capture strip, under the preview, newest on the left; they leave together. "Keep in chat" saves one with the reply it
// is on (a ChatSavedFile), and RestoreSavedFile makes its card again when the chat is reopened. Never given to the model.
public sealed partial class CameraFeature
{
    private readonly CameraCaptures _captures = new(maxSnapshots: 20, maxPhotos: 5, maxClips: 5);
    private ScrollView _captureStripScroll;
    private HorizontalStackLayout _captureStrip;
    private DateTime _captureStripScrolledAt;
    private bool _captureStripScrollingBack;
    private const double CaptureThumbnailHeight = 56, CaptureThumbnailMaximizedHeight = 72;
    private const int CaptureThumbnailPixels = 240;

    // What a capture has on the page: its chat card, its thumbnail in the strip, how to stop a clip playing in its card,
    // and the reply its card is on (where "Keep in chat" saves it).
    private sealed class CaptureViews
    {
        public View Card;
        public View Thumbnail;
        public Action Stop;
        public ChatMessageItem Reply;
    }

    private View BuildCaptureStrip()
    {
        _captureStrip = new HorizontalStackLayout { Spacing = 4 };
        _captureStripScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _captureStrip, IsVisible = false
        };
        // A new capture scrolls back to the newest unless the user is looking along the strip.
        _captureStripScroll.Scrolled += (_, e) =>
        {
            if (!_captureStripScrollingBack && e.ScrollX > 0)
                _captureStripScrolledAt = DateTime.UtcNow;
        };
        SemanticProperties.SetDescription(_captureStripScroll, "Pictures and clips taken by the camera, newest first");
        _captures.Added += OnCaptureAdded;
        _captures.Removed += OnCaptureRemoved;
        return _captureStripScroll;
    }

    private double CaptureThumbnailSide => _cameraMaximized ? CaptureThumbnailMaximizedHeight : CaptureThumbnailHeight;

    // The strip's height in the panel, 0 while it is empty (the maximized preview gives it the room).
    private double CaptureStripHeight => _captureStripScroll?.IsVisible == true ? CaptureThumbnailSide + 4 : 0;

    // Add a capture: its card is already made (views.Card); the thumbnail goes first in the strip.
    private void AddCapture(CameraCapture capture, CaptureViews views)
    {
        capture.Tag = views;
        _captures.Add(capture);
    }

    private void OnCaptureAdded(CameraCapture capture)
    {
        if (capture.Tag is not CaptureViews views) return;
        views.Thumbnail = CaptureThumbnail(capture);
        _captureStrip.Children.Insert(0, views.Thumbnail);
        bool appeared = !_captureStripScroll.IsVisible;
        _captureStripScroll.IsVisible = true;
        if (appeared) CaptureStripChanged();
        if (_captureStripScroll.ScrollX > 0 && DateTime.UtcNow - _captureStripScrolledAt > TimeSpan.FromSeconds(4))
            _ = ScrollCaptureStripBackAsync();
    }

    private async Task ScrollCaptureStripBackAsync()
    {
        _captureStripScrollingBack = true;
        try { await _captureStripScroll.ScrollToAsync(0, 0, false); }
        catch (Exception) { }
        finally { _captureStripScrollingBack = false; }
    }

    // Leaving the list: the thumbnail always goes; a kept capture pushed out keeps its card and file (it is saved with the
    // chat, and its card still opens the file), anything else loses both.
    private void OnCaptureRemoved(CameraCapture capture, CameraCaptureRemoval why)
    {
        if (capture.Tag is not CaptureViews views) return;
        if (views.Thumbnail != null)
        {
            _captureStrip.Children.Remove(views.Thumbnail);
            if (_captureStrip.Children.Count == 0)
            {
                _captureStripScroll.IsVisible = false;
                CaptureStripChanged();
            }
        }
        if (capture.Kept && why == CameraCaptureRemoval.Cap) return;
        views.Stop?.Invoke();
        if (views.Card != null) Host.RemoveCard(views.Card);
        if (capture.Path != null)
        {
            try { File.Delete(capture.Path); } catch (Exception) { }
        }
    }

    private void ClearCaptures() => _captures.Clear();

    // The strip came or went: the maximized preview makes room, and a panel dragged to the bottom stays on screen.
    private void CaptureStripChanged()
    {
        UpdateCameraPanelSize();
        Host.Page.Dispatcher.Dispatch(() => MoveCameraPanel(_cameraPanel.TranslationX, _cameraPanel.TranslationY));
    }

    // The strip's thumbnails follow the panel's size (small or maximized).
    private void UpdateCaptureThumbnailSizes()
    {
        if (_captureStrip == null) return;
        double side = CaptureThumbnailSide;
        foreach (IView child in _captureStrip.Children)
            if (child is View view)
            {
                view.HeightRequest = side;
                view.WidthRequest = side * 4 / 3;
            }
    }

    private View CaptureThumbnail(CameraCapture capture)
    {
        double side = CaptureThumbnailSide;
        var image = new Image { Aspect = Aspect.AspectFill };
        if (capture.Thumbnail != null) image.Source = CvViews.ToImageSource(capture.Thumbnail);
        var grid = new Grid { Children = { image } };
        string kind = capture.Kind == CameraCaptureKind.Snapshot ? "Snapshot" : capture.Kind == CameraCaptureKind.Photo ? "Photo" : "Clip";
        if (capture.Kind == CameraCaptureKind.Clip)
        {
            grid.Children.Add(new Border
            {
                BackgroundColor = Color.FromRgba(0, 0, 0, 0.55), StrokeThickness = 0, Padding = new Thickness(4, 1),
                StrokeShape = new RoundRectangle { CornerRadius = 4 }, Margin = new Thickness(3),
                HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End, InputTransparent = true,
                Content = new Label { Text = "▶ " + FormatCaptureLength(capture.Duration), FontSize = 10, TextColor = Colors.White }
            });
        }
        var thumbnail = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, BackgroundColor = CvViews.Color("ChatRowBorder"),
            HeightRequest = side, WidthRequest = side * 4 / 3, Content = grid
        };
        thumbnail.OnTap(() => _ = OpenCaptureAsync(capture));
        SemanticProperties.SetDescription(thumbnail, kind + ", " + capture.TakenAt.ToString("HH:mm:ss") + ". Tap to open.");
        return thumbnail;
    }

    private static string FormatCaptureLength(TimeSpan length) => (int)length.TotalMinutes + ":" + length.Seconds.ToString("00");

    // A thumbnail opens what its card opens.
    private async Task OpenCaptureAsync(CameraCapture capture)
    {
        switch (capture.Kind)
        {
            case CameraCaptureKind.Snapshot:
                if (capture.Data != null) ShowVisualSnapshotLarge(capture.Data, capture.TakenAt);
                break;
#if ANDROID || (IOS && !MACCATALYST)
            case CameraCaptureKind.Photo:
                await OpenVisualPhotoAsync(capture.Path);
                break;
            case CameraCaptureKind.Clip:
                (capture.Tag as CaptureViews)?.Stop?.Invoke();
                await PlayVisualClipAsync(capture.Path);
                break;
#endif
        }
        await Task.CompletedTask;
    }

    // A small JPEG for the strip (a 960 px snapshot decoded 20 times over is ~70 MB on a phone).
    private static byte[] SmallJpeg(byte[] jpeg, int pixels = CaptureThumbnailPixels)
    {
        if (jpeg == null) return null;
        try
        {
            using var image = new Mat();
            CvInvoke.Imdecode(jpeg, ImreadModes.ColorBgr, image);
            if (image.IsEmpty) return null;
            int longest = Math.Max(image.Width, image.Height);
            if (longest <= pixels) return jpeg;
            double scale = (double)pixels / longest;
            using var small = new Mat();
            CvInvoke.Resize(image, small, new System.Drawing.Size((int)(image.Width * scale), (int)(image.Height * scale)), 0, 0, Inter.Area);
            return CvInvoke.Imencode(".jpg", small, new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 80));
        }
        catch (Exception) { return null; }
    }

    // The reply a capture's card goes on (Host.AddCardToReply's): where "Keep in chat" saves it; null before the first reply.
    private ChatMessageItem CaptureReply() =>
        Host.Conversation?.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);

    private static string CaptureKeptText(bool kept) => kept ? "Kept in this chat" : "Session only";

    // "Keep in chat" for a capture's card: saves the picture or clip with its reply; hidden in a private chat or without a reply.
    private Button KeepInChatButton(CameraCapture capture, ChatMessageItem reply, Label caption)
    {
        if (reply == null || Host.IsPrivate) return null;
        var keep = MonitorButton("Keep in chat");
        SemanticProperties.SetDescription(keep, "Save this with the chat, so it is here when the chat is opened again");
        keep.Clicked += async (_, _) =>
        {
            if (capture.Kept || !keep.IsEnabled) return;
            if (Host.IsPrivate)
            {
                await Host.AskAsync("Private chat", "A private chat isn't saved, so nothing can be kept in it.", "OK");
                return;
            }
            keep.IsEnabled = false;
            try
            {
                byte[] bytes = capture.Data ?? await File.ReadAllBytesAsync(capture.Path);
                var file = new ChatSavedFile(CameraCaptureFile.KindOf(capture.Kind), CameraCaptureFile.MediaTypeOf(capture), bytes);
                await Host.AddCardToSavedReplyAsync(reply, null, file);
                _captures.Keep(capture);
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

    /// <inheritdoc/>
    public override View RestoreSavedFile(ChatSavedFile file)
    {
        CameraCapture capture = CameraCaptureFile.Parse(file?.Kind, file?.MediaType, file?.Data);
        if (capture == null) return null;
        try
        {
            switch (capture.Kind)
            {
                case CameraCaptureKind.Snapshot:
                    return SnapshotCard(capture, null);
#if ANDROID || (IOS && !MACCATALYST)
                case CameraCaptureKind.Photo:
                case CameraCaptureKind.Clip:
                    // Opened, shared and played from a file: written once to the cache from the saved bytes.
                    string folder = System.IO.Path.Combine(FileSystem.CacheDirectory, "kept");
                    Directory.CreateDirectory(folder);
                    string path = System.IO.Path.Combine(folder, (capture.Kind == CameraCaptureKind.Photo ? "photo-" : "clip-") +
                        capture.TakenAt.ToString("yyyyMMdd-HHmmss-fff") + "-" + capture.Data.Length + (capture.Kind == CameraCaptureKind.Photo ? ".jpg" : ".mp4"));
                    if (!File.Exists(path) || new FileInfo(path).Length != capture.Data.Length)
                        File.WriteAllBytes(path, capture.Data);
                    var onDisk = new CameraCapture(capture.Kind, capture.TakenAt, capture.Condition, path: path)
                    {
                        Width = capture.Width, Height = capture.Height, Duration = capture.Duration, WithSound = capture.WithSound, Kept = true
                    };
                    return capture.Kind == CameraCaptureKind.Photo ? PhotoCard(onDisk, PhotoThumbnail(path), null) : ClipCard(onDisk, ClipThumbnail(path), null, out _);
#endif
                default:
                    return null;
            }
        }
        catch (Exception) { return null; }
    }
}
