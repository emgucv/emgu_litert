//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Maui.Demo.Lite
{
    public class AboutPage : ContentPage
    {
        public AboutPage()
        {
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            String tensorflowVer = Emgu.TF.Lite.TfLiteInvoke.Version;
            bool hasXnnPack = Emgu.TF.Lite.TfLiteInvoke.HasXNNPack;

            // LiteRT has no runtime-queryable version API; its Major.Minor.Patch is baked into the
            // Emgu.LiteRT assembly's version at build time, so read it back via reflection. The
            // Revision component is Emgu's own git commit count, not part of LiteRT's version.
            Version asmVer = typeof(Emgu.LiteRT.CompiledModel).Assembly.GetName().Version;
            String liteRtVer = (asmVer == null)
                ? "unknown"
                : String.Format("{0}.{1}.{2}", asmVer.Major, asmVer.Minor, asmVer.Build);

            var versions = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    InfoRow("TensorFlow Lite", tensorflowVer),
                    Theme.Divider(),
                    InfoRow("LiteRT", liteRtVer),
                    Theme.Divider(),
                    InfoRow("XNNPack", hasXnnPack ? "Yes" : "No")
                }
            };

            var links = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    LinkRow(Theme.GlyphInfo, "TensorFlow Lite license", "https://github.com/tensorflow/tensorflow/blob/master/LICENSE"),
                    Theme.Divider(54),
                    LinkRow(Theme.GlyphGrid, "Visit our website", "https://www.emgu.com/wiki/index.php/Emgu_TF"),
                    Theme.Divider(54),
                    LinkRow(Theme.GlyphText, "Email support", "mailto:support@emgu.com")
                }
            };

            Content = Theme.CenteredScroll(new VerticalStackLayout
            {
                Spacing = 16,
                Padding = new Thickness(16, 12, 16, 24),
                Children =
                {
                    Theme.PageHeader(this, Theme.GlyphInfo, "About", "Emgu LiteRT examples", true, false),
                    Theme.Card(new VerticalStackLayout { Spacing = 8, Children = { Theme.SectionTitle("Versions"), versions } }),
                    Theme.Card(new VerticalStackLayout { Spacing = 8, Children = { Theme.SectionTitle("Links"), links } })
                }
            });
        }

        private static View InfoRow(string name, string value)
        {
            var g = new Grid
            {
                Padding = new Thickness(0, 12),
                ColumnSpacing = 16,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            g.Add(new Label { Text = name, FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.SecondaryText }, 0, 0);
            g.Add(new Label { Text = value, FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText }, 1, 0);
            return g;
        }

        private static View LinkRow(string glyph, string text, string uri)
        {
            var g = new Grid
            {
                Padding = new Thickness(0, 10),
                ColumnSpacing = 12,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            g.Add(Theme.IconTile(glyph, 42, 22, 11), 0, 0);
            g.Add(new Label { Text = text, FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText, VerticalOptions = LayoutOptions.Center }, 1, 0);
            g.Add(Theme.MakeIcon(Theme.GlyphChevronRight, Theme.Chevron, 22), 2, 0);
            g.OnTap(async () =>
            {
                try { await Launcher.OpenAsync(new Uri(uri)); } catch { }
            });
            return g;
        }
    }
}
