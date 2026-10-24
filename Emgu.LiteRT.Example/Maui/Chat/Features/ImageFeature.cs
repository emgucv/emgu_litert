//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.LiteRT.Models;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite
{
    // Image generation: with Settings > "Image generation" on (off by default), Gemma 4 gets the generate_image tool
    // (Emgu.LiteRT.LM.Agents' AgentTools.GenerateImage), which draws a 256 x 256 picture with Z-Image-Turbo
    // (Emgu.LiteRT.Models' ZImageTurbo) on this device's CPU and shows it above the reply - the model doesn't see it. The
    // model is 10.6 GB, downloaded when the setting is turned on, and its first image writes ~9 GB of weight caches. The
    // pictures are saved with the reply (attachments of kind "generated", never given back to the model). On iOS the model
    // folder is kept out of iCloud backups. With the setting off the tool is offered anyway and tells the model where to turn it on.
    internal sealed class ImageFeature
    {
        private const string ImageGenerationPreference = "litert_lm_chat_image_generation";
        public const string GeneratedKind = "generated";
        public const string Instructions = ChatPlannerInstructions.Images;

        private readonly IChatHost _host;
        private string _imagePauseReason;

        public ImageFeature(IChatHost host) { _host = host; }

        private Switch _imageSwitch;
        private Label _imageStatus;
        private Button _imageDeleteButton;
        private ZImageTurbo _zimage;
        private Task _zimageInit;
        private CancellationTokenSource _zimageDownload;
        private Task _zimageDownloadTask;
        private double _zimageDownloadFraction = -1;
        // The pictures drawn for the reply being written, saved with it.
        private readonly List<byte[]> _sendImages = new List<byte[]>();

        // Everywhere the chat runs.
        public static bool Supported => true;

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

        public View BuildSection(View modelSelector)
        {
            if (!Supported)
                return new ContentView { IsVisible = false };
            _zimage = new ZImageTurbo();
            ExcludeFromBackup(_zimage.Folder);
            bool on = false;
            try { on = Preferences.Default.Get(ImageGenerationPreference, false); } catch (Exception) { }
            _imageSwitch = new Switch { IsToggled = on, OnColor = Theme.Accent, VerticalOptions = LayoutOptions.Center };
            _imageSwitch.Toggled += OnImageGenerationToggled;
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new Label
            {
                Text = "Image generation",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            row.Add(_imageSwitch, 1, 0);
            _imageStatus = new Label { FontFamily = Theme.TitleFont, FontSize = 13, TextColor = Theme.Accent, IsVisible = false };
            _imageDeleteButton = new Button
            {
                Text = "Manage image model storage...",
                FontFamily = Theme.BodyFont,
                FontSize = 13,
                TextColor = Theme.Accent,
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
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    modelSelector,
                    row,
                    new Label
                    {
                        Text = string.Format(CultureInfo.InvariantCulture,
                            "Lets Gemma 4 draw pictures (\"draw a red fox in the snow\") with Z-Image-Turbo, 256 x 256, on this device. The model is a {0:F1} GB download, and the first picture prepares about 9 GB more; a picture takes about half a minute on a computer, longer on a phone. Nothing leaves the device.",
                            ZImageTurbo.DownloadSize / 1e9),
                        FontFamily = Theme.BodyFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText
                    },
                    _imageStatus,
                    _imageDeleteButton
                }
            };
        }

        private async void OnImageGenerationToggled(object sender, ToggledEventArgs e)
        {
            try { Preferences.Default.Set(ImageGenerationPreference, e.Value); } catch (Exception) { }
            if (e.Value && !_zimage.IsDownloaded)
            {
                long remaining = Math.Max(0, ZImageTurbo.DownloadSize - _zimage.DownloadedBytes);
                string remainingSize = remaining >= 1e9
                    ? string.Format(CultureInfo.InvariantCulture, "{0:F1} GB", remaining / 1e9)
                    : string.Format(CultureInfo.InvariantCulture, "{0:F0} MB", Math.Ceiling(remaining / 1e6));
                bool download = await _host.AskAsync("Image generation",
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
                    if (error != null)
                        _imageStatus.Text = "Download stopped: " + error + " (turn the switch off and on to retry)";
                    else if (paused && _imageSwitch.IsToggled)
                        _imageStatus.Text = "Download paused. Turn Image generation off and on to continue from the saved bytes.";
                });
            });
        }

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
            _imageDeleteButton.IsVisible = present > 0;
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
            await _host.OpenStorageAsync();
        }

        public Task EnsureModelAsync()
        {
            if (_zimageInit == null || _zimageInit.IsFaulted)
            {
                _zimage.NumThreads = CpuThreadSettings.Count;
                _zimageInit = _zimage.Init();
            }
            return _zimageInit;
        }

        // The generate_image tool: draw the picture, show it above the reply, and tell the model what happened.
        public async Task<string> GenerateAsync(string prompt, CancellationToken cancel)
        {
            if (!Supported)
                return "Image generation isn't available on this device.";
            if (!ImageGenerationOn)
                return string.Format(CultureInfo.InvariantCulture,
                    "Image generation is turned off, so no picture can be drawn. Tell the user they can turn on \"Image generation\" in {0}; the image model is a {1:F1} GB download and runs on this device.",
                    _host.SettingsLocation,
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
            await _host.WaitForWorkAsync(false, modelPaused, cancel);
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
                _host.AddCardToReply(created.View);
                return created;
            });
            Stopwatch watch = Stopwatch.StartNew();
            UiStallWatchdog.Mark("generate_image: " + prompt);
            try
            {
                // The tool runs off the UI thread, so the progress is marshalled to it here.
                ZImageResult result = await _zimage.Generate(prompt, seed, 8, new InlineProgress<ZImageProgress>(p =>
                {
                    // Generate reports each stage synchronously on its worker thread, before the native graph runs.
                    // Keep the current latent state while waiting; cancellation still stops the pending generation.
                    if (p.Stage != "Done")
                        _host.WaitForWorkAsync(false, reason => MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _imagePauseReason = reason;
                            UpdateStatus();
                            tile.Stage.Text = reason == null ? p.Stage : "Image generation paused: " + reason;
                        }), cancel).GetAwaiter().GetResult();
                    string text = p.Stage + string.Format(CultureInfo.InvariantCulture, " ({0:F0} s)", watch.Elapsed.TotalSeconds);
                    MainThread.BeginInvokeOnMainThread(() => tile.Stage.Text = text);
                }), cancel);
                byte[] png = await Task.Run(() => AddPictureDetails(EncodePng(result), prompt, seed));
                lock (_sendImages)
                    _sendImages.Add(png);
                await MainThread.InvokeOnMainThreadAsync(() => ShowGeneratedImage(tile, png,
                    string.Format(CultureInfo.InvariantCulture, "{0:F0} s", result.Elapsed.TotalSeconds)));
                UiStallWatchdog.Mark(string.Format(CultureInfo.InvariantCulture, "generate_image done in {0:F1} s", result.Elapsed.TotalSeconds));
                return Emgu.LiteRT.LM.Agents.AgentTools.ImageGeneratedResult(prompt);
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

        private static byte[] EncodePng(ZImageResult result)
        {
            using Mat rgb = new Mat(result.Height, result.Width, Emgu.CV.CvEnum.DepthType.Cv8U, 3);
            rgb.SetTo(result.Rgb);
            using Mat bgr = new Mat();
            CvInvoke.CvtColor(rgb, bgr, Emgu.CV.CvEnum.ColorConversion.Rgb2Bgr);
            return ImageUtil.Encode(bgr);
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

        // A picture being drawn, above the reply: a placeholder with a spinner and the stage, then the picture.
        private ImageTile CreateImageTile(string prompt)
        {
            ImageTile tile = new ImageTile();
            tile.Frame = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                Stroke = Colors.Transparent,
                BackgroundColor = Theme.TileBackground,
                WidthRequest = GeneratedImageSize,
                HeightRequest = GeneratedImageSize,
                Content = new ActivityIndicator { IsRunning = true, Color = Theme.Accent, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            tile.Stage = new Label { Text = "Starting...", FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText };
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

        // The Windows file picker reports cancelling as a plain exception, "Operation cancelled or Path doesn't exist.",
        // not an OperationCanceledException (Apple and Android do use that).
        internal static bool SaveWasCancelled(Exception exception)
        {
            return exception is OperationCanceledException
                || (exception != null && exception.Message.StartsWith("Operation cancelled", StringComparison.OrdinalIgnoreCase));
        }

        private void ShowGeneratedImage(ImageTile tile, byte[] png, string caption)
        {
            tile.Frame.Content = new Image { Source = ImageUtil.ToImageSource(png), Aspect = Aspect.AspectFill };
            tile.Stage.Text = caption;
            tile.View.OnTap(() => ShowGeneratedImageLarge(png));
        }

        // A picture a saved reply drew, for above it again when the chat is reopened.
        public View SavedImageView(byte[] png)
        {
            ImageTile tile = CreateImageTile(string.Empty);
            ShowGeneratedImage(tile, png, string.Empty);
            tile.Stage.IsVisible = false;
            return tile.View;
        }

        private async void ShowGeneratedImageLarge(byte[] png)
        {
            Image image = new Image
            {
                Source = ImageUtil.ToImageSource(png),
                Aspect = Aspect.AspectFit,
                HeightRequest = Math.Max(200, Math.Min(_host.PageSize.Width, _host.PageSize.Height) * 0.45)
            };
            var details = PngMetadata.ReadText(png);
            details.TryGetValue("Description", out string description);
            details.TryGetValue("Seed", out string seed);
            var status = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false };
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string fileName = "generated-image-" + stamp + ".png";
            var content = new VerticalStackLayout { Spacing = 10 };
            content.Children.Add(image);
            if (!string.IsNullOrEmpty(description))
            {
                // What it was drawn from, so it can be made again; the seed is what makes this exact picture.
                var caption = new Label
                {
                    Text = description + (string.IsNullOrEmpty(seed) ? "" : "\nSeed " + seed + " · 8 steps"),
                    FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.PrimaryText
                };
                SemanticProperties.SetDescription(caption, "Description and seed of this picture");
                content.Children.Add(caption);
            }

            Button Action(string text, string hint)
            {
                var b = new Button { Text = text, FontFamily = Theme.TitleFont, HorizontalOptions = LayoutOptions.Fill };
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
                    await _host.AskAsync("Couldn't " + button.Text.TrimEnd('…').ToLowerInvariant(), ex.Message, "OK", null);
                }
                finally { button.IsEnabled = true; }
            }

            var again = Action("Draw it again", "Draw the same description again with a new seed");
            again.IsVisible = !string.IsNullOrEmpty(description);
            again.Clicked += (_, _) => _host.DismissSheet("again");
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
            var save = Action("Save image…", "Save the original generated image as a PNG file");
            save.Clicked += async (_, _) => await Run(save, async () =>
            {
                using var stream = new System.IO.MemoryStream(png, writable: false);
                var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
                if (result.IsSuccessful)
                    return "Saved to " + result.FilePath;
                if (!SaveWasCancelled(result.Exception))
                    throw result.Exception ?? new System.IO.IOException("The image could not be saved.");
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
            buttons.Add(save, 1, 1);
            content.Children.Add(buttons);
            content.Children.Add(status);
            string choice = await _host.ShowSheetAsync("Picture", content, "Close");
            if (choice == "again" && !string.IsNullOrEmpty(description) && !_host.IsBusy)
                await _host.SendMessageAsync("Draw this again, using exactly this description: " + description);
        }

        // Whether the model has been loaded (or is loading).
        public bool ModelLoaded => _zimageInit != null;

        // The pictures drawn for the reply being written, to save with it; and forgetting them for the next reply.
        public List<(string Kind, string MediaType, byte[] Data)> Drawn()
        {
            lock (_sendImages)
                return _sendImages.Select(png => (GeneratedKind, "image/png", png)).ToList();
        }

        public void ClearDrawn()
        {
            lock (_sendImages)
                _sendImages.Clear();
        }

        public void ClearPromptCache() => _zimage?.ClearPromptCache();

        // Releases the model and starts again with a new instance, loaded on the next picture (the CPU thread count changed, a private
        // chat began or ended).
        public async Task ResetModelAsync()
        {
            await ReleaseModelAsync();
            _zimageInit = null;
            if (_zimage != null)
                _zimage = new ZImageTurbo();
        }

        // Storage deleted the model: image generation is turned off.
        public async Task TurnOffAndResetAsync()
        {
            if (_imageSwitch != null)
            {
                _imageSwitch.Toggled -= OnImageGenerationToggled;
                _imageSwitch.IsToggled = false;
                _imageSwitch.Toggled += OnImageGenerationToggled;
            }
            Preferences.Default.Set(ImageGenerationPreference, false);
            await ResetModelAsync();
        }

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
}

#endif
