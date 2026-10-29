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

    // Speaker labels: with pauseSeconds, a piece also ends at a pause once long enough; not at a shorter one, and the
    // 30-50 s rule still holds for a long turn.
    [Test]
    public void WithSpeakersPiecesEndAtPauses()
    {
        var chunker = new MeetingAudioChunker(Rate, 30, 50, pauseSeconds: 2);
        var audio = new List<float>();
        audio.AddRange(Speech(1.2));            // too short to end at the next pause
        audio.AddRange(new float[Rate * 2 / 5]); // 400 ms pause
        audio.AddRange(Speech(3));
        audio.AddRange(new float[Rate / 2]);     // 500 ms pause: the cut
        audio.AddRange(Speech(60));              // one long turn
        var chunks = new List<MeetingAudioChunk>();
        float[] all = audio.ToArray();
        for (int at = 0; at < all.Length; at += Rate / 10)
            chunks.AddRange(chunker.Add(all.Skip(at).Take(Math.Min(Rate / 10, all.Length - at)).ToArray()));
        Assert.That(chunks.Count, Is.EqualTo(2));
        Assert.That(chunks[0].Duration.TotalSeconds, Is.EqualTo(1.2 + 0.4 + 3 + 0.15).Within(0.05), "at the second pause, 150 ms in");
        Assert.That(chunks[1].Duration.TotalSeconds, Is.InRange(30, 50), "a long turn is still cut at 50 s at most");
        Assert.That((chunks[1].Start + chunks[1].Duration).TotalSeconds, Is.LessThan(all.Length / (double)Rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeetingAudioChunker(Rate, 30, 50, pauseSeconds: 40));
    }

    // A unit vector near one of three "voices".
    private static float[] Voice(int voice, int variant)
    {
        var v = new float[16];
        v[voice] = 1;
        v[8 + variant % 8] = 0.25f;
        double length = Math.Sqrt(v.Sum(x => x * x));
        return v.Select(x => (float)(x / length)).ToArray();
    }

    [Test]
    public void ClustersVoicesAndFoldsTinySpeakers()
    {
        var prints = new List<float[]> { Voice(0, 0), Voice(1, 1), Voice(0, 2), Voice(2, 3), Voice(1, 4), Voice(0, 5) };
        Assert.That(SpeakerClustering.Cluster(prints), Is.EqualTo(new[] { 0, 1, 0, 2, 1, 0 }));
        // Voice 2 spoke for 1 s only, but it is like no one else: it stays a speaker. A short odd piece of voice 0 (its
        // fingerprint between voices 0 and 1, nearer 0) joins voice 0.
        Assert.That(SpeakerClustering.Cluster(prints, seconds: new double[] { 5, 5, 5, 1, 5, 5 }).Distinct().Count(), Is.EqualTo(3));
        float[] odd = Voice(0, 6).Zip(Voice(1, 6), (a, b) => 0.75f * a + 0.45f * b).ToArray();
        float length = (float)Math.Sqrt(odd.Sum(x => x * x));
        var withOdd = new List<float[]>(prints) { odd.Select(x => x / length).ToArray() };
        int[] folded = SpeakerClustering.Cluster(withOdd, seconds: new double[] { 5, 5, 5, 6, 5, 5, 1 });
        Assert.That(folded.Distinct().Count(), Is.EqualTo(3));
        Assert.That(folded[6], Is.EqualTo(folded[0]));

        // Pieces in a row that sound alike are one turn first: three short, unsteady pieces of one voice (each alone too
        // little speech) are one speaker rather than three, and the voice after them is another.
        var sequence = new List<float[]> { Voice(0, 0), Voice(0, 1), Voice(1, 2), Voice(1, 3), Voice(1, 4), Voice(0, 5) };
        Assert.That(SpeakerClustering.ClusterSequence(sequence, new double[] { 4, 4, 2, 2, 2, 4 }), Is.EqualTo(new[] { 0, 0, 1, 1, 1, 0 }));
        Assert.That(SpeakerClustering.Cluster(new List<float[]>()), Is.Empty);
        Assert.That(SpeakerClustering.Cluster(new List<float[]> { Voice(0, 0) }), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void TheTranscriptSaysWhoSpoke()
    {
        var transcript = new MeetingTranscript();
        transcript.Add(TimeSpan.Zero, "Good morning.", Voice(0, 0), 4);
        transcript.Add(TimeSpan.FromSeconds(4), "Let's start with the budget.", Voice(0, 1), 5);
        transcript.Add(TimeSpan.FromSeconds(9), "Sam will send it by Friday.", Voice(1, 2), 6);
        transcript.Add(TimeSpan.FromSeconds(15), "Thanks.", Voice(0, 3), 3);
        Assert.That(transcript.Text(), Does.Not.Contain("Speaker"), "no labels before LabelSpeakers");
        Assert.That(transcript.LabelSpeakers(), Is.EqualTo(2));
        Assert.That(transcript.Text(), Is.EqualTo(
            "[0:00] Speaker 1: Good morning. Let's start with the budget.\n[0:09] Speaker 2: Sam will send it by Friday.\n[0:15] Speaker 1: Thanks."));
        Assert.That(transcript.Text(false), Does.Not.Contain("Speaker"));
        Assert.That(transcript.Sections(1000, t => t.Split(' ').Length).Single().Text, Does.Contain("Speaker 2: Sam"));
        string markdown = MeetingNotes.DocumentMarkdown("Budget", new DateTime(2026, 10, 24, 14, 30, 0), TimeSpan.FromSeconds(18), null, transcript);
        Assert.That(markdown, Does.Contain("**0:09** **Speaker 2:** Sam will send it by Friday.").And.Contain("told apart by voice"));
        Assert.That(MeetingNotes.NotesPrompt("Budget", TimeSpan.FromSeconds(18), transcript.Text(), null, speakers: true),
            Does.Contain("told apart by their voices").And.Not.Contain("doesn't say who is speaking"));
        Assert.That(MeetingNotes.NotesPrompt("Budget", TimeSpan.FromSeconds(18), "x", null), Does.Contain("doesn't say who is speaking"));

        // One voice: no labels.
        var alone = new MeetingTranscript();
        alone.Add(TimeSpan.Zero, "Hello.", Voice(0, 0), 4);
        alone.Add(TimeSpan.FromSeconds(5), "Bye.", Voice(0, 1), 4);
        Assert.That(alone.LabelSpeakers(), Is.EqualTo(1));
        Assert.That(alone.Text(), Is.EqualTo("[0:00] Hello.\n[0:05] Bye."));
    }

    // What writes a meeting down: the model on a desktop and the recognizer on a phone by default, the other when the
    // choice can't be done here, nothing when neither can.
    [Test]
    public void ChoosesWhatWritesTheMeetingDown()
    {
        Assert.That(MeetingTranscription.Choose(null, true, true, isDesktop: true), Is.EqualTo(MeetingTranscriber.Model));
        Assert.That(MeetingTranscription.Choose(null, true, true, isDesktop: false), Is.EqualTo(MeetingTranscriber.Recognizer));
        Assert.That(MeetingTranscription.Choose(null, false, true, false), Is.EqualTo(MeetingTranscriber.Model), "no recognizer: the model");
        Assert.That(MeetingTranscription.Choose(MeetingTranscriber.Model, true, false, true), Is.EqualTo(MeetingTranscriber.Recognizer), "a model without audio");
        Assert.That(MeetingTranscription.Choose(MeetingTranscriber.RecognizerThenModel, false, true, false), Is.EqualTo(MeetingTranscriber.Model));
        Assert.That(MeetingTranscription.Choose(MeetingTranscriber.RecognizerThenModel, true, true, false), Is.EqualTo(MeetingTranscriber.RecognizerThenModel));
        Assert.That(MeetingTranscription.Choose(null, false, false, true), Is.Null);
        foreach (MeetingTranscriber? choice in new MeetingTranscriber?[] { null, MeetingTranscriber.Recognizer, MeetingTranscriber.Model, MeetingTranscriber.RecognizerThenModel })
            Assert.That(MeetingTranscription.FromSetting(MeetingTranscription.ToSetting(choice)), Is.EqualTo(choice));
        Assert.That(MeetingTranscription.FromSetting("7"), Is.Null);
    }

    [Test]
    public void CleansWhatTheModelWroteDown()
    {
        Assert.That(MeetingTranscription.Clean("Transcript: \"Good morning everyone.\""), Is.EqualTo("Good morning everyone."));
        Assert.That(MeetingTranscription.Clean("  Sounds good.\n Thanks everyone. "), Is.EqualTo("Sounds good. Thanks everyone."));
        foreach (string nothing in new[] { "(silence)", "[no speech]", "No speech.", "There is no speech in this recording.", "" , null })
            Assert.That(MeetingTranscription.Clean(nothing), Is.Empty, nothing);
        Assert.That(MeetingTranscription.Clean("(laughs) That's great."), Is.EqualTo("(laughs) That's great."));
        // Gemma 4 E2B's answer to a piece of room noise.
        Assert.That(MeetingTranscription.Clean("I'm sorry, but I cannot transcribe the speech in the recording. Please provide the audio."), Is.Empty);
        Assert.That(MeetingTranscription.Clean("I'm sorry, I can't make it on Friday."), Is.EqualTo("I'm sorry, I can't make it on Friday."));
        Assert.That(MeetingTranscription.Clean("We need the recording of the demo by Friday."), Is.Not.Empty);
    }

    // Speech comes and goes; a room's steady noise, or quiet, isn't speech.
    [Test]
    public void TellsSpeechFromRoomNoise()
    {
        var random = new Random(5);
        float[] noise = Enumerable.Range(0, Rate * 3).Select(_ => (float)((random.NextDouble() - 0.5) * 0.05)).ToArray();
        float[] syllables = Enumerable.Range(0, Rate * 3).Select(i =>
            (float)((i / (Rate / 4)) % 2 == 0 ? 0.3 * Math.Sin(2 * Math.PI * 180 * i / Rate) : 0.002 * (random.NextDouble() - 0.5))).ToArray();
        Assert.That(new MeetingAudioChunk(TimeSpan.Zero, noise, Rate).HasSpeech, Is.False, "steady noise");
        Assert.That(new MeetingAudioChunk(TimeSpan.Zero, syllables, Rate).HasSpeech, Is.True);
        Assert.That(new MeetingAudioChunk(TimeSpan.Zero, syllables.Select(x => x * 0.01f).ToArray(), Rate).HasSpeech, Is.False, "too quiet");
        Assert.That(new MeetingAudioChunk(TimeSpan.Zero, new float[100], Rate).HasSpeech, Is.False);
    }

    [Test]
    public void APieceCanBeWrittenDownAgain()
    {
        var transcript = new MeetingTranscript();
        transcript.Add(TimeSpan.Zero, "Good morning", Voice(0, 0), 8);
        transcript.Add(TimeSpan.FromSeconds(9), "the video labs", Voice(1, 1), 8);
        transcript.Replace(TimeSpan.FromSeconds(9), "the video ads");
        transcript.Replace(TimeSpan.FromSeconds(9), "  ");
        transcript.Replace(TimeSpan.FromSeconds(20), "Thanks everyone.");
        Assert.That(transcript.Text(), Is.EqualTo("[0:00] Good morning\n[0:09] the video ads\n[0:20] Thanks everyone."));
        Assert.That(transcript.LabelSpeakers(), Is.EqualTo(2), "the rewritten piece keeps its voice");
        Assert.That(transcript.LabelledSegments[1].Speaker, Is.EqualTo("Speaker 2"));
    }

    private sealed class SynchronousProgress : IProgress<string>
    {
        private readonly Action<string> _report;
        public SynchronousProgress(Action<string> report) { _report = report; }
        public void Report(string value) => _report(value);
    }
}
