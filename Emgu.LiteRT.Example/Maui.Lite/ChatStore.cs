//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    internal sealed class ChatStore
    {
        private readonly string _attachmentRoot;
        private readonly Lazy<Task<SQLiteAsyncConnection>> _connection;

        public ChatStore()
        {
            string folder = Path.Combine(FileSystem.AppDataDirectory, "chats");
            Directory.CreateDirectory(folder);
            _attachmentRoot = Path.Combine(folder, "attachments");
            _connection = new Lazy<Task<SQLiteAsyncConnection>>(async () =>
            {
                var connection = new SQLiteAsyncConnection(Path.Combine(folder, "chats.db3"),
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await connection.CreateTableAsync<ConversationRecord>();
                await connection.CreateTableAsync<MessageRecord>();
                await connection.CreateTableAsync<MemoryRecord>();
                return connection;
            });
        }

        private Task<SQLiteAsyncConnection> Db => _connection.Value;

        /// <summary>The saved chats, most recently updated first.</summary>
        public async Task<List<ConversationRecord>> GetConversationsAsync()
        {
            return await (await Db).Table<ConversationRecord>().OrderByDescending(c => c.Updated).ToListAsync();
        }

        public async Task<List<MessageRecord>> GetMessagesAsync(int conversationId)
        {
            return await (await Db).Table<MessageRecord>().Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Id).ToListAsync();
        }

        /// <summary>Start a saved chat; returns its id.</summary>
        public async Task<int> CreateConversationAsync(string title, string model)
        {
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
            return record.Id;
        }

        /// <summary>Record that the model sees the messages up to upToMessageId only as the summary (or not at all).</summary>
        public async Task SetSummaryAsync(int conversationId, string summary, int upToMessageId)
        {
            SQLiteAsyncConnection db = await Db;
            var conversation = await db.FindAsync<ConversationRecord>(conversationId);
            if (conversation == null)
                return;
            conversation.Summary = summary;
            conversation.SummaryUpTo = upToMessageId;
            await db.UpdateAsync(conversation);
        }

        /// <summary>A saved message's attachments, with their full paths.</summary>
        public List<SavedAttachment> GetAttachments(MessageRecord message)
        {
            List<SavedAttachment> result = new List<SavedAttachment>();
            if (string.IsNullOrEmpty(message.Attachments))
                return result;
            foreach (string line in message.Attachments.Split('\n'))
            {
                string[] parts = line.Split('|');
                if (parts.Length != 3)
                    continue;
                string path = Path.Combine(_attachmentRoot, message.ConversationId.ToString(), parts[2]);
                if (File.Exists(path))
                    result.Add(new SavedAttachment { Kind = parts[0], MediaType = parts[1], Path = path });
            }
            return result;
        }

        /// <summary>Delete a saved chat, its messages and attachment files.</summary>
        public async Task DeleteConversationAsync(int conversationId)
        {
            SQLiteAsyncConnection db = await Db;
            await db.ExecuteAsync("DELETE FROM Message WHERE ConversationId = ?", conversationId);
            await db.DeleteAsync<ConversationRecord>(conversationId);
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
            return await (await Db).Table<MemoryRecord>().OrderBy(m => m.Id).ToListAsync();
        }

        /// <summary>Remember something; returns false if the same text is already remembered.</summary>
        public async Task<bool> AddMemoryAsync(string text)
        {
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

        public async Task DeleteMemoryAsync(int id)
        {
            await (await Db).DeleteAsync<MemoryRecord>(id);
        }

        private static string Extension(string mediaType)
        {
            switch (mediaType)
            {
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

#endif
