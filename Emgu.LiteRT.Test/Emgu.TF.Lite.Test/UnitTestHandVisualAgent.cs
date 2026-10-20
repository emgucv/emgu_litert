// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;
using Emgu.LiteRT.Util;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The hand-gesture visual agent (TODO item 47): the words people use for a gesture, the agent's descriptor, the conversion of hands into observations
// of a gesture class - against MediaPipe's own test images - and a monitor for "thumb up" created and satisfied the way a COCO monitor is.
[TestFixture]
public class UnitTestHandVisualAgent
{
    [TestCase("thumb up", "thumb up")]
    [TestCase("Thumbs up", "thumb up")]
    [TestCase("Thumb_Up", "thumb up")]
    [TestCase("thumbs-up!", "thumb up")]
    [TestCase("👍", "thumb up")]
    [TestCase("thumbs down", "thumb down")]
    [TestCase("Thumb_Down", "thumb down")]
    [TestCase("peace sign", "victory")]
    [TestCase("Victory", "victory")]
    [TestCase("✌️", "victory")]
    [TestCase("open palm", "open palm")]
    [TestCase("Open_Palm", "open palm")]
    [TestCase("high five", "open palm")]
    [TestCase("fist", "closed fist")]
    [TestCase("Closed_Fist", "closed fist")]
    [TestCase("Pointing_Up", "pointing up")]
    [TestCase("pointing finger", "pointing up")]
    [TestCase("hand-gesture.thumb up", "thumb up")]
    [TestCase("hand gesture: victory", "victory")]
    [TestCase("ILoveYou", "i love you")]
    [TestCase("I love you", "i love you")]
    public void TheWordsForAGestureFindItsClass(string said, string gesture)
    {
        Assert.That(VisualHostCatalog.CanonicalGesture(said), Is.EqualTo(gesture));
    }

    [TestCase("person")]
    [TestCase("stop sign")]          // a COCO class, not a hand
    [TestCase("None")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("a wave")]
    public void OtherWordsAreNotGestures(string said)
    {
        Assert.That(VisualHostCatalog.CanonicalGesture(said), Is.Null);
    }

    [Test]
    public void EveryMediaPipeLabelHasAClass()
    {
        foreach (string label in HandTracker.GestureLabels.Where(l => l != "None"))
            Assert.That(VisualHostCatalog.Gestures, Does.Contain(VisualHostCatalog.CanonicalGesture(label)), label);
        Assert.That(VisualHostCatalog.Gestures, Is.Unique);
        Assert.That(VisualHostCatalog.Gestures.Length, Is.EqualTo(7));
    }

    [TestCase("Use the selfie camera and take a picture when you see a thumb up", "front")]
    [TestCase("take a picture with the front camera", "front")]
    [TestCase("use the front-facing camera", "front")]
    [TestCase("Open the camera facing me", "front")]
    [TestCase("switch to the rear camera", "back")]
    [TestCase("use the back camera", "back")]
    [TestCase("Use the main camera.", "back")]
    [TestCase("stop using the selfie camera, use the back camera", "back")]
    [TestCase("use the back camera... no, the selfie camera", "front")]
    [TestCase("when you see a person, take a picture", null)]
    [TestCase("take a selfie of me", "front")]
    [TestCase("the front door camera", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void TheWordsForACameraChooseIt(string said, string expected)
    {
        Assert.That(VisualHostCatalog.CameraFacingRequest(said), Is.EqualTo(expected));
    }

    [Test]
    public void AGestureMonitorGetsALowerDefaultConfidenceAndKeepsAChosenOne()
    {
        Assert.That(VisualHostCatalog.GestureConfidence(0.5), Is.EqualTo(0.4));
        Assert.That(VisualHostCatalog.GestureConfidence(0.7), Is.EqualTo(0.7));
        Assert.That(VisualHostCatalog.GestureConfidence(0.3), Is.EqualTo(0.3));
        Assert.That(VisualHostCatalog.GestureConfidence(0.9), Is.EqualTo(0.9));
    }

    private sealed class FakeAgent(VisualAgentDescriptor descriptor) : IVisualAgent
    {
        public VisualAgentDescriptor Descriptor => descriptor;
        public Task PrepareAsync(Action<string> progress, CancellationToken token) => Task.CompletedTask;
        public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Test]
    public void AMonitorForAGestureSelectsTheHandAgentAndAnObjectOneStillSelectsTheDetector()
    {
        var agents = new VisualAgentRegistry();
        agents.Register(new FakeAgent(VisualHostCatalog.Detector()));
        agents.Register(new FakeAgent(VisualHostCatalog.HandDetector()));
        Assert.That(agents.SelectObjects("thumb up").Descriptor.Id, Is.EqualTo("hand-gesture"));
        Assert.That(agents.SelectObjects("victory").Descriptor.Id, Is.EqualTo("hand-gesture"));
        Assert.That(agents.SelectObjects("person").Descriptor.Id, Is.EqualTo("coco-ssd-mobilenet-v3"));
        Assert.Throws<ArgumentException>(() => agents.SelectObjects("thumbs up"));            // the host maps the words first
        var hand = agents.Describe().Single(d => d.Id == "hand-gesture");
        Assert.That(hand.Capabilities, Does.Contain("object_detection").And.Contain("hand_gestures"));
        Assert.That(hand.ObjectClasses, Is.EquivalentTo(VisualHostCatalog.Gestures));
        // The classes don't overlap COCO's, so no gesture is ever claimed by the object detector.
        Assert.That(VisualHostCatalog.Classes.Intersect(VisualHostCatalog.Gestures), Is.Empty);
    }

    [Test]
    public void AGestureConditionCountsObservationsOfItsClass()
    {
        var condition = new ObjectCountCondition("thumb up", 1, 0.5);
        var frame = new VisualFrame(new byte[12], 2, 2, 6, "camera", 1, DateTimeOffset.Now, TimeSpan.FromSeconds(1));
        VisualAnalysis Analysis(params (string Label, double Score)[] items) =>
            new VisualAnalysis("hand-gesture", frame, items.Select(i => new VisualObservation("object", i.Label, i.Score)));
        Assert.That(condition.Count(Analysis(("thumb up", 0.9))), Is.EqualTo(1));
        Assert.That(condition.Count(Analysis(("thumb up", 0.9), ("thumb up", 0.8), ("victory", 0.9))), Is.EqualTo(2));
        Assert.That(condition.Count(Analysis(("thumb up", 0.4))), Is.Zero);
        Assert.That(condition.Count(Analysis(("victory", 0.99))), Is.Zero);
    }
}

[TestFixture, Category("Native")]
public class UnitTestHandVisualAgentNative
{
    [Test]
    public async Task MediaPipesTestImagesGiveTheirGestureAsAnObservationInsideTheImage()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Emgu.LiteRT.Tests", "Hands");
        var bundle = new DownloadableFile(HandModelFiles.BundleUrl, Path.Combine("Emgu.LiteRT.Tests", "Hands"), HandModelFiles.BundleSha256);
        var downloads = new FileDownloadManager();
        downloads.AddFile(bundle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await downloads.Download(1, timeout.Token);
        Assert.That(downloads.AllFilesDownloaded, Is.True);
        string[] models = HandModelFiles.Extract(bundle.LocalFile, folder);
        using var tracker = new HandTracker(models[0], models[1], models[2], models[3]);
        string root = AppContext.BaseDirectory;
        foreach (var (image, expected) in new[]
        {
            ("hand_thumb_up.jpg", "thumb up"), ("hand_victory.jpg", "victory"), ("hand_fist.jpg", "closed fist"),
            ("hand_pointing_up.jpg", "pointing up")
        })
        {
            using var bgr = Emgu.CV.CvInvoke.Imread(Path.Combine(root, image), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
            var observations = VisualHostCatalog.GestureObservations(HandImageInput.Track(tracker, bgr)).ToArray();
            Assert.That(observations.Length, Is.EqualTo(1), image);
            var o = observations[0];
            Assert.That(o.Kind, Is.EqualTo("object"));
            Assert.That(o.Label, Is.EqualTo(expected), image);
            Assert.That(o.Confidence, Is.InRange(0.5, 1.0), image);
            Assert.That(o.Box.X, Is.InRange(0, 1)); Assert.That(o.Box.Y, Is.InRange(0, 1));
            Assert.That(o.Box.X + o.Box.Width, Is.LessThanOrEqualTo(1.0 + 1e-9)); Assert.That(o.Box.Width, Is.GreaterThan(0.05));
            Assert.That(new ObjectCountCondition(expected).Count(new VisualAnalysis("hand-gesture",
                new VisualFrame(new byte[12], 2, 2, 6, "camera", 1, DateTimeOffset.Now, TimeSpan.Zero), observations)), Is.EqualTo(1), image);
        }
        // The sideways hand: MediaPipe's own reading is "none"; with the turned readings it is pointing up.
        using var sideways = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "hand_pointing_up_rotated.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(VisualHostCatalog.GestureObservations(HandImageInput.Track(tracker, sideways)).Select(o => o.Label), Is.EqualTo(new[] { "pointing up" }));
        tracker.TiltAngles = Array.Empty<double>();
        Assert.That(VisualHostCatalog.GestureObservations(HandImageInput.Track(tracker, sideways)), Is.Empty);
        tracker.TiltAngles = new double[] { -30, 30, -60, 60 };
        using var shuttle = Emgu.CV.CvInvoke.Imread(Path.Combine(root, "space_shuttle.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(VisualHostCatalog.GestureObservations(HandImageInput.Track(tracker, shuttle)), Is.Empty);
    }
}
