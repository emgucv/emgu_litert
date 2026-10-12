//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        /// The backend of the vision encoder of models that embed images, e.g. "cpu" or "gpu"; null to not load it
        /// (images are then not supported). "cpu" by default; "gpu" is refused for now (see Init). Set before Init.
        /// </summary>
        public String VisionBackend { get; set; } = "cpu";

        /// <summary>
        /// The backend of the audio encoder of models that embed audio, e.g. "cpu"; null to not load it (audio is
        /// then not supported). Set before Init.
        /// </summary>
        public String AudioBackend { get; set; } = "cpu";

        /// <summary>
        /// True if the model can embed images (EmbedImage)
        /// </summary>
        public virtual bool SupportsImages
        {
            get { return false; }
        }

        /// <summary>
        /// True if the model can embed audio (EmbedAudio)
        /// </summary>
        public virtual bool SupportsAudio
        {
            get { return false; }
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
        /// <param name="backend">The backend: "cpu". "gpu" is refused for now (see the remarks)</param>
        /// <param name="configureSettings">Called to adjust the settings before the engine is created, or null</param>
        /// <param name="cancellationToken">Cancels checking or downloading the file</param>
        /// <returns>A task that completes when the model is loaded</returns>
        /// <remarks>
        /// The GPU crashes the process with EmbeddingGemma 2 (Metal on macOS, OpenCL on Android): its weights are
        /// external buffers, which the GPU accelerator plugins bind through LiteRT's WeightLoader interface, and the
        /// prebuilt plugins were built after LiteRT changed that interface's return type from absl::Status to
        /// LiteRtStatus (LiteRT commit dd3db3123, 2026-10-08), while libLiteRt is built from the LiteRT that
        /// LiteRT-LM v0.18.0 pins, before that change. Init refuses a "gpu" backend until both match.
        /// </remarks>
        public async Task Init(
            DownloadableFile modelFile = null,
            String backend = "cpu",
            Action<EmbeddingEngineSettings> configureSettings = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsGpu(backend) || (SupportsImages && IsGpu(VisionBackend)) || (SupportsAudio && IsGpu(AudioBackend)))
                throw new NotSupportedException(
                    "Embedding models can't run on the GPU with this LiteRT build: the prebuilt GPU accelerator crashes "
                    + "binding their external weights (a LiteRT WeightLoader ABI mismatch). Use the \"cpu\" backend.");
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
                using (EmbeddingEngineSettings settings = CreateEngineSettings(modelPath, backend))
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

        /// <summary>
        /// Create the engine settings for Init, with VisionBackend and AudioBackend for the models that embed images or
        /// audio.
        /// </summary>
        /// <param name="modelPath">The path of the model file</param>
        /// <param name="backend">The main backend</param>
        /// <returns>The settings</returns>
        protected virtual EmbeddingEngineSettings CreateEngineSettings(String modelPath, String backend)
        {
            return new EmbeddingEngineSettings(
                modelPath,
                backend,
                SupportsImages ? VisionBackend : null,
                SupportsAudio ? AudioBackend : null);
        }

        private static bool IsGpu(String backend)
        {
            return String.Equals(backend, "gpu", StringComparison.OrdinalIgnoreCase);
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
        /// The normalized embedding of one request made of several inputs - e.g. an image and a caption - as they are.
        /// </summary>
        /// <param name="inputs">The inputs: text, image or audio</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embedding</returns>
        public float[] Embed(InputData[] inputs, int outputSize = 0)
        {
            using (EmbeddingOptions options = CreateOptions(outputSize))
                return CheckEngine().ComputeEmbedding(inputs, options);
        }

        /// <summary>
        /// The normalized embedding of an image, optionally with a text describing it (e.g. a title or caption).
        /// Images and texts share one embedding space, so the result can be compared with EmbedQuery's to find the
        /// images a text describes.
        /// </summary>
        /// <param name="encodedImage">The image file's content: JPEG, PNG, BMP, ...</param>
        /// <param name="text">A text that goes with the image, or null</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embedding</returns>
        public float[] EmbedImage(byte[] encodedImage, String text = null, int outputSize = 0)
        {
            if (!SupportsImages)
                throw new NotSupportedException(String.Format("{0} can't embed images", GetType().Name));
            return EmbedWithMedia(InputDataType.Image, encodedImage, text, outputSize);
        }

        /// <summary>
        /// The normalized embedding of an image file, optionally with a text describing it.
        /// </summary>
        /// <param name="imageFile">The image file: JPEG, PNG, BMP, ...</param>
        /// <param name="text">A text that goes with the image, or null</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embedding</returns>
        public float[] EmbedImageFile(String imageFile, String text = null, int outputSize = 0)
        {
            return EmbedImage(File.ReadAllBytes(imageFile), text, outputSize);
        }

        /// <summary>
        /// The normalized embeddings of several images, in one batch.
        /// </summary>
        /// <param name="encodedImages">The image files' contents: JPEG, PNG, BMP, ...</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embeddings, in the order of the images</returns>
        public float[][] EmbedImages(IList<byte[]> encodedImages, int outputSize = 0)
        {
            if (!SupportsImages)
                throw new NotSupportedException(String.Format("{0} can't embed images", GetType().Name));
            InputData[][] requests = new InputData[encodedImages.Count][];
            try
            {
                for (int i = 0; i < requests.Length; i++)
                    requests[i] = new[] { InputData.FromImage(encodedImages[i]) };
                using (EmbeddingOptions options = CreateOptions(outputSize))
                    return CheckEngine().ComputeEmbeddings(requests, options);
            }
            finally
            {
                foreach (InputData[] request in requests)
                    if (request != null)
                        request[0].Dispose();
            }
        }

        /// <summary>
        /// The normalized embedding of a sound, optionally with a text that goes with it.
        /// </summary>
        /// <param name="encodedAudio">The audio file's content: WAV, MP3, FLAC</param>
        /// <param name="text">A text that goes with the audio, or null</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embedding</returns>
        public float[] EmbedAudio(byte[] encodedAudio, String text = null, int outputSize = 0)
        {
            if (!SupportsAudio)
                throw new NotSupportedException(String.Format("{0} can't embed audio", GetType().Name));
            return EmbedWithMedia(InputDataType.Audio, encodedAudio, text, outputSize);
        }

        /// <summary>
        /// The normalized embedding of an audio file, optionally with a text that goes with it.
        /// </summary>
        /// <param name="audioFile">The audio file: WAV, MP3, FLAC</param>
        /// <param name="text">A text that goes with the audio, or null</param>
        /// <param name="outputSize">The number of values, for models that allow shorter embeddings; 0 for all</param>
        /// <returns>The embedding</returns>
        public float[] EmbedAudioFile(String audioFile, String text = null, int outputSize = 0)
        {
            return EmbedAudio(File.ReadAllBytes(audioFile), text, outputSize);
        }

        private float[] EmbedWithMedia(InputDataType type, byte[] data, String text, int outputSize)
        {
            using (InputData media = new InputData(type, data))
            using (InputData caption = String.IsNullOrEmpty(text) ? null : new InputData(text))
                return Embed(caption == null ? new[] { media } : new[] { media, caption }, outputSize);
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

    /// <summary>
    /// EmbeddingGemma 2 Text Vision 440M (Apache 2.0): EmbeddingGemma 2 Text 270M plus a vision encoder, mapping texts
    /// and images into one 768-value embedding space (or 512, 256, 128) - so a text query can find images (EmbedImage)
    /// and an image can find similar images or texts. From
    /// https://huggingface.co/litert-community/embeddinggemma-2-text-vision-440m-litert-lm
    /// </summary>
    public class EmbeddingGemma2TextVision : EmbeddingGemma2
    {
        /// <summary>
        /// The model file for CPU and GPU (about 388 MB). The vision encoder is only loaded when an image is embedded.
        /// </summary>
        public new static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/embeddinggemma-2-text-vision-440m-litert-lm/resolve/main/embeddinggemma-2-text-vision-440m.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "92dcbea108899e5d6e30d919b0744f90d9967e80c67a4ab5503ac16d54f62eb0");
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
        /// True unless VisionBackend is null
        /// </summary>
        public override bool SupportsImages
        {
            get { return VisionBackend != null; }
        }
    }

    /// <summary>
    /// EmbeddingGemma 2 740M (Apache 2.0): the full multimodal EmbeddingGemma 2 - text, images and audio in one
    /// 768-value embedding space (or 512, 256, 128). From
    /// https://huggingface.co/litert-community/embeddinggemma-2-740m-litert-lm
    /// </summary>
    public class EmbeddingGemma2Multimodal : EmbeddingGemma2
    {
        /// <summary>
        /// The model file for CPU and GPU (about 485 MB). The vision and audio encoders are only loaded when first used.
        /// </summary>
        public new static DownloadableFile ModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/embeddinggemma-2-740m-litert-lm/resolve/main/embeddinggemma-2-740m.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "e7a8a2204b91e0f96e92960e84a09a89212e1633dcb7575a9bf3378b4df77f4c");
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
        /// True unless VisionBackend is null
        /// </summary>
        public override bool SupportsImages
        {
            get { return VisionBackend != null; }
        }

        /// <summary>
        /// True unless AudioBackend is null
        /// </summary>
        public override bool SupportsAudio
        {
            get { return AudioBackend != null; }
        }
    }
}
