// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Emgu.LiteRT.LM.Agents;
using Emgu.LiteRT.LM.Extensions.AI;
using Emgu.LiteRT.LM.Models;
using Emgu.LiteRT.Models;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// PP-OCRv6 Small's text detector: the DB post-processing of its probability map (no model needed), and a spike on real
// photos (HOMEWORK_PHOTOS) that draws the lines it finds.
[TestFixture, Category("Native")]
public class UnitTestPpOcr
{
    private static float[] MapWith(params (PointF Center, SizeF Size, float Angle)[] boxes)
    {
        int size = PpOcrDetModel.InputSize;
        using var image = new Mat(size, size, DepthType.Cv32F, 1);
        image.SetTo(new MCvScalar(0));
        foreach (var box in boxes)
        {
            using var polygon = new VectorOfPoint(new RotatedRect(box.Center, box.Size, box.Angle).GetVertices().Select(p => Point.Round(p)).ToArray());
            CvInvoke.FillConvexPoly(image, polygon, new MCvScalar(0.9));
        }
        var map = new float[size * size];
        image.CopyTo(map);
        return map;
    }

    [Test]
    public void FindsAHorizontalLineAndGrowsItAndMapsItToThePage()
    {
        // A 300 x 20 line on the map; the page is twice as large, so the scale is 0.5 (page pixels -> map pixels).
        var quads = PpOcrDetector.PostProcess(MapWith((new PointF(320, 100), new SizeF(300, 20), 0)), 0.5, 1280, 1280);
        Assert.That(quads.Length, Is.EqualTo(1));
        var bounds = quads[0].Bounds;
        // Grown by area * 1.4 / perimeter = 300 * 20 * 1.4 / 640 = 13.1 each side on the map, so 26 on the page.
        Assert.That(bounds.Width, Is.EqualTo((300 + 26.25) * 2).Within(8));
        Assert.That(bounds.Height, Is.EqualTo((20 + 26.25) * 2).Within(8));
        Assert.That(bounds.X + bounds.Width / 2, Is.EqualTo(640).Within(4));
        Assert.That(quads[0].Angle, Is.EqualTo(0).Within(1));
        Assert.That(quads[0].Score, Is.GreaterThan(0.8f));
    }

    [Test]
    public void KeepsTheTiltOfASkewedLineAndOrdersTopToBottom()
    {
        var quads = PpOcrDetector.PostProcess(MapWith(
            (new PointF(320, 400), new SizeF(240, 16), 8), (new PointF(320, 100), new SizeF(240, 16), 0)), 1, 640, 640);
        Assert.That(quads.Length, Is.EqualTo(2));
        Assert.That(quads[0].Bounds.Y, Is.LessThan(quads[1].Bounds.Y));
        Assert.That(quads[0].Angle, Is.EqualTo(0).Within(1));
        Assert.That(quads[1].Angle, Is.EqualTo(8).Within(1.5));
    }

    [Test]
    public void DropsSpecksAndWeakRegions()
    {
        var map = MapWith((new PointF(100, 100), new SizeF(2, 2), 0), (new PointF(320, 320), new SizeF(120, 14), 0));
        for (int i = 0; i < map.Length; i++) if (map[i] > 0) map[i] = Math.Min(map[i], i % 640 < 300 ? 0.9f : 0.9f);
        var quads = PpOcrDetector.PostProcess(map, 1, 640, 640);
        Assert.That(quads.Length, Is.EqualTo(1), "the 2 x 2 speck is under the minimum side");
        var weak = MapWith((new PointF(320, 320), new SizeF(120, 14), 0));
        for (int i = 0; i < weak.Length; i++) if (weak[i] > 0) weak[i] = 0.3f;   // above the 0.2 threshold, under the 0.45 box score
        Assert.That(PpOcrDetector.PostProcess(weak, 1, 640, 640), Is.Empty);
    }

    [Test]
    public void OrdersCornersClockwiseFromTheTopLeft()
    {
        var ordered = PpOcrDetector.Order(new[] { new PointF(100, 50), new PointF(0, 60), new PointF(10, 0), new PointF(110, -10) });
        Assert.That(ordered[0], Is.EqualTo(new PointF(10, 0)));
        Assert.That(ordered[1], Is.EqualTo(new PointF(110, -10)));
        Assert.That(ordered[2], Is.EqualTo(new PointF(100, 50)));
        Assert.That(ordered[3], Is.EqualTo(new PointF(0, 60)));
    }

    // Spike: HOMEWORK_PHOTOS ('...;...'), HOMEWORK_OUT (a folder for the overlays). Draws every line found on its page.
    [Test]
    public async Task DrawsTheLinesFoundOnHomeworkPhotos()
    {
        string photos = Environment.GetEnvironmentVariable("HOMEWORK_PHOTOS");
        if (string.IsNullOrEmpty(photos))
            Assert.Ignore("Set HOMEWORK_PHOTOS to the homework photos (separated by ';')");
        string output = Environment.GetEnvironmentVariable("HOMEWORK_OUT") ?? Path.GetTempPath();
        using var detector = new PpOcrDetector(Path.Combine(output, "models"));
        await detector.PrepareAsync(2, Console.WriteLine, CancellationToken.None);
        using var recognizer = new PpOcrRecognizer(Path.Combine(output, "models"));
        await recognizer.PrepareAsync(2, Console.WriteLine, CancellationToken.None);
        foreach (string photo in photos.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            using var page = CvInvoke.Imread(photo, ImreadModes.ColorBgr);
            var time = System.Diagnostics.Stopwatch.StartNew();
            var quads = detector.Detect(page);
            Console.WriteLine("{0}: {1}x{2}, {3} lines in {4} ms (detection)", Path.GetFileName(photo), page.Width, page.Height, quads.Length, time.ElapsedMilliseconds);
            using var overlay = page.Clone();
            int number = 0;
            foreach (var quad in quads)
            {
                using var polygon = new VectorOfPoint(quad.Points.Select(p => Point.Round(p)).ToArray());
                CvInvoke.Polylines(overlay, polygon, true, new MCvScalar(0, 0, 255), 2);
                CvInvoke.PutText(overlay, (++number).ToString(), Point.Round(quad.Points[0]), FontFace.HersheySimplex, 0.6, new MCvScalar(255, 0, 0), 2);
                string start = recognizer.RecognizeStart(page, quad, 3, 3, out float startScore);
                Console.WriteLine("  {0}: ({1:F0},{2:F0}) {3:F0}x{4:F0} angle {5:F1} score {6:F2} start '{7}' ({8:F2})", number, quad.Points[0].X, quad.Points[0].Y, quad.Width, quad.Height, quad.Angle, quad.Score, start, startScore);
            }
            CvInvoke.Imwrite(Path.Combine(output, Path.GetFileNameWithoutExtension(photo) + ".lines.jpg"), overlay);
            // The layout on these lines, and a crop for every unit.
            var layoutLines = quads.Select((q, i) =>
            {
                RectangleF b = q.Bounds;
                string plain = recognizer.RecognizeStart(page, q, 6, 0, out _);
                string left = b.X < 0.3 * page.Width ? recognizer.RecognizeStart(page, q, 3, 3, out _) : null;
                return new LayoutLine(i, b.X, b.Y, b.Width, b.Height, plain, left);
            }).ToList();
            var units = HomeworkLayout.Segment(layoutLines, page.Width, page.Height);
            Console.WriteLine("{0}: {1} units: {2}", Path.GetFileName(photo), units.Count, string.Join(" ", units.Select(u => u.Label)));
            string folder = Path.Combine(output, Path.GetFileNameWithoutExtension(photo) + "_units");
            Directory.CreateDirectory(folder);
            foreach (var unit in units)
            {
                using var crop = PpOcrCrop.Crop(page, unit.Lines.Select(i => quads[i]).ToList(), 0.04, unit.SlotLeft, unit.SlotRight);
                if (crop != null)
                    CvInvoke.Imwrite(Path.Combine(folder, "q" + unit.Label + ".jpg"), crop);
                Console.WriteLine("  {0}: {1} lines {2}", unit.Label, unit.Lines.Count, crop == null ? "no crop" : crop.Width + "x" + crop.Height);
            }
        }
    }

    // Spike: the per-question check on real pages with a real model. HOMEWORK_PHOTOS, HOMEWORK_OUT, HOMEWORK_MODEL ("E4B" or
    // "12B"; default 12B) and HOMEWORK_LABELS (comma separated, e.g. "2,4d,5,8a"; default every question).
    [Test]
    public async Task ChecksHomeworkQuestionsWithAModel()
    {
        string photos = Environment.GetEnvironmentVariable("HOMEWORK_PHOTOS");
        if (string.IsNullOrEmpty(photos))
            Assert.Ignore("Set HOMEWORK_PHOTOS to the homework photos (separated by ';')");
        string output = Environment.GetEnvironmentVariable("HOMEWORK_OUT") ?? Path.GetTempPath();
        string[] labels = (Environment.GetEnvironmentVariable("HOMEWORK_LABELS") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        using var segmenter = new HomeworkSegmenter(Path.Combine(output, "models"));
        await segmenter.PrepareAsync(2, Console.WriteLine, CancellationToken.None);
        var units = new List<(int Page, HomeworkPageUnit Unit)>();
        int page = 0;
        foreach (string photo in photos.Split(';', StringSplitOptions.RemoveEmptyEntries))
            units.AddRange(segmenter.Segment(System.IO.File.ReadAllBytes(photo)).Select(u => (++page, u)).Select(x => (x.Item1, x.u)).ToList());
        using LanguageModel model = Environment.GetEnvironmentVariable("HOMEWORK_MODEL") == "E4B" ? new Gemma4E4B() : new Gemma4_12B();
        await model.Init(backend: "gpu");
        using IChatClient client = model.AsIChatClient();
        HomeworkQuestionChecker.Trace = message => Console.WriteLine("   TRACE " + message.Replace("\n", " "));
        foreach (var (pageNumber, unit) in units)
        {
            if (labels.Length > 0 && !labels.Contains(unit.Label))
                continue;
            var result = new HomeworkQuestionResult();
            var time = System.Diagnostics.Stopwatch.StartNew();
            await HomeworkQuestionChecker.CheckAsync(client, unit.Label, unit.Crop, unit.ParentCrop, unit.Text, null, result, null, CancellationToken.None);
            Console.WriteLine("Q{0} (page {1}, {2:F0} s): {3} | answer '{4}' | question '{5}'", unit.Label, pageNumber, time.Elapsed.TotalSeconds, result.Verdict, result.StudentAnswer, result.Question.Length > 60 ? result.Question.Substring(0, 60) : result.Question);
            Console.WriteLine("   OCR: {0}", string.Join(" | ", unit.Text));
            if (result.Verdict == HomeworkVerdict.Wrong)
                Console.WriteLine("   wrong: {0} || how: {1}", result.WhatWentWrong, result.HowToSolve.Length > 100 ? result.HowToSolve.Substring(0, 100) : result.HowToSolve);
            if (result.Verdict == HomeworkVerdict.Unclear)
                Console.WriteLine("   unclear: {0}", result.Reason);
        }
    }

    // Spike: can the model tell a question with handwriting in its picture from one without? (yes/no per question)
    [Test]
    public async Task TellsWhetherAQuestionHasHandwriting()
    {
        string photos = Environment.GetEnvironmentVariable("HOMEWORK_PHOTOS");
        if (string.IsNullOrEmpty(photos))
            Assert.Ignore("Set HOMEWORK_PHOTOS to the homework photos (separated by ';')");
        string output = Environment.GetEnvironmentVariable("HOMEWORK_OUT") ?? Path.GetTempPath();
        using var segmenter = new HomeworkSegmenter(Path.Combine(output, "models"));
        await segmenter.PrepareAsync(2, Console.WriteLine, CancellationToken.None);
        var units = photos.Split(';', StringSplitOptions.RemoveEmptyEntries).SelectMany(photo => segmenter.Segment(System.IO.File.ReadAllBytes(photo))).ToList();
        using LanguageModel model = Environment.GetEnvironmentVariable("HOMEWORK_MODEL") == "E4B" ? new Gemma4E4B() : new Gemma4_12B();
        await model.Init(backend: "gpu");
        using IChatClient client = model.AsIChatClient();
        const string prompt = "This picture is part of a student's homework page. The question is printed. Look for anything the student " +
            "wrote by hand (pen or pencil), such as an answer, numbers or working. Is there any handwriting in this picture? " +
            "Reply with exactly one word: yes or no.";
        foreach (HomeworkPageUnit unit in units)
        {
            ChatResponse response = await client.GetResponseAsync(
                new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, new List<AIContent> { new DataContent(unit.Crop, "image/jpeg"), new TextContent(prompt) }) },
                new ChatOptions { MaxOutputTokens = 8, Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None } });
            Console.WriteLine("HANDWRITING Q{0}: {1}", unit.Label, HomeworkQuestionChecker.WithoutThinking(response.Text));
        }
    }
}
