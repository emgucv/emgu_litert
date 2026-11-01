//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.LM.Models
{
    /// <summary>
    /// A speech recognition model on LiteRT-LM's speech-to-text engine (<see cref="SpeechToTextEngine"/>): downloads
    /// its files, then turns audio into text, all on the device.
    /// </summary>
    public abstract class SpeechToTextModel : DisposableObject
    {
        private readonly FileDownloadManager _downloadManager;
        private SpeechToTextEngine _engine;

        /// <summary>Create the model; call Init before transcribing.</summary>
        protected SpeechToTextModel()
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

        /// <summary>The model's name in LiteRT-LM's model registry, e.g. "parakeet-tdt-0.6b-v3".</summary>
        public abstract String ModelName { get; }

        /// <summary>The model file.</summary>
        public abstract DownloadableFile ModelFile { get; }

        /// <summary>A separate tokenizer file (tokenizer.json), or null when the model file has it (every .litertlm).</summary>
        public virtual DownloadableFile TokenizerFile
        {
            get { return null; }
        }

        /// <summary>
        /// How the overlapping windows' texts are joined, set before Init: "levenshtein" (aligns their words),
        /// "timestamp" (cuts at the words' times) or null for the model's default.
        /// </summary>
        public String TextMerger { get; set; }

        /// <summary>How much of each window the next one overlaps (0 to 1), set before Init.</summary>
        public float OverlapRatio { get; set; } = 0.4f;

        /// <summary>The engine, once initialized.</summary>
        public SpeechToTextEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>True once Init has loaded the model.</summary>
        public bool Initialized
        {
            get { return _engine != null; }
        }

        /// <summary>The model's files, all downloaded and verified.</summary>
        public bool IsDownloaded
        {
            get
            {
                foreach (DownloadableFile file in Files)
                    if (!file.IsLocalFileValid)
                        return false;
                return true;
            }
        }

        private IEnumerable<DownloadableFile> Files
        {
            get
            {
                yield return ModelFile;
                if (TokenizerFile != null)
                    yield return TokenizerFile;
            }
        }

        /// <summary>
        /// Download the model's files if needed, then load it.
        /// </summary>
        /// <param name="backend">"cpu" (the only one supported for now)</param>
        /// <param name="numThreads">The CPU threads, or 0 for the default (4)</param>
        /// <param name="cancellationToken">Cancels the download</param>
        public async Task Init(String backend = "cpu", int numThreads = 0, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!String.Equals(backend ?? "cpu", "cpu", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(
                    "Speech recognition runs on the CPU only for now: its decoding loop is slower on a mobile GPU, and this "
                    + "LiteRT build's prebuilt GPU accelerators have an ABI mismatch with libLiteRt.");
            List<DownloadableFile> files = new List<DownloadableFile>(Files);
            List<DownloadableFile> missing = await Task.Run(() => files.FindAll(f => !f.IsLocalFileValid), cancellationToken).ConfigureAwait(false);
            if (missing.Count > 0)
            {
                _downloadManager.Clear();
                foreach (DownloadableFile file in missing)
                    _downloadManager.AddFile(file);
                await Task.Run(() => _downloadManager.Download(1, cancellationToken), cancellationToken).ConfigureAwait(false);
                foreach (DownloadableFile file in missing)
                    if (!await Task.Run(() => file.IsLocalFileValid, cancellationToken).ConfigureAwait(false))
                        throw new IOException(String.Format("Failed to download {0}", file.Url));
            }
            cancellationToken.ThrowIfCancellationRequested();
            String modelPath = ModelFile.LocalFile;
            String tokenizerPath = TokenizerFile == null ? null : TokenizerFile.LocalFile;
            SpeechToTextEngine engine = await Task.Run(() => new SpeechToTextEngine(ModelName, modelPath, tokenizerPath, "cpu", numThreads, OverlapRatio, TextMerger)).ConfigureAwait(false);
            if (_engine != null)
                _engine.Dispose();
            _engine = engine;
        }

        /// <summary>
        /// Start recognizing a stream of audio (e.g. from the microphone): push 16 kHz mono samples to the session as
        /// they come, and process it for the text.
        /// </summary>
        public SpeechToTextSession CreateSession()
        {
            return CheckEngine().CreateSession();
        }

        /// <summary>
        /// The text spoken in some audio.
        /// </summary>
        /// <param name="samples">Mono samples, in [-1, 1]</param>
        /// <param name="sampleRate">Their sample rate in Hz; resampled to the model's (16 kHz) if different</param>
        /// <param name="partial">Told the text so far after each window (confirmed text and the guess after it), or null</param>
        /// <param name="cancellationToken">Stops between windows</param>
        /// <returns>The transcript</returns>
        public Task<String> TranscribeAsync(float[] samples, int sampleRate, IProgress<String> partial = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (samples == null)
                throw new ArgumentNullException("samples");
            SpeechToTextEngine engine = CheckEngine();
            return Task.Run(() =>
            {
                float[] audio = Resample(samples, sampleRate, engine.SampleRate);
                using (SpeechToTextSession session = engine.CreateSession())
                {
                    session.PushAudio(audio);
                    session.EndAudio();
                    return ReadAll(session, partial, cancellationToken);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// The text spoken in some 16-bit audio.
        /// </summary>
        /// <param name="samples">Mono 16-bit samples</param>
        /// <param name="sampleRate">Their sample rate in Hz</param>
        /// <param name="partial">Told the text so far after each window, or null</param>
        /// <param name="cancellationToken">Stops between windows</param>
        /// <returns>The transcript</returns>
        public Task<String> TranscribeAsync(short[] samples, int sampleRate, IProgress<String> partial = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (samples == null)
                throw new ArgumentNullException("samples");
            float[] audio = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
                audio[i] = samples[i] / 32768f;
            return TranscribeAsync(audio, sampleRate, partial, cancellationToken);
        }

        /// <summary>
        /// Process a session until it needs more audio or the audio ended, joining the confirmed text.
        /// </summary>
        /// <param name="session">The session</param>
        /// <param name="partial">Told the text so far after each window, or null</param>
        /// <param name="cancellationToken">Stops between windows</param>
        /// <returns>The confirmed text</returns>
        public static String ReadAll(SpeechToTextSession session, IProgress<String> partial = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (session == null)
                throw new ArgumentNullException("session");
            StringBuilder text = new StringBuilder();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                String confirmed, unconfirmed;
                SpeechToTextResult result = session.Process(out confirmed, out unconfirmed);
                Append(text, confirmed);
                if (result != SpeechToTextResult.Text)
                    break;
                if (partial != null)
                    partial.Report(Join(text.ToString(), unconfirmed));
            }
            return text.ToString();
        }

        /// <summary>
        /// Append a piece of confirmed text, with one space between pieces. The model capitalizes the first word of
        /// each window, so a piece that continues a sentence ("...morning, | Clearing by the afternoon") gets its first
        /// word lowercased - unless it looks like a name: "I", a word with other capitals, or one followed by another
        /// capitalized word ("City Library").
        /// </summary>
        /// <param name="text">The text so far</param>
        /// <param name="piece">The piece</param>
        public static void Append(StringBuilder text, String piece)
        {
            if (String.IsNullOrWhiteSpace(piece))
                return;
            piece = piece.Trim();
            int end = text.Length;
            while (end > 0 && Char.IsWhiteSpace(text[end - 1]))
                end--;
            if (end > 0 && ".?!:;\"”".IndexOf(text[end - 1]) < 0)
                piece = LowercaseFirstWord(piece);
            if (text.Length > 0 && !Char.IsWhiteSpace(text[text.Length - 1]))
                text.Append(' ');
            text.Append(piece);
        }

        private static String LowercaseFirstWord(String piece)
        {
            int wordEnd = 0;
            while (wordEnd < piece.Length && Char.IsLetter(piece[wordEnd]))
                wordEnd++;
            if (wordEnd < 2 || !Char.IsUpper(piece[0]))
                return piece;
            for (int i = 1; i < wordEnd; i++)
                if (!Char.IsLower(piece[i]))
                    return piece;
            // An apostrophe ("I'm") or another capitalized word next ("City Library") suggests a name: leave it.
            if (wordEnd < piece.Length && piece[wordEnd] == '\'')
                return piece;
            int next = wordEnd;
            while (next < piece.Length && Char.IsWhiteSpace(piece[next]))
                next++;
            if (next > wordEnd && next < piece.Length && Char.IsUpper(piece[next]))
                return piece;
            return Char.ToLowerInvariant(piece[0]) + piece.Substring(1);
        }

        private static String Join(String first, String second)
        {
            StringBuilder text = new StringBuilder(first);
            Append(text, second);
            return text.ToString();
        }

        /// <summary>
        /// Resample mono audio by linear interpolation (speech only needs up to 8 kHz, which 16 kHz keeps).
        /// </summary>
        /// <param name="samples">The samples</param>
        /// <param name="fromRate">Their sample rate</param>
        /// <param name="toRate">The sample rate wanted</param>
        /// <returns>The samples at toRate (the same array if the rates match)</returns>
        public static float[] Resample(float[] samples, int fromRate, int toRate)
        {
            if (samples == null)
                throw new ArgumentNullException("samples");
            if (fromRate <= 0 || toRate <= 0)
                throw new ArgumentOutOfRangeException("fromRate");
            if (fromRate == toRate || samples.Length == 0)
                return samples;
            int count = (int)((long)samples.Length * toRate / fromRate);
            float[] result = new float[Math.Max(1, count)];
            double step = (double)fromRate / toRate;
            for (int i = 0; i < result.Length; i++)
            {
                double position = i * step;
                int index = (int)position;
                if (index >= samples.Length - 1)
                {
                    result[i] = samples[samples.Length - 1];
                    continue;
                }
                double fraction = position - index;
                result[i] = (float)(samples[index] * (1 - fraction) + samples[index + 1] * fraction);
            }
            return result;
        }

        private SpeechToTextEngine CheckEngine()
        {
            if (_engine == null)
                throw new InvalidOperationException("The model is not initialized: call Init first");
            return _engine;
        }

        /// <summary>Release the engine.</summary>
        protected override void DisposeObject()
        {
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }
        }
    }

    /// <summary>
    /// NVIDIA's Parakeet-TDT-0.6B-v3 (int8, LiteRT): speech recognition in 25 European languages, detected
    /// automatically, with punctuation and capitalization; about 5% word error rate in English. The weights are
    /// CC-BY-4.0: apps using them must credit NVIDIA. One 615 MB .litertlm (the model and its tokenizer), downloaded on
    /// Init.
    /// </summary>
    public class ParakeetTdt06BV3 : SpeechToTextModel
    {
        /// <summary>
        /// The model's name in LiteRT-LM's registry; its compiled-model caches next to the file are named after it
        /// ("parakeet-tdt-0.6b-v3_..._.xnnpack_cache").
        /// </summary>
        public const String RegisteredName = "parakeet-tdt-0.6b-v3";

        /// <inheritdoc/>
        public override String ModelName
        {
            get { return RegisteredName; }
        }

        /// <summary>The model file (int8, stateful decoder, 5 s windows, with the tokenizer).</summary>
        public static DownloadableFile DefaultModelFile
        {
            get
            {
                return new DownloadableFile(
                    "https://huggingface.co/litert-community/parakeet-tdt-0.6b-v3/resolve/main/parakeet_tdt_0.6b_v3_5s_i8_stateful.litertlm",
                    LanguageModel.DefaultLocalSubfolder,
                    "66c5a74cf6c2c1d2b990901f6cece5bb53e3214dbceafa0989900a6f0eeb3504");
            }
        }

        /// <summary>
        /// The files LiteRT-LM before v0.19.0 loaded instead (the .tflite and NVIDIA's tokenizer.json), for an app to
        /// delete once the .litertlm is there.
        /// </summary>
        public static readonly String[] ObsoleteFileNames =
        {
            "parakeet_tdt_0.6b_v3_5s_i8_stateful.tflite",
            "parakeet-tdt-0.6b-v3_tokenizer.json",
        };

        /// <summary>
        /// Create the model, joining its windows' texts with the Levenshtein merger: on a 34 s English sample the
        /// timestamp merger (LiteRT-LM's default for Parakeet) repeated a phrase at one seam ("and look at the for a
        /// library card") at every overlap tried, while the Levenshtein one got every word right.
        /// </summary>
        public ParakeetTdt06BV3()
        {
            TextMerger = "levenshtein";
        }

        private readonly DownloadableFile _modelFile = DefaultModelFile;

        /// <inheritdoc/>
        public override DownloadableFile ModelFile
        {
            get { return _modelFile; }
        }
    }
}
