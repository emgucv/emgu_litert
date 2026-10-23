//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using SQLite;

namespace Emgu.LiteRT.LM.Chat.Sqlite
{
    /// <summary>
    /// Saved chats in a SQLite database: each chat's messages, the replies' thinking and step lines, the user's images and
    /// audio (as files next to the database), and where the model's memory of the chat starts after it was compacted.
    /// <code>
    /// var store = new ChatHistoryStore(folder);
    /// int chatId = await store.CreateChatAsync(ChatHistoryStore.TitleOf(message), "Qwen3");
    /// await store.SaveExchangeAsync(chatId, message, reply);        // e.g. in ChatConversation.ExchangeCompleted
    /// (await store.LoadAsync(chatId)).Load(conversation);            // reopen it later
    /// </code>
    /// <para>
    /// The database is <c>chats.db3</c> in the folder, and the files are in <c>attachments/&lt;chat id&gt;/</c>. An app
    /// keeps its own tables in the same database (<see cref="CreateTablesAsync"/>, <see cref="GetConnectionAsync"/>) and
    /// its own columns in records derived from <see cref="ChatRecord"/> / <see cref="ChatMessageRecord"/>. The store
    /// remembers which row each message it saved or loaded is, so <see cref="SaveCompactionAsync"/> and
    /// <see cref="GetMessageId"/> need nothing else. With <see cref="IsPrivate"/> on, nothing is saved.
    /// </para>
    /// </summary>
    public class ChatHistoryStore
    {
        /// <summary>The version of the tables this store reads and writes (kept in the database, for migrations).</summary>
        public const int SchemaVersion = 1;

        private const string SchemaName = "Emgu.LiteRT.LM.Chat.Sqlite";
        private readonly Lazy<Task<SQLiteAsyncConnection>> _connection;
        private readonly ConditionalWeakTable<ChatMessageItem, SavedId> _ids = new ConditionalWeakTable<ChatMessageItem, SavedId>();
        private volatile bool _isPrivate;

        private sealed class SavedId
        {
            public int Id;
        }

        /// <summary>
        /// Open (or create) the store in a folder: <c>chats.db3</c>, and the files in <c>attachments/</c>.
        /// </summary>
        /// <param name="folder">The folder, e.g. a "chats" folder in the app's data folder (created if needed)</param>
        public ChatHistoryStore(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                throw new ArgumentNullException(nameof(folder));
            Folder = folder;
            Directory.CreateDirectory(folder);
            AttachmentFolder = Path.Combine(folder, "attachments");
            _connection = new Lazy<Task<SQLiteAsyncConnection>>(OpenAsync);
        }

        /// <summary>The store's folder.</summary>
        public string Folder { get; }

        /// <summary>The folder the files are saved in, one folder per chat.</summary>
        public string AttachmentFolder { get; }

        /// <summary>
        /// True while the chat is private: nothing is saved (creating a chat or saving a message throws; other writes do
        /// nothing) and no saved chat is read.
        /// </summary>
        public bool IsPrivate
        {
            get => _isPrivate;
            set => _isPrivate = value;
        }

        /// <summary>Raised when a chat was created, added to or deleted.</summary>
        public event EventHandler ChatsChanged;

        /// <summary>Raise <see cref="ChatsChanged"/> (e.g. after the app changed a chat itself).</summary>
        protected void OnChatsChanged()
        {
            ChatsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task<SQLiteAsyncConnection> OpenAsync()
        {
            var connection = new SQLiteAsyncConnection(Path.Combine(Folder, "chats.db3"),
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await connection.CreateTableAsync<ChatRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<ChatMessageRecord>().ConfigureAwait(false);
            await connection.CreateTableAsync<SchemaVersionRecord>().ConfigureAwait(false);
            if (await connection.FindAsync<SchemaVersionRecord>(SchemaName).ConfigureAwait(false) == null)
                await connection.InsertAsync(new SchemaVersionRecord { Name = SchemaName, Version = SchemaVersion }).ConfigureAwait(false);
            await CreateTablesAsync(connection).ConfigureAwait(false);
            return connection;
        }

        /// <summary>
        /// Create the app's own tables when the database is opened (after the store's), e.g. a derived record's table, which
        /// adds its columns: <c>await connection.CreateTableAsync&lt;MyMessageRecord&gt;()</c>.
        /// </summary>
        /// <param name="connection">The database</param>
        protected virtual Task CreateTablesAsync(SQLiteAsyncConnection connection)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Delete the app's own rows of a chat, in the transaction that deletes the chat (before its messages go).
        /// </summary>
        /// <param name="connection">The database, in the transaction</param>
        /// <param name="chatId">The chat</param>
        protected virtual void DeletingChat(SQLiteConnection connection, int chatId)
        {
        }

        /// <summary>The database, opened (and its tables created) on first use.</summary>
        public Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            return _connection.Value;
        }

        /// <summary>Close the database, if it was opened.</summary>
        public async Task CloseAsync()
        {
            if (_connection.IsValueCreated)
                await (await _connection.Value.ConfigureAwait(false)).CloseAsync().ConfigureAwait(false);
        }

        // ---------- Chats ----------

        /// <summary>The saved chats, the most recently updated first (none while <see cref="IsPrivate"/>).</summary>
        public async Task<List<ChatRecord>> GetChatsAsync()
        {
            if (IsPrivate)
                return new List<ChatRecord>();
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            return await db.Table<ChatRecord>().OrderByDescending(c => c.Updated).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>A saved chat, or null.</summary>
        /// <param name="chatId">Its id</param>
        public async Task<ChatRecord> GetChatAsync(int chatId)
        {
            if (IsPrivate)
                return null;
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            return await db.FindAsync<ChatRecord>(chatId).ConfigureAwait(false);
        }

        /// <summary>Start a saved chat.</summary>
        /// <param name="title">Its title, e.g. <see cref="TitleOf"/> the first message (shortened to 60 characters)</param>
        /// <param name="model">The model, in the app's words, or null</param>
        /// <returns>The chat's id</returns>
        public async Task<int> CreateChatAsync(string title, string model)
        {
            ThrowIfPrivate();
            DateTime now = DateTime.UtcNow;
            var chat = new ChatRecord { Title = Shorten(title, 60), Model = model, Created = now, Updated = now };
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.InsertAsync(chat).ConfigureAwait(false);
            OnChatsChanged();
            return chat.Id;
        }

        /// <summary>A title for a chat that starts with this message: its text, else "Image" or "Voice message".</summary>
        /// <param name="message">The first message</param>
        public static string TitleOf(ChatMessageItem message)
        {
            if (message != null && !string.IsNullOrWhiteSpace(message.Text))
                return message.Text;
            if (message != null && message.Attachments.Any(a => a.HasTopLevelMediaType("image")))
                return "Image";
            return "Voice message";
        }

        /// <summary>Delete a chat, its messages (and the app's rows: <see cref="DeletingChat"/>) and its files.</summary>
        /// <param name="chatId">The chat</param>
        public async Task DeleteChatAsync(int chatId)
        {
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.RunInTransactionAsync(connection =>
            {
                DeletingChat(connection, chatId);
                connection.Execute("DELETE FROM Message WHERE ConversationId = ?", chatId);
                connection.Delete<ChatRecord>(chatId);
            }).ConfigureAwait(false);
            OnChatsChanged();
            string folder = Path.Combine(AttachmentFolder, chatId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch (Exception)
            {
            }
        }

        // ---------- Saving ----------

        /// <summary>
        /// Save an exchange: the user's message (with its images, audio, context and transcript), the reply's step lines,
        /// and the reply (with its thinking; an interrupted one ends with "…"). Call it once the reply is complete, e.g. in
        /// <see cref="ChatConversation.ExchangeCompleted"/>.
        /// </summary>
        /// <param name="chatId">The chat (<see cref="CreateChatAsync"/>)</param>
        /// <param name="message">The user's message</param>
        /// <param name="reply">The reply</param>
        /// <param name="replyAttachments">Files of the app's own kinds to keep with the reply (e.g. a picture it drew), or null</param>
        /// <returns>The ids of the message and the reply</returns>
        public async Task<ChatSavedExchange> SaveExchangeAsync(int chatId, ChatMessageItem message, ChatMessageItem reply,
            IEnumerable<(string Kind, string MediaType, byte[] Data)> replyAttachments = null)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (reply == null)
                throw new ArgumentNullException(nameof(reply));
            ThrowIfPrivate();
            var files = message.Attachments
                .Select(a => (a.HasTopLevelMediaType("image") ? ChatSavedAttachment.ImageKind : ChatSavedAttachment.AudioKind, a.MediaType, a.Data.ToArray()))
                .ToList();
            int messageId = await InsertAsync(chatId, ChatMessageRecord.UserRole, message.Text, null, files, message.Context, message.Transcript).ConfigureAwait(false);
            Remember(message, messageId);
            foreach (ChatToolStep step in reply.Steps.ToList())
                await InsertAsync(chatId, ChatMessageRecord.StepRole, step.Description, null, null, null, null).ConfigureAwait(false);
            int replyId = await InsertAsync(chatId, ChatMessageRecord.AssistantRole, ReplyText(reply), reply.Thinking, replyAttachments, null, null).ConfigureAwait(false);
            Remember(reply, replyId);
            await TouchAsync(chatId).ConfigureAwait(false);
            OnChatsChanged();
            return new ChatSavedExchange(messageId, replyId);
        }

        /// <summary>
        /// Replace the saved reply to a message - the reply and its step lines, the rows saved after the message - with a
        /// new one, e.g. after <see cref="ChatConversation.RegenerateAsync"/> (<see cref="ChatExchangeEventArgs.Regenerated"/>).
        /// The message must have been saved by this store (or loaded from it) and be the chat's latest; if it wasn't saved,
        /// the exchange is saved as a new one.
        /// </summary>
        /// <param name="chatId">The chat</param>
        /// <param name="message">The user's message</param>
        /// <param name="reply">The new reply</param>
        /// <param name="replyAttachments">Files of the app's own kinds to keep with the reply, or null</param>
        /// <returns>The ids of the message and the new reply</returns>
        public async Task<ChatSavedExchange> ReplaceReplyAsync(int chatId, ChatMessageItem message, ChatMessageItem reply,
            IEnumerable<(string Kind, string MediaType, byte[] Data)> replyAttachments = null)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            if (reply == null)
                throw new ArgumentNullException(nameof(reply));
            int messageId = GetMessageId(message);
            if (messageId <= 0)
                return await SaveExchangeAsync(chatId, message, reply, replyAttachments).ConfigureAwait(false);
            ThrowIfPrivate();
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            List<ChatMessageRecord> after = await db.Table<ChatMessageRecord>()
                .Where(m => m.ConversationId == chatId && m.Id > messageId).ToListAsync().ConfigureAwait(false);
            foreach (ChatMessageRecord row in after)
            {
                foreach (ChatSavedAttachment file in GetAttachments(row))
                {
                    try { File.Delete(file.Path); }
                    catch (Exception) { }
                }
                await db.DeleteAsync<ChatMessageRecord>(row.Id).ConfigureAwait(false);
            }
            foreach (ChatToolStep step in reply.Steps.ToList())
                await InsertAsync(chatId, ChatMessageRecord.StepRole, step.Description, null, null, null, null).ConfigureAwait(false);
            int replyId = await InsertAsync(chatId, ChatMessageRecord.AssistantRole, ReplyText(reply), reply.Thinking, replyAttachments, null, null).ConfigureAwait(false);
            Remember(reply, replyId);
            await TouchAsync(chatId).ConfigureAwait(false);
            OnChatsChanged();
            return new ChatSavedExchange(messageId, replyId);
        }

        // An interrupted reply is kept as cut off.
        private static string ReplyText(ChatMessageItem reply) =>
            reply.State == ChatMessageState.Interrupted ? (reply.Text.Length > 0 ? reply.Text + " …" : "…") : reply.Text;

        /// <summary>
        /// Save a conversation from now on: each completed exchange (creating the chat with the first), a regenerated
        /// reply in place of the one before, the compaction point, and a voice message's transcript written down after the
        /// message was saved. Nothing is saved while <see cref="IsPrivate"/>.
        /// <code>
        /// ChatHistory history = await store.LoadAsync(chatId);   // a saved chat, to continue
        /// history.Load(conversation);
        /// using var saving = store.Attach(conversation, chatId);
        /// </code>
        /// </summary>
        /// <param name="conversation">The conversation</param>
        /// <param name="chatId">The saved chat it continues (e.g. one loaded into it), or 0 to create one with the first exchange</param>
        /// <param name="model">The model, in the app's words, for a chat it creates; or null</param>
        /// <returns>The binding: dispose it to stop saving (e.g. for a new chat)</returns>
        public ChatHistoryBinding Attach(ChatConversation conversation, int chatId = 0, string model = null)
        {
            if (conversation == null)
                throw new ArgumentNullException(nameof(conversation));
            return new ChatHistoryBinding(this, conversation, chatId, model);
        }

        /// <summary>
        /// Add one row to a chat - a message, or a step line (<see cref="ChatMessageRecord.StepRole"/>) - saving its files.
        /// For what <see cref="SaveExchangeAsync"/> doesn't do, e.g. an app's own step line.
        /// </summary>
        /// <returns>The row's id</returns>
        public async Task<int> AddMessageAsync(int chatId, string role, string text, string thinking = null,
            IEnumerable<(string Kind, string MediaType, byte[] Data)> attachments = null, string context = null, string transcript = null)
        {
            ThrowIfPrivate();
            int id = await InsertAsync(chatId, role, text, thinking, attachments, context, transcript).ConfigureAwait(false);
            await TouchAsync(chatId).ConfigureAwait(false);
            OnChatsChanged();
            return id;
        }

        /// <summary>
        /// Keep the words of a voice message, written down after it was saved; a chat still titled "Voice message" is
        /// titled with them. Does nothing while <see cref="IsPrivate"/>.
        /// </summary>
        /// <param name="chatId">The chat</param>
        /// <param name="messageId">The message (<see cref="GetMessageId"/>)</param>
        /// <param name="transcript">The words</param>
        public async Task SetTranscriptAsync(int chatId, int messageId, string transcript)
        {
            if (IsPrivate || messageId <= 0 || string.IsNullOrWhiteSpace(transcript))
                return;
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.ExecuteAsync("UPDATE Message SET Transcript = ? WHERE Id = ?", transcript.Trim(), messageId).ConfigureAwait(false);
            if (await db.ExecuteAsync("UPDATE Conversation SET Title = ? WHERE Id = ? AND Title = ?",
                    Shorten(transcript, 60), chatId, "Voice message").ConfigureAwait(false) > 0)
                OnChatsChanged();
        }

        /// <summary>
        /// Keep where the model's memory of the chat starts after the conversation was compacted, and the summary of the
        /// messages before (null if they were dropped). Call it in <see cref="ChatConversation.Compacted"/>; the messages
        /// before <see cref="ChatConversation.ContextStart"/> must have been saved by this store (or loaded from it).
        /// Does nothing while <see cref="IsPrivate"/>.
        /// </summary>
        /// <param name="chatId">The chat</param>
        /// <param name="conversation">The conversation that was compacted</param>
        public Task SaveCompactionAsync(int chatId, ChatConversation conversation)
        {
            if (conversation == null)
                throw new ArgumentNullException(nameof(conversation));
            int upTo = conversation.Messages.Take(conversation.ContextStart).Select(GetMessageId).Where(id => id > 0).DefaultIfEmpty(0).Last();
            return upTo > 0 ? SetSummaryAsync(chatId, conversation.Summary, upTo) : Task.CompletedTask;
        }

        /// <summary>
        /// Keep that the model sees the messages up to a message only as the summary (null: not at all). Does nothing while
        /// <see cref="IsPrivate"/>.
        /// </summary>
        /// <param name="chatId">The chat</param>
        /// <param name="summary">The summary, or null</param>
        /// <param name="upToMessageId">The last message the model no longer sees</param>
        public async Task SetSummaryAsync(int chatId, string summary, int upToMessageId)
        {
            if (IsPrivate)
                return;
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.ExecuteAsync("UPDATE Conversation SET Summary = ?, SummaryUpTo = ? WHERE Id = ?", summary, upToMessageId, chatId).ConfigureAwait(false);
        }

        /// <summary>The id of a message this store saved or loaded, or 0.</summary>
        /// <param name="message">The message</param>
        public int GetMessageId(ChatMessageItem message)
        {
            return message != null && _ids.TryGetValue(message, out SavedId saved) ? saved.Id : 0;
        }

        private void Remember(ChatMessageItem message, int id)
        {
            _ids.Remove(message);
            _ids.Add(message, new SavedId { Id = id });
        }

        private void ThrowIfPrivate()
        {
            if (IsPrivate)
                throw new InvalidOperationException("Private chats cannot be saved.");
        }

        private async Task<int> InsertAsync(int chatId, string role, string text, string thinking,
            IEnumerable<(string Kind, string MediaType, byte[] Data)> attachments, string context, string transcript)
        {
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            var record = new ChatMessageRecord
            {
                ConversationId = chatId,
                Role = role,
                Text = text ?? string.Empty,
                Thinking = string.IsNullOrEmpty(thinking) ? null : thinking,
                Context = string.IsNullOrEmpty(context) ? null : context,
                Transcript = string.IsNullOrWhiteSpace(transcript) ? null : transcript.Trim(),
                Created = DateTime.UtcNow
            };
            await db.InsertAsync(record).ConfigureAwait(false);

            List<string> saved = new List<string>();
            int index = 0;
            foreach (var attachment in attachments ?? Enumerable.Empty<(string, string, byte[])>())
            {
                if (attachment.Data == null)
                    continue;
                string folder = Path.Combine(AttachmentFolder, chatId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                string name = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}_{1}{2}", record.Id, index++, Extension(attachment.MediaType));
                await WriteFileAsync(Path.Combine(folder, name), attachment.Data).ConfigureAwait(false);
                saved.Add(string.Join("|", attachment.Kind, attachment.MediaType, name));
            }
            if (saved.Count > 0)
            {
                record.Attachments = string.Join("\n", saved);
                await db.UpdateAsync(record).ConfigureAwait(false);
            }
            return record.Id;
        }

        private async Task TouchAsync(int chatId)
        {
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.ExecuteAsync("UPDATE Conversation SET Updated = ? WHERE Id = ?", DateTime.UtcNow.Ticks, chatId).ConfigureAwait(false);
        }

        // ---------- Loading ----------

        /// <summary>A chat's rows, in order (none while <see cref="IsPrivate"/>).</summary>
        /// <typeparam name="TMessage"><see cref="ChatMessageRecord"/>, or an app's record derived from it</typeparam>
        /// <param name="chatId">The chat</param>
        public async Task<List<TMessage>> GetMessagesAsync<TMessage>(int chatId) where TMessage : ChatMessageRecord, new()
        {
            if (IsPrivate)
                return new List<TMessage>();
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            return await db.Table<TMessage>().Where(m => m.ConversationId == chatId).OrderBy(m => m.Id).ToListAsync().ConfigureAwait(false);
        }

        /// <summary>Read a saved chat back, to show and continue it (<see cref="ChatHistory.Load"/>); null if there is none.</summary>
        /// <param name="chatId">The chat</param>
        public Task<ChatHistory> LoadAsync(int chatId)
        {
            return LoadAsync<ChatMessageRecord>(chatId);
        }

        /// <summary>
        /// Read a saved chat back with an app's own message record, whose columns <see cref="ChatHistory.RecordOf"/> then
        /// has; null if there is none.
        /// </summary>
        /// <typeparam name="TMessage">An app's record derived from <see cref="ChatMessageRecord"/></typeparam>
        /// <param name="chatId">The chat</param>
        public async Task<ChatHistory> LoadAsync<TMessage>(int chatId) where TMessage : ChatMessageRecord, new()
        {
            ChatRecord chat = await GetChatAsync(chatId).ConfigureAwait(false);
            if (chat == null)
                return null;
            List<TMessage> rows = await GetMessagesAsync<TMessage>(chatId).ConfigureAwait(false);
            var items = new List<ChatMessageItem>();
            var records = new Dictionary<ChatMessageItem, ChatMessageRecord>();
            var files = new Dictionary<ChatMessageItem, IReadOnlyList<ChatSavedAttachment>>();
            var steps = new List<string>();
            int contextStart = -1;
            foreach (TMessage row in rows)
            {
                if (row.Role == ChatMessageRecord.StepRole)
                {
                    steps.Add(row.Text);
                    continue;
                }
                // The model sees the messages after the last one it no longer sees.
                if (contextStart < 0 && row.Id > chat.SummaryUpTo)
                    contextStart = items.Count;
                List<ChatSavedAttachment> saved = GetAttachments(row);
                ChatMessageItem item;
                if (row.Role == ChatMessageRecord.UserRole)
                {
                    var attachments = new List<DataContent>();
                    foreach (ChatSavedAttachment file in saved.Where(f => f.Kind == ChatSavedAttachment.ImageKind || f.Kind == ChatSavedAttachment.AudioKind))
                        attachments.Add(new DataContent(await ReadFileAsync(file.Path).ConfigureAwait(false), file.MediaType) { Name = Path.GetFileName(file.Path) });
                    item = new ChatMessageItem(ChatMessageRole.User, row.Text, attachments, row.Context) { Transcript = row.Transcript };
                }
                else
                {
                    item = new ChatMessageItem(ChatMessageRole.Assistant, row.Text) { Thinking = row.Thinking ?? string.Empty };
                    foreach (string step in steps)
                        item.Steps.Add(new ChatToolStep(step));
                    steps.Clear();
                }
                Remember(item, row.Id);
                records[item] = row;
                files[item] = saved;
                items.Add(item);
            }
            string summary = chat.SummaryUpTo > 0 ? chat.Summary : null;
            return new ChatHistory(chat, items, summary, contextStart < 0 ? items.Count : contextStart, records, files);
        }

        /// <summary>The files saved with a row, with their full paths.</summary>
        /// <param name="message">The row</param>
        /// <param name="includeMissing">True to include files that are no longer there</param>
        public List<ChatSavedAttachment> GetAttachments(ChatMessageRecord message, bool includeMissing = false)
        {
            var result = new List<ChatSavedAttachment>();
            if (message == null || string.IsNullOrEmpty(message.Attachments))
                return result;
            foreach (string line in message.Attachments.Split('\n'))
            {
                string[] parts = line.Split('|');
                // Only a plain file name in the chat's folder.
                if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[2])
                    || parts[2].IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || parts[2] == "." || parts[2] == "..")
                    continue;
                string path = Path.Combine(AttachmentFolder, message.ConversationId.ToString(System.Globalization.CultureInfo.InvariantCulture), parts[2]);
                if (includeMissing || File.Exists(path))
                    result.Add(new ChatSavedAttachment(parts[0], parts[1], path));
            }
            return result;
        }

        // ---------- Helpers ----------

        /// <summary>A text on one line, at most this long (cut with "…").</summary>
        public static string Shorten(string text, int length)
        {
            text = (text ?? string.Empty).Replace('\n', ' ').Trim();
            return text.Length <= length ? text : text.Substring(0, length - 1).TrimEnd() + "…";
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
                default: return mediaType != null && mediaType.StartsWith("audio", StringComparison.Ordinal) ? ".wav" : ".bin";
            }
        }

        // netstandard2.0 has no File.WriteAllBytesAsync / ReadAllBytesAsync.
        private static async Task WriteFileAsync(string path, byte[] data)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadFileAsync(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                byte[] data = new byte[stream.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = await stream.ReadAsync(data, read, data.Length - read).ConfigureAwait(false);
                    if (n == 0)
                        break;
                    read += n;
                }
                return data;
            }
        }
    }
}
