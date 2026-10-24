//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

#if WINDOWS || IOS || ANDROID || MACCATALYST

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// A document's text and images, as read by DocumentReader.
    /// </summary>
    internal sealed class DocumentContent
    {
        public string Name;
        // Pages of a PDF; 0 for other files.
        public int PageCount;
        // The text, per page for a PDF ("page 3"; null location for other files).
        public List<(string Text, string Location)> Texts = new List<(string, string)>();
        // JPEG images: pictures in the document, and images of pages without text (IsPage).
        public List<(byte[] Jpeg, string Location, bool IsPage)> Images = new List<(byte[], string, bool)>();
        // Pages beyond MaxPages that weren't read, and images left out (over the limit, or in a format that couldn't
        // be decoded).
        public int PagesSkipped;
        public int ImagesSkipped;
    }

    /// <summary>
    /// Reads a document for the chat's document questions: PDF (text and pictures with PdfPig, images of pages without
    /// text from the platform's PDF renderer), Word (.docx: its text and pictures) and text files. Pictures and page
    /// images are re-encoded as JPEG at most MaxImageSize pixels, which the vision models read.
    /// </summary>
    internal static class DocumentReader
    {
        public static readonly FilePickerFileType FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "public.plain-text" } },
            { DevicePlatform.MacCatalyst, new[] { "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "public.plain-text" } },
            { DevicePlatform.Android, new[] { "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain", "text/markdown" } },
            { DevicePlatform.WinUI, new[] { ".pdf", ".docx", ".txt", ".md" } },
        });

        // Longer documents are cut: each page's passages take a model run to index.
        public const int MaxPages = 150;
        // A PDF page with less text than this (letters and digits) is rendered as an image: a scan, a chart or a photo.
        private const int MinPageText = 80;
        // Pictures smaller than this (in pixels, or as a fraction of the page) are icons, logos or decorations.
        private const int MinPictureSize = 100;
        private const double MinPictureArea = 0.04;
        private const int MaxImageSize = 1024;

        /// <summary>
        /// Read a document. Runs on a background thread.
        /// </summary>
        /// <param name="bytes">The file</param>
        /// <param name="fileName">Its name, whose extension tells its type</param>
        /// <param name="maxImages">The most pictures and page images to keep (0 for none): each takes seconds to index on a phone</param>
        /// <param name="progress">Reports what is being read, or null</param>
        /// <param name="cancellationToken">Stops between pages</param>
        public static Task<DocumentContent> ReadAsync(byte[] bytes, string fileName, int maxImages,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            return Task.Run(async () =>
            {
                DocumentContent content = new DocumentContent { Name = fileName };
                switch (Path.GetExtension(fileName)?.ToLowerInvariant())
                {
                    case ".pdf":
                        await ReadPdfAsync(bytes, content, maxImages, progress, cancellationToken);
                        break;
                    case ".docx":
                        ReadDocx(bytes, content, maxImages);
                        break;
                    default:
                        content.Texts.Add((DecodeText(bytes), null));
                        break;
                }
                return content;
            }, cancellationToken);
        }

        private static async Task ReadPdfAsync(byte[] bytes, DocumentContent content, int maxImages,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            // Pages without text (to render), and pictures: (page, area as a fraction of the page, JPEG).
            List<int> imagePages = new List<int>();
            List<(int Page, double Area, byte[] Jpeg)> pictures = new List<(int, double, byte[])>();
            using (UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(bytes))
            {
                content.PageCount = document.NumberOfPages;
                int pages = Math.Min(document.NumberOfPages, MaxPages);
                content.PagesSkipped = document.NumberOfPages - pages;
                for (int number = 1; number <= pages; number++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(string.Format("Reading page {0} of {1}...", number, pages));
                    UglyToad.PdfPig.Content.Page page = document.GetPage(number);
                    string text = ContentOrderTextExtractor.GetText(page);
                    if (!string.IsNullOrWhiteSpace(text))
                        content.Texts.Add((text, "page " + number));
                    if (maxImages <= 0)
                        continue;
                    if (text.Count(char.IsLetterOrDigit) < MinPageText)
                    {
                        // The page is rendered whole, which includes its pictures.
                        imagePages.Add(number);
                        continue;
                    }
                    double pageArea = Math.Max(1, page.Width * page.Height);
                    foreach (IPdfImage image in page.GetImages())
                    {
                        if (image.IsImageMask || image.WidthInSamples < MinPictureSize || image.HeightInSamples < MinPictureSize)
                            continue;
                        double area = Math.Abs(image.Bounds.Width * image.Bounds.Height) / pageArea;
                        if (area < MinPictureArea)
                            continue;
                        byte[] jpeg = ToJpeg(PictureBytes(image));
                        if (jpeg == null)
                            content.ImagesSkipped++;
                        else
                            pictures.Add((number, area, jpeg));
                    }
                }
            }

            // Page images first (they are all there is of those pages), then the largest pictures, in page order.
            int renderCount = Math.Min(imagePages.Count, maxImages);
            content.ImagesSkipped += imagePages.Count - renderCount;
            if (renderCount > 0)
            {
                using PdfPageRenderer renderer = new PdfPageRenderer(bytes);
                foreach (int number in imagePages.Take(renderCount))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(string.Format("Rendering page {0}...", number));
                    byte[] jpeg = null;
                    try
                    {
                        jpeg = ToJpeg(await renderer.RenderAsync(number - 1, MaxImageSize));
                    }
                    catch (Exception)
                    {
                    }
                    if (jpeg == null)
                        content.ImagesSkipped++;
                    else
                        content.Images.Add((jpeg, "page " + number, true));
                }
            }
            int pictureCount = Math.Min(pictures.Count, maxImages - content.Images.Count);
            content.ImagesSkipped += pictures.Count - Math.Max(0, pictureCount);
            if (pictureCount > 0)
                foreach (var picture in pictures.OrderByDescending(p => p.Area).Take(pictureCount).OrderBy(p => p.Page))
                    content.Images.Add((picture.Jpeg, "page " + picture.Page, false));
        }

        // A picture as PNG (PdfPig decodes most images) or, for JPEG images it can't convert, the JPEG itself.
        private static byte[] PictureBytes(IPdfImage image)
        {
            try
            {
                if (image.TryGetPng(out byte[] png))
                    return png;
            }
            catch (Exception)
            {
            }
            byte[] raw = image.RawMemory.ToArray();
            return raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 ? raw : null;
        }

        // Decode (OpenCV), downscale and re-encode as JPEG; null if the image can't be decoded.
        private static byte[] ToJpeg(byte[] image)
        {
            if (image == null || image.Length == 0)
                return null;
            Emgu.CV.Mat mat = ImageUtil.Decode(image);
            if (mat == null)
                return null;
            using Emgu.CV.Mat small = ImageUtil.Downscale(mat, MaxImageSize);
            using Emgu.CV.Util.VectorOfByte buffer = new Emgu.CV.Util.VectorOfByte();
            Emgu.CV.CvInvoke.Imencode(".jpg", small, buffer);
            return buffer.ToArray();
        }

        // Word: the paragraphs of word/document.xml (w:p, with w:t text, w:tab and w:br), and the pictures in
        // word/media - Word has no fixed pages, so there are no page numbers.
        private static void ReadDocx(byte[] bytes, DocumentContent content, int maxImages)
        {
            using MemoryStream stream = new MemoryStream(bytes);
            using ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read);
            ZipArchiveEntry body = zip.GetEntry("word/document.xml");
            if (body != null)
            {
                StringBuilder text = new StringBuilder();
                using (Stream xml = body.Open())
                using (XmlReader reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                {
                    const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                    // ReadElementContentAsString leaves the reader on the next node, which must not be skipped.
                    bool onNextNode = false;
                    while (onNextNode || reader.Read())
                    {
                        onNextNode = false;
                        if (reader.NamespaceURI != WordNamespace)
                            continue;
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            if (reader.LocalName == "t" && !reader.IsEmptyElement)
                            {
                                text.Append(reader.ReadElementContentAsString());
                                onNextNode = true;
                            }
                            else if (reader.LocalName == "tab")
                                text.Append(' ');
                            else if (reader.LocalName == "br" || reader.LocalName == "cr")
                                text.Append('\n');
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                        {
                            // Paragraphs are separated by empty lines, so headings stay apart from the text.
                            text.Append("\n\n");
                        }
                    }
                }
                content.Texts.Add((text.ToString(), null));
            }
            if (maxImages <= 0)
                return;
            string[] pictureTypes = { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
            List<ZipArchiveEntry> media = zip.Entries
                .Where(e => e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                    && pictureTypes.Contains(Path.GetExtension(e.Name).ToLowerInvariant()))
                .ToList();
            int index = 0;
            foreach (ZipArchiveEntry entry in media.OrderByDescending(e => e.Length).Take(maxImages).OrderBy(e => e.FullName))
            {
                using Stream entryStream = entry.Open();
                using MemoryStream copy = new MemoryStream();
                entryStream.CopyTo(copy);
                byte[] jpeg = ToJpeg(copy.ToArray());
                if (jpeg == null)
                {
                    content.ImagesSkipped++;
                    continue;
                }
                content.Images.Add((jpeg, "picture " + (++index), false));
            }
            content.ImagesSkipped += Math.Max(0, media.Count - maxImages);
        }

        // Text files: UTF-8 (with or without a byte order mark) or UTF-16 with one.
        private static string DecodeText(byte[] bytes)
        {
            using StreamReader reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
    }
}

#endif
