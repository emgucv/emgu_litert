// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The body-pose visual agent (TODO item 52): the postures read from 33 landmarks - made-up ones here, MediaPipe's own on a real photo in the native test -,
// waving over time, the words people use, and a monitor for "waving" selecting the agent.
[TestFixture]
public class UnitTestPoseVisualAgent
{
    private const int W = 200, H = 400;                    // the made-up image: 200 x 400 pixels

    // A person standing in the middle of it: shoulders 40 px apart, a torso of 120 px, thighs and shins of 80 px, arms down.
    private static float[][] Person(Action<float[][]> change = null)
    {
        var p = Enumerable.Range(0, 33).Select(_ => new[] { 0.5f, 0.5f, 0f, 1f, 1f }).ToArray();
        void Set(int i, float x, float y) { p[i][0] = x; p[i][1] = y; }
        Set(0, 0.5f, 0.1f);
        Set(11, 0.4f, 0.25f); Set(12, 0.6f, 0.25f);
        Set(15, 0.35f, 0.5f); Set(16, 0.65f, 0.5f);
        Set(23, 0.43f, 0.55f); Set(24, 0.57f, 0.55f);
        Set(25, 0.43f, 0.75f); Set(26, 0.57f, 0.75f);
        Set(27, 0.43f, 0.95f); Set(28, 0.57f, 0.95f);
        change?.Invoke(p);
        return p;
    }

    private static string[] Names(float[][] landmarks, float min = 0.5f) => PoseGestures.Classify(landmarks, W, H, min).Select(r => r.Name).ToArray();

    // The person turned about the hips by an angle (a tilted camera or a leaning body), x and y in pixels.
    private static float[][] Turn(float[][] p, double degrees)
    {
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
        double cx = p[23][0] * W / 2 + p[24][0] * W / 2, cy = p[23][1] * H / 2 + p[24][1] * H / 2;
        return p.Select(q =>
        {
            double x = q[0] * W - cx, y = q[1] * H - cy;
            return new[] { (float)((cx + c * x - s * y) / W), (float)((cy + s * x + c * y) / H), q[2], q[3], q[4] };
        }).ToArray();
    }

    [Test]
    public void AStandingPersonIsStandingAndNothingElse()
    {
        Assert.That(Names(Person()), Is.EqualTo(new[] { "standing" }));
        // Turned 25 degrees about the hips (a tilted camera): the thighs are measured along the spine, so still standing.
        Assert.That(Names(Turn(Person(), 25)), Is.EqualTo(new[] { "standing" }));
    }

    [Test]
    public void SittingHasTheKneesLevelWithTheHips()
    {
        var sitting = Person(p => { p[25][1] = 0.56f; p[26][1] = 0.56f; p[27][1] = 0.8f; p[28][1] = 0.8f; });
        Assert.That(Names(sitting), Is.EqualTo(new[] { "sitting" }));
        Assert.That(Names(Turn(sitting, -25)), Is.EqualTo(new[] { "sitting" }));
    }

    [Test]
    public void ALungeIsNeitherStandingNorSitting()
    {
        // One thigh down the spine, one across it (the test photo's warrior pose).
        var lunge = Person(p => { p[25][1] = 0.56f; p[26][1] = 0.75f; });
        Assert.That(Names(lunge), Is.Empty);
    }

    [Test]
    public void LyingDownIsASpineAcrossTheImage()
    {
        // Shoulders at the left, hips at the right, the same height.
        var lying = Person(p =>
        {
            p[11][0] = 0.2f; p[11][1] = 0.45f; p[12][0] = 0.2f; p[12][1] = 0.55f;
            p[23][0] = 0.5f; p[23][1] = 0.45f; p[24][0] = 0.5f; p[24][1] = 0.55f;
            p[25][0] = 0.7f; p[25][1] = 0.45f; p[26][0] = 0.7f; p[26][1] = 0.55f;
        });
        Assert.That(Names(lying), Is.EqualTo(new[] { "lying down" }));
        // Leaning 45 degrees is a person leaning, not lying.
        Assert.That(Names(Turn(Person(), 45)), Is.Not.Contains("lying down"));
    }

    [Test]
    public void ARaisedWristIsAHandAndTwoAreArms()
    {
        Assert.That(Names(Person(p => { p[15][1] = 0.1f; })), Is.EqualTo(new[] { "hand raised", "standing" }));
        Assert.That(Names(Person(p => { p[16][1] = 0.1f; })), Is.EqualTo(new[] { "hand raised", "standing" }));
        Assert.That(Names(Person(p => { p[15][1] = 0.1f; p[16][1] = 0.1f; })), Is.EqualTo(new[] { "arms raised", "standing" }));
        // A wrist at shoulder height is not raised; a little above (10 px against a 40 px shoulder width) isn't either.
        Assert.That(Names(Person(p => { p[15][1] = 0.25f; })), Is.EqualTo(new[] { "standing" }));
        Assert.That(Names(Person(p => { p[15][1] = 0.225f; })), Is.EqualTo(new[] { "standing" }));
    }

    [Test]
    public void ALandmarkThatIsNotVisibleIsNeverUsed()
    {
        // The legs cut off by the frame: no standing or sitting, whatever their made-up positions say.
        var cutOff = Person(p => { foreach (int i in new[] { 25, 26, 27, 28 }) p[i][3] = 0.1f; });
        Assert.That(Names(cutOff), Is.Empty);
        // The raised wrist hidden: no hand.
        Assert.That(Names(Person(p => { p[15][1] = 0.1f; p[15][3] = 0.2f; })), Is.EqualTo(new[] { "standing" }));
        // No shoulders, no postures at all.
        Assert.That(Names(Person(p => { p[11][3] = 0.1f; })), Is.Empty);
        // The score is the least visibility among the landmarks used.
        var readings = PoseGestures.Classify(Person(p => { p[25][3] = 0.7f; }), W, H);
        Assert.That(readings.Single().Score, Is.EqualTo(0.7f).Within(1e-6));
        Assert.Throws<ArgumentException>(() => PoseGestures.Classify(new float[5][], W, H));
        Assert.Throws<ArgumentOutOfRangeException>(() => PoseGestures.Classify(Person(), 0, H));
    }

    // The raised right hand's x over time: a wave swings 20 px (half the shoulder width) either side of the middle.
    private static PoseReading Wave(PoseGestures.WaveDetector detector, double seconds, float x, bool raised = true) =>
        detector.Update(TimeSpan.FromSeconds(seconds), Person(p => { p[16][0] = x; if (raised) p[16][1] = 0.1f; }), W, H);

    [Test]
    public void AHandSwingingFromSideToSideIsWaving()
    {
        var detector = new PoseGestures.WaveDetector();
        PoseReading reading = null;
        float[] xs = { 0.60f, 0.70f, 0.60f, 0.70f, 0.60f };       // 5 frames, 0.2 s apart, 20 px swings
        for (int i = 0; i < xs.Length; i++) { reading = Wave(detector, i * 0.2, xs[i]); if (i < 2) Assert.That(reading, Is.Null, "frame " + i); }
        Assert.That(reading, Is.Not.Null);
        Assert.That(reading.Name, Is.EqualTo("waving"));
        Assert.That(reading.Score, Is.EqualTo(1f));
    }

    [Test]
    public void AHeldHandAWobbleAndALoweredHandAreNotWaving()
    {
        var held = new PoseGestures.WaveDetector();
        for (int i = 0; i < 12; i++) Assert.That(Wave(held, i * 0.2, 0.65f), Is.Null, "held");
        var wobble = new PoseGestures.WaveDetector();
        for (int i = 0; i < 12; i++) Assert.That(Wave(wobble, i * 0.2, i % 2 == 0 ? 0.65f : 0.66f), Is.Null, "wobble of 2 px");
        // Swinging with the hand down is walking, not waving.
        var down = new PoseGestures.WaveDetector();
        for (int i = 0; i < 12; i++) Assert.That(Wave(down, i * 0.2, i % 2 == 0 ? 0.6f : 0.7f, raised: false), Is.Null, "hand down");
        // Moving once in each direction over two seconds is two movements, not a wave.
        var once = new PoseGestures.WaveDetector();
        float[] xs = { 0.55f, 0.6f, 0.65f, 0.7f, 0.75f, 0.7f, 0.65f };
        for (int i = 0; i < xs.Length; i++) Assert.That(Wave(once, i * 0.25, xs[i]), Is.Null, "one sweep");
    }

    [Test]
    public void TheWaveDetectorForgetsAfterTwoSecondsAndWhenNobodyIsThere()
    {
        var detector = new PoseGestures.WaveDetector();
        for (int i = 0; i < 5; i++) Wave(detector, i * 0.2, i % 2 == 0 ? 0.6f : 0.7f);
        Assert.That(detector.Update(TimeSpan.FromSeconds(1.0), null, W, H), Is.Null);       // nobody: reset
        Assert.That(Wave(detector, 1.2, 0.6f), Is.Null);                                    // a wave needs its swings again
        Assert.That(Wave(detector, 1.4, 0.7f), Is.Null);
        // Swings further apart than the window don't add up.
        var slow = new PoseGestures.WaveDetector();
        Assert.That(new[] { Wave(slow, 0, 0.6f), Wave(slow, 1.5, 0.7f), Wave(slow, 3.0, 0.6f), Wave(slow, 4.5, 0.7f) }, Is.All.Null);
    }

    [TestCase("arms raised", "arms raised")]
    [TestCase("Hands up!", "arms raised")]
    [TestCase("both arms up", "arms raised")]
    [TestCase("hands in the air", "arms raised")]
    [TestCase("raise my hand", "hand raised")]
    [TestCase("Hand raised", "hand raised")]
    [TestCase("🙋", "hand raised")]
    [TestCase("waving", "waving")]
    [TestCase("a wave", "waving")]
    [TestCase("wave hello", "waving")]
    [TestCase("👋", "waving")]
    [TestCase("standing up", "standing")]
    [TestCase("stands", "standing")]
    [TestCase("sit down", "sitting")]
    [TestCase("seated", "sitting")]
    [TestCase("lying down", "lying down")]
    [TestCase("Lie down", "lying down")]
    [TestCase("body-pose.waving", "waving")]
    [TestCase("body pose: standing", "standing")]
    public void TheWordsForAPostureFindItsClass(string said, string pose)
    {
        Assert.That(VisualClasses.CanonicalPose(said), Is.EqualTo(pose));
    }

    [TestCase("person")]
    [TestCase("thumb up")]
    [TestCase("hand up")]        // an open palm: the hand-gesture agent's
    [TestCase("falls")]          // nothing maps to a fall: the model's card rules out life-critical use
    [TestCase("fell down")]
    [TestCase("collapse")]
    [TestCase("asleep")]
    [TestCase("")]
    [TestCase(null)]
    public void OtherWordsAreNotPostures(string said)
    {
        Assert.That(VisualClasses.CanonicalPose(said), Is.Null);
    }

    [TestCase("Say hello when someone raises their hand.", "hand raised")]
    [TestCase("Take a picture when she raises her hand", "hand raised")]
    [TestCase("Beep when both arms are up", "arms raised")]
    [TestCase("Beep when the kids put their hands up", "arms raised")]
    [TestCase("Take a picture when the person is lying down.", "lying down")]
    [TestCase("tell me if grandpa lies down on the sofa", "lying down")]
    [TestCase("Beep when you see me waving.", "waving")]
    [TestCase("Record a clip when I wave at the camera", "waving")]
    [TestCase("Beep when I stand up.", "standing")]
    [TestCase("Alert me when the baby sits down", "sitting")]
    [TestCase("When a person stands at the door, take a picture", null)]
    [TestCase("Take a picture when you see a person", null)]
    [TestCase("Beep when a dog sits at the gate", null)]
    [TestCase("Say hello", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void TheRequestNamesAPostureInSoManyWords(string request, string pose)
    {
        Assert.That(VisualClasses.PoseFromRequest(request), Is.EqualTo(pose));
    }

    [Test]
    public void ALabelThatIsNoClassFallsBackToWhatTheUserAsked()
    {
        // The words are a posture: used as they are, whatever the request says.
        Assert.That(VisualClasses.PostureForMonitor("waving", "Beep when I sit down"), Is.EqualTo("waving"));
        // The agent's name or the person class, where the request names a posture (what Gemma 4 E2B did).
        Assert.That(VisualClasses.PostureForMonitor("body-pose", "Say hello when someone raises their hand."), Is.EqualTo("hand raised"));
        Assert.That(VisualClasses.PostureForMonitor("person", "Take a picture when the person is lying down."), Is.EqualTo("lying down"));
        Assert.That(VisualClasses.PostureForMonitor("", "Beep when I stand up"), Is.EqualTo("standing"));
        Assert.That(VisualClasses.PostureForMonitor("raising hand", "x"), Is.EqualTo("hand raised"));
        // An ordinary label stays, whatever else the request says: a dog that sits down is still a dog.
        Assert.That(VisualClasses.PostureForMonitor("dog", "Beep when the dog sits down"), Is.Null);
        Assert.That(VisualClasses.PostureForMonitor("thumb up", "Take a picture when I wave and show a thumb up"), Is.Null);
        Assert.That(VisualClasses.PostureForMonitor("person", "Take a picture when you see a person"), Is.Null);
        // A label that is no class at all and a request with no posture: left for the registry to refuse.
        Assert.That(VisualClasses.PostureForMonitor("something odd", "Beep when it happens"), Is.Null);
    }

    [Test]
    public void ThePostureClassesAreTheAgentsAndDontOverlapOthers()
    {
        Assert.That(VisualClasses.PoseClasses, Is.EqualTo(new[] { "arms raised", "hand raised", "waving", "standing", "sitting", "lying down" }));
        Assert.That(VisualClasses.PoseClasses, Is.Unique);
        foreach (string pose in VisualClasses.PoseClasses)
            Assert.That(VisualClasses.CanonicalPose(pose), Is.EqualTo(pose), pose);
        Assert.That(VisualClasses.ObjectClasses.Intersect(VisualClasses.PoseClasses), Is.Empty);
        Assert.That(VisualClasses.Gestures.Intersect(VisualClasses.PoseClasses), Is.Empty);
        foreach (string gesture in VisualClasses.Gestures)
            Assert.That(VisualClasses.CanonicalPose(gesture), Is.Null, gesture);
    }

    private sealed class FakeAgent(VisualAgentDescriptor descriptor) : IVisualAgent
    {
        public VisualAgentDescriptor Descriptor => descriptor;
        public Task PrepareAsync(Action<string> progress, CancellationToken token) => Task.CompletedTask;
        public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Test]
    public void AMonitorForAPostureSelectsThePoseAgent()
    {
        var agents = new VisualAgentRegistry();
        agents.Register(new FakeAgent(CocoVisualAgent.Describe()));
        agents.Register(new FakeAgent(HandVisualAgent.Describe()));
        agents.Register(new FakeAgent(PoseVisualAgent.Describe()));
        foreach (string pose in VisualClasses.PoseClasses)
            Assert.That(agents.SelectObjects(pose).Descriptor.Id, Is.EqualTo("body-pose"), pose);
        Assert.That(agents.SelectObjects("thumb up").Descriptor.Id, Is.EqualTo("hand-gesture"));
        Assert.That(agents.SelectObjects("person").Descriptor.Id, Is.EqualTo("coco-ssd-mobilenet-v3"));
        var descriptor = agents.Describe().Single(d => d.Id == "body-pose");
        Assert.That(descriptor.Capabilities, Does.Contain("object_detection").And.Contain("body_pose"));
        Assert.That(descriptor.ObjectClasses, Is.EquivalentTo(VisualClasses.PoseClasses));
    }

    [Test]
    public void PosturesBecomeObservationsOfTheirClassInsideTheImage()
    {
        var wave = new PoseGestures.WaveDetector();
        var observations = PoseVisualAgent.Observations(Person(p => { p[15][1] = 0.1f; }), W, H, wave, TimeSpan.Zero).ToArray();
        Assert.That(observations.Select(o => o.Label), Is.EquivalentTo(new[] { "hand raised", "standing" }));
        foreach (var o in observations)
        {
            Assert.That(o.Kind, Is.EqualTo("object"));
            Assert.That(o.Confidence, Is.InRange(0.5, 1.0));
            Assert.That(o.Box.X, Is.InRange(0, 1)); Assert.That(o.Box.Width, Is.GreaterThan(0.1));
            Assert.That(o.Box.X + o.Box.Width, Is.LessThanOrEqualTo(1.0 + 1e-9));
            Assert.That(o.Box.Y + o.Box.Height, Is.LessThanOrEqualTo(1.0 + 1e-9));
        }
        Assert.That(PoseVisualAgent.Observations((float[][])null, W, H, wave, TimeSpan.Zero), Is.Empty);
        // Waving is reported through the same path, once the swings have been seen.
        var detector = new PoseGestures.WaveDetector();
        IEnumerable<VisualObservation> last = null;
        for (int i = 0; i < 5; i++)
            last = PoseVisualAgent.Observations(Person(p => { p[16][0] = i % 2 == 0 ? 0.6f : 0.7f; p[16][1] = 0.1f; }), W, H, detector, TimeSpan.FromSeconds(i * 0.2));
        Assert.That(last.Select(o => o.Label), Does.Contain("waving"));
        var condition = new ObjectCountCondition("waving", 1, 0.5);
        var frame = new VisualFrame(new byte[12], 2, 2, 6, "camera", 1, DateTimeOffset.Now, TimeSpan.FromSeconds(1));
        Assert.That(condition.Count(new VisualAnalysis("body-pose", frame, last)), Is.EqualTo(1));
    }
}

[TestFixture, Category("Native")]
public class UnitTestPoseVisualAgentNative
{
    [Test]
    public async Task MediaPipesTestPhotoOfAWarriorPoseIsNoPostureAndTheSameTurnedOnItsSideIsLying()
    {
        string[] models = await UnitTestPoseTrackerNative.ModelsAsync(PoseModelFiles.Lite);
        using var tracker = new PoseTracker(models[0], models[1]);
        using var source = Emgu.CV.CvInvoke.Imread(System.IO.Path.Combine(AppContext.BaseDirectory, "pose_standing.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        string[] Labels(string variant)
        {
            using var bgr = UnitTestPoseTrackerNative.Variant(source, variant);
            var poses = PoseImageInput.Track(tracker, bgr);
            Assert.That(poses.Count, Is.EqualTo(1), variant);
            return PoseVisualAgent.Observations(poses[0], bgr.Width, bgr.Height, new PoseGestures.WaveDetector(), TimeSpan.Zero).Select(o => o.Label).ToArray();
        }
        // Arms out sideways at shoulder height and a lunge: no raised hand, and the legs are neither standing nor sitting - in any copy of it.
        foreach (string variant in new[] { "original", "flip", "far", "crop" })
            Assert.That(Labels(variant), Is.Empty, variant);
        // Turned a quarter, the spine lies across the image.
        Assert.That(Labels("rot90"), Is.EqualTo(new[] { "lying down" }));
        // And nobody in a picture of a space shuttle.
        using var shuttle = Emgu.CV.CvInvoke.Imread(System.IO.Path.Combine(AppContext.BaseDirectory, "space_shuttle.jpg"), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        Assert.That(PoseImageInput.Track(tracker, shuttle), Is.Empty);
    }
}
