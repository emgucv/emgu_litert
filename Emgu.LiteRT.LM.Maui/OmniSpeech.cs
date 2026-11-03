//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Diagnostics;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A speech recognition model on LiteRT-LM's omni engine for <see cref="SpeechRecognizer"/>: NVIDIA's
    /// Parakeet-TDT-0.6B-v3 (<see cref="Parakeet"/>) or Alibaba Qwen's Qwen3-ASR-0.6B (<see cref="Qwen3Asr"/>). Downloaded
    /// in the background the first time it is needed, loaded on the CPU, one recording at a time, and freed after a while
    /// unused unless <see cref="KeepLoaded"/> (a meeting being recorded). One of them is loaded at a time: loading one frees
    /// the other. An app using them shows their <see cref="Credit"/> (Parakeet's CC-BY-4.0 licence asks for it).
    /// </summary>
    public sealed class OmniSpeech
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private readonly Func<SpeechToTextModel> _create;
        private readonly string[] _obsoleteFileNames;
        private Task<SpeechToTextModel> _load;
        private CancellationTokenSource _idle;
        private bool _keepLoaded, _useGpu;

        private OmniSpeech(SpeechEngine engine, string name, string credit, string modelCardUrl, int downloadMegabytes,
            DownloadableFile modelFile, Func<SpeechToTextModel> create, string[] obsoleteFileNames, bool canUseGpu)
        {
            CanUseGpu = canUseGpu;
            Engine = engine;
            Name = name;
            Credit = credit;
            ModelCardUrl = modelCardUrl;
            DownloadMegabytes = downloadMegabytes;
            ModelFile = modelFile;
            _create = create;
            _obsoleteFileNames = obsoleteFileNames ?? new string[0];
        }

        /// <summary>NVIDIA's Parakeet-TDT-0.6B-v3: 25 European languages, a 614 MB download.</summary>
        public static OmniSpeech Parakeet { get; } = new OmniSpeech(SpeechEngine.Parakeet, "Parakeet-TDT-0.6B-v3",
            "Parakeet-TDT-0.6B-v3 by NVIDIA, CC-BY-4.0", "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3", 614,
            ParakeetTdt06BV3.DefaultModelFile, () => new ParakeetTdt06BV3(), ParakeetTdt06BV3.ObsoleteFileNames, false);

        /// <summary>
        /// Alibaba Qwen's Qwen3-ASR-0.6B: 30 languages including Chinese, Japanese and Korean, a 960 MB download; slower
        /// and larger than Parakeet, with fewer errors.
        /// </summary>
        public static OmniSpeech Qwen3Asr { get; } = new OmniSpeech(SpeechEngine.Qwen3Asr, "Qwen3-ASR-0.6B",
            "Qwen3-ASR-0.6B by Alibaba Qwen, Apache-2.0", "https://huggingface.co/Qwen/Qwen3-ASR-0.6B", 960,
            Qwen3Asr06B.DefaultModelFile, () => new Qwen3Asr06B(), null, true);

        /// <summary>The models, in <see cref="SpeechEngines.Models"/>' order.</summary>
        public static IReadOnlyList<OmniSpeech> All { get; } = new[] { Parakeet, Qwen3Asr };

        /// <summary>The model for an engine, or null for the platform's recognizer.</summary>
        public static OmniSpeech For(SpeechEngine engine) => All.FirstOrDefault(m => m.Engine == engine);

        /// <summary>The engine it is.</summary>
        public SpeechEngine Engine { get; }

        /// <summary>The model's name, shown.</summary>
        public string Name { get; }

        /// <summary>The credit its licence asks for, shown where it is chosen.</summary>
        public string Credit { get; }

        /// <summary>The model card, for the credit's link.</summary>
        public string ModelCardUrl { get; }

        /// <summary>The download's size in megabytes, shown.</summary>
        public int DownloadMegabytes { get; }

        /// <summary>How long a model stays loaded after its last recording.</summary>
        public static TimeSpan IdleRelease { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Whether it may run on the GPU (<see cref="UseGpu"/>): Qwen3-ASR (on a Galaxy S10 about 1.6 times as fast, with ~1 GB
        /// more memory); Parakeet on a GPU isn't measured.
        /// </summary>
        public bool CanUseGpu { get; }

        /// <summary>True where the app has a GPU accelerator for the models: Android (OpenCL) and Mac Catalyst (Metal).</summary>
        public static bool GpuAvailable =>
#if ANDROID || MACCATALYST
            IsAvailable;
#else
            false;
#endif

        /// <summary>
        /// Load it on the GPU (where <see cref="CanUseGpu"/> and <see cref="GpuAvailable"/>; the CPU when the GPU fails).
        /// Changing it frees the model, so the next recording loads it on the other.
        /// </summary>
        public bool UseGpu
        {
            get => _useGpu;
            set
            {
                if (_useGpu == value) return;
                _useGpu = value;
                if (IsLoaded) _ = ReleaseAsync();
            }
        }

        /// <summary>Whether it is loaded on the GPU.</summary>
        public bool OnGpu { get; private set; }

        /// <summary>The model file (a .litertlm holding the tokenizer too).</summary>
        public DownloadableFile ModelFile { get; }

        /// <summary>True if they can run here: libomniextern is in the app (not yet on Windows).</summary>
        public static bool IsAvailable => OmniInvoke.IsAvailable;

        /// <summary>True when the model is downloaded and verified (cheap: no hashing).</summary>
        public bool IsDownloaded => ModelFile.IsLocalFileVerified;

        /// <summary>Whether it is loaded or loading.</summary>
        public bool IsLoaded => _load != null;

        /// <summary>The download's percentage while it downloads, else null.</summary>
        public double? DownloadPercent { get; private set; }

        /// <summary>Raised (on any thread) when the download's progress or the loaded state changes.</summary>
        public event Action Changed;

        /// <summary>Keeps it loaded while true (a meeting is being recorded); when it turns false it is freed after <see cref="IdleRelease"/>.</summary>
        public bool KeepLoaded
        {
            get => _keepLoaded;
            set
            {
                _keepLoaded = value;
                if (!value) ScheduleRelease();
            }
        }

        /// <summary>Download (in the background) and load it, unless it already is.</summary>
        /// <param name="stage">Told the download's progress and the loading, or null</param>
        /// <param name="cancel">Stops the download or the loading</param>
        public Task EnsureAsync(IProgress<string> stage, CancellationToken cancel) => LoadedAsync(stage, cancel);

        private Task<SpeechToTextModel> LoadedAsync(IProgress<string> stage, CancellationToken cancel)
        {
            lock (_lock)
            {
                if (_load == null || _load.IsFaulted || _load.IsCanceled)
                    _load = LoadAsync(stage, cancel);
                return _load;
            }
        }

        private async Task<SpeechToTextModel> LoadAsync(IProgress<string> stage, CancellationToken cancel)
        {
            if (!IsAvailable)
                throw new PlatformNotSupportedException(Name + " speech recognition isn't in this build of the app on this platform.");
            SpeechToTextModel model = _create();
            try
            {
                if (!ModelFile.IsLocalFileVerified)
                {
                    stage?.Report(string.Format("Downloading the speech model ({0} MB)...", DownloadMegabytes));
                    await BackgroundDownloads.DownloadFileAsync(ModelFile, "Speech model", (total, received, percent) =>
                    {
                        if (percent == null) return;
                        DownloadPercent = percent;
                        stage?.Report(string.Format("Downloading the speech model: {0:F0}%", percent));
                        Changed?.Invoke();
                    }, cancel);
                }
                DownloadPercent = null;
                // One model in memory at a time (Qwen3-ASR takes about 400 MB, Parakeet about 150 MB): the other lets go.
                foreach (OmniSpeech other in All)
                    if (other != this && !other.KeepLoaded)
                        await other.ReleaseAsync();
                stage?.Report("Loading the speech model...");
                // Init verifies the files (hashing them once) and skips its own download.
                bool gpu = UseGpu && CanUseGpu && GpuAvailable;
                try { await model.Init(gpu ? "gpu" : "cpu", CpuThreadSettings.Count, cancel); }
                catch (Exception ex) when (gpu && !(ex is OperationCanceledException))
                {
                    // No GPU here after all, or not enough memory for it: the CPU.
                    Debug.WriteLine(Name + " couldn't load on the GPU, so on the CPU: " + ex.Message);
                    model.Dispose();
                    model = _create();
                    gpu = false;
                    await model.Init("cpu", CpuThreadSettings.Count, cancel);
                }
                OnGpu = gpu;
                DeleteObsoleteFiles();
            }
            catch (Exception)
            {
                DownloadPercent = null;
                model.Dispose();
                Changed?.Invoke();
                throw;
            }
            Changed?.Invoke();
            // Loaded ahead of a recording (its download done): freed if none comes.
            ScheduleRelease();
            return model;
        }

        // Files an older LiteRT-LM loaded instead (Parakeet's .tflite and tokenizer.json before v0.19.0, 614 MB): not
        // needed once the .litertlm loads.
        private void DeleteObsoleteFiles()
        {
            string folder = System.IO.Path.GetDirectoryName(ModelFile.LocalFile);
            foreach (string name in _obsoleteFileNames)
                foreach (string path in new[] { name, name + ".sha256ok" })
                {
                    try { System.IO.File.Delete(System.IO.Path.Combine(folder, path)); }
                    catch (Exception) { /* Left for the Storage page. */ }
                }
        }

        /// <summary>The words in some audio, loading the model first if needed.</summary>
        /// <param name="samples">Mono samples in [-1, 1]</param>
        /// <param name="sampleRate">Their sample rate (resampled to 16 kHz if different)</param>
        /// <param name="partial">Told the text so far after each 5 s window, or null</param>
        /// <param name="stage">Told the download's progress and the loading, or null</param>
        /// <param name="cancel">Stops recognizing</param>
        public async Task<string> TranscribeAsync(float[] samples, int sampleRate, IProgress<string> partial, IProgress<string> stage, CancellationToken cancel)
        {
            SpeechToTextModel model = await LoadedAsync(stage, cancel);
            await _gate.WaitAsync(cancel);
            try
            {
                CancelRelease();
                // Freed meanwhile (Storage, idle, the other model loaded): load it again.
                if (!model.Initialized)
                    model = await LoadedAsync(stage, cancel);
                // A phone far from whoever speaks records them quietly: raised to a set level first.
                return (await model.TranscribeAsync(SpeechEngines.Boost(samples), sampleRate, partial, cancel)).Trim();
            }
            finally
            {
                _gate.Release();
                ScheduleRelease();
            }
        }

        /// <summary>Free the model (loaded again when needed), after the recording being written down.</summary>
        public async Task ReleaseAsync()
        {
            await _gate.WaitAsync();
            try
            {
                Task<SpeechToTextModel> load;
                lock (_lock)
                {
                    load = _load;
                    _load = null;
                }
                if (load == null) return;
                try { (await load).Dispose(); }
                catch (Exception) { /* It failed to load: nothing to free. */ }
                OnGpu = false;
            }
            finally
            {
                _gate.Release();
                Changed?.Invoke();
            }
        }

        private void CancelRelease()
        {
            lock (_lock)
            {
                _idle?.Cancel();
                _idle = null;
            }
        }

        private void ScheduleRelease()
        {
            CancellationTokenSource idle;
            lock (_lock)
            {
                _idle?.Cancel();
                if (_keepLoaded || _load == null)
                {
                    _idle = null;
                    return;
                }
                idle = _idle = new CancellationTokenSource();
            }
            _ = Task.Delay(IdleRelease, idle.Token).ContinueWith(async t =>
            {
                if (t.IsCanceled || _keepLoaded) return;
                lock (_lock)
                    if (_idle != idle) return;
                await ReleaseAsync();
            }, TaskScheduler.Default);
        }
    }
}
