// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The long-chat measurements in Emgu.LiteRT.Example/Maui.Lite/Performance.md, for the machine running them: a history
// that fills the context window (a fact at the start - "the secret code is 7341" - then filler) is loaded, and the
// model is asked for the fact, for the fact plus one, and for a long answer. One configuration per process, as the
// numbers there were taken (a fresh process per window size):
//   EMGU_LONGCHAT=cpu:8192, gpu32:8192 (GPU with 32-bit activations, the Gemma4 default) or gpu16:8192 (16-bit)
// On Windows the GPU needs the source-built WebGPU bundle next to libLiteRt (see UnitTest1.TestLiteRtMobilenetGpu).
[TestFixture, NonParallelizable]
public class UnitTestLongChatBenchmark
{
#if !TEST_MODELS
    [Ignore("Opt in with -p:TestModels=true and EMGU_LONGCHAT=<cpu|gpu32|gpu16>:<window>; downloads Gemma 4 E2B.")]
#endif
    [Test, Category("Benchmark")]
    public async Task Gemma4E2BLongChat()
    {
        string[] config = (Environment.GetEnvironmentVariable("EMGU_LONGCHAT") ?? "cpu:4096").Split(':');
        string mode = config[0];
        int window = int.Parse(config[1], CultureInfo.InvariantCulture);
        Assert.That(mode, Is.AnyOf("cpu", "gpu32", "gpu16"));
        LiteRtLmInvoke.SetMinLogLevel(LogSeverity.Warning);

        using var model = new Gemma4E2B { VisionBackend = null, AudioBackend = null, MaxNumTokens = window };
        var load = Stopwatch.StartNew();
        await model.Init(backend: mode == "cpu" ? "cpu" : "gpu", configureSettings: s =>
        {
            s.NumThreads = 4;
            if (mode == "gpu16") s.ActivationDataType = ActivationDataType.Float16;
            if (mode == "gpu32") s.ActivationDataType = ActivationDataType.Float32;
        });
        double loadSeconds = load.Elapsed.TotalSeconds;

        // A fact, then filler up to the window less ~1,200 tokens for the question and the answer.
        var history = new List<ChatMessage> {
            new(ChatMessage.UserRole, "Please remember this: the secret code is 7341. Reply with just OK."),
            new(ChatMessage.AssistantRole, "OK") };
        int tokens = model.CountTokens(history[0].Text) + 16;
        for (int i = 1; tokens < window - 1200; i++)
        {
            string note = $"Note {i}: the river {i} winds past {i + 3} quiet villages, where the market sells fresh bread, " +
                $"soft cheese and red apples every morning, and children count {i * 7} boats on the water.";
            history.Add(new(ChatMessage.UserRole, note));
            history.Add(new(ChatMessage.AssistantRole, "Noted."));
            tokens += model.CountTokens(note) + 16 + 8;
        }

        using Chat chat = model.CreateChat();
        chat.EnableThinking = false;
        chat.MaxOutputTokens = 32;
        chat.LoadHistory(history);
        var timer = Stopwatch.StartNew();
        ChatReply recall = await chat.SendAsync("What is the secret code?");
        double readSeconds = timer.Elapsed.TotalSeconds;
        long peak = Process.GetCurrentProcess().PeakWorkingSet64;
        timer.Restart();
        ChatReply plusOne = await chat.SendAsync("What is the secret code plus one? Answer with just the number.");
        double followUpSeconds = timer.Elapsed.TotalSeconds;

        chat.MaxOutputTokens = 160;
        timer.Restart();
        double firstPiece = 0;
        ChatReply story = await chat.SendAsync("Write a story of about 120 words about a fox.", null,
            _ => { if (firstPiece == 0) firstPiece = timer.Elapsed.TotalSeconds; });
        double total = timer.Elapsed.TotalSeconds;
        int storyTokens = model.CountTokens(story.Text);
        double tokensPerSecond = total > firstPiece && firstPiece > 0 ? storyTokens / (total - firstPiece) : 0;

        var result = new
        {
            Mode = mode, Window = window, HistoryTokens = tokens, HistoryMessages = history.Count,
            LoadSeconds = Math.Round(loadSeconds, 1), ReadAndRecallSeconds = Math.Round(readSeconds, 1),
            Recall = recall.Text, RecallCorrect = recall.Text.Contains("7341"),
            FollowUpSeconds = Math.Round(followUpSeconds, 1), PlusOne = plusOne.Text, PlusOneCorrect = plusOne.Text.Contains("7342"),
            StoryTokens = storyTokens, TokensPerSecond = Math.Round(tokensPerSecond, 1),
            PeakWorkingSetGB = Math.Round(peak / 1e9, 2), RecordedUtc = DateTime.UtcNow
        };
        string json = JsonSerializer.Serialize(result);
        TestContext.Progress.WriteLine("LONGCHAT " + json);
        File.AppendAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "longchat-results.jsonl"), json + Environment.NewLine);
        Assert.That(history.Count, Is.GreaterThan(2));
    }
}
