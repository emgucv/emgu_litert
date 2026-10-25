//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Agents.Documents;
using Emgu.LiteRT.LM.Chat.Sqlite;
using SQLite;

namespace Maui.Demo.Lite;

/// <summary>
/// A saved chat: one row per conversation.
/// </summary>
[Table("Conversation")]
public class ConversationRecord : ChatRecord
{
    /// <summary>The attached document (DocumentRecord.Id), restored when the chat is opened; null if none</summary>
    public string DocumentId { get; set; }
}

/// <summary>
/// A document attached to a chat, indexed for its questions (LiteRtLmChatPage.Documents.cs): kept so attaching the
/// same file again, or opening a chat that used it, doesn't index it again. Deleted with the last chat that uses it.
/// </summary>
[Table("Document")]
public class DocumentRecord
{
    /// <summary>The SHA-256 of the file, in hex</summary>
    [PrimaryKey]
    public string Id { get; set; }

    public string Name { get; set; }

    public int PageCount { get; set; }

    /// <summary>What the index was built with (passage size, embedding model, images...): another value rebuilds it</summary>
    public string Settings { get; set; }

    /// <summary>The chip's description, e.g. "5 pages · 2 pictures"</summary>
    public string Summary { get; set; }

    public DateTime Created { get; set; }
}

/// <summary>
/// A passage or image of a saved document, with its embedding (floats).
/// </summary>
[Table("DocumentItem")]
public class DocumentItemRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string DocumentId { get; set; }

    /// <summary>Its position among the document's passages or images</summary>
    public int Position { get; set; }

    public bool IsImage { get; set; }

    /// <summary>A passage's text</summary>
    public string Text { get; set; }

    public string Location { get; set; }

    /// <summary>An image: whether it is a whole page, its media type and bytes</summary>
    public bool IsPage { get; set; }

    public string MediaType { get; set; }

    public byte[] Data { get; set; }

    public byte[] Embedding { get; set; }
}

/// <summary>
/// A message of a saved chat.
/// </summary>
[Table("Message")]
public class MessageRecord : ChatMessageRecord
{
    /// <summary>
    /// A user message's language (a two-letter code such as "es", detected when it was sent; null if unknown - voice
    /// messages, older chats). For a reply: the language its thinking was translated into (null: not translated).
    /// </summary>
    public string Language { get; set; }

    /// <summary>The reply's thinking translated into <see cref="Language"/> on request (null: not translated).</summary>
    public string ThinkingTranslation { get; set; }
}

/// <summary>A passage of a saved message, embedded with the selected search model.</summary>
[Table("ChatEmbedding")]
public class ChatEmbeddingRecord
{
    [PrimaryKey]
    public string Id { get; set; }
    [Indexed]
    public int MessageId { get; set; }
    public int Position { get; set; }
    public string Model { get; set; }
    public string Text { get; set; }
    public byte[] Embedding { get; set; }
}

/// <summary>A search passage joined to its still-existing message and conversation.</summary>
public class ChatSearchRecord : ChatEmbeddingRecord
{
    public int ConversationId { get; set; }
    public string Title { get; set; }
    public string Role { get; set; }
    public DateTime Created { get; set; }
}

/// <summary>
/// Something the chat agent remembers about the user ("The user is vegetarian"), kept across chats.
/// </summary>
[Table("Memory")]
public class MemoryRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Text { get; set; }

    public DateTime Created { get; set; }
}

/// <summary>
/// A photo's embedding, for the chat's photo search (PhotoIndex): its id in the photo library, the version it was
/// computed from, when it was taken, and the embedding (floats).
/// </summary>
[Table("PhotoEmbedding")]
public class PhotoEmbeddingRecord
{
    public string EmbeddingModel { get; set; }

    [PrimaryKey]
    public string PhotoId { get; set; }

    public long Version { get; set; }

    public DateTime Taken { get; set; }

    /// <summary>The vision tokens the embedding was made with (EmbeddingGemma 2: 70 or 140)</summary>
    public int VisionTokens { get; set; }

    // The photo's details (PhotoItem), read when it was indexed; DetailsVersion changes when what is read changes.
    public int DetailsVersion { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>The nearest town to where it was taken: "Town, Region, Country" (PlaceLookup), or null</summary>
    public string Place { get; set; }

    public string Camera { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Comma-separated: screenshot, selfie, panorama, portrait, favorite</summary>
    public string Kinds { get; set; }

    public byte[] Embedding { get; set; }
}

/// <summary>
/// The chat page's saved chats: a SQLite database (chats/chats.db3 in the app's data folder), with images and
/// audio stored as files in chats/attachments/{conversation id}/ and only their names in the database. (A
/// subfolder, since the app data folder of an unsandboxed Mac Catalyst app is ~/Library itself.) The chats themselves
/// are Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore; the page's own data - documents, memories, goals, the chat and
/// photo search indexes, a message's language and translated thinking, a chat's document - is kept here, in the same
/// database.
/// </summary>
internal sealed partial class ChatStore : ChatHistoryStore, IAgentMemoryStore
{
    internal ChatStore(string folder) : base(folder)
    {
    }

    // The page's own tables, and its columns of the chats and messages (DocumentId, Language, ThinkingTranslation).
    protected override async Task CreateTablesAsync(SQLiteAsyncConnection connection)
    {
        await connection.CreateTableAsync<ConversationRecord>();
        await connection.CreateTableAsync<MessageRecord>();
        await connection.CreateTableAsync<ChatEmbeddingRecord>();
        await connection.CreateTableAsync<MemoryRecord>();
        await connection.CreateTableAsync<GoalRecord>();
        await connection.CreateTableAsync<GoalProgressRecord>();
        await connection.CreateTableAsync<PhotoEmbeddingRecord>();
        await connection.CreateTableAsync<DocumentRecord>();
        await connection.CreateTableAsync<DocumentItemRecord>();
    }

    // A deleted chat's messages leave the chat search index too.
    protected override void DeletingChat(SQLiteConnection connection, int chatId)
    {
        connection.Execute("DELETE FROM ChatEmbedding WHERE MessageId IN (SELECT Id FROM Message WHERE ConversationId = ?)", chatId);
    }

    private Task<SQLiteAsyncConnection> Db => GetConnectionAsync();

    /// <summary>The saved chats, most recently updated first (also while private: the list stays visible).</summary>
    public async Task<List<ConversationRecord>> GetConversationsAsync()
    {
        return await (await Db).Table<ConversationRecord>().OrderByDescending(c => c.Updated).ToListAsync();
    }

    public Task<List<MessageRecord>> GetMessagesAsync(int conversationId) => GetMessagesAsync<MessageRecord>(conversationId);

    /// <summary>Start a saved chat; returns its id.</summary>
    public Task<int> CreateConversationAsync(string title, string model) => CreateChatAsync(title, model);

    /// <summary>Keep a reply's thinking translated into a language (shown again when the chat is reopened).</summary>
    public async Task SetThinkingTranslationAsync(int messageId, string language, string translation)
    {
        if (IsPrivate || messageId <= 0) return;
        SQLiteAsyncConnection db = await Db;
        await db.ExecuteAsync("UPDATE Message SET Language = ?, ThinkingTranslation = ? WHERE Id = ?",
            string.IsNullOrEmpty(language) ? null : language, string.IsNullOrEmpty(translation) ? null : translation, messageId);
    }

    /// <summary>
    /// Add a message, saving its attachments (encoded bytes) as files, with its language; returns its id. (It hides
    /// ChatHistoryStore.AddMessageAsync, whose last parameter is a voice message's transcript.)
    /// </summary>
    public new async Task<int> AddMessageAsync(int conversationId, string role, string text, string thinking,
        IEnumerable<(string Kind, string MediaType, byte[] Data)> attachments = null, string context = null,
        string language = null)
    {
        int id = await base.AddMessageAsync(conversationId, role, text, thinking, attachments, context);
        await SetLanguageAsync(id, language);
        return id;
    }

    /// <summary>Keep a user message's language.</summary>
    public async Task SetLanguageAsync(int messageId, string language)
    {
        if (IsPrivate || messageId <= 0 || string.IsNullOrEmpty(language)) return;
        await (await Db).ExecuteAsync("UPDATE Message SET Language = ? WHERE Id = ?", language, messageId);
    }

    public async Task<List<MessageRecord>> GetSearchMessagesAsync()
    {
        if (IsPrivate) return new List<MessageRecord>();
        return await (await Db).QueryAsync<MessageRecord>(
            "SELECT m.* FROM Message m INNER JOIN Conversation c ON c.Id = m.ConversationId " +
            "WHERE m.Role IN ('user', 'assistant') AND length(trim(m.Text)) > 0 ORDER BY m.Id");
    }

    public async Task<List<ChatSearchRecord>> GetChatEmbeddingsAsync()
    {
        if (IsPrivate) return new List<ChatSearchRecord>();
        return await (await Db).QueryAsync<ChatSearchRecord>(
            "SELECT e.*, m.ConversationId, m.Role, m.Created, c.Title FROM ChatEmbedding e " +
            "INNER JOIN Message m ON m.Id = e.MessageId INNER JOIN Conversation c ON c.Id = m.ConversationId");
    }

    // Never resurrect an index entry if the chat was deleted while its embedding was computed.
    public async Task SaveChatEmbeddingsAsync(int messageId, List<ChatEmbeddingRecord> passages)
    {
        if (IsPrivate) return;
        await (await Db).RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM ChatEmbedding WHERE MessageId = ?", messageId);
            if (connection.Find<MessageRecord>(messageId) != null)
                connection.InsertAll(passages);
        });
    }

    public async Task ClearChatEmbeddingsAsync()
    {
        await (await Db).DeleteAllAsync<ChatEmbeddingRecord>();
    }

    // ---------- Documents ----------

    public async Task<DocumentRecord> GetDocumentAsync(string id)
    {
        if (IsPrivate) return null;
        return await (await Db).FindAsync<DocumentRecord>(id);
    }

    public async Task<List<DocumentItemRecord>> GetDocumentItemsAsync(string id)
    {
        if (IsPrivate) return new List<DocumentItemRecord>();
        return await (await Db).Table<DocumentItemRecord>().Where(i => i.DocumentId == id).OrderBy(i => i.Position).ToListAsync();
    }

    /// <summary>
    /// Every saved passage (not image) with its document, for searching all saved documents. Documents indexed with
    /// another embedding model (the second part of DocumentRecord.Settings) are left out: their vectors can't be compared.
    /// </summary>
    public async Task<List<(DocumentRecord Document, DocumentItemRecord Item)>> GetDocumentPassagesAsync(string embeddingFile)
    {
        var result = new List<(DocumentRecord, DocumentItemRecord)>();
        if (IsPrivate) return result;
        SQLiteAsyncConnection db = await Db;
        foreach (DocumentRecord document in await db.Table<DocumentRecord>().ToListAsync())
        {
            if (document.Settings?.Split('|').ElementAtOrDefault(1) != embeddingFile)
                continue;
            foreach (DocumentItemRecord item in await db.Table<DocumentItemRecord>()
                .Where(i => i.DocumentId == document.Id && !i.IsImage).OrderBy(i => i.Position).ToListAsync())
                if (item.Embedding != null && item.Embedding.Length > 0 && !string.IsNullOrEmpty(item.Text))
                    result.Add((document, item));
        }
        return result;
    }

    /// <summary>The same passages as Emgu.LiteRT.LM.Agents.Documents' search takes them (DocumentLibrarySearch).</summary>
    public async Task<List<DocumentLibraryPassage>> GetDocumentLibraryPassagesAsync(string embeddingFile) =>
        (await GetDocumentPassagesAsync(embeddingFile)).Select(p => new DocumentLibraryPassage
        {
            DocumentId = p.Document.Id,
            DocumentName = p.Document.Name,
            Location = p.Item.Location,
            Text = p.Item.Text,
            Embedding = ToFloats(p.Item.Embedding)
        }).ToList();

    private static float[] ToFloats(byte[] bytes)
    {
        float[] values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
        return values;
    }

    /// <summary>Save a document's index, replacing an older one of the same file.</summary>
    public async Task SaveDocumentAsync(DocumentRecord document, IEnumerable<DocumentItemRecord> items)
    {
        if (IsPrivate) return;
        SQLiteAsyncConnection db = await Db;
        List<DocumentItemRecord> list = items.ToList();
        await db.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM DocumentItem WHERE DocumentId = ?", document.Id);
            connection.InsertOrReplace(document);
            connection.InsertAll(list);
        });
    }

    /// <summary>Record the document a chat uses (null for none).</summary>
    public async Task SetConversationDocumentAsync(int conversationId, string documentId)
    {
        if (IsPrivate) return;
        SQLiteAsyncConnection db = await Db;
        var conversation = await db.FindAsync<ConversationRecord>(conversationId);
        if (conversation == null || conversation.DocumentId == documentId)
            return;
        conversation.DocumentId = documentId;
        await db.UpdateAsync(conversation);
    }

    /// <summary>Delete the documents no saved chat uses, except keepId (e.g. the one attached now).</summary>
    public async Task DeleteUnusedDocumentsAsync(string keepId = null)
    {
        if (IsPrivate) return;
        SQLiteAsyncConnection db = await Db;
        HashSet<string> used = new HashSet<string>((await db.Table<ConversationRecord>().ToListAsync())
            .Where(c => c.DocumentId != null).Select(c => c.DocumentId));
        if (keepId != null)
            used.Add(keepId);
        foreach (DocumentRecord document in await db.Table<DocumentRecord>().ToListAsync())
        {
            if (used.Contains(document.Id))
                continue;
            await db.ExecuteAsync("DELETE FROM DocumentItem WHERE DocumentId = ?", document.Id);
            await db.DeleteAsync<DocumentRecord>(document.Id);
        }
    }

    /// <summary>Delete a saved chat, its messages and attachment files.</summary>
    public Task DeleteConversationAsync(int conversationId) => DeleteChatAsync(conversationId);

    /// <summary>The memories, oldest first.</summary>
    public async Task<List<MemoryRecord>> GetMemoriesAsync()
    {
        if (IsPrivate) return new List<MemoryRecord>();
        return await (await Db).Table<MemoryRecord>().OrderBy(m => m.Id).ToListAsync();
    }

    /// <summary>Remember something; returns false if the same text is already remembered.</summary>
    public async Task<bool> AddMemoryAsync(string text)
    {
        if (IsPrivate) return false;
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;
        SQLiteAsyncConnection db = await Db;
        foreach (MemoryRecord existing in await db.Table<MemoryRecord>().ToListAsync())
            if (string.Equals(existing.Text, text, StringComparison.OrdinalIgnoreCase))
                return false;
        await db.InsertAsync(new MemoryRecord { Text = text, Created = DateTime.UtcNow });
        return true;
    }

    // ---------- Photo search ----------

    public async Task<List<PhotoEmbeddingRecord>> GetPhotoEmbeddingsAsync()
    {
        return await (await Db).Table<PhotoEmbeddingRecord>().ToListAsync();
    }

    public async Task SavePhotoEmbeddingAsync(PhotoEmbeddingRecord record)
    {
        await (await Db).InsertOrReplaceAsync(record);
    }

    public async Task DeletePhotoEmbeddingsAsync(IEnumerable<string> photoIds)
    {
        SQLiteAsyncConnection db = await Db;
        foreach (string id in photoIds)
            await db.DeleteAsync<PhotoEmbeddingRecord>(id);
    }

    public async Task ClearPhotoEmbeddingsAsync()
    {
        await (await Db).DeleteAllAsync<PhotoEmbeddingRecord>();
    }

    public async Task DeleteMemoryAsync(int id)
    {
        if (IsPrivate) return;
        await (await Db).DeleteAsync<MemoryRecord>(id);
    }

    // The memories for the agent's remember / forget tools (Emgu.LiteRT.LM.Agents).
    async Task<IList<AgentMemory>> IAgentMemoryStore.GetMemoriesAsync()
    {
        return (await GetMemoriesAsync()).Select(m => new AgentMemory(m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), m.Text)).ToList();
    }

    Task IAgentMemoryStore.DeleteMemoryAsync(string id)
    {
        return DeleteMemoryAsync(int.Parse(id, System.Globalization.CultureInfo.InvariantCulture));
    }
}
