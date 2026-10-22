//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------


namespace Maui.Demo.Lite;

public partial class MainPage : ContentPage
{
    private const string LastTabPreference = "main_page_tab";

    public MainPage()
    {
        InitializeComponent();

        Shell.SetNavBarIsVisible(this, false);
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Theme.PageBackground;

        // Two tabs, like the Emgu CV demo's category pills: the LiteRT-LM chat, and the TF Lite (Interpreter API) demos.
        var tabs = new List<(string Name, string Glyph, string Title, List<(string Glyph, string Title, string Subtitle, Func<Page> Create)> Rows)>();
#if WINDOWS || IOS || ANDROID || MACCATALYST
        tabs.Add(("LiteRT-LM", Theme.GlyphText, "LiteRT-LM Demos", new List<(string, string, string, Func<Page>)>
        {
            (Theme.GlyphText, "Chat", "Chat with Gemma 4 or Qwen on this device - images, voice and web search too", () => new LiteRtLmChatPage()),
        }));
#endif
        tabs.Add(("TF Lite", Theme.GlyphImage, "TF Lite Demos", new List<(string, string, string, Func<Page>)>
        {
            (Theme.GlyphDetect, "Coco SSD Mobilenet", "Detect and label objects in a photo", () => new CocoSsdMobilenetPage()),
            (Theme.GlyphDetect, "YOLOX Nano", "Try the YOLOX COCO detector on a photo", () => new CocoSsdMobilenetPage(useYoloX: true)),
            (Theme.GlyphDetect, "PicoDet S", "A more accurate small COCO detector, on the GPU where it works", () => new CocoSsdMobilenetPage(usePicoDet: true)),
#if MACCATALYST
            (Theme.GlyphDetect, "D-FINE Nano", "A slower, more accurate COCO detector (CPU) for single photos", () => new CocoSsdMobilenetPage(useDFine: true)),
#endif
            (Theme.GlyphDetect, "Hand gestures", "Find hands and name the gesture, with MediaPipe's models", () => new HandGesturePage()),
            (Theme.GlyphDetect, "Body pose", "Find a person and draw their skeleton, with MediaPipe's models", () => new PosePage()),
            (Theme.GlyphSparkle, "Sound events", "Name the sounds the microphone hears, with YAMNet", () => new SoundEventsPage()),
            (Theme.GlyphImage, "Mobilenet", "Recognize what is in an image", () => new MobilenetPage()),
            (Theme.GlyphSparkle, "Inception", "Recognize flower species", () => new InceptionPage()),
            (Theme.GlyphSettings, "Model Checker", "Inspect any .tflite model file", () => new ModelCheckerPage()),
        }));

        var list = new VerticalStackLayout { Spacing = 0 };
        var sectionTitle = Theme.SectionTitle(tabs[0].Title);

        void ShowTab(int index)
        {
            list.Children.Clear();
            sectionTitle.Text = tabs[index].Title;
            var rows = tabs[index].Rows;
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
        }

        // The tab pills: the selected one filled with the accent colour. The last tab used is opened again.
        var pills = new HorizontalStackLayout { Spacing = 12 };
        var pillCards = new List<(Border Card, Label Label, Image Icon, string Glyph)>();
        void SelectTab(int index)
        {
            for (int i = 0; i < pillCards.Count; i++)
            {
                bool selected = i == index;
                var pill = pillCards[i];
                pill.Card.BackgroundColor = selected ? Theme.Accent : Theme.CardBackground;
                pill.Card.Stroke = selected ? Colors.Transparent : Theme.RowBorder;
                pill.Label.TextColor = selected ? Colors.White : Theme.PrimaryText;
                pill.Icon.Source = Theme.MakeGlyphSource(pill.Glyph, selected ? Colors.White : Theme.PrimaryText, 20);
                SemanticProperties.SetDescription(pill.Card, tabs[i].Name + (selected ? ", selected" : ""));
            }
            ShowTab(index);
            try { Preferences.Default.Set(LastTabPreference, tabs[index].Name); } catch (Exception) { }
        }
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var icon = Theme.MakeIcon(tabs[i].Glyph, Theme.PrimaryText, 20);
            var label = new Label { Text = tabs[i].Name, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center };
            var card = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
                Stroke = Theme.RowBorder,
                StrokeThickness = 1,
                BackgroundColor = Theme.CardBackground,
                Padding = new Thickness(18, 12),
                Content = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { icon, label } }
            };
            card.OnTap(() => SelectTab(index));
            pillCards.Add((card, label, icon, tabs[i].Glyph));
            pills.Children.Add(card);
        }
        int initialTab = 0;
        try
        {
            string last = Preferences.Default.Get(LastTabPreference, null as string);
            int found = tabs.FindIndex(t => t.Name == last);
            if (found >= 0)
                initialTab = found;
        }
        catch (Exception)
        {
        }
        SelectTab(initialTab);

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
                pills,
                Theme.Card(new VerticalStackLayout { Spacing = 6, Children = { sectionTitle, list } }, 18)
            }
        });
    }
}
