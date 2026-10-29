//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Documents;

namespace Emgu.LiteRT.LM.Maui;

/// <summary>
/// Renders PDF pages to images with the platform's own PDF renderer (Android's PdfRenderer, PDFKit on iOS and Mac
/// Catalyst, Windows.Data.Pdf on Windows), for pages whose content isn't text - scans, or charts drawn as shapes:
/// Emgu.LiteRT.LM.Agents.Documents' <c>DocumentReader</c> takes it as <c>DocumentReadOptions.OpenPdf = pdf => new PdfPageRenderer(pdf)</c>.
/// Open one per document; <see cref="RenderAsync"/> returns a JPEG about maxSize pixels on its longer side, or null if the
/// page can't be rendered. On Android the document is written to the cache folder while it is open.
/// </summary>
public sealed class PdfPageRenderer : IPdfPageRenderer
{
#if ANDROID
    private readonly string _path;
    private readonly Android.OS.ParcelFileDescriptor _descriptor;
    private readonly Android.Graphics.Pdf.PdfRenderer _renderer;

    // PdfRenderer reads a file: the document is written to the cache folder while it is open.
    /// <summary>Opens the document.</summary>
    /// <param name="pdf">The PDF file</param>
    public PdfPageRenderer(byte[] pdf)
    {
        _path = Path.Combine(FileSystem.CacheDirectory, "render_" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(_path, pdf);
        try
        {
            _descriptor = Android.OS.ParcelFileDescriptor.Open(new Java.IO.File(_path), Android.OS.ParcelFileMode.ReadOnly);
            _renderer = new Android.Graphics.Pdf.PdfRenderer(_descriptor);
        }
        catch
        {
            _descriptor?.Dispose();
            File.Delete(_path);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task<byte[]> RenderAsync(int pageIndex, int maxSize)
    {
        if (pageIndex < 0 || pageIndex >= _renderer.PageCount)
            return Task.FromResult<byte[]>(null);
        using Android.Graphics.Pdf.PdfRenderer.Page page = _renderer.OpenPage(pageIndex);
        // Page sizes are in points (1/72 inch).
        double scale = (double)maxSize / Math.Max(page.Width, page.Height);
        int width = Math.Max(1, (int)(page.Width * scale));
        int height = Math.Max(1, (int)(page.Height * scale));
        using Android.Graphics.Bitmap bitmap = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888);
        // Pages are transparent where nothing is drawn.
        bitmap.EraseColor(Android.Graphics.Color.White);
        page.Render(bitmap, null, null, Android.Graphics.Pdf.PdfRenderMode.ForDisplay);
        using MemoryStream stream = new MemoryStream();
        bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg, 90, stream);
        return Task.FromResult(stream.ToArray());
    }

    /// <summary>Closes the document.</summary>
    public void Dispose()
    {
        _renderer.Dispose();
        _descriptor.Dispose();
        try { File.Delete(_path); } catch (Exception) { }
    }
#elif IOS || MACCATALYST
    private readonly PdfKit.PdfDocument _document;

    /// <summary>Opens the document.</summary>
    /// <param name="pdf">The PDF file</param>
    public PdfPageRenderer(byte[] pdf)
    {
        _document = new PdfKit.PdfDocument(Foundation.NSData.FromArray(pdf));
    }

    /// <inheritdoc/>
    public Task<byte[]> RenderAsync(int pageIndex, int maxSize)
    {
        if (pageIndex < 0 || pageIndex >= (int)_document.PageCount)
            return Task.FromResult<byte[]>(null);
        PdfKit.PdfPage page = _document.GetPage((nint)pageIndex);
        if (page == null)
            return Task.FromResult<byte[]>(null);
        CoreGraphics.CGRect box = page.GetBoundsForBox(PdfKit.PdfDisplayBox.Media);
        // The page's rotation swaps its sides; the thumbnail is fitted into the size, white where nothing is drawn.
        bool rotated = page.Rotation % 180 != 0;
        double width = rotated ? box.Height : box.Width;
        double height = rotated ? box.Width : box.Height;
        // The thumbnail's size is in points; the image may have more pixels (the screen's scale).
        double scale = maxSize / Math.Max(width, height);
        using UIKit.UIImage image = page.GetThumbnail(new CoreGraphics.CGSize(width * scale, height * scale), PdfKit.PdfDisplayBox.Media);
        using Foundation.NSData jpeg = image?.AsJPEG(0.9f);
        return Task.FromResult(jpeg?.ToArray());
    }

    /// <summary>Closes the document.</summary>
    public void Dispose()
    {
        _document.Dispose();
    }
#elif WINDOWS
    private readonly byte[] _pdf;
    private Windows.Data.Pdf.PdfDocument _document;
    private double _outputScale = 1;
    private bool _outputScaleKnown;

    /// <summary>Opens the document.</summary>
    /// <param name="pdf">The PDF file</param>
    public PdfPageRenderer(byte[] pdf)
    {
        _pdf = pdf;
    }

    /// <inheritdoc/>
    public async Task<byte[]> RenderAsync(int pageIndex, int maxSize)
    {
        if (_document == null)
        {
            Windows.Storage.Streams.InMemoryRandomAccessStream input = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (Windows.Storage.Streams.DataWriter writer = new Windows.Storage.Streams.DataWriter(input))
            {
                writer.WriteBytes(_pdf);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            input.Seek(0);
            _document = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(input);
        }
        if (pageIndex < 0 || pageIndex >= (int)_document.PageCount)
            return null;
        using Windows.Data.Pdf.PdfPage page = _document.GetPage((uint)pageIndex);
        double scale = maxSize / Math.Max(page.Size.Width, page.Size.Height);
        using Windows.Storage.Streams.InMemoryRandomAccessStream output = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        // The renderer multiplies the destination size by the display's scale (150% gave a 1536 px image for 1024), so
        // the first page is measured and, if it came out the wrong size, drawn again with the correction.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Windows.Data.Pdf.PdfPageRenderOptions options = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale / _outputScale),
                DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale / _outputScale),
                BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId
            };
            output.Seek(0);
            output.Size = 0;
            await page.RenderToStreamAsync(output, options);
            if (_outputScaleKnown)
                break;
            output.Seek(0);
            Windows.Graphics.Imaging.BitmapDecoder decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(output);
            double expected = Math.Max(1, Math.Max(page.Size.Width, page.Size.Height) * scale / _outputScale);
            double actual = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            _outputScaleKnown = true;
            if (Math.Abs(actual - expected) <= 1)
                break;
            _outputScale *= actual / expected;
        }
        byte[] bytes = new byte[output.Size];
        using (Windows.Storage.Streams.DataReader reader = new Windows.Storage.Streams.DataReader(output.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)output.Size);
            reader.ReadBytes(bytes);
        }
        return bytes;
    }

    /// <summary>Closes the document.</summary>
    public void Dispose()
    {
        _document = null;
    }
#endif
}
