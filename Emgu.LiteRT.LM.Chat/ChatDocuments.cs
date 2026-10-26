//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// A document attached to a chat and indexed for its questions: kept so attaching the same file again, or opening a
    /// chat that used it, doesn't index it again. Deleted with the last chat that uses it.
    /// </summary>
    public sealed class ChatDocument
    {
        /// <summary>The SHA-256 of the file, in hex</summary>
        public string Id { get; set; }

        /// <summary>The file's name</summary>
        public string Name { get; set; }

        /// <summary>Its pages (0 when unknown)</summary>
        public int PageCount { get; set; }

        /// <summary>What the index was built with (passage size, embedding model, images...): another value rebuilds it</summary>
        public string Settings { get; set; }

        /// <summary>The chip's description, e.g. "5 pages · 2 pictures"</summary>
        public string Summary { get; set; }

        /// <summary>When it was indexed (UTC)</summary>
        public DateTime Created { get; set; }
    }

    /// <summary>A passage or image of a saved document, with its embedding (the floats' bytes).</summary>
    public sealed class ChatDocumentItem
    {
        /// <summary>The document</summary>
        public string DocumentId { get; set; }

        /// <summary>Its position among the document's passages or images</summary>
        public int Position { get; set; }

        /// <summary>Whether it is an image (else a passage)</summary>
        public bool IsImage { get; set; }

        /// <summary>A passage's text</summary>
        public string Text { get; set; }

        /// <summary>Where it is ("page 3"), or null</summary>
        public string Location { get; set; }

        /// <summary>An image: whether it is a whole page</summary>
        public bool IsPage { get; set; }

        /// <summary>An image's media type</summary>
        public string MediaType { get; set; }

        /// <summary>An image's bytes</summary>
        public byte[] Data { get; set; }

        /// <summary>The embedding, as the floats' bytes; null if none</summary>
        public byte[] Embedding { get; set; }
    }

    /// <summary>A saved passage with its document, for the search over every saved document.</summary>
    public sealed class ChatDocumentPassage
    {
        /// <summary>The document</summary>
        public string DocumentId { get; set; }

        /// <summary>Its name</summary>
        public string DocumentName { get; set; }

        /// <summary>Where the passage is ("page 3"), or null</summary>
        public string Location { get; set; }

        /// <summary>The passage's text</summary>
        public string Text { get; set; }

        /// <summary>Its embedding, as the floats' bytes</summary>
        public byte[] Embedding { get; set; }
    }

    /// <summary>
    /// Where the documents attached to chats keep their indexes, and which document each chat uses (Emgu.LiteRT.LM.Chat.Sqlite's
    /// <c>ChatHistoryStore</c> implements it). A private store returns nothing and saves nothing.
    /// </summary>
    public interface IChatDocumentStore
    {
        /// <summary>A saved document, or null.</summary>
        /// <param name="id">Its id</param>
        Task<ChatDocument> GetDocumentAsync(string id);

        /// <summary>A saved document's passages and images, in order.</summary>
        /// <param name="id">The document</param>
        Task<IReadOnlyList<ChatDocumentItem>> GetDocumentItemsAsync(string id);

        /// <summary>
        /// Every saved passage (not image) with its document, for searching all saved documents. Documents indexed with
        /// another embedding model (the second part of <see cref="ChatDocument.Settings"/>) are left out: their vectors can't be compared.
        /// </summary>
        /// <param name="embeddingFile">The embedding model's file</param>
        Task<IReadOnlyList<ChatDocumentPassage>> GetDocumentPassagesAsync(string embeddingFile);

        /// <summary>Save a document's index, replacing an older one of the same file.</summary>
        /// <param name="document">The document</param>
        /// <param name="items">Its passages and images</param>
        Task SaveDocumentAsync(ChatDocument document, IEnumerable<ChatDocumentItem> items);

        /// <summary>The document a chat uses, or null.</summary>
        /// <param name="chatId">The chat</param>
        Task<string> GetChatDocumentAsync(int chatId);

        /// <summary>Record the document a chat uses (null for none).</summary>
        /// <param name="chatId">The chat</param>
        /// <param name="documentId">The document, or null</param>
        Task SetChatDocumentAsync(int chatId, string documentId);

        /// <summary>Delete the documents no saved chat uses, except one (e.g. the one attached now).</summary>
        /// <param name="keepId">A document to keep, or null</param>
        Task DeleteUnusedDocumentsAsync(string keepId = null);

        /// <summary>Delete a document's index; the chats that used it keep their text.</summary>
        /// <param name="id">The document</param>
        Task DeleteDocumentAsync(string id);
    }

    /// <summary>The search_documents tool group: its name, summary and words, and the instructions that go with it.</summary>
    public static class ChatDocuments
    {
        /// <summary>The tool group's name.</summary>
        public const string GroupName = "documents";

        /// <summary>The tool group's summary, for the planner.</summary>
        public const string GroupSummary = "search the documents (PDF, Word, text files) the user attached to earlier chats";

        /// <summary>The words that bring the tool group.</summary>
        public static readonly string[] GroupKeywords =
        {
            "document", "file", "pdf", "report", "contract", "paper", "manual", "my notes", "attached", "the doc"
        };

        /// <summary>The instructions that go with the search_documents tool.</summary>
        public const string Instructions = "When asked about the user's saved documents or files shared in earlier chats, use search_documents with " +
            "the request, and the document's name if given. Cite the document and page. If nothing is found, say so.";
    }
}
