//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents.Audio;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// The microphone as an <see cref="ISoundSource"/>: 16 kHz mono samples as they arrive, for listeners that run for a long time (a sound
    /// monitor on YAMNet, meeting notes) rather than a recording (<see cref="VoiceRecorder"/>). AudioRecord at 16 kHz on Android, an
    /// AVAudioEngine input tap on iOS and Mac Catalyst, averaged down to 16 kHz (<see cref="BoxResampler"/>); not on Windows yet
    /// (<see cref="IsSupported"/>).
    /// <para>
    /// The app must declare microphone access (<c>android.permission.RECORD_AUDIO</c>, <c>NSMicrophoneUsageDescription</c>); a listener that
    /// shares the microphone with recordings should let go of it through <see cref="MicrophoneGate"/>. The samples arrive on a background
    /// thread.
    /// </para>
    /// </summary>
    public sealed class MicrophoneSoundSource : ISoundSource
    {
        /// <inheritdoc/>
        public event Action<float[]> Samples;

#pragma warning disable CS0067 // raised only on iOS and Mac Catalyst
        /// <summary>Raised when something else took the microphone (a call, on iOS and Mac Catalyst): no samples until <see cref="Resumed"/>.</summary>
        public event Action Interrupted;

        /// <summary>Raised when the microphone was started again after <see cref="Interrupted"/> or a change of audio route.</summary>
        public event Action Resumed;
#pragma warning restore CS0067

        /// <summary>Whether this platform has it: Android, iOS and Mac Catalyst.</summary>
        public static bool IsSupported =>
#if ANDROID || IOS || MACCATALYST
            true;
#else
            false;
#endif

#if ANDROID
        /// <summary>Whether the app may record with its screen off or another app in front: on Android with a foreground service of the microphone type.</summary>
        public static bool CanRecordInBackground => true;

        private Android.Media.AudioRecord _record;
        private volatile bool _reading;
        private Task _loop;

        /// <inheritdoc/>
        public async Task<bool> StartAsync()
        {
            if (!await HasPermissionAsync()) return false;
            int rate = SoundInput.SampleRate;
            int bufferSize = Math.Max(Android.Media.AudioRecord.GetMinBufferSize(rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit), rate * 2);
            _record = new Android.Media.AudioRecord(Android.Media.AudioSource.Mic, rate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit, bufferSize);
            if (_record.State != Android.Media.State.Initialized)
            {
                _record.Release(); _record = null;
                throw new InvalidOperationException("Could not open the microphone.");
            }
            _reading = true;
            _record.StartRecording();
            var record = _record;
            _loop = Task.Run(() =>
            {
                var bytes = new byte[rate / 10 * 2];            // 100 ms
                while (_reading)
                {
                    int read = record.Read(bytes, 0, bytes.Length);
                    if (read < 0) break;
                    if (read < 2) continue;
                    var samples = new float[read / 2];
                    for (int i = 0; i < samples.Length; i++) samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
                    Samples?.Invoke(samples);
                }
            });
            return true;
        }

        /// <inheritdoc/>
        public async Task StopAsync()
        {
            _reading = false;
            if (_record != null) { try { _record.Stop(); } catch { } }
            if (_loop != null) await _loop;
            _record?.Release(); _record = null; _loop = null;
        }
#elif IOS || MACCATALYST
        private AVFoundation.AVAudioEngine _engine;
        private Foundation.NSObject _interruptionObserver, _configurationObserver;

        // A call or another app's audio interrupts the session and stops the engine; a route change (headphones) stops it too.
        // It is started again when the interruption ends (with ShouldResume) or right after the route change.
        private void ObserveInterruptions()
        {
            _interruptionObserver = AVFoundation.AVAudioSession.Notifications.ObserveInterruption((_, args) =>
            {
                if (_engine == null) return;
                if (args.InterruptionType == AVFoundation.AVAudioSessionInterruptionType.Began) Interrupted?.Invoke();
                else if (args.Option == AVFoundation.AVAudioSessionInterruptionOptions.ShouldResume) Restart();
            });
            _configurationObserver = Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
                AVFoundation.AVAudioEngine.ConfigurationChangeNotification, _ => { if (_engine != null && !_engine.Running) Restart(); }, _engine);
        }

        private void Restart()
        {
            AVFoundation.AVAudioEngine engine = _engine;
            if (engine == null) return;
            AVFoundation.AVAudioSession.SharedInstance().SetActive(true, out Foundation.NSError _);
            engine.Prepare();
            if (engine.StartAndReturnError(out Foundation.NSError _)) Resumed?.Invoke();
            else Interrupted?.Invoke();
        }

        private void StopObserving()
        {
            _interruptionObserver?.Dispose();
            _interruptionObserver = null;
            if (_configurationObserver != null) Foundation.NSNotificationCenter.DefaultCenter.RemoveObserver(_configurationObserver);
            _configurationObserver = null;
        }

        /// <summary>
        /// Whether the app may record with its screen off or another app in front: its Info.plist's <c>UIBackgroundModes</c>
        /// has <c>audio</c> (iOS; a Mac app always may). Without it iOS stops the microphone when the app leaves the screen.
        /// </summary>
        public static bool CanRecordInBackground
        {
            get
            {
#if MACCATALYST
                return true;
#else
                return Foundation.NSBundle.MainBundle.ObjectForInfoDictionary("UIBackgroundModes") is Foundation.NSArray modes
                    && Enumerable.Range(0, (int)modes.Count).Any(i => modes.GetItem<Foundation.NSString>((nuint)i)?.ToString() == "audio");
#endif
            }
        }

        /// <inheritdoc/>
        public async Task<bool> StartAsync()
        {
            if (!await HasPermissionAsync()) return false;
            AVFoundation.AVAudioSession session = AVFoundation.AVAudioSession.SharedInstance();
            Foundation.NSError error = session.SetCategory(AVFoundation.AVAudioSessionCategory.PlayAndRecord, AVFoundation.AVAudioSessionCategoryOptions.DefaultToSpeaker);
            if (error != null) throw new InvalidOperationException(error.LocalizedDescription);
            session.SetActive(true, out error);
            if (error != null) throw new InvalidOperationException(error.LocalizedDescription);
            _engine = new AVFoundation.AVAudioEngine();
            var format = _engine.InputNode.GetBusOutputFormat(0);
            if (format.SampleRate < 8000) throw new InvalidOperationException("The microphone isn't available.");
            var resampler = new BoxResampler(format.SampleRate);
            _engine.InputNode.InstallTapOnBus(0, 4096, format, (buffer, when) =>
            {
                int frames = (int)buffer.FrameLength;
                if (frames == 0 || buffer.FloatChannelData == IntPtr.Zero) return;
                var channel = new float[frames];
                System.Runtime.InteropServices.Marshal.Copy(System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer.FloatChannelData), channel, 0, frames);
                float[] converted = resampler.Process(channel, frames);
                if (converted.Length > 0) Samples?.Invoke(converted);
            });
            _engine.Prepare();
            if (!_engine.StartAndReturnError(out error))
            {
                _engine.InputNode.RemoveTapOnBus(0);
                _engine.Dispose(); _engine = null;
                throw new InvalidOperationException(error?.LocalizedDescription ?? "Could not start the microphone.");
            }
            ObserveInterruptions();
            return true;
        }

        /// <inheritdoc/>
        public Task StopAsync()
        {
            StopObserving();
            if (_engine != null)
            {
                _engine.InputNode.RemoveTapOnBus(0);
                _engine.Stop();
                _engine.Dispose();
                _engine = null;
            }
            return Task.CompletedTask;
        }
#else
        /// <summary>Whether the app may record in the background: not where there is no microphone source.</summary>
        public static bool CanRecordInBackground => false;

        /// <inheritdoc/>
        public Task<bool> StartAsync() => throw new PlatformNotSupportedException("Listening through the microphone isn't supported on this platform yet.");

        /// <inheritdoc/>
        public Task StopAsync() => Task.CompletedTask;
#endif

#if ANDROID || IOS || MACCATALYST
        private static async Task<bool> HasPermissionAsync()
        {
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted) status = await Permissions.RequestAsync<Permissions.Microphone>();
            return status == PermissionStatus.Granted;
        }
#endif

        /// <inheritdoc/>
        public void Dispose() { StopAsync().GetAwaiter().GetResult(); }
    }
}
