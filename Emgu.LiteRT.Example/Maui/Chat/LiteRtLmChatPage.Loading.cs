//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Maui;
using Emgu.LiteRT.LM.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using Emgu.LiteRT.LM.Chat.Sqlite;

// Loading the model: downloading, the engine on the CPU or GPU, and the card that shows it.
namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    // ---------- Loading the model ----------

    private static bool IsDownloaded(ModelOption option)
    {
        // A file that exists but isn't verified yet (e.g. downloaded by an older version) is checked when loading.
        if (option.File == null)
            return true; // The server: nothing to download.
        try
        {
            string path = option.File.LocalFile;
            return option.File.IsLocalFileVerified || (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 0);
        }
        catch (System.IO.IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Get the selected model ready: nothing to do if it's loaded or loading; load it if it's downloaded (or the
    // download is confirmed); otherwise offer the download. Returns the load task, or null when waiting for the
    // user to confirm a download.
    private Task<bool> PrepareModelAsync(bool downloadConfirmed)
    {
        ModelOption option = SelectedOption;
        if (ModelReady)
        {
            HideLoadCard();
            return Task.FromResult(true);
        }
        if (_loadTask != null && _loadingChoice == option.Choice && _loadingOnGpu == WantsGpu(option)
            && _loadingContextTokens == ContextTokensFor(option))
            return _loadTask;
        if (!downloadConfirmed && !IsDownloaded(option))
        {
            CancelLoad();
            ShowDownloadPrompt(option);
            return null;
        }
        _loadCancel?.Cancel();
        _loadCancel = new CancellationTokenSource();
        _loadingChoice = option.Choice;
        _loadingOnGpu = WantsGpu(option);
        _loadingContextTokens = ContextTokensFor(option);
        _loadTask = LoadModelAsync(option, _loadTask, _loadCancel.Token);
        return _loadTask;
    }

    private static void ConfigureCpuThreads(Emgu.LiteRT.LM.EngineSettings settings)
    {
        int count = CpuThreadSettings.Count;
        settings.EnableBenchmark();
        settings.NumThreads = count;
        settings.AudioNumThreads = count;
    }

    private async Task<bool> LoadModelAsync(ModelOption option, Task<bool> previousLoad, CancellationToken token)
    {
        UiStallWatchdog.Mark("Loading model " + option.Name);
        ShowLoadCard(option, "Starting...", null);
        // Let a cancelled load finish first (an engine being created can't be interrupted), so two models are never
        // in memory at once, then release the model that was loaded before.
        if (previousLoad != null)
        {
            try { await previousLoad; } catch (Exception) { }
        }
        // (PrepareModelAsync only gets here when the loaded model isn't the one wanted: another model, or the same one
        // on another backend.)
        if (_model != null || _serverConnected != null || _chatClient != null)
            await CleanupModelAsync();
        if (token.IsCancellationRequested)
            return false;
        if (option.Choice == ModelChoice.Server)
            return await ConnectServerAsync(option, token);

        bool onGpu = WantsGpu(option);
        int contextTokens = ContextTokensFor(option);
        ModelLoadStage lastStage = ModelLoadStage.CheckingFile;
        LanguageModel NewModel()
        {
            LanguageModel created = CreateModel(option.Choice);
            if (contextTokens > 0)
                created.MaxNumTokens = contextTokens;
            created.LoadStageChanged += (s, stage) =>
            {
                lastStage = stage;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!token.IsCancellationRequested)
                        ShowLoadStage(option, stage);
                });
            };
            created.OnDownloadProgressChanged += (total, received, percent) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!token.IsCancellationRequested)
                    ShowDownloadProgress(total, received);
            });
            return created;
        }
        LanguageModel model = NewModel();
        try
        {
            if (!option.File.IsLocalFileVerified)
                ShowLoadStage(option, ModelLoadStage.Downloading);
            await BackgroundDownloads.DownloadFileAsync(option.File, option.Name,
                (total, received, percent) => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!token.IsCancellationRequested) ShowDownloadProgress(total, received);
                }), token);
            if (onGpu)
            {
                try
                {
                    // Gemma4 uses 32-bit activations on the GPU: with LiteRT-LM's default 16-bit ones, Gemma 4 E2B
                    // answered wrongly after a long conversation on a Pixel Fold and an M1 Mac (Performance.md).
                    await model.Init(backend: "gpu", configureSettings: ConfigureCpuThreads, cancellationToken: token);
                }
                catch (Exception ex) when (lastStage == ModelLoadStage.LoadingEngine && !token.IsCancellationRequested)
                {
                    UiStallWatchdog.Mark("GPU unavailable: " + ex.Message);
                    bool useCpu = await MainThread.InvokeOnMainThreadAsync(() => DisplayAlertAsync(
                        "GPU model could not load", ex.Message + "\n\nRun this model on CPU instead?", "Run on CPU", "Cancel"));
                    if (!useCpu) throw;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _selectedGpu = false;
                        AppSettings.Chat.UseGpu = false;
                        _modelChipLabel.Text = option.Name + " · CPU";
                        RefreshModelList();
                    });
                    onGpu = false;
                    _loadingOnGpu = false;
                    await Task.Run(() => model.Dispose());
                    model = NewModel();
                    await model.Init(configureSettings: ConfigureCpuThreads, cancellationToken: token);
                }
            }
            else
            {
                await model.Init(configureSettings: ConfigureCpuThreads, cancellationToken: token);
            }
        }
        catch (Exception ex)
        {
            await Task.Run(() => model.Dispose());
            if (token.IsCancellationRequested || ex is OperationCanceledException)
            {
                if (_downloadingChoice == option.Choice) _downloadingChoice = null;
                ClearLoading(option.Choice);
                UpdateModelDownloadRows();
                if (SelectedChoice == option.Choice && _loadingChoice == null && !IsDownloaded(option))
                    ShowDownloadPrompt(option);
                return false;
            }
            UiStallWatchdog.Mark("Loading failed: " + ex.Message);
            ShowLoadError(option, ex.Message);
            ClearLoading(option.Choice);
            return false;
        }
        if (token.IsCancellationRequested)
        {
            await Task.Run(() => model.Dispose());
            return false;
        }

        _model = model;
        _loaded = option.Choice;
        _loadedOnGpu = onGpu;
        _loadedContextTokens = contextTokens;
        // Reloaded on another backend in the middle of a chat: a new client for the new engine, which is given the
        // conversation with the next message.
        ResetAgent();
        _rereadNext = ChatMessages.Any();
        if (_gpuUnavailable && WantsGpuSetting(option))
            SetStatus("The GPU isn't available on this device - using the CPU.");
        ClearLoading(option.Choice);
        HideLoadCard();
        UpdateMemoryLabel();
        UiStallWatchdog.Mark("Model ready: " + option.Name);
        return true;
    }

    // The selected model is loaded, on the backend it should run on.
    private bool ModelReady => (SelectedChoice == ModelChoice.Server ? _serverConnected == ServerConnectionKey : _model != null)
        && _loaded == SelectedChoice && _loadedOnGpu == WantsGpu(SelectedOption)
        && _loadedContextTokens == ContextTokensFor(SelectedOption);

    // Whether the model should run on the GPU: the setting, unless the GPU failed to load this session.
    private bool WantsGpu(ModelOption option) => WantsGpuSetting(option) && !_gpuUnavailable;

    private bool WantsGpuSetting(ModelOption option)
    {
        return HasGpu(option) && _selectedGpu;
    }

    // Whether the model can run on this platform's GPU.
    private static bool HasGpu(ModelOption option)
    {
#if ANDROID
        return option.GpuOnAndroid;
#elif MACCATALYST
        return option.GpuOnMac;
#elif WINDOWS_WEBGPU
        return option.GpuOnWindows;
#else
        return false;
#endif
    }

    private static LanguageModel CreateModel(ModelChoice choice) => ChatModelCatalog.Create(choice);

    private void ClearLoading(ModelChoice choice)
    {
        if (_loadingChoice == choice)
        {
            _loadingChoice = null;
            _loadTask = null;
        }
        if (_downloadingChoice == choice) _downloadingChoice = null;
        UpdateModelDownloadRows();
        _chipSpinner.IsVisible = _chipSpinner.IsRunning = _loadTask != null;
    }

    // Cancel the load in progress; downloaded bytes remain available for Continue.
    private void CancelLoad()
    {
        if (_loadTask == null)
            return;
        UiStallWatchdog.Mark("Cancel loading");
        _loadCancel?.Cancel();
        _downloadingChoice = null;
        UpdateModelDownloadRows();
        _loadingChoice = null;
        _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
        ModelOption option = SelectedOption;
        if (ModelReady)
            HideLoadCard();
        else if (!IsDownloaded(option))
            ShowDownloadPrompt(option);
        else
            ShowLoadError(option, "Loading was cancelled.");
        // _loadTask is kept until it finishes, so the next load waits for it.
    }

    private void ShowDownloadPrompt(ModelOption option)
    {
        bool wifi = true;
        try
        {
            var profiles = Connectivity.Current.ConnectionProfiles;
            wifi = profiles.Contains(ConnectionProfile.WiFi) || profiles.Contains(ConnectionProfile.Ethernet);
        }
        catch (Exception)
        {
        }
        bool paused = option.File.PartialDownloadBytes > 0;
        string message = paused ? PausedDownload(option) : string.Format("{0} needs a one-time {1} download.", option.Name, option.Size);
        message += " " + BackgroundDownloads.Hint;
        ShowLoadCard(option, message + (wifi ? "" : " You're not on Wi-Fi - mobile data may be used."), null);
        _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
        _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
        SetLoadButtons(paused ? "Continue" : "Download " + option.Size, () => _ = PrepareModelAsync(true), "Not now");
    }

    private void ShowLoadError(ModelOption option, string message)
    {
        if (_downloadingChoice == option.Choice) _downloadingChoice = null;
        UpdateModelDownloadRows();
        bool paused = option.File != null && option.File.PartialDownloadBytes > 0;
        ShowLoadCard(option, (paused ? PausedDownload(option) + "\n" : "") + "Could not load the model: " + message, null);
        _loadSpinner.IsVisible = _loadSpinner.IsRunning = false;
        SetLoadButtons(paused ? "Continue" : "Retry", () => _ = PrepareModelAsync(true), null);
    }

    private void ShowLoadStage(ModelOption option, ModelLoadStage stage)
    {
        switch (stage)
        {
            case ModelLoadStage.CheckingFile:
                ShowLoadCard(option, "Checking the model file...", null);
                break;
            case ModelLoadStage.Downloading:
                _downloadingChoice = option.Choice;
                _downloadReceived = option.File.PartialDownloadBytes;
                _downloadTotal = option.File.PartialDownloadTotalBytes;
                UpdateModelDownloadRows();
                ShowLoadCard(option, "Downloading...", 0);
                break;
            case ModelLoadStage.VerifyingFile:
                _downloadingChoice = null;
                UpdateModelDownloadRows();
                ShowLoadCard(option, "Checking the download...", null);
                break;
            case ModelLoadStage.LoadingEngine:
#if WINDOWS
                ShowLoadCard(option, _loadingOnGpu ? "Loading onto the GPU (this can take several minutes)..." : "Loading into memory...", null);
#else
                ShowLoadCard(option, _loadingOnGpu ? "Loading onto the GPU (about 30 s the first time)..." : "Loading into memory...", null);
#endif
                break;
        }
    }

    private void ShowDownloadProgress(long? total, long received)
    {
        _downloadReceived = received;
        _downloadTotal = total;
        UpdateModelDownloadRows();
        const double GB = 1024.0 * 1024 * 1024;
        if (total.HasValue && total.Value > 0)
        {
            _loadStage.Text = string.Format("Downloading... {0:0.00} / {1:0.00} GB ({2:0}%)", received / GB, total.Value / GB, 100.0 * received / total.Value);
            _loadProgress.IsVisible = true;
            _loadProgress.Progress = (double)received / total.Value;
        }
        else
        {
            _loadStage.Text = string.Format("Downloading... {0:0.00} GB", received / GB);
        }
    }

    // The card while loading: a spinner, the stage and (for a download) a progress bar, with Cancel.
    private void ShowLoadCard(ModelOption option, string stage, double? progress)
    {
        _loadCard.IsVisible = true;
        _loadTitle.Text = option.Name;
        _loadStage.Text = stage;
        _loadProgress.IsVisible = progress.HasValue;
        if (progress.HasValue)
            _loadProgress.Progress = progress.Value;
        _loadSpinner.IsVisible = _loadSpinner.IsRunning = true;
        _chipSpinner.IsVisible = _chipSpinner.IsRunning = true;
        SetLoadButtons(null, null, "Cancel");
    }

    private void SetLoadButtons(string primaryText, Action primaryAction, string cancelText)
    {
        _loadPrimaryAction = primaryAction;
        _loadPrimaryButton.IsVisible = primaryText != null;
        if (primaryText != null)
            _loadPrimaryButton.Text = primaryText;
        _loadCancelButton.IsVisible = cancelText != null;
        if (cancelText != null)
            _loadCancelButton.Text = cancelText;
    }

    private void HideLoadCard()
    {
        _loadCard.IsVisible = false;
        _chipSpinner.IsVisible = _chipSpinner.IsRunning = false;
    }
}
