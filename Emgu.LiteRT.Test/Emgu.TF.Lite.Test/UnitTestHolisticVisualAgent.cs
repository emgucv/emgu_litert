// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emgu.LiteRT.LM.Agents.Events;
using Emgu.LiteRT.LM.Agents.Vision;
using Emgu.LiteRT.Models;
using Maui.Demo.Lite;
using NUnit.Framework;

namespace Emgu.TF.Lite.Test;

// The holistic visual agent: a posture combined with an expression, and covering the eyes, a hand over the mouth or on the face - the geometry on made-up
// landmarks here, on synthetic pictures in the native test -, the words people use, and the routing of a monitor's label.
[TestFixture]
public class UnitTestHolisticVisualAgent
{
    [Test]
    public void TheHullAndTheSignedDistanceAreRight()
    {
        var hull = HolisticGestures.Hull(new[] { new double[] { 0, 0 }, new double[] { 10, 0 }, new double[] { 10, 10 }, new double[] { 0, 10 }, new double[] { 5, 5 } });
        Assert.That(hull.Count, Is.EqualTo(4), "the inner point isn't on the outline");
        Assert.That(HolisticGestures.Depth(hull, new double[] { 5, 5 }), Is.EqualTo(5).Within(1e-9));
        Assert.That(HolisticGestures.Depth(hull, new double[] { 2, 5 }), Is.EqualTo(2).Within(1e-9));
        Assert.That(HolisticGestures.Depth(hull, new double[] { 13, 5 }), Is.EqualTo(-3).Within(1e-9));
        Assert.That(HolisticGestures.Depth(hull, new double[] { 13, 14 }), Is.EqualTo(-5).Within(1e-9));
    }

    // A person 200 px wide in a 400 x 400 picture: the pose's eyes 40 px apart at y 100, the mouth below, shoulders at y 200, hips at y 360.
    internal static float[][] Body()
    {
        var p = Enumerable.Range(0, PoseTracker.LandmarkCount).Select(_ => new[] { 0.5f, 0.6f, 0f, 1f, 1f }).ToArray();
        void Set(int i, float x, float y) => p[i] = new[] { x / 400, y / 400, 0f, 1f, 1f };
        Set(0, 200, 115);                                 // nose
        Set(1, 190, 98); Set(2, 180, 100); Set(3, 170, 98);     // the person's left eye is on the picture's right... kept simple: 2 and 5 are the eyes
        Set(4, 210, 98); Set(5, 220, 100); Set(6, 230, 98);
        Set(7, 160, 105); Set(8, 240, 105);              // ears
        Set(9, 190, 135); Set(10, 210, 135);             // mouth corners
        Set(11, 140, 200); Set(12, 260, 200);            // shoulders
        Set(13, 120, 270); Set(14, 280, 270); Set(15, 115, 340); Set(16, 285, 340);
        Set(23, 160, 360); Set(24, 240, 360);
        for (int i = 25; i < 33; i++) p[i][3] = 0.1f;   // legs out of the picture
        return p;
    }

    // A hand as 21 points: a 50 x 60 px palm-and-fingers block centred at (x, y).
    internal static Hand HandAt(float x, float y) => new Hand
    {
        Landmarks = Enumerable.Range(0, HandTracker.LandmarkCount)
            .Select(i => new[] { (x - 25 + 50 * (i % 5) / 4f) / 400, (y - 30 + 60 * (i / 5) / 4f) / 400, 0f }).ToArray()
    };

    private static Dictionary<string, float> Interactions(Hand left, Hand right) =>
        HolisticGestures.Interactions(new HolisticResult(new Pose { Landmarks = Body() }, left, right, null), 400, 400).ToDictionary(r => r.Name, r => r.Score);

    [Test]
    public void HandsBesideTheFaceAreNoInteraction()
    {
        // The sample's pose: hands up beside the head, palms out, clear of the face's outline (the pose's ears at x 160 and 240).
        var scores = Interactions(HandAt(125, 100), HandAt(275, 100));
        Assert.That(scores.Values, Is.All.LessThan(0.5f), string.Join(", ", scores));
        Assert.That(Interactions(null, null).Values, Is.All.EqualTo(0f), "no hands");
    }

    [Test]
    public void HandsOverTheEyesCoverThem()
    {
        var scores = Interactions(HandAt(180, 100), HandAt(220, 100));
        Assert.That(scores["covering eyes"], Is.GreaterThan(0.5f));
        Assert.That(scores["hand on face"], Is.GreaterThan(0.5f), "covering the eyes is a hand on the face too");
        // One eye only is not covering the eyes.
        Assert.That(Interactions(HandAt(180, 100), null)["covering eyes"], Is.LessThan(0.5f));
        // One wide hand over both eyes is.
        var wide = new Hand { Landmarks = Enumerable.Range(0, 21).Select(i => new[] { (165 + 70 * (i % 5) / 4f) / 400, (85 + 30 * (i / 5) / 4f) / 400, 0f }).ToArray() };
        Assert.That(Interactions(wide, null)["covering eyes"], Is.GreaterThan(0.5f));
    }

    [Test]
    public void AHandOverTheMouthCoversIt()
    {
        var scores = Interactions(null, HandAt(200, 150));
        Assert.That(scores["hand over mouth"], Is.GreaterThan(0.5f));
        Assert.That(scores["covering eyes"], Is.LessThan(0.5f));
        Assert.That(scores["hand on face"], Is.GreaterThan(0.5f));
    }

    [Test]
    public void AHandOnTheCheekIsOnTheFaceOnly()
    {
        var scores = Interactions(HandAt(155, 125), null);
        Assert.That(scores["hand on face"], Is.GreaterThan(0.5f));
        Assert.That(scores["covering eyes"], Is.LessThan(0.5f));
        Assert.That(scores["hand over mouth"], Is.LessThan(0.5f));
    }

    [Test]
    public void APostureAndAnExpressionCombine()
    {
        var body = Body();
        body[16] = new[] { 285f / 400, 60f / 400, 0f, 0.9f, 1f };   // the right wrist above the head: a hand raised
        var smile = new float[FaceTracker.BlendshapeCount];
        smile[FaceTracker.IndexOfBlendshape("mouthSmileLeft")] = smile[FaceTracker.IndexOfBlendshape("mouthSmileRight")] = 0.8f;
        var face = new Face { Landmarks = Enumerable.Range(0, FaceTracker.LandmarkCount).Select(i => new[] { (165 + 70 * (i % 10) / 9f) / 400, (80 + 80 * (i / 10 % 10) / 9f) / 400, 0f }).ToArray(), Blendshapes = smile };
        // The raised hand found (its wrist, landmark 0, at (275, 30), by the pose's wrist); the lowered one not.
        var readings = HolisticGestures.Classify(new HolisticResult(new Pose { Landmarks = body }, null, HandAt(300, 60), face), 400, 400).ToDictionary(r => r.Name, r => r.Score);
        Assert.That(readings.Keys, Does.Contain("hand raised").And.Contain("smiling").And.Contain("hand raised and smiling"));
        Assert.That(readings["hand raised and smiling"], Is.EqualTo(Math.Min(readings["hand raised"], readings["smiling"])));
        Assert.That(readings.Keys.Where(k => k.Contains(" and ")), Is.EqualTo(new[] { "hand raised and smiling" }));
        Assert.That(HolisticGestures.Classify(null, 400, 400), Is.Empty);
    }

    // A wrist goes by its hand: raised with no hand found (out of the picture, or a guess the video mode keeps), or with the other arm's hand, it isn't.
    [Test]
    public void ARaisedWristNeedsItsOwnHand()
    {
        var body = Body();
        body[15] = new[] { 115f / 400, 60f / 400, 0f, 0.9f, 1f };   // both wrists above the head
        body[16] = new[] { 285f / 400, 60f / 400, 0f, 0.9f, 1f };
        string[] Postures(Hand left, Hand right) => HolisticGestures.Classify(new HolisticResult(new Pose { Landmarks = body }, left, right, null), 400, 400)
            .Select(r => r.Name).Where(PoseGestures.Names.Contains).ToArray();
        Assert.That(Postures(HandAt(140, 60), HandAt(300, 60)), Does.Contain("arms raised"));
        Assert.That(Postures(null, HandAt(300, 60)), Does.Contain("hand raised").And.Not.Contain("arms raised"));
        Assert.That(Postures(null, null), Does.Not.Contain("hand raised").And.Not.Contain("arms raised"));
        // Both hands the same hand, by the right wrist: it is the right arm's.
        float[][] only = HolisticGestures.WithHandsFound(new HolisticResult(new Pose { Landmarks = body }, HandAt(302, 61), HandAt(300, 60), null), 400, 400);
        Assert.That((only[15][3], only[16][3]), Is.EqualTo((0f, 0.9f)));
        Assert.That(Postures(HandAt(302, 61), HandAt(300, 60)), Does.Contain("hand raised").And.Not.Contain("arms raised"));
        Assert.That(body[15][3], Is.EqualTo(0.9f), "the result's landmarks are left as they are");
    }

    [Test]
    public void TheClassesAreCombinationsAndInteractionsOnly()
    {
        Assert.That(HolisticGestures.CombinationNames.Length, Is.EqualTo(PoseGestures.Names.Length * FaceExpressions.Names.Length));
        Assert.That(VisualClasses.HolisticClasses, Is.Unique);
        Assert.That(VisualClasses.HolisticClasses, Does.Contain("covering eyes").And.Contain("waving and smiling").And.Contain("hand raised and smiling"));
        foreach (string other in VisualClasses.ObjectClasses.Concat(VisualClasses.Gestures).Concat(VisualClasses.PoseClasses).Concat(VisualClasses.FaceClasses))
            Assert.That(VisualClasses.HolisticClasses, Does.Not.Contain(other), other);
        foreach (string name in VisualClasses.HolisticClasses)
            Assert.That(VisualClasses.CanonicalCombination(name) ?? VisualClasses.CanonicalInteraction(name), Is.EqualTo(name), name);
    }

    [TestCase("hand raised and smiling", "hand raised and smiling")]
    [TestCase("smiling and hand raised", "hand raised and smiling")]
    [TestCase("raise hand + smile", "hand raised and smiling")]
    [TestCase("waving while smiling", "waving and smiling")]
    [TestCase("Wave & smile", "waving and smiling")]
    [TestCase("arms up with mouth open", "arms raised and mouth open")]
    [TestCase("holistic.standing and winking", "standing and winking")]
    [TestCase("smiling", null)]
    [TestCase("waving", null)]
    [TestCase("thumb up and smiling", null)]          // gestures aren't combined
    [TestCase("waving and standing", null)]           // two postures
    [TestCase("", null)]
    [TestCase(null, null)]
    public void ALabelNamesACombination(string label, string expected)
    {
        Assert.That(VisualClasses.CanonicalCombination(label), Is.EqualTo(expected));
    }

    [TestCase("covering eyes", "covering eyes")]
    [TestCase("covering my eyes", "covering eyes")]
    [TestCase("Peekaboo", "covering eyes")]
    [TestCase("peek-a-boo", "covering eyes")]
    [TestCase("🙈", "covering eyes")]
    [TestCase("hand over mouth", "hand over mouth")]
    [TestCase("covering his mouth", "hand over mouth")]
    [TestCase("touching face", "hand on face")]
    [TestCase("hand on her cheek", "hand on face")]
    [TestCase("facepalm", "hand on face")]
    [TestCase("eyes closed", null)]
    [TestCase("face", null)]
    public void ALabelNamesAnInteraction(string label, string expected)
    {
        Assert.That(VisualClasses.CanonicalInteraction(label), Is.EqualTo(expected));
    }

    [TestCase("Take a picture when I cover my eyes", "covering eyes")]
    [TestCase("Play a chime when the baby plays peek-a-boo", "covering eyes")]
    [TestCase("Beep when she puts her hand over her mouth", "hand over mouth")]
    [TestCase("Take a photo when he covers his mouth", "hand over mouth")]
    [TestCase("Say stop when I touch my face", "hand on face")]
    [TestCase("Beep when I rest my chin on my hand", "hand on face")]
    [TestCase("Take a picture when I smile", null)]
    [TestCase("Close the door when I leave", null)]
    public void TheRequestNamesAnInteractionInSoManyWords(string request, string expected)
    {
        Assert.That(VisualClasses.InteractionFromRequest(request), Is.EqualTo(expected));
    }

    [Test]
    public void AMonitorsLabelIsRoutedToTheHolisticClass()
    {
        // The label names it.
        Assert.That(VisualClasses.HolisticForMonitor("smiling and waving", "Beep when I smile and wave"), Is.EqualTo("waving and smiling"));
        Assert.That(VisualClasses.HolisticForMonitor("peekaboo", "Play a chime for peekaboo"), Is.EqualTo("covering eyes"));
        // Half of a combination, with the request naming both: the combination (Gemma gives one class, as for "raises their hand").
        Assert.That(VisualClasses.HolisticForMonitor("hand raised", "Take a picture when someone raises their hand and smiles"), Is.EqualTo("hand raised and smiling"));
        Assert.That(VisualClasses.HolisticForMonitor("smiling", "Take a picture when I raise my hand and smile"), Is.EqualTo("hand raised and smiling"));
        Assert.That(VisualClasses.HolisticForMonitor("person", "Beep when I stand up and smile"), Is.EqualTo("standing and smiling"));
        Assert.That(VisualClasses.HolisticForMonitor("holistic", "Take a picture when I cover my eyes"), Is.EqualTo("covering eyes"));
        Assert.That(VisualClasses.HolisticForMonitor("face", "Beep when I touch my face"), Is.EqualTo("hand on face"));
        // A single posture or expression stays with its own agent; another agent's class stays theirs.
        Assert.That(VisualClasses.HolisticForMonitor("smiling", "Take a picture when I smile"), Is.Null);
        Assert.That(VisualClasses.HolisticForMonitor("waving", "Beep when I wave"), Is.Null);
        Assert.That(VisualClasses.HolisticForMonitor("thumb up", "Take a picture when I smile and give a thumbs up"), Is.Null);
        Assert.That(VisualClasses.HolisticForMonitor("dog", "Beep when the dog covers its eyes"), Is.Null);
        Assert.That(VisualClasses.HolisticForMonitor("person", "Take a picture when you see a person"), Is.Null);
    }

    private sealed class FakeAgent(VisualAgentDescriptor descriptor) : IVisualAgent
    {
        public VisualAgentDescriptor Descriptor => descriptor;
        public Task PrepareAsync(Action<string> progress, CancellationToken token) => Task.CompletedTask;
        public Task<VisualAnalysis> AnalyzeAsync(VisualFrame frame, CancellationToken token) => throw new NotSupportedException();
        public void Dispose() { }
    }

    [Test]
    public void AMonitorForAHolisticClassSelectsTheHolisticAgent()
    {
        var agents = new VisualAgentRegistry();
        agents.Register(new FakeAgent(CocoVisualAgent.Describe()));
        agents.Register(new FakeAgent(HandVisualAgent.Describe()));
        agents.Register(new FakeAgent(PoseVisualAgent.Describe()));
        agents.Register(new FakeAgent(FaceVisualAgent.Describe()));
        agents.Register(new FakeAgent(HolisticVisualAgent.Describe()));
        foreach (string name in VisualClasses.HolisticClasses)
            Assert.That(agents.SelectObjects(name).Descriptor.Id, Is.EqualTo("holistic"), name);
        // The plain postures and expressions stay with the cheaper agents registered before it.
        Assert.That(agents.SelectObjects("smiling").Descriptor.Id, Is.EqualTo("face-expression"));
        Assert.That(agents.SelectObjects("waving").Descriptor.Id, Is.EqualTo("body-pose"));
        Assert.That(agents.Describe().Single(d => d.Id == "holistic").Capabilities, Does.Contain("object_detection").And.Contain("holistic"));
    }

    [Test]
    public void ReadingsBecomeObservationsWithTheBodysBox()
    {
        var observations = HolisticVisualAgent.Observations(
            new HolisticResult(new Pose { Landmarks = Body() }, HandAt(180, 100), HandAt(220, 100), null), 400, 400, null, TimeSpan.Zero);
        var eyes = observations.Single(o => o.Label == "covering eyes");
        Assert.That(eyes.Kind, Is.EqualTo("object"));
        Assert.That(eyes.Confidence, Is.GreaterThan(0.5));
        Assert.That(eyes.Box.Width, Is.GreaterThan(0.3));
        Assert.That(HolisticVisualAgent.Observations(null, 400, 400, new PoseGestures.WaveDetector(), TimeSpan.Zero), Is.Empty);
    }
}

[TestFixture, Category("Native")]
public class UnitTestHolisticVisualAgentNative
{
    // Synthetic people drawn by Z-Image-Turbo (TestData/holistic_*.jpg; HOLISTIC.md) and the demo's sample, whose hands are raised beside the face
    // without touching it: the interactions and combinations each shows at a monitor's default confidence (the plain postures and expressions are the
    // other agents' and left out here). A chin resting on a hand was left out: the hand was found or not depending on JPEG or PNG, and its fingers
    // by the mouth read as a hand over it.
    [TestCase("holistic_person.png")]
    [TestCase("holistic_covering_eyes.jpg", "covering eyes", "hand on face")]
    [TestCase("holistic_hand_over_mouth.jpg", "hand over mouth", "hand on face")]
    [TestCase("holistic_hand_raised_smiling.jpg", "hand raised and smiling")]
    public async Task EachSyntheticPersonShowsWhatTheyDo(string file, params string[] expected)
    {
        string[] models = await UnitTestHolisticTrackerNative.ModelsAsync();
        using var tracker = new HolisticTracker(models);
        using var bgr = Emgu.CV.CvInvoke.Imread(Path.Combine(AppContext.BaseDirectory, file), Emgu.CV.CvEnum.ImreadModes.ColorBgr);
        HolisticResult result = HolisticImageInput.Track(tracker, bgr);
        Assert.That(result, Is.Not.Null, file);
        var observations = HolisticVisualAgent.Observations(result, bgr.Width, bgr.Height, null, TimeSpan.Zero);
        TestContext.Out.WriteLine(file + ": " + string.Join(", ", observations.Select(o => $"{o.Label} {o.Confidence:0.00}")));
        Assert.That(observations.Where(o => o.Confidence >= 0.5 && VisualClasses.HolisticClasses.Contains(o.Label)).Select(o => o.Label),
            Is.EquivalentTo(expected), file);
    }
}
