//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Microsoft.Maui.Controls.Shapes;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A card above the reply that asks before a tool does something outside the chat (a timer, a calendar event, a tool
    /// server's tool...): the request's title and details, its confirm button and Cancel; then, <see cref="Finish"/>, what
    /// came of it. Made for a <see cref="DeviceActionRequest"/> and shown with <see cref="IChatPageHost.AddCardToReply(View)"/>.
    /// </summary>
    public sealed class ConfirmationCard
    {
        private HorizontalStackLayout _buttons;
        private Label _result;

        private ConfirmationCard()
        {
        }

        /// <summary>The card's view.</summary>
        public Border View { get; private set; }

        /// <summary>The buttons gone, the outcome shown ("Running...", "Cancelled", or what was done).</summary>
        /// <param name="text">The outcome</param>
        /// <param name="done">True for a success (a check mark, the success colour)</param>
        public void Finish(string text, bool done)
        {
            _buttons.IsVisible = false;
            _result.Text = (done ? "✓ " : "") + text;
            if (done)
                ChatResources.SetThemeColor(_result, Label.TextColorProperty, "ChatSuccess");
            else
                ChatResources.SetThemeColor(_result, Label.TextColorProperty, "ChatSecondaryText");
            _result.IsVisible = true;
        }

        /// <summary>The icon for a request: the built-in device actions' own, a cog for the rest.</summary>
        /// <param name="request">The request</param>
        public static string DefaultIcon(DeviceActionRequest request) => request switch
        {
            TimerRequest => "⏲️",
            AlarmRequest => "⏰",
            CalendarEventRequest => "\U0001F4C5",
            ReminderRequest => "✅",
            DirectionsRequest => "\U0001F5FA️",
            _ => "⚙️"
        };

        /// <summary>Make the card.</summary>
        /// <param name="request">What the tool wants to do</param>
        /// <param name="choose">Called with true when confirmed, false when cancelled</param>
        /// <param name="icon">The icon before the title, or null for <see cref="DefaultIcon"/></param>
        public static ConfirmationCard Create(DeviceActionRequest request, Action<bool> choose, string icon = null)
        {
            var card = new ConfirmationCard();
            Color accent = ChatResources.FindColor("ChatAccent");
            var content = new VerticalStackLayout { Spacing = 4 };
            var title = new Label { Text = (icon ?? DefaultIcon(request)) + "  " + request.Title };
            ChatResources.ApplyStyle(title, "ChatListTitleStyle");
            content.Children.Add(title);
            foreach (string detail in request.Details)
            {
                var line = new Label { Text = detail };
                ChatResources.ApplyStyle(line, "ChatSectionTextStyle");
                content.Children.Add(line);
            }
            var confirm = new Button
            {
                Text = request.ConfirmText,
                FontSize = 14,
                BackgroundColor = accent,
                TextColor = Colors.White,
                CornerRadius = 12,
                Padding = new Thickness(16, 6),
                MinimumHeightRequest = 40
            };
            var cancel = new Button
            {
                Text = "Cancel",
                FontSize = 14,
                BackgroundColor = Colors.Transparent,
                TextColor = accent,
                BorderColor = accent,
                BorderWidth = 1,
                CornerRadius = 12,
                Padding = new Thickness(16, 6),
                MinimumHeightRequest = 40
            };
            confirm.Clicked += (s, e) => choose(true);
            cancel.Clicked += (s, e) => choose(false);
            card._buttons = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 6, 0, 0), Children = { confirm, cancel } };
            card._result = new Label { IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
            ChatResources.ApplyStyle(card._result, "ChatListDetailStyle");
            content.Children.Add(card._buttons);
            content.Children.Add(card._result);
            card.View = new Border
            {
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Padding = new Thickness(14, 12),
                Margin = new Thickness(6, 2),
                MaximumWidthRequest = 420,
                HorizontalOptions = LayoutOptions.Start,
                Content = content
            };
            ChatResources.SetThemeColor(card.View, VisualElement.BackgroundColorProperty, "ChatSurface");
            ChatResources.SetThemeColor(card.View, Border.StrokeProperty, "ChatRowBorder");
            SemanticProperties.SetDescription(card.View, request.Title + ": " + string.Join(", ", request.Details));
            return card;
        }
    }
}
