//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>A moment of a video found by find_video_moment: its times, its score and a thumbnail.</summary>
    public sealed class ChatVideoMoment
    {
        /// <summary>Where it starts, in milliseconds</summary>
        public long StartMs { get; set; }

        /// <summary>Where it ends, in milliseconds</summary>
        public long EndMs { get; set; }

        /// <summary>How well it matched (cosine similarity)</summary>
        public float Score { get; set; }

        /// <summary>A JPEG of its first frame, or null</summary>
        public byte[] Thumbnail { get; set; }

        /// <summary>"01:04-01:08"</summary>
        public override string ToString() => ChatVideos.FormatTime(StartMs) + "-" + ChatVideos.FormatTime(EndMs);
    }

    /// <summary>
    /// The moments a search found, as saved with the reply: the video (its name and the file of its copy), what was asked
    /// and the moments, best first.
    /// </summary>
    public sealed class ChatVideoMoments
    {
        /// <summary>The video's file name, as the user picked it</summary>
        public string VideoName { get; set; }

        /// <summary>The app's copy of the video: its file name (the hash of its content and its extension)</summary>
        public string VideoFile { get; set; }

        /// <summary>What was asked</summary>
        public string Request { get; set; }

        /// <summary>The moments, best first</summary>
        public List<ChatVideoMoment> Moments { get; set; } = new List<ChatVideoMoment>();

        /// <summary>The moments as JSON (UTF-8), to save with the reply.</summary>
        public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this);

        /// <summary>Moments saved with ToJson, or null if the data isn't that.</summary>
        public static ChatVideoMoments FromJson(byte[] json)
        {
            if (json == null || json.Length == 0)
                return null;
            try
            {
                return JsonSerializer.Deserialize<ChatVideoMoments>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The find_video_moment tool's name, instructions and texts. It comes with every message while a video is attached
    /// (not as a routing group, whose stub would cost every chat's messages).
    /// </summary>
    public static class ChatVideos
    {
        /// <summary>The skill's name.</summary>
        public const string GroupName = "video_moments";

        /// <summary>The instructions that go with the find_video_moment tool.</summary>
        public const string Instructions = "When the user asks when or where something happens in the video they attached, or to find or show a part " +
            "of it, use the find_video_moment tool with what happens in their words; the moments it finds are shown to the user, who can play " +
            "the video from each - you don't see the video, only their times.";

        /// <summary>The longest video that can be searched, in milliseconds: 5 minutes, about 150 windows to index.</summary>
        public const long MaxDurationMs = 5 * 60 * 1000;

        /// <summary>The most moments a search shows.</summary>
        public const int MaxMoments = 4;

        /// <summary>
        /// How far below the best moment's score another may be to be shown. On a 30 s video of six 5 s test-picture
        /// scenes with speech (EmbeddingGemma 2 Multimodal, iOS simulator), every search also showed one or two moments of
        /// other scenes without it; with it, 6 of 7 searches showed only their scene ("a dog" also showed the apples).
        /// </summary>
        public const float MaxBelowBest = 0.04f;

        /// <summary>
        /// The moments to show, best first: those within <see cref="MaxBelowBest"/> of the best, at most
        /// <see cref="MaxMoments"/>.
        /// </summary>
        /// <param name="scores">Each moment's score, best first</param>
        /// <returns>How many of the first moments to show</returns>
        public static int CountShown(IReadOnlyList<float> scores)
        {
            if (scores == null || scores.Count == 0)
                return 0;
            int count = 1;
            while (count < scores.Count && count < MaxMoments && scores[count] >= scores[0] - MaxBelowBest)
                count++;
            return count;
        }

        /// <summary>A time as "01:05", or "1:01:05" past an hour.</summary>
        public static string FormatTime(long ms)
        {
            long seconds = Math.Max(0, ms) / 1000;
            return seconds >= 3600
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", seconds / 3600, seconds % 3600 / 60, seconds % 60)
                : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", seconds / 60, seconds % 60);
        }

        /// <summary>
        /// What the model is told about the attached video with each message, e.g. "The user attached the video
        /// “party.mp4” (2:31); find_video_moment searches it." - with how much is indexed while it is.
        /// </summary>
        /// <param name="name">The video's name</param>
        /// <param name="durationMs">Its length</param>
        /// <param name="indexed">The share indexed, from 0 to 1</param>
        public static string Context(string name, long durationMs, double indexed)
        {
            var text = new StringBuilder();
            text.Append("The user attached the video “").Append(name).Append("” (").Append(FormatTime(durationMs)).Append(')');
            if (indexed < 1)
                text.Append(", still being watched (").Append(Percent(indexed)).Append(" done)");
            text.Append("; find_video_moment finds when something happens in it.");
            return text.ToString();
        }

        /// <summary>
        /// What find_video_moment tells the model: the moments' times, best first, that the user sees them and can play
        /// them, and how much of the video was searched while it is still being indexed.
        /// </summary>
        /// <param name="request">What was asked</param>
        /// <param name="moments">The moments, best first</param>
        /// <param name="indexed">The share of the video indexed, from 0 to 1</param>
        public static string Reply(string request, IReadOnlyList<ChatVideoMoment> moments, double indexed)
        {
            string searched = indexed < 1 ? " (only the first " + Percent(indexed) + " of the video has been watched so far)" : string.Empty;
            if (moments == null || moments.Count == 0)
                return "No moment of the video matches “" + request + "”" + searched + ".";
            return "Found " + (moments.Count == 1 ? "1 moment" : moments.Count.ToString(CultureInfo.InvariantCulture) + " moments") +
                " for “" + request + "”, best first: " + string.Join(", ", moments.Select(m => m.ToString())) + searched +
                ". They are shown to the user, who can tap one to play the video from there; you can't see the video. " +
                "Tell the user the time of the best one in one short sentence.";
        }

        private static string Percent(double share) =>
            ((int)Math.Round(Math.Max(0, Math.Min(1, share)) * 100)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
