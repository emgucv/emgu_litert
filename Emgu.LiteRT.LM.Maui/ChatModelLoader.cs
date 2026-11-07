//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Chat;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui;

/// <summary>What <see cref="ChatModelLoader.LoadAsync"/> loaded: the model and its backend, or why not.</summary>
/// <param name="Model">The loaded model, or null</param>
/// <param name="Backend">Its backend ("gpu", "cpu"), or null</param>
/// <param name="Error">Why it didn't load, or null</param>
public sealed record ChatModelLoad(LanguageModel Model, string Backend, string Error);

/// <summary>
/// Loads one of the chat's models outside a chat page (a scheduled task's run, the watch-and-listen page), as the chat page
/// would: on the GPU when the chat's GPU setting is on and the model has one, on the CPU if the GPU fails.
/// </summary>
public static class ChatModelLoader
{
    /// <summary>The backend the chat would use for a model: its GPU when the "use_gpu_&lt;platform&gt;" setting is on (by default, except on Windows).</summary>
    /// <param name="settings">The chat's settings</param>
    /// <param name="option">The model</param>
    public static string PreferredBackend(IChatPageSettings settings, ChatModelOption option)
    {
        bool gpu = settings.Get("use_gpu_" + DeviceInfo.Current.Platform, DeviceInfo.Current.Platform != DevicePlatform.WinUI);
        return option.Backends.Count > 1 && gpu ? "gpu" : option.Backends.FirstOrDefault() ?? "cpu";
    }

    /// <summary>The context window the chat would give a model (its "context_tokens" setting, <see cref="ChatContextWindow.Choose"/>).</summary>
    /// <param name="settings">The chat's settings</param>
    /// <param name="option">The model</param>
    public static int ChatContextWindowFor(IChatPageSettings settings, ChatModelOption option)
    {
        bool phone = DeviceInfo.Current.Platform == DevicePlatform.Android || DeviceInfo.Current.Platform == DevicePlatform.iOS;
        int setting = settings.Get("context_tokens", 0);
        if (Array.IndexOf(ChatContextWindow.Choices, setting) < 0)
            setting = 0;
        return option.MaxContextWindow > 0
            ? ChatContextWindow.Choose(setting, option.ContextWindow, option.MaxContextWindow, phone, option.CanUseTools)
            : option.ContextWindow;
    }

    /// <summary>
    /// Load a model on a backend, and on the CPU if the GPU fails.
    /// </summary>
    /// <param name="option">The model</param>
    /// <param name="backend">The backend to try first</param>
    /// <param name="context">The context window in tokens; 0 for the model's own</param>
    /// <param name="download">True to download the model if needed; false to give up when it isn't downloaded</param>
    /// <param name="prepare">Sets up a created model for a backend before it loads (e.g. Gemma 4's vision backend), or null</param>
    /// <param name="configureEngine">Changes the engine's settings (after the CPU threads and the context), or null</param>
    /// <param name="loading">Told each attempt's load state, to show its download and progress, or null</param>
    /// <param name="cancellationToken">Stops loading</param>
    public static async Task<ChatModelLoad> LoadAsync(ChatModelOption option, string backend, int context, bool download,
        Action<LanguageModel, string> prepare, Action<EngineSettings> configureEngine, Action<ModelLoadState> loading,
        CancellationToken cancellationToken)
    {
        string error = null;
        foreach (string tryBackend in backend == "gpu" ? new[] { "gpu", "cpu" } : new[] { backend })
        {
            cancellationToken.ThrowIfCancellationRequested();
            LanguageModel model = option.Create();
            if (context > 0)
                model.MaxNumTokens = context;
            prepare?.Invoke(model, tryBackend);
            var state = new ModelLoadState(model) { Backend = tryBackend };
            if (!download && !state.IsDownloaded)
            {
                model.Dispose();
                return new ChatModelLoad(null, null, "the model isn't downloaded (" + option.DisplayName + ")");
            }
            state.ConfigureSettings = settings =>
            {
                settings.NumThreads = CpuThreadSettings.Count;
                if (context > 0)
                    settings.MaxNumTokens = context;
                configureEngine?.Invoke(settings);
            };
            loading?.Invoke(state);
            using (cancellationToken.Register(state.Cancel))
            {
                if (await state.LoadAsync().ConfigureAwait(false) && model.Initialized)
                    return new ChatModelLoad(model, tryBackend, null);
            }
            error = state.Error;
            await Task.Run(() => model.Dispose()).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return new ChatModelLoad(null, null, "the model couldn't load: " + error);
    }
}
