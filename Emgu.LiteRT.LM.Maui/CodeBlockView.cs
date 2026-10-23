//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A code block of a reply: the code in a monospace font, its language and a Copy button. <see cref="ReplyView"/>
    /// creates one per fenced code block.
    /// <para>
    /// Its look is the "CodeBlockViewTemplate" template (see <see cref="ChatStyles"/>), whose parts are "PART_Code" (a
    /// Label for the code), "PART_Language" (a Label for the language, "code" if none) and "PART_Copy" (tapped to copy
    /// the code; if it is a Label, its text says "Copied" for a moment). A template without one of them just doesn't
    /// show that part.
    /// </para>
    /// </summary>
    public class CodeBlockView : TemplatedView
    {
        private const string TemplateKey = "CodeBlockViewTemplate";

        /// <summary>The code.</summary>
        public static readonly BindableProperty CodeProperty = BindableProperty.Create(
            nameof(Code), typeof(string), typeof(CodeBlockView), string.Empty, propertyChanged: (b, o, n) => ((CodeBlockView)b).Update());

        /// <summary>The code's language as written after the fence, e.g. "python"; empty if none.</summary>
        public static readonly BindableProperty LanguageProperty = BindableProperty.Create(
            nameof(Language), typeof(string), typeof(CodeBlockView), string.Empty, propertyChanged: (b, o, n) => ((CodeBlockView)b).Update());

        /// <summary>The code's font; null for the template's.</summary>
        public static readonly BindableProperty CodeFontFamilyProperty = BindableProperty.Create(
            nameof(CodeFontFamily), typeof(string), typeof(CodeBlockView), null, propertyChanged: (b, o, n) => ((CodeBlockView)b).Update());

        /// <summary>The font of the language label; null for the template's.</summary>
        public static readonly BindableProperty HeaderFontFamilyProperty = BindableProperty.Create(
            nameof(HeaderFontFamily), typeof(string), typeof(CodeBlockView), null, propertyChanged: (b, o, n) => ((CodeBlockView)b).Update());

        private Label _code;
        private Label _language;
        private View _copy;
        private string _copyText;
        private TapGestureRecognizer _copyTap;

        /// <summary>The code.</summary>
        public string Code
        {
            get => (string)GetValue(CodeProperty);
            set => SetValue(CodeProperty, value);
        }

        /// <summary>The code's language as written after the fence, e.g. "python"; empty if none.</summary>
        public string Language
        {
            get => (string)GetValue(LanguageProperty);
            set => SetValue(LanguageProperty, value);
        }

        /// <summary>The code's font; null for the template's.</summary>
        public string CodeFontFamily
        {
            get => (string)GetValue(CodeFontFamilyProperty);
            set => SetValue(CodeFontFamilyProperty, value);
        }

        /// <summary>The font of the language label; null for the template's.</summary>
        public string HeaderFontFamily
        {
            get => (string)GetValue(HeaderFontFamilyProperty);
            set => SetValue(HeaderFontFamilyProperty, value);
        }

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
            if (_copy != null && _copyTap != null)
                _copy.GestureRecognizers.Remove(_copyTap);
            _code = GetTemplateChild("PART_Code") as Label;
            _language = GetTemplateChild("PART_Language") as Label;
            _copy = GetTemplateChild("PART_Copy") as View;
            if (_copy != null)
            {
                _copyText = (_copy as Label)?.Text;
                _copyTap = new TapGestureRecognizer();
                _copyTap.Tapped += async (s, e) => await CopyAsync();
                _copy.GestureRecognizers.Add(_copyTap);
                SemanticProperties.SetDescription(_copy, "Copy the code");
            }
            Update();
        }

        private void Update()
        {
            if (_code != null)
            {
                _code.Text = Code;
                if (CodeFontFamily != null)
                    _code.FontFamily = CodeFontFamily;
            }
            if (_language != null)
            {
                _language.Text = string.IsNullOrEmpty(Language) ? "code" : Language;
                if (HeaderFontFamily != null)
                    _language.FontFamily = HeaderFontFamily;
            }
        }

        private async Task CopyAsync()
        {
            Label label = _copy as Label;
            try
            {
                await Clipboard.Default.SetTextAsync(Code);
                if (label != null)
                {
                    label.Text = "Copied";
                    await Task.Delay(2000);
                    label.Text = _copyText;
                }
            }
            catch (Exception)
            {
                if (label != null)
                    label.Text = "Copy failed";
            }
        }
    }
}
