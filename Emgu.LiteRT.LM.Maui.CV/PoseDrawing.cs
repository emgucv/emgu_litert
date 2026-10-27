//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.LiteRT.Models;

namespace Emgu.LiteRT.LM.Maui.CV;

/// <summary>A body's 33 landmarks drawn as a skeleton on an image: a pose demo page, and the exercise counter in developer mode.</summary>
public static class PoseDrawing
{
    private static readonly MCvScalar Bone = new MCvScalar(247, 123, 61), Joint = new MCvScalar(255, 255, 255), Faint = new MCvScalar(160, 160, 160);

    // Landmarks in 0..1 of the image; a landmark the model thinks is hidden (visibility under 0.5) is drawn grey. Landmarks for which
    // skip is true are left out, with their bones (the holistic page draws the face and hands from their own models).
    /// <summary>Draw the skeleton.</summary>
    /// <param name="render">The image (BGR)</param>
    /// <param name="landmarks">The landmarks, in 0..1 of the image, with their visibility (a landmark the model thinks is hidden is drawn grey)</param>
    /// <param name="skip">Landmarks to leave out, with their bones; or null</param>
    public static void Draw(Mat render, float[][] landmarks, Func<int, bool> skip = null)
    {
        System.Drawing.Point P(int i) => new System.Drawing.Point((int)(landmarks[i][0] * render.Width), (int)(landmarks[i][1] * render.Height));
        foreach (int[] b in PoseTracker.Bones)
            if (skip == null || (!skip(b[0]) && !skip(b[1])))
                CvInvoke.Line(render, P(b[0]), P(b[1]), landmarks[b[0]][3] < 0.5f || landmarks[b[1]][3] < 0.5f ? Faint : Bone, 2);
        for (int i = 0; i < PoseTracker.LandmarkCount; i++)
            if (skip == null || !skip(i))
                CvInvoke.Circle(render, P(i), 3, landmarks[i][3] < 0.5f ? Faint : Joint, -1);
    }
}
