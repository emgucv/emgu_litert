// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.CV;

// The session's snapshots, photos and clips (CameraCaptures). While the camera is open they are thumbnails in the camera panel's
// capture strip, under the preview, newest on the left, and nothing in the chat; when it closes, that opening's captures become
// one row in the chat (CameraFeature.CaptureRow.cs). A thumbnail opens the capture's viewer (CameraFeature.CaptureViewer.cs),
// where "Keep in chat" saves it with the reply. Never given to the model.
public sealed partial class CameraFeature
{
    private readonly CameraCaptures _captures = new(maxSnapshots: 20, maxPhotos: 5, maxClips: 5);
    private int _captureOpening;   // the opening of the camera the strip shows; the next one starts when the camera closes
    private ScrollView _captureStripScroll;
    private HorizontalStackLayout _captureStrip;
    private DateTime _captureStripScrolledAt;
    private bool _captureStripScrollingBack;
    private const double CaptureThumbnailHeight = 56, CaptureThumbnailMaximizedHeight = 72;
    private const int CaptureThumbnailPixels = 240;

    // What a capture has on the page: its thumbnail in the strip while its opening goes on, then its row in the chat; the reply
    // the row is on (where "Keep in chat" saves it); and a larger picture for the viewer (a photo's or a clip's; a snapshot
    // shows its own JPEG).
    private sealed class CaptureViews
    {
        public View Thumbnail;
        public CaptureRow Row;
        public ChatMessageItem Reply;
        public byte[] Preview;
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

    // Add a capture to this opening: the thumbnail goes first in the strip.
    private void AddCapture(CameraCapture capture, byte[] preview = null)
    {
        capture.Opening = _captureOpening;
        capture.Tag = new CaptureViews { Preview = preview };
        _captures.Add(capture);
    }

    private void OnCaptureAdded(CameraCapture capture)
    {
        if (capture.Tag is not CaptureViews views) return;
        views.Thumbnail = CaptureThumbnail(capture, CaptureThumbnailSide);
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

    // Leaving the list: the strip's thumbnail always goes; a kept capture pushed out stays in its row with its file (it is
    // saved with the chat), anything else leaves its row and loses its file.
    private void OnCaptureRemoved(CameraCapture capture, CameraCaptureRemoval why)
    {
        if (capture.Tag is not CaptureViews views) return;
        if (views.Thumbnail != null)
        {
            _captureStrip.Children.Remove(views.Thumbnail);
            views.Thumbnail = null;
            if (_captureStrip.Children.Count == 0)
            {
                _captureStripScroll.IsVisible = false;
                CaptureStripChanged();
            }
        }
        if (capture.Kept && why == CameraCaptureRemoval.Cap) return;
        views.Row?.Remove(capture);
        if (capture.Path != null)
        {
            try { File.Delete(capture.Path); } catch (Exception) { }
        }
    }

    // A new chat: every capture goes, with its file (the rows went with the old chat).
    private void ClearCaptures()
    {
#if ANDROID || (IOS && !MACCATALYST)
        _stopPlayingClip?.Invoke();
#endif
        _captures.Clear();
    }

    // The camera closed for good (not to switch cameras): this opening's captures leave the strip and become one row in the
    // chat, on the latest reply; the next opening starts with an empty strip.
    private void EndCaptureOpening()
    {
        List<CameraCapture> taken = _captures.ItemsOf(_captureOpening).ToList();
        _captureOpening++;
        if (_captureStrip?.Children.Count > 0)
        {
            foreach (CameraCapture capture in taken)
                if (capture.Tag is CaptureViews views) views.Thumbnail = null;
            _captureStrip.Children.Clear();
            _captureStripScroll.IsVisible = false;
            UpdateCameraPanelSize();
        }
        if (taken.Count == 0 || Host.Conversation == null) return;
        var row = new CaptureRow(this, taken, CaptureReply(), restored: false);
        Host.AddCardToReply(row.View);
    }

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

    // A capture's thumbnail, side high (4:3): a clip with its length, a kept one (in a chat row) with a check mark; a tap opens
    // its viewer.
    private View CaptureThumbnail(CameraCapture capture, double side, bool markKept = false)
    {
        var image = new Image { Aspect = Aspect.AspectFill };
        if (capture.Thumbnail != null) image.Source = CvViews.ToImageSource(capture.Thumbnail);
        var grid = new Grid { Children = { image } };
        string kind = capture.Kind == CameraCaptureKind.Snapshot ? "Snapshot" : capture.Kind == CameraCaptureKind.Photo ? "Photo" : "Clip";
        if (capture.Kind == CameraCaptureKind.Clip)
            grid.Children.Add(ThumbnailBadge("▶ " + FormatCaptureLength(capture.Duration), LayoutOptions.Start, LayoutOptions.End));
        if (markKept && capture.Kept)
            grid.Children.Add(ThumbnailBadge("✓", LayoutOptions.End, LayoutOptions.Start));
        var thumbnail = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, BackgroundColor = CvViews.Color("ChatRowBorder"),
            HeightRequest = side, WidthRequest = side * 4 / 3, Content = grid
        };
        thumbnail.OnTap(() => _ = ShowCaptureAsync(capture));
        SemanticProperties.SetDescription(thumbnail, kind + ", " + capture.TakenAt.ToString("HH:mm:ss") + (markKept && capture.Kept ? ", kept in this chat" : "") + ". Tap to open.");
        return thumbnail;
    }

    private static Border ThumbnailBadge(string text, LayoutOptions horizontal, LayoutOptions vertical) => new()
    {
        BackgroundColor = Color.FromRgba(0, 0, 0, 0.55), StrokeThickness = 0, Padding = new Thickness(4, 1),
        StrokeShape = new RoundRectangle { CornerRadius = 4 }, Margin = new Thickness(3),
        HorizontalOptions = horizontal, VerticalOptions = vertical, InputTransparent = true,
        Content = new Label { Text = text, FontSize = 10, TextColor = Colors.White }
    };

    private static string FormatCaptureLength(TimeSpan length) => (int)length.TotalMinutes + ":" + length.Seconds.ToString("00");

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

    // The reply a row goes on (Host.AddCardToReply's), and where a capture kept from the strip is saved; null before the first reply.
    private ChatMessageItem CaptureReply() =>
        Host.Conversation?.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant);

    private bool CanKeepCaptures(ChatMessageItem reply) => reply != null && !Host.IsPrivate;

    // Save a capture with a reply (one ChatSavedFile; the chats' store writes it at once for a saved reply); its row shows it kept.
    private async Task KeepCaptureAsync(CameraCapture capture, ChatMessageItem reply)
    {
        if (capture.Kept) return;
        if (!CanKeepCaptures(reply)) throw new InvalidOperationException(Host.IsPrivate
            ? "A private chat isn't saved, so nothing can be kept in it." : "There is no reply in this chat to keep it with yet.");
        byte[] bytes = capture.Data ?? await File.ReadAllBytesAsync(capture.Path);
        var file = new ChatSavedFile(CameraCaptureFile.KindOf(capture.Kind), CameraCaptureFile.MediaTypeOf(capture), bytes);
        await Host.AddCardToSavedReplyAsync(reply, null, file);
        _captures.Keep(capture);
        (capture.Tag as CaptureViews)?.Row?.Refresh(capture);
    }

    /// <inheritdoc/>
    public override View RestoreSavedFiles(IReadOnlyList<ChatSavedFile> files, ISet<ChatSavedFile> restored)
    {
        var captures = new List<CameraCapture>();
        foreach (ChatSavedFile file in files)
        {
            if (restored.Contains(file)) continue;
            CameraCapture capture = CameraCaptureFile.Parse(file?.Kind, file?.MediaType, file?.Data);
            if (capture == null) continue;
            restored.Add(file);
            try
            {
                CameraCapture shown = RestoredCapture(capture);
                if (shown != null) captures.Add(shown);
            }
            catch (Exception) { }
        }
        if (captures.Count == 0) return null;
        return new CaptureRow(this, captures.OrderByDescending(c => c.TakenAt).ToList(), null, restored: true).View;
    }

    // A kept capture read back, ready to show: a snapshot with its thumbnail, a photo or clip written once to the cache to open,
    // share and play from. Null for what this platform can't show.
    private static CameraCapture RestoredCapture(CameraCapture capture)
    {
        if (capture.Kind == CameraCaptureKind.Snapshot)
        {
            var snapshot = new CameraCapture(capture.Kind, capture.TakenAt, capture.Condition, data: capture.Data, thumbnail: SmallJpeg(capture.Data)) { Kept = true };
            snapshot.Tag = new CaptureViews();
            return snapshot;
        }
#if ANDROID || (IOS && !MACCATALYST)
        bool photo = capture.Kind == CameraCaptureKind.Photo;
        string folder = System.IO.Path.Combine(FileSystem.CacheDirectory, "kept");
        Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, (photo ? "photo-" : "clip-") + capture.TakenAt.ToString("yyyyMMdd-HHmmss-fff") + "-" +
            capture.Data.Length + (photo ? ".jpg" : ".mp4"));
        if (!File.Exists(path) || new FileInfo(path).Length != capture.Data.Length)
            File.WriteAllBytes(path, capture.Data);
        byte[] preview = photo ? PhotoThumbnail(path) : ClipThumbnail(path);
        return new CameraCapture(capture.Kind, capture.TakenAt, capture.Condition, path: path, thumbnail: SmallJpeg(preview))
        {
            Width = capture.Width, Height = capture.Height, Duration = capture.Duration, WithSound = capture.WithSound, Kept = true,
            Tag = new CaptureViews { Preview = preview }
        };
#else
        return null;
#endif
    }
}
