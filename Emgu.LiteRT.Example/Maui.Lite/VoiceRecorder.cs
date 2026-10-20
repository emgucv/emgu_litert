//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.IO;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Records from the microphone into a WAV file in memory (16 kHz, mono, 16-bit PCM), a format LiteRT-LM's
    /// audio decoder (miniaudio) reads and the sample rate Gemma 4's audio encoder works at. MAUI has no
    /// recorder of its own, so each platform uses its native API: AVAudioRecorder on iOS / Mac Catalyst,
    /// AudioRecord on Android and MediaCapture on Windows.
    /// </summary>
    internal sealed class VoiceRecorder : IVoiceInput
    {
        public const int SampleRate = 16000;

        public bool IsRecording { get; private set; }

        /// <summary>
        /// Whether <see cref="ReadLevelDb"/> measures the microphone (not on Windows, whose recorder writes straight to a
        /// stream), which voice conversations need to hear when speech starts and ends.
        /// </summary>
        public static bool SupportsLevel =>
#if WINDOWS
            false;
#else
            true;
#endif

        /// <summary>
        /// The microphone's level (dBFS) since the last call, while recording; very negative when quiet.
        /// </summary>
        public double ReadLevelDb()
        {
            if (!IsRecording)
                return -160;
#if IOS || MACCATALYST
            AVFoundation.AVAudioRecorder recorder = _recorder;
            if (recorder == null)
                return -160;
            recorder.UpdateMeters();
            return recorder.AveragePower(0);
#elif ANDROID
            lock (_levelLock)
            {
                // No samples since the last call: the level hasn't changed (it isn't silence).
                if (_sampleCount > 0)
                    _lastLevel = VoiceActivityDetector.LevelDb(_sumOfSquares, _sampleCount);
                _sumOfSquares = 0;
                _sampleCount = 0;
                return _lastLevel;
            }
#else
            return -160;
#endif
        }

        /// <summary>
        /// Whether replies are heard through headphones (wired, USB or Bluetooth), so the microphone doesn't pick them up
        /// and the user can interrupt a reply by speaking. False where it can't be told.
        /// </summary>
        public static bool HeadphonesConnected()
        {
            try
            {
#if IOS || MACCATALYST
                foreach (AVFoundation.AVAudioSessionPortDescription port in AVFoundation.AVAudioSession.SharedInstance().CurrentRoute.Outputs)
                {
                    string type = port.PortType?.ToString();
                    if (type == AVFoundation.AVAudioSession.PortHeadphones.ToString()
                        || type == AVFoundation.AVAudioSession.PortBluetoothA2DP.ToString()
                        || type == AVFoundation.AVAudioSession.PortBluetoothHfp.ToString()
                        || type == AVFoundation.AVAudioSession.PortBluetoothLE.ToString())
                        return true;
                }
#elif ANDROID
                var audio = (Android.Media.AudioManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.AudioService);
                foreach (Android.Media.AudioDeviceInfo device in audio.GetDevices(Android.Media.GetDevicesTargets.Outputs))
                {
                    switch (device.Type)
                    {
                        case Android.Media.AudioDeviceType.WiredHeadphones:
                        case Android.Media.AudioDeviceType.WiredHeadset:
                        case Android.Media.AudioDeviceType.UsbHeadset:
                        case Android.Media.AudioDeviceType.BluetoothA2dp:
                        case Android.Media.AudioDeviceType.BluetoothSco:
                        case Android.Media.AudioDeviceType.BleHeadset:
                            return true;
                    }
                }
#endif
            }
            catch (Exception)
            {
            }
            return false;
        }

        /// <summary>
        /// Ask for the microphone permission (the first time) and start recording.
        /// </summary>
        /// <returns>False if the permission was denied</returns>
        public async Task<bool> StartAsync()
        {
            if (IsRecording)
                return true;
            PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Microphone>();
            if (status != PermissionStatus.Granted)
                return false;

            // A sound monitor lets go of the microphone first (SoundSource.cs).
            await MicrophoneGate.EnterAsync();
            try { await StartPlatformAsync(); }
            catch
            {
                MicrophoneGate.Exit();
#if IOS || MACCATALYST
                _recorder?.Dispose();
                _recorder = null;
                if (_path != null) File.Delete(_path);
#endif
                throw;
            }
            IsRecording = true;
            return true;
        }

        /// <summary>
        /// Stop recording.
        /// </summary>
        /// <returns>The recording as a WAV file</returns>
        public async Task<byte[]> StopAsync()
        {
            if (!IsRecording)
                return null;
            IsRecording = false;
            try { return await StopPlatformAsync(); }
            finally { MicrophoneGate.Exit(); }
        }

#if IOS || MACCATALYST
        private AVFoundation.AVAudioRecorder _recorder;
        private string _path;

        private Task StartPlatformAsync()
        {
            AVFoundation.AVAudioSession session = AVFoundation.AVAudioSession.SharedInstance();
            // DefaultToSpeaker: on iPhone, read-aloud replies play through the speaker, not the earpiece.
            Foundation.NSError error = session.SetCategory(AVFoundation.AVAudioSessionCategory.PlayAndRecord,
                AVFoundation.AVAudioSessionCategoryOptions.DefaultToSpeaker);
            if (error != null)
                throw new InvalidOperationException(error.LocalizedDescription);
            session.SetActive(true, out error);
            if (error != null)
                throw new InvalidOperationException(error.LocalizedDescription);

            // The .wav extension makes AVAudioRecorder write a WAV file.
            _path = Path.Combine(Path.GetTempPath(), "voice_" + Guid.NewGuid().ToString("N") + ".wav");
            var settings = new AVFoundation.AudioSettings
            {
                Format = AudioToolbox.AudioFormatType.LinearPCM,
                SampleRate = SampleRate,
                NumberChannels = 1,
                LinearPcmBitDepth = 16,
                LinearPcmFloat = false,
                LinearPcmBigEndian = false
            };
            _recorder = AVFoundation.AVAudioRecorder.Create(Foundation.NSUrl.FromFilename(_path), settings, out error);
            if (_recorder == null || error != null)
                throw new InvalidOperationException(error?.LocalizedDescription ?? "Could not create the audio recorder.");
            _recorder.MeteringEnabled = true;
            if (!_recorder.Record())
            {
                _recorder.Dispose();
                _recorder = null;
                throw new InvalidOperationException("Could not start recording.");
            }
            return Task.CompletedTask;
        }

        private Task<byte[]> StopPlatformAsync()
        {
            _recorder.Stop();
            _recorder.Dispose();
            _recorder = null;
            try { return Task.FromResult(File.ReadAllBytes(_path)); }
            finally { File.Delete(_path); _path = null; }
        }
#elif ANDROID
        private Android.Media.AudioRecord _record;
        private MemoryStream _pcm;
        private Task _readLoop;
        private volatile bool _reading;
        private readonly object _levelLock = new object();
        private double _sumOfSquares;
        private int _sampleCount;
        private double _lastLevel = -160;

        private Task StartPlatformAsync()
        {
            int minBuffer = Android.Media.AudioRecord.GetMinBufferSize(SampleRate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
            int bufferSize = Math.Max(minBuffer, SampleRate / 5 * 2); // at least 200 ms
            // Read 20 ms at a time, so ReadLevelDb (every 50 ms in a voice chat) always has new samples: reading the
            // whole buffer at once left it three calls in four with none.
            int chunkSize = SampleRate / 50 * 2;
            _record = new Android.Media.AudioRecord(Android.Media.AudioSource.Mic, SampleRate, Android.Media.ChannelIn.Mono,
                Android.Media.Encoding.Pcm16bit, bufferSize * 2);
            if (_record.State != Android.Media.State.Initialized)
            {
                _record.Release();
                _record = null;
                throw new InvalidOperationException("Could not open the microphone.");
            }
            _pcm = new MemoryStream();
            _reading = true;
            _record.StartRecording();
            Android.Media.AudioRecord record = _record;
            MemoryStream pcm = _pcm;
            _readLoop = Task.Run(() =>
            {
                byte[] buffer = new byte[chunkSize];
                while (_reading)
                {
                    int read = record.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        pcm.Write(buffer, 0, read);
                        double sum = 0;
                        for (int i = 0; i + 1 < read; i += 2)
                        {
                            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                            sum += (double)sample * sample;
                        }
                        lock (_levelLock)
                        {
                            _sumOfSquares += sum;
                            _sampleCount += read / 2;
                        }
                    }
                    else if (read < 0)
                        break;
                }
            });
            return Task.CompletedTask;
        }

        private async Task<byte[]> StopPlatformAsync()
        {
            _reading = false;
            _record.Stop();
            await _readLoop;
            _record.Release();
            _record = null;
            byte[] wav = ToWav(_pcm.ToArray());
            _pcm = null;
            return wav;
        }

        // A canonical 44-byte WAV header followed by the 16-bit mono PCM samples.
        private static byte[] ToWav(byte[] pcm)
        {
            using MemoryStream wav = new MemoryStream(44 + pcm.Length);
            using BinaryWriter writer = new BinaryWriter(wav);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);                // fmt chunk size
            writer.Write((short)1);          // PCM
            writer.Write((short)1);          // mono
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);    // byte rate
            writer.Write((short)2);          // block align
            writer.Write((short)16);         // bits per sample
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
            writer.Flush();
            return wav.ToArray();
        }
#elif WINDOWS
        private Windows.Media.Capture.MediaCapture _capture;
        private Windows.Storage.Streams.InMemoryRandomAccessStream _stream;

        private async Task StartPlatformAsync()
        {
            _capture = new Windows.Media.Capture.MediaCapture();
            await _capture.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Audio
            });
            _stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            Windows.Media.MediaProperties.MediaEncodingProfile profile =
                Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.Auto);
            profile.Audio = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(SampleRate, 1, 16);
            await _capture.StartRecordToStreamAsync(profile, _stream);
        }

        private async Task<byte[]> StopPlatformAsync()
        {
            await _capture.StopRecordAsync();
            _capture.Dispose();
            _capture = null;

            byte[] wav = new byte[_stream.Size];
            using (Windows.Storage.Streams.DataReader reader = new Windows.Storage.Streams.DataReader(_stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)wav.Length);
                reader.ReadBytes(wav);
            }
            _stream.Dispose();
            _stream = null;
            return wav;
        }
#endif
    }
}

#endif
