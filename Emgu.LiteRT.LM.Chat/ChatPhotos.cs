//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// A photo's embedding, for the chat's photo search: its id in the photo library, the version it was computed from,
    /// when it was taken, the embedding (the floats' bytes), and the photo's details read when it was indexed.
    /// </summary>
    public sealed class ChatPhotoEmbedding
    {
        /// <summary>The search model's file it was embedded with</summary>
        public string EmbeddingModel { get; set; }

        /// <summary>The photo's id in the platform's photo library</summary>
        public string PhotoId { get; set; }

        /// <summary>The photo's version (changes when it is edited)</summary>
        public long Version { get; set; }

        /// <summary>When it was taken (UTC)</summary>
        public DateTime Taken { get; set; }

        /// <summary>The vision tokens the embedding was made with (EmbeddingGemma 2: 70 or 140)</summary>
        public int VisionTokens { get; set; }

        /// <summary>The version of the details read with it (changes when what is read changes)</summary>
        public int DetailsVersion { get; set; }

        /// <summary>Where it was taken, or null</summary>
        public double? Latitude { get; set; }

        /// <summary>Where it was taken, or null</summary>
        public double? Longitude { get; set; }

        /// <summary>The nearest town to where it was taken: "Town, Region, Country", or null</summary>
        public string Place { get; set; }

        /// <summary>The camera, or null</summary>
        public string Camera { get; set; }

        /// <summary>The photo's width</summary>
        public int Width { get; set; }

        /// <summary>The photo's height</summary>
        public int Height { get; set; }

        /// <summary>Its kinds, comma-separated ("screenshot", "selfie", "panorama", "portrait", "favorite"), or null</summary>
        public string Kinds { get; set; }

        /// <summary>The embedding, as the floats' bytes</summary>
        public byte[] Embedding { get; set; }
    }

    /// <summary>
    /// Where the photo search keeps its index (Emgu.LiteRT.LM.Chat.Sqlite's <c>ChatHistoryStore</c> implements it).
    /// </summary>
    public interface IPhotoIndexStore
    {
        /// <summary>Every indexed photo.</summary>
        Task<IReadOnlyList<ChatPhotoEmbedding>> GetPhotoEmbeddingsAsync();

        /// <summary>Save a photo's embedding and details, replacing an older one of the same photo.</summary>
        /// <param name="record">The photo</param>
        Task SavePhotoEmbeddingAsync(ChatPhotoEmbedding record);

        /// <summary>Forget photos (deleted from the library, or no longer among the most recent).</summary>
        /// <param name="photoIds">Their ids</param>
        Task DeletePhotoEmbeddingsAsync(IEnumerable<string> photoIds);

        /// <summary>Delete the whole index.</summary>
        Task ClearPhotoEmbeddingsAsync();
    }

    /// <summary>The find_photos tool group: its name, summary and words, and the instructions that go with it.</summary>
    public static class ChatPhotos
    {
        /// <summary>The tool group's name.</summary>
        public const string GroupName = "photo_search";

        /// <summary>The tool group's summary, for the planner.</summary>
        public const string GroupSummary = "find and show the user's own photos and pictures on this device";

        /// <summary>The words that bring the tool group.</summary>
        public static readonly string[] GroupKeywords =
        {
            "photo", "picture", "pic", "selfie", "screenshot", "gallery", "album", "camera roll", "images of",
            "image of my", "my images"
        };

        /// <summary>The instructions that go with the find_photos tool.</summary>
        public const string Instructions = "When the user asks you to find or show their photos or pictures, use the find_photos tool with the request in their " +
            "words; the photos it finds are shown to the user, not to you.";
    }
}
