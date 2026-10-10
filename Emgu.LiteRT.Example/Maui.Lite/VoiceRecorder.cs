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
    internal sealed class VoiceRecorder
    {
        public const int SampleRate = 16000;

        public bool IsRecording { get; private set; }

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

            await StartPlatformAsync();
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
            return await StopPlatformAsync();
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
            byte[] wav = File.ReadAllBytes(_path);
            File.Delete(_path);
            return Task.FromResult(wav);
        }
#elif ANDROID
        private Android.Media.AudioRecord _record;
        private MemoryStream _pcm;
        private Task _readLoop;
        private volatile bool _reading;

        private Task StartPlatformAsync()
        {
            int minBuffer = Android.Media.AudioRecord.GetMinBufferSize(SampleRate, Android.Media.ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
            int bufferSize = Math.Max(minBuffer, SampleRate / 5 * 2); // at least 200 ms
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
                byte[] buffer = new byte[bufferSize];
                while (_reading)
                {
                    int read = record.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                        pcm.Write(buffer, 0, read);
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
