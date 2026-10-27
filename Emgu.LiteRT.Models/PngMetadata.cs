// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Emgu.LiteRT.Models;

/// <summary>
/// Text in a PNG file: the generated pictures carry their description and seed (iTXt chunks, UTF-8), so a picture shows
/// them again when a saved chat is opened - the picture is all that is kept - and a saved or shared file knows how it was
/// made (<see cref="PngEncoder"/> writes the picture itself).
/// </summary>
public static class PngMetadata
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static uint[] _crcTable;
    // ISO-8859-1, which PNG keywords use (Encoding.Latin1 is .NET 5+; this code page is built in everywhere).
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    /// <summary>The PNG with one more text chunk (before IEND). Returns the PNG unchanged if it isn't a PNG.</summary>
    public static byte[] AddText(byte[] png, string keyword, string text)
    {
        if (png == null || png.Length < 20 || !StartsWithSignature(png))
            return png;
        if (string.IsNullOrEmpty(keyword) || keyword.Length > 79 || keyword.IndexOf('\0') >= 0)
            throw new ArgumentException("A PNG keyword is 1-79 characters.", nameof(keyword));
        int iend = FindChunk(png, "IEND");
        if (iend < 0)
            return png;
        var data = new List<byte>();
        data.AddRange(Latin1.GetBytes(keyword));
        data.Add(0); // end of keyword
        data.Add(0); // not compressed
        data.Add(0); // compression method
        data.Add(0); // no language tag
        data.Add(0); // no translated keyword
        data.AddRange(Encoding.UTF8.GetBytes(text ?? string.Empty));
        using var output = new MemoryStream();
        output.Write(png, 0, iend);
        WriteChunk(output, "iTXt", data.ToArray());
        output.Write(png, iend, png.Length - iend);
        return output.ToArray();
    }

    /// <summary>The text of the file's iTXt (uncompressed) and tEXt chunks by keyword; the first of a keyword wins.</summary>
    public static IReadOnlyDictionary<string, string> ReadText(byte[] png)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (png == null || png.Length < 20 || !StartsWithSignature(png))
            return result;
        int position = Signature.Length;
        while (position + 12 <= png.Length)
        {
            int length = ReadInt(png, position);
            if (length < 0 || position + 12L + length > png.Length)
                break;
            string type = Encoding.ASCII.GetString(png, position + 4, 4);
            int start = position + 8, end = start + length;
            if (type == "tEXt" || type == "iTXt")
            {
                int zero = Array.IndexOf(png, (byte)0, start, length);
                if (zero > start)
                {
                    string keyword = Latin1.GetString(png, start, zero - start);
                    string text = null;
                    if (type == "tEXt")
                        text = Latin1.GetString(png, zero + 1, end - zero - 1);
                    else if (zero + 2 < end && png[zero + 1] == 0) // not compressed
                    {
                        int p = zero + 3; // after the flag and the method
                        p = Array.IndexOf(png, (byte)0, p, end - p); // language tag
                        if (p >= 0)
                            p = Array.IndexOf(png, (byte)0, p + 1, end - p - 1); // translated keyword
                        if (p >= 0)
                            text = Encoding.UTF8.GetString(png, p + 1, end - p - 1);
                    }
                    if (text != null && !result.ContainsKey(keyword))
                        result[keyword] = text;
                }
            }
            if (type == "IEND")
                break;
            position = end + 4;
        }
        return result;
    }

    private static bool StartsWithSignature(byte[] png)
    {
        for (int i = 0; i < Signature.Length; i++)
            if (png[i] != Signature[i])
                return false;
        return true;
    }

    // The offset of the first chunk of this type.
    private static int FindChunk(byte[] png, string type)
    {
        int position = Signature.Length;
        while (position + 12 <= png.Length)
        {
            int length = ReadInt(png, position);
            if (length < 0 || position + 12L + length > png.Length)
                return -1;
            if (Encoding.ASCII.GetString(png, position + 4, 4) == type)
                return position;
            position += 12 + length;
        }
        return -1;
    }

    private static int ReadInt(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        WriteInt(output, data.Length);
        output.Write(name, 0, 4);
        output.Write(data, 0, data.Length);
        uint crc = 0xFFFFFFFF;
        crc = Update(crc, name);
        crc = Update(crc, data);
        WriteInt(output, (int)(crc ^ 0xFFFFFFFF));
    }

    private static void WriteInt(Stream output, int value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static uint Update(uint crc, byte[] data)
    {
        uint[] table = _crcTable;
        if (table == null)
        {
            table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            _crcTable = table;
        }
        foreach (byte b in data)
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
