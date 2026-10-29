// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.CV;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.Models;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>
/// Photo effects: the blur_photo_background tool (Emgu.LiteRT.LM.Agents' <c>AgentTools.BlurPhotoBackground</c>) gives a
/// photo the user attached a portrait mode's look - the depth from Depth Anything V2 Small (Emgu.LiteRT.Models.CV's
/// <see cref="DepthEstimator"/>, a 27.7 MB download on first use, on the GPU when it passes its check) and
/// <see cref="DepthEffects.BlurBackground"/>. The new photo is shown above the reply (tap to see it large and save it)
/// and kept with the reply in the saved chat (<see cref="ChatMessageItem.SavedFiles"/>, kind <see cref="EditedKind"/>,
/// never given back to the model). The photo is the message's attachment, or the latest one in the chat. A tool group
/// of the router (<see cref="ChatPhotoEffects"/>).
/// </summary>
public sealed class PhotoEffectsFeature : CvFeature
{
    /// <summary>The kind of a reply's saved file that is a photo it edited.</summary>
    public const string EditedKind = "edited_photo";

    private readonly DepthEstimator _depth;
    private readonly SemaphoreSlim _busy = new(1, 1);

    /// <summary>Create the feature.</summary>
    /// <param name="depth">The depth estimator (with the app's cache folder and validation image, so it can use the GPU)</param>
    public PhotoEffectsFeature(DepthEstimator depth)
    {
        _depth = depth ?? throw new ArgumentNullException(nameof(depth));
    }

    /// <summary>
    /// Saves a photo where the user chooses (e.g. CommunityToolkit.Maui's FileSaver): given the JPEG and a file name,
    /// returns what to tell the user, or null when they cancelled. Without it the photo goes to the gallery
    /// (<see cref="PictureGallery"/>).
    /// </summary>
    public Func<byte[], string, Task<string>> PhotoSaver { get; set; }

    /// <inheritdoc/>
    public override string Name => "photo_effects";

    /// <inheritdoc/>
    public override string Title => "Photo effects";

    /// <inheritdoc/>
    public override string ToolSummary => "blurring a photo's background";

    /// <inheritdoc/>
    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatPhotoEffects.GroupName, ChatPhotoEffects.GroupSummary, ChatPhotoEffects.GroupKeywords,
            () => new AITool[] { AgentTools.BlurPhotoBackground(BlurBackgroundAsync) },
            () => ChatPhotoEffects.Instructions)
    };

    /// <inheritdoc/>
    public override void OnDetached()
    {
        base.OnDetached();
        _ = Task.Run(async () =>
        {
            await _busy.WaitAsync();
            try { _depth.Dispose(); }
            finally { _busy.Release(); }
        });
    }

    /// <summary>The blur_photo_background tool: find the photo, blur its background, show it, and tell the model.</summary>
    /// <param name="imageReference">"image-1"..., or empty for the latest photo</param>
    /// <param name="strength">"light", "medium" or "strong"</param>
    /// <param name="cancel">Stops it</param>
    public async Task<string> BlurBackgroundAsync(string imageReference, string strength, CancellationToken cancel)
    {
        IReadOnlyList<DataContent> photos = MessagePhotos();
        if (photos.Count == 0)
            return "No photo is attached. Tell the user to attach the photo (with + in the message box) and ask again.";
        DataContent photo = photos[ChatPhotoEffects.ImageIndex(imageReference, photos.Count)];
        string level = ChatPhotoEffects.NormalizeStrength(strength);

        PhotoTile tile = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            PhotoTile created = CreateTile();
            Host.AddCardToReply(created.View);
            return created;
        });
        Action<string> progress = text => MainThread.BeginInvokeOnMainThread(() => tile.Stage.Text = text);
        try
        {
            await _busy.WaitAsync(cancel);
            byte[] jpeg;
            string caption;
            try
            {
                await _depth.PrepareAsync(true, 4, progress, cancel);
                progress("Blurring the background…");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                jpeg = await Task.Run(() => Blur(photo.Data.ToArray(), level), cancel);
                caption = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Background blurred in {0:F1} s ({1}) · tap to save",
                    watch.Elapsed.TotalSeconds, _depth.Backend);
            }
            finally { _busy.Release(); }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ShowPhoto(tile, jpeg);
                tile.Stage.Text = caption;
                // Kept with the reply (shown again when the chat is reopened, not given to the model).
                Host.AddCardToReply(null, new ChatSavedFile(EditedKind, "image/jpeg", jpeg));
            });
            return AgentTools.PhotoBackgroundBlurredResult(level);
        }
        catch (OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() => tile.Stage.Text = "Stopped");
            throw;
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => tile.Stage.Text = "Couldn't blur it: " + ex.Message);
            return "The photo's background couldn't be blurred: " + ex.Message;
        }
    }

    // The message's photos, else the latest message's with photos (a follow-up: "make it stronger").
    private IReadOnlyList<DataContent> MessagePhotos()
    {
        static List<DataContent> Photos(IEnumerable<DataContent> attachments) =>
            (attachments ?? Array.Empty<DataContent>()).Where(a => a.MediaType != null && a.MediaType.StartsWith("image/", StringComparison.Ordinal)).ToList();
        List<DataContent> photos = Photos(Context?.Attachments);
        if (photos.Count > 0)
            return photos;
        IReadOnlyList<ChatMessageItem> messages = Host.Conversation?.Messages;
        if (messages == null)
            return photos;
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role != ChatMessageRole.User)
                continue;
            photos = Photos(messages[i].Attachments);
            if (photos.Count > 0)
                return photos;
        }
        return photos;
    }

    private byte[] Blur(byte[] encoded, string level)
    {
        using var bgr = new Mat();
        CvInvoke.Imdecode(encoded, ImreadModes.ColorBgr, bgr);
        if (bgr.IsEmpty)
            throw new InvalidOperationException("the photo couldn't be read");
        using Mat depth = _depth.Estimate(bgr);
        var strength = level == "light" ? BackgroundBlurStrength.Light : level == "strong" ? BackgroundBlurStrength.Strong : BackgroundBlurStrength.Medium;
        using Mat blurred = DepthEffects.BlurBackground(bgr, depth, strength);
        using var jpeg = new VectorOfByte();
        CvInvoke.Imencode(".jpg", blurred, jpeg, new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 92));
        return jpeg.ToArray();
    }

    private sealed class PhotoTile
    {
        public VerticalStackLayout View;
        public Border Frame;
        public Label Stage;
    }

    private const double PhotoWidth = 220;

    // A photo being edited, above the reply: a placeholder with a spinner and the stage, then the photo.
    private static PhotoTile CreateTile()
    {
        var tile = new PhotoTile();
        tile.Frame = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            Stroke = Colors.Transparent,
            WidthRequest = PhotoWidth,
            HeightRequest = PhotoWidth * 0.75,
            Content = new ActivityIndicator { IsRunning = true, Color = ChatResources.FindColor("ChatAccent"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        };
        ChatResources.SetThemeColor(tile.Frame, VisualElement.BackgroundColorProperty, "ChatSurface");
        tile.Stage = new Label { Text = "Starting…" };
        ChatResources.ApplyStyle(tile.Stage, "ChatListDetailStyle");
        tile.View = new VerticalStackLayout
        {
            Spacing = 4,
            Margin = new Thickness(6, 0),
            HorizontalOptions = LayoutOptions.Start,
            Children = { tile.Frame, tile.Stage }
        };
        SemanticProperties.SetDescription(tile.View, "Photo with its background blurred");
        return tile;
    }

    private void ShowPhoto(PhotoTile tile, byte[] jpeg)
    {
        // The photo's own shape: the frame takes its aspect ratio.
        using (var header = new Mat())
        {
            CvInvoke.Imdecode(jpeg, ImreadModes.ReducedGrayscale8, header);
            if (!header.IsEmpty)
                tile.Frame.HeightRequest = PhotoWidth * header.Height / Math.Max(1, header.Width);
        }
        tile.Frame.Content = new Image { Source = CvViews.ToImageSource(jpeg), Aspect = Aspect.AspectFill };
        tile.Stage.Text = "Background blurred · tap to save";
        tile.View.OnTap(() => ShowLarge(jpeg));
    }

    /// <inheritdoc/>
    public override View RestoreSavedFile(ChatSavedFile file)
    {
        if (file.Kind != EditedKind)
            return null;
        PhotoTile tile = CreateTile();
        ShowPhoto(tile, file.Data);
        return tile.View;
    }

    private async void ShowLarge(byte[] jpeg)
    {
        Size page = Host.PageSize;
        var image = new Image { Source = CvViews.ToImageSource(jpeg), Aspect = Aspect.AspectFit,
            HeightRequest = Math.Max(240, Math.Min(page.Width, page.Height) * .65) };
        var save = new Button { Text = "Save photo…" };
        var status = new Label { FontSize = 12, TextColor = CvViews.Color("ChatSecondaryText"), IsVisible = false };
        SemanticProperties.SetDescription(save, "Save this photo as a JPEG file");
        save.Clicked += async (_, _) =>
        {
            if (!save.IsEnabled) return;
            save.IsEnabled = false; status.IsVisible = false;
            try
            {
                string fileName = "photo-blurred-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + ".jpg";
                string saved = await (PhotoSaver ?? PictureGallery.SaveAsync)(jpeg, fileName);
                if (saved != null) { status.Text = saved; status.IsVisible = true; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await Host.AskAsync("Couldn't save the photo", ex.Message, "OK"); }
            finally { save.IsEnabled = true; }
        };
        await Host.ShowSheetAsync("Background blurred", new VerticalStackLayout { Spacing = 10, Children = { image, save, status } }, "Close");
    }
}
