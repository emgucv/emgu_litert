//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Text;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// The length of a saved recording from its header, without opening a player (a reopened chat shows a meeting
    /// recording's length straight away): a PCM WAV's data size, or an MPEG-4 file's (.m4a) movie header.
    /// </summary>
    public static class AudioFileLength
    {
        /// <summary>The length of a WAV or MPEG-4 audio file, or null when it can't be told.</summary>
        /// <param name="data">The file's bytes</param>
        /// <param name="mediaType">"audio/wav" or "audio/mp4" (others: null)</param>
        public static TimeSpan? Of(byte[] data, string mediaType)
        {
            if (data == null) return null;
            try
            {
                switch (mediaType)
                {
                    case "audio/wav": return OfWav(data);
                    case "audio/mp4": return OfMp4(data);
                    default: return null;
                }
            }
            catch (ArgumentException) { return null; }
            catch (IndexOutOfRangeException) { return null; }
        }

        // The "fmt " chunk's byte rate and the "data" chunk's size.
        private static TimeSpan? OfWav(byte[] data)
        {
            if (data.Length < 12 || Tag(data, 0) != "RIFF" || Tag(data, 8) != "WAVE") return null;
            long byteRate = 0;
            for (long at = 12; at + 8 <= data.Length;)
            {
                string id = Tag(data, at);
                long size = BitConverter.ToUInt32(data, (int)at + 4);
                if (id == "fmt " && size >= 12) byteRate = BitConverter.ToUInt32(data, (int)at + 16);
                else if (id == "data")
                {
                    if (byteRate <= 0) return null;
                    // A header written before the end (or not patched) understates or overstates it: what is there counts.
                    long length = Math.Min(size, data.Length - (at + 8));
                    return TimeSpan.FromSeconds(length / (double)byteRate);
                }
                at += 8 + size + (size & 1);
            }
            return null;
        }

        // The movie header ("moov" > "mvhd"): its time scale and duration.
        private static TimeSpan? OfMp4(byte[] data)
        {
            long moov = FindBox(data, 0, data.Length, "moov", out long moovEnd);
            if (moov < 0) return null;
            long mvhd = FindBox(data, moov, moovEnd, "mvhd", out _);
            if (mvhd < 0) return null;
            int version = data[mvhd];
            long timeScale, duration;
            if (version == 1)
            {
                timeScale = BigEndian(data, mvhd + 20, 4);
                duration = BigEndian(data, mvhd + 24, 8);
            }
            else
            {
                timeScale = BigEndian(data, mvhd + 12, 4);
                duration = BigEndian(data, mvhd + 16, 4);
            }
            if (timeScale <= 0 || duration <= 0) return null;
            return TimeSpan.FromSeconds(duration / (double)timeScale);
        }

        // The first box of a type between start and end: where its content starts (-1 when absent) and where it ends.
        private static long FindBox(byte[] data, long start, long end, string type, out long boxEnd)
        {
            boxEnd = -1;
            for (long at = start; at + 8 <= end;)
            {
                long size = BigEndian(data, at, 4), header = 8;
                if (size == 1)
                {
                    if (at + 16 > end) return -1;
                    size = BigEndian(data, at + 8, 8);
                    header = 16;
                }
                else if (size == 0) size = end - at;
                if (size < header || at + size > end) return -1;
                if (Tag(data, at + 4) == type)
                {
                    boxEnd = at + size;
                    return at + header;
                }
                at += size;
            }
            return -1;
        }

        private static long BigEndian(byte[] data, long at, int count)
        {
            long value = 0;
            for (int i = 0; i < count; i++) value = (value << 8) | data[at + i];
            return value;
        }

        private static string Tag(byte[] data, long at) => Encoding.ASCII.GetString(data, (int)at, 4);
    }
}
