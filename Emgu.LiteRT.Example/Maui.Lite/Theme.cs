//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Shared look and feel of the demo: palette, fonts, Material Symbols glyphs and small
    /// view builders, matching the Emgu CV MAUI demo.
    /// </summary>
    internal static class Theme
    {
        // ---------- Palette ----------
        public static readonly Color PageBackground = Color.FromArgb("#EEF1F8");
        public static readonly Color CardBackground = Colors.White;
        public static readonly Color PrimaryText = Color.FromArgb("#1A1C2E");
        public static readonly Color SecondaryText = Color.FromArgb("#8A8FA3");
        public static readonly Color Accent = Color.FromArgb("#3D7BF7");
        public static readonly Color RowBorder = Color.FromArgb("#ECEEF5");
        public static readonly Color TileBackground = Color.FromArgb("#E8EFFE");
        public static readonly Color ImageBackground = Color.FromArgb("#F2F2F7");
        public static readonly Color Chevron = Color.FromArgb("#C2C7D6");
        public static readonly Color Success = Color.FromArgb("#2BA84A");
        public static readonly Color Danger = Color.FromArgb("#D93B3B");

        public const string BodyFont = "InterRegular";
        public const string TitleFont = "InterSemiBold";
        public const string IconFont = "MaterialSymbols";

        // ---------- Material Symbols glyphs (the bundled font is a subset) ----------
        public const string GlyphInfo = "";         // info
        public const string GlyphChevronLeft = "";  // chevron_left
        public const string GlyphChevronRight = ""; // chevron_right
        public const string GlyphExpandMore = "";   // expand_more
        public const string GlyphCheck = "";        // check_circle
        public const string GlyphImage = "";        // image
        public const string GlyphPlay = "";         // play_arrow
        public const string GlyphClose = "";        // close
        public const string GlyphCamera = "";       // photo_camera
        public const string GlyphPets = "";         // pets
        public const string GlyphDetect = "";       // crop_free
        public const string GlyphSparkle = "";      // auto_awesome
        public const string GlyphWidgets = "";      // widgets
        public const string GlyphText = "";         // text_fields
        public const string GlyphSettings = "";     // settings
        public const string GlyphWand = "";         // auto_fix_high
        public const string GlyphGrid = "";         // grid_on

        public static Image MakeIcon(string glyph, Color color, double size) => new Image
        {
            Source = new FontImageSource { FontFamily = IconFont, Glyph = glyph, Color = color, Size = size },
            WidthRequest = size,
            HeightRequest = size,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        public static ImageSource MakeGlyphSource(string glyph, Color color, double size) =>
            new FontImageSource { FontFamily = IconFont, Glyph = glyph, Color = color, Size = size };

        public static void OnTap(this View view, Action action)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => action();
            view.GestureRecognizers.Add(tap);
        }

        public static Border Card(View content, double padding = 16) => new Border
        {
            BackgroundColor = CardBackground,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            Padding = new Thickness(padding),
            Content = content
        };

        public static Label SectionTitle(string text) => new Label
        {
            Text = text,
            FontFamily = TitleFont,
            FontSize = 19,
            TextColor = PrimaryText,
            VerticalOptions = LayoutOptions.Center
        };

        public static Border IconTile(string glyph, double tileSize, double iconSize, double radius) => new Border
        {
            WidthRequest = tileSize,
            HeightRequest = tileSize,
            BackgroundColor = TileBackground,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(radius) },
            VerticalOptions = LayoutOptions.Center,
            Content = MakeIcon(glyph, Accent, iconSize)
        };

        public static Border CircleButton(string glyph, Action onTap)
        {
            var cb = new Border
            {
                WidthRequest = 44,
                HeightRequest = 44,
                BackgroundColor = CardBackground,
                Stroke = RowBorder,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
                VerticalOptions = LayoutOptions.Center,
                Content = MakeIcon(glyph, PrimaryText, 22)
            };
            cb.OnTap(onTap);
            return cb;
        }

        public static Border PillButton(string glyph, string text, Action onTap)
        {
            var pill = new Border
            {
                BackgroundColor = TileBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
                Padding = new Thickness(14, 9),
                VerticalOptions = LayoutOptions.Center,
                Content = new HorizontalStackLayout
                {
                    Spacing = 7,
                    Children =
                    {
                        MakeIcon(glyph, Accent, 18),
                        new Label { Text = text, FontFamily = TitleFont, FontSize = 14, TextColor = Accent, VerticalOptions = LayoutOptions.Center }
                    }
                }
            };
            pill.OnTap(onTap);
            return pill;
        }

        public static Button PrimaryButton(string text, string glyph)
        {
            return new Button
            {
                Text = text,
                FontFamily = TitleFont,
                FontSize = 17,
                BackgroundColor = Accent,
                TextColor = Colors.White,
                CornerRadius = 16,
                HeightRequest = 56,
                ImageSource = MakeGlyphSource(glyph, Colors.White, 20),
                ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8)
            };
        }

        public static Button SecondaryButton(string text, string glyph)
        {
            return new Button
            {
                Text = text,
                FontFamily = TitleFont,
                FontSize = 16,
                BackgroundColor = CardBackground,
                TextColor = Accent,
                BorderColor = Accent,
                BorderWidth = 1,
                CornerRadius = 16,
                HeightRequest = 52,
                ImageSource = MakeGlyphSource(glyph, Accent, 20),
                ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8)
            };
        }

        /// <summary>Back button, optional info button and the big title block shared by every page.</summary>
        public static View PageHeader(ContentPage page, string glyph, string title, string subtitle, bool showBack = true, bool showInfo = true)
        {
            var headerTile = new Border
            {
                WidthRequest = 64,
                HeightRequest = 64,
                BackgroundColor = CardBackground,
                Stroke = RowBorder,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
                VerticalOptions = LayoutOptions.Start,
                Content = MakeIcon(glyph, Accent, 32)
            };
            var titleStack = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = title, FontFamily = TitleFont, FontSize = 28, TextColor = PrimaryText },
                    new Label { Text = subtitle, FontFamily = BodyFont, FontSize = 15, TextColor = SecondaryText, Margin = new Thickness(0, 2, 0, 0) }
                }
            };

            var topRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            if (showBack)
                topRow.Add(CircleButton(GlyphChevronLeft, async () => await page.Navigation.PopAsync()), 0, 0);
            if (showInfo)
                topRow.Add(CircleButton(GlyphInfo, async () => await page.Navigation.PushAsync(new AboutPage())), 2, 0);

            var headerBody = new Grid
            {
                ColumnSpacing = 16,
                Margin = new Thickness(0, 12, 0, 0),
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };
            headerBody.Add(headerTile, 0, 0);
            headerBody.Add(titleStack, 1, 0);

            return new VerticalStackLayout { Children = { topRow, headerBody } };
        }

        /// <summary>Collapsible "About this module" card.</summary>
        public static View AboutCard(string about)
        {
            var chevron = MakeIcon(GlyphChevronRight, SecondaryText, 22);
            var headerRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            headerRow.Add(new Label { Text = "About this demo", FontFamily = TitleFont, FontSize = 17, TextColor = PrimaryText, VerticalOptions = LayoutOptions.Center }, 0, 0);
            headerRow.Add(chevron, 1, 0);

            var details = new VerticalStackLayout { IsVisible = false, Margin = new Thickness(0, 12, 0, 2) };
            details.Children.Add(new Label { Text = about, FontFamily = BodyFont, FontSize = 15, TextColor = SecondaryText });

            bool expanded = false;
            headerRow.OnTap(async () =>
            {
                expanded = !expanded;
                details.IsVisible = expanded;
                await chevron.RotateToAsync(expanded ? 90 : 0, 180, Easing.CubicOut);
            });

            return Card(new VerticalStackLayout { Children = { headerRow, details } });
        }

        public static View Divider(double leftMargin = 0) =>
            new BoxView { HeightRequest = 1, Color = RowBorder, Margin = new Thickness(leftMargin, 0, 0, 0) };

        public static ScrollView CenteredScroll(VerticalStackLayout content)
        {
            content.HorizontalOptions = LayoutOptions.Center;
            var scroll = new ScrollView { Content = content };
            scroll.SizeChanged += (s, e) => content.WidthRequest = Math.Min(760, scroll.Width);
            return scroll;
        }
    }

    /// <summary>Full-page translucent overlay with a spinner and a message.</summary>
    internal sealed class LoadingOverlay : Grid
    {
        private readonly ActivityIndicator _indicator;
        private readonly Label _label;

        public LoadingOverlay()
        {
            IsVisible = false;
            BackgroundColor = Color.FromArgb("#B3EEF1F8");

            _indicator = new ActivityIndicator { IsRunning = false, Color = Theme.Accent, WidthRequest = 44, HeightRequest = 44, HorizontalOptions = LayoutOptions.Center };
            _label = new Label
            {
                Text = "Working...",
                FontFamily = Theme.BodyFont,
                FontSize = 15,
                TextColor = Theme.PrimaryText,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center
            };
            Children.Add(new Border
            {
                BackgroundColor = Theme.CardBackground,
                Stroke = Theme.RowBorder,
                StrokeThickness = 1,
                Padding = new Thickness(28, 22),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = new VerticalStackLayout { Spacing = 14, WidthRequest = 260, Children = { _indicator, _label } }
            });
        }

        public void Show(string message)
        {
            _label.Text = message;
            IsVisible = true;
            _indicator.IsRunning = true;
        }

        public void Hide()
        {
            IsVisible = false;
            _indicator.IsRunning = false;
        }

        public void SetMessage(string message) => _label.Text = message;
    }

    /// <summary>In-page bottom sheet with a dimmed scrim, listing tappable rows.</summary>
    internal sealed class BottomSheet : Grid
    {
        private readonly BoxView _scrim;
        private readonly Border _card;
        private readonly VerticalStackLayout _list = new VerticalStackLayout();
        private TaskCompletionSource<string> _tcs;
        private bool _animating;

        public BottomSheet()
        {
            IsVisible = false;
            _card = new Border
            {
                BackgroundColor = Theme.CardBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) },
                Padding = new Thickness(20, 8, 20, 24),
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.Center,
                MaximumWidthRequest = 560,
                Content = _list
            };
            _scrim = new BoxView { Color = Color.FromArgb("#66000000"), Opacity = 0 };
            _scrim.OnTap(() => Close(null));
            SizeChanged += (s, e) =>
            {
                if (Width > 0)
                    _card.WidthRequest = Math.Min(560, Width);
            };
            Children.Add(_scrim);
            Children.Add(_card);
        }

        /// <summary>Rows: (section heading or null, glyph, text, value). A row with a heading starts a new section.</summary>
        public Task<string> ShowAsync(string title, IReadOnlyList<(string Section, string Glyph, string Text, string Value)> rows)
        {
            _list.Children.Clear();
            _list.Children.Add(new BoxView { WidthRequest = 40, HeightRequest = 5, CornerRadius = 3, Color = Color.FromArgb("#D5D8E0"), HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 10, 0, 6) });
            _list.Children.Add(new Label { Text = title, FontFamily = Theme.TitleFont, FontSize = 20, TextColor = Theme.PrimaryText, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 0, 6) });

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r.Section != null)
                {
                    _list.Children.Add(new Label
                    {
                        Text = r.Section,
                        FontFamily = Theme.TitleFont,
                        FontSize = 12,
                        TextColor = Theme.SecondaryText,
                        CharacterSpacing = 1.2,
                        Margin = new Thickness(2, 16, 0, 2)
                    });
                }
                bool divider = i < rows.Count - 1 && rows[i + 1].Section == null;
                _list.Children.Add(Row(r.Glyph, r.Text, r.Value, divider));
            }

            var cancel = new Border
            {
                BackgroundColor = Theme.ImageBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                HeightRequest = 52,
                Margin = new Thickness(0, 14, 0, 0),
                Content = new Label { Text = "Cancel", FontFamily = Theme.TitleFont, FontSize = 17, TextColor = Theme.Accent, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
            };
            cancel.OnTap(() => Close(null));
            _list.Children.Add(cancel);

            _tcs = new TaskCompletionSource<string>();
            IsVisible = true;
            _scrim.Opacity = 0;
            _card.TranslationY = 700;
            _ = Task.WhenAll(
                _scrim.FadeToAsync(1, 220, Easing.CubicOut),
                _card.TranslateToAsync(0, 0, 260, Easing.CubicOut));
            return _tcs.Task;
        }

        private View Row(string glyph, string text, string value, bool divider)
        {
            var grid = new Grid
            {
                Padding = new Thickness(2, 14),
                ColumnSpacing = 16,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
            };
            grid.Add(Theme.MakeIcon(glyph, Theme.Accent, 26), 0, 0);
            grid.Add(new Label { Text = text, FontFamily = Theme.BodyFont, FontSize = 17, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 1, 0);
            grid.OnTap(() => Close(value));

            var stack = new VerticalStackLayout();
            stack.Children.Add(grid);
            if (divider)
                stack.Children.Add(Theme.Divider(42));
            return stack;
        }

        private async void Close(string value)
        {
            if (_animating)
                return;
            _animating = true;
            await Task.WhenAll(
                _scrim.FadeToAsync(0, 180, Easing.CubicIn),
                _card.TranslateToAsync(0, 700, 220, Easing.CubicIn));
            IsVisible = false;
            _animating = false;
            _tcs?.TrySetResult(value);
        }
    }
}
