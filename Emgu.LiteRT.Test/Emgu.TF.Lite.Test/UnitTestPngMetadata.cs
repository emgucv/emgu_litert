// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The text the generated pictures carry (PngMetadata, TODO item 17).
[TestFixture]
public class UnitTestPngMetadata
{
    // A 1 x 1 red PNG.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

    [Test]
    public void TextComesBackFromTheFile()
    {
        byte[] withText = PngMetadata.AddText(PngMetadata.AddText(Png, "Description", "A red fox in snow, watercolor"), "Seed", "123456789012");
        var read = PngMetadata.ReadText(withText);
        Assert.That(read["Description"], Is.EqualTo("A red fox in snow, watercolor"));
        Assert.That(read["Seed"], Is.EqualTo("123456789012"));
        Assert.That(PngMetadata.ReadText(Png), Is.Empty);
        // Still a PNG whose picture chunks are untouched: the same bytes up to IEND, which is still last.
        Assert.That(withText.Take(Png.Length - 12), Is.EqualTo(Png.Take(Png.Length - 12)));
        Assert.That(withText.Skip(withText.Length - 12), Is.EqualTo(Png.Skip(Png.Length - 12)));
    }

    [Test]
    public void KeepsNonLatinTextAndTheFirstOfAKeyword()
    {
        byte[] withText = PngMetadata.AddText(Png, "Description", "狐狸 and café ☃");
        withText = PngMetadata.AddText(withText, "Description", "second");
        Assert.That(PngMetadata.ReadText(withText)["Description"], Is.EqualTo("狐狸 and café ☃"));
    }

    [Test]
    public void ChunksHaveValidCrcs()
    {
        byte[] withText = PngMetadata.AddText(Png, "Seed", "42");
        int position = 8;
        int checkedChunks = 0;
        while (position < withText.Length)
        {
            int length = (withText[position] << 24) | (withText[position + 1] << 16) | (withText[position + 2] << 8) | withText[position + 3];
            byte[] typeAndData = withText.Skip(position + 4).Take(4 + length).ToArray();
            uint stored = (uint)((withText[position + 8 + length] << 24) | (withText[position + 9 + length] << 16)
                | (withText[position + 10 + length] << 8) | withText[position + 11 + length]);
            Assert.That(Crc32(typeAndData), Is.EqualTo(stored), "chunk at " + position);
            checkedChunks++;
            position += 12 + length;
        }
        Assert.That(checkedChunks, Is.EqualTo(4)); // IHDR, IDAT, the new iTXt and IEND
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    [Test]
    public void LeavesOtherDataAloneAndRejectsBadKeywords()
    {
        byte[] notPng = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 };
        Assert.That(PngMetadata.AddText(notPng, "Seed", "1"), Is.SameAs(notPng));
        Assert.That(PngMetadata.ReadText(notPng), Is.Empty);
        Assert.That(PngMetadata.AddText(null, "Seed", "1"), Is.Null);
        Assert.That(PngMetadata.ReadText(null), Is.Empty);
        Assert.Throws<ArgumentException>(() => PngMetadata.AddText(Png, "", "x"));
        Assert.Throws<ArgumentException>(() => PngMetadata.AddText(Png, new string('k', 80), "x"));
        // A truncated file doesn't throw.
        Assert.That(PngMetadata.ReadText(Png.Take(40).ToArray()), Is.Empty);
    }
}
