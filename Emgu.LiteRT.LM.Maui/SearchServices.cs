//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// When a background index may embed: not while the device is too hot or its battery too low
    /// (<see cref="DeviceIndexingWorkload"/>), or whatever else an app holds indexing for.
    /// </summary>
    public interface IIndexingWorkload
    {
        /// <summary>Return when heavy background work may go on.</summary>
        /// <param name="status">Told why it waits, and null when it goes on (only if it waited); may be null</param>
        /// <param name="cancel">Stops waiting</param>
        /// <param name="onlyWhileCharging">True to wait for a charger too, on a phone or tablet</param>
        Task WaitAsync(Action<string> status, CancellationToken cancel, bool onlyWhileCharging = false);

        /// <summary>
        /// One embedding for a background index: waits while the device is too busy, then embeds holding the model's
        /// gate - not held while waiting, so a paused index doesn't block an interactive search.
        /// </summary>
        /// <typeparam name="T">What the embedding returns</typeparam>
        /// <param name="embedder">The model's embedder</param>
        /// <param name="embed">The embedding, run off the UI thread</param>
        /// <param name="paused">Told why it waits, and null when it goes on; may be null</param>
        /// <param name="cancel">Stops it</param>
        /// <param name="onlyWhileCharging">True to wait for a charger too, on a phone or tablet</param>
        Task<T> RunEmbeddingAsync<T>(SearchEmbedder embedder, Func<T> embed, Action<string> paused, CancellationToken cancel, bool onlyWhileCharging = false);
    }

    /// <summary>The default <see cref="IIndexingWorkload"/>: <see cref="WorkloadPolicy"/> over <see cref="DeviceWorkload"/>.</summary>
    public sealed class DeviceIndexingWorkload : IIndexingWorkload
    {
        private readonly WorkloadPolicy _policy = new WorkloadPolicy();

        /// <inheritdoc/>
        public Task WaitAsync(Action<string> status, CancellationToken cancel, bool onlyWhileCharging = false) =>
            _policy.WaitAsync(DeviceWorkload.ReadAsync, () => onlyWhileCharging, status, cancel);

        /// <inheritdoc/>
        public async Task<T> RunEmbeddingAsync<T>(SearchEmbedder embedder, Func<T> embed, Action<string> paused, CancellationToken cancel, bool onlyWhileCharging = false)
        {
            while (true)
            {
                await WaitAsync(paused, cancel, onlyWhileCharging);
                await embedder.Gate.WaitAsync(cancel);
                try
                {
                    if (_policy.PauseReason(await DeviceWorkload.ReadAsync(), onlyWhileCharging) != null)
                        continue;
                    return await Task.Run(embed, cancel);
                }
                finally { embedder.Gate.Release(); }
            }
        }
    }

    /// <summary>
    /// What the search features share: the search model's embedder, when indexing may run, the model's key for the
    /// indexes (passages embedded with another model are rebuilt), and whether the model was deleted. The simplest
    /// app makes one from a choice: <c>new SearchServices(new EmbeddingModelChoice(() => new EmbeddingGemma2TextVision(), "EmbeddingGemma 2"))</c>.
    /// </summary>
    public sealed class SearchServices
    {
        /// <summary>Create the services, to fill in.</summary>
        public SearchServices()
        {
        }

        /// <summary>Create the services for a model choice: its embedder, the device's workload, the model's file as the key.</summary>
        /// <param name="choice">Which model</param>
        public SearchServices(ISearchModelChoice choice)
        {
            if (choice == null)
                throw new ArgumentNullException(nameof(choice));
            Embedder = new SearchEmbedder(choice);
            Workload = new DeviceIndexingWorkload();
            IndexModel = () =>
            {
                using EmbeddingModel model = choice.CreateModel();
                return System.IO.Path.GetFileName(model.DefaultModelFile.Url);
            };
            DownloadRequired = () => choice.DownloadRequired;
        }

        /// <summary>The search model, shared by the searches.</summary>
        public SearchEmbedder Embedder { get; set; }

        /// <summary>When a background index may embed.</summary>
        public IIndexingWorkload Workload { get; set; }

        /// <summary>The search model's key for the indexes (its file name): another model's passages are rebuilt.</summary>
        public Func<string> IndexModel { get; set; }

        /// <summary>Whether the search model was deleted and isn't downloaded again until the user asks (the searches' switches are then off and disabled); or null.</summary>
        public Func<bool> DownloadRequired { get; set; }

        /// <summary>Makes the notice a search's settings section shows when the model was deleted (with a way to download it again); or null.</summary>
        public Func<View> CreateDownloadNotice { get; set; }

        /// <summary>Whether the search model embeds images too (a document's pictures and photo search need it); true unless an app says otherwise.</summary>
        public bool SupportsImages { get; set; } = true;

        /// <summary>Whether the model was deleted (<see cref="DownloadRequired"/>).</summary>
        public bool IsDownloadRequired => DownloadRequired?.Invoke() == true;
    }
}
