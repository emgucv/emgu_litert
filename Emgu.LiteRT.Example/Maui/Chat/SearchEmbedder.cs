// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using Emgu.LiteRT.LM.Models;

namespace Maui.Demo.Lite;

// The embedding model chosen in Settings > Embedding model, as SearchEmbedder needs it. The page implements it (ModelSettings.cs).
internal interface ISearchModelChoice
{
    // A new instance of the chosen model (not loaded), its name, its backend ("cpu" / "gpu") and whether that is the GPU.
    EmbeddingModel CreateModel();
    string ModelName { get; }
    string Backend { get; }
    bool UsesGpu { get; }

    // The model was deleted in Settings > Storage: it isn't downloaded again until the user asks.
    bool DownloadRequired { get; }

    // The GPU model failed to load: asks the user whether to use the CPU instead and, if so, switches the choice to it.
    Task<bool> SwitchToCpuAsync(string error);
}

// The search model that document search, photo search and past-chat search share (EmbeddingGemma 2): loaded once - downloaded the
// first time - and kept while the page is open, one embedding at a time (Gate).
internal sealed class SearchEmbedder
{
    private readonly ISearchModelChoice _choice;
    private Task<EmbeddingModel> _load;

    public SearchEmbedder(ISearchModelChoice choice) { _choice = choice; }

    // Held while embedding: the searches share the model.
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);

    // The loaded model, or null.
    public EmbeddingModel Model { get; private set; }

    // Whether the model is loaded or loading.
    public bool IsLoaded => _load != null;

    // The model, loading it the first time (stage reports the download and loading).
    public Task<EmbeddingModel> EnsureAsync(IProgress<string> stage, CancellationToken cancel)
    {
        if (_choice.DownloadRequired)
            return Task.FromException<EmbeddingModel>(new InvalidOperationException("The search model was deleted. Download it again in Settings > Storage."));
        if (_load == null || _load.IsFaulted || _load.IsCanceled)
            _load = LoadAsync(stage, cancel);
        return _load;
    }

    private async Task<EmbeddingModel> LoadAsync(IProgress<string> stage, CancellationToken cancel)
    {
        EmbeddingModel embedder = _choice.CreateModel();
        string name = _choice.ModelName;
        embedder.OnDownloadProgressChanged += (total, received, percent) =>
            stage?.Report(string.Format("Downloading {0}: {1:F0}%", name, percent));
        stage?.Report(embedder.DefaultModelFile.IsLocalFileVerified ? "Loading the search model..." : "Downloading " + name + "...");
        try
        {
            await BackgroundDownloads.DownloadFileAsync(embedder.DefaultModelFile, name,
                (total, received, percent) => stage?.Report($"Downloading {name}: {percent:0}%"), cancel);
            Action<Emgu.LiteRT.LM.EmbeddingEngineSettings> configure = settings =>
            {
                int count = CpuThreadSettings.Count;
                settings.NumThreads = count;
                settings.AudioNumThreads = count;
            };
            try
            {
                await embedder.Init(backend: _choice.Backend, configureSettings: configure, cancellationToken: cancel);
            }
            catch (Exception ex) when (_choice.UsesGpu && !cancel.IsCancellationRequested)
            {
                if (!await _choice.SwitchToCpuAsync(ex.Message)) throw;
                embedder.Dispose();
                embedder = _choice.CreateModel();
                await embedder.Init(configureSettings: configure, cancellationToken: cancel);
            }
        }
        catch (Exception)
        {
            embedder.Dispose();
            throw;
        }
        Model = embedder;
        return embedder;
    }

    // Frees the model (loaded again when a search needs it). Call it holding Gate, or use ReleaseWithGateAsync.
    public async Task ReleaseAsync()
    {
        Task<EmbeddingModel> load = _load;
        _load = null;
        if (load == null)
            return;
        try
        {
            EmbeddingModel embedder = await load;
            Model = null;
            await Task.Run(() => embedder.Dispose());
        }
        catch (Exception)
        {
        }
    }

    // Frees the model once nothing is embedding.
    public async Task ReleaseWithGateAsync()
    {
        await Gate.WaitAsync();
        try { await ReleaseAsync(); }
        finally { Gate.Release(); }
    }
}
