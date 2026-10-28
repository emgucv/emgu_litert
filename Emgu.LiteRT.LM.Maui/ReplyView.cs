//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Shows a chat reply: text with basic Markdown formatting (**bold**, *italic*, `inline code`, headings, bullet and
    /// numbered lists, quotes, links), fenced code blocks as <see cref="CodeBlockView"/>s with a Copy button, tables as a
    /// bordered grid (scrolled sideways when wider than the reply), and LaTeX maths as Unicode text (x² + y² = r², not $x^2 + y^2 = r^2$; display maths centered). Set <see cref="Text"/> as the
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
            public Grid Table;
        }

        // The widest a table's cell's text grows before it wraps - narrower on a phone-sized screen, so a comparison's
        // columns fit side by side (at 220 points only two did on a folded Pixel Fold) - and the space either side of it.
        private static double MaxCellWidth
        {
            get
            {
                DisplayInfo display = DeviceDisplay.Current.MainDisplayInfo;
                double width = display.Density > 0 ? Math.Min(display.Width, display.Height) / display.Density : 0;
                return width > 0 && width < 600 ? 140 : 220;
            }
        }
        private const double CellPadding = 10;

        // A cell's length as shown, roughly: its text without the inline markup.
        private static int PlainLength(string cell) => ReplyMarkdown.ParseInline(cell ?? string.Empty).Sum(r => r.Text.Length);

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

        /// <summary>True if the reply contains a code block or a table (bindable: a chat may then let the reply use the full width).</summary>
        public static readonly BindableProperty HasCodeProperty = BindableProperty.Create(
            nameof(HasCode), typeof(bool), typeof(ReplyView), false, BindingMode.OneWayToSource);

        /// <summary>
        /// True if the reply contains a code block or a table (a chat may then let the reply use the full width).
        /// </summary>
        public bool HasCode
        {
            get { return (bool)GetValue(HasCodeProperty); }
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
            // A table is as wide as a code block: the reply may use the full width for either.
            bool hasCode = _shown.Exists(p => p.Block.Kind == ReplyBlockKind.Code || p.Block.Kind == ReplyBlockKind.Table);
            if (hasCode != HasCode)
                SetValue(HasCodeProperty, hasCode);
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
                case ReplyBlockKind.Table:
                    part.Table = new Grid { ColumnSpacing = 0, RowSpacing = 0, HorizontalOptions = LayoutOptions.Start };
                    part.View = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = part.Table, Margin = new Thickness(0, 2) };
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
                case ReplyBlockKind.Table:
                    FillTable(part.Table, part.Block);
                    break;
                default:
                    part.Label.FormattedText = FormatMarkdown(part.Block.Text);
                    break;
            }
        }

        // A table as a grid with a full border: a line around it and between every row and column (lines of their own
        // in thin grid rows and columns, so they show over any background), the header in bold on a light shade, each
        // cell's text wrapping at MaxCellWidth. Rebuilt as the table streams in (tables are small).
        private void FillTable(Grid grid, ReplyBlock block)
        {
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            int columns = block.TableHeader.Count;
            if (columns == 0)
                return;
            const double line = 1;
            // Columns: a line, then a cell and a line for each column. A column whose text is longer than MaxCellWidth gets that
            // width (plus the cells' margins): an Auto column measured a wrapping label at its one-line width, so its row was
            // one line high and the rest of the text was cut off.
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(line)));
            for (int c = 0; c < columns; c++)
            {
                int longest = block.TableRows.Select(r => PlainLength(r[c])).Append(PlainLength(block.TableHeader[c])).Max();
                bool wraps = longest * FontSize * 0.55 > MaxCellWidth;
                grid.ColumnDefinitions.Add(new ColumnDefinition(wraps ? new GridLength(MaxCellWidth + CellPadding * 2) : GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(line)));
            }
            // Rows: a line, then a row of cells and a line for the header and each row.
            int rows = 1 + block.TableRows.Count;
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(line)));
            for (int r = 0; r < rows; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(line)));
            }
            int gridColumns = grid.ColumnDefinitions.Count, gridRows = grid.RowDefinitions.Count;

            // The header's shade, under its cells.
            BoxView shade = new BoxView { Color = QuoteMarkColor.WithAlpha(0.18f) };
            grid.Add(shade, 0, 1);
            Grid.SetColumnSpan(shade, gridColumns);

            void AddRow(IReadOnlyList<string> cells, int row, bool header)
            {
                for (int c = 0; c < columns; c++)
                {
                    Label cell = NewLabel("ReplyParagraphStyle", FontSize);
                    cell.LineBreakMode = LineBreakMode.WordWrap;
                    cell.Margin = new Thickness(CellPadding, 6);
                    cell.HorizontalTextAlignment = block.TableAlignments[c] == ReplyTableAlignment.Right ? TextAlignment.End
                        : block.TableAlignments[c] == ReplyTableAlignment.Center ? TextAlignment.Center : TextAlignment.Start;
                    FormattedString text = new FormattedString();
                    AddInline(text, c < cells.Count ? cells[c] : string.Empty, header ? FontAttributes.Bold : FontAttributes.None, 0, null);
                    cell.FormattedText = text;
                    grid.Add(cell, 1 + 2 * c, row);
                }
            }
            AddRow(block.TableHeader, 1, true);
            for (int r = 0; r < block.TableRows.Count; r++)
                AddRow(block.TableRows[r], 3 + 2 * r, false);

            // The lines: across at every even row, down at every even column.
            for (int r = 0; r < gridRows; r += 2)
            {
                BoxView across = new BoxView { Color = QuoteMarkColor, HeightRequest = line };
                grid.Add(across, 0, r);
                Grid.SetColumnSpan(across, gridColumns);
            }
            for (int c = 0; c < gridColumns; c += 2)
            {
                BoxView down = new BoxView { Color = QuoteMarkColor, WidthRequest = line };
                grid.Add(down, c, 0);
                Grid.SetRowSpan(down, gridRows);
            }
            SemanticProperties.SetDescription(grid, "Table: " + string.Join(", ", block.TableHeader) + "; " + block.TableRows.Count + " rows");
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
