//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Chat.Sqlite;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Maui.Features;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;

namespace MauiChat;

// The whole app is Emgu.LiteRT.LM.Maui's ChatPage: two models to choose from, chats, memories and goals saved in SQLite
// (Emgu.LiteRT.LM.Chat.Sqlite), the built-in features (the date and time, a calculator, units, memories, the web, the
// user's location, the clipboard, goals) and one skill of this app's own (DiceSkill, below). App.xaml restyles it.
public sealed class MainPage : ChatPage
{
    public MainPage() : base(CreateOptions())
    {
    }

    private static ChatPageOptions CreateOptions()
    {
        // The saved chats, in a folder of the sample's own: on Mac Catalyst the app data folder can be ~/Library itself.
        var store = new ChatHistoryStore(Path.Combine(FileSystem.AppDataDirectory, "MauiChat", "chats"));
        var options = new ChatPageOptions { Title = "LiteRT Chat", History = store };
        // Qwen3 0.6B: a 345 MB download, quick on a phone's CPU, no tools. Gemma 4 E2B (2.6 GB) understands images and
        // speech, and calls the features' tools. The page downloads a model (after a tap) and loads it.
        options.Models.Add(new ChatModelOption("Qwen3", "Qwen3 0.6B", () => new Qwen3())
        {
            Detail = "~345 MB download. Fast, and can think before answering.",
            DownloadSize = 345L << 20,
            ModelFile = Qwen3.ModelFile
        });
        // Gemma 4 E2B runs on the GPU too (OpenCL on Android, Metal on a Mac), and its file allows a context window up
        // to 32k: the settings offer the choice, with 8k as Automatic.
        var gemma = new ChatModelOption("Gemma4E2B", "Gemma 4 E2B", () => new Gemma4E2B())
        {
            Detail = "~2.6 GB download. Understands images and speech, uses tools, and can think before answering.",
            DownloadSize = 2600L << 20,
            ModelFile = Gemma4E2B.ModelFile,
            CanUseTools = true,
            AcceptsImages = true,
            AcceptsAudio = true,
            CanSummarize = true,
            ContextWindow = 8192,
            MaxContextWindow = 32000
        };
        if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            gemma.Backends.Add("gpu");
        options.Models.Add(gemma);
        foreach (ChatFeature feature in ChatPage.DefaultFeatures(new SqliteAgentMemoryStore(store), new SqliteAgentGoalStore(store)))
            options.Features.Add(feature);
        // Documents the model writes (create_document): a card with Open and Share, kept with the reply in the saved chat.
        options.Features.Add(new WrittenDocumentsFeature());
        options.Features.Add(new DiceFeature());
        return options;
    }
}

// A skill of the app's own: one tool. A ChatFeature can also add a settings section, a chip above the message box, and
// cards on replies; a pure ChatSkill (Emgu.LiteRT.LM.Agents) works with any IChatClient, outside MAUI too.
public sealed class DiceFeature : ChatFeature
{
    private static readonly Random Random = new Random();

    public override string Name => "dice";

    public override string Title => "Dice";

    public override IEnumerable<AITool> Tools(ChatMessageContext context) => new AITool[]
    {
        AIFunctionFactory.Create((int sides) => Random.Next(1, Math.Max(2, sides) + 1), "roll_die",
            "Rolls a die with the given number of sides (6 for an ordinary die) and returns the number it lands on.")
    };

    public override Task<string> InstructionsAsync(ChatMessageContext context) =>
        Task.FromResult("When the user asks you to roll a die, flip a coin or pick a random number, use roll_die rather than making a number up.");

    public override string ToolSummary => "dice";

    // A switch in the settings; the page keeps it (as "feature_dice") between runs.
    public override View BuildSettingsSection() =>
        ChatPage.SwitchRow("Dice", "Lets the assistant roll dice with the roll_die tool.", Enabled, on => Enabled = on);
}
