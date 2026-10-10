//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        /// <summary>
        /// The folder, under the application's local data folder, the models' files are downloaded to by default
        /// </summary>
        public const String DefaultLocalSubfolder = "LiteRT-LM";

        private readonly FileDownloadManager _downloadManager;
        private Engine _engine;
        // The chats that opened a conversation on the engine; their conversations must be released before the engine.
        private readonly List<WeakReference<Chat>> _chats = new List<WeakReference<Chat>>();

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
        /// True if LiteRT-LM can keep one Conversation open across the messages of a chat for this model, i.e. its chat
        /// template renders earlier turns the same way as more turns follow. Chat then keeps the conversation open
        /// instead of creating a new one, seeded with the history, for every message (see the remarks of Chat).
        /// </summary>
        public virtual bool SupportsMultiTurnConversation
        {
            get { return false; }
        }

        /// <summary>
        /// True if chat messages may include images (ChatAttachment.Image / ImageFile)
        /// </summary>
        public virtual bool SupportsImages
        {
            get { return false; }
        }

        /// <summary>
        /// True if chat messages may include audio (ChatAttachment.Audio / AudioFile)
        /// </summary>
        public virtual bool SupportsAudio
        {
            get { return false; }
        }

        /// <summary>
        /// True if the model can call tools (functions) in a LiteRT-LM conversation, i.e. LiteRT-LM parses its tool calls
        /// and its chat template takes the tool responses
        /// </summary>
        public virtual bool SupportsToolCalling
        {
            get { return false; }
        }

        /// <summary>
        /// Raised before the engine is disposed or replaced (by Dispose or another Init), so objects that created
        /// conversations on it can release them first. Chats are released automatically.
        /// </summary>
        public event EventHandler EngineReleasing;

        /// <summary>
        /// Create the engine settings in Init. The default uses the given backend for the main model only; models
        /// that accept images or audio override this to also set their vision / audio backends.
        /// </summary>
        /// <param name="modelPath">The local path of the model file</param>
        /// <param name="backend">The backend passed to Init</param>
        /// <returns>The engine settings</returns>
        protected virtual EngineSettings CreateEngineSettings(String modelPath, String backend)
        {
            return new EngineSettings(modelPath, backend);
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
                using (EngineSettings settings = CreateEngineSettings(modelPath, backend))
                {
                    // Keep compiled-model caches next to the model.
                    settings.CacheDir = Path.GetDirectoryName(modelPath);
                    if (configureSettings != null)
                        configureSettings(settings);
                    return new Engine(settings);
                }
            }).ConfigureAwait(false);

            CloseChatConversations();
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
        /// Remember a chat that created a conversation on the engine.
        /// </summary>
        internal void RegisterChat(Chat chat)
        {
            lock (_chats)
            {
                _chats.RemoveAll(c => { Chat target; return !c.TryGetTarget(out target) || target == chat; });
                _chats.Add(new WeakReference<Chat>(chat));
            }
        }

        // Release the chats' open conversations, which must not outlive the engine they were created on.
        private void CloseChatConversations()
        {
            EventHandler engineReleasing = EngineReleasing;
            if (engineReleasing != null && _engine != null)
                engineReleasing(this, EventArgs.Empty);
            lock (_chats)
            {
                foreach (WeakReference<Chat> reference in _chats)
                {
                    Chat chat;
                    if (reference.TryGetTarget(out chat))
                        chat.CloseConversation();
                }
                _chats.Clear();
            }
        }

        /// <summary>
        /// Release the engine
        /// </summary>
        protected override void ReleaseManagedResources()
        {
            CloseChatConversations();
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
