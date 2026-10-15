// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    internal enum StorageKind { ChatModel, SearchModel, ImageModel, Document, PhotoIndex, ChatIndex, Attachments, Database, Exports }
    internal sealed class StorageEntry
    {
        public StorageKind Kind;
        public string Key;
        public string Name;
        public string Detail;
        public long Bytes;
        public bool InDatabase;
        public bool CanDelete = true;
        public bool CanDownload;
    }
    internal sealed class StorageSnapshot
    {
        public List<StorageEntry> Entries = new();
        public long DiskBytes => Entries.Where(e => !e.InDatabase).Sum(e => e.Bytes);
    }

    internal sealed class StorageCatalog
    {
        private readonly ChatStore _store;
        internal readonly string ModelRoot;
        private readonly string _imageFolder;
        private readonly string _exportFolder;
        private readonly List<(string Name, string File, bool Search)> _models;

        public StorageCatalog(ChatStore store, string modelRoot, string imageFolder, string exportFolder,
            IEnumerable<(string Name, string File, bool Search)> models)
        {
            _store = store;
            ModelRoot = Path.GetFullPath(modelRoot);
            _imageFolder = Path.GetFullPath(imageFolder);
            _exportFolder = Path.GetFullPath(exportFolder);
            _models = models.ToList();
        }

        public async Task<StorageSnapshot> ReadAsync()
        {
            var result = new StorageSnapshot();
            var models = _models.ToList();
            if (Directory.Exists(ModelRoot))
            {
                // Include older downloads and caches left behind by models no longer in the picker.
                foreach (string path in Directory.EnumerateFiles(ModelRoot))
                {
                    string name = Path.GetFileName(path);
                    int end = name.IndexOf(".litertlm", StringComparison.OrdinalIgnoreCase);
                    if (end < 0) continue;
                    string file = name.Substring(0, end + ".litertlm".Length);
                    if (models.Any(m => string.Equals(m.File, file, StringComparison.OrdinalIgnoreCase))) continue;
                    models.Add((file, file, file.StartsWith("embedding", StringComparison.OrdinalIgnoreCase)));
                }
            }
            foreach (var model in models)
            {
                ValidateModelName(model.File);
                var files = StorageFiles.ModelFiles(Path.Combine(ModelRoot, model.File));
                long bytes = files.Sum(StorageFiles.Size);
                result.Entries.Add(new StorageEntry { Kind = model.Search ? StorageKind.SearchModel : StorageKind.ChatModel,
                    Key = model.File, Name = model.Name, Bytes = bytes,
                    CanDownload = model.Search && _models.Any(m => m.File == model.File)
                        && !File.Exists(Path.Combine(ModelRoot, model.File)),
                    Detail = "Model, partial downloads, and its CPU/GPU caches. Download again to use after deletion." });
            }
            result.Entries.Add(new StorageEntry { Kind = StorageKind.ImageModel, Name = "Z-Image-Turbo", Bytes = StorageFiles.Size(_imageFolder),
                Detail = "Image model, host tensors, and prepared weight caches. Deleting turns image generation off." });
            foreach (var document in await _store.GetDocumentStorageAsync())
                result.Entries.Add(new StorageEntry { Kind = StorageKind.Document, Key = document.Id, Name = "Document · " + document.Name,
                    Bytes = document.Bytes, InDatabase = true, Detail = "Stored text, images and embeddings. Detaches this document from saved chats; chat text and saved excerpts remain." });
            result.Entries.Add(new StorageEntry { Kind = StorageKind.PhotoIndex, Name = "Photo search index", InDatabase = true,
                Bytes = await _store.GetPhotoIndexBytesAsync(), Detail = "Deletes embeddings and turns photo search off. Your photo library is kept." });
            result.Entries.Add(new StorageEntry { Kind = StorageKind.ChatIndex, Name = "Past-chat search index", InDatabase = true,
                Bytes = await _store.GetChatIndexBytesAsync(), Detail = "Deletes embeddings and turns past-chat search off. Saved conversations are kept." });
            result.Entries.Add(new StorageEntry { Kind = StorageKind.Attachments, Name = "Chat attachments", Bytes = StorageFiles.Size(_store.AttachmentFolder),
                Detail = "Photos, generated pictures and recordings from all saved chats. Deleting keeps chat text but removes these files." });
            string db = Path.Combine(_store.StorageFolder, "chats.db3");
            result.Entries.Add(new StorageEntry { Kind = StorageKind.Database, Name = "Chat database", CanDelete = false,
                Bytes = new[] { db, db + "-wal", db + "-shm", db + "-journal" }.Sum(StorageFiles.Size),
                Detail = "Disk size, including chat text, memories, documents and indexes. Database item sizes above are stored-data estimates, already included here. Delete chats from the chat list." });
            result.Entries.Add(new StorageEntry { Kind = StorageKind.Exports, Name = "Shared chat exports", Bytes = StorageFiles.Size(_exportFolder),
                Detail = "Temporary ZIP copies made for sharing. Deleting does not remove saved chats or files already saved by another app." });
            return result;
        }

        public async Task DeleteAsync(StorageEntry entry)
        {
            switch (entry.Kind)
            {
                case StorageKind.ChatModel:
                case StorageKind.SearchModel:
                    ValidateModelName(entry.Key);
                    StorageFiles.DeleteModel(ModelRoot, Path.Combine(ModelRoot, entry.Key));
                    return;
                case StorageKind.ImageModel:
                    StorageFiles.DeleteDirectory(Path.GetDirectoryName(_imageFolder), _imageFolder);
                    return;
                case StorageKind.Exports:
                    StorageFiles.DeleteDirectory(Path.GetDirectoryName(_exportFolder), _exportFolder);
                    return;
                case StorageKind.Document: await _store.DeleteDocumentAsync(entry.Key); break;
                case StorageKind.PhotoIndex: await _store.ClearPhotoEmbeddingsAsync(); break;
                case StorageKind.ChatIndex: await _store.ClearChatEmbeddingsAsync(); break;
                case StorageKind.Attachments: await _store.DeleteAllAttachmentsAsync(); break;
                default: throw new InvalidOperationException("This storage item cannot be deleted here.");
            }
            await _store.CompactAsync();
        }

        private static void ValidateModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || !name.EndsWith(".litertlm", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid model file name.");
        }
    }
}
