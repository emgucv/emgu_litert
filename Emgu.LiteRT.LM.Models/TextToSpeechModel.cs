//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// A text-to-speech model on LiteRT-LM's OmniEngine (<see cref="TextToSpeechEngine"/>): downloads its .litertlm,
    /// then speaks text in one of its voices, all on the device.
    /// </summary>
    public abstract class TextToSpeechModel : DisposableObject
    {
        private readonly FileDownloadManager _downloadManager;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private TextToSpeechEngine _engine;
        private String _backend;
        private int _numThreads;

        /// <summary>Create the model; call Init before speaking.</summary>
        protected TextToSpeechModel()
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

        /// <summary>The model's name in LiteRT-LM's model registry, e.g. "kokoro".</summary>
        public abstract String ModelName { get; }

        /// <summary>The name to show, e.g. "Kokoro".</summary>
        public abstract String DisplayName { get; }

        /// <summary>The model file.</summary>
        public abstract DownloadableFile ModelFile { get; }

        /// <summary>The model file's size in bytes, for asking before the download.</summary>
        public abstract long DownloadSize { get; }

        /// <summary>
        /// The folder of its compiled-model caches, next to the model file: "&lt;model name&gt;-cache" (the caches are
        /// named after the graphs, so each model has a folder of its own).
        /// </summary>
        public String CacheFolder
        {
            get { return Path.Combine(Path.GetDirectoryName(ModelFile.LocalFile), CacheFolderName); }
        }

        /// <summary>The name of <see cref="CacheFolder"/> in the models' folder.</summary>
        public virtual String CacheFolderName
        {
            get { return ModelName + "-cache"; }
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

        /// <summary>The language the engine speaks (as asked for; null for the model's default).</summary>
        public String Language { get; private set; }

        /// <summary>The voice the engine speaks in (as asked for; null for the language's default).</summary>
        public String Voice { get; private set; }

        /// <summary>The backend Init loaded the model on, or null.</summary>
        public String Backend
        {
            get { return _engine == null ? null : _backend; }
        }

        /// <summary>The engine, once initialized.</summary>
        public TextToSpeechEngine Engine
        {
            get { return _engine; }
        }

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
        /// Download the model file if needed, then load it in a language and voice.
        /// </summary>
        /// <param name="backend">"cpu" or "gpu"</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="language">BCP-47, or null for the voice's or the model's default</param>
        /// <param name="voice">A voice of that language, or null for the language's default</param>
        /// <param name="cancellationToken">Cancels the download</param>
        public async Task Init(String backend = "cpu", int numThreads = 0, String language = null, String voice = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await Download(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _backend = backend ?? "cpu";
            _numThreads = numThreads;
            await SetVoiceAsync(language, voice).ConfigureAwait(false);
        }

        /// <summary>
        /// Speak in another language or voice from now on (a new engine, well under a second once the caches are
        /// written; nothing happens if it is the same one).
        /// </summary>
        /// <param name="language">BCP-47, or null for the voice's or the model's default</param>
        /// <param name="voice">A voice of that language, or null for the language's default</param>
        public async Task SetVoiceAsync(String language, String voice)
        {
            if (_backend == null)
                throw new InvalidOperationException("The model is not initialized: call Init first");
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_engine != null && Language == language && Voice == voice)
                    return;
                String name = ModelName, path = ModelFile.LocalFile, backend = _backend, cache = CacheFolder;
                int threads = _numThreads;
                TextToSpeechEngine engine = await Task.Run(() =>
                {
                    Directory.CreateDirectory(cache);
                    return new TextToSpeechEngine(name, path, backend, threads, language, voice, cache);
                }).ConfigureAwait(false);
                if (_engine != null)
                    _engine.Dispose();
                _engine = engine;
                Language = language;
                Voice = voice;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>The model's voices: those of a language (BCP-47), or all of them for null.</summary>
        public String[] GetVoices(String language = null)
        {
            return CheckEngine().GetVoices(language);
        }

        /// <summary>The languages the model speaks, BCP-47.</summary>
        public String[] Languages
        {
            get { return CheckEngine().Languages; }
        }

        /// <summary>The model's default voice for a language (BCP-47).</summary>
        public String GetDefaultVoice(String language)
        {
            return CheckEngine().GetDefaultVoice(language);
        }

        /// <summary>
        /// Speak a text, one at a time, in the current language and voice.
        /// </summary>
        /// <param name="text">What to say</param>
        /// <param name="cancellationToken">Checked before it starts; a cancellation while it speaks returns at once,
        /// and the audio is thrown away when it is done</param>
        /// <returns>Mono samples in [-1, 1] at <see cref="TextToSpeechAudio.SampleRate"/></returns>
        public async Task<TextToSpeechAudio> SynthesizeAsync(String text, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (String.IsNullOrWhiteSpace(text))
                throw new ArgumentException("The text is empty.", "text");
            CheckEngine();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            Task<TextToSpeechAudio> speaking;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                TextToSpeechEngine engine = _engine;
                speaking = Task.Run(() =>
                {
                    int sampleRate;
                    float[] samples = engine.Synthesize(text, out sampleRate);
                    return new TextToSpeechAudio(samples, sampleRate);
                });
            }
            catch
            {
                _gate.Release();
                throw;
            }
            _ = speaking.ContinueWith(t => _gate.Release(), TaskScheduler.Default);
            if (!cancellationToken.CanBeCanceled)
                return await speaking.ConfigureAwait(false);
            TaskCompletionSource<bool> cancelled = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(speaking, cancelled.Task).ConfigureAwait(false) != speaking)
                {
                    _ = speaking.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            return await speaking.ConfigureAwait(false);
        }

        private TextToSpeechEngine CheckEngine()
        {
            TextToSpeechEngine engine = _engine;
            if (engine == null)
                throw new InvalidOperationException("The model is not initialized: call Init first");
            return engine;
        }

        /// <summary>Release the engine (after speech being made is done).</summary>
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
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    /// <summary>Speech a <see cref="TextToSpeechModel"/> made: mono samples in [-1, 1].</summary>
    public sealed class TextToSpeechAudio
    {
        /// <summary>Wrap samples.</summary>
        public TextToSpeechAudio(float[] samples, int sampleRate)
        {
            Samples = samples ?? throw new ArgumentNullException("samples");
            SampleRate = sampleRate;
        }

        /// <summary>The samples.</summary>
        public float[] Samples { get; }

        /// <summary>Their sample rate in Hz.</summary>
        public int SampleRate { get; }

        /// <summary>How long it lasts.</summary>
        public TimeSpan Duration
        {
            get { return TimeSpan.FromSeconds(SampleRate > 0 ? (double)Samples.Length / SampleRate : 0); }
        }
    }

    /// <summary>
    /// Kokoro 82M (hexgrad, Apache-2.0) for LiteRT-LM: 24 kHz speech in 9 languages and dialects - American and British
    /// English, Spanish, French, Italian, Brazilian Portuguese, Hindi, Mandarin Chinese, Japanese - with 54 voices and
    /// its own text front-ends (no espeak). One 295 MB .litertlm, downloaded on Init. Two files hold the same model:
    /// the GPU one (the vocoder on the GPU, about half the memory; on the CPU its audio is noisier) and the CPU one.
    /// On an M1 (2026-11-01): the GPU file on Metal speaks 12.8 s in 1.7 s (RTF 0.13, 0.33 GB), the CPU file on the CPU
    /// in 9.4 s (RTF 0.73, 0.94 GB).
    /// </summary>
    public class Kokoro82M : TextToSpeechModel
    {
        /// <summary>The model's name in LiteRT-LM's registry.</summary>
        public const String RegisteredName = "kokoro";

        private const String Revision = "33ad69fec6c4d9eb10a547aaf9a8c63e7c446a56";

        /// <summary>The file whose vocoder runs on the GPU (load it with "gpu"): faster and about half the memory.</summary>
        public static DownloadableFile GpuModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Kokoro-82M/resolve/" + Revision + "/kokoro_int8_gpu_fp16.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "1ef178b127ec48c737daf17eabb4cea5f6b3ffbc510aced270fe5c267bbeac52");
            }
        }

        /// <summary>The file for devices without a GPU: clean audio on the CPU.</summary>
        public static DownloadableFile CpuModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/Kokoro-82M/resolve/" + Revision + "/kokoro_int8_fp16.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "e7b931ee5d4b7104012a838c9eee240d19ea69cb3ca269041a3274f83dbb7fa1");
            }
        }

        private readonly DownloadableFile _modelFile;
        private readonly bool _gpuFile;

        /// <summary>Create the model.</summary>
        /// <param name="gpuFile">True for the GPU file (load it with "gpu"), false for the CPU one</param>
        public Kokoro82M(bool gpuFile = true)
        {
            _gpuFile = gpuFile;
            _modelFile = gpuFile ? GpuModelFile : CpuModelFile;
        }

        /// <inheritdoc/>
        public override String ModelName
        {
            get { return RegisteredName; }
        }

        /// <inheritdoc/>
        public override String DisplayName
        {
            get { return "Kokoro"; }
        }

        /// <inheritdoc/>
        public override DownloadableFile ModelFile
        {
            get { return _modelFile; }
        }

        /// <inheritdoc/>
        public override long DownloadSize
        {
            get { return _gpuFile ? 294750530L : 294783298L; }
        }

        /// <summary>"kokoro-gpu-cache" or "kokoro-cpu-cache": the two files' caches have the same names.</summary>
        public override String CacheFolderName
        {
            get { return ModelName + (_gpuFile ? "-gpu-cache" : "-cpu-cache"); }
        }

        private static readonly Dictionary<char, String> VoiceLanguages = new Dictionary<char, String>
        {
            { 'a', "en-US" }, { 'b', "en-GB" }, { 'e', "es" }, { 'f', "fr" }, { 'i', "it" },
            { 'p', "pt-BR" }, { 'h', "hi" }, { 'z', "zh-CN" }, { 'j', "ja" },
        };

        /// <summary>The language (BCP-47) of a Kokoro voice, from its name's first letter ("af_heart": "en-US"), or null.</summary>
        public static String VoiceLanguage(String voice)
        {
            String language;
            return !String.IsNullOrEmpty(voice) && VoiceLanguages.TryGetValue(voice[0], out language) ? language : null;
        }

        /// <summary>A voice's name to show: "af_heart" becomes "Heart (female)".</summary>
        public static String DescribeVoice(String voice)
        {
            if (String.IsNullOrEmpty(voice) || voice.Length < 4 || voice[2] != '_')
                return voice;
            String name = voice.Substring(3);
            name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
            String gender = voice[1] == 'f' ? "female" : voice[1] == 'm' ? "male" : null;
            return gender == null ? name : name + " (" + gender + ")";
        }
    }
}
