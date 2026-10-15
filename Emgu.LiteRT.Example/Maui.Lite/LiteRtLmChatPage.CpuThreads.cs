#if WINDOWS || IOS || ANDROID || MACCATALYST
using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Models;

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
        bool reloadEmbedder = _embedderLoad != null;
        bool reloadImage = _zimageInit != null;
        try
        {
            // Prevent new work while allowing the current send (including its tools) to finish.
            _settingsView.IsEnabled = false;
            _photoIndexing?.Cancel();
            await StopChatIndexingAsync();
            while (_busy)
                await Task.Delay(100);
            SetBusy(true);
            try { await (_photoIndexTask ?? Task.CompletedTask); } catch (OperationCanceledException) { }
            if (_document?.Ready != null)
                await _document.Ready;
            // A send can load a tool model while we wait, so check again after it finishes.
            reloadChat |= _model != null || _loadTask != null;
            reloadEmbedder |= _embedderLoad != null;
            reloadImage |= _zimageInit != null;
            _loadCancel?.Cancel();
            if (_loadTask != null)
                await _loadTask;
            await CleanupModelAsync();
            await ReleaseEmbedderAsync();
            await ReleaseImageModelAsync();
            _zimageInit = null;
            if (_zimage != null)
                _zimage = new ZImageTurbo();
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
                await EnsureEmbedderAsync(null, CancellationToken.None);
            if (reloadImage)
                await EnsureImageModelAsync();
            SetStatus($"Models ready with {CpuThreadSettings.Count} CPU threads.");
        }
        finally
        {
            _reloadingCpuThreads = false;
            _settingsView.IsEnabled = true;
            SetBusy(false);
            if (_pageVisible)
                StartChatIndexing();
            if (_pageVisible)
                StartPhotoIndexing();
        }
    }

    private async Task CleanupAfterCpuReloadAsync()
    {
        try { await (_cpuReloadTask ?? Task.CompletedTask); } catch (Exception) { }
        await StopChatIndexingAsync();
        await CleanupModelAsync();
        await ReleaseEmbedderAsync();
        await ReleaseImageModelAsync();
    }
}
#endif
