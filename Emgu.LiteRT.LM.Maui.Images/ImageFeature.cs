//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.Models;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui.Images;

/// <summary>
/// Image generation: with Settings > "Image generation" on (off by default), the model gets the generate_image tool
/// (Emgu.LiteRT.LM.Agents' <c>AgentTools.GenerateImage</c>), which draws a 256 x 256 picture with Z-Image-Turbo
/// (Emgu.LiteRT.Models' <see cref="ZImageTurbo"/>) on this device's CPU and shows it above the reply - the model doesn't
/// see it. The model is 10.6 GB, downloaded when the setting is turned on (in the background, through
/// <see cref="BackgroundDownloads"/>), and its first image writes ~9 GB of weight caches. The pictures are kept with the
/// reply in the saved chat (<see cref="ChatMessageItem.SavedFiles"/>, kind <see cref="GeneratedKind"/>, never given back
/// to the model) and open large with their description and seed, "Draw it again", "Add to Photos", Share and - with a
/// <see cref="PictureSaver"/> - Save. On iOS the model folder is kept out of iCloud backups. With the setting off the
/// tool is offered anyway and tells the model where to turn it on. A tool group of the router
/// (<see cref="ChatImageGeneration"/>). A storage feature on the page deletes the model's folder through
/// <see cref="StorageKey"/> (the feature is an <see cref="IStorageParticipant"/>).
/// </summary>
public sealed class ImageFeature : ChatFeature, IStorageParticipant
{
    /// <summary>The kind of a reply's saved file that is a picture it drew.</summary>
    public const string GeneratedKind = "generated";

    /// <summary>The settings key of the switch.</summary>
    public const string SettingKey = "image_generation";

    /// <summary>The image model's folder in a storage catalog (<c>new StorageFolder(ImageFeature.StorageKey, "Z-Image-Turbo", new ZImageTurbo().Folder, ...)</c>).</summary>
    public const string StorageKey = "image";

    private readonly IIndexingWorkload _workload;
    private string _imagePauseReason;
    private Switch _imageSwitch;
    private Label _imageStatus;
    private Button _imageDeleteButton;
    private ZImageTurbo _zimage;
    private Task _zimageInit;
    private CancellationTokenSource _zimageDownload;
    private Task _zimageDownloadTask;
    private double _zimageDownloadFraction = -1;

    /// <summary>Create the feature.</summary>
    /// <param name="workload">When heavy work may run (a cool, charged device): e.g. the searches' <see cref="SearchServices.Workload"/>; null for the device's own policy</param>
    public ImageFeature(IIndexingWorkload workload = null)
    {
        _workload = workload ?? new DeviceIndexingWorkload();
    }

    /// <summary>A view of the app's own above the switch (e.g. its choice of image model), or null.</summary>
    public Func<View> ModelSelector { get; set; }

    /// <summary>
    /// Saves a picture where the user chooses (a Save As dialog, e.g. CommunityToolkit.Maui's FileSaver): given the PNG
    /// and a file name, returns what to tell the user, or null when they cancelled. Without it the sheet has no Save button.
    /// </summary>
    public Func<byte[], string, Task<string>> PictureSaver { get; set; }

    /// <inheritdoc/>
    public override string Name => "images";

    /// <inheritdoc/>
    public override string Title => "Image generation";

    /// <summary>The switch is the feature's own setting, not the page's feature switch.</summary>
    public override bool PersistEnabled => false;

    /// <inheritdoc/>
    public override void RegisterServices(ChatFeatureServices services)
    {
        services.Add(this);
        services.Add<IStorageParticipant>(this);
    }

    /// <inheritdoc/>
    public override void OnAttached(IChatPageHost host)
    {
        base.OnAttached(host);
        _zimage = new ZImageTurbo();
        ExcludeFromBackup(_zimage.Folder);
    }

    // ---------- IStorageParticipant ----------

    // Storage is deleting the image model's folder: image generation is turned off and the model let go of first.
    /// <inheritdoc/>
    public Task PrepareStorageDeleteAsync(StorageEntry entry) =>
        entry.Kind == StorageKind.Folder && entry.Key == StorageKey ? TurnOffAndResetAsync() : Task.CompletedTask;

    /// <inheritdoc/>
    public void StorageChanged(StorageEntry entry) => UpdateStatus();

    /// <inheritdoc/>
    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatImageGeneration.GroupName, ChatImageGeneration.GroupSummary, ChatImageGeneration.GroupKeywords,
            () => new AITool[] { AgentTools.GenerateImage(GenerateAsync) },
            () => ChatImageGeneration.Instructions)
    };

    // iOS backs up the app's Library/Application Support (where ZImageTurbo keeps its ~21 GB) to iCloud unless told
    // not to; the model can be downloaded again.
    private static void ExcludeFromBackup(string folder)
    {
#if IOS && !MACCATALYST
        try
        {
            System.IO.Directory.CreateDirectory(folder);
            Foundation.NSUrl.FromFilename(folder).SetResource(Foundation.NSUrl.IsExcludedFromBackupKey,
                Foundation.NSNumber.FromBoolean(true), out Foundation.NSError _);
        }
        catch (Exception)
        {
        }
#endif
    }

    private bool ImageGenerationOn => _imageSwitch != null && _imageSwitch.IsToggled;

    private static Color Accent => ChatResources.FindColor("ChatAccent");

    /// <inheritdoc/>
    public override View BuildSettingsSection()
    {
        bool on = Host.Settings.Get(SettingKey, false);
        _imageSwitch = new Switch { IsToggled = on, OnColor = Accent, VerticalOptions = LayoutOptions.Center };
        _imageSwitch.Toggled += OnImageGenerationToggled;
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var title = new Label { Text = "Image generation", VerticalOptions = LayoutOptions.Center };
        ChatResources.ApplyStyle(title, "ChatSectionTextStyle");
        row.Add(title, 0, 0);
        row.Add(_imageSwitch, 1, 0);
        _imageStatus = new Label { FontSize = 13, TextColor = Accent, IsVisible = false };
        _imageDeleteButton = new Button
        {
            Text = "Manage image model storage...",
            FontSize = 13,
            TextColor = Accent,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false
        };
        _imageDeleteButton.Clicked += OnDeleteImageModelClicked;
        UpdateStatus();
        // Turned on before and the model isn't complete: go on downloading.
        if (on && !_zimage.IsDownloaded)
            StartImageModelDownload();
        View modelSelector = ModelSelector?.Invoke() ?? new ContentView();
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                modelSelector,
                row,
                ChatPage.Note(string.Format(CultureInfo.InvariantCulture,
                    "Lets the model draw pictures (\"draw a red fox in the snow\") with Z-Image-Turbo, 256 x 256, on this device. The model is a {0:F1} GB download, and the first picture prepares about 9 GB more; a picture takes about half a minute on a computer, longer on a phone. Nothing leaves the device.",
                    ZImageTurbo.DownloadSize / 1e9)),
                _imageStatus,
                _imageDeleteButton
            }
        };
    }

    private async void OnImageGenerationToggled(object sender, ToggledEventArgs e)
    {
        Host.Settings.Set(SettingKey, e.Value);
        if (e.Value && !_zimage.IsDownloaded)
        {
            long remaining = Math.Max(0, ZImageTurbo.DownloadSize - _zimage.DownloadedBytes);
            string remainingSize = remaining >= 1e9
                ? string.Format(CultureInfo.InvariantCulture, "{0:F1} GB", remaining / 1e9)
                : string.Format(CultureInfo.InvariantCulture, "{0:F0} MB", Math.Ceiling(remaining / 1e6));
            bool download = await Host.AskAsync("Image generation",
                string.Format(CultureInfo.InvariantCulture,
                    "Download the image model ({0} remaining)? Model files and prepared caches use about {1:F0} GB in total.",
                    remainingSize, (ZImageTurbo.DownloadSize + 9e9) / 1e9),
                "Download", "Cancel");
            if (!download)
            {
                _imageSwitch.IsToggled = false;
                return;
            }
            StartImageModelDownload();
        }
        else if (!e.Value)
        {
            _zimageDownload?.Cancel();
        }
        UpdateStatus();
    }

    private void StartImageModelDownload()
    {
        if (_zimageDownload != null)
            return;
        CancellationTokenSource cancel = new CancellationTokenSource();
        _zimageDownload = cancel;
        _zimageDownloadFraction = (double)_zimage.DownloadedBytes / ZImageTurbo.DownloadSize;
        UpdateStatus();
        _zimageDownloadTask = Task.Run(async () =>
        {
            string error = null;
            bool paused = false;
            BackgroundDownloads downloadLease = null;
            try
            {
                await using var lease = await BackgroundDownloads.BeginAsync("Image model", cancel.Token);
                downloadLease = lease;
                await _zimage.Download(new InlineProgress<ZImageDownloadProgress>(p =>
                {
                    lease.Report(p.TotalBytes, p.BytesDone);
                    _zimageDownloadFraction = (double)p.BytesDone / p.TotalBytes;
                    UpdateStatus();
                }), lease.Token);
            }
            catch (Exception) when (cancel.IsCancellationRequested || downloadLease?.Token.IsCancellationRequested == true)
            {
                paused = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _zimageDownload = null;
                _zimageDownloadFraction = -1;
                UpdateStatus();
                if (_imageStatus == null)
                    return;
                if (error != null)
                    _imageStatus.Text = "Download stopped: " + error + " (turn the switch off and on to retry)";
                else if (paused && _imageSwitch.IsToggled)
                    _imageStatus.Text = "Download paused. Turn Image generation off and on to continue from the saved bytes.";
            });
        });
    }

    /// <summary>Show the model's state in the settings again (downloading, ready, paused...).</summary>
    public void UpdateStatus()
    {
        if (_imageStatus == null)
            return;
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(UpdateStatus);
            return;
        }
        bool downloaded = _zimage.IsDownloaded;
        long present = _zimage.DownloadedBytes;
        _imageDeleteButton.IsVisible = present > 0 && Host.Services.Get<StorageFeature>() != null;
        if (_imagePauseReason != null)
        {
            _imageStatus.IsVisible = true;
            _imageStatus.Text = "Image generation paused: " + _imagePauseReason;
        }
        else if (_zimageDownload != null)
        {
            _imageStatus.IsVisible = true;
            _imageStatus.Text = string.Format(CultureInfo.InvariantCulture, "Downloading the image model: {0:P0} of {1:F1} GB",
                Math.Max(0, _zimageDownloadFraction), ZImageTurbo.DownloadSize / 1e9);
        }
        else if (ImageGenerationOn)
        {
            _imageStatus.IsVisible = true;
            _imageStatus.Text = downloaded ? "Ready" : "The image model isn't downloaded";
        }
        else
        {
            _imageStatus.IsVisible = false;
        }
    }

    private async void OnDeleteImageModelClicked(object sender, EventArgs e)
    {
        StorageFeature storage = Host.Services.Get<StorageFeature>();
        if (storage != null)
            await storage.OpenStorageAsync();
    }

    /// <summary>Load the model (once; again after <see cref="ResetModelAsync"/>), with the page's CPU thread count.</summary>
    public Task EnsureModelAsync()
    {
        if (_zimageInit == null || _zimageInit.IsFaulted)
        {
            _zimage.NumThreads = CpuThreadSettings.Count;
            _zimageInit = _zimage.Init();
        }
        return _zimageInit;
    }

    /// <summary>The generate_image tool: draw the picture, show it above the reply, and tell the model what happened.</summary>
    /// <param name="prompt">What to draw, in English</param>
    /// <param name="cancel">Stops the drawing</param>
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancel)
    {
        if (!ImageGenerationOn)
            return string.Format(CultureInfo.InvariantCulture,
                "Image generation is turned off, so no picture can be drawn. Tell the user they can turn on \"Image generation\" in {0}; the image model is a {1:F1} GB download and runs on this device.",
                Host.SettingsLocation,
                ZImageTurbo.DownloadSize / 1e9);
        if (_zimageDownload != null)
            return string.Format(CultureInfo.InvariantCulture,
                "The image model is still downloading ({0:P0} done), so no picture can be drawn yet. Tell the user to try again when it has finished.",
                Math.Max(0, _zimageDownloadFraction));
        if (!_zimage.IsDownloaded)
            return "The image model isn't downloaded. Tell the user to turn \"Image generation\" off and on in the settings to download it.";
        prompt = (prompt ?? string.Empty).Trim();
        if (prompt.Length == 0)
            return "The description was empty: call generate_image again with what to draw.";

        Action<string> modelPaused = reason => MainThread.BeginInvokeOnMainThread(() =>
        {
            _imagePauseReason = reason;
            UpdateStatus();
        });
        await _workload.WaitAsync(modelPaused, cancel);
        await EnsureModelAsync();
        int tokens = _zimage.CountPromptTokens(prompt);
        if (tokens > ZImageTurbo.MaxPromptTokens)
            return string.Format(CultureInfo.InvariantCulture,
                "The description is too long for the image model ({0} tokens, at most {1} fit). Call generate_image again with a shorter English description of about 15 words, keeping the subject and the style.",
                tokens, ZImageTurbo.MaxPromptTokens);

        long seed = Random.Shared.NextInt64(1, 1L << 40);
        ImageTile tile = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ImageTile created = CreateImageTile(prompt);
            Host.AddCardToReply(created.View);
            return created;
        });
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            // The tool runs off the UI thread, so the progress is marshalled to it here.
            ZImageResult result = await _zimage.Generate(prompt, seed, 8, new InlineProgress<ZImageProgress>(p =>
            {
                // Generate reports each stage synchronously on its worker thread, before the native graph runs.
                // Keep the current latent state while waiting; cancellation still stops the pending generation.
                if (p.Stage != "Done")
                    _workload.WaitAsync(reason => MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _imagePauseReason = reason;
                        UpdateStatus();
                        tile.Stage.Text = reason == null ? p.Stage : "Image generation paused: " + reason;
                    }), cancel).GetAwaiter().GetResult();
                string text = p.Stage + string.Format(CultureInfo.InvariantCulture, " ({0:F0} s)", watch.Elapsed.TotalSeconds);
                MainThread.BeginInvokeOnMainThread(() => tile.Stage.Text = text);
            }), cancel);
            byte[] png = await Task.Run(() => AddPictureDetails(PngEncoder.Encode(result), prompt, seed));
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ShowGeneratedImage(tile, png, string.Format(CultureInfo.InvariantCulture, "{0:F0} s", result.Elapsed.TotalSeconds));
                // Kept with the reply (shown again when the chat is reopened, not given to the model).
                Host.AddCardToReply(null, new ChatSavedFile(GeneratedKind, "image/png", png));
            });
            return AgentTools.ImageGeneratedResult(prompt);
        }
        catch (OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() => tile.Stage.Text = "Stopped");
            throw;
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => tile.Stage.Text = "Couldn't draw it: " + ex.Message);
            return "The picture couldn't be drawn: " + ex.Message;
        }
    }

    // IProgress without Progress<T>'s capture of the current SynchronizationContext (none on a worker thread):
    // the handler runs where Report is called.
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public InlineProgress(Action<T> handler)
        {
            _handler = handler;
        }

        public void Report(T value)
        {
            _handler(value);
        }
    }

    // The description and seed go into the PNG (PngMetadata): a saved chat keeps only the picture, and a saved or shared
    // file then says how it was made.
    private static byte[] AddPictureDetails(byte[] png, string prompt, long seed)
    {
        png = PngMetadata.AddText(png, "Description", prompt);
        png = PngMetadata.AddText(png, "Seed", seed.ToString(CultureInfo.InvariantCulture));
        return PngMetadata.AddText(png, "Software", "Emgu LiteRT, Z-Image-Turbo, 8 steps");
    }

    private sealed class ImageTile
    {
        public VerticalStackLayout View;
        public Border Frame;
        public Label Stage;
    }

    private const double GeneratedImageSize = 220;

    private static ImageSource ToImageSource(byte[] png) => ImageSource.FromStream(() => new System.IO.MemoryStream(png));

    // A picture being drawn, above the reply: a placeholder with a spinner and the stage, then the picture.
    private ImageTile CreateImageTile(string prompt)
    {
        ImageTile tile = new ImageTile();
        tile.Frame = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Stroke = Colors.Transparent,
            WidthRequest = GeneratedImageSize,
            HeightRequest = GeneratedImageSize,
            Content = new ActivityIndicator { IsRunning = true, Color = Accent, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        };
        ChatResources.SetThemeColor(tile.Frame, VisualElement.BackgroundColorProperty, "ChatSurface");
        tile.Stage = new Label { Text = "Starting..." };
        ChatResources.ApplyStyle(tile.Stage, "ChatListDetailStyle");
        tile.View = new VerticalStackLayout
        {
            Spacing = 4,
            Margin = new Thickness(6, 0),
            HorizontalOptions = LayoutOptions.Start,
            Children = { tile.Frame, tile.Stage }
        };
        SemanticProperties.SetDescription(tile.View, "Picture of " + prompt);
        return tile;
    }

    private void ShowGeneratedImage(ImageTile tile, byte[] png, string caption)
    {
        tile.Frame.Content = new Image { Source = ToImageSource(png), Aspect = Aspect.AspectFill };
        tile.Stage.Text = caption;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) => ShowGeneratedImageLarge(png);
        tile.View.GestureRecognizers.Add(tap);
    }

    /// <inheritdoc/>
    public override View RestoreSavedFile(ChatSavedFile file) => file.Kind == GeneratedKind ? SavedImageView(file.Data) : null;

    /// <summary>A picture a saved reply drew, for above it again when the chat is reopened.</summary>
    /// <param name="png">The picture</param>
    public View SavedImageView(byte[] png)
    {
        ImageTile tile = CreateImageTile(string.Empty);
        ShowGeneratedImage(tile, png, string.Empty);
        tile.Stage.IsVisible = false;
        return tile.View;
    }

    private async void ShowGeneratedImageLarge(byte[] png)
    {
        Size page = Host.PageSize;
        Image image = new Image
        {
            Source = ToImageSource(png),
            Aspect = Aspect.AspectFit,
            HeightRequest = Math.Max(200, Math.Min(page.Width, page.Height) * 0.45)
        };
        var details = PngMetadata.ReadText(png);
        details.TryGetValue("Description", out string description);
        details.TryGetValue("Seed", out string seed);
        var status = new Label { IsVisible = false };
        ChatResources.ApplyStyle(status, "ChatSectionNoteStyle");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = "generated-image-" + stamp + ".png";
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Children.Add(image);
        if (!string.IsNullOrEmpty(description))
        {
            // What it was drawn from, so it can be made again; the seed is what makes this exact picture.
            var caption = new Label { Text = description + (string.IsNullOrEmpty(seed) ? "" : "\nSeed " + seed + " · 8 steps") };
            ChatResources.ApplyStyle(caption, "ChatSectionTextStyle");
            SemanticProperties.SetDescription(caption, "Description and seed of this picture");
            content.Children.Add(caption);
        }

        Button Action(string text, string hint)
        {
            var b = new Button { Text = text, HorizontalOptions = LayoutOptions.Fill };
            SemanticProperties.SetDescription(b, hint);
            return b;
        }
        // Runs a button's work with the button off, and tells the user how it went.
        async Task Run(Button button, Func<Task<string>> work)
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            status.IsVisible = false;
            try
            {
                string done = await work();
                if (done != null)
                {
                    status.Text = done;
                    status.IsVisible = true;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await Host.AskAsync("Couldn't " + button.Text.TrimEnd('…').ToLowerInvariant(), ex.Message, "OK", null);
            }
            finally { button.IsEnabled = true; }
        }

        var again = Action("Draw it again", "Draw the same description again with a new seed");
        again.IsVisible = !string.IsNullOrEmpty(description);
        again.Clicked += (_, _) => Host.DismissSheet("again");
        var gallery = Action("Add to " + PictureGallery.DestinationName, "Add the picture to the device's " + PictureGallery.DestinationName);
        gallery.Clicked += async (_, _) => await Run(gallery, async () => await PictureGallery.SaveAsync(png, fileName));
        var share = Action("Share…", "Share the picture with another app");
        share.Clicked += async (_, _) => await Run(share, async () =>
        {
            string path = System.IO.Path.Combine(FileSystem.CacheDirectory, fileName);
            await System.IO.File.WriteAllBytesAsync(path, png);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "Picture", File = new ShareFile(path, "image/png") });
            return null;
        });
        // Two by two, so the sheet doesn't need scrolling to reach them.
        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            RowSpacing = 8
        };
        buttons.Add(again, 0, 0);
        buttons.Add(gallery, 1, 0);
        buttons.Add(share, 0, 1);
        Func<byte[], string, Task<string>> saver = PictureSaver;
        if (saver != null)
        {
            var save = Action("Save image…", "Save the original generated image as a PNG file");
            save.Clicked += async (_, _) => await Run(save, () => saver(png, fileName));
            buttons.Add(save, 1, 1);
        }
        content.Children.Add(buttons);
        content.Children.Add(status);
        string choice = await Host.ShowSheetAsync("Picture", content, "Close");
        if (choice == "again" && !string.IsNullOrEmpty(description) && !Host.IsBusy)
            await Host.SendMessageAsync("Draw this again, using exactly this description: " + description);
    }

    /// <summary>Whether the model has been loaded (or is loading).</summary>
    public bool ModelLoaded => _zimageInit != null;

    /// <summary>Forget the prompts' cached encodings (a private chat began or ended).</summary>
    public void ClearPromptCache() => _zimage?.ClearPromptCache();

    /// <summary>
    /// Release the model and start again with a new instance, loaded on the next picture (the CPU thread count changed,
    /// a private chat began or ended).
    /// </summary>
    public async Task ResetModelAsync()
    {
        await ReleaseModelAsync();
        _zimageInit = null;
        if (_zimage != null)
            _zimage = new ZImageTurbo();
    }

    /// <summary>Storage deleted the model: image generation is turned off and the model let go of.</summary>
    public async Task TurnOffAndResetAsync()
    {
        if (_imageSwitch != null)
        {
            _imageSwitch.Toggled -= OnImageGenerationToggled;
            _imageSwitch.IsToggled = false;
            _imageSwitch.Toggled += OnImageGenerationToggled;
        }
        Host?.Settings.Set(SettingKey, false);
        await ResetModelAsync();
    }

    /// <summary>Release the model (and stop its download), e.g. when the page closes.</summary>
    public async Task ReleaseModelAsync()
    {
        _zimageDownload?.Cancel();
        try { await (_zimageDownloadTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
        ZImageTurbo model = _zimage;
        if (model == null)
            return;
        try { await (_zimageInit ?? Task.CompletedTask); } catch (Exception) { }
        await Task.Run(() => model.Dispose());
    }
}
