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

/// <summary>An image model <see cref="ImageFeature"/> offers.</summary>
/// <param name="Key">What tells it apart in the "image_model" setting</param>
/// <param name="Name">Its name in the list, e.g. "Bonsai Image 4B · CPU"</param>
/// <param name="Detail">A line about it in the list, or null</param>
/// <param name="StorageKey">Its entry's key in the storage catalog: its .litertlm's file name, or a folder's key
/// (<see cref="ImageFeature.StorageKey"/> for Z-Image-Turbo's)</param>
/// <param name="Create">Makes the model (owned by the feature, disposed when it lets go of it)</param>
public sealed record ImageModelOption(string Key, string Name, string Detail, string StorageKey, Func<IImageModel> Create);

/// <summary>
/// Image generation: with Settings > "Image generation" on (off by default), the model gets the generate_image tool
/// (Emgu.LiteRT.LM.Agents' <c>AgentTools.GenerateImage</c>), which draws a picture with the chosen image model
/// (<see cref="ModelOptions"/>: Z-Image-Turbo by default, or LiteRT-LM's Bonsai Image and FLUX.2-klein through
/// <see cref="OmniImageModel"/>) on this device and shows it above the reply - the model doesn't see it. The model is
/// downloaded when the setting is turned on or another model is chosen (in the background, through
/// <see cref="BackgroundDownloads"/>). The pictures are kept with the reply in the saved chat
/// (<see cref="ChatMessageItem.SavedFiles"/>, kind <see cref="GeneratedKind"/>, never given back to the model) and open
/// large with their description, seed and model, "Draw it again", "Add to Photos", Share and - with a
/// <see cref="PictureSaver"/> - Save. On iOS the model folder is kept out of iCloud backups. With the setting off the
/// tool is offered anyway and tells the model where to turn it on. A tool group of the router
/// (<see cref="ChatImageGeneration"/>). A storage feature on the page deletes a model's files through its option's
/// <see cref="ImageModelOption.StorageKey"/> (the feature is an <see cref="IStorageParticipant"/>).
/// </summary>
public sealed class ImageFeature : ChatFeature, IStorageParticipant
{
    /// <summary>The kind of a reply's saved file that is a picture it drew.</summary>
    public const string GeneratedKind = "generated";

    /// <summary>The settings key of the switch.</summary>
    public const string SettingKey = "image_generation";

    /// <summary>The settings key of the chosen image model (its <see cref="ImageModelOption.Key"/>).</summary>
    public const string ModelSettingKey = "image_model";

    /// <summary>Z-Image-Turbo's folder in a storage catalog (<c>new StorageFolder(ImageFeature.StorageKey, "Z-Image-Turbo", new ZImageTurbo().Folder, ...)</c>).</summary>
    public const string StorageKey = "image";

    /// <summary>Z-Image-Turbo's option (the default list's only one).</summary>
    public static ImageModelOption ZImageTurboOption { get; } = new ImageModelOption("z-image-turbo", "Z-Image-Turbo · CPU",
        "256 x 256, 8 steps; a 10.6 GB download and about 9 GB of caches.", StorageKey, () => new ZImageTurboImageModel());

    private readonly IIndexingWorkload _workload;
    private string _imagePauseReason;
    private Switch _imageSwitch;
    private Label _imageStatus;
    private Label _imageNote;
    private Button _imageDeleteButton;
    private Button _modelButton;
    private ImageModelOption _option;
    private IImageModel _model;
    private Task _modelInit;
    private CancellationTokenSource _download;
    private Task _downloadTask;
    private double _downloadFraction = -1;
    private bool _gpuFallbackShown;

    /// <summary>Create the feature.</summary>
    /// <param name="workload">When heavy work may run (a cool, charged device): e.g. the searches' <see cref="SearchServices.Workload"/>; null for the device's own policy</param>
    public ImageFeature(IIndexingWorkload workload = null)
    {
        _workload = workload ?? new DeviceIndexingWorkload();
    }

    /// <summary>A view of the app's own above the switch, or null (the feature shows its own choice of model when there are several).</summary>
    public Func<View> ModelSelector { get; set; }

    /// <summary>The image models to choose from, the first the default; set before the feature is attached.</summary>
    public IReadOnlyList<ImageModelOption> ModelOptions { get; set; } = new[] { ZImageTurboOption };

    /// <summary>The chosen image model's option.</summary>
    public ImageModelOption SelectedOption => _option;

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
        if (ModelOptions == null || ModelOptions.Count == 0)
            ModelOptions = new[] { ZImageTurboOption };
        string key = host.Settings.Get(ModelSettingKey, ModelOptions[0].Key);
        _option = ModelOptions.FirstOrDefault(o => o.Key == key) ?? ModelOptions[0];
        _model = _option.Create();
        ExcludeFromBackup(_model.Folder);
    }

    // ---------- IStorageParticipant ----------

    // Storage is deleting the chosen image model's files: image generation is turned off and the model let go of first.
    /// <inheritdoc/>
    public Task PrepareStorageDeleteAsync(StorageEntry entry) =>
        (entry.Kind == StorageKind.Folder || entry.Kind == StorageKind.ImageModel) && _option != null && entry.Key == _option.StorageKey
            ? TurnOffAndResetAsync() : Task.CompletedTask;

    /// <inheritdoc/>
    public void StorageChanged(StorageEntry entry) => UpdateStatus();

    /// <inheritdoc/>
    public override IEnumerable<ToolGroup> ToolGroups() => new[]
    {
        new ToolGroup(ChatImageGeneration.GroupName, ChatImageGeneration.GroupSummary, ChatImageGeneration.GroupKeywords,
            () => new AITool[] { AgentTools.GenerateImage(GenerateAsync) },
            () => ChatImageGeneration.Instructions)
    };

    // iOS backs up the app's Library/Application Support (where the image models keep their 4-21 GB) to iCloud unless
    // told not to; the model can be downloaded again.
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
        _imageNote = ChatPage.Note(string.Empty);
        UpdateStatus();
        // Turned on before and the model isn't complete: go on downloading.
        if (on && !_model.IsDownloaded)
            StartImageModelDownload();
        View modelSelector = ModelSelector?.Invoke() ?? BuildModelChoice();
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                modelSelector,
                row,
                _imageNote,
                _imageStatus,
                _imageDeleteButton
            }
        };
    }

    // A button with the chosen model's name and a list of the models, like Settings > Embedding model; nothing when
    // there is only one.
    private View BuildModelChoice()
    {
        if (ModelOptions.Count < 2)
            return new ContentView();
        var title = new Label { Text = "Image model" };
        ChatResources.ApplyStyle(title, "ChatSectionTextStyle");
        _modelButton = new Button { Text = _option.Name, HorizontalOptions = LayoutOptions.Fill };
        _modelButton.Clicked += OnChooseModelClicked;
        SemanticProperties.SetDescription(_modelButton, "Choose the model that draws pictures");
        return new VerticalStackLayout { Spacing = 6, Children = { title, _modelButton } };
    }

    private static string Gigabytes(long bytes) => bytes >= 1e9
        ? string.Format(CultureInfo.InvariantCulture, "{0:F1} GB", bytes / 1e9)
        : string.Format(CultureInfo.InvariantCulture, "{0:F0} MB", Math.Ceiling(bytes / 1e6));

    private string ModelNote() => string.Format(CultureInfo.InvariantCulture,
        "Lets the model draw pictures (\"draw a red fox in the snow\") with {0}, {1}, on this device. The model is a {2} download{3}. Nothing leaves the device.",
        _model.Name, _model.SizeLabel, Gigabytes(_model.DownloadSize),
        _model.CacheSize > 0 ? ", and the first picture prepares about " + Gigabytes(_model.CacheSize) + " more" : string.Empty);

    private async void OnChooseModelClicked(object sender, EventArgs e)
    {
        if (Host.IsBusy)
        {
            Host.SetStatus("Hang on - still busy.");
            return;
        }
        string chosen = await Host.ChooseAsync("Image model", ModelOptions.Select(o => new ChatChoice(o.Key, o.Name, o.Detail)), _option.Key);
        ImageModelOption option = ModelOptions.FirstOrDefault(o => o.Key == chosen);
        if (option == null || option == _option || Host.IsBusy)
            return;
        await SwitchModelAsync(option);
    }

    /// <summary>Choose another image model: the current one is let go of, and the new one downloaded (asked first) when image generation is on.</summary>
    /// <param name="option">One of <see cref="ModelOptions"/></param>
    public async Task SwitchModelAsync(ImageModelOption option)
    {
        if (option == null)
            throw new ArgumentNullException(nameof(option));
        await ReleaseModelAsync();
        _modelInit = null;
        _option = option;
        _model = option.Create();
        ExcludeFromBackup(_model.Folder);
        Host.Settings.Set(ModelSettingKey, option.Key);
        if (_modelButton != null)
            _modelButton.Text = option.Name;
        UpdateStatus();
        if (ImageGenerationOn && !_model.IsDownloaded && !await AskToDownloadAsync())
        {
            _imageSwitch.Toggled -= OnImageGenerationToggled;
            _imageSwitch.IsToggled = false;
            _imageSwitch.Toggled += OnImageGenerationToggled;
            Host.Settings.Set(SettingKey, false);
            UpdateStatus();
        }
    }

    // Asks before downloading the model, and starts the download on yes.
    private async Task<bool> AskToDownloadAsync()
    {
        long remaining = Math.Max(0, _model.DownloadSize - _model.DownloadedBytes);
        bool download = await Host.AskAsync("Image generation",
            string.Format(CultureInfo.InvariantCulture,
                "Download {0} ({1} remaining)? Its files{2} use about {3} in total.",
                _model.Name, Gigabytes(remaining), _model.CacheSize > 0 ? " and prepared caches" : string.Empty,
                Gigabytes(_model.DownloadSize + _model.CacheSize)),
            "Download", "Cancel");
        if (download)
            StartImageModelDownload();
        return download;
    }

    private async void OnImageGenerationToggled(object sender, ToggledEventArgs e)
    {
        Host.Settings.Set(SettingKey, e.Value);
        if (e.Value && !_model.IsDownloaded)
        {
            if (!await AskToDownloadAsync())
            {
                _imageSwitch.IsToggled = false;
                return;
            }
        }
        else if (!e.Value)
        {
            _download?.Cancel();
        }
        UpdateStatus();
    }

    private void StartImageModelDownload()
    {
        if (_download != null)
            return;
        CancellationTokenSource cancel = new CancellationTokenSource();
        _download = cancel;
        IImageModel model = _model;
        _downloadFraction = (double)model.DownloadedBytes / model.DownloadSize;
        UpdateStatus();
        _downloadTask = Task.Run(async () =>
        {
            string error = null;
            bool paused = false;
            BackgroundDownloads downloadLease = null;
            try
            {
                await using var lease = await BackgroundDownloads.BeginAsync("Image model", cancel.Token);
                downloadLease = lease;
                await model.DownloadAsync((done, total) =>
                {
                    lease.Report(total, done);
                    _downloadFraction = (double)done / total;
                    UpdateStatus();
                }, lease.Token);
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
                _download = null;
                _downloadFraction = -1;
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
        bool downloaded = _model.IsDownloaded;
        long present = _model.DownloadedBytes;
        if (_imageNote != null)
            _imageNote.Text = ModelNote();
        _imageDeleteButton.IsVisible = present > 0 && Host.Services.Get<StorageFeature>() != null;
        if (_imagePauseReason != null)
        {
            _imageStatus.IsVisible = true;
            _imageStatus.Text = "Image generation paused: " + _imagePauseReason;
        }
        else if (_download != null)
        {
            _imageStatus.IsVisible = true;
            _imageStatus.Text = string.Format(CultureInfo.InvariantCulture, "Downloading {0}: {1:P0} of {2}",
                _model.Name, Math.Max(0, _downloadFraction), Gigabytes(_model.DownloadSize));
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
        if (_modelInit == null || _modelInit.IsFaulted)
            _modelInit = _model.InitAsync(CpuThreadSettings.Count);
        return _modelInit;
    }

    /// <summary>The generate_image tool: draw the picture, show it above the reply, and tell the model what happened.</summary>
    /// <param name="prompt">What to draw, in English</param>
    /// <param name="cancel">Stops the drawing</param>
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancel)
    {
        if (!ImageGenerationOn)
            return string.Format(CultureInfo.InvariantCulture,
                "Image generation is turned off, so no picture can be drawn. Tell the user they can turn on \"Image generation\" in {0}; the image model ({1}) is a {2} download and runs on this device.",
                Host.SettingsLocation, _model.Name, Gigabytes(_model.DownloadSize));
        if (_download != null)
            return string.Format(CultureInfo.InvariantCulture,
                "The image model is still downloading ({0:P0} done), so no picture can be drawn yet. Tell the user to try again when it has finished.",
                Math.Max(0, _downloadFraction));
        if (!_model.IsDownloaded)
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
        IImageModel model = _model;
        await EnsureModelAsync();
        if (model is OmniImageModel { GpuFailure: not null } && !_gpuFallbackShown)
        {
            _gpuFallbackShown = true;
            MainThread.BeginInvokeOnMainThread(() => Host.SetStatus("The GPU couldn't load the image model, so it draws on the CPU (slower)."));
        }
        string tooLong = model.CheckPrompt(prompt);
        if (tooLong != null)
            return tooLong;

        long seed = Random.Shared.NextInt64(1, 1L << 40);
        ImageTile tile = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ImageTile created = CreateImageTile(prompt);
            Host.AddCardToReply(created.View);
            return created;
        });
        Stopwatch watch = Stopwatch.StartNew();
        // A model that doesn't report its steps (LiteRT-LM's draw a picture in one call) shows how long it has taken.
        using var ticking = new CancellationTokenSource();
        if (!model.ReportsSteps)
            _ = TickAsync(tile, watch, ticking.Token);
        try
        {
            // The tool runs off the UI thread, so the progress is marshalled to it here.
            ImageModelPicture result = await model.GenerateAsync(prompt, seed, stage =>
            {
                // Reported synchronously on the worker thread, before the stage's native graph runs. Keep the current
                // latent state while waiting; cancellation still stops the pending generation.
                _workload.WaitAsync(reason => MainThread.BeginInvokeOnMainThread(() =>
                {
                    _imagePauseReason = reason;
                    UpdateStatus();
                    tile.Stage.Text = reason == null ? stage : "Image generation paused: " + reason;
                }), cancel).GetAwaiter().GetResult();
                string text = stage + string.Format(CultureInfo.InvariantCulture, " ({0:F0} s)", watch.Elapsed.TotalSeconds);
                MainThread.BeginInvokeOnMainThread(() => tile.Stage.Text = text);
            }, cancel);
            ticking.Cancel();
            byte[] png = await Task.Run(() => AddPictureDetails(PngEncoder.Encode(result.Rgb, result.Width, result.Height), prompt, seed, model));
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
            ticking.Cancel();
            await MainThread.InvokeOnMainThreadAsync(() => tile.Stage.Text = "Stopped");
            throw;
        }
        catch (Exception ex)
        {
            ticking.Cancel();
            await MainThread.InvokeOnMainThreadAsync(() => tile.Stage.Text = "Couldn't draw it: " + ex.Message);
            return "The picture couldn't be drawn: " + ex.Message;
        }
    }

    // "Drawing the picture (12 s)" under the tile every second until stopped.
    private static async Task TickAsync(ImageTile tile, Stopwatch watch, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                string text = string.Format(CultureInfo.InvariantCulture, "Drawing the picture ({0:F0} s)", watch.Elapsed.TotalSeconds);
                MainThread.BeginInvokeOnMainThread(() => { if (!stop.IsCancellationRequested) tile.Stage.Text = text; });
                await Task.Delay(1000, stop);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // The description, seed and model go into the PNG (PngMetadata): a saved chat keeps only the picture, and a saved
    // or shared file then says how it was made.
    private static byte[] AddPictureDetails(byte[] png, string prompt, long seed, IImageModel model)
    {
        png = PngMetadata.AddText(png, "Description", prompt);
        png = PngMetadata.AddText(png, "Seed", seed.ToString(CultureInfo.InvariantCulture));
        return PngMetadata.AddText(png, "Software", string.Format(CultureInfo.InvariantCulture, "{0}{1}, {2} steps",
            SoftwarePrefix, model.Name, model.Steps));
    }

    private const string SoftwarePrefix = "Emgu LiteRT, ";

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
        details.TryGetValue("Software", out string software);
        // "Emgu LiteRT, Bonsai Image 4B, 4 steps": the model and its steps.
        string madeWith = software != null && software.StartsWith(SoftwarePrefix, StringComparison.Ordinal)
            ? software.Substring(SoftwarePrefix.Length) : null;
        var status = new Label { IsVisible = false };
        ChatResources.ApplyStyle(status, "ChatSectionNoteStyle");
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string fileName = "generated-image-" + stamp + ".png";
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Children.Add(image);
        if (!string.IsNullOrEmpty(description))
        {
            // What it was drawn from, so it can be made again; the seed is what makes this exact picture.
            var caption = new Label { Text = description + (string.IsNullOrEmpty(seed) ? "" : "\nSeed " + seed)
                + (madeWith == null ? "" : (string.IsNullOrEmpty(seed) ? "\n" : " · ") + madeWith) };
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
    public bool ModelLoaded => _modelInit != null;

    /// <summary>Forget the prompts' cached encodings (a private chat began or ended).</summary>
    public void ClearPromptCache() => _model?.ClearPromptCache();

    /// <summary>
    /// Release the model and start again with a new instance, loaded on the next picture (the CPU thread count changed,
    /// a private chat began or ended).
    /// </summary>
    public async Task ResetModelAsync()
    {
        await ReleaseModelAsync();
        _modelInit = null;
        if (_model != null)
            _model = _option.Create();
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
        _download?.Cancel();
        try { await (_downloadTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
        IImageModel model = _model;
        if (model == null)
            return;
        try { await (_modelInit ?? Task.CompletedTask); } catch (Exception) { }
        await Task.Run(() => model.Dispose());
    }
}
