//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Emgu.LiteRT.Models
{
    /// <summary>
    /// Writes an RGB picture as a PNG (8-bit RGB, no filtering, zlib over .NET's deflate): what an image model gives
    /// (<see cref="ZImageResult.Rgb"/>), without an image library. <see cref="PngMetadata"/> adds text to the result.
    /// </summary>
    public static class PngEncoder
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] CrcTable = MakeCrcTable();

        /// <summary>Encode a picture.</summary>
        /// <param name="rgb">The pixels, three bytes each (red, green, blue), row by row</param>
        /// <param name="width">The width</param>
        /// <param name="height">The height</param>
        /// <returns>The PNG</returns>
        public static byte[] Encode(byte[] rgb, int width, int height)
        {
            if (rgb == null)
                throw new ArgumentNullException(nameof(rgb));
            if (width <= 0 || height <= 0 || rgb.Length < width * height * 3)
                throw new ArgumentException("The pixels don't fit the size");
            // Each row starts with the filter byte 0 (none).
            int stride = width * 3;
            byte[] raw = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
            using (var png = new MemoryStream())
            {
                png.Write(Signature, 0, Signature.Length);
                byte[] header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8;   // bit depth
                header[9] = 2;   // colour type: RGB
                WriteChunk(png, "IHDR", header);
                WriteChunk(png, "IDAT", Zlib(raw));
                WriteChunk(png, "IEND", Array.Empty<byte>());
                return png.ToArray();
            }
        }

        /// <summary>Encode an image model's result.</summary>
        /// <param name="result">The picture</param>
        public static byte[] Encode(ZImageResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            return Encode(result.Rgb, result.Width, result.Height);
        }

        // A zlib stream: the two-byte header, raw deflate, the Adler-32 of the input (DeflateStream writes raw deflate
        // on every target).
        private static byte[] Zlib(byte[] data)
        {
            using (var buffer = new MemoryStream())
            {
                buffer.WriteByte(0x78);
                buffer.WriteByte(0x9C);
                using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, true))
                    deflate.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (byte value in data)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }
                byte[] adler = new byte[4];
                WriteBigEndian(adler, 0, (b << 16) | a);
                buffer.Write(adler, 0, 4);
                return buffer.ToArray();
            }
        }

        private static void WriteChunk(Stream png, string type, byte[] data)
        {
            byte[] length = new byte[4];
            WriteBigEndian(length, 0, (uint)data.Length);
            png.Write(length, 0, 4);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            png.Write(typeBytes, 0, 4);
            png.Write(data, 0, data.Length);
            // The CRC runs over the type then the data.
            uint crc = 0xFFFFFFFFu;
            crc = Crc(typeBytes, crc);
            crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            byte[] crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, crc);
            png.Write(crcBytes, 0, 4);
        }

        private static uint Crc(byte[] bytes, uint crc)
        {
            foreach (byte b in bytes)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static void WriteBigEndian(byte[] target, int offset, uint value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }

        private static uint[] MakeCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
