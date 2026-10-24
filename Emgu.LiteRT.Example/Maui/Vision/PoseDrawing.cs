//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.LiteRT.Models;

namespace Maui.Demo.Lite
{
    // A body's 33 landmarks drawn as a skeleton: the "Body pose" page, and the chat's jumping jack counter in developer mode.
    internal static class PoseDrawing
    {
        private static readonly MCvScalar Bone = new MCvScalar(247, 123, 61), Joint = new MCvScalar(255, 255, 255), Faint = new MCvScalar(160, 160, 160);

        // Landmarks in 0..1 of the image; a landmark the model thinks is hidden (visibility under 0.5) is drawn grey.
        public static void Draw(Mat render, float[][] landmarks)
        {
            System.Drawing.Point P(int i) => new System.Drawing.Point((int)(landmarks[i][0] * render.Width), (int)(landmarks[i][1] * render.Height));
            foreach (int[] b in PoseTracker.Bones)
                CvInvoke.Line(render, P(b[0]), P(b[1]), landmarks[b[0]][3] < 0.5f || landmarks[b[1]][3] < 0.5f ? Faint : Bone, 2);
            for (int i = 0; i < PoseTracker.LandmarkCount; i++)
                CvInvoke.Circle(render, P(i), 3, landmarks[i][3] < 0.5f ? Faint : Joint, -1);
        }
    }
}
