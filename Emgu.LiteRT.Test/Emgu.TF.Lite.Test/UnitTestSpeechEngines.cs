// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Choosing what writes down a recording (the device's recognizer or Parakeet), and reading a recording's samples.
[TestFixture]
public class UnitTestSpeechEngines
{
    [Test]
    public void ParakeetWhenChosenAndItKnowsTheLanguage()
    {
        // Chosen, and English (the device's) or a chosen French: Parakeet.
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, true, true, null, "en-US"), Is.EqualTo(SpeechEngine.Parakeet));
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, true, true, "fr-FR", "ja-JP"), Is.EqualTo(SpeechEngine.Parakeet));
        // Japanese, chosen or the device's: the device's recognizer.
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, true, true, "ja-JP", "en-US"), Is.EqualTo(SpeechEngine.System));
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, true, true, null, "zh-Hans-CN"), Is.EqualTo(SpeechEngine.System));
        // Not chosen, or not in the app: the device's.
        Assert.That(SpeechEngines.Choose(SpeechEngine.System, true, true, null, "en-US"), Is.EqualTo(SpeechEngine.System));
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, true, false, null, "en-US"), Is.EqualTo(SpeechEngine.System));
        // No recognizer of the device's (Windows): Parakeet even when not chosen, for the languages it knows; else nothing.
        Assert.That(SpeechEngines.Choose(SpeechEngine.System, false, true, null, "de-DE"), Is.EqualTo(SpeechEngine.Parakeet));
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, false, true, "ja-JP", "en-US"), Is.Null);
        Assert.That(SpeechEngines.Choose(SpeechEngine.Parakeet, false, false, null, "en-US"), Is.Null);
    }

    [Test]
    public void KnowsParakeetsLanguages()
    {
        Assert.That(SpeechEngines.ParakeetLanguages.Count, Is.EqualTo(25));
        foreach (string tag in new[] { "en-US", "pt-BR", "uk", "mt-MT", "es_419" })
            Assert.That(SpeechEngines.ParakeetCovers(tag), Is.True, tag);
        foreach (string tag in new[] { "zh-Hans", "cmn-Hans-CN", "ja", "nb-NO", "ko-KR", "", null })
            Assert.That(SpeechEngines.ParakeetCovers(tag), Is.False, tag ?? "null");
    }

    [Test]
    public void TheSettingDefaultsToTheDevicesRecognizerWhereThereIsOne()
    {
        Assert.That(SpeechEngines.Parse(null, true), Is.EqualTo(SpeechEngine.System));
        Assert.That(SpeechEngines.Parse("", false), Is.EqualTo(SpeechEngine.Parakeet));
        Assert.That(SpeechEngines.Parse(SpeechEngines.ToSetting(SpeechEngine.Parakeet), true), Is.EqualTo(SpeechEngine.Parakeet));
        Assert.That(SpeechEngines.Parse(SpeechEngines.ToSetting(SpeechEngine.System), false), Is.EqualTo(SpeechEngine.System));
    }

    // A WAV file with the chunks given after "fmt ".
    private static byte[] Wav(int channels, short[] samples, params (string Id, byte[] Data)[] before)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(16000);
        writer.Write(16000 * 2 * channels);
        writer.Write((short)(2 * channels));
        writer.Write((short)16);
        foreach (var (id, data) in before)
        {
            writer.Write(Encoding.ASCII.GetBytes(id));
            writer.Write(data.Length);
            writer.Write(data);
            if ((data.Length & 1) == 1) writer.Write((byte)0);
        }
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (short sample in samples) writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }

    [Test]
    public void ReadsARecordingsSamples()
    {
        float[] plain = SpeechEngines.WavSamples(Wav(1, new short[] { 0, 16384, -32768 }), out int rate);
        Assert.That(rate, Is.EqualTo(16000));
        Assert.That(plain, Is.EqualTo(new[] { 0f, 0.5f, -1f }));
        // Apple's recorder puts a chunk before the samples (an odd-sized one is padded).
        float[] apple = SpeechEngines.WavSamples(Wav(1, new short[] { 16384 }, ("FLLR", new byte[7]), ("LIST", new byte[4])), out _);
        Assert.That(apple, Is.EqualTo(new[] { 0.5f }));
        // Stereo is mixed down.
        Assert.That(SpeechEngines.WavSamples(Wav(2, new short[] { 16384, 0, -16384, -16384 }), out _), Is.EqualTo(new[] { 0.25f, -0.5f }));
        // The meeting's pieces read back the same way.
        float[] piece = SpeechEngines.WavSamples(new MeetingAudioChunk(TimeSpan.Zero, new[] { 0.5f, -0.25f }, 16000).ToWav(), out _);
        Assert.That(piece[0], Is.EqualTo(0.5f).Within(1e-4));
        Assert.That(piece[1], Is.EqualTo(-0.25f).Within(1e-4));
        Assert.Throws<InvalidDataException>(() => SpeechEngines.WavSamples(new byte[] { 1, 2, 3 }, out _));
        Assert.Throws<InvalidDataException>(() => SpeechEngines.WavSamples(Wav(1, new short[0]).Take(36).ToArray(), out _));
    }

    // A WAVE_FORMAT_EXTENSIBLE file (as macOS's afconvert writes) with the given sub-format.
    private static byte[] Extensible(short[] samples, ushort subFormat)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(40);
        writer.Write((ushort)0xFFFE);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write((short)22);
        writer.Write((short)16);
        writer.Write(4);
        writer.Write(subFormat);
        writer.Write(new byte[] { 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xAA, 0, 0x38, 0x9B, 0x71 });
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (short sample in samples) writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }

    [Test]
    public void ReadsAnExtensiblePcmRecording()
    {
        Assert.That(SpeechEngines.WavSamples(Extensible(new short[] { 16384, -16384 }, 1), out int rate), Is.EqualTo(new[] { 0.5f, -0.5f }));
        Assert.That(rate, Is.EqualTo(16000));
        // Floats (sub-format 3) aren't read.
        Assert.Throws<InvalidDataException>(() => SpeechEngines.WavSamples(Extensible(new short[] { 0 }, 3), out _));
    }
}
