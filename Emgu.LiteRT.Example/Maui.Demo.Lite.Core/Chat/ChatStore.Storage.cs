// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Maui.Demo.Lite;

internal sealed class DocumentStorageInfo
{
    public string Id { get; set; }
    public string Name { get; set; }
    public long Bytes { get; set; }
}

internal sealed partial class ChatStore
{
    internal string StorageFolder => Folder;

    public async Task<List<DocumentStorageInfo>> GetDocumentStorageAsync()
    {
        return await (await Db).QueryAsync<DocumentStorageInfo>(
            "SELECT d.Id, d.Name, COALESCE(SUM(COALESCE(length(CAST(i.Text AS BLOB)),0) + COALESCE(length(i.Data),0) " +
            "+ COALESCE(length(i.Embedding),0) + COALESCE(length(CAST(i.Location AS BLOB)),0)),0) AS Bytes " +
            "FROM Document d LEFT JOIN DocumentItem i ON i.DocumentId = d.Id GROUP BY d.Id, d.Name");
    }

    public async Task<long> GetPhotoIndexBytesAsync() => await (await Db).ExecuteScalarAsync<long>(
        "SELECT COALESCE(SUM(COALESCE(length(Embedding),0) + COALESCE(length(CAST(PhotoId AS BLOB)),0) " +
        "+ COALESCE(length(CAST(Place AS BLOB)),0) + COALESCE(length(CAST(Camera AS BLOB)),0)),0) FROM PhotoEmbedding");

    public async Task<long> GetChatIndexBytesAsync() => await (await Db).ExecuteScalarAsync<long>(
        "SELECT COALESCE(SUM(COALESCE(length(Embedding),0) + COALESCE(length(CAST(Text AS BLOB)),0)),0) FROM ChatEmbedding");

    public async Task DeleteDocumentAsync(string id)
    {
        await (await Db).RunInTransactionAsync(connection =>
        {
            connection.Execute("UPDATE Conversation SET DocumentId = NULL WHERE DocumentId = ?", id);
            connection.Execute("DELETE FROM DocumentItem WHERE DocumentId = ?", id);
            connection.Delete<DocumentRecord>(id);
        });
    }

    public async Task DeleteAllAttachmentsAsync()
    {
        StorageFiles.DeleteDirectory(StorageFolder, AttachmentFolder);
        await (await Db).ExecuteAsync("UPDATE Message SET Attachments = NULL");
    }

    // SQLite rows share pages; compact after deletion so their freed space is returned to the device.
    public async Task CompactAsync() => await (await Db).ExecuteAsync("VACUUM");
}
