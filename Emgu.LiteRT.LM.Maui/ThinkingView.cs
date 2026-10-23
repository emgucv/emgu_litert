//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Shows a reply's thinking in little room. While the model thinks (<see cref="IsThinking"/>): a "Thinking..." header
    /// with a spinner and the last few lines of the thinking. Afterwards: a "Thought for N s" header (or "Thoughts" when
    /// the time isn't known) that shows or hides the whole thinking, formatted like a reply, when tapped. It hides itself
    /// while there is no thinking.
    /// <para>
    /// Its look is the "ThinkingViewTemplate" template (see <see cref="ChatStyles"/>), with the parts "PART_Header" (tapped
    /// to show or hide the thinking), "PART_Spinner" (an ActivityIndicator), "PART_Title" (a Label), "PART_Chevron" (turned
    /// a quarter turn while the thinking is shown), "PART_Accessory" (a ContentView showing <see cref="Accessory"/>),
    /// "PART_Preview" (a Label) and "PART_Body" (a <see cref="ReplyView"/>). A template without one of them just doesn't
    /// show that part.
    /// </para>
    /// </summary>
    public class ThinkingView : TemplatedView
    {
        private const string TemplateKey = "ThinkingViewTemplate";

        /// <summary>The thinking, as streamed so far.</summary>
        public static readonly BindableProperty ThinkingProperty = BindableProperty.Create(
            nameof(Thinking), typeof(string), typeof(ThinkingView), string.Empty, propertyChanged: Changed);

        /// <summary>Text to show instead of the thinking when it is expanded (e.g. a translation); null for the thinking.</summary>
        public static readonly BindableProperty DisplayTextProperty = BindableProperty.Create(
            nameof(DisplayText), typeof(string), typeof(ThinkingView), null, propertyChanged: Changed);

        /// <summary>True while the model is still thinking (no answer yet).</summary>
        public static readonly BindableProperty IsThinkingProperty = BindableProperty.Create(
            nameof(IsThinking), typeof(bool), typeof(ThinkingView), false, propertyChanged: Changed);

        /// <summary>How long the model thought; null if unknown (e.g. a reopened chat).</summary>
        public static readonly BindableProperty DurationProperty = BindableProperty.Create(
            nameof(Duration), typeof(TimeSpan?), typeof(ThinkingView), null, propertyChanged: Changed);

        /// <summary>True while the whole thinking is shown; tapping the header toggles it.</summary>
        public static readonly BindableProperty IsExpandedProperty = BindableProperty.Create(
            nameof(IsExpanded), typeof(bool), typeof(ThinkingView), false, BindingMode.TwoWay, propertyChanged: Changed);

        /// <summary>True to show the whole thinking whatever <see cref="IsExpanded"/> says, e.g. for a reply that ended
        /// without an answer.</summary>
        public static readonly BindableProperty AlwaysExpandedProperty = BindableProperty.Create(
            nameof(AlwaysExpanded), typeof(bool), typeof(ThinkingView), false, propertyChanged: Changed);

        /// <summary>The colour of the thinking text; null for the template's.</summary>
        public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
            nameof(TextColor), typeof(Color), typeof(ThinkingView), null, propertyChanged: Changed);

        /// <summary>The font of the thinking text; null for the template's.</summary>
        public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
            nameof(FontFamily), typeof(string), typeof(ThinkingView), null, propertyChanged: Changed);

        /// <summary>A view shown under the header while the thinking is shown, e.g. a "Translate" link; null for none.</summary>
        public static readonly BindableProperty AccessoryProperty = BindableProperty.Create(
            nameof(Accessory), typeof(View), typeof(ThinkingView), null, propertyChanged: Changed);

        private View _header;
        private ActivityIndicator _spinner;
        private Label _title;
        private View _chevron;
        private ContentView _accessory;
        private Label _preview;
        private ReplyView _body;
        private TapGestureRecognizer _headerTap;

        /// <summary>The thinking, as streamed so far.</summary>
        public string Thinking
        {
            get => (string)GetValue(ThinkingProperty);
            set => SetValue(ThinkingProperty, value);
        }

        /// <summary>Text to show instead of the thinking when it is expanded (e.g. a translation); null for the thinking.</summary>
        public string DisplayText
        {
            get => (string)GetValue(DisplayTextProperty);
            set => SetValue(DisplayTextProperty, value);
        }

        /// <summary>True while the model is still thinking (no answer yet).</summary>
        public bool IsThinking
        {
            get => (bool)GetValue(IsThinkingProperty);
            set => SetValue(IsThinkingProperty, value);
        }

        /// <summary>How long the model thought; null if unknown (e.g. a reopened chat).</summary>
        public TimeSpan? Duration
        {
            get => (TimeSpan?)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        /// <summary>True while the whole thinking is shown; tapping the header toggles it.</summary>
        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        /// <summary>True to show the whole thinking whatever <see cref="IsExpanded"/> says, e.g. for a reply that ended
        /// without an answer.</summary>
        public bool AlwaysExpanded
        {
            get => (bool)GetValue(AlwaysExpandedProperty);
            set => SetValue(AlwaysExpandedProperty, value);
        }

        /// <summary>The colour of the thinking text; null for the template's.</summary>
        public Color TextColor
        {
            get => (Color)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        /// <summary>The font of the thinking text; null for the template's.</summary>
        public string FontFamily
        {
            get => (string)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        /// <summary>A view shown under the header while the thinking is shown, e.g. a "Translate" link; null for none.</summary>
        public View Accessory
        {
            get => (View)GetValue(AccessoryProperty);
            set => SetValue(AccessoryProperty, value);
        }

        /// <summary>
        /// True if the whole thinking is shown now: there is thinking, the model has finished it, and it is expanded.
        /// </summary>
        public bool ShowsThinking
        {
            get { return !string.IsNullOrEmpty(Thinking) && !IsThinking && (IsExpanded || AlwaysExpanded); }
        }

        /// <summary>True if the thinking is shown and contains a code block (a chat may then use the full width).</summary>
        public bool HasCode
        {
            get { return ShowsThinking && _body != null && _body.HasCode; }
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
            if (_header != null && _headerTap != null)
                _header.GestureRecognizers.Remove(_headerTap);
            _header = GetTemplateChild("PART_Header") as View;
            _spinner = GetTemplateChild("PART_Spinner") as ActivityIndicator;
            _title = GetTemplateChild("PART_Title") as Label;
            _chevron = GetTemplateChild("PART_Chevron") as View;
            _accessory = GetTemplateChild("PART_Accessory") as ContentView;
            _preview = GetTemplateChild("PART_Preview") as Label;
            _body = GetTemplateChild("PART_Body") as ReplyView;
            if (_header != null)
            {
                _headerTap = new TapGestureRecognizer();
                _headerTap.Tapped += (s, e) =>
                {
                    if (!IsThinking)
                        IsExpanded = !IsExpanded;
                };
                _header.GestureRecognizers.Add(_headerTap);
            }
            Update();
        }

        private static void Changed(BindableObject bindable, object oldValue, object newValue)
        {
            ((ThinkingView)bindable).Update();
        }

        private void Update()
        {
            string thinking = Thinking;
            bool hasThinking = !string.IsNullOrEmpty(thinking);
            IsVisible = hasThinking;
            if (!hasThinking)
                return;
            bool expanded = ShowsThinking;
            if (_spinner != null)
                _spinner.IsVisible = _spinner.IsRunning = IsThinking;
            if (_chevron != null)
            {
                _chevron.IsVisible = !IsThinking;
                _chevron.Rotation = expanded ? 90 : 0;
            }
            string title = IsThinking ? "Thinking..."
                : Duration != null ? string.Format("Thought for {0:N0} s", Math.Max(1, Duration.Value.TotalSeconds))
                : "Thoughts";
            if (_title != null)
                _title.Text = title;
            if (_header != null)
                SemanticProperties.SetDescription(_header, IsThinking ? title
                    : title + (expanded ? ", shown. Tap to hide." : ", hidden. Tap to show."));
            if (_accessory != null)
            {
                _accessory.Content = Accessory;
                _accessory.IsVisible = Accessory != null && expanded;
            }
            if (_preview != null)
            {
                _preview.IsVisible = IsThinking;
                if (IsThinking)
                {
                    _preview.Text = ReplyMarkdown.Preview(thinking);
                    if (TextColor != null)
                        _preview.TextColor = TextColor;
                    if (FontFamily != null)
                        _preview.FontFamily = FontFamily;
                }
            }
            if (_body != null)
            {
                _body.IsVisible = expanded;
                if (TextColor != null)
                    _body.TextColor = TextColor;
                if (FontFamily != null)
                    _body.FontFamily = FontFamily;
                if (expanded)
                    _body.Text = (DisplayText ?? thinking).Trim();
            }
        }
    }
}
