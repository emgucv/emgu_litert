//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Picks the template of a <see cref="ChatMessageItem"/> in a <see cref="ChatView"/>'s transcript: a user message, a
    /// reply or a notice. Unless they are set, the templates are the resources "ChatUserMessageTemplate",
    /// "ChatReplyTemplate" and "ChatNoticeTemplate" (the app's, else those of <see cref="ChatStyles"/>), so an app replaces
    /// one by defining a resource with its key - or sets <see cref="ChatView.MessageTemplateSelector"/> to a selector of
    /// its own.
    /// </summary>
    public class ChatMessageTemplateSelector : DataTemplateSelector
    {
        /// <summary>The template of the user's messages; null for the "ChatUserMessageTemplate" resource.</summary>
        public DataTemplate UserTemplate { get; set; }

        /// <summary>The template of the replies; null for the "ChatReplyTemplate" resource.</summary>
        public DataTemplate ReplyTemplate { get; set; }

        /// <summary>The template of the app's notices; null for the "ChatNoticeTemplate" resource.</summary>
        public DataTemplate NoticeTemplate { get; set; }

        /// <inheritdoc/>
        protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        {
            ChatMessageItem message = item as ChatMessageItem;
            if (message != null && message.Role == ChatMessageRole.User)
                return UserTemplate ??= ChatResources.Find<DataTemplate>("ChatUserMessageTemplate");
            if (message != null && message.Role == ChatMessageRole.Notice)
                return NoticeTemplate ??= ChatResources.Find<DataTemplate>("ChatNoticeTemplate");
            return ReplyTemplate ??= ChatResources.Find<DataTemplate>("ChatReplyTemplate");
        }
    }
}
