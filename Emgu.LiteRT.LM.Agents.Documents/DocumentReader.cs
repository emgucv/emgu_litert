//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

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

namespace Emgu.LiteRT.LM.Agents.Documents
{
    /// <summary>A passage of a document's text, with where it is ("page 3"; null for files without pages).</summary>
    public sealed class DocumentText
    {
        /// <summary>The text</summary>
        public string Text { get; set; }
        /// <summary>Where it is in the document ("page 3"), or null</summary>
        public string Location { get; set; }
    }

    /// <summary>A picture of a document, or the image of a page without text, as JPEG (or as the image itself, without an encoder).</summary>
    public sealed class DocumentPicture
    {
        /// <summary>The image</summary>
        public byte[] Jpeg { get; set; }
        /// <summary>Where it is ("page 3", "picture 2")</summary>
        public string Location { get; set; }
        /// <summary>True for the image of a whole page (a scan, a chart drawn as shapes), false for a picture in the document</summary>
        public bool IsPage { get; set; }
    }

    /// <summary>A document's text and images, as <see cref="DocumentReader"/> reads it.</summary>
    public sealed class DocumentContent
    {
        /// <summary>The file's name</summary>
        public string Name { get; set; }
        /// <summary>Pages of a PDF; 0 for other files</summary>
        public int PageCount { get; set; }
        /// <summary>The text, per page for a PDF</summary>
        public IList<DocumentText> Texts { get; } = new List<DocumentText>();
        /// <summary>Pictures in the document, and images of pages without text</summary>
        public IList<DocumentPicture> Images { get; } = new List<DocumentPicture>();
        /// <summary>Pages beyond <see cref="DocumentReadOptions.MaxPages"/> that weren't read</summary>
        public int PagesSkipped { get; set; }
        /// <summary>Images left out: over the limit, or in a format that couldn't be decoded</summary>
        public int ImagesSkipped { get; set; }
    }

    /// <summary>What <see cref="DocumentReader"/> reads, and how it makes images.</summary>
    public sealed class DocumentReadOptions
    {
        /// <summary>The pages of a PDF to read (<see cref="DocumentReader.DefaultMaxPages"/>): each page's passages take a model run to index.</summary>
        public int MaxPages { get; set; } = DocumentReader.DefaultMaxPages;
        /// <summary>The most pictures and page images to keep, 0 (the default) for none: each takes seconds to index on a phone.</summary>
        public int MaxImages { get; set; }
        /// <summary>The longer side of the images in pixels (1024, which vision models read)</summary>
        public int MaxImageSize { get; set; } = 1024;
        /// <summary>
        /// Decodes an image (PNG, JPEG, GIF, BMP...), scales it down to at most the given size and encodes it as JPEG; null if it can't be decoded.
        /// The MAUI demo uses Emgu CV. Without it, only JPEG pictures are kept, as they are.
        /// </summary>
        public Func<byte[], int, byte[]> EncodeImage { get; set; }
        /// <summary>Opens a PDF's page renderer (Emgu.LiteRT.LM.Maui's <c>PdfPageRenderer</c>), for images of pages without text; without it those
        /// pages give only their text.</summary>
        public Func<byte[], IPdfPageRenderer> OpenPdf { get; set; }
    }

    /// <summary>
    /// Reads a document for a chat's document questions: PDF (text and pictures with PdfPig, images of pages without text from the platform's PDF
    /// renderer), Word (.docx: its text and pictures) and text files. The text goes to Emgu.LiteRT.LM.Models' <c>DocumentIndex</c> as passages,
    /// the images (JPEG, at most <see cref="DocumentReadOptions.MaxImageSize"/> pixels) for vision models.
    /// </summary>
    public static class DocumentReader
    {
        /// <summary>The file types it reads, by extension: PDF, Word and text files (anything else is read as text, too)</summary>
        public static readonly IReadOnlyList<string> Extensions = new[] { ".pdf", ".docx", ".txt", ".md" };

        /// <summary>The pages of a PDF read by default; longer documents are cut</summary>
        public const int DefaultMaxPages = 150;

        // A PDF page with less text than this (letters and digits) is rendered as an image: a scan, a chart or a photo.
        private const int MinPageText = 80;
        // Pictures smaller than this (in pixels, or as a fraction of the page) are icons, logos or decorations.
        private const int MinPictureSize = 100;
        private const double MinPictureArea = 0.04;

        /// <summary>
        /// Read a document. Runs on a background thread.
        /// </summary>
        /// <param name="bytes">The file</param>
        /// <param name="fileName">Its name, whose extension tells its type</param>
        /// <param name="options">What to read and how to make images (the defaults: 150 pages, no images)</param>
        /// <param name="progress">Reports what is being read, or null</param>
        /// <param name="cancellationToken">Stops between pages</param>
        public static Task<DocumentContent> ReadAsync(byte[] bytes, string fileName, DocumentReadOptions options,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            options = options ?? new DocumentReadOptions();
            return Task.Run(async () =>
            {
                DocumentContent content = new DocumentContent { Name = fileName };
                switch (Path.GetExtension(fileName)?.ToLowerInvariant())
                {
                    case ".pdf":
                        await ReadPdfAsync(bytes, content, options, progress, cancellationToken).ConfigureAwait(false);
                        break;
                    case ".docx":
                        ReadDocx(bytes, content, options);
                        break;
                    default:
                        content.Texts.Add(new DocumentText { Text = DecodeText(bytes) });
                        break;
                }
                return content;
            }, cancellationToken);
        }

        private static async Task ReadPdfAsync(byte[] bytes, DocumentContent content, DocumentReadOptions options,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            int maxImages = options.MaxImages;
            // Pages without text (to render), and pictures: (page, area as a fraction of the page, JPEG).
            List<int> imagePages = new List<int>();
            List<(int Page, double Area, byte[] Jpeg)> pictures = new List<(int, double, byte[])>();
            using (UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(bytes))
            {
                content.PageCount = document.NumberOfPages;
                int pages = Math.Min(document.NumberOfPages, options.MaxPages);
                content.PagesSkipped = document.NumberOfPages - pages;
                for (int number = 1; number <= pages; number++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(string.Format("Reading page {0} of {1}...", number, pages));
                    Page page = document.GetPage(number);
                    string text = ContentOrderTextExtractor.GetText(page);
                    if (!string.IsNullOrWhiteSpace(text))
                        content.Texts.Add(new DocumentText { Text = text, Location = "page " + number });
                    if (maxImages <= 0)
                        continue;
                    if (text.Count(char.IsLetterOrDigit) < MinPageText && options.OpenPdf != null)
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
                        byte[] jpeg = ToJpeg(PictureBytes(image), options);
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
                using (IPdfPageRenderer renderer = options.OpenPdf(bytes))
                {
                    foreach (int number in imagePages.Take(renderCount))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(string.Format("Rendering page {0}...", number));
                        byte[] jpeg = null;
                        try
                        {
                            jpeg = ToJpeg(await renderer.RenderAsync(number - 1, options.MaxImageSize).ConfigureAwait(false), options);
                        }
                        catch (Exception)
                        {
                        }
                        if (jpeg == null)
                            content.ImagesSkipped++;
                        else
                            content.Images.Add(new DocumentPicture { Jpeg = jpeg, Location = "page " + number, IsPage = true });
                    }
                }
            }
            int pictureCount = Math.Min(pictures.Count, maxImages - content.Images.Count);
            content.ImagesSkipped += pictures.Count - Math.Max(0, pictureCount);
            if (pictureCount > 0)
                foreach (var picture in pictures.OrderByDescending(p => p.Area).Take(pictureCount).OrderBy(p => p.Page))
                    content.Images.Add(new DocumentPicture { Jpeg = picture.Jpeg, Location = "page " + picture.Page });
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
            return IsJpeg(raw) ? raw : null;
        }

        private static bool IsJpeg(byte[] image) => image != null && image.Length > 3 && image[0] == 0xFF && image[1] == 0xD8;

        // Through the encoder (decoded, scaled down, JPEG), or a JPEG as it is without one; null if the image can't be used.
        private static byte[] ToJpeg(byte[] image, DocumentReadOptions options)
        {
            if (image == null || image.Length == 0)
                return null;
            if (options.EncodeImage == null)
                return IsJpeg(image) ? image : null;
            return options.EncodeImage(image, options.MaxImageSize);
        }

        // Word: the paragraphs of word/document.xml (w:p, with w:t text, w:tab and w:br), and the pictures in
        // word/media - Word has no fixed pages, so there are no page numbers.
        private static void ReadDocx(byte[] bytes, DocumentContent content, DocumentReadOptions options)
        {
            int maxImages = options.MaxImages;
            using (MemoryStream stream = new MemoryStream(bytes))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
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
                    content.Texts.Add(new DocumentText { Text = text.ToString() });
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
                    byte[] jpeg;
                    using (Stream entryStream = entry.Open())
                    using (MemoryStream copy = new MemoryStream())
                    {
                        entryStream.CopyTo(copy);
                        jpeg = ToJpeg(copy.ToArray(), options);
                    }
                    if (jpeg == null)
                    {
                        content.ImagesSkipped++;
                        continue;
                    }
                    content.Images.Add(new DocumentPicture { Jpeg = jpeg, Location = "picture " + (++index) });
                }
                content.ImagesSkipped += Math.Max(0, media.Count - maxImages);
            }
        }

        // Text files: UTF-8 (with or without a byte order mark) or UTF-16 with one.
        private static string DecodeText(byte[] bytes)
        {
            using (StreamReader reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
                return reader.ReadToEnd();
        }
    }
}
