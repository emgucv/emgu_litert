using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.LM.Maui;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    private bool _reloadingCpuThreads;
    private Task _cpuReloadTask;

    private Task ReloadCpuModelsAsync()
    {
        if (_reloadingCpuThreads)
            return _cpuReloadTask;
        if (_recorder.IsRecording)
            return Task.FromException(new InvalidOperationException("Stop recording before reloading models."));
        _reloadingCpuThreads = true;
        _cpuReloadTask = ReloadCpuModelsCoreAsync();
        return _cpuReloadTask;
    }

    private async Task ReloadCpuModelsCoreAsync()
    {
        bool reloadChat = _model != null || _loadTask != null;
        bool reloadEmbedder = Embedder.IsLoaded;
        bool reloadImage = Images.ModelLoaded;
        try
        {
            // Prevent new work while allowing the current send (including its tools) to finish.
            _settingsView.IsEnabled = false;
            PhotoSearch.CancelIndexing();
            await PastChats.StopIndexingAsync();
            while (_busy)
                await Task.Delay(100);
            SetBusy(true);
            await PhotoSearch.WaitForIndexingAsync();
            if (_document?.Ready != null)
                await _document.Ready;
            // A send can load a tool model while we wait, so check again after it finishes.
            reloadChat |= _model != null || _loadTask != null;
            reloadEmbedder |= Embedder.IsLoaded;
            reloadImage |= Images.ModelLoaded;
            _loadCancel?.Cancel();
            if (_loadTask != null)
                await _loadTask;
            await CleanupModelAsync();
            await Embedder.ReleaseAsync();
            await Images.ResetModelAsync();
            CpuThreadSettings.RequestReload();
            if (!_pageVisible)
                return;
            SetStatus("Reloading models with the new CPU thread count...");
            if (reloadChat)
            {
                Task<bool> load = PrepareModelAsync(false);
                if (load == null || !await load)
                    throw new InvalidOperationException("The chat model could not be reloaded.");
            }
            if (reloadEmbedder)
                await Embedder.EnsureAsync(null, CancellationToken.None);
            if (reloadImage)
                await Images.EnsureModelAsync();
            SetStatus($"Models ready with {CpuThreadSettings.Count} CPU threads.");
        }
        finally
        {
            _reloadingCpuThreads = false;
            _settingsView.IsEnabled = true;
            SetBusy(false);
            if (_pageVisible)
                PastChats.StartIndexing();
            if (_pageVisible)
                PhotoSearch.StartIndexing();
        }
    }

    private async Task CleanupAfterCpuReloadAsync()
    {
        try { await (_cpuReloadTask ?? Task.CompletedTask); } catch (Exception) { }
        await PastChats.StopIndexingAsync();
        await CleanupModelAsync();
        await Embedder.ReleaseAsync();
        await Images.ReleaseModelAsync();
    }
}
