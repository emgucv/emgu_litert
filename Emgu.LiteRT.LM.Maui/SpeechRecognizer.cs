//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Turns a voice recording (<see cref="VoiceRecorder"/>'s 16 kHz mono WAV) into text with the platform's speech
    /// recognizer - on the device where it can be - so models that don't take audio (a server's model, small text models)
    /// can be talked to: Android's SpeechRecognizer fed the recording (Android 13+), and Apple's Speech framework on iOS
    /// and Mac Catalyst. Not on Windows.
    /// <para>
    /// iOS and Mac Catalyst ask the user the first time, which needs <c>NSSpeechRecognitionUsageDescription</c> in
    /// Info.plist. On Android the on-device recognizer is used when it has the language; otherwise the normal one, which
    /// may send the audio to Google's service (and a download of the on-device language is requested for next time).
    /// </para>
    /// </summary>
    public static class SpeechRecognizer
    {
        /// <summary>
        /// The language to listen in, a BCP-47 tag such as "zh-CN" (e.g. one of <see cref="VoiceLanguages.All"/>), or
        /// null / empty for the device's language.
        /// </summary>
        public static string Language { get; set; }

        /// <summary>
        /// Added to the errors that say a language can't be recognized, e.g. where the app lets the user choose another
        /// ("Choose the language in Settings > Voice language."); or null.
        /// </summary>
        public static string ChooseLanguageHint { get; set; }

        private static string Hint => string.IsNullOrEmpty(ChooseLanguageHint) ? string.Empty : " " + ChooseLanguageHint;

        /// <summary>The language recognized: <see cref="Language"/>, else the device's, as a plain language-region tag.</summary>
        public static string LanguageTag => VoiceLanguages.Resolve(Language, System.Globalization.CultureInfo.CurrentUICulture.Name);

        /// <summary>True if this device can write down a recording (Android 13+ with a recognizer, iOS, Mac Catalyst).</summary>
        public static bool IsSupported
        {
            get
            {
#if ANDROID
                try
                {
                    return OperatingSystem.IsAndroidVersionAtLeast(33)
                        && Android.Speech.SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context);
                }
                catch (Exception) { return false; }
#elif IOS || MACCATALYST
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The words in the recording, or an empty string if none were recognised.
        /// </summary>
        /// <param name="wav">A 16 kHz mono 16-bit WAV recording, as <see cref="VoiceRecorder.StopAsync"/> returns</param>
        /// <param name="token">Stops recognizing</param>
        /// <exception cref="PlatformNotSupportedException">Where <see cref="IsSupported"/> is false</exception>
        /// <exception cref="InvalidOperationException">The language can't be recognized, or recognition failed</exception>
        public static async Task<string> TranscribeAsync(byte[] wav, CancellationToken token = default)
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                throw new PlatformNotSupportedException("Speech recognition from a recording needs Android 13 or later.");
            return await TranscribeAndroidAsync(PcmOf(wav), token);
#elif IOS || MACCATALYST
            return await TranscribeAppleAsync(wav, token);
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException("Speech recognition isn't available on this platform.");
#endif
        }

        // The samples of a WAV file: the "data" chunk.
        private static byte[] PcmOf(byte[] wav)
        {
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                int size = BitConverter.ToInt32(wav, pos + 4);
                if (id == "data")
                {
                    size = Math.Min(size, wav.Length - pos - 8);
                    byte[] pcm = new byte[size];
                    Buffer.BlockCopy(wav, pos + 8, pcm, 0, size);
                    return pcm;
                }
                pos += 8 + size + (size & 1);
            }
            throw new InvalidDataException("The recording has no audio data.");
        }

#if ANDROID
        private static readonly SemaphoreSlim _androidGate = new SemaphoreSlim(1, 1);

        [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
        private static async Task<string> TranscribeAndroidAsync(byte[] pcm, CancellationToken token)
        {
            await _androidGate.WaitAsync(token);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    try { return await RecognizeAndroidAsync(pcm, token); }
                    catch (RecognizerDisconnectedException) when (attempt == 0)
                    {
                        // The recognition service went away (it can, while the last recognizer is being released): try
                        // once more with a new one.
                        await Task.Delay(500, token);
                    }
                }
            }
            finally
            {
                _androidGate.Release();
            }
        }

        // The recognizer reads the samples from a pipe (RecognizerIntent.EXTRA_AUDIO_SOURCE, Android 13+) instead of the
        // microphone. Google's on-device recognizer wants them at the speed they were recorded (given them all at once, it
        // dropped words), so recognition takes about as long as the recording, and it doesn't take the end of the pipe as
        // the end of speech: its final result came minutes later. So the recording is followed by a moment of silence and
        // StopListening, and if the final words still don't come soon, the last partial results are used - they were the
        // same words. A recognizer is used for one recording only: a reused one passed late results of the last recording
        // to the next.
        [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
        private static async Task<string> RecognizeAndroidAsync(byte[] pcm, CancellationToken token)
        {
            var context = Android.App.Application.Context;
            Android.OS.ParcelFileDescriptor[] pipe = Android.OS.ParcelFileDescriptor.CreatePipe();
            Android.Speech.SpeechRecognizer recognizer = null;
            Listener listener = new Listener();
            try
            {
                string tag = LanguageTag;
                listener.Tag = tag;
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguageModel, Android.Speech.RecognizerIntent.LanguageModelFreeForm);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguage, tag);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPreferOffline, true);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPartialResults, true);
                    // A segmented session: the recognizer splits the recording at pauses and reports each stretch's words
                    // (OnSegmentResults) instead of stopping at the first pause.
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraSegmentedSession, Android.Speech.RecognizerIntent.ExtraAudioSource);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraAudioSource, pipe[0]);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraAudioSourceChannelCount, 1);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraAudioSourceEncoding, (int)Android.Media.Encoding.Pcm16bit);
                    intent.PutExtra(Android.Speech.RecognizerIntent.ExtraAudioSourceSamplingRate, VoiceRecorder.SampleRate);
                    recognizer = Android.Speech.SpeechRecognizer.IsOnDeviceRecognitionAvailable(context)
                        ? Android.Speech.SpeechRecognizer.CreateOnDeviceSpeechRecognizer(context)
                        : Android.Speech.SpeechRecognizer.CreateSpeechRecognizer(context);
                    // The on-device recognizer listens only in the languages installed on the device (the device's own
                    // by default): for another, ask for it to be installed for next time and use the normal recognizer,
                    // which can recognize it online.
                    Android.Speech.RecognitionSupport support = await CheckSupportAsync(recognizer, intent, context);
                    if (support != null && !VoiceLanguages.IsListed(support.InstalledOnDeviceLanguages, tag))
                    {
                        bool downloading = VoiceLanguages.IsListed(support.SupportedOnDeviceLanguages, tag);
                        if (downloading)
                            recognizer.TriggerModelDownload(intent);
                        recognizer.Destroy();
                        // The normal recognizer (Google's service, which can use the network) lists the online languages.
                        recognizer = Android.Speech.SpeechRecognizer.CreateSpeechRecognizer(context);
                        Android.Speech.RecognitionSupport online = await CheckSupportAsync(recognizer, intent, context);
                        if (online != null && !VoiceLanguages.IsListed(online.OnlineLanguages, tag)
                            && !VoiceLanguages.IsListed(online.InstalledOnDeviceLanguages, tag))
                            throw new InvalidOperationException(LanguageUnavailableMessage(tag, downloading));
                        intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPreferOffline, false);
                    }
                    recognizer.SetRecognitionListener(listener);
                    recognizer.StartListening(intent);
                });
                await Task.WhenAny(listener.Ready, listener.Done, Task.Delay(2000, token));
                if (!listener.Done.IsCompleted)
                    await FeedAsync(pipe[1], pcm, listener.Done, token);
                listener.Stopping = true;
                if (!listener.Done.IsCompleted)
                    await MainThread.InvokeOnMainThreadAsync(() => recognizer.StopListening());
                await Task.WhenAny(listener.Done, Task.Delay(2500, token));
                listener.FinishWithPartialResults();
                return await listener.Done;
            }
            finally
            {
                try { pipe[0].Close(); } catch (Exception) { }
                try { pipe[1].Close(); } catch (Exception) { }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    try
                    {
                        recognizer?.Cancel();
                        recognizer?.Destroy();
                    }
                    catch (Exception) { }
                });
            }
        }

        // What the recognizer can do in each language (Android 13+): installed on the device, downloadable, online. Null if it
        // doesn't say within three seconds.
        [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
        private static async Task<Android.Speech.RecognitionSupport> CheckSupportAsync(
            Android.Speech.SpeechRecognizer recognizer, Android.Content.Intent intent, Android.Content.Context context)
        {
            var result = new TaskCompletionSource<Android.Speech.RecognitionSupport>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                recognizer.CheckRecognitionSupport(intent, context.MainExecutor, new SupportCallback(result));
            }
            catch (Exception)
            {
                return null;
            }
            Task finished = await Task.WhenAny(result.Task, Task.Delay(3000));
            return finished == result.Task ? await result.Task : null;
        }

        private sealed class SupportCallback : Java.Lang.Object, Android.Speech.IRecognitionSupportCallback
        {
            private readonly TaskCompletionSource<Android.Speech.RecognitionSupport> _result;
            public SupportCallback(TaskCompletionSource<Android.Speech.RecognitionSupport> result) { _result = result; }
            public void OnSupportResult(Android.Speech.RecognitionSupport recognitionSupport) => _result.TrySetResult(recognitionSupport);
            public void OnError(int error) => _result.TrySetResult(null);
        }

        private static string LanguageUnavailableMessage(string tag, bool downloadRequested = false) =>
            "Speech recognition isn't available for " + VoiceLanguages.NameOf(tag) + " on this phone: its offline speech language isn't " +
            "installed" + (downloadRequested ? " (a download was requested - try again in a few minutes)" : "") +
            " and it isn't offered online." + Hint;

        // Writes the samples, then 0.6 s of silence, 20 ms at a time at the speed they were recorded.
        private static Task FeedAsync(Android.OS.ParcelFileDescriptor pipe, byte[] pcm, Task done, CancellationToken token)
        {
            return Task.Run(async () =>
            {
                const int chunk = VoiceRecorder.SampleRate / 50 * 2;
                byte[] audio = new byte[pcm.Length + VoiceRecorder.SampleRate * 6 / 10 * 2];
                Buffer.BlockCopy(pcm, 0, audio, 0, pcm.Length);
                using var output = new Android.OS.ParcelFileDescriptor.AutoCloseOutputStream(pipe);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int offset = 0; offset < audio.Length && !done.IsCompleted; offset += chunk)
                {
                    output.Write(audio, offset, Math.Min(chunk, audio.Length - offset));
                    output.Flush();
                    long due = (long)(offset + chunk) * 500 / VoiceRecorder.SampleRate;
                    long wait = due - clock.ElapsedMilliseconds;
                    if (wait > 0)
                        await Task.Delay((int)wait, token);
                }
            }, token);
        }

        private sealed class RecognizerDisconnectedException : Exception
        {
            public RecognizerDisconnectedException() : base("The speech recognition service disconnected.") { }
        }

        // The words of each stretch of speech, joined when the session ends. (Without a segmented session Google's
        // on-device recognizer still split the recording, reported each stretch only as partial results and gave an
        // empty final result.)
        private sealed class Listener : Java.Lang.Object, Android.Speech.IRecognitionListener
        {
            private readonly TaskCompletionSource<string> _done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly List<string> _stretches = new List<string>();
            // The latest partial result of the stretch being recognised.
            private string _partial = string.Empty;

            public Task<string> Done => _done.Task;
            public Task Ready => _ready.Task;

            // The language asked for, for the error message.
            public string Tag { get; set; }

            // Set once the input has ended: an error after that (StopListening can report "Client") just ends it.
            public bool Stopping { get; set; }

            // Ends the recognition with the words heard so far, if it hasn't ended.
            public void FinishWithPartialResults() => Finish();

            private static string TextOf(Android.OS.Bundle bundle)
            {
                var texts = bundle?.GetStringArrayList(Android.Speech.SpeechRecognizer.ResultsRecognition);
                return texts != null && texts.Count > 0 ? (texts[0] ?? string.Empty).Trim() : string.Empty;
            }

            // A stretch's final words, or its last partial result when the final one came back empty.
            private void Add(Android.OS.Bundle bundle)
            {
                string text = TextOf(bundle);
                if (text.Length == 0)
                    text = _partial;
                if (text.Length > 0)
                    _stretches.Add(text);
                _partial = string.Empty;
            }

            private void Finish()
            {
                if (_done.Task.IsCompleted)
                    return;
                if (_partial.Length > 0)
                    _stretches.Add(_partial);
                _partial = string.Empty;
                _done.TrySetResult(string.Join(" ", _stretches));
            }

            public void OnSegmentResults(Android.OS.Bundle segmentResults) => Add(segmentResults);

            public void OnEndOfSegmentedSession() => Finish();

            public void OnResults(Android.OS.Bundle results)
            {
                Add(results);
                Finish();
            }

            public void OnError(Android.Speech.SpeechRecognizerError error)
            {
                if (error == Android.Speech.SpeechRecognizerError.ServerDisconnected)
                    _done.TrySetException(new RecognizerDisconnectedException());
                else if ((error == Android.Speech.SpeechRecognizerError.LanguageNotSupported || error == Android.Speech.SpeechRecognizerError.LanguageUnavailable)
                    && _stretches.Count == 0 && _partial.Length == 0)
                    _done.TrySetException(new InvalidOperationException(LanguageUnavailableMessage(Tag)));
                // No words heard is a result, not a failure.
                else if (Stopping || _stretches.Count > 0 || _partial.Length > 0 || error == Android.Speech.SpeechRecognizerError.NoMatch
                    || error == Android.Speech.SpeechRecognizerError.SpeechTimeout)
                    Finish();
                else
                    _done.TrySetException(new InvalidOperationException("Speech recognition failed: " + error));
            }

            public void OnPartialResults(Android.OS.Bundle partialResults)
            {
                string text = TextOf(partialResults);
                if (text.Length > 0)
                    _partial = text;
            }

            public void OnBeginningOfSpeech() { }
            public void OnBufferReceived(byte[] buffer) { }
            public void OnEndOfSpeech() { }
            public void OnEvent(int eventType, Android.OS.Bundle @params) { }
            public void OnReadyForSpeech(Android.OS.Bundle @params) => _ready.TrySetResult(true);
            public void OnRmsChanged(float rmsdB) { }
        }
#endif

#if IOS || MACCATALYST
        private static async Task<string> TranscribeAppleAsync(byte[] wav, CancellationToken token)
        {
            var authorized = new TaskCompletionSource<Speech.SFSpeechRecognizerAuthorizationStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            Speech.SFSpeechRecognizer.RequestAuthorization(status => authorized.TrySetResult(status));
            if (await authorized.Task != Speech.SFSpeechRecognizerAuthorizationStatus.Authorized)
                throw new UnauthorizedAccessException("Speech recognition isn't allowed. Turn it on for this app in the system's privacy settings.");

            // The language chosen, else the device's.
            string chosen = Language;
            string tag = string.IsNullOrWhiteSpace(chosen) ? null : VoiceLanguages.Resolve(chosen, null);
            Speech.SFSpeechRecognizer recognizer = tag != null
                ? new Speech.SFSpeechRecognizer(new Foundation.NSLocale(tag))
                : new Speech.SFSpeechRecognizer(Foundation.NSLocale.CurrentLocale) ?? new Speech.SFSpeechRecognizer();
            if (recognizer == null)
                throw new InvalidOperationException("Speech recognition isn't supported for " + VoiceLanguages.NameOf(tag) + "." + Hint);
            if (!recognizer.Available)
                throw new InvalidOperationException("Speech recognition isn't available for " + (tag != null ? VoiceLanguages.NameOf(tag) : "this language") + " right now.");

            string path = Path.Combine(Path.GetTempPath(), "speech-" + Guid.NewGuid().ToString("N") + ".wav");
            await File.WriteAllBytesAsync(path, wav, token);
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Speech.SFSpeechRecognitionTask task = null;
            try
            {
                var request = new Speech.SFSpeechUrlRecognitionRequest(Foundation.NSUrl.FromFilename(path))
                {
                    ShouldReportPartialResults = false,
                    // On the device when it can (the recording then doesn't leave it).
                    RequiresOnDeviceRecognition = recognizer.SupportsOnDeviceRecognition
                };
                if (OperatingSystem.IsIOSVersionAtLeast(16) || OperatingSystem.IsMacCatalystVersionAtLeast(16))
                    request.AddsPunctuation = true;
                task = recognizer.GetRecognitionTask(request, (result, error) =>
                {
                    if (result != null && result.Final)
                        done.TrySetResult(result.BestTranscription.FormattedString ?? string.Empty);
                    else if (error != null)
                    {
                        // "No speech detected" is a result, not a failure.
                        if (error.Code == 1110)
                            done.TrySetResult(string.Empty);
                        else
                            done.TrySetException(new InvalidOperationException("Speech recognition failed: " + error.LocalizedDescription));
                    }
                });
                using (token.Register(() => done.TrySetCanceled(token)))
                {
                    Task finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(30), token));
                    if (finished != done.Task)
                        throw new TimeoutException("Speech recognition took too long.");
                    return await done.Task;
                }
            }
            finally
            {
                task?.Cancel();
                try { File.Delete(path); } catch (Exception) { }
            }
        }
#endif
    }
}
