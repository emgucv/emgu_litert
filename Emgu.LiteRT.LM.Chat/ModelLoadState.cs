//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM;
using Emgu.LiteRT.Util;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;
using ModelLoadStage = Emgu.LiteRT.LM.Models.ModelLoadStage;

namespace Emgu.LiteRT.LM.Chat
{
    /// <summary>
    /// Loading a <see cref="LanguageModel"/> for a UI to show: whether its file is downloaded, the stage
    /// (checking, downloading with its progress, verifying, loading), the error if it failed, and Cancel / Retry. Its
    /// properties change on the synchronization context <see cref="LoadAsync"/> was called on (the model reports its
    /// progress on background threads).
    /// </summary>
    public sealed class ModelLoadState : ChatObservable
    {
        private readonly object _lock = new object();
        private ModelLoadStage? _stage;
        private string _error;
        private long _bytesReceived;
        private long? _totalBytes;
        private bool _isLoading;
        private CancellationTokenSource _cancel;
        private Task _loading;
        private SynchronizationContext _context;
        private bool _progressPosted;
        private string _displayName;
        private long _downloadSize;

        /// <summary>
        /// Prepare to load a model.
        /// </summary>
        /// <param name="model">The model</param>
        /// <param name="modelFile">Its file, or null for <see cref="LanguageModel.DefaultModelFile"/></param>
        public ModelLoadState(LanguageModel model, DownloadableFile modelFile = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            ModelFile = modelFile ?? model.DefaultModelFile;
            model.LoadStageChanged += (s, stage) => Post(() => Stage = stage);
            model.OnDownloadProgressChanged += OnProgress;
            if (model.Initialized)
                _stage = ModelLoadStage.Ready;
        }

        /// <summary>The model.</summary>
        public LanguageModel Model { get; }

        /// <summary>The model's file.</summary>
        public DownloadableFile ModelFile { get; }

        /// <summary>The LiteRT-LM backend to load the model on: "cpu" (the default) or "gpu".</summary>
        public string Backend { get; set; } = "cpu";

        /// <summary>Adjusts the engine settings before the engine is created (see <see cref="LanguageModel.Init"/>), or null.</summary>
        public Action<EngineSettings> ConfigureSettings { get; set; }

        /// <summary>A name for the model to show, e.g. "Gemma 4 E2B"; defaults to the model's class name.</summary>
        public string DisplayName
        {
            get => _displayName;
            set => Set(ref _displayName, value);
        }

        /// <summary>The size of the download, to show before it starts (the model doesn't know it before then); 0 if unknown.</summary>
        public long DownloadSize
        {
            get => _downloadSize;
            set => Set(ref _downloadSize, value);
        }

        /// <summary>
        /// True if the model's file is on the device and was verified before (a cheap check that doesn't read the file):
        /// loading then needs no download, so a UI may start it straight away.
        /// </summary>
        public bool IsDownloaded => ModelFile.IsLocalFileVerified;

        /// <summary>The stage loading has reached, or null before it started.</summary>
        public ModelLoadStage? Stage
        {
            get => _stage;
            private set
            {
                if (Set(ref _stage, value))
                {
                    Raise(nameof(IsReady));
                    Raise(nameof(IsDownloading));
                }
            }
        }

        /// <summary>True once the model is loaded.</summary>
        public bool IsReady => _stage == ModelLoadStage.Ready && Model.Initialized;

        /// <summary>True while the file downloads.</summary>
        public bool IsDownloading => _isLoading && _stage == ModelLoadStage.Downloading;

        /// <summary>True while loading (from checking the file to creating the engine).</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (Set(ref _isLoading, value))
                    Raise(nameof(IsDownloading));
            }
        }

        /// <summary>Why loading failed, or null.</summary>
        public string Error
        {
            get => _error;
            private set
            {
                if (Set(ref _error, value))
                    Raise(nameof(HasError));
            }
        }

        /// <summary>True if loading failed (see <see cref="Error"/>); <see cref="LoadAsync"/> tries again.</summary>
        public bool HasError => _error != null;

        /// <summary>The bytes downloaded so far.</summary>
        public long BytesReceived => _bytesReceived;

        /// <summary>The size of the download, once known.</summary>
        public long? TotalBytes => _totalBytes;

        /// <summary>The part downloaded, 0 to 1, or null if unknown.</summary>
        public double? Progress => _totalBytes > 0 ? Math.Min(1.0, _bytesReceived / (double)_totalBytes.Value) : (double?)null;

        /// <summary>
        /// Download the model if needed and load it. Calling it while it loads returns the same task; after a failure or a
        /// cancel it tries again. A failure sets <see cref="Error"/> (the task doesn't throw), a cancel resets the state.
        /// </summary>
        /// <returns>True if the model is ready</returns>
        public Task<bool> LoadAsync()
        {
            if (IsReady)
                return Task.FromResult(true);
            if (_loading is Task<bool> running && !running.IsCompleted)
                return running;
            _context = SynchronizationContext.Current;
            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            Task<bool> task = RunAsync(_cancel.Token);
            _loading = task;
            return task;
        }

        /// <summary>
        /// Stop loading (a partly downloaded file is deleted; creating the engine can't be interrupted, so a cancel then
        /// takes effect once it is done).
        /// </summary>
        public void Cancel()
        {
            _cancel?.Cancel();
        }

        private async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            Error = null;
            IsLoading = true;
            _bytesReceived = 0;
            _totalBytes = null;
            RaiseProgress();
            try
            {
                await Model.Init(ModelFile, Backend, ConfigureSettings, cancellationToken);
                Stage = ModelLoadStage.Ready;
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Stage = null;
                return false;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnProgress(long? totalBytesToReceive, long bytesReceived, double? progressPercentage)
        {
            lock (_lock)
            {
                _totalBytes = totalBytesToReceive;
                _bytesReceived = bytesReceived;
                // Many reports a second: one update queued at a time.
                if (_progressPosted)
                    return;
                _progressPosted = true;
            }
            Post(() =>
            {
                lock (_lock)
                    _progressPosted = false;
                RaiseProgress();
            });
        }

        private void RaiseProgress()
        {
            Raise(nameof(BytesReceived));
            Raise(nameof(TotalBytes));
            Raise(nameof(Progress));
        }

        private void Post(Action action)
        {
            SynchronizationContext context = _context;
            if (context == null || context == SynchronizationContext.Current)
                action();
            else
                context.Post(_ => action(), null);
        }
    }
}
