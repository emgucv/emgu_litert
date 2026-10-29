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
                if (view is SelectableLabel)
                    handler.PlatformView.IsTextSelectionEnabled = true;
#endif
            });
        }
    }
}
