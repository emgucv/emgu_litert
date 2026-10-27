//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>What a <see cref="StorageEntry"/> is.</summary>
    public enum StorageKind
    {
        /// <summary>A chat model's file, with its partial downloads and caches.</summary>
        ChatModel,
        /// <summary>A search (embedding) model's file, with its partial downloads and caches.</summary>
        SearchModel,
        /// <summary>A folder of the app's own (<see cref="StorageFolder"/>), e.g. an image model's.</summary>
        Folder,
        /// <summary>A saved document's text, images and embeddings, in the database.</summary>
        Document,
        /// <summary>The photo search's index, in the database.</summary>
        PhotoIndex,
        /// <summary>The past chats' search index, in the database.</summary>
        ChatIndex,
        /// <summary>Every chat's attachment files.</summary>
        Attachments,
        /// <summary>The database file itself (not deletable here).</summary>
        Database,
        /// <summary>The ZIPs made for sharing chats.</summary>
        Exports
    }

    /// <summary>One thing the app keeps on the device, with its size.</summary>
    public sealed class StorageEntry
    {
        /// <summary>What it is.</summary>
        public StorageKind Kind { get; set; }

        /// <summary>What tells it apart: a model's file name, a folder's key, a document's id; null for the one-of-a-kind entries.</summary>
        public string Key { get; set; }

        /// <summary>Its name, shown.</summary>
        public string Name { get; set; }

        /// <summary>What it holds and what deleting it does, shown.</summary>
        public string Detail { get; set; }

        /// <summary>Its size: on disk, or an estimate of the rows for an entry in the database.</summary>
        public long Bytes { get; set; }

        /// <summary>True for an entry in the database (counted once, in the <see cref="StorageKind.Database"/> entry's size).</summary>
        public bool InDatabase { get; set; }

        /// <summary>True when it can be deleted.</summary>
        public bool CanDelete { get; set; } = true;

        /// <summary>True for a search model that was deleted and can be downloaded again.</summary>
        public bool CanDownload { get; set; }
    }

    /// <summary>What the app keeps, read at one moment.</summary>
    public sealed class StorageSnapshot
    {
        /// <summary>The entries, in the order shown.</summary>
        public List<StorageEntry> Entries { get; } = new List<StorageEntry>();

        /// <summary>What they take on disk (the database once).</summary>
        public long DiskBytes => Entries.Where(e => !e.InDatabase).Sum(e => e.Bytes);
    }

    /// <summary>A model the app offers, for the catalog: a LiteRT-LM file in the models' folder.</summary>
    public sealed class StorageModel
    {
        /// <summary>Describe the model.</summary>
        /// <param name="name">Its name, shown</param>
        /// <param name="file">Its file name in the models' folder (a .litertlm)</param>
        /// <param name="isSearch">True for a search (embedding) model</param>
        public StorageModel(string name, string file, bool isSearch = false)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            File = file ?? throw new ArgumentNullException(nameof(file));
            IsSearch = isSearch;
        }

        /// <summary>Its name, shown.</summary>
        public string Name { get; }

        /// <summary>Its file name in the models' folder.</summary>
        public string File { get; }

        /// <summary>True for a search (embedding) model, which the storage page can download again once deleted.</summary>
        public bool IsSearch { get; }

        /// <summary>The download of a search model, for the storage page's Download button; null when it can't be downloaded there.</summary>
        public DownloadableFile Download { get; set; }
    }

    /// <summary>A folder of the app's own in the catalog (an image model's, a cache), deleted whole.</summary>
    public sealed class StorageFolder
    {
        /// <summary>Describe the folder.</summary>
        /// <param name="key">What tells it apart (<see cref="StorageEntry.Key"/>), e.g. "image"</param>
        /// <param name="name">Its name, shown</param>
        /// <param name="path">The folder</param>
        /// <param name="detail">What it holds and what deleting it does, shown</param>
        public StorageFolder(string key, string name, string path, string detail)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Path = System.IO.Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            Detail = detail;
        }

        /// <summary>What tells it apart.</summary>
        public string Key { get; }

        /// <summary>Its name, shown.</summary>
        public string Name { get; }

        /// <summary>The folder.</summary>
        public string Path { get; }

        /// <summary>What it holds and what deleting it does, shown.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// What the app keeps on the device, with sizes, and how to delete each thing: the models' folder (the app's models
    /// and any other LiteRT-LM file left there), the app's own folders, the saved documents, the search indexes, the
    /// attachments, the database and the shared exports (<see cref="ReadAsync"/>); each deletable with
    /// <see cref="DeleteAsync"/>, inside its root only. The store's part comes through <see cref="IChatStorageStore"/>.
    /// </summary>
    public sealed class StorageCatalog
    {
        private readonly IChatStorageStore _store;
        private readonly string _exportFolder;
        private readonly List<StorageModel> _models;
        private readonly List<StorageFolder> _folders;

        /// <summary>Create the catalog.</summary>
        /// <param name="store">The chats' store</param>
        /// <param name="modelRoot">The folder the models are downloaded to</param>
        /// <param name="exportFolder">The folder the chat exports are made in</param>
        /// <param name="models">The app's models</param>
        /// <param name="folders">The app's own folders, or null</param>
        public StorageCatalog(IChatStorageStore store, string modelRoot, string exportFolder,
            IEnumerable<StorageModel> models, IEnumerable<StorageFolder> folders = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            ModelRoot = Path.GetFullPath(modelRoot ?? throw new ArgumentNullException(nameof(modelRoot)));
            _exportFolder = Path.GetFullPath(exportFolder ?? throw new ArgumentNullException(nameof(exportFolder)));
            _models = (models ?? throw new ArgumentNullException(nameof(models))).ToList();
            _folders = (folders ?? Enumerable.Empty<StorageFolder>()).ToList();
        }

        /// <summary>The folder the models are downloaded to.</summary>
        public string ModelRoot { get; }

        /// <summary>The app's models.</summary>
        public IReadOnlyList<StorageModel> Models => _models;

        /// <summary>The app's own folders.</summary>
        public IReadOnlyList<StorageFolder> Folders => _folders;

        /// <summary>Read what is kept, with sizes.</summary>
        public async Task<StorageSnapshot> ReadAsync()
        {
            var result = new StorageSnapshot();
            var models = _models.Select(m => (m.Name, m.File, m.IsSearch)).ToList();
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
                List<string> files = StorageFiles.ModelFiles(Path.Combine(ModelRoot, model.File));
                long bytes = files.Sum(StorageFiles.Size);
                result.Entries.Add(new StorageEntry
                {
                    Kind = model.IsSearch ? StorageKind.SearchModel : StorageKind.ChatModel,
                    Key = model.File, Name = model.Name, Bytes = bytes,
                    CanDownload = model.IsSearch && _models.Any(m => m.File == model.File && m.Download != null)
                        && !File.Exists(Path.Combine(ModelRoot, model.File)),
                    Detail = "Model, partial downloads, and its CPU/GPU caches. Download again to use after deletion."
                });
            }
            foreach (StorageFolder folder in _folders)
                result.Entries.Add(new StorageEntry { Kind = StorageKind.Folder, Key = folder.Key, Name = folder.Name, Bytes = StorageFiles.Size(folder.Path), Detail = folder.Detail });
            foreach (ChatDocumentStorage document in await _store.GetDocumentStorageAsync().ConfigureAwait(false))
                result.Entries.Add(new StorageEntry
                {
                    Kind = StorageKind.Document, Key = document.Id, Name = "Document · " + document.Name, Bytes = document.Bytes, InDatabase = true,
                    Detail = "Stored text, images and embeddings. Detaches this document from saved chats; chat text and saved excerpts remain."
                });
            result.Entries.Add(new StorageEntry
            {
                Kind = StorageKind.PhotoIndex, Name = "Photo search index", InDatabase = true,
                Bytes = await _store.GetPhotoIndexBytesAsync().ConfigureAwait(false), Detail = "Deletes embeddings and turns photo search off. Your photo library is kept."
            });
            result.Entries.Add(new StorageEntry
            {
                Kind = StorageKind.ChatIndex, Name = "Past-chat search index", InDatabase = true,
                Bytes = await _store.GetChatIndexBytesAsync().ConfigureAwait(false), Detail = "Deletes embeddings and turns past-chat search off. Saved conversations are kept."
            });
            result.Entries.Add(new StorageEntry
            {
                Kind = StorageKind.Attachments, Name = "Chat attachments", Bytes = StorageFiles.Size(_store.AttachmentFolder),
                Detail = "Photos, generated pictures and recordings from all saved chats. Deleting keeps chat text but removes these files."
            });
            string db = Path.Combine(_store.Folder, "chats.db3");
            result.Entries.Add(new StorageEntry
            {
                Kind = StorageKind.Database, Name = "Chat database", CanDelete = false,
                Bytes = new[] { db, db + "-wal", db + "-shm", db + "-journal" }.Sum(StorageFiles.Size),
                Detail = "Disk size, including chat text, memories, documents and indexes. Database item sizes above are stored-data estimates, already included here. Delete chats from the chat list."
            });
            result.Entries.Add(new StorageEntry
            {
                Kind = StorageKind.Exports, Name = "Shared chat exports", Bytes = StorageFiles.Size(_exportFolder),
                Detail = "Temporary ZIP copies made for sharing. Deleting does not remove saved chats or files already saved by another app."
            });
            return result;
        }

        /// <summary>Delete an entry (the database can't be), compacting the database after a deletion in it.</summary>
        /// <param name="entry">The entry</param>
        public async Task DeleteAsync(StorageEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            switch (entry.Kind)
            {
                case StorageKind.ChatModel:
                case StorageKind.SearchModel:
                    ValidateModelName(entry.Key);
                    StorageFiles.DeleteModel(ModelRoot, Path.Combine(ModelRoot, entry.Key));
                    return;
                case StorageKind.Folder:
                    StorageFolder folder = _folders.FirstOrDefault(f => f.Key == entry.Key)
                        ?? throw new InvalidOperationException("Unknown storage folder.");
                    StorageFiles.DeleteDirectory(Path.GetDirectoryName(folder.Path), folder.Path);
                    return;
                case StorageKind.Exports:
                    StorageFiles.DeleteDirectory(Path.GetDirectoryName(_exportFolder), _exportFolder);
                    return;
                case StorageKind.Document: await _store.DeleteDocumentAsync(entry.Key).ConfigureAwait(false); break;
                case StorageKind.PhotoIndex: await _store.ClearPhotoEmbeddingsAsync().ConfigureAwait(false); break;
                case StorageKind.ChatIndex: await _store.ClearChatEmbeddingsAsync().ConfigureAwait(false); break;
                case StorageKind.Attachments: await _store.DeleteAllAttachmentsAsync().ConfigureAwait(false); break;
                default: throw new InvalidOperationException("This storage item cannot be deleted here.");
            }
            await _store.CompactAsync().ConfigureAwait(false);
        }

        private static void ValidateModelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || !name.EndsWith(".litertlm", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid model file name.");
        }
    }
}
