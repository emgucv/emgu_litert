// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Agents;

/// <summary>One question, or part of a question, cut out of a homework page.</summary>
public sealed class HomeworkPageUnit
{
    /// <summary>Create one.</summary>
    /// <param name="unit">What the layout found</param>
    /// <param name="crop">The unit's picture (JPEG): its printed text and the handwriting next to it</param>
    /// <param name="parentCrop">The whole question's picture (JPEG), the same as the crop for a question without parts</param>
    /// <param name="text">What the recognizer read on each line of the unit, in reading order: a hint for telling the student's handwriting from the printed question</param>
    public HomeworkPageUnit(HomeworkUnit unit, byte[] crop, byte[] parentCrop, IReadOnlyList<string> text = null)
    {
        Unit = unit;
        Crop = crop;
        ParentCrop = parentCrop;
        Text = text ?? new List<string>();
    }

    /// <summary>The text the recognizer read on each line of the unit, in reading order (it can make mistakes).</summary>
    public IReadOnlyList<string> Text { get; }

    /// <summary>The layout's unit (label, lines).</summary>
    public HomeworkUnit Unit { get; }
    /// <summary>"3" or "2a".</summary>
    public string Label => Unit.Label;
    /// <summary>The unit's picture (JPEG).</summary>
    public byte[] Crop { get; }
    /// <summary>The whole question's picture (JPEG).</summary>
    public byte[] ParentCrop { get; }
}

/// <summary>
/// Cuts a photographed homework page into one picture per question (or part): PaddleOCR's PP-OCRv6 Small finds the text
/// lines as four-point quadrilaterals (<see cref="PpOcrDetector"/>), the recognizer reads the start of each line for the
/// question numbers and part letters (<see cref="PpOcrRecognizer"/>), <see cref="HomeworkLayout"/> groups the lines, and
/// <see cref="PpOcrCrop"/> warps each group upright. About 34 MB of models, downloaded once; the CPU only. Callers
/// serialize <c>PrepareAsync</c>, <c>Segment</c> and <c>Dispose</c> off the UI thread.
/// </summary>
public sealed class HomeworkSegmenter : IDisposable
{
    /// <summary>A line the recognizer is less sure of than this is marked "[unsure]" in a unit's text.</summary>
    public const float UnsureBelow = 0.9f;

    private readonly PpOcrDetector _detector;
    private readonly PpOcrRecognizer _recognizer;

    /// <summary>Creates the segmenter.</summary>
    /// <param name="cacheDirectory">Where the models are downloaded to (default: a folder in the temp path)</param>
    public HomeworkSegmenter(string cacheDirectory = null)
    {
        _detector = new PpOcrDetector(cacheDirectory);
        _recognizer = new PpOcrRecognizer(cacheDirectory);
    }

    /// <summary>Whether the models are ready.</summary>
    public bool Imported => _detector.Imported && _recognizer.Imported;

    /// <summary>Download (once) and compile the detector and the recognizer.</summary>
    /// <param name="numThreads">CPU threads</param>
    /// <param name="progress">Called with what is happening</param>
    /// <param name="token">Cancels</param>
    public async Task PrepareAsync(int numThreads, Action<string> progress, CancellationToken token)
    {
        await _detector.PrepareAsync(numThreads, progress, token).ConfigureAwait(false);
        await _recognizer.PrepareAsync(numThreads, progress, token).ConfigureAwait(false);
    }

    /// <summary>Cut a page into its questions.</summary>
    /// <param name="image">The page (JPEG or PNG)</param>
    /// <param name="minLines">The fewest text lines for a page to be cut; fewer (a photo with no text) gives no units</param>
    /// <returns>The questions and parts in reading order; empty when the page has no numbered questions</returns>
    public IReadOnlyList<HomeworkPageUnit> Segment(byte[] image, int minLines = 4)
    {
        if (!Imported) throw new InvalidOperationException("Prepare the segmenter first.");
        using var page = new Mat();
        CvInvoke.Imdecode(image, ImreadModes.ColorBgr, page);
        if (page.IsEmpty)
            return new List<HomeworkPageUnit>();
        TextQuad[] quads = _detector.Detect(page);
        if (quads.Length < minLines)
            return new List<HomeworkPageUnit>();
        var lines = new List<LayoutLine>();
        var texts = new string[quads.Length];
        var scores = new float[quads.Length];
        for (int i = 0; i < quads.Length; i++)
        {
            RectangleF box = quads[i].Bounds;
            string plain = _recognizer.RecognizeStart(page, quads[i], 6, 0, out float plainScore);
            // A question's number is often a box the detector leaves out of the line: read a little to the left of the
            // lines at the margin too.
            string left = box.X < 0.3 * page.Width ? _recognizer.RecognizeStart(page, quads[i], 3, 3, out _) : null;
            lines.Add(new LayoutLine(i, box.X, box.Y, box.Width, box.Height, plain, left));
            // The whole line, for the hint; a line that is short enough was just read.
            if (quads[i].Width <= 6 * quads[i].Height)
            {
                texts[i] = plain;
                scores[i] = plainScore;
            }
            else
                texts[i] = _recognizer.RecognizeLine(page, quads[i], out scores[i]);
        }
        IReadOnlyList<HomeworkUnit> units = HomeworkLayout.Segment(lines, page.Width, page.Height);
        var result = new List<HomeworkPageUnit>();
        var parents = new Dictionary<string, byte[]>();
        foreach (HomeworkUnit unit in units)
        {
            byte[] crop = Encode(page, unit.Lines.Select(i => quads[i]).ToList(), unit.SlotLeft, unit.SlotRight);
            if (crop == null)
                continue;
            byte[] parent = crop;
            if (unit.Part != null)
            {
                if (!parents.TryGetValue(unit.Question, out parent))
                    parents[unit.Question] = parent = Encode(page, unit.ParentLines.Select(i => quads[i]).ToList(), null, null) ?? crop;
            }
            result.Add(new HomeworkPageUnit(unit, crop, parent, unit.Lines.Where(i => !string.IsNullOrWhiteSpace(texts[i])).Select(i => scores[i] < UnsureBelow ? texts[i].Trim() + " [unsure]" : texts[i].Trim()).ToList()));
        }
        return result;
    }

    private static byte[] Encode(Mat page, IReadOnlyList<TextQuad> quads, double? slotLeft, double? slotRight)
    {
        using Mat crop = PpOcrCrop.Crop(page, quads, 0.04, slotLeft, slotRight);
        if (crop == null || crop.IsEmpty)
            return null;
        using var jpeg = new VectorOfByte();
        CvInvoke.Imencode(".jpg", crop, jpeg, new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, 90));
        return jpeg.ToArray();
    }

    /// <summary>Release the models.</summary>
    public void Dispose()
    {
        _detector.Dispose();
        _recognizer.Dispose();
    }
}
