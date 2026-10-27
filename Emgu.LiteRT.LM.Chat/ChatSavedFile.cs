//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// A file an app keeps with a reply (<see cref="ChatMessageItem.SavedFiles"/>): a picture the reply drew, a document
    /// it wrote. Saved with the reply by the chats' store (as an attachment of the app's own kind, never given to the
    /// model) and read back when the chat is opened, for the app to show again.
    /// </summary>
    public sealed class ChatSavedFile
    {
        /// <summary>Describe the file.</summary>
        /// <param name="kind">The app's kind, e.g. "generated" or "written_document" (not "image" or "audio", the user's attachments)</param>
        /// <param name="mediaType">Its media type, with any parameter the app needs back ("application/pdf; name=Notes.pdf")</param>
        /// <param name="data">The bytes</param>
        public ChatSavedFile(string kind, string mediaType, byte[] data)
        {
            if (string.IsNullOrEmpty(kind))
                throw new ArgumentException("A kind is needed", nameof(kind));
            Kind = kind;
            MediaType = mediaType ?? "application/octet-stream";
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>The app's kind.</summary>
        public string Kind { get; }

        /// <summary>Its media type, with the app's parameters.</summary>
        public string MediaType { get; }

        /// <summary>The bytes.</summary>
        public byte[] Data { get; }
    }
}
