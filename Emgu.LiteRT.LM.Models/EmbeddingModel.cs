//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// Base class of the ready-to-use embedding models: downloads a .litertlm embedding model if needed and loads it
    /// into an EmbeddingEngine. An embedding turns a text into a vector, so that texts with similar meaning get vectors
    /// with a high cosine similarity - for semantic search, e.g. finding the parts of a document that answer a question.
    /// </summary>
    public abstract class EmbeddingModel : DisposableObject
    {
        private readonly FileDownloadManager _downloadManager;
        private EmbeddingEngine _engine;

        /// <summary>
        /// Create the model. Call Init to download and load it.
        /// </summary>
        protected EmbeddingModel()
        {
            _downloadManager = new FileDownloadManager();
            _downloadManager.OnDownloadProgressChanged += (total, received, percent) =>
            {
                if (OnDownloadProgressChanged != null)
                    OnDownloadProgressChanged(total, received, percent);
            };
        }

        /// <summary>
        /// Raised while the model file is downloaded
        /// </summary>
        public event FileDownloadManager.DownloadProgressChangedEventHandler OnDownloadProgressChanged;

        /// <summary>
        /// The model file downloaded when Init is called without one
        /// </summary>
        public abstract DownloadableFile DefaultModelFile { get; }

        /// <summary>
        /// The prefix of a search query (EmbedQuery), or an empty string
        /// </summary>
        public virtual String QueryPrefix
        {
            get { return String.Empty; }
        }

        /// <summary>
        /// The prefix of a text to be found (EmbedDocument), or an empty string
        /// </summary>
        public virtual String DocumentPrefix
        {
            get { return String.Empty; }
        }

        /// <summary>
        /// The embedding engine, or null before Init
        /// </summary>
        public EmbeddingEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>
        /// True once Init has loaded the model
        /// </summary>
        public bool Initialized
        {
            get { return _engine != null; }
        }

        /// <summary>
        /// Download the model file if it isn't already, and load it - on background threads.
        /// </summary>
        /// <param name="modelFile">The model file, or null for DefaultModelFile</param>
        /// <param name="backend">The backend, e.g. "cpu" or "gpu"</param>
        /// <param name="configureSettings">Called to adjust the settings before the engine is created, or null</param>
        /// <param name="cancellationToken">Cancels checking or downloading the file</param>
        /// <returns>A task that completes when the model is loaded</returns>
        public async Task Init(
            DownloadableFile modelFile = null,
            String backend = "cpu",
            Action<EmbeddingEngineSettings> configureSettings = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (modelFile == null)
                modelFile = DefaultModelFile;
            bool valid = await Task.Run(() => modelFile.IsLocalFileValid, cancellationToken).ConfigureAwait(false);
            if (!valid)
            {
                _downloadManager.Clear();
                _downloadManager.AddFile(modelFile);
                await Task.Run(() => _downloadManager.Download(1, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (!await Task.Run(() => modelFile.IsLocalFileValid, cancellationToken).ConfigureAwait(false))
                    throw new Exception(String.Format("Failed to download {0}", modelFile.Url));
            }
            cancellationToken.ThrowIfCancellationRequested();
            String modelPath = modelFile.LocalFile;
            EmbeddingEngine engine = await Task.Run(() =>
            {
                using (EmbeddingEngineSettings settings = new EmbeddingEngineSettings(modelPath, backend))
                {
                    settings.CacheDir = Path.GetDirectoryName(modelPath);
                    if (configureSettings != null)
                        configureSettings(settings);
                    return new EmbeddingEngine(settings);
                }
            }).ConfigureAwait(false);
            if (_engine != null)
                _engine.Dispose();
            _engine = engine;
        }

        private EmbeddingEngine CheckEngine()
        {
            if (_engine == null)
                throw new InvalidOperationException("The model is not initialized: call Init first");
            return _engine;
        }

        private static EmbeddingOptions CreateOptions(int outputSize)
        {
            EmbeddingOptions options = new EmbeddingOptions();
            options.Normalize = true;
            if (outputSize > 0)
                options.OutputSize = outputSize;
            return options;
        }

        /// <summary>
        /// The normalized embedding of a text, as it is.
        /// </summary>
        /// <param name="text">The text</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embedding</returns>
        public float[] Embed(String text, int outputSize = 0)
        {
            using (EmbeddingOptions options = CreateOptions(outputSize))
                return CheckEngine().ComputeEmbedding(text, options);
        }

        /// <summary>
        /// The normalized embeddings of several texts, as they are, in one batch.
        /// </summary>
        /// <param name="texts">The texts</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embeddings, in the order of the texts</returns>
        public float[][] Embed(String[] texts, int outputSize = 0)
        {
            using (EmbeddingOptions options = CreateOptions(outputSize))
                return CheckEngine().ComputeEmbeddings(texts, options);
        }

        /// <summary>
        /// The embedding of a search query, with the model's query prefix.
        /// </summary>
        public float[] EmbedQuery(String query, int outputSize = 0)
        {
            return Embed(QueryPrefix + query, outputSize);
        }

        /// <summary>
        /// The embeddings of texts to search, with the model's document prefix.
        /// </summary>
        public float[][] EmbedDocuments(String[] documents, int outputSize = 0)
        {
            String[] prefixed = new String[documents.Length];
            for (int i = 0; i < documents.Length; i++)
                prefixed[i] = DocumentPrefix + documents[i];
            return Embed(prefixed, outputSize);
        }

        /// <summary>
        /// The cosine similarity of two embeddings (for normalized ones, their dot product).
        /// </summary>
        public static float CosineSimilarity(float[] a, float[] b)
        {
            return EmbeddingEngine.CosineSimilarity(a, b);
        }

        /// <summary>
        /// Release the engine
        /// </summary>
        protected override void DisposeObject()
        {
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }
        }
    }

    /// <summary>
    /// EmbeddingGemma 2 Text 270M (Apache 2.0): a small, multilingual text embedding model - 768 values, or 512, 256 or
    /// 128 (OutputSize), inputs up to 8192 tokens - from https://huggingface.co/litert-community/embeddinggemma-2-text-270m-litert-lm
    /// </summary>
    public class EmbeddingGemma2 : EmbeddingModel
    {
        /// <summary>
        /// The model file for CPU and GPU (about 165 MB). The repository also has NPU builds for some phones.
        /// </summary>
        public static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/embeddinggemma-2-text-270m-litert-lm/resolve/main/embeddinggemma-2-text-270m.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "2d079ee2f6f066b1f368e8d7c819f55214eaef1d0513b312321901f30ab286fb");
            }
        }

        /// <summary>
        /// The model file downloaded when Init is called without one: ModelFile
        /// </summary>
        public override DownloadableFile DefaultModelFile
        {
            get { return ModelFile; }
        }

        /// <summary>
        /// "task: search result | query: ", EmbeddingGemma's prompt for search queries
        /// </summary>
        public override String QueryPrefix
        {
            get { return "task: search result | query: "; }
        }

        /// <summary>
        /// "title: none | text: ", EmbeddingGemma's prompt for the texts to search
        /// </summary>
        public override String DocumentPrefix
        {
            get { return "title: none | text: "; }
        }
    }
}
