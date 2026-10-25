// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Threading.Tasks;

namespace Emgu.LiteRT.LM.Agents.Documents
{
    /// <summary>
    /// Renders the pages of one PDF document to images, for pages whose content isn't text - scans, or charts drawn as shapes - which a vision
    /// model can then read. A platform supplies it: Emgu.LiteRT.LM.Maui's <c>PdfPageRenderer</c> uses Android's PdfRenderer, PDFKit on iOS and Mac
    /// Catalyst and Windows.Data.Pdf on Windows. Emgu.LiteRT.LM.Agents.Documents' <c>DocumentReader</c> opens one per document and disposes it.
    /// </summary>
    public interface IPdfPageRenderer : IDisposable
    {
        /// <summary>The page as an encoded image (JPEG or PNG), about <paramref name="maxSize"/> pixels on its longer side, white where nothing is
        /// drawn; null if it can't be rendered or there is no such page.</summary>
        /// <param name="pageIndex">The page, from 0</param>
        /// <param name="maxSize">The image's longer side in pixels</param>
        Task<byte[]> RenderAsync(int pageIndex, int maxSize);
    }
}
