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
        Exports,
        /// <summary>"Search my videos"' index, in the database.</summary>
        VideoIndex,
        /// <summary>A speech recognition model's files (e.g. Parakeet's model and tokenizer).</summary>
        SpeechModel,
        /// <summary>An image model's file and caches (e.g. Bonsai Image's .litertlm).</summary>
        ImageModel
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

        /// <summary>Describe a speech recognition model: its model file and the files that go with it, deleted together.</summary>
        /// <param name="name">Its name, shown</param>
        /// <param name="file">Its model file's name in the models' folder (a .litertlm, or a .tflite)</param>
        /// <param name="companions">The other files' names in the models' folder (e.g. a tokenizer, or the files an
        /// older version used, so they are counted and deleted with it)</param>
        /// <param name="detail">What deleting it does, shown</param>
        /// <param name="cacheNames">Names its compiled-model caches start with when they are named after the model
        /// rather than the file (LiteRT-LM's speech models: "parakeet-tdt-0.6b-v3_..._.xnnpack_cache"); only cache
        /// files match</param>
        public static StorageModel Speech(string name, string file, IEnumerable<string> companions, string detail,
            IEnumerable<string> cacheNames = null) =>
            new StorageModel(name, file)
            {
                IsSpeech = true, Companions = (companions ?? Enumerable.Empty<string>()).ToList(), Detail = detail,
                CacheNames = (cacheNames ?? Enumerable.Empty<string>()).ToList()
            };

        /// <summary>
        /// Describe an image model on LiteRT-LM (a .litertlm in the models' folder, with its caches in a folder of
        /// their own beside it), its own row rather than a chat model's.
        /// </summary>
        /// <param name="name">Its name, shown</param>
        /// <param name="file">Its model file's name in the models' folder (a .litertlm)</param>
        /// <param name="detail">What deleting it does, shown</param>
        /// <param name="cacheFolder">Its caches' folder's name in the models' folder (e.g. "bonsai-flux2-cache"), deleted with it, or null</param>
        public static StorageModel Image(string name, string file, string detail, string cacheFolder = null) =>
            new StorageModel(name, file) { IsImage = true, Detail = detail, CacheFolder = cacheFolder };

        /// <summary>An image model's caches' folder in the models' folder, or null.</summary>
        public string CacheFolder { get; private set; }

        /// <summary>True for a speech recognition model (<see cref="Speech"/>).</summary>
        public bool IsSpeech { get; private set; }

        /// <summary>True for an image model (<see cref="Image"/>).</summary>
        public bool IsImage { get; private set; }

        // A speech or image model: its own row, with its companions and caches, never listed as a chat model.
        internal bool HasOwnRow => IsSpeech || IsImage;

        /// <summary>The other files deleted with it (a speech model's tokenizer or older files).</summary>
        public IReadOnlyList<string> Companions { get; private set; } = new string[0];

        /// <summary>Names its caches start with when named after the model (a speech model's).</summary>
        public IReadOnlyList<string> CacheNames { get; private set; } = new string[0];

        /// <summary>What deleting it does, shown; null for the usual text.</summary>
        public string Detail { get; private set; }

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
            var models = _models.Where(m => !m.HasOwnRow).Select(m => (m.Name, m.File, m.IsSearch)).ToList();
            // A speech or image model's .litertlm is its own row, not an unknown chat model's.
            var speechFiles = new HashSet<string>(_models.Where(m => m.HasOwnRow).Select(m => m.File), StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(ModelRoot))
            {
                // Include older downloads and caches left behind by models no longer in the picker.
                foreach (string path in Directory.EnumerateFiles(ModelRoot))
                {
                    string name = Path.GetFileName(path);
                    int end = name.IndexOf(".litertlm", StringComparison.OrdinalIgnoreCase);
                    if (end < 0) continue;
                    string file = name.Substring(0, end + ".litertlm".Length);
                    if (speechFiles.Contains(file) || models.Any(m => string.Equals(m.File, file, StringComparison.OrdinalIgnoreCase))) continue;
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
            foreach (StorageModel model in _models.Where(m => m.HasOwnRow))
                result.Entries.Add(new StorageEntry
                {
                    Kind = model.IsImage ? StorageKind.ImageModel : StorageKind.SpeechModel, Key = model.File, Name = model.Name,
                    Bytes = SpeechFiles(model).Sum(StorageFiles.Size) + (CacheFolderPath(model) is string caches ? StorageFiles.Size(caches) : 0),
                    Detail = model.Detail ?? (model.IsImage ? "Image model and its caches. Downloaded again when it is chosen."
                        : "Speech recognition model. Downloaded again when it is used.")
                });
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
                Kind = StorageKind.VideoIndex, Name = "Video search index", InDatabase = true,
                Bytes = await _store.GetVideoIndexBytesAsync().ConfigureAwait(false), Detail = "Deletes embeddings and turns video search off. Your videos are kept."
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
                case StorageKind.SpeechModel:
                case StorageKind.ImageModel:
                    StorageModel speech = _models.FirstOrDefault(m => m.HasOwnRow && m.File == entry.Key
                            && m.IsImage == (entry.Kind == StorageKind.ImageModel))
                        ?? throw new InvalidOperationException("Unknown speech or image model.");
                    foreach (string file in new[] { speech.File }.Concat(speech.Companions))
                    {
                        ValidateModelName(file, speech: true);
                        StorageFiles.DeleteModel(ModelRoot, Path.Combine(ModelRoot, file));
                    }
                    foreach (string cache in SpeechCaches(speech))
                        File.Delete(cache);
                    if (CacheFolderPath(speech) is string cacheFolder && Directory.Exists(cacheFolder))
                        StorageFiles.DeleteDirectory(ModelRoot, cacheFolder);
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
                case StorageKind.VideoIndex: await _store.ClearVideoIndexAsync().ConfigureAwait(false); break;
                case StorageKind.ChatIndex: await _store.ClearChatEmbeddingsAsync().ConfigureAwait(false); break;
                case StorageKind.Attachments: await _store.DeleteAllAttachmentsAsync().ConfigureAwait(false); break;
                default: throw new InvalidOperationException("This storage item cannot be deleted here.");
            }
            await _store.CompactAsync().ConfigureAwait(false);
        }

        // A speech or image model's files, each with its partial download and marker, and its caches.
        private List<string> SpeechFiles(StorageModel model)
        {
            var files = new List<string>();
            foreach (string file in new[] { model.File }.Concat(model.Companions))
            {
                ValidateModelName(file, speech: true);
                files.AddRange(StorageFiles.ModelFiles(Path.Combine(ModelRoot, file)));
            }
            files.AddRange(SpeechCaches(model).Where(cache => !files.Contains(cache)));
            return files;
        }

        // An image model's caches' folder, right in the models' folder; null when it has none.
        private string CacheFolderPath(StorageModel model)
        {
            if (model.CacheFolder == null)
                return null;
            if (string.IsNullOrWhiteSpace(model.CacheFolder) || model.CacheFolder.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || model.CacheFolder.StartsWith(".", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid cache folder name.");
            return Path.Combine(ModelRoot, model.CacheFolder);
        }

        // The caches named after a speech model (its CacheNames) rather than its file: cache files only.
        private List<string> SpeechCaches(StorageModel model)
        {
            var caches = new List<string>();
            foreach (string cacheName in model.CacheNames)
            {
                if (string.IsNullOrWhiteSpace(cacheName) || cacheName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                    throw new InvalidOperationException("Invalid cache name.");
                caches.AddRange(StorageFiles.ModelFiles(Path.Combine(ModelRoot, cacheName))
                    .Where(file => !string.Equals(Path.GetFileName(file), cacheName, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(file).StartsWith(cacheName + "_", StringComparison.OrdinalIgnoreCase)));
            }
            return caches;
        }

        // A file name right in the models' folder: a .litertlm, or for a speech model also a .tflite or a tokenizer's .json.
        private static void ValidateModelName(string name, bool speech = false)
        {
            string[] extensions = speech ? new[] { ".litertlm", ".tflite", ".json" } : new[] { ".litertlm" };
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0
                || !extensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Invalid model file name.");
        }
    }
}
