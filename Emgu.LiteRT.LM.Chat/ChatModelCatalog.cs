//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>The platform a chat runs on, for what <see cref="ChatModelCatalog"/> offers there.</summary>
    public enum ChatPlatform
    {
        /// <summary>Android.</summary>
        Android,
        /// <summary>iOS (a phone or tablet).</summary>
        iOS,
        /// <summary>Mac Catalyst (a Mac).</summary>
        MacCatalyst,
        /// <summary>Windows.</summary>
        Windows
    }

    /// <summary>
    /// The Emgu.LiteRT.LM.Models language models as a chat offers them, per platform: what each can do (images, audio,
    /// tools, thinking, summaries), its download size and file, the context window to load it with and the largest its
    /// file allows, and the GPU where it was verified (Gemma 4 E2B with OpenCL on Android; the Gemma 4 models with Metal
    /// on a Mac; all but Gemma 4 12B on Windows). Gemma 4 12B is offered on desktops only. Each call makes new options;
    /// an app takes the ones it wants by <see cref="ChatModelOption.Id"/> ("Qwen3", "Qwen35_0_8B_VL", "Qwen35_4B",
    /// "Gemma4E2B", "Gemma4E4B", "Gemma4_12B" - lasting, since saved chats and settings keep them).
    /// <code>
    /// foreach (ChatModelOption model in ChatModelCatalog.For(ChatDevicePlatform.Current))
    ///     options.Models.Add(model);
    /// </code>
    /// </summary>
    public static class ChatModelCatalog
    {
        // Gemma 4's files allow up to ~32k, but the model re-reads the whole conversation when a saved chat is opened or
        // after compacting, which grows with the window: a full 4k chat took 2 minutes on a Pixel Fold's CPU (8k: almost
        // 6, with 1.5 GB of its RAM left; on its GPU 8 s at 4k), 8k 27 s (E2B) / 61 s (E4B) on an Apple Silicon Mac.
        // Phones use 8k too for models with tools: the agent's instructions and tool definitions alone grew to ~4k tokens.
        private const int GemmaContextWindow = 8192;
        private const int GemmaMaxContextWindow = 32000;

        /// <summary>The models a chat offers on a platform, smallest first.</summary>
        /// <param name="platform">The platform</param>
        public static IList<ChatModelOption> For(ChatPlatform platform)
        {
            bool android = platform == ChatPlatform.Android, mac = platform == ChatPlatform.MacCatalyst, windows = platform == ChatPlatform.Windows;
            var models = new List<ChatModelOption>
            {
                Model("Qwen3", "Qwen3 0.6B", () => new Qwen3(), Qwen3.ModelFile, 350,
                    "~350 MB download. Fast, and can think before answering.", gpu: windows),
                Model("Qwen35_0_8B_VL", "Qwen3.5 0.8B", () => new Qwen35_0_8B_VL(), Qwen35_0_8B_VL.ModelFile, 1300,
                    "~1.3 GB download. Newer and still fast; understands images.", gpu: windows, images: true, canThink: false),
                Model("Qwen35_4B", "Qwen3.5 4B", () => new Qwen35_4B(), Qwen35_4B.ModelFile, 2800,
                    "~2.8 GB download. Much more capable and can think before answering, but slow on the CPU.", gpu: windows, summarize: true),
                Gemma("Gemma4E2B", "Gemma 4 E2B", () => new Gemma4E2B(), Gemma4E2B.ModelFile, 2600,
                    "~2.6 GB download. Understands images and speech, and can think before answering.", gpu: android || mac || windows),
                // E4B's GPU wasn't verified on Android.
                Gemma("Gemma4E4B", "Gemma 4 E4B", () => new Gemma4E4B(), Gemma4E4B.ModelFile, 3700,
                    "~3.7 GB download. More capable than E2B, but slower and needs more memory.", gpu: mac || windows),
            };
            // A large desktop model; on Windows its GPU image inference failed compatibility testing.
            if (mac || windows)
                models.Add(Gemma("Gemma4_12B", "Gemma 4 12B", () => new Gemma4_12B(), Gemma4_12B.ModelFile, 6900,
                    windows ? "~6.9 GB download. Large desktop model for code and harder questions. CPU only on Windows: GPU image inference failed compatibility testing. Needs substantial system memory."
                        : "~6.9 GB download. The most capable - best for code and harder questions. Needs a Mac with 16 GB of memory.",
                    gpu: mac));
            return models;
        }

        private static ChatModelOption Model(string id, string name, Func<LanguageModel> create, DownloadableFile file,
            long megabytes, string detail, bool gpu, bool images = false, bool canThink = true, bool summarize = false)
        {
            var option = new ChatModelOption(id, name, create)
            {
                Detail = detail,
                DownloadSize = megabytes << 20,
                ModelFile = file,
                AcceptsImages = images,
                CanThink = canThink,
                CanSummarize = summarize
            };
            if (gpu)
                option.Backends.Add("gpu");
            return option;
        }

        private static ChatModelOption Gemma(string id, string name, Func<LanguageModel> create, DownloadableFile file,
            long megabytes, string detail, bool gpu)
        {
            ChatModelOption option = Model(id, name, create, file, megabytes, detail, gpu, images: true, summarize: true);
            option.AcceptsAudio = true;
            option.CanUseTools = true;
            option.ContextWindow = GemmaContextWindow;
            option.MaxContextWindow = GemmaMaxContextWindow;
            return option;
        }
    }
}
