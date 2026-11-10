//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Models;
using Microsoft.Extensions.AI;

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
    /// one embedding at a time (<see cref="Gate"/>). A GPU model that fails to load asks the choice for the CPU. When the
    /// choice is a model on a server (<see cref="Remote"/>), texts are embedded there and nothing loads on the device.
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

        /// <summary>
        /// One embedding at a time, searches first: searches take it with <see cref="EnterSearchAsync"/>, background
        /// indexes with <see cref="EnterBackgroundAsync"/> (through <see cref="IIndexingWorkload.RunEmbeddingAsync"/>).
        /// </summary>
        public SearchFirstGate Searches { get; } = new SearchFirstGate();

        /// <summary>
        /// The semaphore under <see cref="Searches"/>, held while embedding: taking it directly excludes everything else
        /// without going before the searches (e.g. to release the model).
        /// </summary>
        public SemaphoreSlim Gate => Searches.Semaphore;

        /// <summary>Take the model for a search, before any background index waiting. Dispose the result.</summary>
        /// <param name="cancel">Stops waiting</param>
        public Task<IDisposable> EnterSearchAsync(CancellationToken cancel = default) => Searches.EnterSearchAsync(cancel);

        /// <summary>Take the model for one background embedding, after the searches. Dispose the result.</summary>
        /// <param name="cancel">Stops waiting</param>
        public Task<IDisposable> EnterBackgroundAsync(CancellationToken cancel = default) => Searches.EnterBackgroundAsync(cancel);

        /// <summary>The loaded model, or null.</summary>
        public EmbeddingModel Model { get; private set; }

        /// <summary>Whether the model is loaded or loading.</summary>
        public bool IsLoaded => _load != null;

        /// <summary>
        /// Gives the server's embedding generator when the chosen search model runs on a server (a Mac serving its model:
        /// text only), or null for a model on this device; null for none.
        /// </summary>
        public Func<IEmbeddingGenerator<string, Embedding<float>>> Remote { get; set; }

        /// <summary>Whether the chosen search model runs on a server (<see cref="Remote"/>): texts only, no model on the device.</summary>
        public bool IsRemote => Remote?.Invoke() != null;

        /// <summary>
        /// Embed a search query: on the server, or with the model on the device (loading it the first time), before any
        /// background index waiting.
        /// </summary>
        /// <param name="text">The query</param>
        /// <param name="cancel">Stops it</param>
        public async Task<float[]> EmbedQueryAsync(string text, CancellationToken cancel)
        {
            IEmbeddingGenerator<string, Embedding<float>> remote = Remote?.Invoke();
            if (remote != null)
                return (await EmbedRemoteAsync(remote, new[] { text }, "query", cancel))[0];
            EmbeddingModel model = await EnsureAsync(null, cancel);
            using (await EnterSearchAsync(cancel))
                return await Task.Run(() => model.EmbedQuery(text), cancel);
        }

        /// <summary>Embed passages to index on the server (<see cref="Remote"/>, which must be set).</summary>
        /// <param name="texts">The passages</param>
        /// <param name="cancel">Stops it</param>
        public Task<float[][]> EmbedDocumentsRemotelyAsync(string[] texts, CancellationToken cancel)
        {
            IEmbeddingGenerator<string, Embedding<float>> remote = Remote?.Invoke()
                ?? throw new InvalidOperationException("The search model isn't on a server.");
            return EmbedRemoteAsync(remote, texts, "document", cancel);
        }

        private static async Task<float[][]> EmbedRemoteAsync(IEmbeddingGenerator<string, Embedding<float>> remote, string[] texts, string inputType, CancellationToken cancel)
        {
            var options = new EmbeddingGenerationOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary { [LiteRtLmEmbeddingGenerator.InputTypeKey] = inputType }
            };
            GeneratedEmbeddings<Embedding<float>> embeddings = await remote.GenerateAsync(texts, options, cancel);
            if (embeddings.Count != texts.Length)
                throw new InvalidOperationException("The server gave " + embeddings.Count + " embeddings for " + texts.Length + " texts.");
            return embeddings.Select(e => e.Vector.ToArray()).ToArray();
        }

        /// <summary>The model, loading it the first time.</summary>
        /// <param name="stage">Told the download's progress and the loading, or null</param>
        /// <param name="cancel">Stops the download or the loading</param>
        /// <exception cref="InvalidOperationException">The model was deleted (<see cref="ISearchModelChoice.DownloadRequired"/>),
        /// or it runs on a server (<see cref="IsRemote"/>)</exception>
        public Task<EmbeddingModel> EnsureAsync(IProgress<string> stage, CancellationToken cancel)
        {
            if (IsRemote)
                return Task.FromException<EmbeddingModel>(new InvalidOperationException("The search model runs on the server: only text is searched (no photos or videos)."));
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
