//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using SQLite;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// A saved chat: one row per conversation.
    /// </summary>
    [Table("Conversation")]
    public class ConversationRecord
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>The first message, shortened</summary>
        public string Title { get; set; }

        /// <summary>The model's ModelChoice name, e.g. "Gemma4E2B"</summary>
        public string Model { get; set; }

        public DateTime Created { get; set; }

        public DateTime Updated { get; set; }

        /// <summary>
        /// When the chat outgrew the model's context window: a summary of the messages up to SummaryUpTo, given to the
        /// model in their place; null if they were dropped without one (or the chat was never compacted)
        /// </summary>
        public string Summary { get; set; }

        /// <summary>The id of the last message the model no longer sees (summarized or dropped); 0 for none</summary>
        public int SummaryUpTo { get; set; }

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
    public class MessageRecord
    {
        public const string UserRole = "user";
        public const string AssistantRole = "assistant";
        /// <summary>A tool call shown in the transcript (e.g. "Searching Wikipedia for ..."); not sent to the model</summary>
        public const string StepRole = "step";

        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public int ConversationId { get; set; }

        public string Role { get; set; }

        public string Text { get; set; }

        public string Thinking { get; set; }

        /// <summary>
        /// What the model was given with a user message besides its text: excerpts of an attached document (null if
        /// none). The model sees it before the message; the transcript shows only the message.
        /// </summary>
        public string Context { get; set; }

        /// <summary>
        /// Attachments, one per line: "image|audio", the media type and the file's name in the conversation's folder,
        /// separated by '|'
        /// </summary>
        public string Attachments { get; set; }

        public DateTime Created { get; set; }
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
    /// An attachment of a saved message: its kind ("image" or "audio"), media type and file.
    /// </summary>
    public sealed class SavedAttachment
    {
        public string Kind;
        public string MediaType;
        public string Path;
    }

    /// <summary>
    /// The chat page's saved chats: a SQLite database (chats/chats.db3 in the app's data folder), with images and
    /// audio stored as files in chats/attachments/{conversation id}/ and only their names in the database. (A
    /// subfolder, since the app data folder of an unsandboxed Mac Catalyst app is ~/Library itself.)
    /// </summary>
    internal sealed partial class ChatStore : IAgentMemoryStore
    {
        private readonly string _attachmentRoot;
        private readonly Lazy<Task<SQLiteAsyncConnection>> _connection;

        public event Action ChatsChanged;
        // Per-page/session state, never persisted. Enforce the boundary here as well as in the UI/tool selection.
        private volatile bool _isPrivateSession;
        public bool IsPrivateSession { get => _isPrivateSession; set => _isPrivateSession = value; }

        internal ChatStore(string folder)
        {
            Directory.CreateDirectory(folder);
            _attachmentRoot = Path.Combine(folder, "attachments");
            _connection = new Lazy<Task<SQLiteAsyncConnection>>(async () =>
            {
                var connection = new SQLiteAsyncConnection(Path.Combine(folder, "chats.db3"),
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await connection.CreateTableAsync<ConversationRecord>();
                await connection.CreateTableAsync<MessageRecord>();
                await connection.CreateTableAsync<ChatEmbeddingRecord>();
                await connection.CreateTableAsync<MemoryRecord>();
                await connection.CreateTableAsync<GoalRecord>();
                await connection.CreateTableAsync<GoalProgressRecord>();
                await connection.CreateTableAsync<PhotoEmbeddingRecord>();
                await connection.CreateTableAsync<DocumentRecord>();
                await connection.CreateTableAsync<DocumentItemRecord>();
                return connection;
            });
        }

        private Task<SQLiteAsyncConnection> Db => _connection.Value;

        internal async Task CloseAsync()
        {
            if (_connection.IsValueCreated)
                await (await Db).CloseAsync();
        }

        /// <summary>The saved chats, most recently updated first.</summary>
        public async Task<List<ConversationRecord>> GetConversationsAsync()
        {
            return await (await Db).Table<ConversationRecord>().OrderByDescending(c => c.Updated).ToListAsync();
        }

        public async Task<List<MessageRecord>> GetMessagesAsync(int conversationId)
        {
            if (IsPrivateSession) return new List<MessageRecord>();
            return await (await Db).Table<MessageRecord>().Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Id).ToListAsync();
        }

        /// <summary>Start a saved chat; returns its id.</summary>
        public async Task<int> CreateConversationAsync(string title, string model)
        {
            if (IsPrivateSession) throw new InvalidOperationException("Private chats cannot be saved.");
            var conversation = new ConversationRecord
            {
                Title = Shorten(title, 60),
                Model = model,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };
            await (await Db).InsertAsync(conversation);
            return conversation.Id;
        }

        /// <summary>
        /// Add a message, saving its attachments (encoded bytes) as files; returns its id.
        /// </summary>
        public async Task<int> AddMessageAsync(int conversationId, string role, string text, string thinking,
            IEnumerable<(string Kind, string MediaType, byte[] Data)> attachments = null, string context = null)
        {
            if (IsPrivateSession) throw new InvalidOperationException("Private chats cannot be saved.");
            SQLiteAsyncConnection db = await Db;
            var record = new MessageRecord
            {
                ConversationId = conversationId,
                Role = role,
                Text = text ?? string.Empty,
                Thinking = string.IsNullOrEmpty(thinking) ? null : thinking,
                Context = string.IsNullOrEmpty(context) ? null : context,
                Created = DateTime.UtcNow
            };
            await db.InsertAsync(record);

            List<string> saved = new List<string>();
            int index = 0;
            foreach (var attachment in attachments ?? Enumerable.Empty<(string, string, byte[])>())
            {
                if (attachment.Data == null)
                    continue;
                string folder = Path.Combine(_attachmentRoot, conversationId.ToString());
                Directory.CreateDirectory(folder);
                string name = string.Format("{0}_{1}{2}", record.Id, index++, Extension(attachment.MediaType));
                await File.WriteAllBytesAsync(Path.Combine(folder, name), attachment.Data);
                saved.Add(string.Join("|", attachment.Kind, attachment.MediaType, name));
            }
            if (saved.Count > 0)
            {
                record.Attachments = string.Join("\n", saved);
                await db.UpdateAsync(record);
            }

            var conversation = await db.FindAsync<ConversationRecord>(conversationId);
            if (conversation != null)
            {
                conversation.Updated = DateTime.UtcNow;
                await db.UpdateAsync(conversation);
            }
            ChatsChanged?.Invoke();
            return record.Id;
        }

        public async Task<List<MessageRecord>> GetSearchMessagesAsync()
        {
            if (IsPrivateSession) return new List<MessageRecord>();
            return await (await Db).QueryAsync<MessageRecord>(
                "SELECT m.* FROM Message m INNER JOIN Conversation c ON c.Id = m.ConversationId " +
                "WHERE m.Role IN ('user', 'assistant') AND length(trim(m.Text)) > 0 ORDER BY m.Id");
        }

        public async Task<List<ChatSearchRecord>> GetChatEmbeddingsAsync()
        {
            if (IsPrivateSession) return new List<ChatSearchRecord>();
            return await (await Db).QueryAsync<ChatSearchRecord>(
                "SELECT e.*, m.ConversationId, m.Role, m.Created, c.Title FROM ChatEmbedding e " +
                "INNER JOIN Message m ON m.Id = e.MessageId INNER JOIN Conversation c ON c.Id = m.ConversationId");
        }

        // Never resurrect an index entry if the chat was deleted while its embedding was computed.
        public async Task SaveChatEmbeddingsAsync(int messageId, List<ChatEmbeddingRecord> passages)
        {
            if (IsPrivateSession) return;
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

        /// <summary>Record that the model sees the messages up to upToMessageId only as the summary (or not at all).</summary>
        public async Task SetSummaryAsync(int conversationId, string summary, int upToMessageId)
        {
            if (IsPrivateSession) return;
            SQLiteAsyncConnection db = await Db;
            var conversation = await db.FindAsync<ConversationRecord>(conversationId);
            if (conversation == null)
                return;
            conversation.Summary = summary;
            conversation.SummaryUpTo = upToMessageId;
            await db.UpdateAsync(conversation);
        }

        // ---------- Documents ----------

        public async Task<DocumentRecord> GetDocumentAsync(string id)
        {
            if (IsPrivateSession) return null;
            return await (await Db).FindAsync<DocumentRecord>(id);
        }

        public async Task<List<DocumentItemRecord>> GetDocumentItemsAsync(string id)
        {
            if (IsPrivateSession) return new List<DocumentItemRecord>();
            return await (await Db).Table<DocumentItemRecord>().Where(i => i.DocumentId == id).OrderBy(i => i.Position).ToListAsync();
        }

        /// <summary>
        /// Every saved passage (not image) with its document, for searching all saved documents. Documents indexed with
        /// another embedding model (the second part of DocumentRecord.Settings) are left out: their vectors can't be compared.
        /// </summary>
        public async Task<List<(DocumentRecord Document, DocumentItemRecord Item)>> GetDocumentPassagesAsync(string embeddingFile)
        {
            var result = new List<(DocumentRecord, DocumentItemRecord)>();
            if (IsPrivateSession) return result;
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

        /// <summary>Save a document's index, replacing an older one of the same file.</summary>
        public async Task SaveDocumentAsync(DocumentRecord document, IEnumerable<DocumentItemRecord> items)
        {
            if (IsPrivateSession) return;
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
            if (IsPrivateSession) return;
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
            if (IsPrivateSession) return;
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

        /// <summary>A saved message's attachments, with their full paths.</summary>
        public List<SavedAttachment> GetAttachments(MessageRecord message, bool includeMissing = false)
        {
            List<SavedAttachment> result = new List<SavedAttachment>();
            if (string.IsNullOrEmpty(message.Attachments))
                return result;
            foreach (string line in message.Attachments.Split('\n'))
            {
                string[] parts = line.Split('|');
                if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[2])
                    || parts[2].IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || parts[2] == "." || parts[2] == "..")
                    continue;
                string path = Path.Combine(_attachmentRoot, message.ConversationId.ToString(), parts[2]);
                if (includeMissing || File.Exists(path))
                    result.Add(new SavedAttachment { Kind = parts[0], MediaType = parts[1], Path = path });
            }
            return result;
        }

        /// <summary>Delete a saved chat, its messages and attachment files.</summary>
        public async Task DeleteConversationAsync(int conversationId)
        {
            SQLiteAsyncConnection db = await Db;
            await db.RunInTransactionAsync(connection =>
            {
                connection.Execute("DELETE FROM ChatEmbedding WHERE MessageId IN (SELECT Id FROM Message WHERE ConversationId = ?)", conversationId);
                connection.Execute("DELETE FROM Message WHERE ConversationId = ?", conversationId);
                connection.Delete<ConversationRecord>(conversationId);
            });
            ChatsChanged?.Invoke();
            string folder = Path.Combine(_attachmentRoot, conversationId.ToString());
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>The memories, oldest first.</summary>
        public async Task<List<MemoryRecord>> GetMemoriesAsync()
        {
            if (IsPrivateSession) return new List<MemoryRecord>();
            return await (await Db).Table<MemoryRecord>().OrderBy(m => m.Id).ToListAsync();
        }

        /// <summary>Remember something; returns false if the same text is already remembered.</summary>
        public async Task<bool> AddMemoryAsync(string text)
        {
            if (IsPrivateSession) return false;
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
            if (IsPrivateSession) return;
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

        private static string Extension(string mediaType)
        {
            int parameter = mediaType?.IndexOf(';') ?? -1;
            if (parameter >= 0)
                mediaType = mediaType.Substring(0, parameter).Trim();
            switch (mediaType)
            {
                case "application/vnd.openxmlformats-officedocument.wordprocessingml.document": return ".docx";
                case "application/pdf": return ".pdf";
                case "text/markdown": return ".md";
                case "image/jpeg": return ".jpg";
                case "image/png": return ".png";
                case "audio/mpeg": return ".mp3";
                case "audio/flac": return ".flac";
                default: return mediaType != null && mediaType.StartsWith("audio") ? ".wav" : ".bin";
            }
        }

        public static string Shorten(string text, int length)
        {
            text = (text ?? string.Empty).Replace('\n', ' ').Trim();
            return text.Length <= length ? text : text.Substring(0, length - 1).TrimEnd() + "…";
        }
    }
}
