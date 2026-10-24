//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Cuts a long recording into pieces a speech recognizer can take (<see cref="MinSeconds"/> to <see cref="MaxSeconds"/> long), as the
    /// samples arrive. Each piece ends at the quietest moment between the two lengths, so a word is rarely cut in half.
    /// </summary>
    public sealed class MeetingAudioChunker
    {
        private readonly List<float> _buffer = new List<float>();
        private long _bufferStart;      // the buffer's first sample, counted from the start of the recording

        /// <summary>Create a chunker</summary>
        /// <param name="sampleRate">The samples per second</param>
        /// <param name="minSeconds">The shortest piece</param>
        /// <param name="maxSeconds">The longest piece</param>
        public MeetingAudioChunker(int sampleRate = 16000, double minSeconds = 30, double maxSeconds = 50)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (minSeconds <= 0 || maxSeconds < minSeconds) throw new ArgumentOutOfRangeException(nameof(maxSeconds));
            SampleRate = sampleRate;
            MinSeconds = minSeconds;
            MaxSeconds = maxSeconds;
        }

        /// <summary>The samples per second</summary>
        public int SampleRate { get; }
        /// <summary>The shortest piece, in seconds</summary>
        public double MinSeconds { get; }
        /// <summary>The longest piece, in seconds</summary>
        public double MaxSeconds { get; }

        /// <summary>The length of everything added so far</summary>
        public TimeSpan Recorded => TimeSpan.FromSeconds((_bufferStart + _buffer.Count) / (double)SampleRate);

        /// <summary>
        /// Add samples (mono, -1..1). Returns the pieces they completed - usually none.
        /// </summary>
        public IList<MeetingAudioChunk> Add(float[] samples)
        {
            var chunks = new List<MeetingAudioChunk>();
            if (samples == null || samples.Length == 0) return chunks;
            _buffer.AddRange(samples);
            int max = (int)(MaxSeconds * SampleRate);
            while (_buffer.Count >= max)
                chunks.Add(Take(QuietestCut()));
            return chunks;
        }

        /// <summary>The rest, as a last piece (null if there is less than half a second).</summary>
        public MeetingAudioChunk Flush()
        {
            if (_buffer.Count < SampleRate / 2)
            {
                _bufferStart += _buffer.Count;
                _buffer.Clear();
                return null;
            }
            return Take(_buffer.Count);
        }

        // Where to end the piece: the middle of the quietest 200 ms between MinSeconds and MaxSeconds.
        private int QuietestCut()
        {
            int window = SampleRate / 5, step = SampleRate / 20;
            int from = (int)(MinSeconds * SampleRate), to = Math.Min(_buffer.Count, (int)(MaxSeconds * SampleRate)) - window;
            int best = to + window / 2;
            double bestEnergy = double.MaxValue;
            for (int start = from; start <= to; start += step)
            {
                double energy = 0;
                for (int i = start; i < start + window; i++)
                    energy += _buffer[i] * _buffer[i];
                if (energy < bestEnergy)
                {
                    bestEnergy = energy;
                    best = start + window / 2;
                }
            }
            return best;
        }

        private MeetingAudioChunk Take(int count)
        {
            float[] samples = _buffer.GetRange(0, count).ToArray();
            _buffer.RemoveRange(0, count);
            var chunk = new MeetingAudioChunk(TimeSpan.FromSeconds(_bufferStart / (double)SampleRate), samples, SampleRate);
            _bufferStart += count;
            return chunk;
        }
    }

    /// <summary>A piece of a recording: where it starts and its samples.</summary>
    public sealed class MeetingAudioChunk
    {
        /// <summary>Create a piece</summary>
        public MeetingAudioChunk(TimeSpan start, float[] samples, int sampleRate)
        {
            Start = start;
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
            SampleRate = sampleRate;
        }

        /// <summary>Where it starts in the recording</summary>
        public TimeSpan Start { get; }
        /// <summary>Its samples, mono, -1..1</summary>
        public float[] Samples { get; }
        /// <summary>The samples per second</summary>
        public int SampleRate { get; }
        /// <summary>Its length</summary>
        public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);

        /// <summary>Whether it is all near silence (nothing to write down): its loudest 100 ms below -50 dBFS.</summary>
        public bool IsSilent
        {
            get
            {
                int window = Math.Max(1, SampleRate / 10);
                for (int start = 0; start < Samples.Length; start += window)
                {
                    double sum = 0;
                    int end = Math.Min(Samples.Length, start + window);
                    for (int i = start; i < end; i++)
                        sum += Samples[i] * Samples[i];
                    if (Math.Sqrt(sum / (end - start)) > 0.00316)  // -50 dBFS
                        return false;
                }
                return true;
            }
        }

        /// <summary>The piece as a 16-bit PCM WAV file.</summary>
        public byte[] ToWav()
        {
            using (var wav = new MemoryStream(44 + Samples.Length * 2))
            using (var writer = new BinaryWriter(wav))
            {
                int dataLength = Samples.Length * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);             // PCM
                writer.Write((short)1);             // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);
                foreach (float sample in Samples)
                    writer.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, sample)) * 32767));
                writer.Flush();
                return wav.ToArray();
            }
        }
    }

    /// <summary>
    /// What was said in a meeting, piece by piece as a speech recognizer wrote it down, with the time each piece starts.
    /// Thread-safe.
    /// </summary>
    public sealed class MeetingTranscript
    {
        private readonly List<(TimeSpan Start, string Text)> _segments = new List<(TimeSpan, string)>();

        /// <summary>Add what was said from a time on (empty text is ignored). Pieces may come out of order; they are kept in order.</summary>
        public void Add(TimeSpan start, string text)
        {
            text = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
            if (text.Length == 0) return;
            lock (_segments)
            {
                int at = _segments.Count;
                while (at > 0 && _segments[at - 1].Start > start) at--;
                _segments.Insert(at, (start, text));
            }
        }

        /// <summary>The pieces, in order</summary>
        public IList<(TimeSpan Start, string Text)> Segments
        {
            get { lock (_segments) return _segments.ToList(); }
        }

        /// <summary>The number of words written down</summary>
        public int WordCount
        {
            get { lock (_segments) return _segments.Sum(s => s.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length); }
        }

        /// <summary>Whether nothing was written down</summary>
        public bool IsEmpty
        {
            get { lock (_segments) return _segments.Count == 0; }
        }

        /// <summary>The transcript with a [m:ss] time before each piece, or the plain text.</summary>
        public string Text(bool withTimes = true)
        {
            var segments = Segments;
            return string.Join(withTimes ? "\n" : " ", segments.Select(s => withTimes ? "[" + MeetingNotes.FormatTime(s.Start) + "] " + s.Text : s.Text));
        }

        /// <summary>The end of the transcript, at most a number of characters (starting at a word), for showing as it grows.</summary>
        public string Tail(int maxLength)
        {
            string text = Text(false);
            if (text.Length <= maxLength) return text;
            int start = text.IndexOf(' ', text.Length - maxLength);
            return "…" + text.Substring(start < 0 ? text.Length - maxLength : start + 1);
        }

        /// <summary>
        /// The transcript in sections of at most a number of tokens each, split between pieces (a piece longer than that is a section of
        /// its own), each with the time it starts.
        /// </summary>
        public IList<(TimeSpan Start, string Text)> Sections(int maxTokens, Func<string, int> countTokens)
        {
            if (countTokens == null) throw new ArgumentNullException(nameof(countTokens));
            var sections = new List<(TimeSpan, string)>();
            var current = new StringBuilder();
            TimeSpan start = TimeSpan.Zero;
            int tokens = 0;
            foreach (var segment in Segments)
            {
                string line = "[" + MeetingNotes.FormatTime(segment.Start) + "] " + segment.Text;
                int lineTokens = countTokens(line) + 1;
                if (current.Length > 0 && tokens + lineTokens > maxTokens)
                {
                    sections.Add((start, current.ToString()));
                    current.Clear();
                    tokens = 0;
                }
                if (current.Length == 0) start = segment.Start;
                else current.Append('\n');
                current.Append(line);
                tokens += lineTokens;
            }
            if (current.Length > 0) sections.Add((start, current.ToString()));
            return sections;
        }
    }

    /// <summary>
    /// Writes meeting notes from a <see cref="MeetingTranscript"/> with a language model whose window is far smaller than a long
    /// meeting: a transcript that fits is given whole; a longer one is summarized section by section (keeping decisions, action items,
    /// names, numbers and dates), summaries that still don't fit are summarized together again, and the notes are written from the
    /// summaries.
    /// </summary>
    public static class MeetingNotes
    {
        /// <summary>The longest a section summary may be, in tokens</summary>
        public const int SectionSummaryTokens = 350;
        /// <summary>The longest the notes may be, in tokens</summary>
        public const int NotesTokens = 900;

        /// <summary>A time in a recording as m:ss, or h:mm:ss from an hour on.</summary>
        public static string FormatTime(TimeSpan time) => time.TotalHours >= 1
            ? ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + time.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + time.Seconds.ToString("00", CultureInfo.InvariantCulture)
            : ((int)time.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + time.Seconds.ToString("00", CultureInfo.InvariantCulture);

        private const string TranscriptCaveat = "It was written down automatically by speech recognition, so some words may be wrong, " +
            "and it doesn't say who is speaking.";

        /// <summary>The request to summarize one section of a long transcript.</summary>
        public static string SectionPrompt(string title, string section, int index, int count)
        {
            return "Below is part " + (index + 1) + " of " + count + " of the transcript of a meeting" + TitlePart(title) + ". " + TranscriptCaveat +
                "\nSummarize this part in at most 8 short bullet points. Keep every decision, action item (who will do what, by when), " +
                "number, date and name that is mentioned. Use only what the transcript says. Write in the language of the transcript. " +
                "Write only the bullet points.\n\nTranscript:\n" + section;
        }

        /// <summary>The request to combine several section summaries into one, when they are too long to write the notes from.</summary>
        public static string CombinePrompt(string title, IList<string> summaries)
        {
            return "Below are summaries of consecutive parts of a meeting" + TitlePart(title) + ". Combine them into one summary of at most " +
                "10 short bullet points, keeping every decision, action item (who, what, by when), number, date and name. Write only the " +
                "bullet points.\n\n" + Numbered(summaries);
        }

        /// <summary>The request to write the notes, from the whole transcript or from section summaries.</summary>
        public static string NotesPrompt(string title, TimeSpan duration, string transcript, IList<string> summaries)
        {
            string source = summaries == null
                ? "Below is the transcript of a meeting" + TitlePart(title) + " that lasted " + Minutes(duration) + ". " + TranscriptCaveat
                : "Below are summaries of the parts of a meeting" + TitlePart(title) + " that lasted " + Minutes(duration) + ", in order. " +
                  "They were made from a transcript written down by speech recognition.";
            return source + "\nWrite meeting notes in Markdown with these sections:\n" +
                "## Summary\n(two to four sentences)\n## Key points\n(bullet points)\n## Decisions\n(bullet points, or \"None recorded.\")\n" +
                "## Action items\n(bullet points: who - what - by when, as far as it was said, or \"None recorded.\")\n" +
                "## Open questions\n(bullet points, or \"None recorded.\")\n" +
                "Use only what is said below: don't invent names, numbers or dates. Write in the language of the meeting. Write only the notes." +
                "\n\n" + (summaries == null ? "Transcript:\n" + transcript : Numbered(summaries));
        }

        private static string TitlePart(string title) => string.IsNullOrWhiteSpace(title) ? "" : " (\"" + title.Trim() + "\")";

        private static string Minutes(TimeSpan duration)
        {
            int minutes = (int)Math.Round(duration.TotalMinutes);
            return minutes < 1 ? "under a minute" : minutes == 1 ? "1 minute" : minutes.ToString(CultureInfo.InvariantCulture) + " minutes";
        }

        private static string Numbered(IList<string> summaries) =>
            string.Join("\n\n", summaries.Select((s, i) => "Part " + (i + 1) + ":\n" + s.Trim()));

        /// <summary>
        /// Write the notes.
        /// </summary>
        /// <param name="transcript">What was said</param>
        /// <param name="title">The meeting's title, or null</param>
        /// <param name="duration">How long it lasted</param>
        /// <param name="ask">Asks the model one question in a request of its own: (prompt, the most tokens to answer with, cancellation) to the answer</param>
        /// <param name="countTokens">Counts a text's tokens</param>
        /// <param name="maxPromptTokens">The most tokens a request may have (the window less the answer and some room)</param>
        /// <param name="progress">Says what is being done ("Summarizing part 2 of 4"), or null</param>
        /// <param name="cancellationToken">Stops it</param>
        /// <returns>The notes in Markdown, or null for an empty transcript</returns>
        public static async Task<string> WriteAsync(MeetingTranscript transcript, string title, TimeSpan duration,
            Func<string, int, CancellationToken, Task<string>> ask, Func<string, int> countTokens, int maxPromptTokens,
            IProgress<string> progress = null, CancellationToken cancellationToken = default)
        {
            if (transcript == null) throw new ArgumentNullException(nameof(transcript));
            if (ask == null) throw new ArgumentNullException(nameof(ask));
            if (countTokens == null) throw new ArgumentNullException(nameof(countTokens));
            if (transcript.IsEmpty) return null;
            maxPromptTokens = Math.Max(400, maxPromptTokens);
            // The instructions around the text take about 250 tokens.
            int textTokens = maxPromptTokens - 250;
            IList<(TimeSpan Start, string Text)> sections = transcript.Sections(textTokens, countTokens);
            if (sections.Count == 1)
            {
                progress?.Report("Writing the notes…");
                return Clean(await ask(NotesPrompt(title, duration, sections[0].Text, null), NotesTokens, cancellationToken).ConfigureAwait(false));
            }

            var summaries = new List<string>();
            for (int i = 0; i < sections.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("Summarizing part " + (i + 1) + " of " + sections.Count + "…");
                string summary = Clean(await ask(SectionPrompt(title, sections[i].Text, i, sections.Count), SectionSummaryTokens, cancellationToken).ConfigureAwait(false));
                summaries.Add("(from " + FormatTime(sections[i].Start) + ")\n" + summary);
            }
            // Summaries that together are still too long are combined a few at a time, until they fit.
            while (summaries.Count > 1 && countTokens(string.Join("\n\n", summaries)) > textTokens)
            {
                var combined = new List<string>();
                var group = new List<string>();
                int groupTokens = 0;
                async Task FlushGroup()
                {
                    if (group.Count == 1) combined.Add(group[0]);
                    else if (group.Count > 1)
                    {
                        progress?.Report("Combining the summaries…");
                        combined.Add(Clean(await ask(CombinePrompt(title, group), SectionSummaryTokens, cancellationToken).ConfigureAwait(false)));
                    }
                    group = new List<string>();
                    groupTokens = 0;
                }
                foreach (string summary in summaries)
                {
                    int tokens = countTokens(summary) + 8;
                    if (group.Count > 0 && groupTokens + tokens > textTokens) await FlushGroup().ConfigureAwait(false);
                    group.Add(summary);
                    groupTokens += tokens;
                }
                await FlushGroup().ConfigureAwait(false);
                if (combined.Count >= summaries.Count) break;     // nothing got shorter: write from what there is
                summaries = combined;
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Writing the notes…");
            return Clean(await ask(NotesPrompt(title, duration, null, summaries), NotesTokens, cancellationToken).ConfigureAwait(false));
        }

        // The model's answer without a code fence around it.
        private static string Clean(string text)
        {
            text = (text ?? string.Empty).Trim();
            Match fence = Regex.Match(text, @"^```[a-zA-Z]*\s*\n([\s\S]*?)\n?```$");
            return fence.Success ? fence.Groups[1].Value.Trim() : text;
        }

        /// <summary>
        /// The notes and the transcript as one Markdown document (for <see cref="DocumentWriter"/>).
        /// </summary>
        public static string DocumentMarkdown(string title, DateTime started, TimeSpan duration, string notes, MeetingTranscript transcript)
        {
            var text = new StringBuilder();
            text.Append("# ").Append(string.IsNullOrWhiteSpace(title) ? "Meeting notes" : title.Trim()).Append("\n\n");
            text.Append(started.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)).Append(" · ").Append(FormatTime(duration)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(notes))
                text.Append(Regex.Replace(notes.Trim(), @"^# .*\n+", "")).Append("\n\n");
            text.Append("## Transcript\n\n");
            text.Append("*Written down automatically by speech recognition; some words may be wrong.*\n\n");
            foreach (var segment in transcript.Segments)
                text.Append("**").Append(FormatTime(segment.Start)).Append("** ").Append(segment.Text).Append("\n\n");
            return text.ToString();
        }
    }

    public static partial class AgentTools
    {
        /// <summary>
        /// record_meeting: the app records a meeting, lecture or conversation through the microphone, writes down what is said as it goes
        /// and, when the user stops it, writes notes (<see cref="MeetingNotes"/>) and returns them. The call lasts as long as the
        /// recording.
        /// </summary>
        /// <param name="record">Records: (title - may be empty -, cancellation) to the result text for the model</param>
        /// <returns>The tool</returns>
        public static AIFunction RecordMeeting(Func<string, CancellationToken, Task<string>> record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            return AIFunctionFactory.Create(
                ([Description("A short title for the meeting if the user gave one (e.g. \"Weekly team meeting\"); empty if not")] string title = "",
                 CancellationToken cancellationToken = default) =>
                    record((title ?? string.Empty).Trim(), cancellationToken),
                "record_meeting",
                "Records a meeting, lecture, interview or conversation through the microphone, writes down what is said and, when the " +
                "user presses Stop, writes notes of it (summary, decisions, action items) and returns them. Use it when the user wants " +
                "a meeting recorded, transcribed or taken notes of.");
        }

        /// <summary>
        /// The result of record_meeting for the model.
        /// </summary>
        /// <param name="duration">How long the recording lasted</param>
        /// <param name="words">How many words were written down</param>
        /// <param name="notes">The notes, or null if none were written</param>
        /// <param name="fileName">The document with the notes and transcript, or null</param>
        /// <param name="problem">Why there are no notes, or null</param>
        /// <returns>The text</returns>
        public static string MeetingResultText(TimeSpan duration, int words, string notes, string fileName, string problem)
        {
            string text = "Recorded " + MeetingNotes.FormatTime(duration) + " (m:ss); " + words.ToString(CultureInfo.InvariantCulture) + " words were written down.";
            if (string.IsNullOrWhiteSpace(notes))
                return text + " " + (problem ?? "Nothing was heard.") + " Tell the user in one short sentence.";
            const int maxNotes = 2500;
            string shown = notes.Length <= maxNotes ? notes : notes.Substring(0, maxNotes) + "…";
            return text + " The notes below are already shown to the user" + (fileName != null ? " and saved with the transcript as \"" + fileName + "\"" : "") +
                ". Reply in one short sentence (e.g. \"Here are the notes of your meeting.\") - don't repeat them. Use them to answer the " +
                "user's questions about the meeting.\n\nNotes:\n" + shown;
        }
    }
}
