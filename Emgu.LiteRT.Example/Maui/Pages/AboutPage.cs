//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    public class AboutPage : ContentPage
    {
        public AboutPage()
        {
            Shell.SetNavBarIsVisible(this, false);
            BackgroundColor = Theme.PageBackground;

            String tensorflowVer = Emgu.TF.Lite.TfLiteInvoke.Version;

            // LiteRT has no runtime-queryable version API; its Major.Minor.Patch is baked into the
            // Emgu.LiteRT assembly's version at build time, so read it back via reflection. The
            // Revision component is Emgu's own git commit count, not part of LiteRT's version.
            Version asmVer = typeof(Emgu.LiteRT.CompiledModel).Assembly.GetName().Version;
            String liteRtVer = (asmVer == null)
                ? "unknown"
                : String.Format("{0}.{1}.{2}", asmVer.Major, asmVer.Minor, asmVer.Build);

            // LiteRT-LM has no version function either; Emgu.LiteRT.LM's assembly version carries it (from
            // LiteRT-LM/version.bzl) the same way. Shown once liblitert-lm loads, which isn't in every build (e.g. Android
            // armeabi-v7a / x86) - in the background, since it is ~30 MB.
            Version lmAsmVer = typeof(Emgu.LiteRT.LM.Engine).Assembly.GetName().Version;
            String liteRtLmVer = (lmAsmVer == null)
                ? "unknown"
                : String.Format("{0}.{1}.{2}", lmAsmVer.Major, lmAsmVer.Minor, lmAsmVer.Build);
            View liteRtLmRow = InfoRow("LiteRT-LM", "\u2026", out Label liteRtLmLabel);
            _ = Task.Run(() =>
            {
                String shown;
                try
                {
                    shown = Emgu.LiteRT.LM.LiteRtLmInvoke.IsV018OrLater ? liteRtLmVer : "older than 0.18.0";
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
                {
                    shown = "Not included";
                }
                MainThread.BeginInvokeOnMainThread(() => liteRtLmLabel.Text = shown);
            });

            var versions = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    InfoRow("TensorFlow Lite", tensorflowVer),
                    Theme.Divider(),
                    InfoRow("LiteRT", liteRtVer),
                    Theme.Divider(),
                    liteRtLmRow
                }
            };

            var links = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    NavRow(Theme.GlyphInfo, "Open-source licences", () => Navigation.PushAsync(new LicensesPage())),
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
            return InfoRow(name, value, out _);
        }

        private static View InfoRow(string name, string value, out Label valueLabel)
        {
            var g = new Grid
            {
                Padding = new Thickness(0, 12),
                ColumnSpacing = 16,
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
            };
            g.Add(new Label { Text = name, FontFamily = Theme.BodyFont, FontSize = 16, TextColor = Theme.SecondaryText }, 0, 0);
            valueLabel = new Label { Text = value, FontFamily = Theme.TitleFont, FontSize = 16, TextColor = Theme.PrimaryText };
            g.Add(valueLabel, 1, 0);
            return g;
        }

        private static View NavRow(string glyph, string text, Func<Task> open)
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
            g.OnTap(async () => await open());
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
