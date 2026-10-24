//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;

namespace Maui.Demo.Lite
{
    /// <summary>
    /// Renders PDF pages to images with the platform's own PDF renderer (Android's PdfRenderer, PDFKit on iOS and Mac
    /// Catalyst, Windows.Data.Pdf on Windows), for pages whose content isn't text - scans, or charts drawn as shapes.
    /// Open one per document; RenderAsync returns an encoded image (JPEG or PNG) about maxSize pixels on its longer
    /// side, or null if the page can't be rendered.
    /// </summary>
    internal sealed class PdfPageRenderer : IDisposable
    {
#if ANDROID
        private readonly string _path;
        private readonly Android.OS.ParcelFileDescriptor _descriptor;
        private readonly Android.Graphics.Pdf.PdfRenderer _renderer;

        // PdfRenderer reads a file: the document is written to the cache folder while it is open.
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

        public void Dispose()
        {
            _renderer.Dispose();
            _descriptor.Dispose();
            try { File.Delete(_path); } catch (Exception) { }
        }
#elif IOS || MACCATALYST
        private readonly PdfKit.PdfDocument _document;

        public PdfPageRenderer(byte[] pdf)
        {
            _document = new PdfKit.PdfDocument(Foundation.NSData.FromArray(pdf));
        }

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

        public void Dispose()
        {
            _document.Dispose();
        }
#elif WINDOWS
        private readonly byte[] _pdf;
        private Windows.Data.Pdf.PdfDocument _document;

        public PdfPageRenderer(byte[] pdf)
        {
            _pdf = pdf;
        }

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
            Windows.Data.Pdf.PdfPageRenderOptions options = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, page.Size.Width * scale),
                DestinationHeight = (uint)Math.Max(1, page.Size.Height * scale),
                BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId
            };
            using Windows.Storage.Streams.InMemoryRandomAccessStream output = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, options);
            byte[] bytes = new byte[output.Size];
            using (Windows.Storage.Streams.DataReader reader = new Windows.Storage.Streams.DataReader(output.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)output.Size);
                reader.ReadBytes(bytes);
            }
            return bytes;
        }

        public void Dispose()
        {
            _document = null;
        }
#endif
    }
}
