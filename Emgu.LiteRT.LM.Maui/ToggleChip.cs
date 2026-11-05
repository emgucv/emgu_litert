//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A pill that switches an option on or off (e.g. "Think" above a chat's message box): a Button with the style
    /// "ChatChipStyle" while off and "ChatChipOnStyle" while on (see <see cref="ChatStyles"/>), unless given styles of its own.
    /// </summary>
    public class ToggleChip : Button
    {
        private bool _isToggled;

        /// <summary>Create a chip.</summary>
        /// <param name="text">Its text</param>
        public ToggleChip(string text)
        {
            Text = text;
            Clicked += (s, e) => IsToggled = !IsToggled;
            Update();
        }

        /// <summary>The style while off, or null for "ChatChipStyle".</summary>
        public Style OffStyle { get; set; }

        /// <summary>The style while on, or null for "ChatChipOnStyle".</summary>
        public Style OnStyle { get; set; }

        /// <summary>Raised when the chip is switched.</summary>
        public event EventHandler<ToggledEventArgs> Toggled;

        /// <summary>Whether the option is on.</summary>
        public bool IsToggled
        {
            get => _isToggled;
            set
            {
                if (_isToggled == value)
                    return;
                _isToggled = value;
                Update();
                Toggled?.Invoke(this, new ToggledEventArgs(value));
            }
        }

        /// <inheritdoc/>
        protected override void OnParentSet()
        {
            base.OnParentSet();
            Update();
        }

        // A FlexLayout row measures its chips at any width, so a chip wider than the row was measured as one line and its
        // second line was cut off (Android at the largest text sizes): it is measured at most as wide as its row.
        /// <inheritdoc/>
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            if (Parent is Layout row && row.Width > 0)
                widthConstraint = Math.Min(widthConstraint, Math.Max(0, row.Width - row.Padding.HorizontalThickness - Margin.HorizontalThickness));
            return base.MeasureOverride(widthConstraint, heightConstraint);
        }

        private void Update()
        {
            Style = _isToggled ? OnStyle ?? ChatResources.Find<Style>("ChatChipOnStyle") : OffStyle ?? ChatResources.Find<Style>("ChatChipStyle");
            SemanticProperties.SetDescription(this, Text + (_isToggled ? ": on" : ": off"));
        }
    }
}
