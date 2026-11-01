//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// NVIDIA's Parakeet-TDT-0.6B-v3 for <see cref="SpeechRecognizer"/>: downloaded in the background the first time it is
    /// needed (a 614 MB .litertlm), loaded on the CPU, one recording at a time, and freed after a while unused
    /// unless <see cref="KeepLoaded"/> (a meeting being recorded). The weights are CC-BY-4.0: an app using them credits
    /// NVIDIA (<see cref="Credit"/>).
    /// </summary>
    public sealed class ParakeetSpeech
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private Task<ParakeetTdt06BV3> _load;
        private CancellationTokenSource _idle;
        private bool _keepLoaded;

        /// <summary>The app's one Parakeet.</summary>
        public static ParakeetSpeech Default { get; } = new ParakeetSpeech();

        /// <summary>The model's name, shown.</summary>
        public const string Name = "Parakeet-TDT-0.6B-v3";

        /// <summary>The credit the model's licence asks for, shown where it is chosen.</summary>
        public const string Credit = "Parakeet-TDT-0.6B-v3 by NVIDIA, CC-BY-4.0";

        /// <summary>The model card, for the credit's link.</summary>
        public const string ModelCardUrl = "https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3";

        /// <summary>The download's size in megabytes, shown.</summary>
        public const int DownloadMegabytes = 614;

        /// <summary>How long it stays loaded after its last recording.</summary>
        public static TimeSpan IdleRelease { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>The model file (a .litertlm holding the tokenizer too).</summary>
        public DownloadableFile ModelFile { get; } = ParakeetTdt06BV3.DefaultModelFile;

        /// <summary>True if it can run here: libomniextern is in the app (not yet on Windows).</summary>
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

        private Task<ParakeetTdt06BV3> LoadedAsync(IProgress<string> stage, CancellationToken cancel)
        {
            lock (_lock)
            {
                if (_load == null || _load.IsFaulted || _load.IsCanceled)
                    _load = LoadAsync(stage, cancel);
                return _load;
            }
        }

        private async Task<ParakeetTdt06BV3> LoadAsync(IProgress<string> stage, CancellationToken cancel)
        {
            if (!IsAvailable)
                throw new PlatformNotSupportedException("Parakeet speech recognition isn't in this build of the app on this platform.");
            var model = new ParakeetTdt06BV3();
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
                stage?.Report("Loading the speech model...");
                // Init verifies the files (hashing them once) and skips its own download.
                await model.Init("cpu", CpuThreadSettings.Count, cancel);
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

        // The .tflite and tokenizer.json LiteRT-LM before v0.19.0 loaded (614 MB): not needed once the .litertlm loads.
        private void DeleteObsoleteFiles()
        {
            string folder = System.IO.Path.GetDirectoryName(ModelFile.LocalFile);
            foreach (string name in ParakeetTdt06BV3.ObsoleteFileNames)
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
            ParakeetTdt06BV3 model = await LoadedAsync(stage, cancel);
            await _gate.WaitAsync(cancel);
            try
            {
                CancelRelease();
                // Freed meanwhile (Storage, idle): load it again.
                if (!model.Initialized)
                    model = await LoadedAsync(stage, cancel);
                return (await model.TranscribeAsync(samples, sampleRate, partial, cancel)).Trim();
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
                Task<ParakeetTdt06BV3> load;
                lock (_lock)
                {
                    load = _load;
                    _load = null;
                }
                if (load == null) return;
                try { (await load).Dispose(); }
                catch (Exception) { /* It failed to load: nothing to free. */ }
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
