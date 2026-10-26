// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System.Collections.Generic;
using Emgu.LiteRT.LM.Chat;

namespace Maui.Demo.Lite;

// The demo's model catalog as the library's ChatModelOptions, for Emgu.LiteRT.LM.Maui's ChatPage: the ModelChoice's name
// is the id (the "model" setting and a saved chat's model carry over), the GPU per platform becomes a backend, and the
// user's own server is a remote option.
internal static class ChatModelOptions
{
    // The id of the remote option for the user's own server.
    public const string ServerId = "Server";

    public static IList<ChatModelOption> FromCatalog(IEnumerable<ModelOption> models, ChatPlatform platform)
    {
        var options = new List<ChatModelOption>();
        foreach (ModelOption model in models)
        {
            ModelChoice choice = model.Choice;
            var option = new ChatModelOption(choice.ToString(), model.Name, () => ChatModelCatalog.Create(choice))
            {
                Detail = model.Detail,
                DownloadSize = model.DownloadBytes,
                ModelFile = model.File,
                CanUseTools = model.CanUseTools,
                AcceptsImages = model.AcceptsImages,
                AcceptsAudio = model.AcceptsAudio,
                CanThink = model.CanThink,
                CanSummarize = model.CanSummarize,
                ContextWindow = model.DesktopContextTokens,
                MaxContextWindow = model.MaxContextTokens
            };
            bool gpu = platform == ChatPlatform.Android ? model.GpuOnAndroid
                : platform == ChatPlatform.MacCatalyst ? model.GpuOnMac
                : platform == ChatPlatform.Windows ? model.GpuOnWindows : false;
            if (gpu)
                option.Backends.Add("gpu");
            options.Add(option);
        }
        options.Add(new ChatModelOption(ServerId, "Your own server")
        {
            Detail = "An OpenAI-compatible server on your network (LM Studio, Ollama...), set up in Settings > Your own server.",
            CanUseTools = true,
            CanSummarize = true
        });
        return options;
    }
}
