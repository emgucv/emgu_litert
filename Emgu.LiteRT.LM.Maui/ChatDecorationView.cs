//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Shows one of a message's <see cref="Emgu.LiteRT.LM.Chat.ChatMessageItem.Decorations"/>: a View as it is, anything
    /// else as text. A View can only be in one place, so when the transcript's views are created again, it moves to the
    /// new one. The message templates use it with <c>BindableLayout</c>; its look for text is the "ChatDecorationTextStyle"
    /// Label style (see <see cref="ChatStyles"/>).
    /// </summary>
    public class ChatDecorationView : ContentView
    {
        /// <summary>The decoration.</summary>
        public static readonly BindableProperty DecorationProperty = BindableProperty.Create(
            nameof(Decoration), typeof(object), typeof(ChatDecorationView), null, propertyChanged: (b, o, n) => ((ChatDecorationView)b).Show(n));

        /// <summary>The decoration.</summary>
        public object Decoration
        {
            get => GetValue(DecorationProperty);
            set => SetValue(DecorationProperty, value);
        }

        private void Show(object decoration)
        {
            if (decoration is View view)
            {
                // Taken from where it was shown before.
                if (view.Parent is ContentView previous && previous != this)
                    previous.Content = null;
                else if (view.Parent is Layout layout)
                    layout.Remove(view);
                Content = view;
            }
            else if (decoration != null)
            {
                Label label = new Label { Text = decoration.ToString(), Style = ChatResources.Find<Style>("ChatDecorationTextStyle") };
                Content = label;
            }
            else
                Content = null;
        }
    }
}
