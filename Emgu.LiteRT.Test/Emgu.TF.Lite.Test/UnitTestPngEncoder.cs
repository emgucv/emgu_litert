// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test
{
    // Emgu.LiteRT.Models' PngEncoder (the image feature's PNG writer, so it needs no image library): a picture encoded by it
    // decodes to the same pixels, and PngMetadata's text goes in and comes out. No model.
    [TestFixture]
    public class UnitTestPngEncoder
    {
        [Test]
        public void EncodesRgbThatDecodesToTheSamePixels()
        {
            const int width = 5, height = 3;
            byte[] rgb = new byte[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                rgb[i * 3] = (byte)(i * 17);        // red
                rgb[i * 3 + 1] = (byte)(255 - i * 9); // green
                rgb[i * 3 + 2] = (byte)(i * i);     // blue
            }
            byte[] png = PngEncoder.Encode(rgb, width, height);
            Assert.That(png.Take(8), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));

            using Mat decoded = new Mat();
            CvInvoke.Imdecode(png, ImreadModes.ColorBgr, decoded);
            Assert.That((decoded.Width, decoded.Height, decoded.NumberOfChannels), Is.EqualTo((width, height, 3)));
            byte[] bgr = new byte[width * height * 3];
            decoded.CopyTo(bgr);
            for (int i = 0; i < width * height; i++)
                Assert.That((bgr[i * 3 + 2], bgr[i * 3 + 1], bgr[i * 3]), Is.EqualTo((rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2])), "pixel " + i);
        }

        [Test]
        public void KeepsMetadataTextAndStillDecodes()
        {
            byte[] png = PngEncoder.Encode(new byte[2 * 2 * 3], 2, 2);
            png = PngMetadata.AddText(png, "Description", "a red fox in the snow");
            png = PngMetadata.AddText(png, "Seed", "42");
            IReadOnlyDictionary<string, string> text = PngMetadata.ReadText(png);
            Assert.That(text["Description"], Is.EqualTo("a red fox in the snow"));
            Assert.That(text["Seed"], Is.EqualTo("42"));
            using Mat decoded = new Mat();
            CvInvoke.Imdecode(png, ImreadModes.ColorBgr, decoded);
            Assert.That((decoded.Width, decoded.Height), Is.EqualTo((2, 2)));
        }

        [Test]
        public void RejectsPixelsThatDontFit()
        {
            Assert.Throws<ArgumentException>(() => PngEncoder.Encode(new byte[10], 2, 2));
            Assert.Throws<ArgumentNullException>(() => PngEncoder.Encode(null, 1, 1));
        }
    }
}
