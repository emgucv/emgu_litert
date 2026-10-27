//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>A saved document's size in the store, for <see cref="StorageCatalog"/>.</summary>
    public sealed class ChatDocumentStorage
    {
        /// <summary>The document's id.</summary>
        public string Id { get; set; }

        /// <summary>Its name.</summary>
        public string Name { get; set; }

        /// <summary>What its text, images and embeddings take, roughly.</summary>
        public long Bytes { get; set; }
    }

    /// <summary>
    /// What a <see cref="StorageCatalog"/> needs of the chats' store: the sizes of what it keeps, and deleting them.
    /// Emgu.LiteRT.LM.Chat.Sqlite's <c>ChatHistoryStore</c> implements it.
    /// </summary>
    public interface IChatStorageStore
    {
        /// <summary>The store's folder (its database file is chats.db3 in it).</summary>
        string Folder { get; }

        /// <summary>The folder the chats' attachments are saved in.</summary>
        string AttachmentFolder { get; }

        /// <summary>Each saved document with its size.</summary>
        Task<List<ChatDocumentStorage>> GetDocumentStorageAsync();

        /// <summary>What the photo search's index takes, roughly.</summary>
        Task<long> GetPhotoIndexBytesAsync();

        /// <summary>What the past chats' search index takes, roughly.</summary>
        Task<long> GetChatIndexBytesAsync();

        /// <summary>Delete a document and detach it from the chats that had it (their text and saved excerpts stay).</summary>
        /// <param name="id">The document's id</param>
        Task DeleteDocumentAsync(string id);

        /// <summary>Delete the photo search's index.</summary>
        Task ClearPhotoEmbeddingsAsync();

        /// <summary>Delete the past chats' search index.</summary>
        Task ClearChatEmbeddingsAsync();

        /// <summary>Delete every chat's attachment files (the messages keep their text).</summary>
        Task DeleteAllAttachmentsAsync();

        /// <summary>Give the space deleted rows took back to the device.</summary>
        Task CompactAsync();
    }
}
