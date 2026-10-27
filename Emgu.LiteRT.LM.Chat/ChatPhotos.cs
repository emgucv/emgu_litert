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

        /// <summary>
        /// The neutral text a photo's match is measured against: how much more a photo is like the description than like
        /// "a photo" (its lift) tells a real match from a photo that is merely like any text.
        /// </summary>
        public const string BaselineQuery = "a photo";

        /// <summary>The lowest text-to-photo similarity a match may have.</summary>
        public const float MinScore = 0.6f;

        /// <summary>
        /// The smallest lift a match must have. Measured with EmbeddingGemma 2 Text Vision at 140 vision tokens on 18 photos
        /// (12 test photos and 6 drawn walls, doorways and a screenshot): the right photo's lift was 0.040-0.162 (dog, tulips,
        /// surfers, space shuttle, hands, faces, beach, screenshot); with nothing to find ("cat", "a car", "food", "a birthday
        /// cake", "mountains", "snow") the best photo's was 0.026 at most - while their raw similarities were 0.62-0.65, which
        /// the old rule (0.6, within 0.04 of the best) let through: "cat" showed eight photos of walls.
        /// </summary>
        public const float MinLift = 0.035f;

        /// <summary>How far below the best match's lift another match may be.</summary>
        public const float MaxBelowBestLift = 0.04f;

        /// <summary>The most photos a search shows.</summary>
        public const int MaxResults = 8;

        /// <summary>
        /// The photos that match a description, best first: at least <see cref="MinScore"/> like it, a lift (the
        /// similarity minus the similarity to <see cref="BaselineQuery"/>) of at least <see cref="MinLift"/> and not more
        /// than <see cref="MaxBelowBestLift"/> below the best lift; at most <paramref name="maxResults"/>.
        /// </summary>
        /// <param name="scores">Each photo's similarity to the description</param>
        /// <param name="baselineScores">Each photo's similarity to <see cref="BaselineQuery"/></param>
        /// <param name="maxResults">The most to return</param>
        /// <returns>The indexes of the matches, best first; empty when no photo stands out</returns>
        public static List<int> SelectMatches(IReadOnlyList<float> scores, IReadOnlyList<float> baselineScores, int maxResults = MaxResults)
        {
            if (scores == null)
                throw new ArgumentNullException(nameof(scores));
            if (baselineScores == null || baselineScores.Count != scores.Count)
                throw new ArgumentException("One baseline score per photo is needed.", nameof(baselineScores));
            var candidates = new List<(int Index, float Lift)>();
            for (int i = 0; i < scores.Count; i++)
            {
                float lift = scores[i] - baselineScores[i];
                if (scores[i] >= MinScore && lift >= MinLift)
                    candidates.Add((i, lift));
            }
            if (candidates.Count == 0)
                return new List<int>();
            float best = float.MinValue;
            foreach (var c in candidates)
                best = Math.Max(best, c.Lift);
            candidates.RemoveAll(c => c.Lift < best - MaxBelowBestLift);
            candidates.Sort((x, y) => y.Lift.CompareTo(x.Lift));
            var result = new List<int>();
            for (int i = 0; i < candidates.Count && i < maxResults; i++)
                result.Add(candidates[i].Index);
            return result;
        }
    }
}
