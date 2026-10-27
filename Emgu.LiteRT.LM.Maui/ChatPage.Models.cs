//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.Util;
using Microsoft.Extensions.AI;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;
using ModelLoadStage = Emgu.LiteRT.LM.Models.ModelLoadStage;

namespace Emgu.LiteRT.LM.Maui
{
    // The models: choosing one (the list in a sheet, or the side panel), loading it with its backend and settings, and
    // reloading it - for another backend, context window or thread count - with the chat going on.
    public partial class ChatPage
    {
        private ChatModelOption _model;
        private string _backend = "cpu";
        private LanguageModel _modelInstance;
        private int _contextTokens;
        private bool _gpuUnavailable;
        private bool _remoteReady;
        private int _reloadDepth;
        private VerticalStackLayout _sideModelList;

        /// <summary>The model chosen, or null before the page is built.</summary>
        public ChatModelOption SelectedModel => _model;

        /// <summary>The LiteRT-LM backend the model runs on: "cpu" or "gpu".</summary>
        public string SelectedBackend => _backend;

        /// <summary>The model's instance on the device (loaded or loading), or null for a remote model.</summary>
        protected LanguageModel ModelInstance => _modelInstance;

        /// <summary>True while a model is being reloaded (CPU threads, the context window, a private chat...): no new work starts meanwhile.</summary>
        public bool IsReloadingModel => _reloadDepth > 0;

        /// <summary>True once the model is loaded and ready (a remote model: connected).</summary>
        public bool ModelReady => _model?.IsRemote == true ? _remoteReady : _chatView?.LoadState?.IsReady == true;

        // "Use the GPU" is kept per platform (the key the Emgu LiteRT demo has always used: "use_gpu_Android", "use_gpu_MacCatalyst"...).
        private static string GpuKey => "use_gpu_" + DeviceInfo.Current.Platform;

        private static bool OnPhone => DeviceInfo.Current.Platform == DevicePlatform.Android || DeviceInfo.Current.Platform == DevicePlatform.iOS;

        private void SelectInitialModel()
        {
            string saved = _options.Settings.Get<string>("model", null);
            ChatModelOption option = _options.Models.FirstOrDefault(m => m.Id == saved) ?? _options.Models[0];
            // The GPU by default where it was verified (not on Windows), as the demo.
            bool gpu = _options.Settings.Get(GpuKey, DeviceInfo.Current.Platform != DevicePlatform.WinUI);
            string backend = option.Backends.Count > 1 && gpu ? "gpu" : option.Backends.FirstOrDefault() ?? "cpu";
            SelectModel(option, backend);
        }

        /// <summary>
        /// Mark the page as reloading a model until the result is disposed (<see cref="IsReloadingModel"/>): a derived
        /// page's own reloads (another search model, a private chat's cleanup) hold it too.
        /// </summary>
        protected IDisposable BeginReload()
        {
            _reloadDepth++;
            return new ReloadScope(this);
        }

        private Task _reloadTask;

        /// <summary>The reload work going on (<see cref="RunReloadAsync"/>), to wait for before closing; a completed task when there is none.</summary>
        protected Task PendingReload => _reloadTask ?? Task.CompletedTask;

        /// <summary>
        /// Run work under <see cref="BeginReload"/> (<see cref="IsReloadingModel"/> while it runs) and remember it as
        /// <see cref="PendingReload"/>: a feature's own reload (another search model, a storage deletion).
        /// </summary>
        /// <param name="work">The work</param>
        protected async Task RunReloadAsync(Func<Task> work)
        {
            if (work == null)
                throw new ArgumentNullException(nameof(work));
            using (BeginReload())
            {
                Task task = work();
                _reloadTask = task;
                try { await task; }
                finally { if (_reloadTask == task) _reloadTask = null; }
            }
        }

        private sealed class ReloadScope : IDisposable
        {
            private ChatPage _page;
            public ReloadScope(ChatPage page) => _page = page;
            public void Dispose()
            {
                if (_page == null) return;
                _page._reloadDepth = Math.Max(0, _page._reloadDepth - 1);
                _page = null;
            }
        }

        /// <summary>
        /// Choose a model (loaded with the next message, or straight away if it is on the device): a new chat, as a
        /// chat belongs to a model. The same model on another backend is reloaded instead, and the chat goes on
        /// (<see cref="ReloadModelAsync"/>). Nothing while a reply is being written.
        /// </summary>
        /// <param name="option">The model (one of the page's)</param>
        /// <param name="backend">"cpu" or "gpu" (one of the model's), or null for the one in use or the model's first</param>
        public void SelectModel(ChatModelOption option, string backend = null)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));
            // Not while a reply is being written (the page's own busy work - opening a chat - changes the model itself).
            if (Conversation?.IsBusy == true || IsReloadingModel)
            {
                SetStatus("Wait for the reply to finish before changing the model.");
                return;
            }
            backend = Backend(option, backend ?? _backend);
            bool chosen = _model == option && (_chatView.Model != null || option.IsRemote);
            if (chosen && _backend == backend)
                return;
            if (chosen)
            {
                _ = ReloadModelAsync(backend);
                return;
            }
            _model = option;
            _backend = backend;
            _gpuUnavailable = false;
            _remoteReady = false;
            // The client holds the model's conversation; the old model goes with it.
            _client.Reset();
            LanguageModel previous = _modelInstance;
            _modelInstance = option.IsRemote ? null : NewInstance(option);
            // A new conversation for the model (OnConversationChanged) and its load card; a remote model's conversation
            // runs over the client alone.
            _chatView.Model = _modelInstance;
            ApplyModelCapabilities();
            if (previous != null)
                _ = Task.Run(() => previous.Dispose());
            UpdateModelButton();
            _options.Settings.Set("model", option.Id);
            if (option.Backends.Count > 1)
                _options.Settings.Set(GpuKey, backend == "gpu");
            UpdateChips();
            RefreshModelList();
            UpdateContextSection();
            foreach (ChatFeature feature in _features)
            {
                try { feature.OnModelChanged(option); }
                catch (Exception) { }
            }
        }

        private static string Backend(ChatModelOption option, string backend) =>
            option.Backends.Contains(backend) ? backend : option.Backends.FirstOrDefault() ?? "cpu";

        // A new, unloaded instance of the model with the context window chosen for it.
        private LanguageModel NewInstance(ChatModelOption option)
        {
            LanguageModel model = option.Create();
            _contextTokens = option.MaxContextWindow > 0
                ? ChatContextWindow.Choose(ContextSetting, option.ContextWindow, option.MaxContextWindow, OnPhone, option.CanUseTools)
                : option.ContextWindow;
            if (_contextTokens > 0)
                model.MaxNumTokens = _contextTokens;
            return model;
        }

        /// <summary>
        /// Load the chosen model again - on another backend, with the context window or CPU threads chosen now - with
        /// the chat going on: the model reads the conversation with the next message. A reply being written finishes
        /// first. Nothing for a remote model.
        /// </summary>
        /// <param name="backend">"cpu" or "gpu", or null for the backend in use</param>
        /// <returns>True once the model is ready</returns>
        public async Task<bool> ReloadModelAsync(string backend = null)
        {
            ChatModelOption option = _model;
            if (option == null || option.IsRemote)
                return false;
            using (BeginReload())
            {
                await _chatView.PendingSend;
                _backend = Backend(option, backend ?? _backend);
                if (option.Backends.Count > 1)
                    _options.Settings.Set(GpuKey, _backend == "gpu");
                _client.Reset();
                LanguageModel previous = _modelInstance;
                _modelInstance = NewInstance(option);
                // The same conversation, on the new engine (LoadStateCreated sets the card up); it is given the chat
                // again with the next message.
                _chatView.ReplaceModel(_modelInstance);
                ConfigureConversation(Conversation, option, _modelInstance);
                RereadNext = Conversation?.Messages.Any(m => m.Role != ChatMessageRole.Notice) == true;
                UpdateModelButton();
                RefreshModelList();
                // Freeing an engine can take a moment: not on the UI thread.
                if (previous != null)
                    await Task.Run(() => previous.Dispose());
                ModelLoadState state = _chatView.LoadState;
                return state == null || await state.LoadAsync();
            }
        }

        private void UpdateModelButton()
        {
            if (_modelButton != null && _model != null)
                _modelButton.Text = _model.DisplayName + (_model.Backends.Count > 1 ? " · " + _backend.ToUpperInvariant() : string.Empty);
        }

        // The load card's name, size and backend, the download, and the engine's threads and context window.
        private void ConfigureLoadState(ModelLoadState state)
        {
            ChatModelOption option = _model;
            if (option == null)
                return;
            state.DisplayName = option.DisplayName;
            state.DownloadSize = option.DownloadSize;
            state.Backend = _backend;
            // The download goes on when the app leaves the screen, as far as the platform allows, and a paused one continues.
            state.Downloader = BackgroundDownloads.DownloadFileAsync;
            state.Hint = BackgroundDownloads.Hint;
            int threads = _options.CpuThreads > 0 ? _options.CpuThreads : CpuThreadSettings.Count;
            int context = _contextTokens;
            Action<EngineSettings> configure = _options.ConfigureEngine;
            state.ConfigureSettings = settings =>
            {
                settings.NumThreads = threads;
                if (context > 0)
                    settings.MaxNumTokens = context;
                configure?.Invoke(settings);
            };
            state.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ModelLoadState.IsReady) && state.IsReady)
                {
                    RefreshModelList();
                    UpdateMemoryLabel();
                }
            };
        }

        // Loading on the GPU failed while creating the engine: offer the CPU (the chat goes on). Other failures show on the card.
        private async void OnModelLoadFailed(object sender, EventArgs e)
        {
            ModelLoadState state = _chatView.LoadState;
            if (state == null || _backend != "gpu" || _gpuUnavailable || state.FailedStage != ModelLoadStage.LoadingEngine)
                return;
            bool useCpu = await DisplayAlertAsync("GPU model could not load", state.Error + "\n\nRun this model on CPU instead?", "Run on CPU", "Cancel");
            if (!useCpu)
                return;
            _gpuUnavailable = true;
            await ReloadModelAsync("cpu");
            SetStatus("The GPU isn't available on this device - using the CPU.");
        }

        private async void OnModelButtonTapped()
        {
            if (IsWorking || _options.Models.Count < 2)
                return;
            string choice = await _sheet.ShowAsync("Model", BuildModelList(true), "Cancel");
            string[] parts = choice?.Split(':');
            if (parts?.Length == 2 && int.TryParse(parts[0], out int index) && index >= 0 && index < _options.Models.Count && !IsWorking)
                SelectModel(_options.Models[index], parts[1]);
        }

        // The models, one row per model and backend, with the one in use marked and whether each is on the device.
        private View BuildModelList(bool inSheet)
        {
            var list = new VerticalStackLayout { Spacing = 2 };
            for (int i = 0; i < _options.Models.Count; i++)
            {
                int index = i;
                ChatModelOption option = _options.Models[i];
                string detail = string.Join(" ", new[] { DownloadStatus(option), option.Detail }.Where(s => !string.IsNullOrEmpty(s)));
                foreach (string backend in option.Backends.Count == 0 ? new[] { "cpu" } : option.Backends)
                {
                    string chosen = backend;
                    bool selected = option == _model && backend == _backend;
                    string title = option.DisplayName + (option.Backends.Count > 1 ? " · " + backend.ToUpperInvariant() : string.Empty);
                    list.Children.Add(ListRow(title, detail, selected, () =>
                    {
                        if (inSheet)
                            _sheet.Dismiss(index + ":" + chosen);
                        else if (!IsWorking)
                            SelectModel(option, chosen);
                    }, null, null));
                }
            }
            return list;
        }

        // "Downloaded ·", "Download paused at 1.2 GB of 2.6 GB ·", or nothing, from the option's file.
        private string DownloadStatus(ChatModelOption option)
        {
            DownloadableFile file = option.ModelFile;
            if (file == null)
                return null;
            try
            {
                if (file.IsLocalFileVerified)
                    return "Downloaded ·";
                if (file.PartialDownloadBytes > 0)
                    return "Download paused at " + FormatSize(file.PartialDownloadBytes) + (option.DownloadSize > 0 ? " of " + FormatSize(option.DownloadSize) : string.Empty) + " ·";
            }
            catch (Exception)
            {
            }
            return null;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30)
                return string.Format("{0:0.0} GB", bytes / (double)(1L << 30));
            return string.Format("{0:0} MB", bytes / (double)(1L << 20));
        }

        /// <summary>
        /// Apply what the chosen model takes: for a remote model, the attachment kinds from the option (a model on the
        /// device says itself), dropping the pending attachments it doesn't take, and the conversation's context
        /// window from the option. Called when the model is chosen and when a remote model's abilities are learned
        /// (<see cref="IChatPageHost.ModelCapabilitiesChanged"/>).
        /// </summary>
        protected void ApplyModelCapabilities()
        {
            ChatModelOption option = _model;
            if (option == null || _chatView == null)
                return;
            if (!option.IsRemote)
            {
                _chatView.AttachmentKinds = null;
                return;
            }
            ChatAttachmentKinds kinds = (option.AcceptsImages ? ChatAttachmentKinds.Image : ChatAttachmentKinds.None)
                | (option.AcceptsAudio ? ChatAttachmentKinds.Audio : ChatAttachmentKinds.None);
            _chatView.AttachmentKinds = kinds;
            foreach (DataContent attachment in _chatView.PendingAttachments.ToList())
            {
                bool image = attachment.MediaType != null && attachment.MediaType.StartsWith("image/", StringComparison.Ordinal);
                bool audio = attachment.MediaType != null && attachment.MediaType.StartsWith("audio/", StringComparison.Ordinal);
                if ((image && !kinds.HasFlag(ChatAttachmentKinds.Image)) || (audio && !kinds.HasFlag(ChatAttachmentKinds.Audio)))
                    _chatView.PendingAttachments.Remove(attachment);
            }
            if (Conversation != null && option.ContextWindow > 0)
                Conversation.ContextWindow = option.ContextWindow;
        }

        /// <summary>List the models again (a row's detail changed, e.g. a server's address).</summary>
        protected void RefreshModelList()
        {
            if (_sideModelList == null || _wide != true)
                return;
            _sideModelList.Children.Clear();
            _sideModelList.Children.Add(BuildModelList(false));
        }

        /// <summary>
        /// Free the model's engine without loading it again (its file is being deleted): the card offers the download or
        /// the load again, and the chat goes on when it is loaded. Nothing for a remote model.
        /// </summary>
        protected async Task UnloadModelAsync()
        {
            ChatModelOption option = _model;
            if (option == null || option.IsRemote)
                return;
            using (BeginReload())
            {
                await _chatView.PendingSend;
                _chatView.LoadState?.Cancel();
                _client.Reset();
                LanguageModel previous = _modelInstance;
                _modelInstance = NewInstance(option);
                _chatView.ReplaceModel(_modelInstance);
                ConfigureConversation(Conversation, option, _modelInstance);
                RereadNext = Conversation?.Messages.Any(m => m.Role != ChatMessageRole.Notice) == true;
                RefreshModelList();
                if (previous != null)
                    await Task.Run(() => previous.Dispose());
            }
        }

        /// <summary>
        /// Free the model's engine for good (the page is closing): a load in progress is cancelled and waited for, a
        /// reply being written finishes first.
        /// </summary>
        protected async Task ShutdownModelAsync()
        {
            using (BeginReload())
            {
                await _chatView.PendingSend;
                ModelLoadState state = _chatView.LoadState;
                if (state != null && state.IsLoading)
                {
                    // Creating an engine can't be interrupted: the cancel takes effect once it is done.
                    state.Cancel();
                    try { await state.LoadAsync(); } catch (Exception) { }
                }
                _client.Reset();
                LanguageModel previous = _modelInstance;
                _modelInstance = null;
                if (previous != null)
                    await Task.Run(() => previous.Dispose());
            }
        }
    }
}
