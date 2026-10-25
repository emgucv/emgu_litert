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

        /// <summary>Whether this platform has it: Android, iOS and Mac Catalyst.</summary>
        public static bool IsSupported =>
#if ANDROID || IOS || MACCATALYST
            true;
#else
            false;
#endif

#if ANDROID
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
            return true;
        }

        /// <inheritdoc/>
        public Task StopAsync()
        {
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
