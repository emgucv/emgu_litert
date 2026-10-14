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
    // pictures are saved with the reply (attachments of kind "generated", never given back to the model). Not on iOS: the
    // iOS app has no libLiteRt (LiteRT-LM comes as its own framework there). With the setting off the tool is offered
    // anyway and tells the model where to turn it on.
    public partial class LiteRtLmChatPage
    {
        private const string ImageGenerationPreference = "litert_lm_chat_image_generation";
        private const string GeneratedKind = "generated";
        private const string ImageInstructions =
            "When the user asks you to draw, paint or create a picture, use the generate_image tool with a short English " +
            "description; the picture is shown to the user, not to you.";

        private Switch _imageSwitch;
        private Label _imageStatus;
        private Button _imageDeleteButton;
        private ZImageTurbo _zimage;
        private Task _zimageInit;
        private CancellationTokenSource _zimageDownload;
        private double _zimageDownloadFraction = -1;
        // The pictures drawn for the reply being written, saved with it.
        private readonly List<byte[]> _sendImages = new List<byte[]>();

        private static bool ImageGenerationSupported => !(OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst());

        private bool ImageGenerationOn => _imageSwitch != null && _imageSwitch.IsToggled;

        private View BuildImageSection()
        {
            if (!ImageGenerationSupported)
                return new ContentView { IsVisible = false };
            _zimage = new ZImageTurbo();
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
                Text = "Delete the image model",
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
                bool download = await DisplayAlertAsync("Image generation",
                    string.Format(CultureInfo.InvariantCulture,
                        "Download the image model ({0:F1} GB)? It needs about {1:F0} GB of free space in all.",
                        (ZImageTurbo.DownloadSize - _zimage.DownloadedBytes) / 1e9, (ZImageTurbo.DownloadSize + 9e9) / 1e9),
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
            _ = Task.Run(async () =>
            {
                string error = null;
                try
                {
                    await _zimage.Download(new InlineProgress<ZImageDownloadProgress>(p =>
                    {
                        _zimageDownloadFraction = (double)p.BytesDone / p.TotalBytes;
                        UpdateImageStatus();
                    }), cancel.Token);
                }
                catch (OperationCanceledException)
                {
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
            _imageDeleteButton.IsVisible = present > 0 && _zimageDownload == null && _zimageInit == null;
            if (_zimageDownload != null)
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
            if (!await DisplayAlertAsync("Image generation",
                    string.Format(CultureInfo.InvariantCulture, "Delete the image model ({0:F1} GB) and its caches?", _zimage.DownloadedBytes / 1e9),
                    "Delete", "Cancel"))
                return;
            _imageSwitch.IsToggled = false;
            ZImageTurbo old = _zimage;
            _zimage = new ZImageTurbo();
            _zimageInit = null;
            await Task.Run(() =>
            {
                old.Dispose();
                try { System.IO.Directory.Delete(old.Folder, true); } catch (Exception) { }
            });
            UpdateImageStatus();
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
                    string text = p.Stage + string.Format(CultureInfo.InvariantCulture, " ({0:F0} s)", watch.Elapsed.TotalSeconds);
                    MainThread.BeginInvokeOnMainThread(() => tile.Stage.Text = text);
                }), cancel);
                byte[] png = await Task.Run(() => EncodePng(result));
                lock (_sendImages)
                    _sendImages.Add(png);
                await MainThread.InvokeOnMainThreadAsync(() => ShowGeneratedImage(tile, png,
                    string.Format(CultureInfo.InvariantCulture, "{0:F0} s", result.Elapsed.TotalSeconds)));
                UiStallWatchdog.Mark(string.Format(CultureInfo.InvariantCulture, "generate_image done in {0:F1} s", result.Elapsed.TotalSeconds));
                return string.Format(CultureInfo.InvariantCulture,
                    "Done: the picture of \"{0}\" is now shown to the user. Now answer the user in one short sentence that says what you drew, e.g. \"Here is {0}.\"",
                    prompt);
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
                HeightRequest = Math.Max(240, Math.Min(Width, Height) * 0.8)
            };
            await _sheet.ShowViewAsync("Picture", image, "Close");
        }

        private async Task ReleaseImageModelAsync()
        {
            _zimageDownload?.Cancel();
            ZImageTurbo model = _zimage;
            if (model == null)
                return;
            try { await (_zimageInit ?? Task.CompletedTask); } catch (Exception) { }
            await Task.Run(() => model.Dispose());
        }
    }
}

#endif
