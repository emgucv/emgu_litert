//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Shows a chat reply: text with basic Markdown formatting (**bold**, *italic*, `inline code`, headings, bullet and
    /// numbered lists, quotes, links), fenced code blocks as <see cref="CodeBlockView"/>s with a Copy button, and LaTeX
    /// maths as Unicode text (x² + y² = r², not $x^2 + y^2 = r^2$; display maths centered). Set <see cref="Text"/> as the
    /// reply streams: the views of earlier parts are kept and only the last one is updated.
    /// <para>
    /// Its look comes from templates and styles found by key (an app replaces one by defining a resource with the same
    /// key, see <see cref="ChatStyles"/>): "ReplyViewTemplate" (whose "PART_Blocks" layout holds the parts),
    /// "ReplyParagraphStyle" and "ReplyMathStyle" (Labels), and "CodeBlockViewTemplate"; and from the properties here.
    /// </para>
    /// </summary>
    public class ReplyView : TemplatedView
    {
        private const string TemplateKey = "ReplyViewTemplate";

        /// <summary>The reply's text, as streamed so far.</summary>
        public static readonly BindableProperty TextProperty = BindableProperty.Create(
            nameof(Text), typeof(string), typeof(ReplyView), string.Empty, propertyChanged: (b, o, n) => ((ReplyView)b).Update());

        /// <summary>The text colour; null for the "ReplyParagraphStyle" one.</summary>
        public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
            nameof(TextColor), typeof(Color), typeof(ReplyView), null, propertyChanged: Restyle);

        /// <summary>The size of the text; headings and maths are sized relative to it.</summary>
        public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
            nameof(FontSize), typeof(double), typeof(ReplyView), 15.0, propertyChanged: Restyle);

        /// <summary>The text's font; null for the "ReplyParagraphStyle" one.</summary>
        public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
            nameof(FontFamily), typeof(string), typeof(ReplyView), null, propertyChanged: Restyle);

        /// <summary>The font of inline code and code blocks.</summary>
        public static readonly BindableProperty MonospaceFontFamilyProperty = BindableProperty.Create(
            nameof(MonospaceFontFamily), typeof(string), typeof(ReplyView), DefaultMonospaceFont, propertyChanged: Restyle);

        /// <summary>The colour of links.</summary>
        public static readonly BindableProperty LinkColorProperty = BindableProperty.Create(
            nameof(LinkColor), typeof(Color), typeof(ReplyView), Color.FromArgb("#3D7BF7"), propertyChanged: Restyle);

        /// <summary>The colour of quotes and rules.</summary>
        public static readonly BindableProperty SecondaryTextColorProperty = BindableProperty.Create(
            nameof(SecondaryTextColor), typeof(Color), typeof(ReplyView), Color.FromArgb("#8A8FA3"), propertyChanged: Restyle);

        /// <summary>The colour of the bar in front of a quote.</summary>
        public static readonly BindableProperty QuoteMarkColorProperty = BindableProperty.Create(
            nameof(QuoteMarkColor), typeof(Color), typeof(ReplyView), Color.FromArgb("#C2C7D6"), propertyChanged: Restyle);

        /// <summary>The background of `inline code`.</summary>
        public static readonly BindableProperty InlineCodeBackgroundColorProperty = BindableProperty.Create(
            nameof(InlineCodeBackgroundColor), typeof(Color), typeof(ReplyView), Color.FromArgb("#1A1F2430"), propertyChanged: Restyle);

#if ANDROID
        private const string DefaultMonospaceFont = "monospace";
#elif IOS || MACCATALYST
        private const string DefaultMonospaceFont = "Menlo";
#else
        private const string DefaultMonospaceFont = "Consolas";
#endif

        private sealed class Part
        {
            public ReplyBlock Block;
            public View View;
            public Label Label;
            public CodeBlockView Code;
        }

        private readonly List<Part> _shown = new List<Part>();
        private Layout _blocks;

        /// <summary>The reply's text, as streamed so far.</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>The text colour; null for the "ReplyParagraphStyle" one.</summary>
        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        /// <summary>The size of the text; headings and maths are sized relative to it.</summary>
        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        /// <summary>The text's font; null for the "ReplyParagraphStyle" one.</summary>
        public string FontFamily
        {
            get => (string)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        /// <summary>The font of inline code and code blocks.</summary>
        public string MonospaceFontFamily
        {
            get => (string)GetValue(MonospaceFontFamilyProperty);
            set => SetValue(MonospaceFontFamilyProperty, value);
        }

        /// <summary>The colour of links.</summary>
        public Color LinkColor
        {
            get => (Color)GetValue(LinkColorProperty);
            set => SetValue(LinkColorProperty, value);
        }

        /// <summary>The colour of quotes and rules.</summary>
        public Color SecondaryTextColor
        {
            get => (Color)GetValue(SecondaryTextColorProperty);
            set => SetValue(SecondaryTextColorProperty, value);
        }

        /// <summary>The colour of the bar in front of a quote.</summary>
        public Color QuoteMarkColor
        {
            get => (Color)GetValue(QuoteMarkColorProperty);
            set => SetValue(QuoteMarkColorProperty, value);
        }

        /// <summary>The background of `inline code`.</summary>
        public Color InlineCodeBackgroundColor
        {
            get => (Color)GetValue(InlineCodeBackgroundColorProperty);
            set => SetValue(InlineCodeBackgroundColorProperty, value);
        }

        /// <summary>
        /// True if the reply contains a code block (a chat may then let the reply use the full width).
        /// </summary>
        public bool HasCode
        {
            get { return _shown.Exists(p => p.Block.Kind == ReplyBlockKind.Code); }
        }

        /// <summary>
        /// Raised when a link is tapped. Unless a handler sets <see cref="ReplyLinkTappedEventArgs.Handled"/>, http and
        /// https links are opened in the browser.
        /// </summary>
        public event EventHandler<ReplyLinkTappedEventArgs> LinkTapped;

        /// <inheritdoc/>
        protected override void OnParentSet()
        {
            base.OnParentSet();
            ChatResources.ApplyDefaultTemplate(this, TemplateKey);
        }

        /// <inheritdoc/>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _blocks?.Clear();
            _blocks = GetTemplateChild("PART_Blocks") as Layout;
            _shown.Clear();
            Update();
        }

        private static void Restyle(BindableObject bindable, object oldValue, object newValue)
        {
            ReplyView view = (ReplyView)bindable;
            // Rebuild every part with the new look.
            view._blocks?.Clear();
            view._shown.Clear();
            view.Update();
        }

        private void Update()
        {
            if (_blocks == null)
                return;
            IReadOnlyList<ReplyBlock> blocks = ReplyMarkdown.SplitBlocks(Text);
            for (int i = 0; i < blocks.Count; i++)
            {
                ReplyBlock block = blocks[i];
                if (i < _shown.Count && _shown[i].Block.Kind == block.Kind && _shown[i].Block.Language == block.Language)
                {
                    // Same kind of part as before: only its text may have grown.
                    if (_shown[i].Block.Text != block.Text)
                    {
                        _shown[i].Block = block;
                        UpdatePart(_shown[i]);
                    }
                    continue;
                }
                while (_shown.Count > i)
                    RemoveLast();
                Part part = CreatePart(block);
                _shown.Add(part);
                _blocks.Add(part.View);
            }
            while (_shown.Count > blocks.Count)
                RemoveLast();
        }

        private void RemoveLast()
        {
            _blocks.Remove(_shown[_shown.Count - 1].View);
            _shown.RemoveAt(_shown.Count - 1);
        }

        private Part CreatePart(ReplyBlock block)
        {
            Part part = new Part { Block = block };
            switch (block.Kind)
            {
                case ReplyBlockKind.Code:
                    part.Code = new CodeBlockView { CodeFontFamily = MonospaceFontFamily, HeaderFontFamily = FontFamily };
                    part.View = part.Code;
                    break;
                case ReplyBlockKind.Math:
                    part.Label = NewLabel("ReplyMathStyle", FontSize + 2);
                    part.View = part.Label;
                    break;
                default:
                    part.Label = NewLabel("ReplyParagraphStyle", FontSize);
                    part.View = part.Label;
                    break;
            }
            UpdatePart(part);
            return part;
        }

        private Label NewLabel(string styleKey, double fontSize)
        {
            Label label = new Label { Style = ChatResources.Find<Style>(styleKey) };
            label.FontSize = fontSize;
            if (TextColor != null)
                label.TextColor = TextColor;
            if (FontFamily != null)
                label.FontFamily = FontFamily;
            return label;
        }

        private void UpdatePart(Part part)
        {
            switch (part.Block.Kind)
            {
                case ReplyBlockKind.Code:
                    part.Code.Language = part.Block.Language;
                    part.Code.Code = part.Block.Text;
                    break;
                case ReplyBlockKind.Math:
                    part.Label.Text = MathText.ToUnicode(part.Block.Text);
                    break;
                default:
                    part.Label.FormattedText = FormatMarkdown(part.Block.Text);
                    break;
            }
        }

        private FormattedString FormatMarkdown(string text)
        {
            FormattedString formatted = new FormattedString();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    formatted.Spans.Add(new Span { Text = "\n" });
                ReplyLine line = ReplyMarkdown.ParseLine(lines[i]);
                switch (line.Kind)
                {
                    case ReplyLineKind.Heading:
                        AddInline(formatted, line.Content, FontAttributes.Bold, FontSize + (line.HeadingLevel <= 2 ? 4 : 2), null);
                        break;
                    case ReplyLineKind.Rule:
                        formatted.Spans.Add(new Span { Text = "———", TextColor = SecondaryTextColor });
                        break;
                    case ReplyLineKind.Bullet:
                    case ReplyLineKind.Numbered:
                        formatted.Spans.Add(new Span { Text = line.Indent + line.Marker + " " });
                        AddInline(formatted, line.Content, FontAttributes.None, 0, null);
                        break;
                    case ReplyLineKind.Quote:
                        formatted.Spans.Add(new Span { Text = "▏ ", TextColor = QuoteMarkColor });
                        AddInline(formatted, line.Content, FontAttributes.Italic, 0, SecondaryTextColor);
                        break;
                    default:
                        AddInline(formatted, line.Content, FontAttributes.None, 0, null);
                        break;
                }
            }
            return formatted;
        }

        private void AddInline(FormattedString formatted, string text, FontAttributes baseAttributes, double fontSize, Color color)
        {
            foreach (ReplyRun run in ReplyMarkdown.ParseInline(text))
            {
                switch (run.Kind)
                {
                    case ReplyRunKind.Code:
                        Span code = new Span
                        {
                            Text = run.Text,
                            FontFamily = MonospaceFontFamily,
                            FontSize = fontSize > 0 ? fontSize - 1 : FontSize - 1,
                            BackgroundColor = InlineCodeBackgroundColor
                        };
                        if ((color ?? TextColor) != null)
                            code.TextColor = color ?? TextColor;
                        formatted.Spans.Add(code);
                        break;
                    case ReplyRunKind.Bold:
                        formatted.Spans.Add(TextSpan(run.Text, baseAttributes | FontAttributes.Bold, fontSize, color));
                        break;
                    case ReplyRunKind.Italic:
                        formatted.Spans.Add(TextSpan(run.Text, baseAttributes | FontAttributes.Italic, fontSize, color));
                        break;
                    case ReplyRunKind.Link:
                        formatted.Spans.Add(LinkSpan(run, baseAttributes, fontSize));
                        break;
                    default:
                        // Plain text, and inline maths (already Unicode text).
                        formatted.Spans.Add(TextSpan(run.Text, baseAttributes, fontSize, color));
                        break;
                }
            }
        }

        private Span LinkSpan(ReplyRun run, FontAttributes attributes, double fontSize)
        {
            Span link = new Span
            {
                Text = run.Text,
                TextColor = LinkColor,
                TextDecorations = TextDecorations.Underline,
                FontAttributes = attributes
            };
            if (fontSize > 0)
                link.FontSize = fontSize;
            TapGestureRecognizer tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                ReplyLinkTappedEventArgs args = new ReplyLinkTappedEventArgs(run.Url);
                LinkTapped?.Invoke(this, args);
                if (args.Handled)
                    return;
                try
                {
                    if (Uri.TryCreate(run.Url, UriKind.Absolute, out Uri uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                        await Launcher.Default.OpenAsync(uri);
                }
                catch (Exception)
                {
                }
            };
            link.GestureRecognizers.Add(tap);
            return link;
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
    }

    /// <summary>
    /// A link tapped in a <see cref="ReplyView"/>.
    /// </summary>
    public sealed class ReplyLinkTappedEventArgs : EventArgs
    {
        internal ReplyLinkTappedEventArgs(string url)
        {
            Url = url;
        }

        /// <summary>The link's address as the model wrote it (not checked).</summary>
        public string Url { get; }

        /// <summary>Set to true to stop the link being opened in the browser.</summary>
        public bool Handled { get; set; }
    }
}
