//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// A photo the index knows: its embedding and details.
    /// </summary>
    internal sealed class IndexedPhoto
    {
        public string Id;
        public DateTimeOffset Taken;
        public float[] Embedding;
        public double? Latitude;
        public double? Longitude;
        public string Place;
        public string Camera;
        public int Width;
        public int Height;
        public HashSet<string> Kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The chat's photo search: the most recent photos (PhotoLibrary), each turned into an embedding by an image embedding
    /// model (EmbeddingGemma 2 Text Vision) in the same space as text, with its details - when and where it was taken
    /// (the nearest town, from the offline place list, PlaceLookup), the camera, its size and kind - kept in the chat
    /// database (ChatStore) so only new or edited photos are processed again, and searched by a description and/or the
    /// details (PhotoQuery).
    /// </summary>
    internal sealed class PhotoIndex
    {
        // Photos are embedded from images this size, with EmbeddingGemma 2's larger vision encoder (140 tokens, the
        // model's default) for the best quality. 70 tokens was 2.3 times faster on an M1's CPU (461 vs 1041 ms per photo)
        // and matched 6 of 6 test photos as well, but sees less detail. Changing it re-embeds every photo (VisionTokens is
        // kept with each embedding).
        private const int EmbedImageSize = 512;
        private const int VisionTokens = 140;
        // Raised when what LoadDetailsAsync reads changes: the details are read again (without embedding again).
        private const int DetailsVersion = 3;
        // A photo is shown if it matches the description at least this well, and not much worse than the best match
        // (text-to-photo cosine similarity with EmbeddingGemma 2: 0.67-0.79 for the right photo of six, 0.58-0.63 for
        // the best wrong one; within 0.06 let three screenshots of shapes through for "a piece of fruit").
        private const float MinScore = 0.6f;
        private const float MaxBelowBest = 0.04f;
        private const int MaxResults = 8;
        // Without a description (only place, dates...), the most recent matches.
        private const int MaxListed = 12;

        private static Task<PlaceLookup> _places;

        private readonly ChatStore _store;
        private readonly List<IndexedPhoto> _entries = new List<IndexedPhoto>();
        private bool _loaded;

        public PhotoIndex(ChatStore store)
        {
            _store = store;
        }

        public int Count
        {
            get { lock (_entries) return _entries.Count; }
        }

        public int WithPlaceCount
        {
            get { lock (_entries) return _entries.Count(e => e.Place != null); }
        }

        /// <summary>The places of the indexed photos ("Town, Region, Country"), for recognising them in requests</summary>
        public List<string> KnownPlaces()
        {
            lock (_entries)
                return _entries.Where(e => e.Place != null).Select(e => e.Place).Distinct().ToList();
        }

        /// <summary>The cameras of the indexed photos, for recognising them in requests</summary>
        public List<string> KnownCameras()
        {
            lock (_entries)
                return _entries.Where(e => e.Camera != null).Select(e => e.Camera).Distinct().ToList();
        }

        // The photos found to index in the last run.
        public int Total { get; private set; }

        public bool IsIndexing { get; private set; }

        /// <summary>
        /// Raised on the indexing thread after each photo, and when indexing starts and stops.
        /// </summary>
        public event Action Changed;

        // The offline place list (Resources/Raw/places.tsv.gz: GeoNames' towns of 5,000+ people), loaded once.
        private static Task<PlaceLookup> Places()
        {
            if (_places == null || _places.IsFaulted)
                _places = Task.Run(async () =>
                {
                    using System.IO.Stream stream = await FileSystem.OpenAppPackageFileAsync("places.tsv.gz");
                    using System.IO.MemoryStream copy = new System.IO.MemoryStream();
                    await stream.CopyToAsync(copy);
                    copy.Position = 0;
                    return PlaceLookup.Load(copy);
                });
            return _places;
        }

        private async Task LoadAsync()
        {
            if (_loaded)
                return;
            List<PhotoEmbeddingRecord> records = await _store.GetPhotoEmbeddingsAsync();
            lock (_entries)
            {
                _entries.Clear();
                // Embeddings made with another vision encoder size are in a slightly different space: they are made again.
                foreach (PhotoEmbeddingRecord record in records.Where(r => r.VisionTokens == VisionTokens))
                    _entries.Add(ToEntry(record));
            }
            _loaded = true;
        }

        private static IndexedPhoto ToEntry(PhotoEmbeddingRecord record)
        {
            IndexedPhoto entry = new IndexedPhoto
            {
                Id = record.PhotoId,
                Taken = new DateTimeOffset(DateTime.SpecifyKind(record.Taken, DateTimeKind.Utc)),
                Embedding = ToFloats(record.Embedding),
                Latitude = record.Latitude,
                Longitude = record.Longitude,
                Place = record.Place,
                Camera = record.Camera,
                Width = record.Width,
                Height = record.Height
            };
            foreach (string kind in (record.Kinds ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                entry.Kinds.Add(kind);
            return entry;
        }

        /// <summary>
        /// Index the most recent photos that aren't yet (or were edited since, or were indexed with older settings), and
        /// forget the ones no longer among them.
        /// </summary>
        /// <param name="embedder">The image embedding model</param>
        /// <param name="gate">Held while the model embeds a photo: the model is shared with document search</param>
        /// <param name="maxPhotos">How many of the most recent photos to keep indexed</param>
        /// <param name="waitWhile">Indexing pauses while this is true (e.g. while the chat is answering)</param>
        public async Task IndexAsync(EmbeddingModel embedder, SemaphoreSlim gate, int maxPhotos, Func<bool> waitWhile,
            CancellationToken cancellationToken)
        {
            IsIndexing = true;
            Changed?.Invoke();
            try
            {
                await LoadAsync();
                PlaceLookup places = null;
                try { places = await Places(); } catch (Exception) { }
                List<PhotoItem> photos = await PhotoLibrary.GetRecentPhotosAsync(maxPhotos);
                Total = photos.Count;
                Dictionary<string, PhotoEmbeddingRecord> known = (await _store.GetPhotoEmbeddingsAsync()).ToDictionary(r => r.PhotoId);
                HashSet<string> current = new HashSet<string>(photos.Select(p => p.Id));
                List<string> gone = known.Keys.Where(id => !current.Contains(id)).ToList();
                if (gone.Count > 0)
                {
                    await _store.DeletePhotoEmbeddingsAsync(gone);
                    lock (_entries)
                        _entries.RemoveAll(e => !current.Contains(e.Id));
                }
                Changed?.Invoke();
                foreach (PhotoItem photo in photos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    known.TryGetValue(photo.Id, out PhotoEmbeddingRecord record);
                    bool embeddingCurrent = record != null && record.Version == photo.Version && record.VisionTokens == VisionTokens;
                    if (embeddingCurrent && record.DetailsVersion == DetailsVersion)
                        continue;
                    while (waitWhile != null && waitWhile())
                        await Task.Delay(1000, cancellationToken);

                    await PhotoLibrary.LoadDetailsAsync(photo);
                    string place = null;
                    if (places != null && photo.Latitude.HasValue && photo.Longitude.HasValue)
                        place = places.FindNearest(photo.Latitude.Value, photo.Longitude.Value)?.ToString();

                    byte[] embedding = embeddingCurrent ? record.Embedding : null;
                    if (embedding == null)
                    {
                        byte[] image = await PhotoLibrary.LoadImageAsync(photo.Id, EmbedImageSize);
                        if (image == null)
                            continue;
                        float[] values;
                        await gate.WaitAsync(cancellationToken);
                        try
                        {
                            values = await Task.Run(() => embedder.EmbedImage(image, visionTokensPerImage: VisionTokens), cancellationToken);
                        }
                        finally
                        {
                            gate.Release();
                        }
                        embedding = ToBytes(values);
                    }
                    PhotoEmbeddingRecord updated = new PhotoEmbeddingRecord
                    {
                        PhotoId = photo.Id,
                        Version = photo.Version,
                        Taken = photo.Taken.UtcDateTime,
                        VisionTokens = VisionTokens,
                        Embedding = embedding,
                        DetailsVersion = DetailsVersion,
                        Latitude = photo.Latitude,
                        Longitude = photo.Longitude,
                        Place = place,
                        Camera = photo.Camera,
                        Width = photo.Width,
                        Height = photo.Height,
                        Kinds = photo.Kinds.Count > 0 ? string.Join(",", photo.Kinds.OrderBy(k => k)) : null
                    };
                    await _store.SavePhotoEmbeddingAsync(updated);
                    IndexedPhoto entry = ToEntry(updated);
                    lock (_entries)
                    {
                        _entries.RemoveAll(e => e.Id == photo.Id);
                        _entries.Add(entry);
                    }
                    Changed?.Invoke();
                }
            }
            finally
            {
                IsIndexing = false;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// The photos that match a query: those with its details (place, dates, kind, camera), and of those the ones that
        /// match its description, the best first - or, without a description, the most recent. Matching is how many photos
        /// had the query's details, before the description.
        /// </summary>
        public async Task<(List<(IndexedPhoto Photo, float Score)> Found, int Matching)> SearchAsync(EmbeddingModel embedder,
            SemaphoreSlim gate, PhotoQuery query, CancellationToken cancellationToken)
        {
            await LoadAsync();
            List<IndexedPhoto> candidates;
            lock (_entries)
                candidates = _entries.Where(e => MatchesDetails(e, query)).ToList();
            if (candidates.Count == 0)
                return (new List<(IndexedPhoto, float)>(), 0);
            if (query.Description == null)
                return (candidates.OrderByDescending(e => e.Taken).Take(MaxListed).Select(e => (e, 0f)).ToList(), candidates.Count);

            float[] text;
            await gate.WaitAsync(cancellationToken);
            try
            {
                text = await Task.Run(() => embedder.EmbedQuery(query.Description), cancellationToken);
            }
            finally
            {
                gate.Release();
            }
            List<(IndexedPhoto Photo, float Score)> scored = candidates
                .Select(e => (e, EmbeddingModel.CosineSimilarity(text, e.Embedding))).ToList();
            float best = scored.Max(s => s.Item2);
            return (scored.Where(s => s.Item2 >= MinScore && s.Item2 >= best - MaxBelowBest)
                .OrderByDescending(s => s.Item2).Take(MaxResults).ToList(), candidates.Count);
        }

        // Common ways of naming countries the place list spells otherwise.
        private static readonly Dictionary<string, string> CountryAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "USA", "United States" }, { "US", "United States" }, { "U.S.", "United States" }, { "U.S.A.", "United States" },
            { "America", "United States" }, { "UK", "United Kingdom" }, { "U.K.", "United Kingdom" }, { "Britain", "United Kingdom" },
            { "Great Britain", "United Kingdom" }, { "Holland", "Netherlands" }, { "the Netherlands", "Netherlands" },
            { "Czech Republic", "Czechia" }, { "Korea", "South Korea" }, { "UAE", "United Arab Emirates" }
        };

        private static bool MatchesDetails(IndexedPhoto photo, PhotoQuery query)
        {
            if (query.From.HasValue && photo.Taken.LocalDateTime < query.From.Value)
                return false;
            if (query.To.HasValue && photo.Taken.LocalDateTime > query.To.Value)
                return false;
            if (query.Place != null)
            {
                string place = CountryAliases.TryGetValue(query.Place.Trim(), out string country) ? country : query.Place;
                if (!PlaceLookup.Matches(photo.Place, place))
                    return false;
            }
            if (query.Kind != null)
            {
                string kind = query.Kind.Trim().ToLowerInvariant().TrimEnd('s');
                if (kind == "favourite")
                    kind = "favorite";
                if (!photo.Kinds.Contains(kind))
                    return false;
            }
            if (query.Camera != null && (photo.Camera == null || photo.Camera.IndexOf(query.Camera.Trim(), StringComparison.OrdinalIgnoreCase) < 0))
                return false;
            return true;
        }

        /// <summary>
        /// Forget every photo (when photo search is turned off).
        /// </summary>
        public async Task ClearAsync()
        {
            await _store.ClearPhotoEmbeddingsAsync();
            lock (_entries)
                _entries.Clear();
            Total = 0;
            Changed?.Invoke();
        }

        private static byte[] ToBytes(float[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static float[] ToFloats(byte[] bytes)
        {
            float[] values = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
            return values;
        }
    }
}

#endif
