// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Chat;

namespace Maui.Demo.Lite;

// The page's own data for a reply, in its ChatMessageItem.Tag: its step lines of the page's own (the document's, the
// memories') and the page's handle for the cards that features put above the reply (AddCard). The thinking's
// translation is the item's own (the library's TranslationFeature).
internal sealed class StreamingBubble
{
    public StreamingBubble(ChatMessageItem item)
    {
        Item = item;
        item.Tag = this;
    }

    public ChatMessageItem Item { get; }

    // The document's step line ("Looked in report.pdf..."), a card above the reply.
    public string DocumentStep;
    // The memories the reply drew on (LiteRtLmChatPage.ShowMemoriesUsedAsync), saved as a step line.
    public string MemoryStep;
}
