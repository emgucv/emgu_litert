//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// The base class of the ready-to-use language models: downloads the model's .litertlm file on first use and
    /// creates the LiteRT-LM Engine.
    /// </summary>
    public abstract class LanguageModel : DisposableObject
    {
        private readonly FileDownloadManager _downloadManager;
        private Engine _engine;

        /// <summary>
        /// Create the model. Call Init to download and load it.
        /// </summary>
        protected LanguageModel()
        {
            _downloadManager = new FileDownloadManager();
            _downloadManager.OnDownloadProgressChanged += onDownloadProgressChanged;
        }

        private void onDownloadProgressChanged(long? totalBytesToReceive, long bytesReceived, double? progressPercentage)
        {
            if (OnDownloadProgressChanged != null)
                OnDownloadProgressChanged(totalBytesToReceive, bytesReceived, progressPercentage);
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
        /// The local path of the model file, or null before Init
        /// </summary>
        public String ModelPath { get; private set; }

        /// <summary>
        /// The LiteRT-LM engine, or null before Init. Use it directly for the lower level Emgu.LiteRT.LM APIs, e.g.
        /// Engine.CreateSession.
        /// </summary>
        public Engine Engine
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
        /// Download the model file if it isn't already, and load it.
        /// </summary>
        /// <param name="modelFile">The model file, or null for DefaultModelFile</param>
        /// <param name="backend">The LiteRT-LM backend, e.g. "cpu" or "gpu"</param>
        /// <param name="configureSettings">Called to adjust the engine settings before the engine is created, or
        /// null</param>
        /// <returns>A task that completes when the model is loaded</returns>
        public async Task Init(
            DownloadableFile modelFile = null,
            String backend = "cpu",
            Action<EngineSettings> configureSettings = null)
        {
            if (modelFile == null)
                modelFile = DefaultModelFile;

            _downloadManager.Clear();
            _downloadManager.AddFile(modelFile);
            await _downloadManager.Download().ConfigureAwait(false);
            if (!_downloadManager.AllFilesDownloaded)
                throw new Exception(String.Format("Failed to download {0}", modelFile.Url));

            String modelPath = modelFile.LocalFile;
            // Loading a model can take a while; don't block the caller's thread.
            Engine engine = await Task.Run(() =>
            {
                using (EngineSettings settings = new EngineSettings(modelPath, backend))
                {
                    // Keep compiled-model caches next to the model.
                    settings.CacheDir = Path.GetDirectoryName(modelPath);
                    if (configureSettings != null)
                        configureSettings(settings);
                    return new Engine(settings);
                }
            }).ConfigureAwait(false);

            if (_engine != null)
                _engine.Dispose();
            _engine = engine;
            ModelPath = modelPath;
        }

        /// <summary>
        /// Start a chat with the model.
        /// </summary>
        /// <param name="systemMessage">The system message (instructions for the model), or null for none</param>
        /// <returns>The chat</returns>
        public virtual Chat CreateChat(String systemMessage = null)
        {
            return new Chat(this, systemMessage);
        }

        /// <summary>
        /// Release the engine
        /// </summary>
        protected override void ReleaseManagedResources()
        {
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }
        }

        /// <summary>
        /// Nothing to release: the engine is a managed object, released by ReleaseManagedResources (an undisposed
        /// engine releases itself when finalized).
        /// </summary>
        protected override void DisposeObject()
        {
        }
    }
}
