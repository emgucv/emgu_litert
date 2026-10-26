//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;
using LanguageModel = Emgu.LiteRT.LM.Models.LanguageModel;

namespace Emgu.LiteRT.LM.Maui
{
    // The models: choosing one (the list in a sheet, or the side panel), and loading it with its backend and settings.
    public partial class ChatPage
    {
        private ChatModelOption _model;
        private string _backend = "cpu";
        private LanguageModel _modelInstance;
        private VerticalStackLayout _sideModelList;

        /// <summary>The model chosen, or null before the page is built.</summary>
        public ChatModelOption SelectedModel => _model;

        /// <summary>The LiteRT-LM backend the model runs on: "cpu" or "gpu".</summary>
        public string SelectedBackend => _backend;

        // "Use the GPU" is kept per platform (the key the Emgu LiteRT demo has always used: "use_gpu_Android", "use_gpu_MacCatalyst"...).
        private static string GpuKey => "use_gpu_" + DeviceInfo.Current.Platform;

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
        /// Choose a model (loaded with the next message, or straight away if it is on the device): a new chat, as a
        /// chat belongs to a model. Nothing while a reply is being written.
        /// </summary>
        /// <param name="option">The model (one of the page's)</param>
        /// <param name="backend">"cpu" or "gpu" (one of the model's), or null for the one in use or the model's first</param>
        public void SelectModel(ChatModelOption option, string backend = null)
        {
            if (option == null)
                throw new ArgumentNullException(nameof(option));
            if (IsWorking && _chatView.Model != null)
            {
                SetStatus("Wait for the reply to finish before changing the model.");
                return;
            }
            backend = backend ?? _backend;
            if (!option.Backends.Contains(backend))
                backend = option.Backends.FirstOrDefault() ?? "cpu";
            if (_model == option && _backend == backend && _chatView.Model != null)
                return;
            _model = option;
            _backend = backend;
            // The client holds the model's conversation; the old model goes with it.
            _client.Reset();
            LanguageModel previous = _modelInstance;
            _modelInstance = option.Create();
            if (option.ContextWindow > 0)
                _modelInstance.MaxNumTokens = option.ContextWindow;
            // A new conversation for the model (OnConversationChanged), and its load card.
            _chatView.Model = _modelInstance;
            previous?.Dispose();
            if (_modelButton != null)
                _modelButton.Text = option.DisplayName + (option.Backends.Count > 1 ? " · " + backend.ToUpperInvariant() : string.Empty);
            _options.Settings.Set("model", option.Id);
            if (option.Backends.Count > 1)
                _options.Settings.Set(GpuKey, backend == "gpu");
            UpdateChips();
            RefreshModelList();
            foreach (ChatFeature feature in _features)
            {
                try { feature.OnModelChanged(option); }
                catch (Exception) { }
            }
        }

        // The load card's name, size and backend, and the engine's threads and context window.
        private void ConfigureLoadState(ModelLoadState state)
        {
            ChatModelOption option = _model;
            if (option == null)
                return;
            state.DisplayName = option.DisplayName;
            state.DownloadSize = option.DownloadSize;
            state.Backend = _backend;
            int threads = _options.CpuThreads > 0 ? _options.CpuThreads : CpuThreadSettings.Count;
            int context = option.ContextWindow;
            state.ConfigureSettings = settings =>
            {
                settings.NumThreads = threads;
                if (context > 0)
                    settings.MaxNumTokens = context;
            };
        }

        /// <summary>True once the model is loaded and ready.</summary>
        public bool ModelReady => _chatView?.LoadState?.IsReady == true;

        private async void OnModelButtonTapped()
        {
            if (IsWorking || _options.Models.Count < 2)
                return;
            string choice = await _sheet.ShowAsync("Model", BuildModelList(true), "Cancel");
            string[] parts = choice?.Split(':');
            if (parts?.Length == 2 && int.TryParse(parts[0], out int index) && index >= 0 && index < _options.Models.Count && !IsWorking)
                SelectModel(_options.Models[index], parts[1]);
        }

        // The models, one row per model and backend, with the one in use marked.
        private View BuildModelList(bool inSheet)
        {
            var list = new VerticalStackLayout { Spacing = 2 };
            for (int i = 0; i < _options.Models.Count; i++)
            {
                int index = i;
                ChatModelOption option = _options.Models[i];
                foreach (string backend in option.Backends.Count == 0 ? new[] { "cpu" } : option.Backends)
                {
                    string chosen = backend;
                    bool selected = option == _model && backend == _backend;
                    string title = option.DisplayName + (option.Backends.Count > 1 ? " · " + backend.ToUpperInvariant() : string.Empty);
                    list.Children.Add(ListRow(title, option.Detail, selected, () =>
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

        private void RefreshModelList()
        {
            if (_sideModelList == null || _wide != true)
                return;
            _sideModelList.Children.Clear();
            _sideModelList.Children.Add(BuildModelList(false));
        }
    }
}
