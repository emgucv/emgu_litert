//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// An in-page bottom sheet with a dimmed scrim, showing a view under a title with a closing button; the title and the
    /// button stay put, and what's between them scrolls when it is taller than the screen allows. Put it over the page's
    /// content (the last child of a Grid) and call <see cref="ShowAsync"/>. Its look comes from the styles
    /// "ChatSheetStyle" (the card, a Border), "ChatSheetTitleStyle" (a Label) and "ChatSheetCloseStyle" (the closing Button).
    /// </summary>
    public class BottomSheet : Grid
    {
        // Room the sheet leaves above itself, and what its title and closing button take.
        private const double TopGap = 40;
        private const double HeaderAndFooterHeight = 170;
        private readonly BoxView _scrim;
        private readonly Border _card;
        private readonly Label _title;
        private readonly ContentView _content = new ContentView();
        private readonly ScrollView _scroll;
        private readonly Button _close;
        private TaskCompletionSource<string> _tcs;
        private bool _animating;

        /// <summary>Create the sheet, hidden.</summary>
        public BottomSheet()
        {
            IsVisible = false;
            _card = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(28, 28, 0, 0) },
                Padding = new Thickness(20, 8, 20, 24),
                VerticalOptions = LayoutOptions.End,
                HorizontalOptions = LayoutOptions.Center,
                MaximumWidthRequest = 560,
            };
            _title = new Label { HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(0, 10, 0, 6) };
            _scroll = new ScrollView { Content = _content, VerticalScrollBarVisibility = ScrollBarVisibility.Default };
            _close = new Button { Margin = new Thickness(0, 14, 0, 0) };
            _close.Clicked += (s, e) => Close(null);
            _card.Content = new VerticalStackLayout { Children = { _title, _scroll, _close } };
            _scrim = new BoxView { Color = Color.FromArgb("#66000000"), Opacity = 0 };
            TapGestureRecognizer tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => Close(null);
            _scrim.GestureRecognizers.Add(tap);
            SizeChanged += (s, e) =>
            {
                if (Width > 0)
                    _card.WidthRequest = Math.Min(560, Width);
                if (Height > 0)
                    _scroll.MaximumHeightRequest = Math.Max(120, Height - TopGap - HeaderAndFooterHeight);
            };
            Children.Add(_scrim);
            Children.Add(_card);
        }

        /// <summary>True while the sheet is shown.</summary>
        public bool IsShown => IsVisible;

        /// <inheritdoc/>
        protected override void OnParentSet()
        {
            base.OnParentSet();
            ChatResources.ApplyStyle(_card, "ChatSheetStyle");
            ChatResources.ApplyStyle(_title, "ChatSheetTitleStyle");
            ChatResources.ApplyStyle(_close, "ChatSheetCloseStyle");
        }

        /// <summary>
        /// Show a view under a title, with a closing button. The view can close the sheet with a value by calling
        /// <see cref="Dismiss"/>; closing it otherwise gives null.
        /// </summary>
        /// <param name="title">The title</param>
        /// <param name="content">The view</param>
        /// <param name="closeText">The closing button's text</param>
        /// <returns>The value the sheet was dismissed with, or null</returns>
        public Task<string> ShowAsync(string title, View content, string closeText = "Done")
        {
            if (_tcs != null && !_tcs.Task.IsCompleted)
                _tcs.TrySetResult(null);
            // The view may still be where it was shown last (the sheet, or a side panel).
            if (content?.Parent is Layout parent)
                parent.Children.Remove(content);
            if (content?.Parent is ContentView holder)
                holder.Content = null;
            _title.Text = title;
            _content.Content = content;
            _close.Text = closeText;
            _ = _scroll.ScrollToAsync(0, 0, false);
            _tcs = new TaskCompletionSource<string>();
            IsVisible = true;
            _scrim.Opacity = 0;
            _card.TranslationY = 700;
            _ = Task.WhenAll(
                _scrim.FadeToAsync(1, 220, Easing.CubicOut),
                _card.TranslateToAsync(0, 0, 260, Easing.CubicOut));
            return _tcs.Task;
        }

        /// <summary>Close the sheet; <see cref="ShowAsync"/> completes with the value.</summary>
        /// <param name="value">The value, or null</param>
        public void Dismiss(string value) => Close(value);

        private async void Close(string value)
        {
            if (_animating || !IsVisible)
                return;
            _animating = true;
            await Task.WhenAll(
                _scrim.FadeToAsync(0, 180, Easing.CubicIn),
                _card.TranslateToAsync(0, 700, 220, Easing.CubicIn));
            IsVisible = false;
            // Let go of the content, not just its visibility.
            _content.Content = null;
            _animating = false;
            _tcs?.TrySetResult(value);
        }
    }
}
