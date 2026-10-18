//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
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
    // pictures are saved with the reply (attachments of kind "generated", never given back to the model). On iOS, LiteRT
    // comes from LiteRT-LM's CLiteRTLM framework (Emgu.LiteRT.LM.runtime.maui.ios), and the model folder is kept out of
    // iCloud backups. With the setting off the tool is offered anyway and tells the model where to turn it on.
    public partial class LiteRtLmChatPage
    {
        private const string ImageGenerationPreference = "litert_lm_chat_image_generation";
        private const string GeneratedKind = "generated";
        private const string ImageInstructions = ChatPlannerInstructions.Images;

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

        // Everywhere the chat runs: on iOS Emgu.LiteRT uses the LiteRT inside CLiteRTLM, which the chat needs anyway.
        private static bool ImageGenerationSupported => true;

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

        private View BuildImageSection()
        {
            if (!ImageGenerationSupported)
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
            UpdateImageStatus();
            // Turned on before and the model isn't complete: go on downloading.
            if (on && !_zimage.IsDownloaded)
                StartImageModelDownload();
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    BuildImageModelSelector(),
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
                bool download = await DisplayAlertAsync("Image generation",
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
            UpdateImageStatus();
        }

        private void StartImageModelDownload()
        {
            if (_zimageDownload != null)
                return;
            CancellationTokenSource cancel = new CancellationTokenSource();
            _zimageDownload = cancel;
            _zimageDownloadFraction = (double)_zimage.DownloadedBytes / ZImageTurbo.DownloadSize;
            UpdateImageStatus();
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
                        UpdateImageStatus();
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
                    UpdateImageStatus();
                    if (error != null)
                        _imageStatus.Text = "Download stopped: " + error + " (turn the switch off and on to retry)";
                    else if (paused && _imageSwitch.IsToggled)
                        _imageStatus.Text = "Download paused. Turn Image generation off and on to continue from the saved bytes.";
                });
            });
        }

        private void UpdateImageStatus()
        {
            if (_imageStatus == null)
                return;
            if (!MainThread.IsMainThread)
            {
                MainThread.BeginInvokeOnMainThread(UpdateImageStatus);
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
            await OpenStorageAsync();
        }

        private Task EnsureImageModelAsync()
        {
            if (_zimageInit == null || _zimageInit.IsFaulted)
            {
                _zimage.NumThreads = CpuThreadSettings.Count;
                _zimageInit = _zimage.Init();
            }
            return _zimageInit;
        }

        // The generate_image tool: draw the picture, show it above the reply, and tell the model what happened.
        private async Task<string> GenerateImageAsync(string prompt, CancellationToken cancel)
        {
            if (!ImageGenerationSupported)
                return "Image generation isn't available on this device.";
            if (!ImageGenerationOn)
                return string.Format(CultureInfo.InvariantCulture,
                    "Image generation is turned off, so no picture can be drawn. Tell the user they can turn on \"Image generation\" in {0}; the image model is a {1:F1} GB download and runs on this device.",
                    _settingsButton.IsVisible ? "this chat's settings (the gear button at the top)" : "the settings panel beside the chat",
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
                UpdateImageStatus();
            });
            await WaitForWorkAsync(false, modelPaused, cancel);
            await EnsureImageModelAsync();
            int tokens = _zimage.CountPromptTokens(prompt);
            if (tokens > ZImageTurbo.MaxPromptTokens)
                return string.Format(CultureInfo.InvariantCulture,
                    "The description is too long for the image model ({0} tokens, at most {1} fit). Call generate_image again with a shorter English description of about 15 words, keeping the subject and the style.",
                    tokens, ZImageTurbo.MaxPromptTokens);

            long seed = Random.Shared.NextInt64(1, 1L << 40);
            StreamingBubble bubble = _agentBubble;
            ImageTile tile = await MainThread.InvokeOnMainThreadAsync(() => AddImageTile(bubble, prompt));
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
                        WaitForWorkAsync(false, reason => MainThread.BeginInvokeOnMainThread(() =>
                        {
                            _imagePauseReason = reason;
                            UpdateImageStatus();
                            tile.Stage.Text = reason == null ? p.Stage : "Image generation paused: " + reason;
                        }), cancel).GetAwaiter().GetResult();
                    string text = p.Stage + string.Format(CultureInfo.InvariantCulture, " ({0:F0} s)", watch.Elapsed.TotalSeconds);
                    MainThread.BeginInvokeOnMainThread(() => tile.Stage.Text = text);
                }), cancel);
                byte[] png = await Task.Run(() => EncodePng(result));
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

        private sealed class ImageTile
        {
            public VerticalStackLayout View;
            public Border Frame;
            public Label Stage;
        }

        private const double GeneratedImageSize = 220;

        // A picture being drawn, above the reply: a placeholder with a spinner and the stage, then the picture.
        private ImageTile AddImageTile(StreamingBubble bubble, string prompt)
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
            int index = bubble != null ? _transcript.Children.IndexOf(bubble.Row) : -1;
            if (_transcript.Children.Contains(_emptyLabel))
                _transcript.Children.Remove(_emptyLabel);
            if (index < 0)
                _transcript.Children.Add(tile.View);
            else
                _transcript.Children.Insert(index, tile.View);
            ScrollToEnd();
            return tile;
        }

        // The Windows file picker reports cancelling as a plain exception, "Operation cancelled or Path doesn't exist.",
        // not an OperationCanceledException (Apple and Android do use that).
        private static bool SaveWasCancelled(Exception exception)
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

        // A saved chat's picture, above the saved reply.
        private void AddSavedGeneratedImage(byte[] png)
        {
            ImageTile tile = AddImageTile(null, string.Empty);
            ShowGeneratedImage(tile, png, string.Empty);
            tile.Stage.IsVisible = false;
        }

        private async void ShowGeneratedImageLarge(byte[] png)
        {
            Image image = new Image
            {
                Source = ImageUtil.ToImageSource(png),
                Aspect = Aspect.AspectFit,
                HeightRequest = Math.Max(240, Math.Min(Width, Height) * 0.65)
            };
            var save = new Button { Text = "Save image…", FontFamily = Theme.TitleFont, HorizontalOptions = LayoutOptions.Fill };
            var status = new Label { FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false };
            SemanticProperties.SetDescription(save, "Save the original generated image as a PNG file");
            string fileName = "generated-image-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".png";
            save.Clicked += async (_, _) =>
            {
                if (!save.IsEnabled) return;
                save.IsEnabled = false;
                status.IsVisible = false;
                try
                {
                    using var stream = new System.IO.MemoryStream(png, writable: false);
                    var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream);
                    if (result.IsSuccessful)
                    {
                        status.Text = "Saved to " + result.FilePath;
                        status.IsVisible = true;
                    }
                    else if (!SaveWasCancelled(result.Exception))
                        throw result.Exception ?? new System.IO.IOException("The image could not be saved.");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    await DisplayAlertAsync("Couldn't save image", ex.Message, "OK");
                }
                finally { save.IsEnabled = true; }
            };
            await _sheet.ShowViewAsync("Picture", new VerticalStackLayout { Spacing = 10, Children = { image, save, status } }, "Close");
        }

        private async Task ReleaseImageModelAsync()
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
