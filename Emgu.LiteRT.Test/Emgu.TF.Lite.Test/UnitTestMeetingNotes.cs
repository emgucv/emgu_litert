// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Meeting notes (record_meeting): cutting the recording into pieces, the transcript, and writing the notes in stages with a model whose
// window is smaller than the meeting - all without a model or a microphone.
[TestFixture]
public class UnitTestMeetingNotes
{
    private const int Rate = 16000;

    // A tone, with a quiet gap at a given second.
    private static float[] Speech(double seconds, double quietAt = -1)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            bool quiet = quietAt >= 0 && Math.Abs(t - quietAt) < 0.15;
            samples[i] = quiet ? 0 : (float)(0.3 * Math.Sin(2 * Math.PI * 220 * t));
        }
        return samples;
    }

    [Test]
    public void CutsTheRecordingAtTheQuietestMoment()
    {
        var chunker = new MeetingAudioChunker(Rate, 30, 50);
        float[] audio = Speech(110, quietAt: 41);
        var chunks = new List<MeetingAudioChunk>();
        // As a microphone hands it over: 100 ms at a time.
        for (int at = 0; at < audio.Length; at += Rate / 10)
            chunks.AddRange(chunker.Add(audio.Skip(at).Take(Math.Min(Rate / 10, audio.Length - at)).ToArray()));
        Assert.That(chunks.Count, Is.EqualTo(2));
        Assert.That(chunks[0].Start, Is.EqualTo(TimeSpan.Zero));
        Assert.That(chunks[0].Duration.TotalSeconds, Is.EqualTo(41).Within(0.1), "cut in the quiet gap");
        Assert.That(chunks[1].Start.TotalSeconds, Is.EqualTo(41).Within(0.1));
        Assert.That(chunks[1].Duration.TotalSeconds, Is.InRange(30, 50));
        MeetingAudioChunk last = chunker.Flush();
        Assert.That(last, Is.Not.Null);
        Assert.That((last.Start + last.Duration).TotalSeconds, Is.EqualTo(110).Within(0.01), "nothing lost");
        Assert.That(chunker.Recorded.TotalSeconds, Is.EqualTo(110).Within(0.01));
        Assert.That(chunker.Flush(), Is.Null);
    }

    [Test]
    public void TellsSilenceFromSpeechAndWritesWav()
    {
        var silent = new MeetingAudioChunk(TimeSpan.Zero, new float[Rate * 2], Rate);
        var spoken = new MeetingAudioChunk(TimeSpan.Zero, Speech(2), Rate);
        Assert.That(silent.IsSilent, Is.True);
        Assert.That(spoken.IsSilent, Is.False);
        byte[] wav = spoken.ToWav();
        Assert.That(wav.Length, Is.EqualTo(44 + Rate * 2 * 2));
        Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(Rate));
    }

    [Test]
    public void KeepsThePiecesInOrderAndSplitsIntoSections()
    {
        var transcript = new MeetingTranscript();
        transcript.Add(TimeSpan.FromSeconds(45), "Second   piece.");
        transcript.Add(TimeSpan.Zero, "First piece.");
        transcript.Add(TimeSpan.FromSeconds(90), "  ");
        transcript.Add(TimeSpan.FromMinutes(61), "Late piece.");
        Assert.That(transcript.Text(), Is.EqualTo("[0:00] First piece.\n[0:45] Second piece.\n[1:01:00] Late piece."));
        Assert.That(transcript.WordCount, Is.EqualTo(6));
        Assert.That(transcript.Tail(12), Is.EqualTo("…Late piece."));

        Func<string, int> words = text => text.Split(' ').Length;
        var sections = transcript.Sections(6, words);
        Assert.That(sections.Select(s => s.Start), Is.EqualTo(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(61) }));
        Assert.That(transcript.Sections(100, words).Count, Is.EqualTo(1));
    }

    private static MeetingTranscript LongMeeting(int pieces)
    {
        var transcript = new MeetingTranscript();
        for (int i = 0; i < pieces; i++)
            transcript.Add(TimeSpan.FromSeconds(40 * i), "Piece " + i + " " + string.Join(" ", Enumerable.Repeat("word", 40)));
        return transcript;
    }

    [Test]
    public async Task AShortMeetingIsWrittenUpInOneRequest()
    {
        var prompts = new List<string>();
        string notes = await MeetingNotes.WriteAsync(LongMeeting(3), "Standup", TimeSpan.FromMinutes(2),
            (prompt, max, token) => { prompts.Add(prompt); return Task.FromResult("```markdown\n## Summary\nAll good.\n```"); },
            text => text.Length / 4, 2000);
        Assert.That(prompts.Count, Is.EqualTo(1));
        Assert.That(prompts[0], Does.Contain("Transcript:\n[0:00] Piece 0").And.Contain("## Action items").And.Contain("\"Standup\"").And.Contain("2 minutes"));
        Assert.That(notes, Is.EqualTo("## Summary\nAll good."), "the code fence is taken off");
        Assert.That(await MeetingNotes.WriteAsync(new MeetingTranscript(), null, TimeSpan.Zero, (p, m, t) => Task.FromResult("x"), t => 1, 2000), Is.Null);
    }

    [Test]
    public async Task ALongMeetingIsSummarizedInSectionsFirst()
    {
        var prompts = new List<(string Prompt, int Max)>();
        var stages = new List<string>();
        var progress = new SynchronousProgress(stages.Add);
        // ~50 tokens a piece, 600 to a request: ~7 pieces a section.
        string notes = await MeetingNotes.WriteAsync(LongMeeting(40), null, TimeSpan.FromMinutes(27),
            (prompt, max, token) =>
            {
                prompts.Add((prompt, max));
                return Task.FromResult(prompt.StartsWith("Below is part") ? "- a point" : "## Summary\nDone.");
            },
            text => text.Length / 4, 600, progress);
        int sections = prompts.Count(p => p.Prompt.StartsWith("Below is part"));
        Assert.That(sections, Is.GreaterThan(3));
        Assert.That(prompts.Take(sections).All(p => p.Max == MeetingNotes.SectionSummaryTokens));
        Assert.That(prompts[0].Prompt, Does.Contain("part 1 of " + sections));
        Assert.That(prompts.Last().Prompt, Does.StartWith("Below are summaries of the parts").And.Contain("Part " + sections + ":\n(from "));
        Assert.That(prompts.Last().Max, Is.EqualTo(MeetingNotes.NotesTokens));
        Assert.That(notes, Is.EqualTo("## Summary\nDone."));
        Assert.That(stages.First(), Is.EqualTo("Summarizing part 1 of " + sections + "…"));
        Assert.That(stages.Last(), Is.EqualTo("Writing the notes…"));
    }

    [Test]
    public async Task SummariesThatDontFitAreCombinedFirst()
    {
        var prompts = new List<string>();
        string longSummary = string.Join(" ", Enumerable.Repeat("detail", 40));
        await MeetingNotes.WriteAsync(LongMeeting(60), null, TimeSpan.FromMinutes(40),
            (prompt, max, token) =>
            {
                prompts.Add(prompt);
                return Task.FromResult(prompt.StartsWith("Below is part") ? longSummary : prompt.StartsWith("Below are summaries of consecutive") ? "- combined" : "## Summary");
            },
            text => text.Length / 4, 600);
        Assert.That(prompts.Count(p => p.StartsWith("Below are summaries of consecutive")), Is.GreaterThan(0));
        Assert.That(prompts.Last(), Does.StartWith("Below are summaries of the parts").And.Contain("- combined"));
    }

    [Test]
    public void TheDocumentAndTheResultForTheModel()
    {
        var transcript = new MeetingTranscript();
        transcript.Add(TimeSpan.Zero, "Let's start.");
        transcript.Add(TimeSpan.FromSeconds(42), "Sam will send the budget by Friday.");
        string markdown = MeetingNotes.DocumentMarkdown("Budget review", new DateTime(2026, 10, 24, 14, 30, 0), TimeSpan.FromSeconds(75),
            "# Notes\n\n## Summary\nThe budget.", transcript);
        Assert.That(markdown, Does.StartWith("# Budget review\n\nSaturday 24 October 2026, 14:30 · 1:15\n\n## Summary\nThe budget."));
        Assert.That(markdown, Does.Contain("## Transcript").And.Contain("**0:42** Sam will send the budget by Friday."));
        Assert.That(DocumentWriter.Create("Budget review", markdown, "word").Data.Length, Is.GreaterThan(1000));

        string result = AgentTools.MeetingResultText(TimeSpan.FromSeconds(75), 9, "## Summary\nThe budget.", "Budget review.docx", null);
        Assert.That(result, Does.Contain("Recorded 1:15").And.Contain("\"Budget review.docx\"").And.Contain("don't repeat them").And.EndWith("The budget."));
        Assert.That(AgentTools.MeetingResultText(TimeSpan.FromSeconds(5), 0, null, null, "No speech was heard."), Does.Contain("No speech was heard. Tell the user"));
        Assert.That(AgentTools.RecordMeeting((title, token) => Task.FromResult(title)).Name, Is.EqualTo("record_meeting"));
    }

    private sealed class SynchronousProgress : IProgress<string>
    {
        private readonly Action<string> _report;
        public SynchronousProgress(Action<string> report) { _report = report; }
        public void Report(string value) => _report(value);
    }
}
