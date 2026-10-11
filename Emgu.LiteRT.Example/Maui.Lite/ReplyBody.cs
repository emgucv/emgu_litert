//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// The answer part of a chat reply: text with basic Markdown formatting (**bold**, *italic*, `inline code`,
    /// headings, bullet and numbered lists, quotes, links), and Markdown code blocks (```lang ... ```) shown as
    /// monospace panels with the language and a Copy button. SetText can be called with the text streamed so far: views of
    /// earlier parts are kept and only the last one is updated, and a code block that hasn't been closed yet is shown
    /// as code already.
    /// </summary>
    internal sealed class ReplyBody : VerticalStackLayout
    {
        private static readonly Color CodeBackground = Color.FromArgb("#1F2430");
        private static readonly Color CodeText = Color.FromArgb("#E6E9F0");
        private static readonly Color CodeHeaderText = Color.FromArgb("#9AA3B5");

#if ANDROID
        private const string MonospaceFont = "monospace";
#elif IOS || MACCATALYST
        private const string MonospaceFont = "Menlo";
#else
        private const string MonospaceFont = "Consolas";
#endif

        private static readonly Regex Fence = new Regex(@"^ {0,3}```\s*([^\s`]*)\s*$");

        private readonly Color _textColor;
        private readonly List<Segment> _shown = new List<Segment>();

        public ReplyBody(Color textColor)
        {
            _textColor = textColor;
            Spacing = 8;
        }

        private sealed class Segment
        {
            public bool IsCode;
            public string Language;
            public string Text;
            public View View;
            public Label TextLabel;
        }

        public void SetText(string text)
        {
            List<Segment> segments = Parse(text ?? string.Empty);
            for (int i = 0; i < segments.Count; i++)
            {
                Segment segment = segments[i];
                if (i < _shown.Count && _shown[i].IsCode == segment.IsCode && _shown[i].Language == segment.Language)
                {
                    // Same kind of part as before: only its text may have grown.
                    if (_shown[i].Text != segment.Text)
                    {
                        _shown[i].Text = segment.Text;
                        if (segment.IsCode)
                            _shown[i].TextLabel.Text = segment.Text;
                        else
                            _shown[i].TextLabel.FormattedText = FormatMarkdown(segment.Text);
                    }
                    continue;
                }
                while (_shown.Count > i)
                {
                    Children.Remove(_shown[_shown.Count - 1].View);
                    _shown.RemoveAt(_shown.Count - 1);
                }
                CreateView(segment);
                _shown.Add(segment);
                Children.Add(segment.View);
            }
            while (_shown.Count > segments.Count)
            {
                Children.Remove(_shown[_shown.Count - 1].View);
                _shown.RemoveAt(_shown.Count - 1);
            }
        }

        /// <summary>
        /// True if the reply contains a code block (the chat page then lets the bubble use the full width).
        /// </summary>
        public bool HasCode
        {
            get { return _shown.Exists(s => s.IsCode); }
        }

        // ---------- Markdown text ----------

        private static readonly Regex Heading = new Regex(@"^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex Bullet = new Regex(@"^(\s*)[-*+]\s+(.*)$");
        private static readonly Regex Numbered = new Regex(@"^(\s*)(\d+[.)])\s+(.*)$");
        private static readonly Regex Quote = new Regex(@"^ {0,3}>\s?(.*)$");
        private static readonly Regex Rule = new Regex(@"^ {0,3}([-*_])(\s*\1){2,}\s*$");
        // Inline: `code`, **bold** / __bold__, [text](url), *italic* / _italic_ (underscores only at word boundaries, so
        // snake_case stays as it is). Unclosed markup (e.g. while streaming) is shown as it is.
        private static readonly Regex Inline = new Regex(
            @"`([^`\n]+)`|\*\*(.+?)\*\*|(?<![\w])__(.+?)__(?![\w])|\[([^\]\n]+)\]\(([^)\s]+)\)|(?<![\w*])\*(?!\s)([^*\n]+?)\*(?![\w*])|(?<![\w])_(?!\s)([^_\n]+?)_(?![\w])");

        private FormattedString FormatMarkdown(string text)
        {
            FormattedString formatted = new FormattedString();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    formatted.Spans.Add(new Span { Text = "\n" });
                string line = lines[i].TrimEnd('\r');
                Match m;
                if ((m = Heading.Match(line)).Success)
                {
                    double size = m.Groups[1].Length <= 2 ? 19 : 17;
                    AddInline(formatted, m.Groups[2].Value, FontAttributes.Bold, size, null);
                }
                else if (Rule.IsMatch(line))
                {
                    formatted.Spans.Add(new Span { Text = "\u2014\u2014\u2014", TextColor = Theme.SecondaryText });
                }
                else if ((m = Bullet.Match(line)).Success)
                {
                    formatted.Spans.Add(new Span { Text = Indent(m.Groups[1].Value) + "\u2022 " });
                    AddInline(formatted, m.Groups[2].Value, FontAttributes.None, 0, null);
                }
                else if ((m = Numbered.Match(line)).Success)
                {
                    formatted.Spans.Add(new Span { Text = Indent(m.Groups[1].Value) + m.Groups[2].Value + " " });
                    AddInline(formatted, m.Groups[3].Value, FontAttributes.None, 0, null);
                }
                else if ((m = Quote.Match(line)).Success)
                {
                    formatted.Spans.Add(new Span { Text = "\u258F ", TextColor = Theme.Chevron });
                    AddInline(formatted, m.Groups[1].Value, FontAttributes.Italic, 0, Theme.SecondaryText);
                }
                else
                {
                    AddInline(formatted, line, FontAttributes.None, 0, null);
                }
            }
            return formatted;
        }

        private static string Indent(string leading)
        {
            // Two spaces (or a tab) per nesting level.
            int width = leading.Replace("\t", "  ").Length;
            return new string(' ', width / 2 * 3);
        }

        private void AddInline(FormattedString formatted, string text, FontAttributes baseAttributes, double fontSize, Color color)
        {
            int position = 0;
            foreach (Match m in Inline.Matches(text))
            {
                if (m.Index > position)
                    formatted.Spans.Add(TextSpan(text.Substring(position, m.Index - position), baseAttributes, fontSize, color));
                if (m.Groups[1].Success)
                {
                    formatted.Spans.Add(new Span
                    {
                        Text = m.Groups[1].Value,
                        FontFamily = MonospaceFont,
                        FontSize = fontSize > 0 ? fontSize - 1 : 14,
                        BackgroundColor = Color.FromArgb("#1A1F2430"),
                        TextColor = color ?? _textColor
                    });
                }
                else if (m.Groups[2].Success || m.Groups[3].Success)
                {
                    string bold = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
                    formatted.Spans.Add(TextSpan(bold, baseAttributes | FontAttributes.Bold, fontSize, color));
                }
                else if (m.Groups[4].Success)
                {
                    string url = m.Groups[5].Value;
                    Span link = new Span
                    {
                        Text = m.Groups[4].Value,
                        TextColor = Theme.Accent,
                        TextDecorations = TextDecorations.Underline,
                        FontAttributes = baseAttributes
                    };
                    if (fontSize > 0)
                        link.FontSize = fontSize;
                    TapGestureRecognizer tap = new TapGestureRecognizer();
                    tap.Tapped += async (s, e) =>
                    {
                        try
                        {
                            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                                await Launcher.Default.OpenAsync(uri);
                        }
                        catch (Exception)
                        {
                        }
                    };
                    link.GestureRecognizers.Add(tap);
                    formatted.Spans.Add(link);
                }
                else
                {
                    string italic = m.Groups[6].Success ? m.Groups[6].Value : m.Groups[7].Value;
                    formatted.Spans.Add(TextSpan(italic, baseAttributes | FontAttributes.Italic, fontSize, color));
                }
                position = m.Index + m.Length;
            }
            if (position < text.Length)
                formatted.Spans.Add(TextSpan(text.Substring(position), baseAttributes, fontSize, color));
        }

        private static Span TextSpan(string text, FontAttributes attributes, double fontSize, Color color)
        {
            Span span = new Span { Text = text, FontAttributes = attributes };
            if (fontSize > 0)
                span.FontSize = fontSize;
            if (color != null)
                span.TextColor = color;
            return span;
        }

        // Split the text into plain text and fenced code blocks; an unclosed fence (still streaming) runs to the end.
        private static List<Segment> Parse(string text)
        {
            List<Segment> segments = new List<Segment>();
            List<string> lines = new List<string>();
            bool inCode = false;
            string language = null;

            void Flush()
            {
                string content = string.Join("\n", lines);
                if (inCode)
                    segments.Add(new Segment { IsCode = true, Language = language ?? "", Text = content.TrimEnd('\n') });
                else if (content.Trim().Length > 0)
                    segments.Add(new Segment { IsCode = false, Text = content.Trim('\n', '\r', ' ') });
                lines.Clear();
            }

            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                Match fence = Fence.Match(rawLine);
                if (fence.Success)
                {
                    Flush();
                    inCode = !inCode;
                    language = inCode ? fence.Groups[1].Value : null;
                    continue;
                }
                lines.Add(rawLine);
            }
            Flush();
            return segments;
        }

        private void CreateView(Segment segment)
        {
            if (!segment.IsCode)
            {
                segment.TextLabel = new Label { FormattedText = FormatMarkdown(segment.Text), TextColor = _textColor, FontFamily = Theme.BodyFont, FontSize = 15 };
                segment.View = segment.TextLabel;
                return;
            }

            segment.TextLabel = new Label
            {
                Text = segment.Text,
                TextColor = CodeText,
                FontFamily = MonospaceFont,
                FontSize = 13,
                // Long lines wrap: a horizontal ScrollView around the label (to keep lines whole) is measured once and
                // didn't grow as the streamed code got longer, showing only its first line.
                LineBreakMode = LineBreakMode.WordWrap
            };
            Label copy = new Label
            {
                Text = "Copy",
                FontFamily = Theme.TitleFont,
                FontSize = 12,
                TextColor = CodeHeaderText,
                Padding = new Thickness(8, 2),
                HorizontalOptions = LayoutOptions.End
            };
            SemanticProperties.SetDescription(copy, "Copy the code");
            copy.OnTap(async () =>
            {
                try
                {
                    await Clipboard.Default.SetTextAsync(segment.Text);
                    copy.Text = "Copied";
                    await Task.Delay(2000);
                    copy.Text = "Copy";
                }
                catch (Exception)
                {
                    copy.Text = "Copy failed";
                }
            });
            var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            header.Add(new Label
            {
                Text = string.IsNullOrEmpty(segment.Language) ? "code" : segment.Language,
                FontFamily = Theme.BodyFont,
                FontSize = 12,
                TextColor = CodeHeaderText,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            header.Add(copy, 1, 0);

            segment.View = new Border
            {
                BackgroundColor = CodeBackground,
                Stroke = Colors.Transparent,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(10, 6, 10, 10),
                Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        header,
                        segment.TextLabel
                    }
                }
            };
        }
    }
}

#endif
