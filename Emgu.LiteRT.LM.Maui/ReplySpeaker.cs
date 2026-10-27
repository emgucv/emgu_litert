//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.ComponentModel;
using System.Globalization;
using Emgu.LiteRT.LM.Agents.Audio;
using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Reads replies aloud for a <see cref="ChatView"/> with <see cref="ChatView.ReadAloud"/> on.
    /// </summary>
    public interface IReplySpeaker
    {
        /// <summary>
        /// Start reading a reply; it may still be streaming (read it as it is written). Stops reading an earlier one.
        /// </summary>
        /// <param name="reply">The reply</param>
        void Start(ChatMessageItem reply);

        /// <summary>
        /// Stop reading.
        /// </summary>
        void Stop();

        /// <summary>Raised with each piece of text as it is about to be spoken (a voice chat shows it, and pauses listening meanwhile).</summary>
        event Action<string> Speaking;

        /// <summary>Ends when the reply being read has been read out, or reading was stopped; completed when nothing is being read.</summary>
        Task Completion { get; }
    }

    /// <summary>
    /// The default <see cref="IReplySpeaker"/>: MAUI's TextToSpeech reads a reply sentence by sentence while it is written
    /// (<see cref="SpeechSegmenter"/>: the answer only - no thinking, code or tables), its Markdown, links and emoji removed
    /// (<see cref="ReplySpeechText.CleanForSpeech"/>), with a voice for its language (<see cref="DetectLanguage"/>, by default
    /// <see cref="TextLanguage.DetectAsync"/>), preferring the device's region. Use it from the UI thread.
    /// </summary>
    public class TextToSpeechReplySpeaker : IReplySpeaker
    {
        private static IReadOnlyList<Locale> _locales;
        private Reader _reader;

        /// <summary>
        /// Gives a text's language (two-letter code, or null for the device's default voice); by default
        /// <see cref="TextLanguage.DetectAsync"/>: the writing system's, and for Latin script <see cref="TextLanguage.IdentifyLatin"/>'s or
        /// Apple's recognizer.
        /// </summary>
        public Func<string, Task<string>> DetectLanguage { get; set; } = TextLanguage.DetectAsync;

        /// <inheritdoc/>
        public event Action<string> Speaking;

        /// <inheritdoc/>
        public Task Completion => _reader?.Completion ?? Task.CompletedTask;

        /// <inheritdoc/>
        public void Start(ChatMessageItem reply)
        {
            Stop();
            if (reply == null)
                return;
            _reader = new Reader(this, reply);
        }

        /// <inheritdoc/>
        public void Stop()
        {
            _reader?.Stop();
            _reader = null;
        }

        /// <summary>
        /// Speak a text now, with a voice for its language.
        /// </summary>
        /// <param name="text">The text, as written (Markdown is removed)</param>
        /// <param name="cancellationToken">Stops speaking</param>
        public virtual async Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            string speech = ReplySpeechText.CleanForSpeech(text);
            if (speech.Length == 0)
                return;
            Locale locale = await ChooseLocaleAsync(await DetectLanguage(speech));
            cancellationToken.ThrowIfCancellationRequested();
            await TextToSpeech.Default.SpeakAsync(speech, new SpeechOptions { Locale = locale }, cancellationToken);
        }

        // Reads one reply: the segmenter cuts the text into sentences, which are spoken in order, everything that arrived
        // while one was spoken together as the next.
        private sealed class Reader
        {
            private readonly TextToSpeechReplySpeaker _owner;
            private readonly ChatMessageItem _reply;
            private readonly SpeechSegmenter _segmenter = new SpeechSegmenter();
            private readonly List<string> _queue = new List<string>();
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private readonly TaskCompletionSource<bool> _done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool _speaking, _finished;

            public Reader(TextToSpeechReplySpeaker owner, ChatMessageItem reply)
            {
                _owner = owner;
                _reply = reply;
                reply.PropertyChanged += OnReplyChanged;
                Read();
            }

            // Ends when the reply has been read out, or reading was stopped.
            public Task Completion => _done.Task;

            private void OnReplyChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(ChatMessageItem.Text) || e.PropertyName == nameof(ChatMessageItem.State))
                    Read();
            }

            private void Read()
            {
                if (_stop.IsCancellationRequested)
                    return;
                switch (_reply.State)
                {
                    case ChatMessageState.Streaming:
                        _queue.AddRange(_segmenter.Add(_reply.Text));
                        break;
                    case ChatMessageState.Done:
                        _queue.AddRange(_segmenter.Finish(_reply.Text));
                        _reply.PropertyChanged -= OnReplyChanged;
                        _finished = true;
                        break;
                    default:
                        // Interrupted or failed: no more.
                        Stop();
                        return;
                }
                Pump();
            }

            public void Stop()
            {
                _reply.PropertyChanged -= OnReplyChanged;
                _stop.Cancel();
                _queue.Clear();
                _finished = true;
                if (!_speaking)
                    _done.TrySetResult(true);
            }

            private async void Pump()
            {
                if (_speaking)
                    return;
                _speaking = true;
                try
                {
                    while (_queue.Count > 0 && !_stop.IsCancellationRequested)
                    {
                        string text = string.Join(" ", _queue);
                        _queue.Clear();
                        _owner.Speaking?.Invoke(text);
                        try
                        {
                            await _owner.SpeakAsync(text, _stop.Token);
                        }
                        catch (Exception)
                        {
                            // Stopped, or no speech engine.
                        }
                    }
                }
                finally
                {
                    _speaking = false;
                }
                if (_finished)
                    _done.TrySetResult(true);
            }
        }

        // A voice for the language, preferring the device's own region (e.g. zh-TW on a Taiwanese device); null for the
        // device's default voice.
        private static async Task<Locale> ChooseLocaleAsync(string language)
        {
            CultureInfo ui = CultureInfo.CurrentUICulture;
            if (language == null)
            {
                // Latin text: the default voice, unless the device's own language uses another script (e.g. a Chinese
                // device reading an English reply), then an English voice.
                if (ReplySpeechText.DetectScriptLanguage(ui.NativeName) == null)
                    return null;
                language = "en";
            }
            IReadOnlyList<Locale> locales = await GetLocalesAsync();
            List<Locale> matches = locales.Where(l => SameLanguage(l.Language, language)).ToList();
            if (matches.Count == 0)
                return null;
            string region = RegionOf(ui);
            Locale sameRegion = SameLanguage(ui.TwoLetterISOLanguageName, language)
                ? matches.FirstOrDefault(l => string.Equals(l.Country, region, StringComparison.OrdinalIgnoreCase))
                : null;
            if (sameRegion != null)
                return sameRegion;
            // Otherwise the main variant: zh-CN, en-US.
            string preferred = language == "zh" ? "CN" : language == "en" ? "US" : null;
            return matches.FirstOrDefault(l => preferred != null && string.Equals(l.Country, preferred, StringComparison.OrdinalIgnoreCase))
                ?? matches[0];
        }

        private static async Task<IReadOnlyList<Locale>> GetLocalesAsync()
        {
            if (_locales == null)
            {
                try
                {
                    _locales = (await TextToSpeech.Default.GetLocalesAsync()).ToList();
                }
                catch (Exception)
                {
                    _locales = Array.Empty<Locale>();
                }
            }
            return _locales;
        }

        // Locale.Language is a two-letter code on most platforms but may include the region (e.g. "zh-CN").
        private static bool SameLanguage(string code, string language) =>
            !string.IsNullOrEmpty(code)
            && (string.Equals(code, language, StringComparison.OrdinalIgnoreCase)
                || code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase));

        private static string RegionOf(CultureInfo culture)
        {
            try
            {
                return new RegionInfo(culture.Name).TwoLetterISORegionName;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
