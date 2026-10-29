//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;

namespace Emgu.LiteRT.LM.Agents
{
    /// <summary>
    /// Writes mono 16-bit PCM WAV to a file as the samples arrive (a meeting's recording: an hour at 16 kHz is 115 MB, so it
    /// never sits in memory); the header's lengths are filled in when it is closed. Not thread-safe: one writer at a time.
    /// </summary>
    public sealed class WavFileWriter : IDisposable
    {
        private readonly FileStream _file;
        private long _samples;
        private bool _closed;

        /// <summary>Create the file (replacing one already there) and write its header.</summary>
        /// <param name="path">The file</param>
        /// <param name="sampleRate">The samples per second</param>
        public WavFileWriter(string path, int sampleRate = 16000)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            Path = path ?? throw new ArgumentNullException(nameof(path));
            SampleRate = sampleRate;
            _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
            WriteHeader(0);
        }

        /// <summary>The file.</summary>
        public string Path { get; }

        /// <summary>The samples per second.</summary>
        public int SampleRate { get; }

        /// <summary>The length written so far.</summary>
        public TimeSpan Duration => TimeSpan.FromSeconds(_samples / (double)SampleRate);

        /// <summary>Add samples (-1..1, clipped).</summary>
        /// <param name="samples">The samples</param>
        public void Write(float[] samples)
        {
            if (_closed) throw new ObjectDisposedException(nameof(WavFileWriter));
            if (samples == null || samples.Length == 0) return;
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)Math.Round(Math.Max(-1f, Math.Min(1f, samples[i])) * 32767);
                bytes[2 * i] = (byte)value;
                bytes[2 * i + 1] = (byte)(value >> 8);
            }
            _file.Write(bytes, 0, bytes.Length);
            _samples += samples.Length;
        }

        // RIFF header for 16-bit mono PCM with the data's length.
        private void WriteHeader(long samples)
        {
            long dataLength = samples * 2;
            using (var header = new MemoryStream(44))
            using (var writer = new BinaryWriter(header))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write((int)Math.Min(uint.MaxValue, 36 + dataLength));
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write((int)Math.Min(uint.MaxValue, dataLength));
                writer.Flush();
                _file.Position = 0;
                _file.Write(header.ToArray(), 0, 44);
            }
        }

        /// <summary>Fill in the header's lengths and close the file (again does nothing).</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            WriteHeader(_samples);
            _file.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose() => Close();
    }
}
