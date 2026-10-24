//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

/// <summary>
/// One thing the app uses under an open-source or other licence: a library, model, font, data set or web service.
/// </summary>
internal sealed class LicenseEntry
{
    public string Category;
    public string Name;
    public string Version;
    public string License;
    public string Copyright;
    public string Url;
    public string UsedFor;
    public string Notice;
    public string[] Packages;
    public string Text;
}

/// <summary>
/// The open-source licences of what the app ships, downloads or shows (About > Open-source licences), from
/// Resources/Raw/licenses.json.gz - built by tools/make_licenses.py from the .NET packages, the native libraries'
/// dependencies and a list of models, fonts, data and services. Grouped by kind, searchable; an entry opens its
/// licence text.
/// </summary>
public class LicensesPage : ContentPage
{
    private static Task<List<LicenseEntry>> _entries;
    private readonly VerticalStackLayout _list = new VerticalStackLayout { Spacing = 16 };
    private readonly Label _status;
    private readonly Entry _search;
    private List<LicenseEntry> _loaded;

    public LicensesPage()
    {
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Theme.PageBackground;
        _search = new Entry
        {
            Placeholder = "Search libraries, models and licences",
            FontFamily = Theme.BodyFont,
            FontSize = 16,
            TextColor = Theme.PrimaryText,
            PlaceholderColor = Theme.SecondaryText,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _search.TextChanged += (s, e) => Show();
        var intro = new Label
        {
            Text = "This app uses the libraries, models, fonts, data and services below, under their licences. Tap one for its licence text.",
            FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.SecondaryText, LineBreakMode = LineBreakMode.WordWrap
        };
        var introCard = new VerticalStackLayout { Spacing = 8, Children = { intro, _search } };

        _status = new Label { Text = "Loading...", FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.SecondaryText };
        ScrollView scroll = Theme.CenteredScroll(new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(16, 12, 16, 24),
            Children =
            {
                Theme.PageHeader(this, Theme.GlyphInfo, "Open-source licences", "What this app is built with", true, false),
                Theme.Card(introCard, 12),
                _status,
                _list
            }
        });
        LimitWidth(scroll, intro);
        Content = scroll;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded != null)
            return;
        try
        {
            _loaded = await (_entries ??= Task.Run(LoadAsync));
            Show();
        }
        catch (Exception ex)
        {
            _entries = null;
            _status.Text = "Couldn't read the licence list: " + ex.Message;
        }
    }

    // The centred column (Theme.CenteredScroll) is as wide as its content, up to 760, so on Mac Catalyst a long label
    // widened its card and was cut off instead of wrapping: limit such labels to the column's width less the page's
    // and the card's padding.
    internal static void LimitWidth(ScrollView scroll, params Label[] labels)
    {
        scroll.SizeChanged += (s, e) =>
        {
            double width = Math.Min(760, scroll.Width) - 32 - 34;
            if (width > 0)
                foreach (Label label in labels)
                    label.MaximumWidthRequest = width;
        };
    }

    private static async Task<List<LicenseEntry>> LoadAsync()
    {
        using Stream file = await FileSystem.OpenAppPackageFileAsync("licenses.json.gz");
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using JsonDocument document = await JsonDocument.ParseAsync(gzip);
        JsonElement texts = document.RootElement.GetProperty("texts");
        string Get(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? v.GetString() : null;
        var entries = new List<LicenseEntry>();
        foreach (JsonElement e in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new LicenseEntry
            {
                Category = Get(e, "category"),
                Name = Get(e, "name"),
                Version = Get(e, "version"),
                License = Get(e, "license"),
                Copyright = Get(e, "copyright"),
                Url = Get(e, "url"),
                UsedFor = Get(e, "usedFor"),
                Notice = Get(e, "notice"),
                Packages = e.TryGetProperty("packages", out JsonElement p) ? p.EnumerateArray().Select(x => x.GetString()).ToArray() : null,
                Text = texts.GetProperty(Get(e, "text")).GetString()
            });
        }
        return entries;
    }

    private static bool Matches(LicenseEntry entry, string query) =>
        string.IsNullOrWhiteSpace(query)
        || new[] { entry.Name, entry.License, entry.Category, entry.Copyright, entry.UsedFor }
            .Any(t => t != null && t.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
        || (entry.Packages != null && entry.Packages.Any(p => p.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)));

    private void Show()
    {
        if (_loaded == null)
            return;
        _list.Children.Clear();
        List<LicenseEntry> shown = _loaded.Where(e => Matches(e, _search.Text)).ToList();
        _status.Text = shown.Count == _loaded.Count ? _loaded.Count + " entries" : shown.Count + " of " + _loaded.Count + " entries";
        foreach (IGrouping<string, LicenseEntry> category in shown.GroupBy(e => e.Category))
        {
            var rows = new VerticalStackLayout { Spacing = 0 };
            foreach (LicenseEntry entry in category)
            {
                if (rows.Children.Count > 0)
                    rows.Children.Add(Theme.Divider());
                rows.Children.Add(Row(entry));
            }
            _list.Children.Add(Theme.Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children = { Theme.SectionTitle(category.Key + " (" + category.Count() + ")"), rows }
            }));
        }
    }

    private View Row(LicenseEntry entry)
    {
        var row = new Grid
        {
            Padding = new Thickness(0, 10),
            ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        row.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = entry.Name + (entry.Version != null ? " " + entry.Version : ""),
                    FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.PrimaryText
                },
                new Label { Text = entry.License, FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText }
            }
        }, 0, 0);
        row.Add(Theme.MakeIcon(Theme.GlyphChevronRight, Theme.Chevron, 22), 1, 0);
        SemanticProperties.SetDescription(row, entry.Name + ", " + entry.License);
        row.OnTap(async () => await Navigation.PushAsync(new LicenseDetailPage(entry)));
        return row;
    }
}

/// <summary>One entry of the licence list: what it is, where it's from, and its licence text.</summary>
internal sealed class LicenseDetailPage : ContentPage
{
    public LicenseDetailPage(LicenseEntry entry)
    {
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Theme.PageBackground;
        var details = new VerticalStackLayout { Spacing = 0 };
        var wide = new List<Label>();
        void Info(string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            if (details.Children.Count > 0)
                details.Children.Add(Theme.Divider());
            details.Children.Add(new VerticalStackLayout
            {
                Padding = new Thickness(0, 8),
                Spacing = 2,
                Children =
                {
                    new Label { Text = name, FontFamily = Theme.BodyFont, FontSize = 13, TextColor = Theme.SecondaryText },
                    Value(value)
                }
            });
        }
        Label Value(string value)
        {
            var label = new Label { Text = value, FontFamily = Theme.BodyFont, FontSize = 15, TextColor = Theme.PrimaryText };
            wide.Add(label);
            return label;
        }
        Info("Licence", entry.License);
        Info("Version", entry.Version);
        Info("Copyright", entry.Copyright);
        Info("Used for", entry.UsedFor);
        Info("Note", entry.Notice);
        Info("Includes", entry.Packages == null ? null : string.Join(", ", entry.Packages));
        if (!string.IsNullOrEmpty(entry.Url) && Uri.TryCreate(entry.Url, UriKind.Absolute, out Uri uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            details.Children.Add(Theme.Divider());
            var link = new Label
            {
                Text = entry.Url, FontFamily = Theme.TitleFont, FontSize = 15, TextColor = Theme.Accent, Padding = new Thickness(0, 10)
            };
            SemanticProperties.SetDescription(link, "Open the website of " + entry.Name);
            link.OnTap(async () =>
            {
                try { await Launcher.OpenAsync(uri); } catch (Exception) { }
            });
            details.Children.Add(link);
        }

        var text = new Label
        {
            Text = entry.Text,
            FontFamily = DeviceInfo.Platform == DevicePlatform.Android ? "monospace" : DeviceInfo.Platform == DevicePlatform.WinUI ? "Consolas" : "Menlo",
            FontSize = 12,
            TextColor = Theme.PrimaryText,
            LineBreakMode = LineBreakMode.WordWrap
        };
        wide.Add(text);
        ScrollView scroll = Theme.CenteredScroll(new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(16, 12, 16, 24),
            Children =
            {
                Theme.PageHeader(this, Theme.GlyphInfo, entry.Name, entry.Category, true, false),
                Theme.Card(details),
                Theme.Card(new VerticalStackLayout { Spacing = 8, Children = { Theme.SectionTitle("Licence text"), text } })
            }
        });
        LicensesPage.LimitWidth(scroll, wide.ToArray());
        Content = scroll;
    }
}
