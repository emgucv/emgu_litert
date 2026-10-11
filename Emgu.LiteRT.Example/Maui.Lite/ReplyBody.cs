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
    /// The answer part of a chat reply: plain text, with Markdown code blocks (```lang ... ```) shown as monospace
    /// panels with the language and a Copy button. SetText can be called with the text streamed so far: views of
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
                        _shown[i].TextLabel.Text = segment.Text;
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
                segment.TextLabel = new Label { Text = segment.Text, TextColor = _textColor, FontFamily = Theme.BodyFont, FontSize = 15 };
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
