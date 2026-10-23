//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Emgu.LiteRT.LM.Chat.Sqlite
{
    /// <summary>
    /// A saved chat, read back by <see cref="ChatHistoryStore.LoadAsync(int)"/>: what <see cref="ChatConversation.Load"/>
    /// takes (<see cref="Load"/> passes it), and each message's saved row and files for an app that saved more.
    /// </summary>
    public sealed class ChatHistory
    {
        private readonly Dictionary<ChatMessageItem, ChatMessageRecord> _records;
        private readonly Dictionary<ChatMessageItem, IReadOnlyList<ChatSavedAttachment>> _attachments;

        internal ChatHistory(ChatRecord chat, List<ChatMessageItem> messages, string summary, int contextStart,
            Dictionary<ChatMessageItem, ChatMessageRecord> records, Dictionary<ChatMessageItem, IReadOnlyList<ChatSavedAttachment>> attachments)
        {
            Chat = chat;
            Messages = messages;
            Summary = summary;
            ContextStart = contextStart;
            _records = records;
            _attachments = attachments;
        }

        /// <summary>The chat.</summary>
        public ChatRecord Chat { get; }

        /// <summary>
        /// The user's messages (with their images, audio, context and transcript) and the replies (with their thinking and
        /// step lines), in order.
        /// </summary>
        public IReadOnlyList<ChatMessageItem> Messages { get; }

        /// <summary>The summary of the messages before <see cref="ContextStart"/>, or null (none, or they were dropped).</summary>
        public string Summary { get; }

        /// <summary>The first message the model still sees (0 if the chat was never compacted).</summary>
        public int ContextStart { get; }

        /// <summary>
        /// The saved row of a message - of the type <see cref="ChatHistoryStore.LoadAsync{TMessage}(int)"/> read, for an
        /// app's own columns; null for a message that isn't in this history.
        /// </summary>
        /// <param name="message">One of <see cref="Messages"/></param>
        public ChatMessageRecord RecordOf(ChatMessageItem message)
        {
            return message != null && _records.TryGetValue(message, out ChatMessageRecord record) ? record : null;
        }

        /// <summary>
        /// The files saved with a message, of every kind: a user message's images and audio (also in its
        /// <see cref="ChatMessageItem.Attachments"/>), and an app's own kinds - e.g. pictures a reply drew, which a reply
        /// item doesn't hold.
        /// </summary>
        /// <param name="message">One of <see cref="Messages"/></param>
        public IReadOnlyList<ChatSavedAttachment> AttachmentsOf(ChatMessageItem message)
        {
            return message != null && _attachments.TryGetValue(message, out IReadOnlyList<ChatSavedAttachment> files)
                ? files : Array.Empty<ChatSavedAttachment>();
        }

        /// <summary>
        /// Show the chat in a conversation and continue it: <see cref="ChatConversation.Load"/> with the messages, the
        /// summary and the context start.
        /// </summary>
        /// <param name="conversation">The conversation; not sending a message</param>
        public void Load(ChatConversation conversation)
        {
            if (conversation == null)
                throw new ArgumentNullException(nameof(conversation));
            conversation.Load(Messages, Summary, ContextStart);
        }
    }

    /// <summary>The ids of a saved exchange's message and reply.</summary>
    public sealed class ChatSavedExchange
    {
        internal ChatSavedExchange(int messageId, int replyId)
        {
            MessageId = messageId;
            ReplyId = replyId;
        }

        /// <summary>The user message's id.</summary>
        public int MessageId { get; }

        /// <summary>The reply's id.</summary>
        public int ReplyId { get; }
    }
}
