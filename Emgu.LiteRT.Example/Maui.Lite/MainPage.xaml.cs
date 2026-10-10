//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------


namespace Maui.Demo.Lite;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Theme.PageBackground;

        var rows = new List<(string Glyph, string Title, string Subtitle, Func<Page> Create)>
        {
            (Theme.GlyphDetect, "Coco SSD Mobilenet", "Detect and label objects in a photo", () => new CocoSsdMobilenetPage()),
            (Theme.GlyphImage, "Mobilenet", "Recognize what is in an image", () => new MobilenetPage()),
            (Theme.GlyphSparkle, "Inception", "Recognize flower species", () => new InceptionPage()),
            (Theme.GlyphSettings, "Model Checker", "Inspect any .tflite model file", () => new ModelCheckerPage()),
        };
#if WINDOWS || IOS || ANDROID
        rows.Add((Theme.GlyphText, "LiteRT-LM Chat", "Chat with an on-device language model", () => new LiteRtLmChatPage()));
#endif

        var list = new VerticalStackLayout { Spacing = 0 };
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                list.Children.Add(Theme.Divider(58));
            var (glyph, title, subtitle, create) = rows[i];
            var row = new Grid
            {
                Padding = new Thickness(0, 10),
                ColumnSpacing = 14,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            row.Add(Theme.IconTile(glyph, 44, 24, 12), 0, 0);
            row.Add(new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label { Text = title, FontFamily = Theme.TitleFont, FontSize = 17, TextColor = Theme.PrimaryText },
                    new Label { Text = subtitle, FontFamily = Theme.BodyFont, FontSize = 14, TextColor = Theme.SecondaryText }
                }
            }, 1, 0);
            row.Add(Theme.MakeIcon(Theme.GlyphChevronRight, Theme.Chevron, 24), 2, 0);
            row.OnTap(async () => await Navigation.PushAsync(create()));
            list.Children.Add(row);
        }

        var header = new Grid
        {
            ColumnSpacing = 16,
            Margin = new Thickness(0, 12, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(new Border
        {
            WidthRequest = 64,
            HeightRequest = 64,
            BackgroundColor = Theme.CardBackground,
            Stroke = Theme.RowBorder,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) },
            Content = Theme.MakeIcon(Theme.GlyphWidgets, Theme.Accent, 32)
        }, 0, 0);
        header.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Emgu LiteRT", FontFamily = Theme.TitleFont, FontSize = 28, TextColor = Theme.PrimaryText },
                new Label { Text = "On-device machine learning demos", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.SecondaryText }
            }
        }, 1, 0);
        header.Add(Theme.CircleButton(Theme.GlyphInfo, async () => await Navigation.PushAsync(new AboutPage())), 2, 0);

        Content = Theme.CenteredScroll(new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(16, 12, 16, 24),
            Children =
            {
                header,
                Theme.Card(new VerticalStackLayout { Spacing = 6, Children = { Theme.SectionTitle("Demos"), list } }, 18)
            }
        });
    }
}
