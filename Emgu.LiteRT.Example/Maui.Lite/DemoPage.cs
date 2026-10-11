//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite
{
    /// <summary>One line of a results list. A negative <see cref="Fraction"/> hides the bar.</summary>
    public sealed class ResultRow
    {
        public string Label;
        public string Value;
        public double Fraction = -1;

        public ResultRow(string label, string value, double fraction = -1)
        {
            Label = label;
            Value = value;
            Fraction = fraction;
        }
    }

    public sealed class DemoResult
    {
        /// <summary>Image to show instead of the input (disposed by the page), or null to keep the input.</summary>
        public Mat Annotated;
        public List<ResultRow> Rows = new List<ResultRow>();
        public string Summary;
    }

    internal static class ImageUtil
    {
        public static async Task<byte[]> ReadAppFileAsync(string fileName)
        {
            using Stream stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }

        public static Mat Decode(byte[] bytes)
        {
            Mat m = new Mat();
            CvInvoke.Imdecode(bytes, ImreadModes.ColorBgr, m);
            if (m.IsEmpty)
            {
                m.Dispose();
                return null;
            }
            return m;
        }

        public static Mat Downscale(Mat src, int maxDim)
        {
            int longest = Math.Max(src.Width, src.Height);
            if (longest <= maxDim)
                return src;
            double scale = (double)maxDim / longest;
            Mat dst = new Mat();
            CvInvoke.Resize(src, dst, new System.Drawing.Size((int)(src.Width * scale), (int)(src.Height * scale)));
            src.Dispose();
            return dst;
        }

        public static byte[] Encode(Mat m)
        {
            using VectorOfByte buf = new VectorOfByte();
            CvInvoke.Imencode(".png", m, buf);
            return buf.ToArray();
        }

        public static ImageSource ToImageSource(byte[] png) => ImageSource.FromStream(() => new MemoryStream(png));
    }

    /// <summary>
    /// Card-style page shared by the image demos: header, input image with a "Change Photo" sheet,
    /// a run button, a results list and a collapsible about card.
    /// </summary>
    public abstract class DemoPage : ContentPage
    {
        public sealed class Sample
        {
            public string File;
            public string Name;
            public string Glyph;
            public Sample(string file, string name, string glyph = null)
            {
                File = file;
                Name = name;
                Glyph = glyph ?? Theme.GlyphImage;
            }
        }

        private readonly Sample[] _samples;
        private readonly int _defaultSample;
        private readonly string _resultsTitle;
        private readonly string _idleText;

        private readonly Image _previewImage;
        private readonly VerticalStackLayout _resultsRows;
        private readonly VerticalStackLayout _emptyState;
        private readonly Label _emptyTitle;
        private readonly Label _emptySubtitle;
        private readonly Label _resultsCountLabel;
        private readonly Image _resultsCheck;
        private readonly Label _resultsTimeLabel;
        private readonly Button _runButton;
        private readonly LoadingOverlay _loading;
        private readonly BottomSheet _sheet;

        private readonly VerticalStackLayout _content;
        private Mat _currentImage;
        private byte[] _lastResultPng;
        private bool _running;
        private bool _defaultLoaded;

        protected DemoPage(
            string title,
            string subtitle,
            string glyph,
            string about,
            Sample[] samples,
            int defaultSample,
            string runText,
            string resultsTitle = "Results",
            string idleText = "Tap the button above to analyze the image.")
        {
            Title = title;
            BackgroundColor = Theme.PageBackground;
            Shell.SetNavBarIsVisible(this, false);

            _samples = samples;
            _defaultSample = defaultSample;
            _resultsTitle = resultsTitle;
            _idleText = idleText;

            // ---------- Input image card ----------
            var inputHeader = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            inputHeader.Add(Theme.SectionTitle("Input Image"), 0, 0);
            inputHeader.Add(Theme.PillButton(Theme.GlyphImage, "Change Photo", OnChangePhoto), 1, 0);

            _previewImage = new Image { Aspect = Aspect.AspectFit, HeightRequest = 340 };
            var previewFrame = new Border
            {
                BackgroundColor = Theme.ImageBackground,
                Stroke = Theme.RowBorder,
                StrokeThickness = 1,
                Padding = new Thickness(10),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Content = _previewImage
            };
            var inputCard = Theme.Card(new VerticalStackLayout { Spacing = 14, Children = { inputHeader, previewFrame } });

            // ---------- Buttons ----------
            _runButton = Theme.PrimaryButton(runText, Theme.GlyphPlay);
            _runButton.Clicked += OnRunClicked;

            // ---------- Results card ----------
            _resultsCountLabel = new Label { Text = "", FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText, VerticalOptions = LayoutOptions.Center };
            _resultsCheck = Theme.MakeIcon(Theme.GlyphCheck, Theme.Success, 18);
            _resultsCheck.IsVisible = false;
            _resultsTimeLabel = new Label { Text = "", FontFamily = Theme.BodyFont, FontSize = 12, TextColor = Theme.SecondaryText, IsVisible = false, HorizontalTextAlignment = TextAlignment.End };
            var countRow = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.End, Children = { _resultsCountLabel, _resultsCheck } };
            var statusStack = new VerticalStackLayout { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, Children = { countRow, _resultsTimeLabel } };

            var resultsHeader = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            resultsHeader.Add(Theme.SectionTitle(_resultsTitle), 0, 0);
            resultsHeader.Add(statusStack, 1, 0);

            _emptyTitle = new Label { Text = "Nothing yet", FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText, HorizontalTextAlignment = TextAlignment.Center };
            _emptySubtitle = new Label { Text = _idleText, FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText, HorizontalTextAlignment = TextAlignment.Center };
            _emptyState = new VerticalStackLayout
            {
                Spacing = 6,
                Padding = new Thickness(0, 18, 0, 8),
                HorizontalOptions = LayoutOptions.Center,
                Children = { Theme.MakeIcon(Theme.GlyphImage, Theme.Chevron, 40), _emptyTitle, _emptySubtitle }
            };
            _resultsRows = new VerticalStackLayout { Spacing = 0 };
            var resultsCard = Theme.Card(new VerticalStackLayout { Spacing = 12, Children = { resultsHeader, _emptyState, _resultsRows } });

            // ---------- Page content ----------
            var content = new VerticalStackLayout
            {
                Spacing = 18,
                Padding = new Thickness(20, 16, 20, 28)
            };
            content.Children.Add(Theme.PageHeader(this, glyph, title, subtitle));
            content.Children.Add(inputCard);
            _content = content;
            content.Children.Add(_runButton);
            content.Children.Add(resultsCard);
            content.Children.Add(Theme.AboutCard(about));

            _loading = new LoadingOverlay();
            _sheet = new BottomSheet();

            // Cap the column width and centre it on wide windows.
            content.HorizontalOptions = LayoutOptions.Center;
            var scroll = new Microsoft.Maui.Controls.ScrollView { Content = content };
            scroll.SizeChanged += (s, e) => content.WidthRequest = Math.Min(760, scroll.Width);
            Content = new Grid { Children = { scroll, _loading, _sheet } };
        }

        /// <summary>Adds an extra card between the input image and the run button (e.g. a backend picker).</summary>
        protected void AddOptionsCard(View card)
        {
            _content.Children.Insert(_content.Children.IndexOf(_runButton), card);
        }

        /// <summary>Run the model on the image. Called on the UI thread; push heavy work to a background thread.</summary>
        protected abstract Task<DemoResult> RunAsync(Mat input);

        /// <summary>Free native model objects when the page is popped.</summary>
        protected virtual void ReleaseModel()
        {
        }

        /// <summary>Allow a subclass to clear state when the picked image changes.</summary>
        protected virtual void OnImageChanged()
        {
        }

        protected void ShowProgress(string message)
        {
            MainThread.BeginInvokeOnMainThread(() => _loading.SetMessage(message));
        }

        protected void OnDownloadProgress(long? totalBytesToReceive, long bytesReceived, double? progressPercentage)
        {
            string msg = totalBytesToReceive.HasValue && totalBytesToReceive.Value > 0
                ? $"Downloading AI model...\n{bytesReceived / (1024 * 1024)} of {totalBytesToReceive.Value / (1024 * 1024)} MB ({(int)(progressPercentage ?? 0)}%)"
                : $"Downloading AI model...\n{bytesReceived / (1024 * 1024)} MB";
            ShowProgress(msg);
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (!_defaultLoaded)
            {
                _defaultLoaded = true;
                Mat m = await LoadSampleAsync(_defaultSample);
                if (m != null)
                    SetCurrentImage(m);
            }
        }

        protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
        {
            base.OnNavigatedFrom(args);
            if (!Navigation.NavigationStack.Contains(this) && !Navigation.ModalStack.Contains(this))
            {
                _currentImage?.Dispose();
                _currentImage = null;
                ReleaseModel();
            }
        }

        // ---------- Image sources ----------

        private async void OnChangePhoto()
        {
            UiStallWatchdog.Mark("Change photo: " + Title);
            var rows = new List<(string Section, string Glyph, string Text, string Value)>();
            for (int i = 0; i < _samples.Length; i++)
                rows.Add((i == 0 ? "SAMPLE IMAGES" : null, _samples[i].Glyph, _samples[i].Name, "sample:" + i));
            rows.Add(("YOUR PHOTOS", Theme.GlyphImage, "Photo Library", "library"));
            if (MediaPicker.Default.IsCaptureSupported)
                rows.Add((null, Theme.GlyphCamera, "Take Photo", "camera"));

            string action = await _sheet.ShowAsync("Change Photo", rows);
            if (string.IsNullOrEmpty(action))
                return;

            if (action.StartsWith("sample:"))
            {
                Mat sm = await LoadSampleAsync(int.Parse(action.Substring("sample:".Length)));
                if (sm != null)
                    SetCurrentImage(sm);
                return;
            }

            try
            {
                FileResult file = action == "camera"
                    ? await MediaPicker.Default.CapturePhotoAsync()
                    : (await MediaPicker.Default.PickPhotosAsync())?.FirstOrDefault();
                if (file == null)
                    return;

                _loading.Show("Loading photo...");
                using Stream stream = await file.OpenReadAsync();
                using MemoryStream ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                Mat m = ImageUtil.Decode(ms.ToArray());
                _loading.Hide();
                if (m == null)
                {
                    await DisplayAlertAsync("Photo", "That file could not be read as an image.", "OK");
                    return;
                }
                SetCurrentImage(ImageUtil.Downscale(m, 1024));
            }
            catch (Exception ex)
            {
                _loading.Hide();
                await DisplayAlertAsync("Could not load photo", ex.Message, "OK");
            }
        }

        private async Task<Mat> LoadSampleAsync(int idx)
        {
            try
            {
                return ImageUtil.Decode(await ImageUtil.ReadAppFileAsync(_samples[idx].File));
            }
            catch
            {
                return null;
            }
        }

        private void SetCurrentImage(Mat image)
        {
            _currentImage?.Dispose();
            _currentImage = image;
            _previewImage.Source = ImageUtil.ToImageSource(ImageUtil.Encode(image));
            ResetResults();
            OnImageChanged();
        }

        private void ResetResults()
        {
            _resultsRows.Children.Clear();
            _emptyState.IsVisible = true;
            _emptyTitle.Text = "Nothing yet";
            _emptySubtitle.Text = _idleText;
            _resultsCountLabel.Text = "";
            _resultsCheck.IsVisible = false;
            _resultsTimeLabel.IsVisible = false;
            _lastResultPng = null;
        }

        // ---------- Running ----------

        private async void OnRunClicked(object sender, EventArgs e)
        {
            UiStallWatchdog.Mark("Run: " + Title);
            if (_running || _currentImage == null)
                return;

            _running = true;
            _runButton.IsEnabled = false;
            _loading.Show("Preparing...");
            try
            {
                DemoResult result = await RunAsync(_currentImage);

                _resultsRows.Children.Clear();
                _emptyState.IsVisible = false;
                for (int i = 0; i < result.Rows.Count; i++)
                    _resultsRows.Children.Add(BuildRow(result.Rows[i], i == result.Rows.Count - 1));

                if (result.Annotated != null)
                {
                    using (result.Annotated)
                    {
                        _lastResultPng = ImageUtil.Encode(result.Annotated);
                    }
                    _previewImage.Source = ImageUtil.ToImageSource(_lastResultPng);
                }

                _resultsCountLabel.Text = result.Rows.Count == 1 ? "1 result" : $"{result.Rows.Count} results";
                _resultsCountLabel.TextColor = Theme.PrimaryText;
                _resultsCheck.IsVisible = result.Rows.Count > 0;
                _resultsTimeLabel.Text = result.Summary ?? "";
                _resultsTimeLabel.IsVisible = !string.IsNullOrEmpty(result.Summary);

                if (result.Rows.Count == 0)
                {
                    _emptyState.IsVisible = true;
                    _emptyTitle.Text = "Nothing found";
                    _emptySubtitle.Text = "Try a different photo.";
                }
            }
            catch (Exception ex)
            {
                _resultsRows.Children.Clear();
                _emptyState.IsVisible = true;
                _emptyTitle.Text = "Something went wrong";
                _emptySubtitle.Text = ex.Message;
            }
            finally
            {
                _running = false;
                _runButton.IsEnabled = true;
                _loading.Hide();
            }
        }

        private static View BuildRow(ResultRow row, bool isLast)
        {
            var grid = new Grid
            {
                Padding = new Thickness(2, 12),
                ColumnSpacing = 12,
                RowSpacing = 8,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }
            };
            grid.Add(new Label { Text = row.Label, FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 0, 0);
            grid.Add(new Label { Text = row.Value, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center }, 1, 0);

            if (row.Fraction >= 0)
            {
                var track = new Grid { HeightRequest = 6, BackgroundColor = Theme.TileBackground };
                var fill = new BoxView { Color = Theme.Accent, HorizontalOptions = LayoutOptions.Start, CornerRadius = 3 };
                track.Children.Add(fill);
                track.SizeChanged += (s, e) => fill.WidthRequest = Math.Max(4, track.Width * Math.Clamp(row.Fraction, 0, 1));
                var trackBorder = new Border { Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) }, Content = track };
                grid.Add(trackBorder, 0, 1);
                Grid.SetColumnSpan(trackBorder, 2);
            }

            var stack = new VerticalStackLayout();
            stack.Children.Add(grid);
            if (!isLast)
                stack.Children.Add(Theme.Divider());
            return stack;
        }
    }
}
