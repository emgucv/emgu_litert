//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents.Audio;
#if IOS || MACCATALYST
using AVFoundation;
#endif

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A video file's frames and sound, read with the platform's own decoders for indexing it (Emgu.LiteRT.LM.Models'
    /// VideoMomentIndex): MediaMetadataRetriever and MediaExtractor/MediaCodec on Android, AVAssetImageGenerator and
    /// AVAssetReader on iOS and Mac Catalyst, MediaComposition and MediaTranscoder on Windows.
    /// </summary>
    public sealed class VideoSource : IDisposable
    {
        private const int SampleRate = 16000;
        private readonly string _path;
#if ANDROID
        private readonly Android.Media.MediaMetadataRetriever _retriever;
        private readonly object _retrieverLock = new object();
#elif IOS || MACCATALYST
        private readonly AVFoundation.AVAsset _asset;
        private readonly AVFoundation.AVAssetImageGenerator _generator;
#elif WINDOWS
        private readonly Windows.Media.Editing.MediaComposition _composition;
#endif

        /// <summary>Whether this platform can read videos.</summary>
        public static bool IsSupported =>
#if ANDROID || IOS || MACCATALYST || WINDOWS
            true;
#else
            false;
#endif

        /// <summary>The video's length, in milliseconds.</summary>
        public long DurationMs { get; private set; }

        /// <summary>Whether it has a sound track.</summary>
        public bool HasAudio { get; private set; }

#if ANDROID
        private VideoSource(string path, Android.Media.MediaMetadataRetriever retriever)
        {
            _path = path;
            _retriever = retriever;
        }
#elif IOS || MACCATALYST
        private VideoSource(string path, AVFoundation.AVAsset asset)
        {
            _path = path;
            _asset = asset;
            _generator = new AVFoundation.AVAssetImageGenerator(asset)
            {
                AppliesPreferredTrackTransform = true,
                // A key frame near the time is fine (a window's frames are seconds apart), and much faster.
                RequestedTimeToleranceBefore = CoreMedia.CMTime.FromSeconds(0.5, 600),
                RequestedTimeToleranceAfter = CoreMedia.CMTime.FromSeconds(0.5, 600)
            };
        }
#elif WINDOWS
        private VideoSource(string path, Windows.Media.Editing.MediaComposition composition)
        {
            _path = path;
            _composition = composition;
        }
#endif

        /// <summary>Open a video file (or, on Android, a content:// URI such as a MediaStore video's) and read its length.</summary>
        /// <param name="path">The file, or a content:// URI on Android</param>
        /// <exception cref="PlatformNotSupportedException">Where <see cref="IsSupported"/> is false</exception>
        /// <exception cref="IOException">The file isn't a video this platform can read</exception>
        public static async Task<VideoSource> OpenAsync(string path)
        {
#if ANDROID
            return await Task.Run(() =>
            {
                var retriever = new Android.Media.MediaMetadataRetriever();
                try
                {
                    if (IsContentUri(path))
                        retriever.SetDataSource(Android.App.Application.Context, Android.Net.Uri.Parse(path));
                    else
                        retriever.SetDataSource(path);
                    if (retriever.ExtractMetadata(Android.Media.MetadataKey.HasVideo) != "yes")
                        throw new IOException("It has no video.");
                    var source = new VideoSource(path, retriever)
                    {
                        DurationMs = long.TryParse(retriever.ExtractMetadata(Android.Media.MetadataKey.Duration), out long ms) ? ms : 0,
                        HasAudio = retriever.ExtractMetadata(Android.Media.MetadataKey.HasAudio) == "yes"
                    };
                    return source;
                }
                catch (Exception e) when (!(e is IOException))
                {
                    retriever.Release();
                    throw new IOException("The video couldn't be read: " + e.Message, e);
                }
                catch (IOException)
                {
                    retriever.Release();
                    throw;
                }
            });
#elif IOS || MACCATALYST
            return await OpenAsync(AVFoundation.AVUrlAsset.Create(Foundation.NSUrl.FromFilename(path)), path);
#elif WINDOWS
            try
            {
                Windows.Storage.StorageFile file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
                Windows.Media.Editing.MediaClip clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
                var composition = new Windows.Media.Editing.MediaComposition();
                composition.Clips.Add(clip);
                return new VideoSource(path, composition)
                {
                    DurationMs = (long)clip.OriginalDuration.TotalMilliseconds,
                    HasAudio = clip.EmbeddedAudioTracks.Count > 0
                };
            }
            catch (Exception e)
            {
                throw new IOException("The video couldn't be read: " + e.Message, e);
            }
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException("Reading videos isn't supported on this platform.");
#endif
        }

#if ANDROID
        private static bool IsContentUri(string path) => path != null && path.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
#elif IOS || MACCATALYST
        /// <summary>Open a video the app already has as an AVAsset (e.g. from the Photos library) and read its length.</summary>
        /// <param name="asset">The video</param>
        /// <param name="name">What to call it in errors</param>
        /// <exception cref="IOException">It has no video track</exception>
        public static async Task<VideoSource> OpenAsync(AVFoundation.AVAsset asset, string name)
        {
            await asset.LoadValuesTaskAsync(new[] { "duration", "tracks" });
            if (asset.TracksWithMediaType(AVFoundation.AVMediaTypes.Video.GetConstant()).Length == 0)
                throw new IOException("It has no video.");
            return new VideoSource(name, asset)
            {
                DurationMs = (long)(asset.Duration.Seconds * 1000),
                HasAudio = asset.TracksWithMediaType(AVFoundation.AVMediaTypes.Audio.GetConstant()).Length > 0
            };
        }
#endif

        /// <summary>The frame nearest a time (on Android the frame at that time, not its key frame, which can be seconds away; within about half a second elsewhere), as a JPEG at most maxSize pixels on its longer side; null if there's none.</summary>
        /// <param name="ms">The time, in milliseconds</param>
        /// <param name="maxSize">The longest side, in pixels</param>
        public async Task<byte[]> FrameAtAsync(long ms, int maxSize = 512)
        {
            ms = Math.Max(0, Math.Min(ms, Math.Max(0, DurationMs - 1)));
#if ANDROID
            return await Task.Run(() =>
            {
                Android.Graphics.Bitmap frame;
                lock (_retrieverLock)
                {
                    frame = OperatingSystem.IsAndroidVersionAtLeast(27)
                        ? _retriever.GetScaledFrameAtTime(ms * 1000, Android.Media.Option.Closest, maxSize, maxSize)
                        : _retriever.GetFrameAtTime(ms * 1000, Android.Media.Option.Closest);
                }
                if (frame == null)
                    return null;
                try
                {
                    using var stream = new MemoryStream();
                    frame.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 85, stream);
                    return stream.ToArray();
                }
                finally
                {
                    frame.Recycle();
                }
            });
#elif IOS || MACCATALYST
            return await Task.Run(() =>
            {
                lock (_generator)
                {
                    _generator.MaximumSize = new CoreGraphics.CGSize(maxSize, maxSize);
#pragma warning disable CA1422 // CopyCGImageAtTime: the asynchronous replacement needs iOS 16; this runs off the UI thread.
                    using CoreGraphics.CGImage image = _generator.CopyCGImageAtTime(CoreMedia.CMTime.FromSeconds(ms / 1000.0, 600), out _, out Foundation.NSError error);
#pragma warning restore CA1422
                    if (image == null || error != null)
                        return null;
                    using var picture = new UIKit.UIImage(image);
                    return picture.AsJPEG(0.85f)?.ToArray();
                }
            });
#elif WINDOWS
            try
            {
                using Windows.Graphics.Imaging.ImageStream thumbnail = await _composition.GetThumbnailAsync(TimeSpan.FromMilliseconds(ms), 0, 0,
                    Windows.Media.Editing.VideoFramePrecision.NearestKeyFrame);
                Windows.Graphics.Imaging.BitmapDecoder decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(thumbnail);
                double scale = Math.Min(1.0, (double)maxSize / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                using Windows.Graphics.Imaging.SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    new Windows.Graphics.Imaging.BitmapTransform
                    {
                        ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
                        ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale),
                        InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant
                    },
                    Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
                using var output = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                Windows.Graphics.Imaging.BitmapEncoder encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, output);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                var bytes = new byte[output.Size];
                using (var reader = new Windows.Storage.Streams.DataReader(output.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)output.Size);
                    reader.ReadBytes(bytes);
                }
                return bytes;
            }
            catch (Exception)
            {
                return null;
            }
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException("Reading videos isn't supported on this platform.");
#endif
        }

        /// <summary>
        /// The sound track as 16 kHz mono samples (5 minutes are 9.6 MB), decoded off the UI thread; an empty array
        /// without one.
        /// </summary>
        /// <param name="token">Stops it</param>
        /// <param name="untilMs">Stop reading after this time (on Android, iOS and Mac Catalyst), or 0 for the whole track</param>
        public Task<short[]> ReadAudioAsync(CancellationToken token = default, long untilMs = 0)
        {
            if (!HasAudio)
                return Task.FromResult(new short[0]);
#if ANDROID || IOS || MACCATALYST
            return Task.Run(() => DecodeAudio(token, untilMs > 0 ? untilMs : long.MaxValue), token);
#elif WINDOWS
            return TranscodeAudioAsync(token);
#else
            throw new PlatformNotSupportedException("Reading videos isn't supported on this platform.");
#endif
        }

#if ANDROID
        // The first audio track decoded with MediaCodec (16-bit PCM, any rate and channels), mixed to mono and averaged
        // down to 16 kHz.
        private short[] DecodeAudio(CancellationToken token, long untilMs)
        {
            var extractor = new Android.Media.MediaExtractor();
            Android.Media.MediaCodec codec = null;
            long untilSamples = untilMs == long.MaxValue ? long.MaxValue : untilMs * SampleRate / 1000;
            var samples = new List<short>((int)Math.Min(int.MaxValue, Math.Min(DurationMs, untilMs) * SampleRate / 1000 + SampleRate));
            try
            {
                if (IsContentUri(_path))
                    extractor.SetDataSource(Android.App.Application.Context, Android.Net.Uri.Parse(_path), null);
                else
                    extractor.SetDataSource(_path);
                int track = -1;
                Android.Media.MediaFormat format = null;
                for (int i = 0; i < extractor.TrackCount && track < 0; i++)
                {
                    Android.Media.MediaFormat f = extractor.GetTrackFormat(i);
                    if (f.GetString(Android.Media.MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true)
                    {
                        track = i;
                        format = f;
                    }
                }
                if (track < 0)
                    return new short[0];
                extractor.SelectTrack(track);
                codec = Android.Media.MediaCodec.CreateDecoderByType(format.GetString(Android.Media.MediaFormat.KeyMime));
                codec.Configure(format, null, null, Android.Media.MediaCodecConfigFlags.None);
                codec.Start();
                int channels = format.GetInteger(Android.Media.MediaFormat.KeyChannelCount);
                BoxResampler resampler = new BoxResampler(format.GetInteger(Android.Media.MediaFormat.KeySampleRate));
                var info = new Android.Media.MediaCodec.BufferInfo();
                byte[] bytes = new byte[0];
                bool inputDone = false, outputDone = false;
                while (!outputDone && samples.Count < untilSamples)
                {
                    token.ThrowIfCancellationRequested();
                    if (!inputDone)
                    {
                        int inIndex = codec.DequeueInputBuffer(10000);
                        if (inIndex >= 0)
                        {
                            Java.Nio.ByteBuffer buffer = codec.GetInputBuffer(inIndex);
                            int size = extractor.ReadSampleData(buffer, 0);
                            if (size < 0)
                            {
                                codec.QueueInputBuffer(inIndex, 0, 0, 0, Android.Media.MediaCodecBufferFlags.EndOfStream);
                                inputDone = true;
                            }
                            else
                            {
                                codec.QueueInputBuffer(inIndex, 0, size, extractor.SampleTime, Android.Media.MediaCodecBufferFlags.None);
                                extractor.Advance();
                            }
                        }
                    }
                    int outIndex = codec.DequeueOutputBuffer(info, 10000);
                    if (outIndex == (int)Android.Media.MediaCodecInfoState.OutputFormatChanged)
                    {
                        Android.Media.MediaFormat output = codec.OutputFormat;
                        channels = output.GetInteger(Android.Media.MediaFormat.KeyChannelCount);
                        resampler = new BoxResampler(output.GetInteger(Android.Media.MediaFormat.KeySampleRate));
                    }
                    else if (outIndex >= 0)
                    {
                        if (info.Size > 0)
                        {
                            Java.Nio.ByteBuffer decoded = codec.GetOutputBuffer(outIndex);
                            decoded.Position(info.Offset);
                            decoded.Limit(info.Offset + info.Size);
                            if (bytes.Length < info.Size)
                                bytes = new byte[info.Size];
                            decoded.Get(bytes, 0, info.Size);
                            AddMono(samples, resampler, bytes, info.Size, channels);
                        }
                        codec.ReleaseOutputBuffer(outIndex, false);
                        if ((info.Flags & Android.Media.MediaCodecBufferFlags.EndOfStream) != 0)
                            outputDone = true;
                    }
                }
                return samples.ToArray();
            }
            finally
            {
                if (codec != null)
                {
                    try { codec.Stop(); } catch (Exception) { }
                    codec.Release();
                }
                extractor.Release();
            }
        }

        // Interleaved 16-bit little-endian samples of channels channels, mixed to mono, resampled and added.
        private static void AddMono(List<short> samples, BoxResampler resampler, byte[] bytes, int count, int channels)
        {
            channels = Math.Max(1, channels);
            int frames = count / (2 * channels);
            float[] mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += BitConverter.ToInt16(bytes, (f * channels + c) * 2);
                mono[f] = sum / (float)channels;
            }
            foreach (float v in resampler.Process(mono, frames))
                samples.Add((short)Math.Clamp((int)Math.Round(v), short.MinValue, short.MaxValue));
        }
#elif IOS || MACCATALYST
        // The first audio track read with AVAssetReader, which converts it to 16 kHz mono 16-bit PCM itself.
        private short[] DecodeAudio(CancellationToken token, long untilMs)
        {
            AVFoundation.AVAssetTrack track = _asset.TracksWithMediaType(AVFoundation.AVMediaTypes.Audio.GetConstant()).FirstOrDefault();
            if (track == null)
                return new short[0];
            using var reader = new AVFoundation.AVAssetReader(_asset, out Foundation.NSError error);
            if (error != null)
                throw new IOException("The video's sound couldn't be read: " + error.LocalizedDescription);
            var settings = new AVFoundation.AudioSettings
            {
                Format = AudioToolbox.AudioFormatType.LinearPCM,
                SampleRate = SampleRate,
                NumberChannels = 1,
                LinearPcmBitDepth = 16,
                LinearPcmFloat = false,
                LinearPcmBigEndian = false,
                LinearPcmNonInterleaved = false
            };
            using var output = new AVFoundation.AVAssetReaderTrackOutput(track, settings);
            reader.AddOutput(output);
            if (untilMs != long.MaxValue)
                reader.TimeRange = new CoreMedia.CMTimeRange { Start = CoreMedia.CMTime.Zero, Duration = CoreMedia.CMTime.FromSeconds(untilMs / 1000.0, 600) };
            if (!reader.StartReading())
                throw new IOException("The video's sound couldn't be read: " + reader.Error?.LocalizedDescription);
            var samples = new List<short>((int)Math.Min(int.MaxValue, Math.Min(DurationMs, untilMs) * SampleRate / 1000 + SampleRate));
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    reader.CancelReading();
                    token.ThrowIfCancellationRequested();
                }
                using CoreMedia.CMSampleBuffer buffer = output.CopyNextSampleBuffer();
                if (buffer == null)
                    break;
                using CoreMedia.CMBlockBuffer block = buffer.GetDataBuffer();
                if (block == null)
                    continue;
                int length = (int)block.DataLength;
                byte[] bytes = new byte[length];
                System.Runtime.InteropServices.GCHandle pinned = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    block.CopyDataBytes(0, (nuint)length, pinned.AddrOfPinnedObject());
                }
                finally
                {
                    pinned.Free();
                }
                for (int i = 0; i + 1 < length; i += 2)
                    samples.Add(BitConverter.ToInt16(bytes, i));
            }
            if (reader.Status == AVFoundation.AVAssetReaderStatus.Failed)
                throw new IOException("The video's sound couldn't be read: " + reader.Error?.LocalizedDescription);
            return samples.ToArray();
        }
#elif WINDOWS
        // The sound track transcoded to a 16 kHz mono 16-bit WAV file with MediaTranscoder, then read.
        private async Task<short[]> TranscodeAudioAsync(CancellationToken token)
        {
            string wavPath = Path.Combine(Path.GetTempPath(), "emgu_video_audio_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                Windows.Storage.StorageFile source = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(_path));
                Windows.Storage.StorageFolder folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(wavPath));
                Windows.Storage.StorageFile target = await folder.CreateFileAsync(Path.GetFileName(wavPath), Windows.Storage.CreationCollisionOption.ReplaceExisting);
                Windows.Media.MediaProperties.MediaEncodingProfile profile =
                    Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.Low);
                profile.Audio = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(SampleRate, 1, 16);
                var transcoder = new Windows.Media.Transcoding.MediaTranscoder();
                Windows.Media.Transcoding.PrepareTranscodeResult prepared = await transcoder.PrepareFileTranscodeAsync(source, target, profile);
                if (!prepared.CanTranscode)
                    return new short[0];
                await prepared.TranscodeAsync().AsTask(token);
                return ReadWavSamples(await File.ReadAllBytesAsync(wavPath, token));
            }
            finally
            {
                try { File.Delete(wavPath); } catch (Exception) { }
            }
        }

        // A 16-bit WAV file's samples (its "data" chunk).
        private static short[] ReadWavSamples(byte[] wav)
        {
            int position = 12;
            while (position + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, position, 4);
                int size = BitConverter.ToInt32(wav, position + 4);
                if (id == "data")
                {
                    var samples = new short[Math.Min(size, wav.Length - position - 8) / 2];
                    Buffer.BlockCopy(wav, position + 8, samples, 0, samples.Length * 2);
                    return samples;
                }
                position += 8 + size + (size & 1);
            }
            return new short[0];
        }
#endif

        /// <summary>Let go of the file.</summary>
        public void Dispose()
        {
#if ANDROID
            lock (_retrieverLock)
                _retriever.Release();
#elif IOS || MACCATALYST
            _generator.Dispose();
            _asset.Dispose();
#endif
        }
    }
}
