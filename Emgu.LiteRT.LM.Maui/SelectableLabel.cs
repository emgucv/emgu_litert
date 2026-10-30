//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A Label whose text the user can select and copy, as in a web page: on Windows the text block's own selection
    /// (drag with the mouse, double-click a word, Ctrl+C or the right-click menu), set by
    /// <see cref="MauiAppBuilderExtensions.UseLiteRtChat"/>'s handler mapping. The chat's message and reply text use it.
    /// Elsewhere it is an ordinary Label (the Copy buttons under the messages copy a whole message).
    /// </summary>
    public class SelectableLabel : Label
    {
        internal static void MapSelection()
        {
            Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping(nameof(SelectableLabel), (handler, view) =>
            {
#if WINDOWS
                if (view is SelectableLabel label)
                {
                    handler.PlatformView.IsTextSelectionEnabled = true;
                    ActivateLinks(handler.PlatformView, label);
                }
#endif
            });
        }

#if WINDOWS
        // Selectable text swallows the tap MAUI turns into a link span's tap, so a click on a reply's link span (ReplyLinkSpan) is
        // found here: the pointer went down and up in about the same place (a drag is a selection) over the span's text. MAUI
        // expects every inline to be a Run (replacing them with Hyperlinks made its layout throw), so the runs stay as they are.
        private static void ActivateLinks(Microsoft.UI.Xaml.Controls.TextBlock block, Label label)
        {
            Windows.Foundation.Point? pressed = null;
            block.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
                new Microsoft.UI.Xaml.Input.PointerEventHandler((sender, args) => pressed = args.GetCurrentPoint(block).Position), true);
            block.AddHandler(Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((sender, args) =>
            {
                Windows.Foundation.Point? start = pressed;
                pressed = null;
                Microsoft.UI.Input.PointerPoint point = args.GetCurrentPoint(block);
                if (start == null || Math.Abs(start.Value.X - point.Position.X) > 4 || Math.Abs(start.Value.Y - point.Position.Y) > 4)
                    return;
                if (args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse
                    && point.Properties.PointerUpdateKind != Microsoft.UI.Input.PointerUpdateKind.LeftButtonReleased)
                    return;
                if (!string.IsNullOrEmpty(block.SelectedText))
                    return;
                LinkAt(block, label, point.Position)?.Activate?.Invoke();
            }), true);
        }

        private static ReplyLinkSpan LinkAt(Microsoft.UI.Xaml.Controls.TextBlock block, Label label, Windows.Foundation.Point point)
        {
            FormattedString formatted = label.FormattedText;
            // The label's spans are its inlines, one each, in order.
            if (formatted == null || block.Inlines.Count != formatted.Spans.Count)
                return null;
            for (int i = 0; i < formatted.Spans.Count; i++)
                if (formatted.Spans[i] is ReplyLinkSpan span && block.Inlines[i] is Microsoft.UI.Xaml.Documents.Run run && Covers(run, point))
                    return span;
            return null;
        }

        // Whether a run's text, which may wrap over several lines, is under a point.
        private static bool Covers(Microsoft.UI.Xaml.Documents.Run run, Windows.Foundation.Point point)
        {
            Windows.Foundation.Rect first = run.ContentStart.GetCharacterRect(Microsoft.UI.Xaml.Documents.LogicalDirection.Forward);
            Windows.Foundation.Rect last = run.ContentEnd.GetCharacterRect(Microsoft.UI.Xaml.Documents.LogicalDirection.Backward);
            bool sameLine = Math.Abs(first.Y - last.Y) < first.Height / 2;
            if (sameLine)
                return point.X >= first.X && point.X <= last.X + last.Width && point.Y >= first.Y && point.Y <= first.Y + first.Height;
            if (point.Y >= first.Y && point.Y <= first.Y + first.Height)
                return point.X >= first.X;
            if (point.Y >= last.Y && point.Y <= last.Y + last.Height)
                return point.X <= last.X + last.Width;
            return point.Y > first.Y + first.Height && point.Y < last.Y;
        }
#endif
    }
}
