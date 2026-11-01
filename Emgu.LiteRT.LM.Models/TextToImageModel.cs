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
    /// A text-to-image model on LiteRT-LM's OmniEngine (<see cref="TextToImageEngine"/>): downloads its .litertlm,
    /// then draws square pictures for prompts, all on the device.
    /// </summary>
    /// <remarks>
    /// LiteRT-LM draws a picture in one call, with no progress and no way to stop it: <see cref="GenerateAsync"/>
    /// checks the cancellation token before it starts, and a cancellation while it draws returns at once but leaves
    /// the engine busy until the picture is done (it is then thrown away).
    /// </remarks>
    public abstract class TextToImageModel : DisposableObject
    {
        private readonly FileDownloadManager _downloadManager;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private TextToImageEngine _engine;

        /// <summary>Create the model; call Init before drawing.</summary>
        protected TextToImageModel()
        {
            _downloadManager = new FileDownloadManager();
            _downloadManager.OnDownloadProgressChanged += (total, received, percent) =>
            {
                if (OnDownloadProgressChanged != null)
                    OnDownloadProgressChanged(total, received, percent);
            };
        }

        /// <summary>The download's progress.</summary>
        public event FileDownloadManager.DownloadProgressChangedEventHandler OnDownloadProgressChanged;

        /// <summary>The model's name in LiteRT-LM's model registry, e.g. "bonsai-flux2".</summary>
        public abstract String ModelName { get; }

        /// <summary>The name to show, e.g. "Bonsai Image 4B".</summary>
        public abstract String DisplayName { get; }

        /// <summary>The model file.</summary>
        public abstract DownloadableFile ModelFile { get; }

        /// <summary>The model file's size in bytes, for asking before the download.</summary>
        public abstract long DownloadSize { get; }

        /// <summary>The side of the square pictures it draws, in pixels (its graphs are made for this size).</summary>
        public abstract int ImageSize { get; }

        /// <summary>
        /// The folder of its compiled-model caches (about 3.5 GB for the CPU, as much again for the GPU), next to the
        /// model file: "&lt;model name&gt;-cache". LiteRT-LM names the caches after the graphs ("flux2_dit_..."), the
        /// same for both models, so each has a folder of its own.
        /// </summary>
        public String CacheFolder
        {
            get { return Path.Combine(Path.GetDirectoryName(ModelFile.LocalFile), CacheFolderName); }
        }

        /// <summary>The name of <see cref="CacheFolder"/> in the models' folder: "&lt;model name&gt;-cache" by default.</summary>
        public virtual String CacheFolderName
        {
            get { return ModelName + "-cache"; }
        }

        /// <summary>The denoising steps it was distilled for.</summary>
        public virtual int DefaultSteps
        {
            get { return 4; }
        }

        /// <summary>The engine, once initialized.</summary>
        public TextToImageEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>True once Init has loaded the model.</summary>
        public bool Initialized
        {
            get { return _engine != null; }
        }

        /// <summary>The model file is downloaded and verified (reads the whole file the first time: call it off the UI thread).</summary>
        public bool IsDownloaded
        {
            get { return ModelFile.IsLocalFileValid; }
        }

        /// <summary>The backend Init loaded the model on, or null.</summary>
        public String Backend { get; private set; }

        /// <summary>
        /// Download the model file if needed (resuming a partial one).
        /// </summary>
        /// <param name="cancellationToken">Cancels the download</param>
        public async Task Download(CancellationToken cancellationToken = default(CancellationToken))
        {
            DownloadableFile file = ModelFile;
            if (await Task.Run(() => file.IsLocalFileValid, cancellationToken).ConfigureAwait(false))
                return;
            _downloadManager.Clear();
            _downloadManager.AddFile(file);
            await Task.Run(() => _downloadManager.Download(3, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (!await Task.Run(() => file.IsLocalFileValid, cancellationToken).ConfigureAwait(false))
                throw new IOException(String.Format("Failed to download {0}", file.Url));
        }

        /// <summary>
        /// Download the model file if needed, then load it.
        /// </summary>
        /// <param name="backend">"cpu" or "gpu"</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="cancellationToken">Cancels the download</param>
        public async Task Init(String backend = "cpu", int numThreads = 0, CancellationToken cancellationToken = default(CancellationToken))
        {
            await Download(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            String modelPath = ModelFile.LocalFile;
            String name = ModelName;
            int size = ImageSize;
            String cacheFolder = CacheFolder;
            TextToImageEngine engine = await Task.Run(() =>
            {
                Directory.CreateDirectory(cacheFolder);
                return new TextToImageEngine(name, modelPath, backend ?? "cpu", numThreads, size, cacheFolder);
            }).ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_engine != null)
                    _engine.Dispose();
                _engine = engine;
                Backend = backend ?? "cpu";
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Draw a picture, one at a time.
        /// </summary>
        /// <param name="prompt">What to draw</param>
        /// <param name="seed">The starting noise, from 1: the same prompt and seed give the same picture</param>
        /// <param name="steps">The denoising steps, or 0 for <see cref="DefaultSteps"/></param>
        /// <param name="cancellationToken">Checked before the picture starts; a cancellation while it is drawn returns
        /// at once, and the picture is thrown away when it is done</param>
        /// <returns>The picture, ImageSize x ImageSize RGB</returns>
        public async Task<TextToImageResult> GenerateAsync(String prompt, ulong seed, int steps = 0,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("The prompt is empty.", "prompt");
            if (seed == 0)
                throw new ArgumentOutOfRangeException("seed", "The seed starts at 1 (0 is the model's fixed default).");
            if (steps < 0)
                throw new ArgumentOutOfRangeException("steps");
            if (_engine == null)
                throw new InvalidOperationException("The model is not initialized: call Init first");
            int stepCount = steps == 0 ? DefaultSteps : steps;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            Task<TextToImageResult> drawing;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                TextToImageEngine engine = _engine;
                drawing = Task.Run(() => engine.Generate(prompt, stepCount, seed));
            }
            catch
            {
                _gate.Release();
                throw;
            }
            // The gate is released when the native call returns, whether or not the caller still waits for it.
            Task release = drawing.ContinueWith(t => _gate.Release(), TaskScheduler.Default);
            if (!cancellationToken.CanBeCanceled)
                return await drawing.ConfigureAwait(false);
            TaskCompletionSource<bool> cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(drawing, cancelled.Task).ConfigureAwait(false) != drawing)
                {
                    // Observe the abandoned picture's outcome so a late failure isn't unobserved.
                    _ = drawing.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await drawing.ConfigureAwait(false);
        }

        /// <summary>Release the engine (after a picture being drawn is done).</summary>
        protected override void DisposeObject()
        {
            _gate.Wait();
            try
            {
                if (_engine != null)
                {
                    _engine.Dispose();
                    _engine = null;
                }
                Backend = null;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// PrismML's Bonsai Image 4B (ternary weights on FLUX.2-klein-4B's architecture) for LiteRT: 256 x 256 pictures,
    /// distilled for 4 steps. Apache-2.0. One 4.3 GB .litertlm (the Qwen3 text encoder, the diffusion transformer and
    /// the VAE decoder), downloaded on Init. On an M1 (2026-11-01): 24 s a picture on the CPU (4.8 GB at the peak), 11 s
    /// on the GPU (Metal, 2.1 GB); the first load writes about 3.6 GB of caches (28-33 s), later loads take 1-3 s.
    /// </summary>
    public class BonsaiImage4B : TextToImageModel
    {
        /// <summary>The model's name in LiteRT-LM's registry.</summary>
        public const String RegisteredName = "bonsai-flux2";

        /// <summary>The model file's size in bytes.</summary>
        public const long FileSize = 4267474352;

        /// <summary>The model file, at the Hugging Face revision checked here.</summary>
        public static DownloadableFile DefaultModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Bonsai-Image-ternary-4B/resolve/317d577f5f962eb2d5a3980bca2ce047a8338212/bonsai-flux2-4b.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "2b00b8906802416516f0dce59f21a73197e99bc23d7bfc585d52c097afb3eebc");
            }
        }

        private readonly DownloadableFile _modelFile = DefaultModelFile;

        /// <inheritdoc/>
        public override String ModelName
        {
            get { return RegisteredName; }
        }

        /// <inheritdoc/>
        public override String DisplayName
        {
            get { return "Bonsai Image 4B"; }
        }

        /// <inheritdoc/>
        public override DownloadableFile ModelFile
        {
            get { return _modelFile; }
        }

        /// <inheritdoc/>
        public override long DownloadSize
        {
            get { return FileSize; }
        }

        /// <inheritdoc/>
        public override int ImageSize
        {
            get { return 256; }
        }
    }

    /// <summary>
    /// Bonsai Image 4B for 512 x 512 pictures: litert-community's .litertlm repackaged by Emgu with the 512 graphs - the
    /// diffusion transformer from litert-community/Bonsai-Image-ternary-4B (dit_gpu_int4b32.tflite) and a VAE decoder
    /// exported by Emgu with GroupNorm written for GPU delegates (Emgu.LiteRT.Models/tools/export_flux2_vae_decoder.py),
    /// so it runs on the CPU and the GPU. Apache-2.0 (the notice is beside the file on Emgu's public S3). One 4.3 GB
    /// .litertlm, downloaded on Init. On an M1 (2026-11-01): 66 s a picture on the CPU (6.0 GB at the peak), 25 s on
    /// the GPU (Metal, 2.3 GB); the first load writes the caches (23-24 s).
    /// </summary>
    public class BonsaiImage4B512 : TextToImageModel
    {
        /// <summary>The model file's size in bytes.</summary>
        public const long FileSize = 4267950124;

        /// <summary>The model file, on Emgu's public S3.</summary>
        public static DownloadableFile DefaultModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://emgu-public.s3.amazonaws.com/bonsai/bonsai-flux2-4b-512.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "0bd495cf0a034ff8e06d692cca17036d4d8ab459cb7fa5d6b1d9552cf00fbdba");
            }
        }

        private readonly DownloadableFile _modelFile = DefaultModelFile;

        /// <inheritdoc/>
        public override String ModelName
        {
            get { return BonsaiImage4B.RegisteredName; }
        }

        /// <inheritdoc/>
        public override String DisplayName
        {
            get { return "Bonsai Image 4B 512"; }
        }

        /// <summary>"bonsai-flux2-512-cache": the 256 model, with the same registered name, keeps its own.</summary>
        public override String CacheFolderName
        {
            get { return ModelName + "-512-cache"; }
        }

        /// <inheritdoc/>
        public override DownloadableFile ModelFile
        {
            get { return _modelFile; }
        }

        /// <inheritdoc/>
        public override long DownloadSize
        {
            get { return FileSize; }
        }

        /// <inheritdoc/>
        public override int ImageSize
        {
            get { return 512; }
        }
    }

    /// <summary>
    /// Black Forest Labs' FLUX.2-klein-4B for LiteRT: 256 x 256 pictures, int8, distilled for 4 steps. Apache-2.0. One
    /// 4.7 GB .litertlm (the Qwen3-4B text encoder in three parts, the diffusion transformer in parts and the VAE
    /// decoder), downloaded on Init. On an M1 (2026-11-01): 23 s a picture on the CPU (5.6 GB at the peak), 17 s on the
    /// GPU (Metal, 2.2 GB); the first load writes about 3.5 GB of caches (26-27 s), later loads take 1-3 s.
    /// </summary>
    public class Flux2Klein4B : TextToImageModel
    {
        /// <summary>The model's name in LiteRT-LM's registry.</summary>
        public const String RegisteredName = "flux2-klein";

        /// <summary>The model file's size in bytes.</summary>
        public const long FileSize = 4653628848;

        /// <summary>The model file, at the Hugging Face revision checked here.</summary>
        public static DownloadableFile DefaultModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/FLUX.2-klein-4B-LiteRT/resolve/223f4c989d8e5275fa8e695ddebea8d7ac7bada6/flux2-klein-4b.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "c4fb4c7a44dc120b19e914fde3c7ab0b9b93c680fd85be2f50b590e62b576c55");
            }
        }

        private readonly DownloadableFile _modelFile = DefaultModelFile;

        /// <inheritdoc/>
        public override String ModelName
        {
            get { return RegisteredName; }
        }

        /// <inheritdoc/>
        public override String DisplayName
        {
            get { return "FLUX.2-klein 4B"; }
        }

        /// <inheritdoc/>
        public override DownloadableFile ModelFile
        {
            get { return _modelFile; }
        }

        /// <inheritdoc/>
        public override long DownloadSize
        {
            get { return FileSize; }
        }

        /// <inheritdoc/>
        public override int ImageSize
        {
            get { return 256; }
        }
    }
}
