// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.LiteRT;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models;

/// <summary>A piece of text found on a page: four corners in the page's pixels (top left, top right, bottom right, bottom left), so a tilted line keeps its shape.</summary>
public sealed class TextQuad
{
    /// <summary>The four corners, clockwise from the top left.</summary>
    public PointF[] Points { get; }
    /// <summary>The detector's confidence, 0..1.</summary>
    public float Score { get; }

    /// <summary>Create one.</summary>
    /// <param name="points">Four corners</param>
    /// <param name="score">The confidence</param>
    public TextQuad(PointF[] points, float score)
    {
        if (points == null || points.Length != 4) throw new ArgumentException("Expected four corners.", nameof(points));
        Points = points;
        Score = score;
    }

    /// <summary>The corners' bounding rectangle.</summary>
    public RectangleF Bounds
    {
        get
        {
            float x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y);
            return new RectangleF(x0, y0, Points.Max(p => p.X) - x0, Points.Max(p => p.Y) - y0);
        }
    }

    /// <summary>The angle of the top edge in degrees (0 for a horizontal line, positive when it slopes down to the right).</summary>
    public double Angle => Math.Atan2(Points[1].Y - Points[0].Y, Points[1].X - Points[0].X) * 180 / Math.PI;

    /// <summary>The length of the top edge.</summary>
    public double Width => Math.Sqrt(Math.Pow(Points[1].X - Points[0].X, 2) + Math.Pow(Points[1].Y - Points[0].Y, 2));

    /// <summary>The length of the left edge.</summary>
    public double Height => Math.Sqrt(Math.Pow(Points[3].X - Points[0].X, 2) + Math.Pow(Points[3].Y - Points[0].Y, 2));
}

/// <summary>
/// Finds the lines of text on a photographed page with PaddleOCR's PP-OCRv6 Small detector (<see cref="PpOcrDetModel"/>) and
/// turns its probability map into quadrilaterals as PaddleOCR's DB post-processing does (threshold 0.2, minimum side 3,
/// box score 0.45, the rectangle grown by 1.4 times its area over its perimeter). The page is scaled to fit 640 x 640 (its
/// long side 640) and padded with white. Callers serialize <c>PrepareAsync</c>, <c>Detect</c> and <c>Dispose</c> off the UI thread.
/// </summary>
public sealed class PpOcrDetector : IDisposable
{
    internal const string Revision = "3e5c5c6367eca9ec18e5c49e4d6119f96f4b5ed6";
    internal const string Asset = "ppocrv6_small_det_640_fp32.tflite";
    internal const string ModelUrl = "https://huggingface.co/litert-community/PP-OCRv6-Small-LiteRT/resolve/" + Revision + "/" + Asset;
    internal const string AssetSha256 = "EC9430B6E592FFB28E83B6BAB539262A30D267D2475C136B193926AD83835768";

    /// <summary>The probability above which a pixel is text.</summary>
    public const float BitmapThreshold = 0.2f;
    /// <summary>The mean probability inside a box below which it is dropped.</summary>
    public const float BoxThreshold = 0.45f;
    /// <summary>How far a box is grown: area times this over perimeter.</summary>
    public const float UnclipRatio = 1.4f;
    /// <summary>Boxes whose shorter side (in the 640 x 640 map) is below this are dropped.</summary>
    public const float MinSide = 3;
    /// <summary>The most contours looked at.</summary>
    public const int MaxCandidates = 3000;

    private readonly string _cacheDirectory;
    private PpOcrDetModel _model;

    /// <summary>Creates the detector.</summary>
    /// <param name="cacheDirectory">Where the weights are downloaded to (default: a folder in the temp path).</param>
    public PpOcrDetector(string cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
    }

    /// <summary>Whether the detector is ready.</summary>
    public bool Imported => _model != null;

    /// <summary>Download (12.4 MB, once) and compile the detector.</summary>
    /// <param name="numThreads">CPU threads</param>
    /// <param name="progress">Called with what is happening</param>
    /// <param name="token">Cancels</param>
    public async Task PrepareAsync(int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(_cacheDirectory, Asset);
        var weights = new DownloadableFile(ModelUrl, "PpOcr", AssetSha256) { LocalFile = file };
        var downloads = new FileDownloadManager();
        downloads.AddFile(weights);
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading the text detector: {percent:0}%" : "Downloading the text detector…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The PP-OCR detector download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        progress?.Invoke("Preparing the text detector…");
        _model = await Task.Run(() => new PpOcrDetModel(file, numThreads), token).ConfigureAwait(false);
        progress?.Invoke("Text detector ready · LiteRT CPU");
    }

    /// <summary>Find the text lines of a page.</summary>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <returns>The lines in reading order (top to bottom, left to right within a row), in the page's pixels</returns>
    public TextQuad[] Detect(Mat bgr)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the text detector first.");
        double scale = WriteInput(_model, bgr);
        float[] map = _model.Run();
        return PostProcess(map, scale, bgr.Width, bgr.Height);
    }

    /// <summary>Scale the page to fit the square input (padded with white), normalize and write it as planar B, G, R.</summary>
    /// <param name="model">The detector graph</param>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <returns>The scale from the page to the input</returns>
    public static double WriteInput(PpOcrDetModel model, Mat bgr)
    {
        if (bgr == null || bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR page.", nameof(bgr));
        int size = PpOcrDetModel.InputSize;
        double scale = (double)size / Math.Max(bgr.Width, bgr.Height);
        int width = Math.Max(1, (int)Math.Round(bgr.Width * scale)), height = Math.Max(1, (int)Math.Round(bgr.Height * scale));
        using var canvas = new Mat(size, size, DepthType.Cv8U, 3);
        canvas.SetTo(new MCvScalar(255, 255, 255));
        using (var resized = new Mat())
        {
            CvInvoke.Resize(bgr, resized, new Size(width, height), 0, 0, Inter.Area);
            using var window = new Mat(canvas, new Rectangle(0, 0, width, height));
            resized.CopyTo(window);
        }
        using var normalized = new Mat();
        canvas.ConvertTo(normalized, DepthType.Cv32F, 1.0 / 255);
        using var mean = new Mat(size, size, DepthType.Cv32F, 3);
        mean.SetTo(new MCvScalar(0.485, 0.456, 0.406));        // B, G, R
        using var inverseStd = new Mat(size, size, DepthType.Cv32F, 3);
        inverseStd.SetTo(new MCvScalar(1 / 0.229, 1 / 0.224, 1 / 0.225));
        CvInvoke.Subtract(normalized, mean, normalized);
        CvInvoke.Multiply(normalized, inverseStd, normalized);
        IntPtr pixels = model.InputBuffer.Lock(TensorBufferLockMode.Write);
        try
        {
            using var planes = new VectorOfMat();
            CvInvoke.Split(normalized, planes);
            using var destination = new Mat(3 * size, size, DepthType.Cv32F, 1, pixels, size * sizeof(float));
            for (int channel = 0; channel < 3; channel++)
            {
                using var window = new Mat(destination, new Rectangle(0, channel * size, size, size));
                planes[channel].CopyTo(window);
            }
        }
        finally { model.InputBuffer.Unlock(); }
        return scale;
    }

    /// <summary>
    /// DB post-processing of the 640 x 640 probability map: threshold, contours, each contour's minimum-area rectangle (dropped
    /// when its short side is under <see cref="MinSide"/> or its mean probability under <see cref="BoxThreshold"/>), grown by
    /// <see cref="UnclipRatio"/>, mapped back to the page and ordered top to bottom.
    /// </summary>
    /// <param name="map">The map, row by row</param>
    /// <param name="scale">The scale from the page to the map (<see cref="WriteInput"/>)</param>
    /// <param name="pageWidth">The page's width in pixels</param>
    /// <param name="pageHeight">The page's height in pixels</param>
    public static TextQuad[] PostProcess(float[] map, double scale, int pageWidth, int pageHeight)
    {
        int size = PpOcrDetModel.InputSize;
        if (map == null || map.Length != size * size) throw new ArgumentException("Expected a 640 x 640 map.", nameof(map));
        var quads = new List<TextQuad>();
        var handle = GCHandle.Alloc(map, GCHandleType.Pinned);
        try
        {
            using var probability = new Mat(size, size, DepthType.Cv32F, 1, handle.AddrOfPinnedObject(), size * sizeof(float));
            using var binary32 = new Mat();
            CvInvoke.Threshold(probability, binary32, BitmapThreshold, 255, ThresholdType.Binary);
            using var binary = new Mat();
            binary32.ConvertTo(binary, DepthType.Cv8U);
            using var contours = new VectorOfVectorOfPoint();
            CvInvoke.FindContours(binary, contours, null, RetrType.List, ChainApproxMethod.ChainApproxSimple);
            for (int i = 0; i < Math.Min(contours.Size, MaxCandidates); i++)
            {
                using VectorOfPoint contour = contours[i];
                if (contour.Size < 4) continue;
                RotatedRect rectangle = CvInvoke.MinAreaRect(contour);
                if (Math.Min(rectangle.Size.Width, rectangle.Size.Height) < MinSide) continue;
                float score = BoxScore(probability, rectangle.GetVertices());
                if (score < BoxThreshold) continue;
                float width = rectangle.Size.Width, height = rectangle.Size.Height;
                float distance = width * height * UnclipRatio / (2 * (width + height));
                var grown = new RotatedRect(rectangle.Center, new SizeF(width + 2 * distance, height + 2 * distance), rectangle.Angle);
                if (Math.Min(grown.Size.Width, grown.Size.Height) < MinSide + 2) continue;
                PointF[] corners = Order(grown.GetVertices().Select(p => new PointF(
                    (float)Clamp(p.X / scale, 0, pageWidth - 1), (float)Clamp(p.Y / scale, 0, pageHeight - 1))).ToArray());
                quads.Add(new TextQuad(corners, score));
            }
        }
        finally { handle.Free(); }
        return quads.OrderBy(q => q.Points[0].Y).ThenBy(q => q.Points[0].X).ToArray();
    }

    // The mean probability inside the box (PaddleOCR's box_score_fast): the box filled into a mask over its bounding rectangle.
    private static float BoxScore(Mat probability, PointF[] vertices)
    {
        int size = probability.Width;
        int x0 = Clamp((int)Math.Floor(vertices.Min(p => p.X)), 0, size - 1), x1 = Clamp((int)Math.Ceiling(vertices.Max(p => p.X)), 0, size - 1);
        int y0 = Clamp((int)Math.Floor(vertices.Min(p => p.Y)), 0, size - 1), y1 = Clamp((int)Math.Ceiling(vertices.Max(p => p.Y)), 0, size - 1);
        var window = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        using var mask = new Mat(window.Height, window.Width, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));
        using var polygon = new VectorOfPoint(vertices.Select(p => new Point((int)Math.Round(p.X) - x0, (int)Math.Round(p.Y) - y0)).ToArray());
        CvInvoke.FillConvexPoly(mask, polygon, new MCvScalar(255));
        using var region = new Mat(probability, window);
        return (float)CvInvoke.Mean(region, mask).V0;
    }

    // Math.Clamp isn't in netstandard2.0.
    private static int Clamp(int value, int low, int high) => value < low ? low : value > high ? high : value;
    private static double Clamp(double value, double low, double high) => value < low ? low : value > high ? high : value;

    /// <summary>Order four corners clockwise from the top left (smallest x + y), as the quads are reported.</summary>
    /// <param name="corners">Four corners in any order</param>
    public static PointF[] Order(PointF[] corners)
    {
        if (corners == null || corners.Length != 4) throw new ArgumentException("Expected four corners.", nameof(corners));
        PointF topLeft = corners.OrderBy(p => p.X + p.Y).First();
        PointF bottomRight = corners.OrderByDescending(p => p.X + p.Y).First();
        PointF topRight = corners.OrderBy(p => p.Y - p.X).First();
        PointF bottomLeft = corners.OrderByDescending(p => p.Y - p.X).First();
        return new[] { topLeft, topRight, bottomRight, bottomLeft };
    }

    /// <summary>Release the graph.</summary>
    public void Dispose() { _model?.Dispose(); _model = null; }
}
