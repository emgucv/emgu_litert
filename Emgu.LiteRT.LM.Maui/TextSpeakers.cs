//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Globalization;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Chat;
using Emgu.LiteRT.LM.Models;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>Says text aloud: the device's voices (<see cref="DeviceTextSpeaker"/>) or Kokoro (<see cref="KokoroTextSpeaker"/>).</summary>
    public interface ITextSpeaker
    {
        /// <summary>Say a text; returns when it has been said, or at once when cancelled (which stops it).</summary>
        /// <param name="speech">The text, already cleaned for speech</param>
        /// <param name="language">Its language (two-letter code), or null for the device's</param>
        /// <param name="cancellationToken">Stops speaking</param>
        Task SpeakAsync(string speech, string language, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The device's text to speech (MAUI's TextToSpeech), with a voice for the text's language that prefers the device's
    /// region (e.g. zh-TW on a Taiwanese device).
    /// </summary>
    public sealed class DeviceTextSpeaker : ITextSpeaker
    {
        private static IReadOnlyList<Locale> _locales;

        /// <inheritdoc/>
        public async Task SpeakAsync(string speech, string language, CancellationToken cancellationToken)
        {
            Locale locale = await ChooseLocaleAsync(language);
            cancellationToken.ThrowIfCancellationRequested();
            await TextToSpeech.Default.SpeakAsync(speech, new SpeechOptions { Locale = locale }, cancellationToken);
        }

        // A voice for the language, preferring the device's own region; null for the device's default voice.
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
            string region = ChatVoice.DeviceRegion;
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
    }

    /// <summary>
    /// Kokoro on the device (<see cref="KokoroSpeech"/>): the text is cut into pieces (<see cref="VoiceEngines.SpeechPieces"/>: the first
    /// sentence, then sentences together), and while one plays (written to a WAV file and played by
    /// <see cref="AudioFilePlayer"/>) the next is made, so the pause between them is the player's, not Kokoro's. The voice
    /// for each language is <see cref="ChatVoice.KokoroVoiceFor"/>'s.
    /// </summary>
    public sealed class KokoroTextSpeaker : ITextSpeaker
    {
        /// <inheritdoc/>
        public Task SpeakAsync(string speech, string language, CancellationToken cancellationToken)
        {
            string kokoroLanguage = VoiceEngines.KokoroLanguage(language, ChatVoice.DeviceRegion, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                ?? "en-US";
            return SpeakInAsync(speech, kokoroLanguage, ChatVoice.KokoroVoiceFor?.Invoke(kokoroLanguage), cancellationToken);
        }

        /// <summary>Say a text in a Kokoro language and voice (e.g. Settings' "Try it").</summary>
        /// <param name="speech">The text, already cleaned for speech</param>
        /// <param name="kokoroLanguage">A Kokoro language (BCP-47, e.g. "en-US")</param>
        /// <param name="voice">A voice of that language, or null for its default</param>
        /// <param name="cancellationToken">Stops speaking</param>
        public async Task SpeakInAsync(string speech, string kokoroLanguage, string voice, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> sentences = VoiceEngines.SpeechPieces(speech);
            if (sentences.Count == 0)
                return;
            KokoroSpeech kokoro = KokoroSpeech.Default;
            Task<TextToSpeechAudio> next = kokoro.SynthesizeAsync(sentences[0], kokoroLanguage, voice, cancellationToken);
            for (int i = 0; i < sentences.Count; i++)
            {
                TextToSpeechAudio audio = await next;
                cancellationToken.ThrowIfCancellationRequested();
                // The next sentence is made while this one plays.
                next = i + 1 < sentences.Count
                    ? kokoro.SynthesizeAsync(sentences[i + 1], kokoroLanguage, voice, cancellationToken)
                    : null;
                try
                {
                    await PlayAsync(audio, cancellationToken);
                }
                catch (Exception)
                {
                    // Observe the one being made, so a failure there isn't unobserved.
                    if (next != null)
                        _ = next.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw;
                }
            }
        }

        private static async Task PlayAsync(TextToSpeechAudio audio, CancellationToken cancellationToken)
        {
            if (audio.Samples.Length == 0)
                return;
            string path = Path.Combine(FileSystem.CacheDirectory, "kokoro-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                using (var writer = new WavFileWriter(path, audio.SampleRate))
                    writer.Write(audio.Samples);
                await AudioFilePlayer.PlayAsync(path, 0, cancellationToken);
            }
            finally
            {
                try { File.Delete(path); }
                catch (Exception) { /* The cache folder is cleared by the system. */ }
            }
        }
    }

    /// <summary>
    /// What the app says aloud with: the device's voices, or Kokoro on the device when it was chosen (Settings > Voice,
    /// <see cref="Features.VoiceFeature"/>), it is downloaded, and it speaks the text's language - else the device's. Used
    /// by <see cref="TextToSpeechReplySpeaker"/> (read-aloud, the voice chat, alert replies) and by features that say
    /// things (alerts, exercise counts) through <see cref="SpeakTextAsync"/>.
    /// </summary>
    public static class ChatVoice
    {
        /// <summary>The device's voices.</summary>
        public static ITextSpeaker Device { get; } = new DeviceTextSpeaker();

        /// <summary>Kokoro.</summary>
        public static KokoroTextSpeaker Kokoro { get; } = new KokoroTextSpeaker();

        /// <summary>The chosen engine (the <see cref="VoiceEngines.SettingKey"/> setting).</summary>
        public static VoiceEngine Engine { get; set; } = VoiceEngine.System;

        /// <summary>The Kokoro voice chosen for a Kokoro language (BCP-47), or null for its default.</summary>
        public static Func<string, string> KokoroVoiceFor { get; set; }

        /// <summary>The device's region (two letters), or null.</summary>
        public static string DeviceRegion
        {
            get
            {
                try
                {
                    return new RegionInfo(CultureInfo.CurrentUICulture.Name).TwoLetterISORegionName;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>The speaker for a text's language now.</summary>
        /// <param name="language">The text's language (two-letter code), or null for the device's</param>
        public static ITextSpeaker For(string language)
        {
            string kokoroLanguage = VoiceEngines.KokoroLanguage(language, DeviceRegion, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            bool kokoro = VoiceEngines.Choose(Engine, KokoroSpeech.IsAvailable, kokoroLanguage) == VoiceEngine.Kokoro
                && KokoroSpeech.Default.IsDownloaded;
            return kokoro ? Kokoro : Device;
        }

        /// <summary>
        /// Say a text with the chosen voice (Kokoro falling back to the device's if it fails).
        /// </summary>
        /// <param name="speech">The text, already cleaned for speech</param>
        /// <param name="language">Its language (two-letter code), or null for the device's</param>
        /// <param name="cancellationToken">Stops speaking</param>
        public static async Task SpeakAsync(string speech, string language, CancellationToken cancellationToken)
        {
            ITextSpeaker speaker = For(language);
            if (speaker == Device)
            {
                await Device.SpeakAsync(speech, language, cancellationToken);
                return;
            }
            try
            {
                await speaker.SpeakAsync(speech, language, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Kokoro couldn't (freed meanwhile, a failed load): the device's voice says it.
                cancellationToken.ThrowIfCancellationRequested();
                await Device.SpeakAsync(speech, language, cancellationToken);
            }
        }

        /// <summary>Say a text as written (Markdown removed), with a voice for its language (<see cref="TextLanguage.DetectAsync"/>).</summary>
        /// <param name="text">The text</param>
        /// <param name="cancellationToken">Stops speaking</param>
        public static async Task SpeakTextAsync(string text, CancellationToken cancellationToken)
        {
            string speech = ReplySpeechText.CleanForSpeech(text);
            if (speech.Length == 0)
                return;
            await SpeakAsync(speech, await TextLanguage.DetectAsync(speech), cancellationToken);
        }
    }
}
