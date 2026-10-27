// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    // Emgu.LiteRT.Models' PngEncoder (the image feature's PNG writer, so it needs no image library). Model-free: the PNG is
    // read back here - its chunks, their CRCs, the zlib stream and the pixels - and PngMetadata's text goes in and comes
    // out. (A decoder's view of it is UnitTestPngEncoderDecode, which needs Emgu CV's native library.)
    [TestFixture]
    public class UnitTestPngEncoder
    {
        internal static byte[] Sample(int width, int height)
        {
            byte[] rgb = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                rgb[i * 3] = (byte)(i * 17);
                rgb[i * 3 + 1] = (byte)(255 - i * 9);
                rgb[i * 3 + 2] = (byte)(i * i);
            }
            return rgb;
        }

        // The chunks of a PNG (type, data), each CRC checked.
        private static List<(string Type, byte[] Data)> Chunks(byte[] png)
        {
            Assert.That(png.Take(8), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));
            var chunks = new List<(string, byte[])>();
            int at = 8;
            while (at < png.Length)
            {
                int length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
                string type = Encoding.ASCII.GetString(png, at + 4, 4);
                byte[] data = png.Skip(at + 8).Take(length).ToArray();
                uint crc = (uint)((png[at + 8 + length] << 24) | (png[at + 9 + length] << 16) | (png[at + 10 + length] << 8) | png[at + 11 + length]);
                Assert.That(crc, Is.EqualTo(Crc32(png.Skip(at + 4).Take(length + 4).ToArray())), "CRC of " + type);
                chunks.Add((type, data));
                at += 12 + length;
            }
            return chunks;
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        [Test]
        public void EncodesRgbThatReadsBackToTheSamePixels()
        {
            const int width = 5, height = 3;
            byte[] rgb = Sample(width, height);
            List<(string Type, byte[] Data)> chunks = Chunks(PngEncoder.Encode(rgb, width, height));
            Assert.That(chunks.Select(c => c.Type), Is.EqualTo(new[] { "IHDR", "IDAT", "IEND" }));
            byte[] header = chunks[0].Data;
            Assert.That((header[3], header[7], header[8], header[9]), Is.EqualTo(((byte)width, (byte)height, (byte)8, (byte)2)));
            byte[] raw;
            using (var input = new MemoryStream(chunks[1].Data))
            using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                zlib.CopyTo(output);
                raw = output.ToArray();
            }
            Assert.That(raw.Length, Is.EqualTo((width * 3 + 1) * height));
            for (int y = 0; y < height; y++)
            {
                Assert.That(raw[y * (width * 3 + 1)], Is.Zero, "filter of row " + y);
                Assert.That(raw.Skip(y * (width * 3 + 1) + 1).Take(width * 3), Is.EqualTo(rgb.Skip(y * width * 3).Take(width * 3)), "row " + y);
            }
        }

        [Test]
        public void KeepsMetadataText()
        {
            byte[] png = PngEncoder.Encode(new byte[2 * 2 * 3], 2, 2);
            png = PngMetadata.AddText(png, "Description", "a red fox in the snow");
            png = PngMetadata.AddText(png, "Seed", "42");
            IReadOnlyDictionary<string, string> text = PngMetadata.ReadText(png);
            Assert.That(text["Description"], Is.EqualTo("a red fox in the snow"));
            Assert.That(text["Seed"], Is.EqualTo("42"));
            Assert.That(Chunks(png).Last().Type, Is.EqualTo("IEND"));
        }

        [Test]
        public void RejectsPixelsThatDontFit()
        {
            Assert.Throws<ArgumentException>(() => PngEncoder.Encode(new byte[10], 2, 2));
            Assert.Throws<ArgumentNullException>(() => PngEncoder.Encode(null, 1, 1));
        }
    }

    // A real decoder's view of PngEncoder's output: Emgu CV decodes it to the same pixels, metadata and all. Needs Emgu
    // CV's native library, so not in the model-free CI run.
    [TestFixture]
    [Category("Native")]
    public class UnitTestPngEncoderDecode
    {
        [Test]
        public void DecodesToTheSamePixelsWithMetadata()
        {
            const int width = 5, height = 3;
            byte[] rgb = UnitTestPngEncoder.Sample(width, height);
            byte[] png = PngMetadata.AddText(PngEncoder.Encode(rgb, width, height), "Seed", "42");
            using Mat decoded = new Mat();
            CvInvoke.Imdecode(png, ImreadModes.ColorBgr, decoded);
            Assert.That((decoded.Width, decoded.Height, decoded.NumberOfChannels), Is.EqualTo((width, height, 3)));
            byte[] bgr = new byte[width * height * 3];
            decoded.CopyTo(bgr);
            for (int i = 0; i < width * height; i++)
                Assert.That((bgr[i * 3 + 2], bgr[i * 3 + 1], bgr[i * 3]), Is.EqualTo((rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2])), "pixel " + i);
        }
    }
}
