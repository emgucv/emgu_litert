// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.LiteRT;
using Emgu.LiteRT.Util;

namespace Emgu.LiteRT.Models;

/// <summary>
/// Reads text with PaddleOCR's PP-OCRv6 Small recognizer (<see cref="PpOcrRecModel"/>, the 320-wide graph: about 6.7 times
/// as wide as high). A line of a page is longer than that, so <see cref="RecognizeStart"/> reads the first few characters
/// of one (enough for a question's number or a part's letter, "1.", "12)", "a)"). Callers serialize <c>PrepareAsync</c>,
/// <c>Recognize*</c> and <c>Dispose</c> off the UI thread.
/// </summary>
public sealed class PpOcrRecognizer : IDisposable
{
    internal const string RecognizerAsset = "ppocrv6_small_rec_320_fp32.tflite";
    internal const string RecognizerUrl = "https://huggingface.co/litert-community/PP-OCRv6-Small-LiteRT/resolve/" + PpOcrDetector.Revision + "/" + RecognizerAsset;
    internal const string RecognizerSha256 = "569BAE77214A57476106AE66996F4BA4EF3AC66F81D150966C44E31A8FB0AE36";
    internal const string CharactersAsset = "characters.json";
    internal const string CharactersUrl = "https://huggingface.co/litert-community/PP-OCRv6-Small-LiteRT/resolve/" + PpOcrDetector.Revision + "/" + CharactersAsset;
    internal const string CharactersSha256 = "9C89DB8AA95AD30C83EE305D53D366C6BBA3F85FF2EAB0375EF345FD50B0674A";

    private readonly string _cacheDirectory;
    private PpOcrRecModel _model;
    private string[] _characters;

    /// <summary>Creates the recognizer.</summary>
    /// <param name="cacheDirectory">Where the files are downloaded to (default: a folder in the temp path).</param>
    public PpOcrRecognizer(string cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? DetectorDefaults.CacheDirectory;
    }

    /// <summary>Whether the recognizer is ready.</summary>
    public bool Imported => _model != null;

    /// <summary>Download (21.5 MB and a 168 KB dictionary, once) and compile the recognizer.</summary>
    /// <param name="numThreads">CPU threads</param>
    /// <param name="progress">Called with what is happening</param>
    /// <param name="token">Cancels</param>
    public async Task PrepareAsync(int numThreads, Action<string> progress, CancellationToken token)
    {
        if (Imported) return;
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(_cacheDirectory, RecognizerAsset), characters = Path.Combine(_cacheDirectory, CharactersAsset);
        var downloads = new FileDownloadManager();
        downloads.AddFile(new DownloadableFile(RecognizerUrl, "PpOcr", RecognizerSha256) { LocalFile = file });
        downloads.AddFile(new DownloadableFile(CharactersUrl, "PpOcr", CharactersSha256) { LocalFile = characters });
        downloads.OnDownloadProgressChanged += (_, _, percent) => progress?.Invoke(percent.HasValue
            ? $"Downloading the text recognizer: {percent:0}%" : "Downloading the text recognizer…");
        await downloads.Download(1, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!downloads.AllFilesDownloaded)
            throw new IOException("The PP-OCR recognizer download failed or did not match its SHA-256 checksum. Check the connection and retry.");
        progress?.Invoke("Preparing the text recognizer…");
        _characters = PpOcrRecModel.LoadCharacters(characters);
        _model = await Task.Run(() => new PpOcrRecModel(file, 320, numThreads), token).ConfigureAwait(false);
        progress?.Invoke("Text recognizer ready · LiteRT CPU");
    }

    /// <summary>
    /// Read the start of a text line: its first <paramref name="heights"/> line heights, and <paramref name="leftHeights"/> more
    /// to the left of it (a question's number is often a box of its own that the detector leaves out of the line), in all at
    /// most 6 line heights, what the 320-wide graph holds. The part is warped upright from the line's four corners, so a tilted
    /// line is read straight.
    /// </summary>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <param name="quad">The line</param>
    /// <param name="heights">How many line heights from its left end to read</param>
    /// <param name="leftHeights">How many line heights to read to the left of its left end</param>
    /// <param name="score">The recognizer's confidence, 0..1</param>
    public string RecognizeStart(Mat bgr, TextQuad quad, double heights, double leftHeights, out float score)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the text recognizer first.");
        double width = quad.Width, height = quad.Height;
        leftHeights = Math.Max(0, Math.Min(leftHeights, 5));
        heights = Math.Max(1, Math.Min(heights, 6 - leftHeights));
        // Fractions of the top and bottom edges: from -leftHeights line heights (outside the line) to the right part.
        double from = width <= 0 ? 0 : -leftHeights * height / width, to = width <= 0 ? 1 : Math.Min(1, heights * height / width);
        PointF[] c = quad.Points;
        PointF At(PointF a, PointF b, double t) => new PointF((float)(a.X + t * (b.X - a.X)), (float)(a.Y + t * (b.Y - a.Y)));
        return Recognize(bgr, new[] { At(c[0], c[1], from), At(c[0], c[1], to), At(c[3], c[2], to), At(c[3], c[2], from) }, out score);
    }

    /// <summary>
    /// Read a whole text line. The 320-wide graph holds about 6 line heights, so a longer line is read in pieces of that length
    /// and the texts are joined (a character cut at a seam can be misread: this is for short lines, like a handwritten answer,
    /// and as a hint for long ones).
    /// </summary>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <param name="quad">The line</param>
    /// <param name="score">The mean confidence of the pieces, 0..1</param>
    /// <param name="maxPieces">The most pieces to read; a longer line is read up to there</param>
    public string RecognizeLine(Mat bgr, TextQuad quad, out float score, int maxPieces = 8)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the text recognizer first.");
        double ratio = quad.Height <= 0 ? 1 : quad.Width / quad.Height;
        int pieces = Math.Max(1, Math.Min(maxPieces, (int)Math.Ceiling(ratio / 6.0)));
        PointF[] c = quad.Points;
        PointF At(PointF a, PointF b, double t) => new PointF((float)(a.X + t * (b.X - a.X)), (float)(a.Y + t * (b.Y - a.Y)));
        var text = new System.Text.StringBuilder();
        float total = 0;
        for (int i = 0; i < pieces; i++)
        {
            double from = (double)i / pieces, to = (double)(i + 1) / pieces;
            text.Append(Recognize(bgr, new[] { At(c[0], c[1], from), At(c[0], c[1], to), At(c[3], c[2], to), At(c[3], c[2], from) }, out float pieceScore));
            total += pieceScore;
        }
        score = total / pieces;
        return text.ToString();
    }

    /// <summary>Read the start of a text line (its first <paramref name="heights"/> line heights, at most 6).</summary>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <param name="quad">The line</param>
    /// <param name="heights">How many line heights from its left end to read</param>
    /// <param name="score">The recognizer's confidence, 0..1</param>
    public string RecognizeStart(Mat bgr, TextQuad quad, double heights, out float score) => RecognizeStart(bgr, quad, heights, 0, out score);

    /// <summary>Read a quadrilateral (top left, top right, bottom right, bottom left; at most 6.6 times as wide as high).</summary>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <param name="corners">Its four corners</param>
    /// <param name="score">The recognizer's confidence, 0..1</param>
    public string Recognize(Mat bgr, PointF[] corners, out float score)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the text recognizer first.");
        if (bgr == null || bgr.IsEmpty || bgr.Depth != DepthType.Cv8U || bgr.NumberOfChannels != 3)
            throw new ArgumentException("Expected a nonempty 8-bit BGR page.", nameof(bgr));
        int cropWidth = (int)Math.Max(Distance(corners[0], corners[1]), Distance(corners[3], corners[2]));
        int cropHeight = (int)Math.Max(Distance(corners[0], corners[3]), Distance(corners[1], corners[2]));
        score = 0;
        if (cropWidth < 1 || cropHeight < 1) return string.Empty;
        using var upright = new Mat();
        using (var from = new Emgu.CV.Util.VectorOfPointF(corners))
        using (var to = new Emgu.CV.Util.VectorOfPointF(new[] { new PointF(0, 0), new PointF(cropWidth, 0), new PointF(cropWidth, cropHeight), new PointF(0, cropHeight) }))
        using (var transform = CvInvoke.GetPerspectiveTransform(from, to))
            CvInvoke.WarpPerspective(bgr, upright, transform, new Size(cropWidth, cropHeight), Inter.Cubic, Warp.Default, BorderType.Replicate);
        int width = Math.Min(_model.Width, Math.Max(1, (int)Math.Ceiling(PpOcrRecModel.InputHeight * (double)cropWidth / cropHeight)));
        using var resized = new Mat();
        CvInvoke.Resize(upright, resized, new Size(width, PpOcrRecModel.InputHeight), 0, 0, Inter.Linear);
        using var normalized = new Mat();
        resized.ConvertTo(normalized, DepthType.Cv32F, 1 / 127.5, -1);
        IntPtr pixels = _model.InputBuffer.Lock(TensorBufferLockMode.Write);
        try
        {
            int total = _model.Width, rows = PpOcrRecModel.InputHeight;
            using var destination = new Mat(3 * rows, total, DepthType.Cv32F, 1, pixels, total * sizeof(float));
            destination.SetTo(new MCvScalar(0));   // the padding is normalized zero
            using var planes = new Emgu.CV.Util.VectorOfMat();
            CvInvoke.Split(normalized, planes);
            for (int channel = 0; channel < 3; channel++)
            {
                using var window = new Mat(destination, new Rectangle(0, channel * rows, width, rows));
                planes[channel].CopyTo(window);
            }
        }
        finally { _model.InputBuffer.Unlock(); }
        return PpOcrRecModel.Decode(_model.Run(), _model.Steps, _characters, out score);
    }

    private static double Distance(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Release the graph.</summary>
    public void Dispose() { _model?.Dispose(); _model = null; }
}
