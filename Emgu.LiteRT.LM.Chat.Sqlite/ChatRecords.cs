//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using SQLite;

namespace Emgu.LiteRT.LM.Chat.Sqlite
{
    /// <summary>
    /// A saved chat (the "Conversation" table). An app that keeps more about a chat derives from it, with
    /// <c>[Table("Conversation")]</c> and its own properties: sqlite-net adds them as columns, and the store's own
    /// reads and writes leave them alone.
    /// </summary>
    [Table("Conversation")]
    public class ChatRecord
    {
        /// <summary>The chat's id.</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The first message, shortened.</summary>
        public string Title { get; set; }

        /// <summary>The model it was held with, in the app's own words (e.g. "Gemma4E2B"); or null.</summary>
        public string Model { get; set; }

        /// <summary>When it was started (UTC).</summary>
        public DateTime Created { get; set; }

        /// <summary>When a message was last added (UTC).</summary>
        public DateTime Updated { get; set; }

        /// <summary>
        /// When the chat outgrew the model's context window: the summary of the messages up to <see cref="SummaryUpTo"/>,
        /// given to the model in their place; null if they were dropped without one, or the chat was never compacted.
        /// </summary>
        public string Summary { get; set; }

        /// <summary>The id of the last message the model no longer sees (summarized or dropped); 0 for none.</summary>
        public int SummaryUpTo { get; set; }

        /// <summary>The document attached to the chat (<see cref="DocumentRecord.Id"/>), restored when it is opened; null if none.</summary>
        public string DocumentId { get; set; }
    }

    /// <summary>
    /// A document attached to a chat, indexed for its questions (the "Document" table; <see cref="IChatDocumentStore"/>): kept so
    /// attaching the same file again, or opening a chat that used it, doesn't index it again. Deleted with the last chat that uses it.
    /// </summary>
    [Table("Document")]
    public class DocumentRecord
    {
        /// <summary>The SHA-256 of the file, in hex</summary>
        [PrimaryKey]
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

    /// <summary>A passage or image of a saved document, with its embedding (the floats' bytes); the "DocumentItem" table.</summary>
    [Table("DocumentItem")]
    public class DocumentItemRecord
    {
        /// <summary>The row's id</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The document</summary>
        [Indexed]
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

    /// <summary>
    /// A saved message (the "Message" table): the user's, a reply, or one of the reply's step lines, which are saved
    /// just before it. An app that keeps more about a message derives from it, with <c>[Table("Message")]</c>.
    /// </summary>
    [Table("Message")]
    public class ChatMessageRecord
    {
        /// <summary>The role of a user message.</summary>
        public const string UserRole = "user";

        /// <summary>The role of a reply.</summary>
        public const string AssistantRole = "assistant";

        /// <summary>The role of a step line shown with the next reply (e.g. "Searching Wikipedia for ..."); not sent to the model.</summary>
        public const string StepRole = "step";

        /// <summary>The message's id, in the order the messages were saved.</summary>
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The chat it belongs to (<see cref="ChatRecord.Id"/>).</summary>
        [Indexed]
        public int ConversationId { get; set; }

        /// <summary><see cref="UserRole"/>, <see cref="AssistantRole"/> or <see cref="StepRole"/>.</summary>
        public string Role { get; set; }

        /// <summary>The text: a reply's answer (an interrupted one ends with "…"), a step line's description.</summary>
        public string Text { get; set; }

        /// <summary>A reply's thinking, or null.</summary>
        public string Thinking { get; set; }

        /// <summary>How long a reply thought, in seconds; null if it didn't, or for chats saved before this column (2026-10-26).</summary>
        public double? ThinkingSeconds { get; set; }

        /// <summary>The words of a voice message the model was given as audio (shown, not given to the model); or null.</summary>
        public string Transcript { get; set; }

        /// <summary>What the model was given before a user message's text (e.g. document excerpts); or null.</summary>
        public string Context { get; set; }

        /// <summary>
        /// The attachments, one per line: the kind ("image", "audio", or an app's own), the media type and the file's
        /// name in the chat's attachment folder, separated by '|'.
        /// </summary>
        public string Attachments { get; set; }

        /// <summary>When it was saved (UTC).</summary>
        public DateTime Created { get; set; }
    }

    /// <summary>
    /// A file saved with a message: an image or audio the user sent, or an app's own kind (e.g. a picture a reply drew).
    /// </summary>
    public sealed class ChatSavedAttachment
    {
        /// <summary>An image the user sent (given to the model).</summary>
        public const string ImageKind = "image";

        /// <summary>Audio the user sent (given to the model).</summary>
        public const string AudioKind = "audio";

        /// <summary>Create one.</summary>
        /// <param name="kind">"image", "audio", or an app's own kind</param>
        /// <param name="mediaType">The media type, e.g. "image/jpeg"</param>
        /// <param name="path">The file</param>
        public ChatSavedAttachment(string kind, string mediaType, string path)
        {
            Kind = kind;
            MediaType = mediaType;
            Path = path;
        }

        /// <summary>"image", "audio", or an app's own kind.</summary>
        public string Kind { get; }

        /// <summary>The media type, e.g. "image/jpeg".</summary>
        public string MediaType { get; }

        /// <summary>The file.</summary>
        public string Path { get; }
    }

    // Which schema the database holds, for migrations later. Version 1: the Conversation and Message tables above, as the
    // MAUI demo's chat page wrote them before this package existed.
    /// <summary>A passage of a saved message, embedded with a search model (the past-chat search's index, <see cref="IChatSearchStore"/>).</summary>
    [Table("ChatEmbedding")]
    public class ChatEmbeddingRecord
    {
        /// <summary>The message's id and the passage's position</summary>
        [PrimaryKey]
        public string Id { get; set; }

        /// <summary>The message</summary>
        [Indexed]
        public int MessageId { get; set; }

        /// <summary>Its position among the message's passages</summary>
        public int Position { get; set; }

        /// <summary>The search model it was embedded with</summary>
        public string Model { get; set; }

        /// <summary>The passage's text</summary>
        public string Text { get; set; }

        /// <summary>Its embedding, as the floats' bytes</summary>
        public byte[] Embedding { get; set; }
    }

    /// <summary>A search passage joined to its still-existing message and chat (a query's projection).</summary>
    public sealed class ChatSearchRecord : ChatEmbeddingRecord
    {
        /// <summary>The chat</summary>
        public int ConversationId { get; set; }

        /// <summary>The chat's title</summary>
        public string Title { get; set; }

        /// <summary>The message's role</summary>
        public string Role { get; set; }

        /// <summary>When the message was saved (UTC)</summary>
        public DateTime Created { get; set; }
    }

    /// <summary>A photo's embedding and details, for the photo search (the "PhotoEmbedding" table; <see cref="IPhotoIndexStore"/>).</summary>
    [Table("PhotoEmbedding")]
    public class PhotoEmbeddingRecord
    {
        /// <summary>The search model's file it was embedded with</summary>
        public string EmbeddingModel { get; set; }

        /// <summary>The photo's id in the platform's photo library</summary>
        [PrimaryKey]
        public string PhotoId { get; set; }

        /// <summary>The photo's version (changes when it is edited)</summary>
        public long Version { get; set; }

        /// <summary>When it was taken (UTC)</summary>
        public DateTime Taken { get; set; }

        /// <summary>The vision tokens the embedding was made with</summary>
        public int VisionTokens { get; set; }

        /// <summary>The version of the details read with it</summary>
        public int DetailsVersion { get; set; }

        /// <summary>Where it was taken, or null</summary>
        public double? Latitude { get; set; }

        /// <summary>Where it was taken, or null</summary>
        public double? Longitude { get; set; }

        /// <summary>The nearest town: "Town, Region, Country", or null</summary>
        public string Place { get; set; }

        /// <summary>The camera, or null</summary>
        public string Camera { get; set; }

        /// <summary>The photo's width</summary>
        public int Width { get; set; }

        /// <summary>The photo's height</summary>
        public int Height { get; set; }

        /// <summary>Its kinds, comma-separated, or null</summary>
        public string Kinds { get; set; }

        /// <summary>The embedding, as the floats' bytes</summary>
        public byte[] Embedding { get; set; }
    }

    [Table("SchemaVersion")]
    internal sealed class SchemaVersionRecord
    {
        [PrimaryKey]
        public string Name { get; set; }

        public int Version { get; set; }
    }
}
