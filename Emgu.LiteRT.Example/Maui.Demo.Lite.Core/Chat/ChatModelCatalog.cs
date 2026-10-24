// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;

namespace Maui.Demo.Lite;

// The platforms the demo's chat runs on (the model list differs a little between them).
internal enum ChatPlatform
{
    Android,
    iOS,
    MacCatalyst,
    Windows
}

internal enum ModelChoice
{
    Qwen3,
    Qwen35_0_8B_VL,
    Qwen35_4B,
    Gemma4E2B,
    Gemma4E4B,
    Gemma4_12B,
    // A model on the user's own server (LiteRtLmChatPage.Server.cs).
    Server
}

internal sealed class ModelOption
{
    public string Name;
    public string Detail;
    public ModelChoice Choice;
    // Whether the model accepts images / audio (LanguageModel.SupportsImages / SupportsAudio) and can
    // think, known before the model is loaded so the controls can be shown right away.
    public bool AcceptsImages;
    public bool AcceptsAudio;
    public bool CanThink = true;
    // Whether the model can call tools (LanguageModel.SupportsToolCalling), for web search.
    public bool CanUseTools;
    // Whether the model writes useful summaries: when a chat outgrows its context window, earlier messages
    // are summarized; otherwise (the small models) they are dropped.
    public bool CanSummarize;
    // The context window Automatic picks, or 0 for LanguageModel's default (4096). Gemma 4's files allow up to
    // ~32k, but the model re-reads the whole conversation when a saved chat is opened or after compacting, which
    // grows with the window: a full 4k chat took 2 minutes on a Pixel Fold's CPU (8k: almost 6, with 1.5 GB of
    // its RAM left; on its GPU 8 s at 4k), 8k 27 s (E2B) / 61 s (E4B) on an Apple Silicon Mac. Phones use it too
    // for models with tools: the agent's instructions and tool definitions alone grew to ~4k tokens (goals
    // pushed them past a 4k window, so every message failed).
    public int DesktopContextTokens;
    // The largest context window the model's file allows, which Settings > Context window can choose up to; 0 for
    // a model with a fixed window (the Qwen files: 4096).
    public int MaxContextTokens;
    // Whether the model runs on the GPU (when "Use the GPU" is on): on Android with LiteRT's OpenCL accelerator
    // (if the device has OpenCL; only Gemma 4 E2B has been tested there), on Mac Catalyst with its Metal one.
    // Gemma4 uses 32-bit activations on the GPU - see Performance.md.
    public bool GpuOnAndroid;
    public bool GpuOnMac;
    public bool GpuOnWindows;
    // The model file (to tell whether it is downloaded) and its download size, for the download prompt.
    public DownloadableFile File;
    public string Size;
}

// The language models the chat page offers, per platform, and making one.
internal static class ChatModelCatalog
{
    public static ModelOption[] ModelsFor(ChatPlatform platform)
    {
        var models = new List<ModelOption>
        {
            new ModelOption { Name = "Qwen3 0.6B", File = Qwen3.ModelFile, Size = "350 MB", Detail = "~350 MB download. Fast, and can think before answering.", Choice = ModelChoice.Qwen3, GpuOnWindows = true },
            new ModelOption { Name = "Qwen3.5 0.8B", File = Qwen35_0_8B_VL.ModelFile, Size = "1.3 GB", Detail = "~1.3 GB download. Newer and still fast; understands images.", Choice = ModelChoice.Qwen35_0_8B_VL, AcceptsImages = true, CanThink = false, GpuOnWindows = true },
            new ModelOption { Name = "Qwen3.5 4B", File = Qwen35_4B.ModelFile, Size = "2.8 GB", Detail = "~2.8 GB download. Much more capable and can think before answering, but slow on the CPU.", Choice = ModelChoice.Qwen35_4B, CanSummarize = true, GpuOnWindows = true },
            new ModelOption { Name = "Gemma 4 E2B", File = Gemma4E2B.ModelFile, Size = "2.6 GB", Detail = "~2.6 GB download. Understands images and speech, and can think before answering.", Choice = ModelChoice.Gemma4E2B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true, CanSummarize = true, DesktopContextTokens = 8192, MaxContextTokens = 32000, GpuOnAndroid = true, GpuOnMac = true, GpuOnWindows = true },
            new ModelOption { Name = "Gemma 4 E4B", File = Gemma4E4B.ModelFile, Size = "3.7 GB", Detail = "~3.7 GB download. More capable than E2B, but slower and needs more memory.", Choice = ModelChoice.Gemma4E4B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true, CanSummarize = true, DesktopContextTokens = 8192, MaxContextTokens = 32000, GpuOnMac = true, GpuOnWindows = true },
        };
        if (platform == ChatPlatform.MacCatalyst || platform == ChatPlatform.Windows)
        {
            // Large desktop model; GPU entries are enabled only for verified platform runtimes.
            models.Add(new ModelOption { Name = "Gemma 4 12B", File = Gemma4_12B.ModelFile, Size = "6.9 GB",
                Detail = platform == ChatPlatform.Windows ? "~6.9 GB download. Large desktop model for code and harder questions. CPU only on Windows: GPU image inference failed compatibility testing. Needs substantial system memory."
                    : "~6.9 GB download. The most capable - best for code and harder questions. Needs a Mac with 16 GB of memory.",
                Choice = ModelChoice.Gemma4_12B, AcceptsImages = true, AcceptsAudio = true, CanUseTools = true, CanSummarize = true, DesktopContextTokens = 8192, MaxContextTokens = 32000, GpuOnMac = true });
        }
        return models.ToArray();
    }

    // A new, unloaded instance of a model.
    public static LanguageModel Create(ModelChoice choice)
    {
        switch (choice)
        {
            case ModelChoice.Qwen3: return new Qwen3();
            case ModelChoice.Qwen35_0_8B_VL: return new Qwen35_0_8B_VL();
            case ModelChoice.Qwen35_4B: return new Qwen35_4B();
            case ModelChoice.Gemma4E4B: return new Gemma4E4B();
            case ModelChoice.Gemma4_12B: return new Gemma4_12B();
            default: return new Gemma4E2B();
        }
    }
}
