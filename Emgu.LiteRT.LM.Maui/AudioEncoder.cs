//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Turns a mono 16-bit PCM WAV recording into AAC in an MPEG-4 file (.m4a) with the platform's own encoder - AVAudioFile on
    /// iOS and Mac Catalyst, MediaCodec and MediaMuxer on Android - about a quarter of a megabyte a minute at the speech
    /// bit rate (a WAV at 16 kHz is 1.9 MB). Elsewhere it isn't supported and the WAV is kept.
    /// </summary>
    public static class AudioEncoder
    {
        /// <summary>The bit rate for speech: 32 kbit/s (14 MB an hour).</summary>
        public const int SpeechBitRate = 32000;

        /// <summary>Whether this platform can encode (<see cref="ToM4aAsync"/>).</summary>
        public static bool IsSupported =>
#if ANDROID || IOS || MACCATALYST
            true;
#else
            false;
#endif

        /// <summary>Encode a WAV file (as Emgu.LiteRT.LM.Agents' WavFileWriter writes it) to an .m4a file, off the UI thread.</summary>
        /// <param name="wavPath">The WAV file: mono, 16-bit PCM, a 44-byte header</param>
        /// <param name="m4aPath">The .m4a file to write (replaced)</param>
        /// <param name="bitRate">The bit rate</param>
        /// <param name="token">Stops it (the .m4a is then incomplete)</param>
        /// <exception cref="PlatformNotSupportedException">Where <see cref="IsSupported"/> is false</exception>
        public static Task ToM4aAsync(string wavPath, string m4aPath, int bitRate = SpeechBitRate, CancellationToken token = default) =>
            Task.Run(() => Encode(wavPath, m4aPath, bitRate, token), token);

        private static int SampleRateOf(string wavPath)
        {
            using var file = File.OpenRead(wavPath);
            var header = new byte[44];
            if (file.Read(header, 0, 44) != 44 || header[0] != 'R' || header[8] != 'W' || BitConverter.ToInt16(header, 22) != 1 || BitConverter.ToInt16(header, 34) != 16)
                throw new IOException("Expected a mono 16-bit PCM WAV file.");
            return BitConverter.ToInt32(header, 24);
        }

        private static void Encode(string wavPath, string m4aPath, int bitRate, CancellationToken token)
        {
            int sampleRate = SampleRateOf(wavPath);
            if (File.Exists(m4aPath)) File.Delete(m4aPath);
#if IOS || MACCATALYST
            var settings = new AVFoundation.AudioSettings
            {
                Format = AudioToolbox.AudioFormatType.MPEG4AAC,
                SampleRate = sampleRate,
                NumberChannels = 1,
                EncoderBitRate = bitRate
            };
            using var input = new AVFoundation.AVAudioFile(Foundation.NSUrl.FromFilename(wavPath), out Foundation.NSError readError);
            if (readError != null) throw new IOException("The recording couldn't be read: " + readError.LocalizedDescription);
            using (var output = new AVFoundation.AVAudioFile(Foundation.NSUrl.FromFilename(m4aPath), settings,
                input.ProcessingFormat.CommonFormat, input.ProcessingFormat.Interleaved, out Foundation.NSError writeError))
            {
                if (writeError != null) throw new IOException("The recording couldn't be encoded: " + writeError.LocalizedDescription);
                using var buffer = new AVFoundation.AVAudioPcmBuffer(input.ProcessingFormat, (uint)sampleRate);
                while (input.FramePosition < input.Length)
                {
                    token.ThrowIfCancellationRequested();
                    if (!input.ReadIntoBuffer(buffer, out Foundation.NSError error) || error != null)
                        throw new IOException("The recording couldn't be read: " + error?.LocalizedDescription);
                    if (buffer.FrameLength == 0) break;
                    if (!output.WriteFromBuffer(buffer, out error) || error != null)
                        throw new IOException("The recording couldn't be encoded: " + error?.LocalizedDescription);
                }
            }
#elif ANDROID
            var format = Android.Media.MediaFormat.CreateAudioFormat(Android.Media.MediaFormat.MimetypeAudioAac, sampleRate, 1);
            format.SetInteger(Android.Media.MediaFormat.KeyAacProfile, (int)Android.Media.MediaCodecProfileType.Aacobjectlc);
            format.SetInteger(Android.Media.MediaFormat.KeyBitRate, bitRate);
            format.SetInteger(Android.Media.MediaFormat.KeyMaxInputSize, 16384);
            Android.Media.MediaCodec codec = Android.Media.MediaCodec.CreateEncoderByType(Android.Media.MediaFormat.MimetypeAudioAac);
            var muxer = new Android.Media.MediaMuxer(m4aPath, Android.Media.MuxerOutputType.Mpeg4);
            bool muxing = false;
            try
            {
                codec.Configure(format, null, null, Android.Media.MediaCodecConfigFlags.Encode);
                codec.Start();
                using var wav = File.OpenRead(wavPath);
                wav.Position = 44;
                var bytes = new byte[16384];
                var info = new Android.Media.MediaCodec.BufferInfo();
                int track = -1;
                long samples = 0;
                bool inputDone = false, outputDone = false;
                while (!outputDone)
                {
                    token.ThrowIfCancellationRequested();
                    if (!inputDone)
                    {
                        int inIndex = codec.DequeueInputBuffer(10000);
                        if (inIndex >= 0)
                        {
                            Java.Nio.ByteBuffer buffer = codec.GetInputBuffer(inIndex);
                            buffer.Clear();
                            int count = wav.Read(bytes, 0, Math.Min(bytes.Length, buffer.Remaining()) & ~1);
                            long time = samples * 1000000L / sampleRate;
                            if (count <= 0)
                            {
                                codec.QueueInputBuffer(inIndex, 0, 0, time, Android.Media.MediaCodecBufferFlags.EndOfStream);
                                inputDone = true;
                            }
                            else
                            {
                                buffer.Put(bytes, 0, count);
                                codec.QueueInputBuffer(inIndex, 0, count, time, Android.Media.MediaCodecBufferFlags.None);
                                samples += count / 2;
                            }
                        }
                    }
                    int outIndex = codec.DequeueOutputBuffer(info, 10000);
                    if (outIndex == (int)Android.Media.MediaCodecInfoState.OutputFormatChanged)
                    {
                        track = muxer.AddTrack(codec.OutputFormat);
                        muxer.Start();
                        muxing = true;
                    }
                    else if (outIndex >= 0)
                    {
                        Java.Nio.ByteBuffer encoded = codec.GetOutputBuffer(outIndex);
                        if ((info.Flags & Android.Media.MediaCodecBufferFlags.CodecConfig) != 0)
                            info.Size = 0;
                        if (info.Size > 0 && muxing)
                        {
                            encoded.Position(info.Offset);
                            encoded.Limit(info.Offset + info.Size);
                            muxer.WriteSampleData(track, encoded, info);
                        }
                        codec.ReleaseOutputBuffer(outIndex, false);
                        if ((info.Flags & Android.Media.MediaCodecBufferFlags.EndOfStream) != 0)
                            outputDone = true;
                    }
                }
            }
            finally
            {
                try { codec.Stop(); } catch (Exception) { }
                codec.Release();
                if (muxing)
                    try { muxer.Stop(); } catch (Exception) { }
                muxer.Release();
            }
#else
            throw new PlatformNotSupportedException("Encoding audio isn't supported on this platform.");
#endif
        }
    }
}
