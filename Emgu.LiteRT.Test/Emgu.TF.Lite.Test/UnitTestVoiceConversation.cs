using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emgu.LiteRT.LM.Agents.Audio;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The voice conversation's speech detection and the streaming read-aloud's sentence splitting (the MAUI demo's
// VoiceConversation.cs); no model or native library.
[TestFixture]
public class UnitTestVoiceConversation
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(30);

    // Frame levels (dBFS) of 16 kHz 16-bit samples, 30 ms each, as the Android recorder measures them.
    private static List<double> Levels(short[] samples)
    {
        int frame = 16000 * 30 / 1000;
        List<double> levels = new List<double>();
        for (int start = 0; start + frame <= samples.Length; start += frame)
        {
            double sum = 0;
            for (int i = start; i < start + frame; i++)
                sum += (double)samples[i] * samples[i];
            levels.Add(VoiceActivityDetector.LevelDb(sum, frame));
        }
        return levels;
    }

    // Quiet background noise (about -60 dBFS), reproducible.
    private static short[] Noise(double seconds, int seed)
    {
        Random random = new Random(seed);
        short[] samples = new short[(int)(16000 * seconds)];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)random.Next(-60, 61);
        return samples;
    }

    private static List<(VoiceActivity Activity, double Seconds)> Run(VoiceActivityDetector detector, IEnumerable<double> levels)
    {
        var events = new List<(VoiceActivity, double)>();
        int index = 0;
        foreach (double level in levels)
        {
            index++;
            VoiceActivity activity = detector.Process(level, Frame);
            if (activity != VoiceActivity.None)
                events.Add((activity, index * Frame.TotalSeconds));
        }
        return events;
    }

    [Test]
    public void DetectsTheStartAndEndOfRecordedSpeech()
    {
        // LiteRT-LM's 5.8 s speech sample (quiet for its first 1.2 s, speech until about 5.4 s), with 2 s of background
        // noise before and 3 s after it.
        byte[] wav = File.ReadAllBytes("audio_sample.wav");
        short[] speech = new short[(wav.Length - 44) / 2];
        Buffer.BlockCopy(wav, 44, speech, 0, speech.Length * 2);
        short[] samples = Noise(2, 1).Concat(speech).Concat(Noise(3, 2)).ToArray();
        double speechStart = 2 + 1.2, speechEnd = 2 + 5.4;

        var events = Run(new VoiceActivityDetector(), Levels(samples));
        TestContext.Out.WriteLine(string.Join(", ", events.Select(e => e.Activity + " " + e.Seconds.ToString("0.00"))));

        Assert.That(events.Count, Is.EqualTo(2), "one utterance");
        Assert.That(events[0].Activity, Is.EqualTo(VoiceActivity.SpeechStarted));
        Assert.That(events[0].Seconds, Is.InRange(speechStart, speechStart + 0.4), "after the minimum speech length");
        Assert.That(events[1].Activity, Is.EqualTo(VoiceActivity.SpeechEnded));
        Assert.That(events[1].Seconds, Is.InRange(speechEnd + 0.5, speechEnd + 1.3), "after the closing pause");
    }

    [Test]
    public void IgnoresClicksAndAdaptsToSteadyNoise()
    {
        var detector = new VoiceActivityDetector();
        var levels = new List<double>();
        levels.AddRange(Enumerable.Repeat(-65.0, 30));
        // A click: 60 ms loud.
        levels.AddRange(Enumerable.Repeat(-20.0, 2));
        levels.AddRange(Enumerable.Repeat(-65.0, 30));
        // A noise that grows slowly (25 dB in 12 s) to a steady -40 dBFS becomes the background rather than speech. (One
        // that jumps up is taken as speech, until it stops or the utterance reaches its length limit.)
        for (int i = 0; i < 400; i++) levels.Add(-65 + 25.0 * i / 400);
        levels.AddRange(Enumerable.Repeat(-40.0, 300));
        Assert.That(Run(detector, levels), Is.Empty);
        Assert.That(detector.NoiseFloorDb, Is.EqualTo(-40).Within(1));

        // Speech over the fan, 2 s, then quiet: one utterance.
        levels = Enumerable.Repeat(-22.0, 66).Concat(Enumerable.Repeat(-40.0, 60)).ToList();
        var events = Run(detector, levels);
        Assert.That(events.Select(e => e.Activity), Is.EqualTo(new[] { VoiceActivity.SpeechStarted, VoiceActivity.SpeechEnded }));
    }

    [Test]
    public void EndsSpeechInANoisyRoomAfterDigitalSilence()
    {
        // As recorded on a Pixel Fold: the recording starts with digital silence, the room is -57 to -47 dBFS with an
        // occasional louder blip, and speech is -30 to -19 dBFS. The background must not stay at the digital silence,
        // where only the -50 dBFS minimum would tell speech from the room's peaks.
        Random random = new Random(3);
        var levels = new List<double> { -160, -80 };
        void Room(int frames)
        {
            for (int i = 0; i < frames; i++)
                levels.Add(i % 25 == 24 ? -44 : -57 + 10 * random.NextDouble());
        }
        Room(20);
        int speechStart = levels.Count;
        for (int i = 0; i < 67; i++)
            levels.Add(i % 9 == 4 ? -41 : -30 + 11 * random.NextDouble());
        int speechEnd = levels.Count;
        Room(100);

        var events = Run(new VoiceActivityDetector(), levels);
        TestContext.Out.WriteLine(string.Join(", ", events.Select(e => e.Activity + " " + e.Seconds.ToString("0.00"))));
        Assert.That(events.Select(e => e.Activity), Is.EqualTo(new[] { VoiceActivity.SpeechStarted, VoiceActivity.SpeechEnded }));
        Assert.That(events[0].Seconds, Is.InRange(speechStart * Frame.TotalSeconds, (speechStart + 10) * Frame.TotalSeconds));
        Assert.That(events[1].Seconds - speechEnd * Frame.TotalSeconds, Is.InRange(0.85, 1.2), "ended by the pause");
    }

    [Test]
    public void ShortPausesDoNotEndAnUtteranceButTheLengthLimitDoes()
    {
        var detector = new VoiceActivityDetector { MaxUtterance = TimeSpan.FromSeconds(5) };
        var levels = new List<double>(Enumerable.Repeat(-60.0, 30));
        for (int word = 0; word < 6; word++)
        {
            levels.AddRange(Enumerable.Repeat(-25.0, 15));   // 450 ms word
            levels.AddRange(Enumerable.Repeat(-60.0, 15));   // 450 ms pause
        }
        var events = Run(detector, levels);
        Assert.That(events.Select(e => e.Activity), Is.EqualTo(new[] { VoiceActivity.SpeechStarted, VoiceActivity.SpeechEnded }));
        Assert.That(events[1].Seconds, Is.EqualTo(0.9 + 5).Within(0.3), "ended by the 5 s limit");
    }

    [Test]
    public void SplitsStreamedSentences()
    {
        var segmenter = new SpeechSegmenter();
        var pieces = new List<string>();
        string reply = "Paris is the capital of France. It has about 2.1 million people! Want to know more?";
        // Streamed a few characters at a time: a piece is returned once the next one has started.
        for (int length = 1; length <= reply.Length; length += 3)
            pieces.AddRange(segmenter.Add(reply.Substring(0, length)));
        Assert.That(pieces, Is.EqualTo(new[] { "Paris is the capital of France.", "It has about 2.1 million people!" }));
        pieces.AddRange(segmenter.Finish(reply));
        Assert.That(pieces.Last(), Is.EqualTo("Want to know more?"));
        Assert.That(pieces.Count, Is.EqualTo(3));
    }

    [Test]
    public void SkipsCodeAndTablesAndBreaksAtLines()
    {
        string reply = "## Steps\n1. Install it\n2. Run it. Then wait\n```python\nprint(\"hello. world\")\n```\n" +
            "| Name | Size |\n|---|---|\n| a | 1 |\nThat's all。谢谢！";
        var segmenter = new SpeechSegmenter();
        var pieces = new List<string>();
        for (int length = 1; length <= reply.Length; length++)
            pieces.AddRange(segmenter.Add(reply.Substring(0, length)));
        pieces.AddRange(segmenter.Finish(reply));
        Assert.That(pieces, Is.EqualTo(new[]
        {
            "## Steps", "1. Install it", "2. Run it.", "Then wait", "That's all。", "谢谢！"
        }));
    }
}
