//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Kokoro 82M for <see cref="ChatVoice"/>: downloaded in the background when it is chosen (a 295 MB .litertlm), loaded
    /// on the GPU where the app has LiteRT's GPU plugin (Android's OpenCL, Mac Catalyst's Metal: the file whose vocoder
    /// runs there) and on the CPU elsewhere (the file for the CPU), one sentence at a time, and freed after a while
    /// unused. Apache-2.0 (hexgrad's weights).
    /// </summary>
    public sealed class KokoroSpeech
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private Task<Kokoro82M> _load;
        private CancellationTokenSource _idle;

        /// <summary>The app's one Kokoro.</summary>
        public static KokoroSpeech Default { get; } = new KokoroSpeech();

        /// <summary>The model's name, shown.</summary>
        public const string Name = "Kokoro";

        /// <summary>The credit shown where it is chosen.</summary>
        public const string Credit = "Kokoro 82M by hexgrad, Apache-2.0";

        /// <summary>The model card, for the credit's link.</summary>
        public const string ModelCardUrl = "https://huggingface.co/hexgrad/Kokoro-82M";

        /// <summary>The download's size in megabytes, shown.</summary>
        public const int DownloadMegabytes = 295;

        /// <summary>How long it stays loaded after it last spoke.</summary>
        public static TimeSpan IdleRelease { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// True where the app has LiteRT's GPU plugin, so the GPU file is used (faster, half the memory); its vocoder is
        /// noisier on the CPU, so elsewhere the CPU file is.
        /// </summary>
        public static bool UsesGpu =>
#if ANDROID || MACCATALYST
            true;
#else
            false;
#endif

        /// <summary>The model file this platform uses.</summary>
        public DownloadableFile ModelFile { get; } = UsesGpu ? Kokoro82M.GpuModelFile : Kokoro82M.CpuModelFile;

        /// <summary>A model object for this platform's file (for its cache folder's name, the storage row).</summary>
        public static Kokoro82M CreateModel() => new Kokoro82M(UsesGpu);

        /// <summary>True if it can run here: libomniextern is in the app (not yet on Windows).</summary>
        public static bool IsAvailable => OmniInvoke.IsAvailable;

        /// <summary>True when the model is downloaded and verified (cheap: no hashing).</summary>
        public bool IsDownloaded => ModelFile.IsLocalFileVerified;

        /// <summary>Whether it is loaded or loading.</summary>
        public bool IsLoaded => _load != null;

        /// <summary>The download's percentage while it downloads, else null.</summary>
        public double? DownloadPercent { get; private set; }

        /// <summary>Why the GPU couldn't load it, when it fell back to the CPU; else null.</summary>
        public string GpuFailure { get; private set; }

        /// <summary>Raised (on any thread) when the download's progress or the loaded state changes.</summary>
        public event Action Changed;

        /// <summary>Download (in the background) and load it, unless it already is.</summary>
        /// <param name="stage">Told the download's progress and the loading, or null</param>
        /// <param name="cancel">Stops the download or the loading</param>
        public Task EnsureAsync(IProgress<string> stage, CancellationToken cancel) => LoadedAsync(stage, cancel);

        private Task<Kokoro82M> LoadedAsync(IProgress<string> stage, CancellationToken cancel)
        {
            lock (_lock)
            {
                if (_load == null || _load.IsFaulted || _load.IsCanceled)
                    _load = LoadAsync(stage, cancel);
                return _load;
            }
        }

        private async Task<Kokoro82M> LoadAsync(IProgress<string> stage, CancellationToken cancel)
        {
            if (!IsAvailable)
                throw new PlatformNotSupportedException("Kokoro isn't in this build of the app on this platform.");
            Kokoro82M model = CreateModel();
            try
            {
                if (!ModelFile.IsLocalFileVerified)
                {
                    stage?.Report(string.Format("Downloading the voice model ({0} MB)...", DownloadMegabytes));
                    await BackgroundDownloads.DownloadFileAsync(ModelFile, "Voice model", (total, received, percent) =>
                    {
                        if (percent == null) return;
                        DownloadPercent = percent;
                        stage?.Report(string.Format("Downloading the voice model: {0:F0}%", percent));
                        Changed?.Invoke();
                    }, cancel);
                }
                DownloadPercent = null;
                stage?.Report("Loading the voice model...");
                // Init verifies the file (hashing it once) and skips its own download.
                try
                {
                    await model.Init(UsesGpu ? "gpu" : "cpu", CpuThreadSettings.Count, cancellationToken: cancel);
                }
                catch (LiteRtLmException ex) when (UsesGpu)
                {
                    // No usable GPU: the same file on the CPU (its vocoder a little noisier there).
                    GpuFailure = ex.ErrorMessage ?? ex.Message;
                    await model.Init("cpu", CpuThreadSettings.Count, cancellationToken: cancel);
                }
            }
            catch (Exception)
            {
                DownloadPercent = null;
                model.Dispose();
                Changed?.Invoke();
                throw;
            }
            Changed?.Invoke();
            ScheduleRelease();
            return model;
        }

        /// <summary>The voices of a Kokoro language (BCP-47), loading the model first if needed.</summary>
        public async Task<string[]> GetVoicesAsync(string language, CancellationToken cancel)
        {
            Kokoro82M model = await LoadedAsync(null, cancel);
            ScheduleRelease();
            return model.GetVoices(language);
        }

        /// <summary>
        /// Speak a text in a Kokoro language and voice, loading the model first if needed (one at a time).
        /// </summary>
        /// <param name="text">What to say, already cleaned for speech</param>
        /// <param name="language">A Kokoro language (BCP-47, e.g. "en-US")</param>
        /// <param name="voice">A voice of that language, or null for its default</param>
        /// <param name="cancel">Stops waiting (the sentence being made is finished and thrown away)</param>
        public async Task<TextToSpeechAudio> SynthesizeAsync(string text, string language, string voice, CancellationToken cancel)
        {
            Kokoro82M model = await LoadedAsync(null, cancel);
            await _gate.WaitAsync(cancel);
            try
            {
                CancelRelease();
                // Freed meanwhile (Storage, idle): load it again.
                if (!model.Initialized)
                    model = await LoadedAsync(null, cancel);
                await model.SetVoiceAsync(language, voice);
                return await model.SynthesizeAsync(text, cancel);
            }
            finally
            {
                _gate.Release();
                ScheduleRelease();
            }
        }

        /// <summary>Free the model (loaded again when needed).</summary>
        public async Task ReleaseAsync()
        {
            await _gate.WaitAsync();
            try
            {
                Task<Kokoro82M> load;
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
                if (_load == null)
                {
                    _idle = null;
                    return;
                }
                idle = _idle = new CancellationTokenSource();
            }
            _ = Task.Delay(IdleRelease, idle.Token).ContinueWith(async t =>
            {
                if (t.IsCanceled) return;
                lock (_lock)
                    if (_idle != idle) return;
                await ReleaseAsync();
            }, TaskScheduler.Default);
        }
    }
}
