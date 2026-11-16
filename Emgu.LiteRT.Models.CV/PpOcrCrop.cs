// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace Emgu.LiteRT.Models;

/// <summary>Cuts the region of a page that holds some lines of text, upright, from the lines' four-point corners.</summary>
public static class PpOcrCrop
{
    /// <summary>
    /// The minimum-area (rotated) rectangle around every corner of the given lines, grown by a margin, warped to an upright
    /// picture: a page photographed at a slant gives a straight crop. The crop is clipped to the page.
    /// </summary>
    /// <param name="bgr">The page, 8-bit BGR</param>
    /// <param name="quads">The lines in the region</param>
    /// <param name="margin">Added on every side, as a fraction of the region's shorter side (at least 8 pixels)</param>
    /// <param name="slotLeft">Widen the region to at least this x (a part's slot of its row; the detector can miss the thin boxes an answer is written in), or null</param>
    /// <param name="slotRight">Widen the region to at least this x, or null</param>
    /// <returns>The crop, or null when there are no lines</returns>
    public static Mat Crop(Mat bgr, IReadOnlyList<TextQuad> quads, double margin = 0.04, double? slotLeft = null, double? slotRight = null)
    {
        if (bgr == null || bgr.IsEmpty) throw new ArgumentException("Expected a nonempty page.", nameof(bgr));
        if (quads == null || quads.Count == 0) return null;
        var corners4 = quads.SelectMany(q => q.Points).ToList();
        if (slotLeft.HasValue || slotRight.HasValue)
        {
            float top = corners4.Min(p => p.Y), bottom = corners4.Max(p => p.Y);
            float left = (float)Math.Min(slotLeft ?? double.MaxValue, corners4.Min(p => p.X)), right = (float)Math.Max(slotRight ?? double.MinValue, corners4.Max(p => p.X));
            right = Math.Min(right, bgr.Width - 1);
            corners4.AddRange(new[] { new PointF(left, top), new PointF(right, top), new PointF(right, bottom), new PointF(left, bottom) });
        }
        PointF[] points = corners4.ToArray();
        RotatedRect rectangle;
        using (var all = new VectorOfPointF(points))
            rectangle = CvInvoke.MinAreaRect(all);
        // Keep the page's orientation: an angle near 90 degrees is the same rectangle turned a quarter.
        float width = rectangle.Size.Width, height = rectangle.Size.Height, angle = rectangle.Angle;
        if (angle > 45) { angle -= 90; (width, height) = (height, width); }
        else if (angle < -45) { angle += 90; (width, height) = (height, width); }
        float pad = (float)Math.Max(8, margin * Math.Min(width, height));
        var grown = new RotatedRect(rectangle.Center, new SizeF(width + 2 * pad, height + 2 * pad), angle);
        PointF[] corners = PpOcrDetector.Order(grown.GetVertices());
        int cropWidth = Math.Max(1, (int)Math.Round(Distance(corners[0], corners[1]))), cropHeight = Math.Max(1, (int)Math.Round(Distance(corners[0], corners[3])));
        var result = new Mat();
        using (var from = new VectorOfPointF(corners))
        using (var to = new VectorOfPointF(new[] { new PointF(0, 0), new PointF(cropWidth, 0), new PointF(cropWidth, cropHeight), new PointF(0, cropHeight) }))
        using (var transform = CvInvoke.GetPerspectiveTransform(from, to))
            CvInvoke.WarpPerspective(bgr, result, transform, new Size(cropWidth, cropHeight), Inter.Cubic, Warp.Default, BorderType.Replicate);
        return result;
    }

    private static double Distance(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
