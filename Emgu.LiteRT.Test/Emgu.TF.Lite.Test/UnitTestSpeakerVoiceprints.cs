// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Telling speakers apart: the Kaldi features, and a three-voice meeting made with macOS's voices (skipped elsewhere) cut as
// the meeting recorder cuts it, fingerprinted with WeSpeaker (downloads 13.4 MB) and labelled - all but the speech recognizer.
[TestFixture]
public class UnitTestKaldiFbank
{
    [Test]
    public void FramesAndAToneInItsMelBin()
    {
        var fbank = new KaldiFbank();
        Assert.That(fbank.FrameCount(SpeakerEmbedder.InputSamples), Is.EqualTo(SpeakerEmbedder.Frames), "5.015 s is 500 frames");
        Assert.That(fbank.FrameCount(399), Is.EqualTo(0));
        var tone = new float[16000];
        for (int i = 0; i < tone.Length; i++) tone[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 16000.0));
        float[] features = fbank.Compute(tone, 0, tone.Length);
        Assert.That(features.Length, Is.EqualTo(fbank.FrameCount(16000) * 80));
        // The loudest bin of a middle frame: the one whose centre is nearest 1 kHz on the mel scale.
        float[] frame = features.Skip(50 * 80).Take(80).ToArray();
        int loudest = Array.IndexOf(frame, frame.Max());
        double mel(double hz) => 1127 * Math.Log(1 + hz / 700);
        double delta = (mel(8000) - mel(20)) / 81;
        int expected = (int)Math.Round((mel(1000) - mel(20)) / delta) - 1;
        Assert.That(loudest, Is.InRange(expected - 1, expected + 1));
        float[] silence = fbank.Compute(new float[16000], 0, 16000);
        Assert.That(silence.Max(), Is.EqualTo((float)Math.Log(1.1920928955078125e-07)).Within(1e-3), "floored at float's epsilon");
    }
}

[TestFixture, Category("Native")]
public class UnitTestSpeakerVoiceprints
{
    private static readonly (string Voice, string Text)[] Turns =
    {
        ("Daniel", "Good morning everyone. Let's start with the launch plan for the new app, since the date is coming up fast."),
        ("Samantha", "Thanks. The beta went well. We had about two hundred testers and the crash rate was under one percent."),
        ("Karen", "That's great news. Did anyone report problems with the camera feature on older phones?"),
        ("Daniel", "A few did. The preview was slow on some Galaxy models, so we lowered the frame rate there."),
        ("Samantha", "I think we should also mention it in the release notes, just so people know what changed."),
        ("Karen", "Agreed. I can write that section today and send it around this afternoon."),
        ("Daniel", "Perfect. Then the last thing is the marketing budget. We still need a decision on the video ads."),
        ("Samantha", "My vote is to start small, maybe two weeks, and see how many installs we get before spending more."),
        ("Karen", "Same here. Let's review the numbers at our next meeting on Thursday."),
    };

    // The meeting at 16 kHz with a faint hiss, 350 ms between turns; and who speaks from which sample.
    private static (float[] Audio, List<(int Start, string Voice)> Turns) Meeting()
    {
        if (!File.Exists("/usr/bin/say"))
            Assert.Ignore("Needs macOS's say command for the voices.");
        string dir = Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "Meeting");
        Directory.CreateDirectory(dir);
        var audio = new List<float>();
        var turns = new List<(int, string)>();
        var random = new Random(7);
        for (int i = 0; i < Turns.Length; i++)
        {
            string aiff = Path.Combine(dir, i + ".aiff"), wav = Path.Combine(dir, i + ".wav");
            Run("/usr/bin/say", "-v", Turns[i].Voice, "-o", aiff, Turns[i].Text);
            Run("/usr/bin/afconvert", "-f", "WAVE", "-d", "LEI16@16000", "-c", "1", aiff, wav);
            byte[] bytes = File.ReadAllBytes(wav);
            int data = 12;
            while (!(bytes[data] == 'd' && bytes[data + 1] == 'a' && bytes[data + 2] == 't' && bytes[data + 3] == 'a'))
                data += 8 + BitConverter.ToInt32(bytes, data + 4);
            int count = BitConverter.ToInt32(bytes, data + 4) / 2;
            turns.Add((audio.Count, Turns[i].Voice));
            for (int k = 0; k < count; k++) audio.Add(BitConverter.ToInt16(bytes, data + 8 + 2 * k) / 32768f);
            for (int k = 0; k < 16000 * 35 / 100; k++) audio.Add(0);
        }
        float[] result = audio.ToArray();
        for (int k = 0; k < result.Length; k++) result[k] += (float)((random.NextDouble() - 0.5) * 0.004);
        return (result, turns);
    }

    private static void Run(string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (string a in arguments) start.ArgumentList.Add(a);
        using Process process = Process.Start(start);
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.EqualTo(0), file);
    }

    [Test]
    public async Task AMeetingsSpeakersAreToldApart()
    {
        (float[] audio, List<(int Start, string Voice)> turns) = Meeting();
        using var voices = new SpeakerVoiceprints(Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "SpeakerDiarization"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await voices.PrepareAsync(false, 4, null, timeout.Token);
        Assert.That(voices.Backend, Is.EqualTo("CPU"));
        Assert.That(voices.Voiceprint(new float[8000]), Is.Null, "too short");

        // Cut as the recorder does with speakers on, 100 ms at a time.
        var chunker = new MeetingAudioChunker(16000, 30, 50, pauseSeconds: 2);
        var chunks = new List<MeetingAudioChunk>();
        for (int at = 0; at < audio.Length; at += 1600)
            chunks.AddRange(chunker.Add(audio.Skip(at).Take(Math.Min(1600, audio.Length - at)).ToArray()));
        MeetingAudioChunk last = chunker.Flush();
        if (last != null) chunks.Add(last);
        Assert.That(chunks.Count, Is.EqualTo(Turns.Length), "a piece per turn");

        var transcript = new MeetingTranscript();
        var truth = new List<string>();
        foreach (MeetingAudioChunk chunk in chunks)
        {
            int middle = (int)(chunk.Start.TotalSeconds * 16000) + chunk.Samples.Length / 2;
            string voice = turns.Last(t => t.Start <= middle).Voice;
            truth.Add(voice);
            transcript.Add(chunk.Start, voice, voices.Voiceprint(chunk.Samples), chunk.Duration.TotalSeconds);
        }
        Assert.That(transcript.LabelSpeakers(), Is.EqualTo(3));
        var labelled = transcript.LabelledSegments;
        var byVoice = truth.Select((v, i) => (v, labelled[i].Speaker)).GroupBy(x => x.v).ToDictionary(g => g.Key, g => g.Select(x => x.Speaker).Distinct().ToList());
        Assert.That(byVoice.Values.All(s => s.Count == 1), Is.True, "each voice is one speaker");
        Assert.That(byVoice.Values.Select(s => s[0]).Distinct().Count(), Is.EqualTo(3), "and three different ones");
        Assert.That(byVoice["Daniel"][0], Is.EqualTo("Speaker 1"), "numbered in the order they first spoke");
        TestContext.WriteLine(transcript.Text());
    }

    // The GPU (FP16) gives the CPU's fingerprints; PrepareAsync checks it the same way on a device.
    [Test, Explicit("Needs the GPU accelerator")]
    public async Task TheGpuGivesTheCpusFingerprints()
    {
        using var voices = new SpeakerVoiceprints(Path.Combine(Path.GetTempPath(), "Emgu.LiteRT.Tests", "SpeakerDiarization"));
        await voices.PrepareAsync(true, 4, null, CancellationToken.None);
        Assert.That(voices.Backend, Does.StartWith("GPU"), voices.GpuFailure);
    }
}
