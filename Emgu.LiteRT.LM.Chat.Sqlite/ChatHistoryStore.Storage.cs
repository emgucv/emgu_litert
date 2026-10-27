//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Chat;
using SQLite;

namespace Emgu.LiteRT.LM.Chat.Sqlite
{
    // What the storage catalog needs (IChatStorageStore): the sizes of what the store keeps, and deleting them.
    public partial class ChatHistoryStore : IChatStorageStore
    {
        /// <inheritdoc/>
        public async Task<List<ChatDocumentStorage>> GetDocumentStorageAsync()
        {
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            return await db.QueryAsync<ChatDocumentStorage>(
                "SELECT d.Id, d.Name, COALESCE(SUM(COALESCE(length(CAST(i.Text AS BLOB)),0) + COALESCE(length(i.Data),0) " +
                "+ COALESCE(length(i.Embedding),0) + COALESCE(length(CAST(i.Location AS BLOB)),0)),0) AS Bytes " +
                "FROM Document d LEFT JOIN DocumentItem i ON i.DocumentId = d.Id GROUP BY d.Id, d.Name").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<long> GetPhotoIndexBytesAsync()
        {
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            return await db.ExecuteScalarAsync<long>(
                "SELECT COALESCE(SUM(COALESCE(length(Embedding),0) + COALESCE(length(CAST(PhotoId AS BLOB)),0) " +
                "+ COALESCE(length(CAST(Place AS BLOB)),0) + COALESCE(length(CAST(Camera AS BLOB)),0)),0) FROM PhotoEmbedding").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<long> GetChatIndexBytesAsync()
        {
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            return await db.ExecuteScalarAsync<long>(
                "SELECT COALESCE(SUM(COALESCE(length(Embedding),0) + COALESCE(length(CAST(Text AS BLOB)),0)),0) FROM ChatEmbedding").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task DeleteAllAttachmentsAsync()
        {
            StorageFiles.DeleteDirectory(Folder, AttachmentFolder);
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.ExecuteAsync("UPDATE Message SET Attachments = NULL").ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task CompactAsync()
        {
            // SQLite rows share pages; compact after deletion so their freed space is returned to the device.
            SQLiteAsyncConnection db = await GetConnectionAsync().ConfigureAwait(false);
            await db.ExecuteAsync("VACUUM").ConfigureAwait(false);
        }
    }
}
