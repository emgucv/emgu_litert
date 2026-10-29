// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if TEST_MODELS
using System;
using System.IO;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Opt in with EMGU_AUDIO_FILE=<a WAV/MP3/FLAC file>, for instance a voice message the demo's VoiceRecorder wrote on a
// device: Gemma 4 E2B hears only that file, in a conversation of its own, and the test prints what it made of it.
[TestFixture, NonParallelizable]
public class UnitTestAudioFile
{
    [Test, Category("Hardware")]
    public async Task Gemma4TranscribesTheAudioFile()
    {
        string file = Environment.GetEnvironmentVariable("EMGU_AUDIO_FILE");
        if (string.IsNullOrEmpty(file))
            Assert.Ignore("Set EMGU_AUDIO_FILE to an audio file to run.");
        Assert.That(File.Exists(file), Is.True, file);

        using (Gemma4 model = new Gemma4E2B())
        {
            await model.Init();
            using (Chat chat = model.CreateChat())
            {
                chat.MaxOutputTokens = 128;
                ChatReply reply = await chat.SendAsync("Transcribe this audio exactly. Reply with the words only.",
                    new ChatAttachment[] { ChatAttachment.Audio(File.ReadAllBytes(file)) });
                Console.WriteLine("Transcript: {0}", reply.Text);
                Assert.That(reply.Text, Is.Not.Empty);
            }
        }
    }
}
#endif
