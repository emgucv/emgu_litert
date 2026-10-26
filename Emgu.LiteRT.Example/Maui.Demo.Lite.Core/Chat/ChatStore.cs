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


/// <summary>
/// The chat page's saved chats: a SQLite database (chats/chats.db3 in the app's data folder), with images and
/// audio stored as files in chats/attachments/{conversation id}/ and only their names in the database. (A
/// subfolder, since the app data folder of an unsandboxed Mac Catalyst app is ~/Library itself.) The chats themselves
/// are Emgu.LiteRT.LM.Chat.Sqlite's ChatHistoryStore, and so are the agent's memories and goals (its
/// SqliteAgentMemoryStore and SqliteAgentGoalStore, in the same database); the page's own data - documents, the chat
/// and photo search indexes, a message's language and translated thinking, a chat's document - is kept here.
/// </summary>
internal sealed partial class ChatStore : ChatHistoryStore
{
    internal ChatStore(string folder) : base(folder)
    {
        Memories = new SqliteAgentMemoryStore(this);
        Goals = new SqliteAgentGoalStore(this);
    }

    /// <summary>The agent's memories (remember / forget), kept across chats; none while private.</summary>
    public SqliteAgentMemoryStore Memories { get; }

    /// <summary>The user's goals and their progress (the goal tools); none while private.</summary>
    public SqliteAgentGoalStore Goals { get; }

    // The page's own tables, and its columns of the chats and messages (DocumentId, Language, ThinkingTranslation).
    protected override async Task CreateTablesAsync(SQLiteAsyncConnection connection)
    {
        await connection.CreateTableAsync<ConversationRecord>();
        await connection.CreateTableAsync<MessageRecord>();
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

    // ---------- Documents (the index is the base store's; the demo's names and Emgu.LiteRT.LM.Agents.Documents' types) ----------

    /// <summary>The same passages as Emgu.LiteRT.LM.Agents.Documents' search takes them (DocumentLibrarySearch).</summary>
    public async Task<List<DocumentLibraryPassage>> GetDocumentLibraryPassagesAsync(string embeddingFile) =>
        (await GetDocumentPassagesAsync(embeddingFile)).Select(p => new DocumentLibraryPassage
        {
            DocumentId = p.DocumentId,
            DocumentName = p.DocumentName,
            Location = p.Location,
            Text = p.Text,
            Embedding = ToFloats(p.Embedding)
        }).ToList();

    private static float[] ToFloats(byte[] bytes)
    {
        float[] values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
        return values;
    }

    /// <summary>Record the document a chat uses (null for none).</summary>
    public Task SetConversationDocumentAsync(int conversationId, string documentId) => SetChatDocumentAsync(conversationId, documentId);

    /// <summary>Delete a saved chat, its messages and attachment files.</summary>
    public Task DeleteConversationAsync(int conversationId) => DeleteChatAsync(conversationId);

}
