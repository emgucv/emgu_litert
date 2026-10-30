//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// A stretch of a video: from StartMs to EndMs, with the times of the frames sampled in it.
    /// </summary>
    public class VideoWindow
    {
        /// <summary>
        /// Create a window.
        /// </summary>
        public VideoWindow(long startMs, long endMs, IReadOnlyList<long> frameTimesMs)
        {
            StartMs = startMs;
            EndMs = endMs;
            FrameTimesMs = frameTimesMs ?? new long[0];
        }

        /// <summary>Where it starts, in milliseconds</summary>
        public long StartMs { get; }

        /// <summary>Where it ends, in milliseconds</summary>
        public long EndMs { get; }

        /// <summary>The times of its frames, in milliseconds</summary>
        public IReadOnlyList<long> FrameTimesMs { get; }

        /// <summary>Its embedding, or null before it is indexed</summary>
        public float[] Embedding { get; internal set; }
    }

    /// <summary>
    /// A moment found in a video: neighbouring windows that matched a query, merged into one stretch.
    /// </summary>
    public class VideoMoment
    {
        /// <summary>
        /// Create a moment.
        /// </summary>
        public VideoMoment(long startMs, long endMs, float score, IReadOnlyList<VideoWindow> windows)
        {
            StartMs = startMs;
            EndMs = endMs;
            Score = score;
            Windows = windows ?? new VideoWindow[0];
        }

        /// <summary>Where it starts, in milliseconds</summary>
        public long StartMs { get; }

        /// <summary>Where it ends, in milliseconds</summary>
        public long EndMs { get; }

        /// <summary>The best cosine similarity of its windows to the query</summary>
        public float Score { get; }

        /// <summary>The matching windows it is made of, in time order</summary>
        public IReadOnlyList<VideoWindow> Windows { get; }

        /// <summary>"01:04-01:08"</summary>
        public override String ToString()
        {
            return VideoMomentIndex.FormatTime(StartMs) + "-" + VideoMomentIndex.FormatTime(EndMs);
        }
    }

    /// <summary>
    /// Finds the moments of a video that a text describes ("someone blowing out candles"), the way Google AI Edge
    /// Gallery's Video Moment Finder does with EmbeddingGemma 2: the video is cut into short windows (2 seconds by
    /// default), each embedded as one input made of a few of its frames - interleaved with the sound around each frame
    /// and their times ("00:02") when the model embeds audio - and a query is ranked against them, the best windows
    /// merged into moments. Indexing is a model run per window (seconds each on a phone's CPU); a search is one text
    /// embedding. The app supplies the frames and the sound (from the platform's video decoder), so this has no video
    /// code of its own.
    /// </summary>
    public class VideoMomentIndex
    {
        private const int AudioSampleRate = 16000;

        // Audio tokens per second of 16 kHz sound: 100 mel frames a second, shrunk 16 times by the encoder (the Gallery's
        // estimate; only used to keep a window inside the model's input).
        private const float AudioTokensPerSecond = 6.25f;

        // A timestamp ("00:03") and the markers around the part it labels, overestimated.
        private const int TimestampAndMarkerTokens = 8;

        private readonly List<VideoWindow> _windows = new List<VideoWindow>();

        /// <summary>
        /// The length of a window, in milliseconds: 2000 by default (the Gallery's offers 2, 4 and 6 seconds)
        /// </summary>
        public int WindowMs { get; set; } = 2000;

        /// <summary>
        /// How many frames are sampled from each window, evenly from its start to its end: 2 by default
        /// </summary>
        public int FramesPerWindow { get; set; } = 2;

        /// <summary>
        /// How much a window overlaps the one before, in milliseconds: 0 by default
        /// </summary>
        public int OverlapMs { get; set; }

        /// <summary>
        /// Whether to embed the sound with the frames, when the model embeds audio and the app gives it: true by default
        /// </summary>
        public bool IncludeAudio { get; set; } = true;

        /// <summary>
        /// The vision tokens per frame: 70 by default, half EmbeddingGemma 2's default, as the Gallery does
        /// </summary>
        public int VisionTokensPerFrame { get; set; } = 70;

        /// <summary>
        /// The most tokens a window may take: 1024 by default. Sound that would go over it is left out.
        /// </summary>
        public int MaxInputTokens { get; set; } = 1024;

        /// <summary>
        /// The windows, in time order
        /// </summary>
        public IReadOnlyList<VideoWindow> Windows
        {
            get { return _windows; }
        }

        /// <summary>
        /// True if the windows were embedded with their sound. Embeddings with and without sound aren't comparable,
        /// so one video is indexed one way throughout.
        /// </summary>
        public bool WithAudio { get; private set; }

        /// <summary>
        /// The video's length in milliseconds, as given to BuildAsync
        /// </summary>
        public long DurationMs { get; private set; }

        /// <summary>
        /// True once every window is embedded
        /// </summary>
        public bool IsBuilt
        {
            get { return _windows.Count > 0 && _windows.All(w => w.Embedding != null); }
        }

        /// <summary>
        /// Index a video: cut it into windows, take their frames (and sound) from the app, and embed each window.
        /// </summary>
        /// <param name="model">An initialized embedding model that SupportsImages, e.g. EmbeddingGemma2Multimodal</param>
        /// <param name="durationMs">The video's length, in milliseconds</param>
        /// <param name="frameAt">The frame nearest a time (milliseconds) as an encoded image (JPEG, PNG...), or null if
        /// there's none; a nearby key frame is fine</param>
        /// <param name="audioBetween">The sound from one time to another (milliseconds) as 16 kHz mono samples, or null
        /// when there's none; null to index the frames only</param>
        /// <param name="progress">Reports the share of windows done, from 0 to 1, or null</param>
        /// <param name="cancellationToken">Stops between windows</param>
        public async Task BuildAsync(EmbeddingModel model, long durationMs,
            Func<long, CancellationToken, Task<byte[]>> frameAt,
            Func<long, long, CancellationToken, Task<short[]>> audioBetween = null,
            IProgress<double> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (frameAt == null) throw new ArgumentNullException(nameof(frameAt));
            if (!model.SupportsImages)
                throw new NotSupportedException(String.Format("{0} can't embed images", model.GetType().Name));
            _windows.Clear();
            _windows.AddRange(Split(durationMs, WindowMs, FramesPerWindow, OverlapMs));
            DurationMs = durationMs;
            float sliceSeconds = WindowMs / 1000f / Math.Max(1, FramesPerWindow);
            WithAudio = IncludeAudio && audioBetween != null && model.SupportsAudio
                && EstimateTokens(FramesPerWindow, VisionTokensPerFrame, FramesPerWindow, sliceSeconds) <= MaxInputTokens;
            int done = 0;
            foreach (VideoWindow window in _windows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<byte[]> frames = new List<byte[]>();
                List<long> times = new List<long>();
                foreach (long t in window.FrameTimesMs)
                {
                    byte[] frame = await frameAt(t, cancellationToken).ConfigureAwait(false);
                    if (frame != null && frame.Length > 0)
                    {
                        frames.Add(frame);
                        times.Add(t);
                    }
                }
                if (frames.Count == 0)
                    throw new InvalidDataException(String.Format("No frame from {0} to {1}", FormatTime(window.StartMs), FormatTime(window.EndMs)));
                short[][] sound = null;
                if (WithAudio)
                {
                    short[] pcm = await audioBetween(window.StartMs, window.EndMs, cancellationToken).ConfigureAwait(false);
                    sound = SplitAudio(pcm, window.StartMs, window.EndMs, frames.Count);
                }
                try
                {
                    window.Embedding = await Task.Run(() => EmbedWindow(model, frames, times, sound), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (WithAudio && done == 0 && !cancellationToken.IsCancellationRequested)
                {
                    // The model said it embeds audio but the engine refused it (e.g. an audio encoder that can't run on the
                    // backend): index this video's frames only, so all its windows stay in one embedding space.
                    WithAudio = false;
                    window.Embedding = await Task.Run(() => EmbedWindow(model, frames, times, null), cancellationToken).ConfigureAwait(false);
                }
                done++;
                if (progress != null)
                    progress.Report((double)done / _windows.Count);
            }
        }

        private float[] EmbedWindow(EmbeddingModel model, List<byte[]> frames, List<long> times, short[][] sound)
        {
            List<InputData> inputs = new List<InputData>();
            try
            {
                bool anySound = sound != null && sound.Any(s => s != null && s.Length > 0);
                for (int i = 0; i < frames.Count; i++)
                {
                    // With sound, the model's timed layout: "00:00" <audio> "00:00" <frame> "00:01" <audio> ... Frames
                    // alone go without times, the way that mode was trained.
                    if (anySound)
                    {
                        String time = FormatTime(times[i]);
                        if (sound[i] != null && sound[i].Length > 0)
                        {
                            inputs.Add(new InputData(time));
                            inputs.Add(InputData.FromAudio(ToWav(sound[i], AudioSampleRate)));
                        }
                        inputs.Add(new InputData(time));
                    }
                    inputs.Add(InputData.FromImage(frames[i]));
                }
                return model.Embed(inputs.ToArray(), 0, VisionTokensPerFrame);
            }
            finally
            {
                foreach (InputData input in inputs)
                    input.Dispose();
            }
        }

        /// <summary>
        /// The moments that best match a query: the topK best windows, neighbours merged. Embeds the query (one model run).
        /// </summary>
        /// <param name="model">The embedding model the index was built with</param>
        /// <param name="query">What to find, e.g. "a dog running on the beach"</param>
        /// <param name="topK">How many windows to keep before merging: 5 by default</param>
        /// <returns>The moments, the best match first</returns>
        public IReadOnlyList<VideoMoment> Search(EmbeddingModel model, String query, int topK = 5)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            float[] q = model.EmbedQuery(query ?? String.Empty);
            return Rank(_windows.Where(w => w.Embedding != null)
                .Select(w => new KeyValuePair<VideoWindow, float>(w, EmbeddingModel.CosineSimilarity(q, w.Embedding))), topK);
        }

        /// <summary>
        /// Keep the topK best-scored windows and merge those that touch (within toleranceMs) into moments, the best first.
        /// </summary>
        /// <param name="scored">Windows and their scores</param>
        /// <param name="topK">How many windows to keep</param>
        /// <param name="toleranceMs">The largest gap between two windows of one moment</param>
        public static IReadOnlyList<VideoMoment> Rank(IEnumerable<KeyValuePair<VideoWindow, float>> scored, int topK = 5, long toleranceMs = 500)
        {
            List<KeyValuePair<VideoWindow, float>> top = scored
                .OrderByDescending(s => s.Value)
                .Take(topK > 0 ? topK : 5)
                .OrderBy(s => s.Key.StartMs)
                .ToList();
            List<VideoMoment> moments = new List<VideoMoment>();
            List<KeyValuePair<VideoWindow, float>> group = new List<KeyValuePair<VideoWindow, float>>();
            long groupEnd = 0;
            foreach (KeyValuePair<VideoWindow, float> s in top)
            {
                if (group.Count > 0 && s.Key.StartMs > groupEnd + toleranceMs)
                {
                    moments.Add(Moment(group));
                    group.Clear();
                }
                group.Add(s);
                groupEnd = group.Count == 1 ? s.Key.EndMs : Math.Max(groupEnd, s.Key.EndMs);
            }
            if (group.Count > 0)
                moments.Add(Moment(group));
            return moments.OrderByDescending(m => m.Score).ToList();
        }

        private static VideoMoment Moment(List<KeyValuePair<VideoWindow, float>> group)
        {
            return new VideoMoment(group[0].Key.StartMs, group.Max(s => s.Key.EndMs), group.Max(s => s.Value),
                group.Select(s => s.Key).ToList());
        }

        /// <summary>
        /// Cut a video into windows of windowMs, each overlapping the one before by overlapMs (at least a second apart),
        /// with framesPerWindow frame times spread from its start to its end (the middle for one frame). The last
        /// window ends with the video.
        /// </summary>
        public static List<VideoWindow> Split(long durationMs, int windowMs = 2000, int framesPerWindow = 2, int overlapMs = 0)
        {
            if (windowMs <= 0) throw new ArgumentOutOfRangeException(nameof(windowMs));
            if (framesPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(framesPerWindow));
            List<VideoWindow> windows = new List<VideoWindow>();
            long step = Math.Max(1000L, windowMs - (long)overlapMs);
            for (long start = 0; start < durationMs; start += step)
            {
                long end = Math.Min(start + windowMs, durationMs);
                long[] times = new long[framesPerWindow];
                for (int i = 0; i < framesPerWindow; i++)
                    times[i] = framesPerWindow == 1 ? start + (end - start) / 2 : start + i * (end - start) / (framesPerWindow - 1);
                windows.Add(new VideoWindow(start, end, times));
            }
            return windows;
        }

        /// <summary>
        /// Split a window's 16 kHz sound into count equal, back-to-back slices, one per frame. A slice the sound doesn't
        /// reach (it ended early) is null, so the others stay with their frames.
        /// </summary>
        public static short[][] SplitAudio(short[] pcm, long startMs, long endMs, int count)
        {
            short[][] slices = new short[Math.Max(0, count)][];
            if (pcm == null || pcm.Length == 0 || count <= 0 || endMs <= startMs)
                return slices;
            long duration = endMs - startMs;
            for (int i = 0; i < count; i++)
            {
                int from = Math.Min(pcm.Length, (int)(i * duration / count * AudioSampleRate / 1000));
                int to = Math.Min(pcm.Length, (int)((i + 1) * duration / count * AudioSampleRate / 1000));
                if (to > from)
                {
                    slices[i] = new short[to - from];
                    Array.Copy(pcm, from, slices[i], 0, to - from);
                }
            }
            return slices;
        }

        /// <summary>
        /// An upper bound of the tokens a window takes: its frames, its sound and the times and markers around them.
        /// </summary>
        public static int EstimateTokens(int frames, int visionTokensPerFrame, int audioSlices, float audioSliceSeconds)
        {
            int audioTokens = (int)Math.Ceiling(audioSlices * Math.Max(0f, audioSliceSeconds) * AudioTokensPerSecond);
            return frames * visionTokensPerFrame + audioTokens + (frames + audioSlices) * TimestampAndMarkerTokens + 2;
        }

        /// <summary>
        /// A time as "01:05", or "1:01:05" past an hour.
        /// </summary>
        public static String FormatTime(long ms)
        {
            long seconds = Math.Max(0, ms) / 1000;
            return seconds >= 3600
                ? String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", seconds / 3600, seconds % 3600 / 60, seconds % 60)
                : String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", seconds / 60, seconds % 60);
        }

        /// <summary>
        /// 16-bit mono samples as a WAV file's content.
        /// </summary>
        public static byte[] ToWav(short[] samples, int sampleRate)
        {
            using (MemoryStream stream = new MemoryStream(44 + samples.Length * 2))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Length * 2);
                foreach (short s in samples)
                    writer.Write(s);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private const int FileVersion = 1;

        /// <summary>
        /// Save the index (its settings, windows and embeddings), to search the video again without indexing it.
        /// </summary>
        public void Save(Stream stream)
        {
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("EVMI"));
                writer.Write(FileVersion);
                writer.Write(DurationMs);
                writer.Write(WindowMs);
                writer.Write(FramesPerWindow);
                writer.Write(OverlapMs);
                writer.Write(VisionTokensPerFrame);
                writer.Write(WithAudio);
                writer.Write(_windows.Count);
                foreach (VideoWindow window in _windows)
                {
                    writer.Write(window.StartMs);
                    writer.Write(window.EndMs);
                    writer.Write(window.FrameTimesMs.Count);
                    foreach (long t in window.FrameTimesMs)
                        writer.Write(t);
                    float[] embedding = window.Embedding ?? new float[0];
                    writer.Write(embedding.Length);
                    foreach (float v in embedding)
                        writer.Write(v);
                }
            }
        }

        /// <summary>
        /// Load an index saved with Save.
        /// </summary>
        public static VideoMomentIndex Load(Stream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "EVMI")
                    throw new InvalidDataException("Not a video moment index");
                int version = reader.ReadInt32();
                if (version != FileVersion)
                    throw new InvalidDataException(String.Format("Unknown video moment index version {0}", version));
                VideoMomentIndex index = new VideoMomentIndex
                {
                    DurationMs = reader.ReadInt64(),
                    WindowMs = reader.ReadInt32(),
                    FramesPerWindow = reader.ReadInt32(),
                    OverlapMs = reader.ReadInt32(),
                    VisionTokensPerFrame = reader.ReadInt32(),
                    WithAudio = reader.ReadBoolean()
                };
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    long start = reader.ReadInt64(), end = reader.ReadInt64();
                    long[] times = new long[reader.ReadInt32()];
                    for (int j = 0; j < times.Length; j++)
                        times[j] = reader.ReadInt64();
                    float[] embedding = new float[reader.ReadInt32()];
                    for (int j = 0; j < embedding.Length; j++)
                        embedding[j] = reader.ReadSingle();
                    index._windows.Add(new VideoWindow(start, end, times) { Embedding = embedding.Length > 0 ? embedding : null });
                }
                return index;
            }
        }
    }
}
