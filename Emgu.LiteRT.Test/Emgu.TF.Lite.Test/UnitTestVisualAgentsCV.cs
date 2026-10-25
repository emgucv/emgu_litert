// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// Emgu.LiteRT.LM.Agents.CV's agents end to end: each prepares its detector (downloading the models on the first run) and reads a picture given as a camera
// frame - a padded row stride, as cameras give - into observations of its classes.
[TestFixture, Category("Native")]
public class UnitTestVisualAgentsCV
{
    private static readonly string Cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Emgu.LiteRT.Tests", "VisualAgents");

    // The picture (turned a quarter clockwise if asked) as a frame whose rows are 16 bytes longer than its pixels.
    private static VisualFrame Frame(string file, bool turned = false)
    {
        using Mat bgr = CvInvoke.Imread(Path.Combine(AppContext.BaseDirectory, file), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        if (turned) CvInvoke.Rotate(bgr, bgr, Emgu.CV.CvEnum.RotateFlags.Rotate90Clockwise);
        int stride = bgr.Width * 3 + 16;
        byte[] pixels = new byte[stride * bgr.Height];
        byte[] row = new byte[bgr.Width * 3];
        for (int y = 0; y < bgr.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(bgr.DataPointer, y * bgr.Step), row, 0, row.Length);
            Buffer.BlockCopy(row, 0, pixels, y * stride, row.Length);
        }
        return new VisualFrame(pixels, bgr.Width, bgr.Height, stride, "test", 1, DateTimeOffset.UtcNow, TimeSpan.Zero);
    }

    // The labels the agent reports for the picture. The holistic agent reports the plain postures and expressions too, besides its own classes.
    private static async Task<string[]> LabelsAsync(IVisualAgent agent, string file, bool turned = false)
    {
        using (agent)
        {
            Assert.That(agent.Descriptor.RequiresPreparation, Is.True);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await agent.PrepareAsync(null, timeout.Token);
            Assert.That(agent.Descriptor.RequiresPreparation, Is.False);
            Assert.That(agent.Descriptor.Name, Does.Contain("LiteRT CPU"));
            using VisualFrame frame = Frame(file, turned);
            VisualAnalysis analysis = await agent.AnalyzeAsync(frame, timeout.Token);
            Assert.That(analysis.AgentId, Is.EqualTo(agent.Descriptor.Id));
            string[] classes = agent.Descriptor.ObjectClasses.Concat(agent is HolisticVisualAgent ? VisualClasses.PoseClasses.Concat(VisualClasses.FaceClasses) : Array.Empty<string>()).ToArray();
            Assert.That(analysis.Observations.Select(o => o.Label).Except(classes), Is.Empty);
            Assert.That(analysis.Observations.All(o => o.Box.X >= 0 && o.Box.Y >= 0 && o.Box.X + o.Box.Width <= 1.0001 && o.Box.Y + o.Box.Height <= 1.0001), Is.True);
            TestContext.Out.WriteLine($"{agent.Descriptor.Id} on {file}: " + string.Join(", ", analysis.Observations.Select(o => $"{o.Label} {o.Confidence:0.00}")));
            return analysis.Observations.Select(o => o.Label).ToArray();
        }
    }

    [Test]
    public async Task TheCocoAgentsFindTheSurfers()
    {
        Assert.That(await LabelsAsync(new CocoVisualAgent(new CocoDetector(Cache)), "surfers.jpg"), Does.Contain("person"));
        Assert.That(await LabelsAsync(new CocoVisualAgent(new YoloXDetector(Cache)), "surfers.jpg"), Does.Contain("person"));
    }

    [Test]
    public async Task TheHandAgentSeesAThumbUp() =>
        Assert.That(await LabelsAsync(new HandVisualAgent(new HandGestureDetector(Cache)), "hand_thumb_up.jpg"), Does.Contain("thumb up"));

    // MediaPipe's warrior pose: arms out at shoulder height and a lunge is no posture; turned on its side, the body is lying down.
    [Test]
    public async Task ThePoseAgentSeesALungeTurnedOnItsSideLyingDown()
    {
        Assert.That(await LabelsAsync(new PoseVisualAgent(new PoseDetector(Cache)), "pose_standing.jpg"), Is.Empty);
        Assert.That(await LabelsAsync(new PoseVisualAgent(new PoseDetector(Cache)), "pose_standing.jpg", turned: true), Is.EqualTo(new[] { "lying down" }));
    }

    [Test]
    public async Task TheFaceAgentSeesASmile() =>
        Assert.That(await LabelsAsync(new FaceVisualAgent(new FaceDetector(Cache)), "face_portrait.png"), Does.Contain("smiling"));

    [Test]
    public async Task TheHolisticAgentSeesARaisedHandAndASmile() =>
        Assert.That(await LabelsAsync(new HolisticVisualAgent(new HolisticDetector(Cache)), "holistic_hand_raised_smiling.jpg"), Does.Contain("hand raised and smiling"));
}
