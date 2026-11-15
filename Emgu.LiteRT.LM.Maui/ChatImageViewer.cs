//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A picture shown full screen on a page of its own: pinch to zoom, drag to move it when zoomed, double-tap to zoom in
    /// and out, and the buttons "+", "-" and "Fit" for a mouse. <see cref="ChatAttachmentView"/> opens it when its picture is
    /// tapped, so an attached photo (a worksheet, say) can be read at full size.
    /// </summary>
    public sealed class ChatImageViewer : ContentPage
    {
        private const double MaxScale = 8;
        private const double ButtonStep = 1.5;
        private const double DoubleTapScale = 2.5;

        private readonly Image _image;
        private readonly Grid _stage;
        private double _scale = 1, _startScale = 1;
        private double _x, _y, _startX, _startY;

        /// <summary>Create the viewer for a picture.</summary>
        /// <param name="data">The picture (PNG, JPEG...)</param>
        public ChatImageViewer(byte[] data)
        {
            BackgroundColor = Colors.Black;
            Title = "Picture";
            _image = new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(data)),
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };
            SemanticProperties.SetDescription(_image, "The picture, pinch to zoom");
            _stage = new Grid { BackgroundColor = Colors.Black, IsClippedToBounds = true, Children = { _image } };

            var pinch = new PinchGestureRecognizer();
            pinch.PinchUpdated += OnPinch;
            _stage.GestureRecognizers.Add(pinch);
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += OnPan;
            _stage.GestureRecognizers.Add(pan);
            var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleTap.Tapped += (s, e) => SetScale(_scale > 1.01 ? 1 : DoubleTapScale);
            _stage.GestureRecognizers.Add(doubleTap);
            _stage.SizeChanged += (s, e) => Clamp();

            Button close = Pill("Close", async () => await CloseAsync());
            close.HorizontalOptions = LayoutOptions.End;
            close.VerticalOptions = LayoutOptions.Start;
            close.Margin = new Thickness(0, 12, 12, 0);
            var zoomOut = Pill("-", () => SetScale(_scale / ButtonStep));
            var fit = Pill("Fit", () => SetScale(1));
            var zoomIn = Pill("+", () => SetScale(_scale * ButtonStep));
            var bar = new HorizontalStackLayout
            {
                Spacing = 10,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, 0, 20),
                Children = { zoomOut, fit, zoomIn }
            };
            Content = new Grid { Children = { _stage, close, bar } };
        }

        private static Button Pill(string text, Action action)
        {
            var button = new Button
            {
                Text = text,
                TextColor = Colors.White,
                BackgroundColor = Color.FromRgba(255, 255, 255, 60),
                CornerRadius = 18,
                HeightRequest = 40,
                MinimumWidthRequest = 56,
                Padding = new Thickness(14, 0)
            };
            SemanticProperties.SetDescription(button, text == "-" ? "Zoom out" : text == "+" ? "Zoom in" : text);
            button.Clicked += (s, e) => action();
            return button;
        }

        private async Task CloseAsync()
        {
            if (Navigation.ModalStack.Contains(this))
                await Navigation.PopModalAsync();
        }

        private void OnPinch(object sender, PinchGestureUpdatedEventArgs e)
        {
            switch (e.Status)
            {
                case GestureStatus.Started:
                    _startScale = _scale;
                    break;
                case GestureStatus.Running:
                    _scale = Math.Clamp(_startScale * e.Scale, 1, MaxScale);
                    Clamp();
                    break;
            }
        }

        private void OnPan(object sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _startX = _x;
                    _startY = _y;
                    break;
                case GestureStatus.Running:
                    _x = _startX + e.TotalX;
                    _y = _startY + e.TotalY;
                    Clamp();
                    break;
            }
        }

        private void SetScale(double scale)
        {
            _scale = Math.Clamp(scale, 1, MaxScale);
            Clamp();
        }

        // The zoomed picture can move only as far as its edges reach the page's edges; at 1x it stays centered.
        private void Clamp()
        {
            double maxX = Math.Max(0, (_scale - 1) * _stage.Width / 2);
            double maxY = Math.Max(0, (_scale - 1) * _stage.Height / 2);
            _x = Math.Clamp(_x, -maxX, maxX);
            _y = Math.Clamp(_y, -maxY, maxY);
            _image.Scale = _scale;
            _image.TranslationX = _x;
            _image.TranslationY = _y;
        }

        /// <summary>Show a picture full screen on a page of its own (nothing happens without a window).</summary>
        /// <param name="from">A view of the page that is showing, to find the window</param>
        /// <param name="data">The picture (PNG, JPEG...)</param>
        public static async Task ShowAsync(VisualElement from, byte[] data)
        {
            INavigation navigation = (from?.Window?.Page ?? Application.Current?.Windows.FirstOrDefault()?.Page)?.Navigation;
            if (navigation == null || data == null || data.Length == 0)
                return;
            await navigation.PushModalAsync(new ChatImageViewer(data));
        }
    }
}
