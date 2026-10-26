//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The search model a <see cref="SearchEmbedder"/> loads: which one, on which backend, and what to do when it can't
    /// load on the GPU. An app with one fixed model uses <see cref="EmbeddingModelChoice"/>; one with a setting
    /// implements this.
    /// </summary>
    public interface ISearchModelChoice
    {
        /// <summary>A new instance of the chosen model (not loaded).</summary>
        EmbeddingModel CreateModel();

        /// <summary>Its name, for status lines ("Downloading EmbeddingGemma 2...").</summary>
        string ModelName { get; }

        /// <summary>Its backend: "cpu" or "gpu".</summary>
        string Backend { get; }

        /// <summary>Whether the backend is the GPU (a failed load then asks for the CPU).</summary>
        bool UsesGpu { get; }

        /// <summary>The model was deleted and isn't downloaded again until the user asks (the embedder refuses to load).</summary>
        bool DownloadRequired { get; }

        /// <summary>The GPU model failed to load: ask whether to use the CPU instead and, if so, switch the choice to it.</summary>
        /// <param name="error">Why it failed</param>
        /// <returns>True to load the model on the CPU</returns>
        Task<bool> SwitchToCpuAsync(string error);
    }

    /// <summary>One fixed search model on the CPU (e.g. <c>new EmbeddingModelChoice(() => new EmbeddingGemma2TextVision(), "EmbeddingGemma 2")</c>).</summary>
    public sealed class EmbeddingModelChoice : ISearchModelChoice
    {
        private readonly Func<EmbeddingModel> _create;

        /// <summary>Create the choice.</summary>
        /// <param name="create">Makes a new instance of the model</param>
        /// <param name="name">Its name, for status lines</param>
        public EmbeddingModelChoice(Func<EmbeddingModel> create, string name)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            ModelName = name ?? "the search model";
        }

        /// <inheritdoc/>
        public EmbeddingModel CreateModel() => _create();

        /// <inheritdoc/>
        public string ModelName { get; }

        /// <inheritdoc/>
        public string Backend => "cpu";

        /// <inheritdoc/>
        public bool UsesGpu => false;

        /// <inheritdoc/>
        public bool DownloadRequired => false;

        /// <inheritdoc/>
        public Task<bool> SwitchToCpuAsync(string error) => Task.FromResult(false);
    }

    /// <summary>
    /// The search model that the searches of a chat page share (past chats, documents, photos): loaded once - downloaded
    /// the first time, in the background (<see cref="BackgroundDownloads"/>) - and kept until <see cref="ReleaseAsync"/>;
    /// one embedding at a time (<see cref="Gate"/>). A GPU model that fails to load asks the choice for the CPU.
    /// </summary>
    public sealed class SearchEmbedder
    {
        private readonly ISearchModelChoice _choice;
        private Task<EmbeddingModel> _load;

        /// <summary>Create the embedder.</summary>
        /// <param name="choice">Which model, and its backend</param>
        public SearchEmbedder(ISearchModelChoice choice)
        {
            _choice = choice ?? throw new ArgumentNullException(nameof(choice));
        }

        /// <summary>Held while embedding: the searches share the model.</summary>
        public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);

        /// <summary>The loaded model, or null.</summary>
        public EmbeddingModel Model { get; private set; }

        /// <summary>Whether the model is loaded or loading.</summary>
        public bool IsLoaded => _load != null;

        /// <summary>The model, loading it the first time.</summary>
        /// <param name="stage">Told the download's progress and the loading, or null</param>
        /// <param name="cancel">Stops the download or the loading</param>
        /// <exception cref="InvalidOperationException">The model was deleted (<see cref="ISearchModelChoice.DownloadRequired"/>)</exception>
        public Task<EmbeddingModel> EnsureAsync(IProgress<string> stage, CancellationToken cancel)
        {
            if (_choice.DownloadRequired)
                return Task.FromException<EmbeddingModel>(new InvalidOperationException("The search model was deleted. Download it again in the settings."));
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
                    (total, received, percent) => stage?.Report(string.Format("Downloading {0}: {1:F0}%", name, percent)), cancel);
                Action<EmbeddingEngineSettings> configure = settings =>
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
                    if (!await _choice.SwitchToCpuAsync(ex.Message))
                        throw;
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

        /// <summary>Free the model (loaded again when a search needs it). Call it holding <see cref="Gate"/>, or use <see cref="ReleaseWithGateAsync"/>.</summary>
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

        /// <summary>Free the model once nothing is embedding.</summary>
        public async Task ReleaseWithGateAsync()
        {
            await Gate.WaitAsync();
            try { await ReleaseAsync(); }
            finally { Gate.Release(); }
        }
    }
}
