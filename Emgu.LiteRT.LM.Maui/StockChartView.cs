//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using Emgu.LiteRT.LM.Agents;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A card with a stock's prices plotted (<see cref="StockChart"/>, from the stock tools): the name, the last price and
    /// its change, and a line - green when the price is up from the previous close (or the period's start), red when down -
    /// with the previous close as a dashed line, the times or dates below and the high and low beside it.
    /// </summary>
    public static class StockChartView
    {
        /// <summary>The card for a chart.</summary>
        /// <param name="chart">The prices</param>
        public static View Create(StockChart chart)
        {
            if (chart == null)
                throw new ArgumentNullException(nameof(chart));
            double last = chart.Points[chart.Points.Count - 1].Price;
            double change = chart.Change ?? 0;
            double start = chart.PreviousClose ?? chart.Points[0].Price;
            double percent = start != 0 ? change / start * 100 : 0;
            var title = new Label { Text = (chart.Name ?? chart.Symbol) + (chart.Name != null && chart.Symbol != null ? " · " + chart.Symbol : string.Empty) };
            ChatResources.ApplyStyle(title, "ChatSectionTextStyle");
            var price = new Label
            {
                Text = last.ToString("0.00", CultureInfo.InvariantCulture) + (chart.Currency != null ? " " + chart.Currency : string.Empty) + "   "
                    + (change >= 0 ? "+" : "") + change.ToString("0.00", CultureInfo.InvariantCulture)
                    + " (" + (percent >= 0 ? "+" : "") + percent.ToString("0.00", CultureInfo.InvariantCulture) + "%)",
                FontSize = 14
            };
            // One colour for both themes (no Light/Dark pair: SetThemeColor would find none and leave the text transparent).
            price.TextColor = ChatResources.FindColor(change >= 0 ? "ChatSuccess" : "ChatDanger");
            var period = ChatPage.Note(chart.Intraday ? chart.Period + ", every 5 minutes" : chart.Period);
            var plot = new GraphicsView { Drawable = new Drawable(chart), HeightRequest = 170, HorizontalOptions = LayoutOptions.Fill };
            var card = new Border
            {
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Padding = new Thickness(14, 12),
                Margin = new Thickness(6, 2),
                WidthRequest = 360,
                MaximumWidthRequest = 420,
                HorizontalOptions = LayoutOptions.Start,
                Content = new VerticalStackLayout { Spacing = 2, Children = { title, price, period, plot } }
            };
            ChatResources.SetThemeColor(card, VisualElement.BackgroundColorProperty, "ChatSurface");
            ChatResources.SetThemeColor(card, Border.StrokeProperty, "ChatRowBorder");
            SemanticProperties.SetDescription(card, title.Text + ", " + period.Text + ": " + price.Text);
            return card;
        }

        private sealed class Drawable : IDrawable
        {
            private readonly StockChart _chart;

            public Drawable(StockChart chart) => _chart = chart;

            public void Draw(ICanvas canvas, RectF rect)
            {
                var points = _chart.Points;
                if (points.Count == 0)
                    return;
                double min = points.Min(p => p.Price), max = points.Max(p => p.Price);
                if (_chart.PreviousClose.HasValue)
                {
                    min = Math.Min(min, _chart.PreviousClose.Value);
                    max = Math.Max(max, _chart.PreviousClose.Value);
                }
                if (max - min < 1e-9)
                {
                    max += 1;
                    min -= 1;
                }
                // Room for the price labels on the right and the time labels below.
                const float right = 52, bottom = 18, top = 6;
                float width = rect.Width - right, height = rect.Height - bottom - top;
                float X(int i) => rect.X + (points.Count == 1 ? width / 2 : i * width / (points.Count - 1));
                float Y(double price) => rect.Y + top + (float)((max - price) / (max - min) * height);

                Color secondary = ChatResources.ThemeColor("ChatSecondaryText");
                Color border = ChatResources.ThemeColor("ChatRowBorder");
                bool up = (_chart.Change ?? 0) >= 0;
                Color line = ChatResources.FindColor(up ? "ChatSuccess" : "ChatDanger");

                // The frame's base line, and the previous close dashed.
                canvas.StrokeColor = border;
                canvas.StrokeSize = 1;
                canvas.DrawLine(rect.X, rect.Y + top + height, rect.X + width, rect.Y + top + height);
                if (_chart.PreviousClose.HasValue)
                {
                    canvas.StrokeDashPattern = new float[] { 4, 4 };
                    canvas.StrokeColor = secondary;
                    float y = Y(_chart.PreviousClose.Value);
                    canvas.DrawLine(rect.X, y, rect.X + width, y);
                    canvas.StrokeDashPattern = null;
                }

                // The prices.
                var path = new PathF();
                for (int i = 0; i < points.Count; i++)
                {
                    if (i == 0)
                        path.MoveTo(X(i), Y(points[i].Price));
                    else
                        path.LineTo(X(i), Y(points[i].Price));
                }
                canvas.StrokeColor = line;
                canvas.StrokeSize = 2;
                canvas.StrokeLineJoin = LineJoin.Round;
                canvas.DrawPath(path);

                // The high and low on the right, the first, middle and last times below.
                canvas.FontColor = secondary;
                canvas.FontSize = 11;
                canvas.DrawString(max.ToString("0.00", CultureInfo.InvariantCulture), rect.X + width + 4, rect.Y + top - 6, right - 4, 14, HorizontalAlignment.Left, VerticalAlignment.Top);
                canvas.DrawString(min.ToString("0.00", CultureInfo.InvariantCulture), rect.X + width + 4, rect.Y + top + height - 14, right - 4, 14, HorizontalAlignment.Left, VerticalAlignment.Bottom);
                bool longPeriod = !_chart.Intraday && SpansYears(points[0].Time, points[points.Count - 1].Time);
                float labelY = rect.Y + top + height + 3;
                canvas.DrawString(_chart.Label(points[0].Time, longPeriod), rect.X, labelY, 70, 14, HorizontalAlignment.Left, VerticalAlignment.Top);
                if (points.Count > 2)
                    canvas.DrawString(_chart.Label(points[points.Count / 2].Time, longPeriod), X(points.Count / 2) - 35, labelY, 70, 14, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.DrawString(_chart.Label(points[points.Count - 1].Time, longPeriod), rect.X + width - 70, labelY, 70, 14, HorizontalAlignment.Right, VerticalAlignment.Top);
            }

            private static bool SpansYears(string first, string last) =>
                DateTime.TryParse(first, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime a)
                && DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime b)
                && (b - a).TotalDays > 400;
        }
    }
}
